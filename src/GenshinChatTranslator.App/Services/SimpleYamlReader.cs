using System.Globalization;
using System.IO;

namespace GenshinChatTranslator.App.Services;

public static class SimpleYamlReader
{
    public static IReadOnlyDictionary<string, object?> ReadMapping(string path)
    {
        var root = new Dictionary<string, object?>(StringComparer.Ordinal);
        var stack = new Stack<(int Indent, Dictionary<string, object?> Mapping)>();
        stack.Push((-1, root));

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = StripComment(rawLine).TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var indent = rawLine.Length - rawLine.TrimStart(' ').Length;
            while (stack.Count > 1 && indent <= stack.Peek().Indent)
            {
                stack.Pop();
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var valueText = line[(separator + 1)..].Trim();
            if (valueText.Length == 0)
            {
                var child = new Dictionary<string, object?>(StringComparer.Ordinal);
                stack.Peek().Mapping[key] = child;
                stack.Push((indent, child));
                continue;
            }

            stack.Peek().Mapping[key] = ParseScalar(valueText);
        }

        return root;
    }

    public static IReadOnlyDictionary<string, object?> Section(
        IReadOnlyDictionary<string, object?> mapping,
        string key)
    {
        if (mapping.TryGetValue(key, out var value) && value is IReadOnlyDictionary<string, object?> section)
        {
            return section;
        }

        throw new InvalidDataException($"Missing YAML section: {key}");
    }

    public static int Int(IReadOnlyDictionary<string, object?> mapping, string key)
    {
        return mapping.TryGetValue(key, out var value) && value is int number
            ? number
            : throw new InvalidDataException($"Missing YAML integer: {key}");
    }

    public static double Double(IReadOnlyDictionary<string, object?> mapping, string key)
    {
        return mapping.TryGetValue(key, out var value)
            ? Convert.ToDouble(value, CultureInfo.InvariantCulture)
            : throw new InvalidDataException($"Missing YAML number: {key}");
    }

    public static bool Bool(IReadOnlyDictionary<string, object?> mapping, string key, bool fallback)
    {
        return mapping.TryGetValue(key, out var value) && value is bool flag ? flag : fallback;
    }

    public static int[] IntArray(IReadOnlyDictionary<string, object?> mapping, string key)
    {
        if (mapping.TryGetValue(key, out var value) && value is IReadOnlyList<object?> values)
        {
            return values.Select(item => Convert.ToInt32(item, CultureInfo.InvariantCulture)).ToArray();
        }

        throw new InvalidDataException($"Missing YAML integer array: {key}");
    }

    private static object? ParseScalar(string valueText)
    {
        if (valueText.StartsWith('[') && valueText.EndsWith(']'))
        {
            var inner = valueText[1..^1].Trim();
            if (inner.Length == 0)
            {
                return Array.Empty<object?>();
            }

            return inner.Split(',', StringSplitOptions.TrimEntries)
                .Select(ParseScalar)
                .ToArray();
        }

        if (bool.TryParse(valueText, out var boolean))
        {
            return boolean;
        }

        if (int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return integer;
        }

        if (double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        return valueText.Trim('"', '\'');
    }

    private static string StripComment(string line)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;
        for (var index = 0; index < line.Length; index++)
        {
            var current = line[index];
            if (current == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
            }
            else if (current == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
            }
            else if (current == '#' && !inSingleQuote && !inDoubleQuote)
            {
                return line[..index];
            }
        }

        return line;
    }
}
