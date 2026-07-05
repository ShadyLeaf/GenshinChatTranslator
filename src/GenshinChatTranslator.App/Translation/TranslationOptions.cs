using System.Globalization;
using System.IO;
using GenshinChatTranslator.App.Ocr;
using GenshinChatTranslator.App.Services;

namespace GenshinChatTranslator.App.Translation;

public sealed record TranslationOptions(
    bool Enabled,
    TranslationEngineKind SelectedEngine,
    ChatLanguage TargetLanguage,
    int TimeoutMs,
    bool CacheEnabled,
    bool SkipSameLanguage,
    MicrosoftEdgeTranslationOptions MicrosoftEdge,
    OpenAiCompatibleLlmTranslationOptions OpenAiCompatibleLlm)
{
    public static TranslationOptions Load(string path)
    {
        var root = SimpleYamlReader.ReadMapping(path);
        var translation = SimpleYamlReader.Section(root, "translation");
        var engines = SimpleYamlReader.Section(translation, "engines");

        return new TranslationOptions(
            SimpleYamlReader.Bool(translation, "enabled", true),
            ParseEngineKind(ScalarString(translation, "selected_engine", "microsoft_edge")),
            ParseLanguage(ScalarString(translation, "target_language", "ChineseSimplified")),
            SimpleYamlReader.Int(translation, "timeout_ms"),
            SimpleYamlReader.Bool(translation, "cache_enabled", true),
            SimpleYamlReader.Bool(translation, "skip_same_language", true),
            LoadMicrosoftEdge(SimpleYamlReader.Section(engines, "microsoft_edge")),
            LoadOpenAiCompatibleLlm(SimpleYamlReader.Section(engines, "openai_compatible_llm")));
    }

    private static MicrosoftEdgeTranslationOptions LoadMicrosoftEdge(IReadOnlyDictionary<string, object?> section)
    {
        return new MicrosoftEdgeTranslationOptions(
            SimpleYamlReader.Bool(section, "enabled", false),
            SimpleYamlReader.Int(section, "priority"),
            ScalarString(section, "auth_endpoint", "https://edge.microsoft.com/translate/auth"),
            ScalarString(section, "endpoint", "https://api-edge.cognitive.microsofttranslator.com/translate"));
    }

    private static OpenAiCompatibleLlmTranslationOptions LoadOpenAiCompatibleLlm(IReadOnlyDictionary<string, object?> section)
    {
        return new OpenAiCompatibleLlmTranslationOptions(
            SimpleYamlReader.Bool(section, "enabled", false),
            SimpleYamlReader.Int(section, "priority"),
            ScalarString(section, "endpoint", "https://api.openai.com/v1/chat/completions"),
            ScalarString(section, "model", "gpt-4o-mini"),
            ScalarString(section, "api_key", string.Empty),
            SimpleYamlReader.Double(section, "temperature"),
            ParseThinkingType(ScalarString(section, "thinking_type", "disabled")));
    }

    private static string ScalarString(IReadOnlyDictionary<string, object?> mapping, string key, string fallback)
    {
        return mapping.TryGetValue(key, out var value) && value is not null
            ? value.ToString() ?? fallback
            : fallback;
    }

    private static ChatLanguage ParseLanguage(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "zh" or "zh-cn" or "zh-hans" or "chinese" or "chinesesimplified" => ChatLanguage.ChineseSimplified,
            "en" or "english" => ChatLanguage.English,
            "ja" or "jp" or "japanese" => ChatLanguage.Japanese,
            _ => throw new InvalidDataException($"Unsupported translation target language: {value}"),
        };
    }

    private static TranslationEngineKind ParseEngineKind(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "none" or "off" or "disabled" => TranslationEngineKind.None,
            "microsoft_edge" or "microsoft" or "edge" => TranslationEngineKind.MicrosoftEdge,
            "openai_compatible_llm" or "openai" or "llm" => TranslationEngineKind.OpenAiCompatibleLlm,
            _ => throw new InvalidDataException($"Unsupported translation engine: {value}"),
        };
    }

    private static string ParseThinkingType(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "enabled" => "enabled",
            "disabled" or "" => "disabled",
            _ => throw new InvalidDataException($"Unsupported LLM thinking type: {value}"),
        };
    }
}

public sealed record MicrosoftEdgeTranslationOptions(
    bool Enabled,
    int Priority,
    string AuthEndpoint,
    string Endpoint);

public sealed record OpenAiCompatibleLlmTranslationOptions(
    bool Enabled,
    int Priority,
    string Endpoint,
    string Model,
    string ApiKey,
    double Temperature,
    string ThinkingType);
