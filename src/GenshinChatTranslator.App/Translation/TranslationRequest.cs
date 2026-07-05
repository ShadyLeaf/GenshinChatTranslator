using GenshinChatTranslator.App.Ocr;

namespace GenshinChatTranslator.App.Translation;

internal sealed record TranslationRequest(
    int Index,
    string Text,
    string Speaker,
    ChatLanguage SourceLanguage,
    ChatLanguage TargetLanguage,
    string CacheKey);

internal sealed record ContextTranslationMessage(
    int Index,
    string Text,
    string Speaker,
    ChatLanguage SourceLanguage);

internal sealed record ContextTranslationRequest(
    IReadOnlyList<ContextTranslationMessage> Messages,
    ChatLanguage TargetLanguage,
    string CacheKey);
