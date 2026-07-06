using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using GenshinChatTranslator.App.Ocr;
using GenshinChatTranslator.App.Translation;

namespace GenshinChatTranslator.App.Services;

public sealed class UserPreferences
{
    public string? UiCultureName { get; set; }

    public string? OcrEngine { get; set; }

    public string? TranslationEngine { get; set; }

    public string? TranslationTargetLanguage { get; set; }

    public bool AutoFixWin11BitBlt { get; set; } = true;

    public static UserPreferences Empty { get; } = new();

    public CultureInfo? GetUiCulture()
    {
        return TryGetCulture(UiCultureName);
    }

    public OcrEngineKind? GetOcrEngine()
    {
        return TryParseEnum<OcrEngineKind>(OcrEngine);
    }

    public TranslationEngineKind? GetTranslationEngine()
    {
        return TryParseEnum<TranslationEngineKind>(TranslationEngine);
    }

    public ChatLanguage? GetTranslationTargetLanguage()
    {
        var language = TryParseEnum<ChatLanguage>(TranslationTargetLanguage);
        return language == ChatLanguage.Auto ? null : language;
    }

    private static CultureInfo? TryGetCulture(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
        {
            return null;
        }

        try
        {
            return CultureInfo.GetCultureInfo(cultureName);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private static TEnum? TryParseEnum<TEnum>(string? value)
        where TEnum : struct
    {
        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : null;
    }
}

public static class UserPreferencesStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public static UserPreferences Load()
    {
        var path = GetPath();
        if (!File.Exists(path))
        {
            return new UserPreferences();
        }

        try
        {
            return JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(path, Encoding.UTF8), SerializerOptions)
                ?? new UserPreferences();
        }
        catch
        {
            return new UserPreferences();
        }
    }

    public static void Save(UserPreferences preferences)
    {
        var path = GetPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(preferences, SerializerOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string GetPath()
    {
        return Path.Combine(WorkspacePaths.GetUserConfigDirectory(), "preferences.json");
    }
}
