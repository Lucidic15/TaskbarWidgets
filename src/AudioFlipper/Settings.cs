using System;
using System.Collections.Generic;
using System.IO;

namespace AudioFlipper
{
    /// <summary>User preferences, stored as key=value lines in %APPDATA%\AudioFlipper\settings.txt.</summary>
    internal sealed class Settings
    {
        static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioFlipper", "settings.txt");

        readonly HashSet<string> excludedDeviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Also make the new device the default communications device (used by Teams, Discord, etc.).</summary>
        public bool SwitchCommunications { get; set; } = true;

        /// <summary>Show the popup when the output device changes.</summary>
        public bool ShowPopup { get; set; } = true;

        public bool IsExcluded(string deviceId) => excludedDeviceIds.Contains(deviceId);

        public void SetExcluded(string deviceId, bool excluded)
        {
            if (excluded)
                excludedDeviceIds.Add(deviceId);
            else
                excludedDeviceIds.Remove(deviceId);
        }

        public static Settings Load()
        {
            var settings = new Settings();
            try
            {
                if (!File.Exists(FilePath))
                    return settings;

                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int separator = line.IndexOf('=');
                    if (line.StartsWith("#") || separator <= 0)
                        continue;

                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();
                    switch (key)
                    {
                        case "SwitchCommunications":
                            settings.SwitchCommunications = ParseBool(value, settings.SwitchCommunications);
                            break;
                        case "ShowPopup":
                            settings.ShowPopup = ParseBool(value, settings.ShowPopup);
                            break;
                        case "Exclude":
                            settings.excludedDeviceIds.Add(value);
                            break;
                    }
                }
            }
            catch (Exception)
            {
                // Unreadable settings shouldn't stop the app; fall back to defaults.
            }
            return settings;
        }

        public void Save()
        {
            var lines = new List<string>
            {
                "# AudioFlipper settings",
                "SwitchCommunications=" + SwitchCommunications,
                "ShowPopup=" + ShowPopup,
            };
            foreach (string id in excludedDeviceIds)
                lines.Add("Exclude=" + id);

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllLines(FilePath, lines);
        }

        static bool ParseBool(string value, bool fallback) => bool.TryParse(value, out bool result) ? result : fallback;
    }
}
