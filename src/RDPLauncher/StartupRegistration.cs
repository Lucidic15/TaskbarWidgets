using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RDPLauncher
{
    /// <summary>Adds or removes RDPLauncher from the current user's startup apps.</summary>
    internal static class StartupRegistration
    {
        const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        // Where Settings > Apps > Startup and Task Manager record startup apps that were turned off.
        const string StartupApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        const string ValueName = "RDPLauncher";

        static string Command => "\"" + Application.ExecutablePath + "\"";

        /// <summary>
        /// True when Windows will start this copy of RDPLauncher at sign-in: the startup entry points
        /// at this exe and hasn't been turned off in Settings or Task Manager.
        /// </summary>
        public static bool IsEnabled()
        {
            using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKeyPath))
            {
                if (!(run?.GetValue(ValueName) is string command) || !IsThisExe(command))
                    return false;
            }
            using (RegistryKey approved = Registry.CurrentUser.OpenSubKey(StartupApprovedKeyPath))
            {
                // The first byte's lowest bit is set when the entry has been turned off.
                return !(approved?.GetValue(ValueName) is byte[] state && state.Length > 0 && (state[0] & 1) != 0);
            }
        }

        public static void SetEnabled(bool enabled)
        {
            using (RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (enabled)
                    run.SetValue(ValueName, Command);
                else
                    run.DeleteValue(ValueName, false);
            }
            // Clear any "Disabled" left by Settings or Task Manager, which would stop it starting.
            using (RegistryKey approved = Registry.CurrentUser.OpenSubKey(StartupApprovedKeyPath, writable: true))
                approved?.DeleteValue(ValueName, false);
        }

        /// <summary>
        /// When the startup entry points at an RDPLauncher.exe that's since been moved or deleted
        /// (for example an older download), points it at this one instead.
        /// </summary>
        public static void RepairMovedExe()
        {
            try
            {
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
                {
                    if (run?.GetValue(ValueName) is string command && !IsThisExe(command) && !File.Exists(ExePath(command)))
                        run.SetValue(ValueName, Command);
                }
            }
            catch (Exception)
            {
                // Not being able to fix it up shouldn't stop the app.
            }
        }

        static bool IsThisExe(string command)
        {
            try
            {
                return string.Equals(Path.GetFullPath(ExePath(command)), Path.GetFullPath(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // The exe path from a startup command like "C:\path\RDPLauncher.exe" (quoted or not).
        static string ExePath(string command)
        {
            command = command.Trim();
            if (command.StartsWith("\""))
            {
                int closingQuote = command.IndexOf('"', 1);
                return closingQuote > 0 ? command.Substring(1, closingQuote - 1) : command.Trim('"');
            }
            return command;
        }
    }
}
