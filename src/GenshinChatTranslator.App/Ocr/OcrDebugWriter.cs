using System.Globalization;
using System.IO;
using System.Text.Json;
using GenshinChatTranslator.App.Services;

namespace GenshinChatTranslator.App.Ocr;

internal static class OcrDebugWriter
{
    public static string Write(IReadOnlyList<ChatOcrItem> items)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var outputDir = WorkspacePaths.GetUserDataPath("artifacts", "ocr_debug", timestamp);
        Directory.CreateDirectory(outputDir);

        foreach (var item in items)
        {
            RgbFramePngWriter.Save(item.InputImage, Path.Combine(outputDir, $"roi_{item.Index:00}_input.png"));
            RgbFramePngWriter.Save(item.PreparedImage, Path.Combine(outputDir, $"roi_{item.Index:00}_prepared.png"));
        }

        File.WriteAllText(
            Path.Combine(outputDir, "ocr_results.json"),
            BuildJson(items),
            System.Text.Encoding.UTF8);
        return outputDir;
    }

    private static string BuildJson(IReadOnlyList<ChatOcrItem> items)
    {
        var payload = new
        {
            created_at = DateTime.Now,
            results = items.Select(item => new
            {
                index = item.Index,
                kind = item.Roi.Kind,
                bubble_box = item.Roi.BubbleBox.ToArray(),
                text_box = item.Roi.TextBox.ToArray(),
                image_hash = item.ImageHash,
                cache_hit = item.CacheHit,
                input_size = new { width = item.InputImage.Width, height = item.InputImage.Height },
                prepared_size = new { width = item.PreparedImage.Width, height = item.PreparedImage.Height },
                ocr = new
                {
                    success = item.Result.IsSuccess,
                    text = item.Result.Text,
                    confidence = Math.Round(item.Result.Confidence, 4),
                    detected_language = item.Result.DetectedLanguage.ToString(),
                    engine = item.Result.Engine.ToString(),
                    duration_ms = Math.Round(item.Result.Duration.TotalMilliseconds, 2),
                    error = item.Result.ErrorMessage,
                },
            }),
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
    }
}
