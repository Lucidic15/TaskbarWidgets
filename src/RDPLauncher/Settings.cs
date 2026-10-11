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

        // Remote Desktop's ID for each display, for each arrangement of outputs (see LocalDisplay.OutputsKey).
        readonly Dictionary<string, Dictionary<string, int>> displayIds = new Dictionary<string, Dictionary<string, int>>();

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

        /// <summary>Remote Desktop's display IDs remembered for an arrangement of outputs, or null.</summary>
        public Dictionary<string, int> GetDisplayIds(string outputsKey) =>
            displayIds.TryGetValue(outputsKey, out Dictionary<string, int> ids) ? new Dictionary<string, int>(ids) : null;

        /// <summary>Remembers Remote Desktop's display IDs for an arrangement of outputs; null forgets them.</summary>
        public void SetDisplayIds(string outputsKey, Dictionary<string, int> ids)
        {
            if (ids == null)
                displayIds.Remove(outputsKey);
            else
                displayIds[outputsKey] = new Dictionary<string, int>(ids);
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
                            // Connection=<key>|<name it was given>, or just the key.
                            int nameBar = value.IndexOf('|');
                            string connectionKey = nameBar >= 0 ? value.Substring(0, nameBar) : value;
                            if (Connection.FromKey(connectionKey) is Connection connection && settings.Connections.All(c => c.Key != connection.Key))
                            {
                                if (nameBar >= 0 && value.Length > nameBar + 1)
                                    connection.CustomName = value.Substring(nameBar + 1).Trim();
                                settings.Connections.Add(connection);
                            }
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
                        case "DisplayIds":
                            // DisplayIds=<outputs key>|<display key>=<id>;<display key>=<id>…
                            int idsBar = value.IndexOf('|');
                            if (idsBar <= 0)
                                break;
                            var ids = new Dictionary<string, int>();
                            foreach (string pair in value.Substring(idsBar + 1).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                int equals = pair.LastIndexOf('=');
                                if (equals > 0 && int.TryParse(pair.Substring(equals + 1), out int id))
                                    ids[pair.Substring(0, equals)] = id;
                            }
                            if (ids.Count > 0)
                                settings.displayIds[value.Substring(0, idsBar)] = ids;
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
                lines.Add("Connection=" + connection.Key + (string.IsNullOrWhiteSpace(connection.CustomName) ? "" : "|" + connection.CustomName.Trim()));
            foreach (KeyValuePair<string, List<List<string>>> setup in displayButtons)
            {
                foreach (List<string> button in setup.Value)
                    lines.Add("DisplayButton=" + setup.Key + "|" + string.Join(";", button));
            }
            foreach (KeyValuePair<string, Dictionary<string, int>> outputs in displayIds)
                lines.Add("DisplayIds=" + outputs.Key + "|" + string.Join(";", outputs.Value.Select(pair => pair.Key + "=" + pair.Value)));

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllLines(FilePath, lines);
        }

        static bool ParseBool(string value, bool fallback) => bool.TryParse(value, out bool result) ? result : fallback;
    }
}
