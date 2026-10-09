using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using AudioFlipper.Audio;

namespace AudioFlipper
{
    /// <summary>
    /// The notification-area icon. Left-click switches to the next output, middle-click
    /// toggles mute and right-click opens the menu. Hovering shows the device and volume.
    /// </summary>
    internal sealed class TrayApp : ApplicationContext
    {
        const int RefreshIntervalMilliseconds = 1000;
        const int MaxTooltipLength = 63; // NotifyIcon.Text limit on .NET Framework.

        readonly AudioDeviceManager audio = new AudioDeviceManager();
        readonly Settings settings = Settings.Load();
        readonly NotifyIcon trayIcon = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly Timer refreshTimer = new Timer { Interval = RefreshIntervalMilliseconds };
        readonly OsdForm popup = new OsdForm();

        OutputStatus lastStatus;
        bool lastLightTaskbar;
        bool hasRefreshed;

        public TrayApp()
        {
            menu.Items.Add("Loading…"); // Replaced every time the menu opens.
            menu.Opening += (sender, e) =>
            {
                RebuildMenu();
                e.Cancel = false;
            };

            trayIcon.ContextMenuStrip = menu;
            trayIcon.MouseClick += OnTrayIconClick;
            // A quick second click arrives as a double-click instead of a click; treat it the same
            // so clicking twice skips ahead two devices.
            trayIcon.MouseDoubleClick += OnTrayIconClick;

            refreshTimer.Tick += (sender, e) => RefreshStatus(false);

            RefreshStatus(false);
            trayIcon.Visible = true;
            refreshTimer.Start();
        }

        void OnTrayIconClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                SwitchToNext();
            else if (e.Button == MouseButtons.Middle)
                ToggleMute();
        }

        void SwitchToNext()
        {
            try
            {
                if (OutputCycler.SwitchToNext(audio, settings) == null)
                {
                    popup.ShowMessage("No output devices", "Enable an output in Windows Sound settings.");
                    return;
                }
                RefreshStatus(true);
            }
            catch (Exception ex)
            {
                popup.ShowMessage("Couldn't switch output", ex.Message);
            }
        }

        void SwitchTo(string deviceId)
        {
            try
            {
                audio.SetDefaultOutputDevice(deviceId, settings.SwitchCommunications);
                RefreshStatus(true);
            }
            catch (Exception ex)
            {
                popup.ShowMessage("Couldn't switch output", ex.Message);
            }
        }

        void ToggleMute()
        {
            try
            {
                audio.ToggleDefaultOutputMute();
                RefreshStatus(true);
            }
            catch (Exception ex)
            {
                popup.ShowMessage("Couldn't toggle mute", ex.Message);
            }
        }

        /// <summary>
        /// Polls the default output and its volume, updating the icon and tooltip when anything
        /// changed. The popup is shown when asked to, or when the output was changed elsewhere
        /// (Windows settings, plugging in headphones, "AudioFlipper.exe --next").
        /// </summary>
        void RefreshStatus(bool showPopup)
        {
            OutputStatus status;
            try
            {
                status = audio.GetDefaultOutputStatus();
            }
            catch (Exception)
            {
                // Typically a device disappearing mid-poll; keep what's shown and try again on the
                // next tick. The very first poll still needs an icon, though.
                if (hasRefreshed)
                    return;
                status = null;
            }

            bool lightTaskbar = Theme.TaskbarUsesLightTheme();
            bool deviceChanged = hasRefreshed && status?.Device.Id != lastStatus?.Device.Id;

            if (!hasRefreshed || lightTaskbar != lastLightTaskbar || !OutputStatus.AreEqual(status, lastStatus))
                UpdateTrayIcon(status, lightTaskbar);

            lastStatus = status;
            lastLightTaskbar = lightTaskbar;
            hasRefreshed = true;

            if ((showPopup || deviceChanged) && settings.ShowPopup)
                popup.ShowStatus(status);
        }

        void UpdateTrayIcon(OutputStatus status, bool lightTaskbar)
        {
            Icon previous = trayIcon.Icon;
            trayIcon.Icon = IconRenderer.Render(status, lightTaskbar);
            previous?.Dispose();
            trayIcon.Text = BuildTooltip(status);
        }

        static string BuildTooltip(OutputStatus status)
        {
            if (status == null)
                return "AudioFlipper: no output device";

            string volume = status.Muted ? $"Muted ({status.VolumePercent}%)" : $"Volume {status.VolumePercent}%";
            int maxNameLength = MaxTooltipLength - volume.Length - 1;
            string name = status.Device.Name.Length <= maxNameLength
                ? status.Device.Name
                : status.Device.Name.Substring(0, maxNameLength - 1) + "…";
            return name + "\n" + volume;
        }

        void RebuildMenu()
        {
            ToolStripItem[] oldItems = menu.Items.Cast<ToolStripItem>().ToArray();
            menu.Items.Clear();
            foreach (ToolStripItem item in oldItems)
                item.Dispose();

            List<AudioDevice> devices;
            string currentId;
            try
            {
                devices = audio.GetActiveOutputDevices();
                currentId = audio.GetDefaultOutputDeviceId();
            }
            catch (Exception ex)
            {
                devices = new List<AudioDevice>();
                currentId = null;
                menu.Items.Add(new ToolStripMenuItem("Error: " + ex.Message) { Enabled = false });
            }

            menu.Items.Add(new ToolStripMenuItem("Output device") { Enabled = false });
            if (devices.Count == 0)
                menu.Items.Add(new ToolStripMenuItem("No active outputs") { Enabled = false });
            foreach (AudioDevice device in devices)
            {
                string id = device.Id;
                string label = settings.IsExcluded(id) ? device.Name + "  (not in rotation)" : device.Name;
                var item = new ToolStripMenuItem(label) { Checked = id == currentId };
                item.Click += (sender, e) => SwitchTo(id);
                menu.Items.Add(item);
            }

            menu.Items.Add(new ToolStripSeparator());

            var rotation = new ToolStripMenuItem("Include in click rotation") { Enabled = devices.Count > 0 };
            foreach (AudioDevice device in devices)
            {
                string id = device.Id;
                rotation.DropDownItems.Add(Toggle(device.Name, !settings.IsExcluded(id), include => settings.SetExcluded(id, !include)));
            }
            menu.Items.Add(rotation);
            menu.Items.Add(Toggle("Also switch communications device", settings.SwitchCommunications, value => settings.SwitchCommunications = value));
            menu.Items.Add(Toggle("Show popup on change", settings.ShowPopup, value => settings.ShowPopup = value));
            menu.Items.Add(Toggle("Start with Windows", StartupRegistration.IsEnabled(), StartupRegistration.SetEnabled));

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Sound settings…", null, (sender, e) => OpenSoundSettings());
            menu.Items.Add("Exit", null, (sender, e) => ExitThread());
        }

        ToolStripMenuItem Toggle(string text, bool isChecked, Action<bool> apply)
        {
            var item = new ToolStripMenuItem(text) { Checked = isChecked };
            item.Click += (sender, e) =>
            {
                try
                {
                    apply(!isChecked);
                    settings.Save();
                }
                catch (Exception ex)
                {
                    popup.ShowMessage("Couldn't change setting", ex.Message);
                }
            };
            return item;
        }

        void OpenSoundSettings()
        {
            try
            {
                Process.Start("ms-settings:sound");
            }
            catch (Exception ex)
            {
                popup.ShowMessage("Couldn't open Sound settings", ex.Message);
            }
        }

        protected override void ExitThreadCore()
        {
            refreshTimer.Stop();
            trayIcon.Visible = false;
            base.ExitThreadCore();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                refreshTimer.Dispose();
                trayIcon.Icon?.Dispose();
                trayIcon.Dispose();
                menu.Dispose();
                popup.Dispose();
                audio.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
