using System.Diagnostics;

namespace GenshinChatTranslator.App.Ocr.Engines;

internal sealed class StubOcrEngine : IChatOcrEngine
{
    public OcrEngineKind Kind => OcrEngineKind.Stub;

    public IReadOnlySet<ChatLanguage> SupportedLanguages { get; } =
        new HashSet<ChatLanguage>
        {
            ChatLanguage.Auto,
            ChatLanguage.ChineseSimplified,
            ChatLanguage.English,
            ChatLanguage.Japanese,
        };

    public Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        stopwatch.Stop();
        return Task.FromResult(OcrResult.Failure(
            Kind,
            stopwatch.Elapsed,
            "Stub OCR engine is enabled; no real OCR engine has been configured yet."));
    }
}
