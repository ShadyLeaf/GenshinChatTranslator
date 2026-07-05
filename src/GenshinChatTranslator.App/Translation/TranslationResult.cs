using GenshinChatTranslator.App.Ocr;

namespace GenshinChatTranslator.App.Translation;

public sealed record TranslationResult(
    bool IsSuccess,
    string Text,
    TranslationEngineKind Engine,
    ChatLanguage SourceLanguage,
    ChatLanguage TargetLanguage,
    TimeSpan Duration,
    string? ErrorMessage = null)
{
    public static TranslationResult Failure(
        TranslationEngineKind engine,
        ChatLanguage sourceLanguage,
        ChatLanguage targetLanguage,
        TimeSpan duration,
        string errorMessage)
    {
        return new TranslationResult(false, string.Empty, engine, sourceLanguage, targetLanguage, duration, errorMessage);
    }
}

internal sealed record ContextTranslationResult(
    int Index,
    TranslationResult Result);

public sealed record ChatTranslationItem(
    int Index,
    string SourceText,
    string Speaker,
    bool CacheHit,
    TranslationResult Result);

public sealed record ChatTranslationBatch(
    IReadOnlyList<ChatTranslationItem> Items,
    bool RequestSent)
{
    public static ChatTranslationBatch Empty { get; } = new(Array.Empty<ChatTranslationItem>(), RequestSent: false);
}
