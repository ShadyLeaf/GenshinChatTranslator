using System.Diagnostics;
using GenshinChatTranslator.App.Services;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace GenshinChatTranslator.App.Ocr.Engines;

internal sealed class WindowsOcrEngine : IChatOcrEngine
{
    public WindowsOcrEngine(WindowsOcrOptions options)
    {
        _ = options;
    }

    public OcrEngineKind Kind => OcrEngineKind.Windows;

    public IReadOnlySet<ChatLanguage> SupportedLanguages { get; } =
        new HashSet<ChatLanguage>
        {
            ChatLanguage.Auto,
            ChatLanguage.ChineseSimplified,
            ChatLanguage.English,
            ChatLanguage.Japanese,
        };

    public async Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var engine = CreateEngine(request.LanguageHint);
            if (engine is null)
            {
                stopwatch.Stop();
                return OcrResult.Failure(Kind, stopwatch.Elapsed, BuildLanguageError(request.LanguageHint));
            }

            var imageBytes = RgbFramePngWriter.Encode(request.PreparedImage);
            cancellationToken.ThrowIfCancellationRequested();

            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(imageBytes);
                await writer.StoreAsync().AsTask(cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
                writer.DetachStream();
            }

            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            cancellationToken.ThrowIfCancellationRequested();

            var result = await engine.RecognizeAsync(bitmap);
            var text = string.Join(
                Environment.NewLine,
                result.Lines.Select(line => line.Text).Where(line => !string.IsNullOrWhiteSpace(line)));

            stopwatch.Stop();
            if (string.IsNullOrWhiteSpace(text))
            {
                return OcrResult.Empty(Kind, stopwatch.Elapsed);
            }

            return new OcrResult(
                IsSuccess: true,
                Text: text,
                Confidence: 0.75,
                DetectedLanguage: FromLanguageTag(engine.RecognizerLanguage.LanguageTag),
                Engine: Kind,
                Duration: stopwatch.Elapsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            return OcrResult.Failure(Kind, stopwatch.Elapsed, ex.Message);
        }
    }

    private static OcrEngine? CreateEngine(ChatLanguage languageHint)
    {
        var preferredTags = languageHint switch
        {
            ChatLanguage.ChineseSimplified => new[] { "zh-Hans", "zh-CN" },
            ChatLanguage.English => new[] { "en-US", "en" },
            ChatLanguage.Japanese => new[] { "ja-JP", "ja" },
            _ => new[] { "zh-Hans", "zh-CN", "en-US", "en", "ja-JP", "ja" },
        };

        var available = OcrEngine.AvailableRecognizerLanguages.ToList();
        foreach (var tag in preferredTags)
        {
            var language = available.FirstOrDefault(item =>
                item.LanguageTag.Equals(tag, StringComparison.OrdinalIgnoreCase) ||
                item.LanguageTag.StartsWith($"{tag}-", StringComparison.OrdinalIgnoreCase));
            if (language is not null)
            {
                return OcrEngine.TryCreateFromLanguage(language);
            }
        }

        return null;
    }

    private static string BuildLanguageError(ChatLanguage languageHint)
    {
        var available = OcrEngine.AvailableRecognizerLanguages.Select(language => language.LanguageTag).ToArray();
        var requested = languageHint == ChatLanguage.Auto ? "zh-Hans/en/ja" : languageHint.ToString();
        var installed = available.Length == 0 ? "(none)" : string.Join(", ", available);
        return $"Windows OCR language pack is unavailable. requested={requested}; installed={installed}";
    }

    private static ChatLanguage FromLanguageTag(string tag)
    {
        if (tag.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
        {
            return ChatLanguage.Japanese;
        }

        if (tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return ChatLanguage.ChineseSimplified;
        }

        if (tag.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return ChatLanguage.English;
        }

        return ChatLanguage.Auto;
    }
}
