namespace GenshinChatTranslator.App.Ocr;

public sealed record OcrResult(
    bool IsSuccess,
    string Text,
    double Confidence,
    ChatLanguage DetectedLanguage,
    OcrEngineKind Engine,
    TimeSpan Duration,
    string? ErrorMessage = null)
{
    public static OcrResult Failure(OcrEngineKind engine, TimeSpan duration, string errorMessage)
    {
        return new OcrResult(false, string.Empty, 0, ChatLanguage.Auto, engine, duration, errorMessage);
    }

    public static OcrResult Empty(OcrEngineKind engine, TimeSpan duration)
    {
        return new OcrResult(true, string.Empty, 1, ChatLanguage.Auto, engine, duration);
    }
}
