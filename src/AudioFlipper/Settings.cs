using System;
using System.Collections.Generic;
using System.IO;
using AudioFlipper.Audio;

namespace AudioFlipper
{
    /// <summary>User preferences, stored as key=value lines in %APPDATA%\AudioFlipper\settings.txt.</summary>
    internal sealed class Settings
    {
        static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioFlipper", "settings.txt");

        readonly HashSet<string> excludedDeviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Also make the new output the default communications device (used by Teams, Discord, etc.).</summary>
        public bool SwitchCommunications { get; set; } = true;

        /// <summary>Also make the new input the default communications device.</summary>
        public bool SwitchInputCommunications { get; set; } = true;

        /// <summary>Show the popup when the output device changes (only while the taskbar widget isn't showing).</summary>
        public bool ShowPopup { get; set; } = true;

        /// <summary>Play the Windows "Default Beep" after changing the output volume, to hear the new level.</summary>
        public bool VolumeDing { get; set; } = true;

        /// <summary>Show the output section on the taskbar widget.</summary>
        public bool ShowOutput { get; set; } = true;

        /// <summary>Show the input (microphone) section on the taskbar widget.</summary>
        public bool ShowInput { get; set; }

        /// <summary>Taskbar widget position in pixels from the taskbar's left edge; negative for the default.</summary>
        public int TaskbarOffset { get; set; } = -1;

        /// <summary>Show the webcam on/off control on the taskbar widget.</summary>
        public bool ShowWebcam { get; set; }

        /// <summary>Device instance ID of the webcam to control.</summary>
        public string WebcamId { get; set; }

        /// <summary>Name of the webcam to control, kept so it can be shown without looking the device up.</summary>
        public string WebcamName { get; set; }

        /// <summary>
        /// The webcam the no-prompt scheduled tasks were set up for (see WebcamTasks), or null
        /// when they aren't set up.
        /// </summary>
        public string WebcamTasksId { get; set; }

        public bool SwitchesCommunications(EDataFlow flow) => flow == EDataFlow.Capture ? SwitchInputCommunications : SwitchCommunications;

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
                        case "ShowOutput":
                        case "ShowOnTaskbar": // Before 1.1 there was one switch for the whole widget.
                            settings.ShowOutput = ParseBool(value, settings.ShowOutput);
                            break;
                        case "VolumeDing":
                            settings.VolumeDing = ParseBool(value, settings.VolumeDing);
                            break;
                        case "ShowInput":
                            settings.ShowInput = ParseBool(value, settings.ShowInput);
                            break;
                        case "SwitchInputCommunications":
                            settings.SwitchInputCommunications = ParseBool(value, settings.SwitchInputCommunications);
                            break;
                        case "ShowWebcam":
                            settings.ShowWebcam = ParseBool(value, settings.ShowWebcam);
                            break;
                        case "WebcamId":
                            settings.WebcamId = value;
                            break;
                        case "WebcamName":
                            settings.WebcamName = value;
                            break;
                        case "WebcamTasksId":
                            settings.WebcamTasksId = value.Length > 0 ? value : null;
                            break;
                        case "TaskbarOffset":
                            if (int.TryParse(value, out int offset))
                                settings.TaskbarOffset = offset;
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
                "ShowOutput=" + ShowOutput,
                "ShowInput=" + ShowInput,
                "VolumeDing=" + VolumeDing,
                "SwitchInputCommunications=" + SwitchInputCommunications,
                "TaskbarOffset=" + TaskbarOffset,
                "ShowWebcam=" + ShowWebcam,
                "WebcamId=" + WebcamId,
                "WebcamName=" + WebcamName,
                "WebcamTasksId=" + WebcamTasksId,
            };
            foreach (string id in excludedDeviceIds)
                lines.Add("Exclude=" + id);

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllLines(FilePath, lines);
        }

        static bool ParseBool(string value, bool fallback) => bool.TryParse(value, out bool result) ? result : fallback;
    }
}
