using GenshinChatTranslator.App.Ocr;

namespace GenshinChatTranslator.App.Translation;

internal static class TranslationLanguageMapper
{
    public static string DisplayName(ChatLanguage language)
    {
        return language switch
        {
            ChatLanguage.ChineseSimplified => "中文",
            ChatLanguage.English => "英语",
            ChatLanguage.Japanese => "日语",
            _ => "自动",
        };
    }

    public static string Microsoft(ChatLanguage language)
    {
        return language switch
        {
            ChatLanguage.ChineseSimplified => "zh-Hans",
            ChatLanguage.English => "en",
            ChatLanguage.Japanese => "ja",
            _ => "auto",
        };
    }

    public static string OpenAiPromptName(ChatLanguage language)
    {
        return language switch
        {
            ChatLanguage.ChineseSimplified => "Simplified Chinese",
            ChatLanguage.English => "English",
            ChatLanguage.Japanese => "Japanese",
            _ => "the target language",
        };
    }
}
