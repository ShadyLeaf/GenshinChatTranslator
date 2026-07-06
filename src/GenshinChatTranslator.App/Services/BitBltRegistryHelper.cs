using Microsoft.Win32;

namespace GenshinChatTranslator.App.Services;

public static class BitBltRegistryHelper
{
    public static void SetDirectXUserGlobalSettings()
    {
        try
        {
            const string keyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
            const string valueName = "DirectXUserGlobalSettings";
            const string valueData = "SwapEffectUpgradeEnable=0;";

            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            key?.SetValue(valueName, valueData, RegistryValueKind.String);
        }
        catch
        {
            // Best effort only. BitBlt capture will still run with the current OS settings.
        }
    }
}
