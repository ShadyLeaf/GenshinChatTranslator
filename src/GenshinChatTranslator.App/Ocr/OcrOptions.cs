using System.IO;
using GenshinChatTranslator.App.Services;

namespace GenshinChatTranslator.App.Ocr;

public sealed record OcrOptions(
    bool Enabled,
    OcrEngineKind SelectedEngine,
    ChatLanguage LanguageHint,
    double MinConfidence,
    int MaxConcurrency,
    bool SkipUnchangedImage,
    bool ContinueOnLowConfidence,
    OcrPreprocessProfile LightBubbleProfile,
    OcrPreprocessProfile DarkBubbleProfile,
    PaddleOcrOptions Paddle,
    WindowsOcrOptions Windows,
    OpenAiVisionOcrOptions OpenAiVision,
    WeChatOcrOptions WeChat)
{
    public static OcrOptions Load(string path)
    {
        var root = SimpleYamlReader.ReadMapping(path);
        var ocr = SimpleYamlReader.Section(root, "ocr");
        var preprocessing = SimpleYamlReader.Section(ocr, "preprocessing");
        var lightBubble = SimpleYamlReader.Section(preprocessing, "light_bubble");
        var darkBubble = SimpleYamlReader.Section(preprocessing, "dark_bubble");
        var engines = SimpleYamlReader.Section(ocr, "engines");

        return new OcrOptions(
            SimpleYamlReader.Bool(ocr, "enabled", true),
            ParseEngineKind(ScalarString(ocr, "selected_engine", "paddle")),
            ParseLanguage(ScalarString(ocr, "language_hint", "auto")),
            SimpleYamlReader.Double(ocr, "min_confidence"),
            Math.Max(1, IntWithFallback(ocr, "max_concurrency", 1)),
            SimpleYamlReader.Bool(ocr, "skip_unchanged_image", true),
            SimpleYamlReader.Bool(ocr, "continue_on_low_confidence", true),
            LoadProfile(lightBubble),
            LoadProfile(darkBubble),
            LoadPaddle(SimpleYamlReader.Section(engines, "paddle")),
            LoadWindows(SimpleYamlReader.Section(engines, "windows")),
            LoadOpenAiVision(SimpleYamlReader.Section(engines, "openai_vision")),
            LoadWeChat(SimpleYamlReader.Section(engines, "wechat")));
    }

    private static OcrPreprocessProfile LoadProfile(IReadOnlyDictionary<string, object?> section)
    {
        return new OcrPreprocessProfile(
            Math.Max(1, SimpleYamlReader.Int(section, "scale")),
            SimpleYamlReader.Bool(section, "grayscale", true),
            SimpleYamlReader.Bool(section, "sharpen", true),
            SimpleYamlReader.Bool(section, "invert", false));
    }

    private static string ScalarString(IReadOnlyDictionary<string, object?> mapping, string key, string fallback)
    {
        return mapping.TryGetValue(key, out var value) && value is not null
            ? value.ToString() ?? fallback
            : fallback;
    }

    private static int IntWithFallback(IReadOnlyDictionary<string, object?> mapping, string key, int fallback)
    {
        return mapping.TryGetValue(key, out var value) && value is int number ? number : fallback;
    }

    private static PaddleOcrOptions LoadPaddle(IReadOnlyDictionary<string, object?> section)
    {
        return new PaddleOcrOptions(
            SimpleYamlReader.Bool(section, "enabled", false),
            SimpleYamlReader.Int(section, "priority"),
            ScalarString(section, "model_path", string.Empty),
            ScalarString(section, "dictionary_path", string.Empty),
            SimpleYamlReader.Int(section, "input_height"),
            SimpleYamlReader.Int(section, "max_width"),
            SimpleYamlReader.Int(section, "cpu_threads"));
    }

    private static WindowsOcrOptions LoadWindows(IReadOnlyDictionary<string, object?> section)
    {
        return new WindowsOcrOptions(
            SimpleYamlReader.Bool(section, "enabled", false),
            SimpleYamlReader.Int(section, "priority"));
    }

    private static OpenAiVisionOcrOptions LoadOpenAiVision(IReadOnlyDictionary<string, object?> section)
    {
        return new OpenAiVisionOcrOptions(
            SimpleYamlReader.Bool(section, "enabled", false),
            SimpleYamlReader.Int(section, "priority"),
            ScalarString(section, "endpoint", "https://api.openai.com/v1/chat/completions"),
            ScalarString(section, "model", "gpt-4o-mini"),
            ScalarString(section, "api_key", string.Empty),
            SimpleYamlReader.Int(section, "timeout_ms"));
    }

    private static WeChatOcrOptions LoadWeChat(IReadOnlyDictionary<string, object?> section)
    {
        return new WeChatOcrOptions(
            SimpleYamlReader.Bool(section, "enabled", false),
            SimpleYamlReader.Int(section, "priority"),
            ScalarString(section, "executable_path", string.Empty),
            ScalarString(section, "arguments_template", "\"{image}\""),
            SimpleYamlReader.Int(section, "timeout_ms"),
            Math.Max(1, IntWithFallback(section, "session_count", 1)));
    }

    private static ChatLanguage ParseLanguage(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "zh" or "zh-cn" or "zh-hans" or "chinese" or "chinesesimplified" => ChatLanguage.ChineseSimplified,
            "en" or "english" => ChatLanguage.English,
            "ja" or "jp" or "japanese" => ChatLanguage.Japanese,
            "auto" or "" => ChatLanguage.Auto,
            _ => throw new InvalidDataException($"Unsupported OCR language hint: {value}"),
        };
    }

    private static OcrEngineKind ParseEngineKind(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "stub" => OcrEngineKind.Stub,
            "paddle" or "paddleocr" => OcrEngineKind.Paddle,
            "windows" or "windowsocr" => OcrEngineKind.Windows,
            "openai" or "openai_vision" or "openaivision" => OcrEngineKind.OpenAiVision,
            "wechat" or "wechatocr" => OcrEngineKind.WeChat,
            _ => throw new InvalidDataException($"Unsupported OCR engine: {value}"),
        };
    }
}

public sealed record PaddleOcrOptions(
    bool Enabled,
    int Priority,
    string ModelPath,
    string DictionaryPath,
    int InputHeight,
    int MaxWidth,
    int CpuThreads);

public sealed record WindowsOcrOptions(bool Enabled, int Priority);

public sealed record OpenAiVisionOcrOptions(
    bool Enabled,
    int Priority,
    string Endpoint,
    string Model,
    string ApiKey,
    int TimeoutMs);

public sealed record WeChatOcrOptions(
    bool Enabled,
    int Priority,
    string ExecutablePath,
    string ArgumentsTemplate,
    int TimeoutMs,
    int SessionCount);
