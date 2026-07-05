using GenshinChatTranslator.App.Models;

namespace GenshinChatTranslator.App.Ocr;

public sealed record OcrRequest(
    RgbFrame FullFrame,
    ChatBubbleRoi Roi,
    ChatLanguage LanguageHint,
    OcrPreprocessProfile Profile,
    string FrameHash,
    RgbFrame PreparedImage);
