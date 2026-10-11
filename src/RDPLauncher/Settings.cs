using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RDPLauncher.Rdp;

namespace RDPLauncher
{
    /// <summary>User preferences, stored as key=value lines in %APPDATA%\RDPLauncher\settings.txt.</summary>
    internal sealed class Settings
    {
        static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RDPLauncher", "settings.txt");

        // Display buttons for each arrangement of displays (see LocalDisplay.SetupKey), as the
        // display keys each button uses.
        readonly Dictionary<string, List<List<string>>> displayButtons = new Dictionary<string, List<List<string>>>();

        /// <summary>The computers to connect to, in the order they were added.</summary>
        public List<Connection> Connections { get; } = new List<Connection>();

        /// <summary>Key of the connection the display buttons start.</summary>
        public string CurrentConnectionKey { get; set; }

        /// <summary>Show the widget on the taskbar (the tray icon is always there).</summary>
        public bool ShowOnTaskbar { get; set; } = true;

        /// <summary>Taskbar widget position in pixels from the taskbar's left edge; negative for the default.</summary>
        public int TaskbarOffset { get; set; } = -1;

        public Connection CurrentConnection =>
            Connections.FirstOrDefault(c => c.Key == CurrentConnectionKey) ?? Connections.FirstOrDefault();

        /// <summary>Adds the connection (unless it's already there) and makes it the current one.</summary>
        public Connection AddConnection(Connection connection)
        {
            Connection existing = Connections.FirstOrDefault(c => string.Equals(c.Key, connection.Key, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
                Connections.Add(existing = connection);
            CurrentConnectionKey = existing.Key;
            return existing;
        }

        public void RemoveConnection(Connection connection)
        {
            Connections.RemoveAll(c => c.Key == connection.Key);
            if (CurrentConnectionKey == connection.Key)
                CurrentConnectionKey = Connections.FirstOrDefault()?.Key;
        }

        /// <summary>The saved display buttons for an arrangement of displays, or null if there are none.</summary>
        public List<List<string>> GetDisplayButtons(string setupKey) =>
            displayButtons.TryGetValue(setupKey, out List<List<string>> buttons) ? buttons.Select(b => b.ToList()).ToList() : null;

        /// <summary>Saves the display buttons for an arrangement of displays; null goes back to the defaults.</summary>
        public void SetDisplayButtons(string setupKey, List<List<string>> buttons)
        {
            if (buttons == null)
                displayButtons.Remove(setupKey);
            else
                displayButtons[setupKey] = buttons.Select(b => b.ToList()).ToList();
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
                        case "Connection":
                            if (Connection.FromKey(value) is Connection connection && settings.Connections.All(c => c.Key != connection.Key))
                                settings.Connections.Add(connection);
                            break;
                        case "CurrentConnection":
                            settings.CurrentConnectionKey = value.Length > 0 ? value : null;
                            break;
                        case "ShowOnTaskbar":
                            settings.ShowOnTaskbar = ParseBool(value, settings.ShowOnTaskbar);
                            break;
                        case "TaskbarOffset":
                            if (int.TryParse(value, out int offset))
                                settings.TaskbarOffset = offset;
                            break;
                        case "DisplayButton":
                            // DisplayButton=<setup key>|<display key>;<display key>…, one line per button.
                            int bar = value.IndexOf('|');
                            if (bar <= 0)
                                break;
                            List<string> displays = value.Substring(bar + 1).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                            if (displays.Count == 0)
                                break;
                            string setupKey = value.Substring(0, bar);
                            if (!settings.displayButtons.TryGetValue(setupKey, out List<List<string>> buttons))
                                settings.displayButtons[setupKey] = buttons = new List<List<string>>();
                            buttons.Add(displays);
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
                "# RDPLauncher settings",
                "ShowOnTaskbar=" + ShowOnTaskbar,
                "TaskbarOffset=" + TaskbarOffset,
                "CurrentConnection=" + CurrentConnectionKey,
            };
            foreach (Connection connection in Connections)
                lines.Add("Connection=" + connection.Key);
            foreach (KeyValuePair<string, List<List<string>>> setup in displayButtons)
            {
                foreach (List<string> button in setup.Value)
                    lines.Add("DisplayButton=" + setup.Key + "|" + string.Join(";", button));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllLines(FilePath, lines);
        }

        static bool ParseBool(string value, bool fallback) => bool.TryParse(value, out bool result) ? result : fallback;
    }
}
