using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GenshinChatTranslator.App.Models;

namespace GenshinChatTranslator.App.Services;

public static class RgbFramePngWriter
{
    public static void Save(RgbFrame frame, string path)
    {
        File.WriteAllBytes(path, Encode(frame));
    }

    public static byte[] Encode(RgbFrame frame)
    {
        var stride = frame.Width * 4;
        var bgra = new byte[frame.Height * stride];
        for (var source = 0; source < frame.Pixels.Length; source += 3)
        {
            var target = (source / 3) * 4;
            bgra[target] = frame.Pixels[source + 2];
            bgra[target + 1] = frame.Pixels[source + 1];
            bgra[target + 2] = frame.Pixels[source];
            bgra[target + 3] = 255;
        }

        var bitmap = BitmapSource.Create(
            frame.Width,
            frame.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            bgra,
            stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
