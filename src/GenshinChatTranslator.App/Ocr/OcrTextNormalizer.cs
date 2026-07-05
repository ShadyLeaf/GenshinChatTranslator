using System.Text.RegularExpressions;

namespace GenshinChatTranslator.App.Ocr;

internal static partial class OcrTextNormalizer
{
    public static string Normalize(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        return WhitespaceRegex().Replace(trimmed, " ");
    }

    public static ChatLanguage GuessLanguage(string text)
    {
        if (text.Any(character => character is >= '\u3040' and <= '\u30ff'))
        {
            return ChatLanguage.Japanese;
        }

        if (text.Any(character => character is >= '\u4e00' and <= '\u9fff'))
        {
            return ChatLanguage.ChineseSimplified;
        }

        var latinCount = text.Count(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
        return latinCount > 0 ? ChatLanguage.English : ChatLanguage.Auto;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
