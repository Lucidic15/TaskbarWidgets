using System.Windows.Forms;
using Microsoft.Win32;

namespace AudioFlipper
{
    /// <summary>Adds or removes AudioFlipper from the current user's startup programs.</summary>
    internal static class StartupRegistration
    {
        const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "AudioFlipper";

        public static bool IsEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                return key?.GetValue(ValueName) is string;
        }

        public static void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (enabled)
                    key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"");
                else
                    key.DeleteValue(ValueName, false);
            }
        }
    }
}
