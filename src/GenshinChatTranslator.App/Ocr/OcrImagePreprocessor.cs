using System.Security.Cryptography;
using GenshinChatTranslator.App.Models;
using GenshinChatTranslator.App.Services;

namespace GenshinChatTranslator.App.Ocr;

internal sealed class OcrImagePreprocessor
{
    private readonly OcrOptions _options;

    public OcrImagePreprocessor(OcrOptions options)
    {
        _options = options;
    }

    public OcrPreparedImage Prepare(RgbFrame frame, ChatBubbleRoi roi)
    {
        var profile = roi.Kind == ChatRoiDetector.SelfLightKind
            ? _options.LightBubbleProfile
            : _options.DarkBubbleProfile;
        var input = Crop(frame, roi.TextBox);
        var scaled = ResizeNearest(input, profile.Scale);
        var processed = ApplyFilters(scaled, profile);
        var hash = ComputeHash(processed);
        return new OcrPreparedImage(input, processed, profile, hash);
    }

    private static RgbFrame Crop(RgbFrame frame, ScreenBox box)
    {
        var left = Math.Clamp(box.Left, 0, frame.Width - 1);
        var top = Math.Clamp(box.Top, 0, frame.Height - 1);
        var right = Math.Clamp(box.Right, left + 1, frame.Width);
        var bottom = Math.Clamp(box.Bottom, top + 1, frame.Height);
        var width = right - left;
        var height = bottom - top;
        var pixels = new byte[width * height * 3];

        for (var y = 0; y < height; y++)
        {
            var sourceOffset = frame.PixelOffset(left, top + y);
            var targetOffset = y * width * 3;
            Buffer.BlockCopy(frame.Pixels, sourceOffset, pixels, targetOffset, width * 3);
        }

        return new RgbFrame(width, height, pixels);
    }

    private static RgbFrame ResizeNearest(RgbFrame frame, int scale)
    {
        if (scale <= 1)
        {
            var copiedPixels = new byte[frame.Pixels.Length];
            Buffer.BlockCopy(frame.Pixels, 0, copiedPixels, 0, frame.Pixels.Length);
            return new RgbFrame(frame.Width, frame.Height, copiedPixels);
        }

        var width = frame.Width * scale;
        var height = frame.Height * scale;
        var pixels = new byte[width * height * 3];

        for (var y = 0; y < height; y++)
        {
            var sourceY = y / scale;
            for (var x = 0; x < width; x++)
            {
                var sourceX = x / scale;
                var sourceOffset = frame.PixelOffset(sourceX, sourceY);
                var targetOffset = ((y * width) + x) * 3;
                pixels[targetOffset] = frame.Pixels[sourceOffset];
                pixels[targetOffset + 1] = frame.Pixels[sourceOffset + 1];
                pixels[targetOffset + 2] = frame.Pixels[sourceOffset + 2];
            }
        }

        return new RgbFrame(width, height, pixels);
    }

    private static RgbFrame ApplyFilters(RgbFrame frame, OcrPreprocessProfile profile)
    {
        var pixels = new byte[frame.Pixels.Length];
        for (var offset = 0; offset < frame.Pixels.Length; offset += 3)
        {
            var red = frame.Pixels[offset];
            var green = frame.Pixels[offset + 1];
            var blue = frame.Pixels[offset + 2];
            var value = profile.Grayscale
                ? (byte)Math.Clamp((int)Math.Round(red * 0.299 + green * 0.587 + blue * 0.114), 0, 255)
                : red;

            if (profile.Invert)
            {
                value = (byte)(255 - value);
            }

            pixels[offset] = value;
            pixels[offset + 1] = profile.Grayscale ? value : (byte)(profile.Invert ? 255 - green : green);
            pixels[offset + 2] = profile.Grayscale ? value : (byte)(profile.Invert ? 255 - blue : blue);
        }

        var filtered = new RgbFrame(frame.Width, frame.Height, pixels);
        return profile.Sharpen ? Sharpen(filtered) : filtered;
    }

    private static RgbFrame Sharpen(RgbFrame frame)
    {
        if (frame.Width < 3 || frame.Height < 3)
        {
            return frame;
        }

        var pixels = new byte[frame.Pixels.Length];
        Buffer.BlockCopy(frame.Pixels, 0, pixels, 0, frame.Pixels.Length);

        for (var y = 1; y < frame.Height - 1; y++)
        {
            for (var x = 1; x < frame.Width - 1; x++)
            {
                for (var channel = 0; channel < 3; channel++)
                {
                    var center = frame.Pixels[frame.PixelOffset(x, y) + channel] * 5;
                    var left = frame.Pixels[frame.PixelOffset(x - 1, y) + channel];
                    var right = frame.Pixels[frame.PixelOffset(x + 1, y) + channel];
                    var top = frame.Pixels[frame.PixelOffset(x, y - 1) + channel];
                    var bottom = frame.Pixels[frame.PixelOffset(x, y + 1) + channel];
                    pixels[frame.PixelOffset(x, y) + channel] = (byte)Math.Clamp(center - left - right - top - bottom, 0, 255);
                }
            }
        }

        return new RgbFrame(frame.Width, frame.Height, pixels);
    }

    private static string ComputeHash(RgbFrame frame)
    {
        using var sha256 = SHA256.Create();
        sha256.TransformBlock(BitConverter.GetBytes(frame.Width), 0, sizeof(int), null, 0);
        sha256.TransformBlock(BitConverter.GetBytes(frame.Height), 0, sizeof(int), null, 0);
        sha256.TransformFinalBlock(frame.Pixels, 0, frame.Pixels.Length);
        return Convert.ToHexString(sha256.Hash!).ToLowerInvariant();
    }
}

internal sealed record OcrPreparedImage(
    RgbFrame InputImage,
    RgbFrame PreparedImage,
    OcrPreprocessProfile Profile,
    string ImageHash);
