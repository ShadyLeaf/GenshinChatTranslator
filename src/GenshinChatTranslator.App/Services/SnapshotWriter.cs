using System.IO;
using System.Globalization;
using System.Text.Json;
using GenshinChatTranslator.App.Models;
using GenshinChatTranslator.App.Ocr;
using GenshinChatTranslator.App.Translation;

namespace GenshinChatTranslator.App.Services;

public static class SnapshotWriter
{
    public static string Write(
        WindowInfo window,
        RgbFrame frame,
        ScreenBox messageRoi,
        IReadOnlyList<ChatBubbleRoi> rois,
        IReadOnlyList<ChatOcrItem> ocrResults,
        IReadOnlyList<ChatTranslationItem> translationResults)
    {
        var outputDir = WorkspacePaths.GetUserDataPath("artifacts", "wpf_snapshot");
        var sampleDir = WorkspacePaths.GetUserDataPath("pics_example");
        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(sampleDir);

        var screenshotPath = Path.Combine(outputDir, "window_capture.png");
        var annotatedPath = Path.Combine(outputDir, "window_capture_rois.png");
        var jsonPath = Path.Combine(outputDir, "window_rois.json");
        var samplePath = CreateSamplePath(sampleDir);

        var ocrDebugPath = ocrResults.Count > 0 ? OcrDebugWriter.Write(ocrResults) : null;

        RgbFramePngWriter.Save(frame, screenshotPath);
        RgbFramePngWriter.Save(frame, samplePath);
        RgbFramePngWriter.Save(DrawAnnotations(frame, messageRoi, rois), annotatedPath);
        File.WriteAllText(jsonPath, BuildJson(window, frame, messageRoi, rois, ocrResults, translationResults, ocrDebugPath), System.Text.Encoding.UTF8);

        return outputDir;
    }

    private static string CreateSamplePath(string sampleDir)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var path = Path.Combine(sampleDir, $"genshin_wpf_sample_{timestamp}.png");
        for (var suffix = 1; File.Exists(path); suffix++)
        {
            path = Path.Combine(sampleDir, $"genshin_wpf_sample_{timestamp}_{suffix}.png");
        }

        return path;
    }

    private static string BuildJson(
        WindowInfo window,
        RgbFrame frame,
        ScreenBox messageRoi,
        IReadOnlyList<ChatBubbleRoi> rois,
        IReadOnlyList<ChatOcrItem> ocrResults,
        IReadOnlyList<ChatTranslationItem> translationResults,
        string? ocrDebugPath)
    {
        var payload = new
        {
            window = new
            {
                hwnd = window.Hwnd.ToInt64(),
                title = window.Title,
                client_box = window.ClientBox.ToArray(),
            },
            image_size = new
            {
                width = frame.Width,
                height = frame.Height,
            },
            message_roi = messageRoi.ToArray(),
            rois = rois.Select((roi, index) => new
            {
                index = index + 1,
                kind = roi.Kind,
                bubble_box = roi.BubbleBox.ToArray(),
                text_box = roi.TextBox.ToArray(),
                confidence = Math.Round(roi.Confidence, 4),
            }),
            ocr = new
            {
                debug_path = ocrDebugPath,
                results = ocrResults.Select(item => new
                {
                    index = item.Index,
                    kind = item.Roi.Kind,
                    image_hash = item.ImageHash,
                    cache_hit = item.CacheHit,
                    success = item.Result.IsSuccess,
                    text = item.Result.Text,
                    confidence = Math.Round(item.Result.Confidence, 4),
                    detected_language = item.Result.DetectedLanguage.ToString(),
                    engine = item.Result.Engine.ToString(),
                    duration_ms = Math.Round(item.Result.Duration.TotalMilliseconds, 2),
                    error = item.Result.ErrorMessage,
                }),
            },
            translation = new
            {
                results = translationResults.Select(item => new
                {
                    index = item.Index,
                    speaker = item.Speaker,
                    source_text = item.SourceText,
                    cache_hit = item.CacheHit,
                    success = item.Result.IsSuccess,
                    text = item.Result.Text,
                    engine = item.Result.Engine.ToString(),
                    source_language = item.Result.SourceLanguage.ToString(),
                    target_language = item.Result.TargetLanguage.ToString(),
                    duration_ms = Math.Round(item.Result.Duration.TotalMilliseconds, 2),
                    error = item.Result.ErrorMessage,
                }),
            },
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
    }

    private static RgbFrame DrawAnnotations(
        RgbFrame frame,
        ScreenBox messageRoi,
        IReadOnlyList<ChatBubbleRoi> rois)
    {
        var pixels = new byte[frame.Pixels.Length];
        Buffer.BlockCopy(frame.Pixels, 0, pixels, 0, frame.Pixels.Length);
        var annotated = new RgbFrame(frame.Width, frame.Height, pixels);

        DrawBox(annotated, messageRoi, 80, 180, 255, 3);
        foreach (var roi in rois)
        {
            if (roi.Kind == ChatRoiDetector.SelfLightKind)
            {
                DrawBox(annotated, roi.BubbleBox, 255, 80, 80, 4);
            }
            else
            {
                DrawBox(annotated, roi.BubbleBox, 80, 255, 120, 4);
            }

            DrawBox(annotated, roi.TextBox, 255, 255, 255, 2);
        }

        return annotated;
    }

    private static void DrawBox(RgbFrame frame, ScreenBox box, byte red, byte green, byte blue, int thickness)
    {
        var left = Math.Clamp(box.Left, 0, frame.Width - 1);
        var right = Math.Clamp(box.Right, 0, frame.Width - 1);
        var top = Math.Clamp(box.Top, 0, frame.Height - 1);
        var bottom = Math.Clamp(box.Bottom, 0, frame.Height - 1);

        for (var t = 0; t < thickness; t++)
        {
            for (var x = left; x < right; x++)
            {
                SetPixel(frame, x, Math.Clamp(top + t, 0, frame.Height - 1), red, green, blue);
                SetPixel(frame, x, Math.Clamp(bottom - t, 0, frame.Height - 1), red, green, blue);
            }

            for (var y = top; y < bottom; y++)
            {
                SetPixel(frame, Math.Clamp(left + t, 0, frame.Width - 1), y, red, green, blue);
                SetPixel(frame, Math.Clamp(right - t, 0, frame.Width - 1), y, red, green, blue);
            }
        }
    }

    private static void SetPixel(RgbFrame frame, int x, int y, byte red, byte green, byte blue)
    {
        var offset = frame.PixelOffset(x, y);
        frame.Pixels[offset] = red;
        frame.Pixels[offset + 1] = green;
        frame.Pixels[offset + 2] = blue;
    }

}
