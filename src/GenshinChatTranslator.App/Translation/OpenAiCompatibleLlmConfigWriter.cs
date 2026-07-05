using System.IO;
using System.Text;

namespace GenshinChatTranslator.App.Translation;

public static class OpenAiCompatibleLlmConfigWriter
{
    public static void Save(
        string path,
        string endpoint,
        string model,
        string? apiKey)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8).ToList();
        var sectionIndex = FindSection(lines, "openai_compatible_llm");
        if (sectionIndex < 0)
        {
            throw new InvalidDataException("Missing YAML section: openai_compatible_llm");
        }

        var sectionIndent = CountLeadingSpaces(lines[sectionIndex]);
        var sectionEnd = FindSectionEnd(lines, sectionIndex + 1, sectionIndent);
        UpdateOrInsert(lines, sectionIndex + 1, sectionEnd, sectionIndent + 2, "endpoint", endpoint);
        UpdateOrInsert(lines, sectionIndex + 1, sectionEnd, sectionIndent + 2, "model", model);
        if (apiKey is not null)
        {
            UpdateOrInsert(lines, sectionIndex + 1, sectionEnd, sectionIndent + 2, "api_key", apiKey);
        }

        File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static int FindSection(IReadOnlyList<string> lines, string sectionName)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var trimmed = lines[index].Trim();
            if (trimmed.Equals($"{sectionName}:", StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindSectionEnd(IReadOnlyList<string> lines, int startIndex, int sectionIndent)
    {
        for (var index = startIndex; index < lines.Count; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var indent = CountLeadingSpaces(line);
            if (indent <= sectionIndent)
            {
                return index;
            }
        }

        return lines.Count;
    }

    private static void UpdateOrInsert(
        List<string> lines,
        int startIndex,
        int endIndex,
        int indent,
        string key,
        string value)
    {
        for (var index = startIndex; index < endIndex; index++)
        {
            var trimmed = lines[index].TrimStart();
            if (trimmed.StartsWith($"{key}:", StringComparison.Ordinal))
            {
                lines[index] = FormatLine(CountLeadingSpaces(lines[index]), key, value);
                return;
            }
        }

        lines.Insert(endIndex, FormatLine(indent, key, value));
    }

    private static string FormatLine(int indent, string key, string value)
    {
        return $"{new string(' ', indent)}{key}: \"{EscapeDoubleQuoted(value)}\"";
    }

    private static string EscapeDoubleQuoted(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static int CountLeadingSpaces(string line)
    {
        return line.Length - line.TrimStart(' ').Length;
    }
}
