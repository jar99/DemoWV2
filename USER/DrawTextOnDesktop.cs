using System.Runtime.InteropServices;
using System.Drawing;

public class DrawRectangle
{
    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateSolidBrush(int crColor);

    [DllImport("gdi32.dll")]
    static extern bool Rectangle(IntPtr hDC, int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

    [DllImport("gdi32.dll")]
    static extern int SetBkMode(IntPtr hDC, int iBkMode);

    [DllImport("gdi32.dll")]
    static extern int SetTextColor(IntPtr hDC, int crColor);

    [DllImport("gdi32.dll")]
    static extern bool TextOut(IntPtr hDC, int x, int y, string lpString, int c);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc,
        int nYSrc, TernaryRasterOperations dwRop);

    public enum TernaryRasterOperations : uint
    {
        SRCCOPY = 0x00CC0020,
        SRCPAINT = 0x00EE0086,
        SRCAND = 0x008800C6,
        SRCINVERT = 0x00660046,
        SRCERASE = 0x00440328,
        DSTINVERT = 0x00330008,
        BLACKNESS = 0x00000042,
        WHITENESS = 0x00FF0062,
    }


    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("user32.dll")]
    static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public IntPtr hdc;
        [MarshalAs(UnmanagedType.Bool)] public bool fErase;
        public RECT rcPaint;
        public bool fRestore;
        public bool fIncUpdate;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }


    [DllImport("user32.dll")]
    static extern IntPtr GetDesktopWindow();

    public static IntPtr GetDesktopWindowHandle()
    {
        return GetDesktopWindow();
    }


    public static void Draw(IntPtr hWnd)
    {
        PAINTSTRUCT ps;
        IntPtr hdc = BeginPaint(hWnd, out ps);

        // Draw a rectangle
        IntPtr hBrush = CreateSolidBrush(0x000000FF); // Blue
        IntPtr hOldBrush = SelectObject(hdc, hBrush);
        Rectangle(hdc, 100, 100, 300, 200);
        SelectObject(hdc, hOldBrush);
        DeleteObject(hBrush);


        // Write text
        SetBkMode(hdc, 1); // Transparent background
        SetTextColor(hdc, 0x00FFFFFF); // White text
        TextOut(hdc, 120, 150, "Hello World", "Hello World".Length);

        // Load and draw image
        try
        {
            Bitmap bmp = (Bitmap)Image.FromFile("image.png"); // Replace with your image path

            IntPtr hBitmap = bmp.GetHbitmap();
            IntPtr hdcMem = CreateCompatibleDC(hdc);
            SelectObject(hdcMem, hBitmap);
            BitBlt(hdc, 400, 100, bmp.Width, bmp.Height, hdcMem, 0, 0, TernaryRasterOperations.SRCCOPY);

            DeleteDC(hdcMem);
            DeleteObject(hBitmap);
            bmp.Dispose();
        }
        catch (Exception ex)
        {
            // Handle the exception if the image file is not found or cannot be loaded.
            Console.WriteLine($"Error loading image: {ex.Message}");
        }


        EndPaint(hWnd, ref ps);
    }

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern bool DeleteDC(IntPtr hdc);
}