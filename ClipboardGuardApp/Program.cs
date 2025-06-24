using System.Runtime.InteropServices;
using System.Diagnostics;

namespace ClipboardGuardApp
{
    public class MainForm : Form
    {
        private TextBox contentTextBox;
        private Label infoLabel;
        private Label sourceInfoLabel; // To display the paste source
        private string internalClipboard = ""; // To hold text copied from this app
        private string lastPastedText = ""; // To prevent duplicate pasting

        // --- P/Invoke declarations for Windows API functions ---

        [DllImport("user32.dll")]
        static extern IntPtr GetClipboardOwner();

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);


        public MainForm()
        {
            Console.WriteLine("Application starting. Initializing main form...");
            // --- Form Initialization ---
            this.Text = "Clipboard Guard";
            this.Size = new Size(450, 400);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(240, 240, 240);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimumSize = new Size(300, 200);

            // --- Control Initialization ---
            // The order of creation and adding is important for correct docking.

            // --- Text Box (Fill) ---
            contentTextBox = new TextBox();
            contentTextBox.Multiline = true;
            contentTextBox.Dock = DockStyle.Fill;
            contentTextBox.ScrollBars = ScrollBars.Vertical;
            contentTextBox.Font = new Font("Consolas", 11F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(0)));
            contentTextBox.BackColor = Color.White;
            contentTextBox.ForeColor = Color.FromArgb(30, 30, 30);
            contentTextBox.BorderStyle = BorderStyle.FixedSingle;
            
            // --- Info Label (Top) ---
            infoLabel = new Label();
            infoLabel.Text = "Paste content into this window. The clipboard will be cleared when you switch to another window.";
            infoLabel.Dock = DockStyle.Top;
            infoLabel.TextAlign = ContentAlignment.MiddleCenter;
            infoLabel.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(0)));
            infoLabel.Padding = new Padding(10);
            infoLabel.Height = 60;

            // --- Source Info Label (Bottom) ---
            sourceInfoLabel = new Label();
            sourceInfoLabel.Text = "Paste source will be shown here.";
            sourceInfoLabel.Dock = DockStyle.Bottom;
            sourceInfoLabel.TextAlign = ContentAlignment.MiddleCenter;
            sourceInfoLabel.Font = new Font("Segoe UI", 9F, FontStyle.Italic, GraphicsUnit.Point, ((byte)(0)));
            sourceInfoLabel.Padding = new Padding(5);
            sourceInfoLabel.Height = 30;

            // --- Add Controls to Form ---
            // Add the Fill control first to ensure it occupies the available space correctly.
            this.Controls.Add(contentTextBox);
            this.Controls.Add(infoLabel);
            this.Controls.Add(sourceInfoLabel);

            // --- Event Handlers ---
            this.Activated += MainForm_Activated;
            this.Deactivate += MainForm_Deactivated;
            contentTextBox.KeyDown += ContentTextBox_KeyDown;
            Console.WriteLine("Main form initialized successfully.");
        }

        private void ContentTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.C)
            {
                if (!string.IsNullOrEmpty(contentTextBox.SelectedText))
                {
                    Console.WriteLine("Copy action (Ctrl+C) detected inside the app.");
                    internalClipboard = contentTextBox.SelectedText;
                }
            }
            else if (e.Control && e.KeyCode == Keys.X)
            {
                if (!string.IsNullOrEmpty(contentTextBox.SelectedText))
                {
                    Console.WriteLine("Cut action (Ctrl+X) detected inside the app.");
                    internalClipboard = contentTextBox.SelectedText;
                }
            }
            else if (e.Control && e.KeyCode == Keys.V)
            {
                Console.WriteLine("Manual Paste action (Ctrl+V) detected. Checking clipboard owner.");
                // Since this is a manual paste, we should update our last pasted text tracker.
                if (Clipboard.ContainsText())
                {
                    lastPastedText = Clipboard.GetText();
                }
                string ownerProcessName = GetClipboardOwnerProcessName();
                sourceInfoLabel.Text = $"Pasted from: {ownerProcessName}";
                Console.WriteLine($"Paste source identified as: {ownerProcessName}");
            }
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

            uint processId;
            GetWindowThreadProcessId(ownerHwnd, out processId);

            if (processId == 0)
            {
                Console.WriteLine("Could not determine process ID from window handle.");
                return "Unknown";
            }

            try
            {
                Process p = Process.GetProcessById((int)processId);
                Console.WriteLine($"Successfully found process: {p.ProcessName} with ID: {processId}");
                return p.ProcessName + ".exe";
            }
            catch (ArgumentException)
            {
                Console.WriteLine($"Process with ID {processId} might have already closed.");
                return "Unknown (process closed)";
            }
        }

        private void MainForm_Activated(object sender, EventArgs e)
        {
            Console.WriteLine("Main window activated.");
            this.BackColor = Color.FromArgb(240, 240, 240);
            infoLabel.Text = "Window is Active. You can paste content here.";
            
            if (!string.IsNullOrEmpty(internalClipboard) && !Clipboard.ContainsText())
            {
                Console.WriteLine("Restoring internal clipboard content to system clipboard.");
                Clipboard.SetText(internalClipboard);
            }

            // Ensure the textbox has focus and then paste content automatically if it's new.
            this.BeginInvoke((MethodInvoker)delegate {
                contentTextBox.Focus();
                Console.WriteLine("Focus set to the main text box.");

                // Automatically paste content if there is new text on the clipboard
                if (Clipboard.ContainsText())
                {
                    string currentClipboardText = Clipboard.GetText();
                    if (currentClipboardText != lastPastedText)
                    {
                        Console.WriteLine("New clipboard content detected. Automatically appending text.");
                        // Append text to avoid overwriting and add a newline for clarity.
                        contentTextBox.AppendText(currentClipboardText + Environment.NewLine);
                        lastPastedText = currentClipboardText; // Update tracker
                        
                        // After pasting, detect the source
                        string ownerProcessName = GetClipboardOwnerProcessName();
                        sourceInfoLabel.Text = $"Pasted from: {ownerProcessName}";
                        Console.WriteLine($"Paste source identified as: {ownerProcessName}");
                    }
                    else
                    {
                        Console.WriteLine("Clipboard content is the same as last paste. Skipping auto-paste.");
                    }
                }
            });
        }

        private void MainForm_Deactivated(object sender, EventArgs e)
        {
            Console.WriteLine("Main window deactivated.");
            this.BackColor = Color.LightGray;
            infoLabel.Text = "Window is Inactive. Clipboard has been cleared.";
            sourceInfoLabel.Text = ""; 

            try
            {
                if (Clipboard.ContainsText())
                {
                    if (Clipboard.GetText() != internalClipboard)
                    {
                        Console.WriteLine("External content detected on clipboard. Clearing internal clipboard reference.");
                        internalClipboard = "";
                    }
                }
                
                Console.WriteLine("Clearing system clipboard...");
                Clipboard.Clear();
                lastPastedText = ""; // Reset tracker to allow re-pasting of same content later.
                Console.WriteLine("System clipboard cleared successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Could not clear clipboard. Reason: {ex.Message}");
            }
        }

        [STAThread]
        public static void Main()
        {
            Console.WriteLine("=====================================");
            Console.WriteLine("Application Main() entry point called.");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            Console.WriteLine("Application closing.");
            Console.WriteLine("=====================================\n");
        }
    }
}
