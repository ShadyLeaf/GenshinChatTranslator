namespace GenshinChatTranslator.App.Ocr;

internal interface IChatOcrEngine
{
    OcrEngineKind Kind { get; }

    IReadOnlySet<ChatLanguage> SupportedLanguages { get; }

    Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken);
}
