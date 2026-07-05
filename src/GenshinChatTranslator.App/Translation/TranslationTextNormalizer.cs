using System.Text.RegularExpressions;

namespace GenshinChatTranslator.App.Translation;

internal static partial class TranslationTextNormalizer
{
    public static string Normalize(string text)
    {
        var normalized = text.ReplaceLineEndings("\n").Trim();
        normalized = RepeatedWhitespaceRegex().Replace(normalized, " ");
        return normalized.Trim();
    }

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex RepeatedWhitespaceRegex();
}
