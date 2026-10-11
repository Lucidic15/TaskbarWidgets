using System;
using Microsoft.Win32;

namespace RDPLauncher
{
    /// <summary>Reads the light/dark mode choices from Settings > Personalization > Colors.</summary>
    internal static class Theme
    {
        const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        /// <summary>"Choose your default Windows mode" — controls the taskbar.</summary>
        public static bool TaskbarUsesLightTheme() => ReadFlag("SystemUsesLightTheme", false);

        /// <summary>"Choose your default app mode".</summary>
        public static bool AppsUseLightTheme() => ReadFlag("AppsUseLightTheme", true);

        static bool ReadFlag(string valueName, bool fallback)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath))
                    return key?.GetValue(valueName) is int value ? value != 0 : fallback;
            }
            catch (Exception)
            {
                return fallback;
            }
        }
    }
}
