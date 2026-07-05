using System.Globalization;
using System.Threading;
using System.Windows;

namespace GenshinChatTranslator.App.Localization;

public static class LocalizationManager
{
    private const string FallbackCultureName = "en-US";

    public static CultureInfo CurrentCulture { get; private set; } = CultureInfo.CurrentUICulture;

    public static void LoadCurrentCultureResources()
    {
        LoadCultureResources(CultureInfo.CurrentUICulture);
    }

    public static void LoadCultureResources(CultureInfo culture)
    {
        var cultureNames = GetCultureFallbacks(culture).Distinct(StringComparer.OrdinalIgnoreCase);
        ResourceDictionary? dictionary = null;
        var loadedCultureName = FallbackCultureName;
        foreach (var cultureName in cultureNames)
        {
            dictionary = TryCreateDictionary(cultureName);
            if (dictionary is not null)
            {
                loadedCultureName = cultureName;
                break;
            }
        }

        if (dictionary is null)
        {
            return;
        }

        var loadedCulture = CultureInfo.GetCultureInfo(loadedCultureName);
        CurrentCulture = loadedCulture;
        CultureInfo.CurrentCulture = loadedCulture;
        CultureInfo.CurrentUICulture = loadedCulture;
        CultureInfo.DefaultThreadCurrentCulture = loadedCulture;
        CultureInfo.DefaultThreadCurrentUICulture = loadedCulture;
        Thread.CurrentThread.CurrentCulture = loadedCulture;
        Thread.CurrentThread.CurrentUICulture = loadedCulture;

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        for (var index = dictionaries.Count - 1; index >= 0; index--)
        {
            var source = dictionaries[index].Source?.OriginalString;
            if (source is not null && source.StartsWith("Resources/Strings.", StringComparison.OrdinalIgnoreCase))
            {
                dictionaries.RemoveAt(index);
            }
        }

        dictionaries.Add(dictionary);
    }

    public static string Text(string key)
    {
        return Application.Current.TryFindResource(key) as string ?? key;
    }

    public static string Format(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, Text(key), args);
    }

    private static ResourceDictionary? TryCreateDictionary(string cultureName)
    {
        try
        {
            return new ResourceDictionary
            {
                Source = new Uri($"Resources/Strings.{cultureName}.xaml", UriKind.Relative),
            };
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> GetCultureFallbacks(CultureInfo culture)
    {
        yield return culture.Name;
        yield return culture.TwoLetterISOLanguageName;

        var regionalFallback = culture.TwoLetterISOLanguageName switch
        {
            "en" => "en-US",
            "ja" => "ja-JP",
            "zh" => "zh-CN",
            _ => FallbackCultureName,
        };
        yield return regionalFallback;
        yield return FallbackCultureName;
    }
}
