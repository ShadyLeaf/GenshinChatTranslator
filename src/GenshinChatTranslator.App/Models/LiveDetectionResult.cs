namespace GenshinChatTranslator.App.Models;

using GenshinChatTranslator.App.Ocr;
using GenshinChatTranslator.App.Translation;

public sealed record LiveDetectionResult(
    WindowInfo Window,
    int FrameWidth,
    int FrameHeight,
    ScreenBox MessageRoi,
    IReadOnlyList<ChatBubbleRoi> Rois,
    IReadOnlyList<ChatOcrItem> OcrResults,
    IReadOnlyList<ChatTranslationItem> TranslationResults,
    DateTime CapturedAt);

public sealed record RoiDetectionLoopSnapshot(
    bool IsRunning,
    LiveDetectionResult? Result,
    string? ErrorMessage,
    bool TargetMissing,
    bool TargetBackground,
    bool ChatInterfaceMissing,
    PipelineLatencyAverages? LatencyAverages,
    DateTime UpdatedAt);

public sealed record PipelineLatencySample(
    double EndToEndMs,
    double? CaptureMs,
    double? ChatGateMs,
    double? RoiMs,
    double? OcrMs,
    double? TranslationMs);

public sealed record PipelineLatencyAverages(
    int Count,
    int Capacity,
    double EndToEndMs,
    double? CaptureMs,
    double? ChatGateMs,
    double? RoiMs,
    double? OcrMs,
    double? TranslationMs);
