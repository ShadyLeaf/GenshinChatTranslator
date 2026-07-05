using GenshinChatTranslator.App.Ocr;

namespace GenshinChatTranslator.App.Translation;

internal interface ITranslationEngine
{
    TranslationEngineKind Kind { get; }

    bool Supports(ChatLanguage sourceLanguage, ChatLanguage targetLanguage);
}

internal interface ISingleMessageTranslationEngine : ITranslationEngine
{
    Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken);
}

internal interface IContextTranslationEngine : ITranslationEngine
{
    Task<IReadOnlyList<ContextTranslationResult>> TranslateContextAsync(
        ContextTranslationRequest request,
        CancellationToken cancellationToken);
}
