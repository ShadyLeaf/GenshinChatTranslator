using System.Runtime.InteropServices;
using System.Text;

namespace GenshinChatTranslator.App.Win32;

internal static class NativeMethods
{
    public const int GwlExStyle = -20;
    public const int WsExLayered = 0x00080000;
    public const int WsExTransparent = 0x00000020;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExNoActivate = 0x08000000;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpShowWindow = 0x0040;
    public const int SwHide = 0;
    public const int SwShowMinimized = 2;
    public const int SwShownoactivate = 4;
    public const int Srccopy = 0x00CC0020;
    public const uint DibRgbColors = 0;
    public const uint BiRgb = 0;

    public static readonly IntPtr HwndTopMost = new(-1);

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLengthW(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    public static extern bool ClientToScreen(IntPtr hwnd, ref Point point);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr hwndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr newLong);

    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(
        IntPtr destinationHdc,
        int x,
        int y,
        int width,
        int height,
        IntPtr sourceHdc,
        int sourceX,
        int sourceY,
        int rasterOperation);

    [DllImport("gdi32.dll")]
    public static extern int GetDIBits(
        IntPtr hdc,
        IntPtr bitmap,
        uint start,
        uint lines,
        [Out] byte[] bits,
        ref BitmapInfo bitmapInfo,
        uint usage);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);

    public static void SetDpiAwareness()
    {
        try
        {
            SetProcessDpiAwarenessContext(new IntPtr(-4));
        }
        catch
        {
            try
            {
                SetProcessDPIAware();
            }
            catch
            {
                // Best effort only. The app can still run without explicit DPI setup.
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;

        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCmd;
        public Point MinPosition;
        public Point MaxPosition;
        public Rect NormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Color0;
        public uint Color1;
        public uint Color2;
    }
}
