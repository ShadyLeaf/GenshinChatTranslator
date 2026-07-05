using GenshinChatTranslator.App.Models;

namespace GenshinChatTranslator.App.Ocr;

public sealed record ChatOcrItem(
    int Index,
    ChatBubbleRoi Roi,
    RgbFrame InputImage,
    RgbFrame PreparedImage,
    string ImageHash,
    OcrResult Result,
    bool CacheHit);
