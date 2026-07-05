using System.Windows;
using GenshinChatTranslator.App.Localization;
using GenshinChatTranslator.App.Services;
using GenshinChatTranslator.App.Win32;

namespace GenshinChatTranslator.App;

public partial class App : Application
{
    public App()
    {
        NativeMethods.SetDpiAwareness();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var preferredCulture = UserPreferencesStore.Load().GetUiCulture();
        if (preferredCulture is null)
        {
            LocalizationManager.LoadCurrentCultureResources();
        }
        else
        {
            LocalizationManager.LoadCultureResources(preferredCulture);
        }

        new MainWindow().Show();
    }
}
