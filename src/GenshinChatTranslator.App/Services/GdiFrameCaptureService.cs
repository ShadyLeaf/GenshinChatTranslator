using System.Runtime.InteropServices;
using GenshinChatTranslator.App.Models;
using GenshinChatTranslator.App.Win32;

namespace GenshinChatTranslator.App.Services;

public sealed class GdiFrameCaptureService
{
    public RgbFrame Capture(WindowInfo window)
    {
        return Capture(
            sourceWindow: window.Hwnd,
            releaseWindow: window.Hwnd,
            width: window.ClientBox.Width,
            height: window.ClientBox.Height,
            sourceX: 0,
            sourceY: 0);
    }

    public RgbFrame Capture(ScreenBox box)
    {
        return Capture(
            sourceWindow: IntPtr.Zero,
            releaseWindow: IntPtr.Zero,
            width: box.Width,
            height: box.Height,
            sourceX: box.Left,
            sourceY: box.Top);
    }

    private static RgbFrame Capture(
        IntPtr sourceWindow,
        IntPtr releaseWindow,
        int width,
        int height,
        int sourceX,
        int sourceY)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Capture size must be positive.");
        }

        var sourceDc = NativeMethods.GetDC(sourceWindow);
        if (sourceDc == IntPtr.Zero)
        {
            throw new InvalidOperationException("GetDC failed.");
        }

        var memoryDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var previousObject = IntPtr.Zero;

        try
        {
            memoryDc = NativeMethods.CreateCompatibleDC(sourceDc);
            bitmap = NativeMethods.CreateCompatibleBitmap(sourceDc, width, height);
            previousObject = NativeMethods.SelectObject(memoryDc, bitmap);

            if (!NativeMethods.BitBlt(
                    memoryDc,
                    0,
                    0,
                    width,
                    height,
                    sourceDc,
                    sourceX,
                    sourceY,
                    NativeMethods.Srccopy))
            {
                throw new InvalidOperationException("BitBlt failed.");
            }

            var bitmapInfo = new NativeMethods.BitmapInfo
            {
                Header = new NativeMethods.BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = NativeMethods.BiRgb,
                    SizeImage = (uint)(width * height * 4),
                },
            };

            var bgra = new byte[width * height * 4];
            var lines = NativeMethods.GetDIBits(
                memoryDc,
                bitmap,
                0,
                (uint)height,
                bgra,
                ref bitmapInfo,
                NativeMethods.DibRgbColors);
            if (lines != height)
            {
                throw new InvalidOperationException($"GetDIBits failed: {lines}/{height} lines.");
            }

            var rgb = new byte[width * height * 3];
            for (var source = 0; source < bgra.Length; source += 4)
            {
                var target = (source / 4) * 3;
                rgb[target] = bgra[source + 2];
                rgb[target + 1] = bgra[source + 1];
                rgb[target + 2] = bgra[source];
            }

            return new RgbFrame(width, height, rgb);
        }
        finally
        {
            if (previousObject != IntPtr.Zero && memoryDc != IntPtr.Zero)
            {
                NativeMethods.SelectObject(memoryDc, previousObject);
            }

            if (bitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(bitmap);
            }

            if (memoryDc != IntPtr.Zero)
            {
                NativeMethods.DeleteDC(memoryDc);
            }

            NativeMethods.ReleaseDC(releaseWindow, sourceDc);
        }
    }
}
