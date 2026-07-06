using System.Text;
using System.Runtime.InteropServices;
using GenshinChatTranslator.App.Models;
using GenshinChatTranslator.App.Win32;

namespace GenshinChatTranslator.App.Services;

public sealed class WindowTracker
{
    public WindowInfo? FindTargetWindow(IEnumerable<string> titleKeywords)
    {
        var keywords = titleKeywords
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .Select(keyword => keyword.ToUpperInvariant())
            .ToArray();
        var currentProcessId = Environment.ProcessId;
        WindowInfo? result = null;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (result is not null || !NativeMethods.IsWindowVisible(hwnd) || IsMinimized(hwnd))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
            if (processId == currentProcessId)
            {
                return true;
            }

            var title = GetWindowTitle(hwnd);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            var upperTitle = title.ToUpperInvariant();
            if (!keywords.Any(upperTitle.Contains))
            {
                return true;
            }

            var clientBox = GetClientBox(hwnd);
            if (clientBox is null || clientBox.Value.Width <= 0 || clientBox.Value.Height <= 0)
            {
                return true;
            }

            result = new WindowInfo(hwnd, title, clientBox.Value);
            return false;
        }, IntPtr.Zero);

        return result;
    }

    public IntPtr GetForegroundWindow()
    {
        return NativeMethods.GetForegroundWindow();
    }

    public bool IsForegroundWindow(WindowInfo window)
    {
        return GetForegroundWindow() == window.Hwnd;
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var length = NativeMethods.GetWindowTextLengthW(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        NativeMethods.GetWindowTextW(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static ScreenBox? GetClientBox(IntPtr hwnd)
    {
        if (!NativeMethods.GetClientRect(hwnd, out var rect))
        {
            return null;
        }

        var point = new NativeMethods.Point(0, 0);
        if (!NativeMethods.ClientToScreen(hwnd, ref point))
        {
            return null;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        return new ScreenBox(point.X, point.Y, point.X + width, point.Y + height);
    }

    private static bool IsMinimized(IntPtr hwnd)
    {
        if (NativeMethods.IsIconic(hwnd))
        {
            return true;
        }

        var placement = new NativeMethods.WindowPlacement
        {
            Length = Marshal.SizeOf<NativeMethods.WindowPlacement>(),
        };
        return NativeMethods.GetWindowPlacement(hwnd, ref placement) &&
            placement.ShowCmd == NativeMethods.SwShowMinimized;
    }
}
