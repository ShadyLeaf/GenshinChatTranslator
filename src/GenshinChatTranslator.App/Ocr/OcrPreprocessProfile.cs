namespace GenshinChatTranslator.App.Ocr;

public sealed record OcrPreprocessProfile(
    int Scale,
    bool Grayscale,
    bool Sharpen,
    bool Invert);
