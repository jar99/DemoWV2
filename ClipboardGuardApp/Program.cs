using System.Runtime.InteropServices;
using System.Diagnostics;

namespace ClipboardGuardApp
{
    public class MainForm : Form
    {
        private readonly TextBox _contentTextBox;
        private readonly Label _infoLabel;
        private readonly Label _sourceInfoLabel; // To display the paste source
        private string _internalClipboard = ""; // To hold text copied from this app
        private string _lastPastedText = ""; // To prevent duplicate pasting

        private readonly bool _clearClipboard;

        // --- P/Invoke declarations for Windows API functions ---

        [DllImport("user32.dll")]
        static extern IntPtr GetClipboardOwner();

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern int GetWindowTextLength(IntPtr hWnd);

        private const int MaxClassNameLength = 256;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        private string GetWindowTitle(IntPtr ownerHwnd)
        {
            Console.WriteLine(
                $"Attempting to find window title for HWND: {ownerHwnd} (0x{ownerHwnd:X})");

            int length = GetWindowTextLength(ownerHwnd);
            if (length > 0)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder(length + 1);
                if (GetWindowText(ownerHwnd, sb, sb.Capacity) > 0)
                {
                    string windowTitle = sb.ToString();
                    Console.WriteLine(
                        $"Found window title '{windowTitle}' for HWND: {ownerHwnd} (0x{ownerHwnd:X})");
                    return windowTitle;
                }

                Console.WriteLine(
                    $"Could not retrieve window text for HWND: {ownerHwnd} (0x{ownerHwnd:X}). GetWindowText returned 0. Error: {Marshal.GetLastWin32Error()}");
            }
            else
            {
                Console.WriteLine(
                    $"Window text length is 0 for HWND: {ownerHwnd} (0x{ownerHwnd:X}).");
            }

            Console.WriteLine(
                $"No window title found for HWND: {ownerHwnd} (0x{ownerHwnd:X})");
            return "Unknown Window";
        }

        private string GetWindowClass(IntPtr ownerHwnd)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder(MaxClassNameLength);

            int actualLength = GetClassName(ownerHwnd, sb, sb.Capacity);

            if (actualLength > 0)
            {
                string windowClass = sb.ToString();
                Console.WriteLine(
                    $"Found window class '{windowClass}' (Actual Length: {actualLength}) for HWND: {ownerHwnd} (0x{ownerHwnd:X})");
                return windowClass;
            }

            Console.WriteLine(
                $"Could not retrieve window class for HWND: {ownerHwnd} (0x{ownerHwnd:X}). GetClassName returned {actualLength}. Error: {Marshal.GetLastWin32Error()}");
            return "Unknown Class";
        }

        private string GetClipboardOwnerProcessName()
        {
            Console.WriteLine("Attempting to get clipboard owner process name...");
            IntPtr ownerHwnd = GetClipboardOwner();
            if (ownerHwnd == IntPtr.Zero)
            {
                Console.WriteLine("No clipboard owner window handle found.");
                return "a non-window application";
            }

            string windowTitle = GetWindowTitle(ownerHwnd);
            string windowClass = GetWindowClass(ownerHwnd);

            GetWindowThreadProcessId(ownerHwnd, out var processId);

            if (processId == 0)
            {
                Console.WriteLine("Could not determine process ID from window handle.");
                return $"Unknown process (Window: '{windowTitle}', Class: '{windowClass}')";
            }

            try
            {
                Process p = Process.GetProcessById((int)processId);
                Console.WriteLine(
                    $"Successfully found process: {p.ProcessName} (ID: {processId}, Main Window: '{p.MainWindowTitle}')");
                return $"{p.ProcessName}.exe (Window: '{windowTitle}, Class: '{windowClass}')";
            }
            catch (ArgumentException)
            {
                Console.WriteLine($"Process with ID {processId} might have already closed.");
                return "Unknown (process closed)";
            }
        }

        private string CheckClipboardContent()
        {
            if (Clipboard.ContainsFileDropList())
            {
                return "File Drop List";
            }

            if (Clipboard.ContainsImage())
            {
                return "Image";
            }

            if (Clipboard.ContainsAudio())
            {
                return "Audio";
            }

            if (Clipboard.ContainsText())
            {
                // If it contains text, check for more specific text formats for better detail.
                IDataObject? data = Clipboard.GetDataObject();
                if (data == null)
                    return "Plain Text"; // Should not be null if Clipboard.ContainsText() is true, but for safety.
                if (data.GetDataPresent(DataFormats.Rtf))
                {
                    return "RTF Text";
                }

                if (data.GetDataPresent(DataFormats.Html))
                {
                    return "HTML Text";
                }

                if (data.GetDataPresent(DataFormats.CommaSeparatedValue))
                {
                    return "CSV Text";
                }

                if (data.GetDataPresent(DataFormats.UnicodeText))
                {
                    return "Unicode Text"; // Most common plain text format
                }

                // Fallback for other text types not explicitly handled but detected by ContainsText()
                return "Plain Text";
            }

            // If none of the common types above were found, check if any data is present at all
            IDataObject? genericData = Clipboard.GetDataObject();
            if (genericData == null) return "None"; // No content detected
            string[] formats = genericData.GetFormats();
            if (formats.Length > 0)
            {
                // Return the first format name found if it's an "OTHER" type
                return $"OTHER ({formats[0]})";
            }

            return "None"; // No content detected
        }

        private MainForm(bool clearClipboard = false)
        {
            Console.WriteLine("Application starting. Initializing main form...");
            _clearClipboard = clearClipboard;

            // --- Form Initialization ---
            base.Text = "Clipboard Guard";
            Size = new Size(450, 400);
            StartPosition = FormStartPosition.CenterScreen;
            base.BackColor = Color.FromArgb(240, 240, 240);
            FormBorderStyle = FormBorderStyle.Sizable;
            base.MinimumSize = new Size(300, 200);

            // --- Control Initialization ---
            // The order of creation and adding is important for correct docking.

            // --- Text Box (Fill) ---
            _contentTextBox = new TextBox();
            _contentTextBox.Multiline = true;
            _contentTextBox.Dock = DockStyle.Fill;
            _contentTextBox.ScrollBars = ScrollBars.Vertical;
            _contentTextBox.Font = new Font("Consolas", 11F, FontStyle.Regular, GraphicsUnit.Point, 0);
            _contentTextBox.BackColor = Color.White;
            _contentTextBox.ForeColor = Color.FromArgb(30, 30, 30);
            _contentTextBox.BorderStyle = BorderStyle.FixedSingle;

            // --- Info Label (Top) ---
            _infoLabel = new Label();
            _infoLabel.Text =
                "Paste content into this window. The clipboard will be cleared when you switch to another window.";
            _infoLabel.Dock = DockStyle.Top;
            _infoLabel.TextAlign = ContentAlignment.MiddleCenter;
            _infoLabel.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            _infoLabel.Padding = new Padding(10);
            _infoLabel.Height = 60;

            // --- Source Info Label (Bottom) ---
            _sourceInfoLabel = new Label();
            _sourceInfoLabel.Text = "Paste source will be shown here.";
            _sourceInfoLabel.Dock = DockStyle.Bottom;
            _sourceInfoLabel.TextAlign = ContentAlignment.MiddleCenter;
            _sourceInfoLabel.Font = new Font("Segoe UI", 9F, FontStyle.Italic, GraphicsUnit.Point, 0);
            _sourceInfoLabel.Padding = new Padding(5);
            _sourceInfoLabel.Height = 30;

            // --- Add Controls to Form ---
            // Add the Fill control first to ensure it occupies the available space correctly.
            Controls.Add(_contentTextBox);
            Controls.Add(_infoLabel);
            Controls.Add(_sourceInfoLabel);

            // --- Event Handlers ---
            Activated += MainForm_Activated;
            Deactivate += MainForm_Deactivated;
            _contentTextBox.KeyDown += ContentTextBox_KeyDown;
            Console.WriteLine("Main form initialized successfully.");
        }

        private void ContentTextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.Control)
            {
                case true when e.KeyCode == Keys.C:
                {
                    if (!string.IsNullOrEmpty(_contentTextBox.SelectedText))
                    {
                        Console.WriteLine("Copy action (Ctrl+C) detected inside the app.");
                        _internalClipboard = _contentTextBox.SelectedText;
                    }

                    break;
                }
                case true when e.KeyCode == Keys.X:
                {
                    if (!string.IsNullOrEmpty(_contentTextBox.SelectedText))
                    {
                        Console.WriteLine("Cut action (Ctrl+X) detected inside the app.");
                        _internalClipboard = _contentTextBox.SelectedText;
                    }

                    break;
                }
                case true when e.KeyCode == Keys.V:
                {
                    Console.WriteLine("Manual Paste action (Ctrl+V) detected. Checking clipboard owner.");

                    string clipboardContentType = CheckClipboardContent();
                    Console.WriteLine($"Clipboard contains: {clipboardContentType}");

                    // Since this is a manual paste, we should update our last pasted text tracker.
                    if (Clipboard.ContainsText())
                    {
                        _lastPastedText = Clipboard.GetText();
                    }

                    string ownerProcessName = GetClipboardOwnerProcessName();
                    _sourceInfoLabel.Text = $"Pasted {clipboardContentType} from: {ownerProcessName}";
                    Console.WriteLine($"Paste {clipboardContentType} source identified as: {ownerProcessName}");
                    break;
                }
            }
        }

        private void MainForm_Activated(object? sender, EventArgs e)
        {
            Console.WriteLine("Main window activated.");
            base.BackColor = Color.FromArgb(240, 240, 240);
            _infoLabel.Text = "Window is Active. You can paste content here.";

            if (!string.IsNullOrEmpty(_internalClipboard) && !Clipboard.ContainsText())
            {
                Console.WriteLine("Restoring internal clipboard content to system clipboard.");
                Clipboard.SetText(_internalClipboard);
            }

            // Ensure the textbox has focus and then paste content automatically if it's new.
            BeginInvoke((MethodInvoker)delegate
            {
                _contentTextBox.Focus();
                Console.WriteLine("Focus set to the main text box.");

                string clipboardContentType = CheckClipboardContent();
                Console.WriteLine($"Clipboard contains: {clipboardContentType}");

                // Automatically paste content if there is new text on the clipboard
                if (!Clipboard.ContainsText()) return;

                string currentClipboardText = Clipboard.GetText();
                if (currentClipboardText != _lastPastedText)
                {
                    Console.WriteLine("New clipboard content detected. Automatically appending text.");
                    // Append text to avoid overwriting and add a newline for clarity.
                    _contentTextBox.AppendText(currentClipboardText + Environment.NewLine);
                    _lastPastedText = currentClipboardText; // Update tracker

                    // After pasting, detect the source
                    string ownerProcessName = GetClipboardOwnerProcessName();
                    _sourceInfoLabel.Text = $"Pasted from: {ownerProcessName}";
                    Console.WriteLine($"Paste source identified as: {ownerProcessName}");
                }
                else
                {
                    Console.WriteLine("Clipboard content is the same as last paste. Skipping auto-paste.");
                }
            });
        }

        private void MainForm_Deactivated(object? sender, EventArgs e)
        {
            Console.WriteLine("Main window deactivated.");
            base.BackColor = Color.LightGray;
            _infoLabel.Text = "Window is Inactive. Clipboard has been cleared.";

            try
            {
                if (Clipboard.ContainsText())
                {
                    if (Clipboard.GetText() != _internalClipboard)
                    {
                        Console.WriteLine(
                            "External content detected on clipboard. Clearing internal clipboard reference.");
                        _internalClipboard = "";
                    }
                }

                if (_clearClipboard)
                {
                    Console.WriteLine("Clearing system clipboard...");
                    Clipboard.Clear();
                    _lastPastedText = ""; // Reset tracker to allow re-pasting of same content later.
                    Console.WriteLine("System clipboard cleared successfully.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Could not clear clipboard. Reason: {ex.Message}");
            }
        }

        [STAThread]
        public static void Main(string[]? args)
        {
            bool clearClipboard =
                args?.Any(arg => arg.Equals("--clear-clipboard", StringComparison.OrdinalIgnoreCase)) ?? false;

            Console.WriteLine("=====================================");
            Console.WriteLine("Application Main() entry point called.");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(clearClipboard: clearClipboard));
            Console.WriteLine("Application closing.");
            Console.WriteLine("=====================================\n");
        }
    }
}