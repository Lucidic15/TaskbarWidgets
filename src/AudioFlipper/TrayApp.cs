using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using AudioFlipper.Audio;
using AudioFlipper.Webcams;

namespace AudioFlipper
{
    /// <summary>
    /// Runs the taskbar widget and the notification-area icon. On either, left-click switches to the
    /// next output, middle-click toggles mute and right-click opens the menu. The widget also changes
    /// volume with the mouse wheel.
    /// </summary>
    internal sealed class TrayApp : ApplicationContext
    {
        const int RefreshIntervalMilliseconds = 500;
        const int MaxTooltipLength = 63; // NotifyIcon.Text limit on .NET Framework.
        const int VolumeStepPercent = 2; // Same step as the Windows volume flyout.
        const int VolumeRefreshMilliseconds = 30; // How often the widget catches up while the volume is changing.
        const int TooltipUpdateMilliseconds = 250;

        readonly AudioDeviceManager audio = new AudioDeviceManager();
        readonly Settings settings = Settings.Load();
        readonly NotifyIcon trayIcon = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly Timer refreshTimer = new Timer { Interval = RefreshIntervalMilliseconds };
        readonly Timer volumeRefreshTimer = new Timer { Interval = VolumeRefreshMilliseconds };
        readonly OsdForm popup = new OsdForm();
        readonly TaskbarStrip strip;

        OutputStatus lastStatus;
        bool lastLightTaskbar;
        bool hasRefreshed;
        bool webcamBusy;
        int lastTooltipUpdate;

        public TrayApp()
        {
            lastTooltipUpdate = Environment.TickCount - TooltipUpdateMilliseconds;
            menu.ImageScalingSize = SystemInformation.SmallIconSize;
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

            strip = new TaskbarStrip(settings.TaskbarOffset);
            strip.NextRequested += (sender, e) => SwitchToNext();
            strip.MuteRequested += (sender, e) => ToggleMute();
            strip.VolumeStepsRequested += (sender, steps) => ChangeVolume(steps * VolumeStepPercent);
            strip.VolumeChangeRequested += (sender, level) => SetVolume(level);
            strip.MenuRequested += (sender, e) => ShowMenuFromStrip();
            strip.WebcamToggleRequested += (sender, e) => ToggleWebcam();
            strip.OffsetChanged += (sender, e) =>
            {
                settings.TaskbarOffset = strip.Offset;
                SaveSettings();
            };

            refreshTimer.Tick += (sender, e) =>
            {
                RefreshStatus(false);
                RefreshWebcam();
            };
            volumeRefreshTimer.Tick += (sender, e) =>
            {
                volumeRefreshTimer.Stop();
                RefreshStatus(false);
            };

            RefreshStatus(false);
            RefreshWebcam();
            trayIcon.Visible = true;
            strip.SetShowOnTaskbar(settings.ShowOnTaskbar);
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

        void SetVolume(float level)
        {
            try
            {
                audio.SetDefaultOutputVolume(level);
                RefreshSoon();
            }
            catch (Exception ex)
            {
                popup.ShowMessage("Couldn't change volume", ex.Message);
            }
        }

        void ChangeVolume(int percentagePoints)
        {
            try
            {
                audio.ChangeDefaultOutputVolume(percentagePoints);
                RefreshSoon();
            }
            catch (Exception ex)
            {
                popup.ShowMessage("Couldn't change volume", ex.Message);
            }
        }

        // Slider drags and fast scrolling change the volume dozens of times a second; refresh the
        // widget at a steady rate instead of after every change, so the slider stays smooth.
        void RefreshSoon()
        {
            if (!volumeRefreshTimer.Enabled)
                volumeRefreshTimer.Start();
        }

        /// <summary>
        /// Polls the default output and its volume, updating the widget, icon and tooltip when
        /// anything changed. The popup is shown when asked to, or when the output was changed elsewhere
        /// (Windows settings, plugging in headphones, "AudioFlipper.exe --next"), but only while the
        /// taskbar widget isn't showing, since the widget already shows the same thing.
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

            bool themeChanged = !hasRefreshed || lightTaskbar != lastLightTaskbar;
            if (themeChanged || !OutputStatus.AreEqual(status, lastStatus))
                strip.SetStatus(status, lightTaskbar);
            if (themeChanged || TrayIconDiffers(status, lastStatus))
                UpdateTrayIcon(status, lightTaskbar);
            UpdateTrayTooltip(status);

            lastStatus = status;
            lastLightTaskbar = lightTaskbar;
            hasRefreshed = true;

            if ((showPopup || deviceChanged) && settings.ShowPopup && !strip.Visible)
                popup.ShowStatus(status);
        }

        // The tray icon shows the device's icon and whether it's muted, not the volume level.
        static bool TrayIconDiffers(OutputStatus a, OutputStatus b)
        {
            if (a == null || b == null)
                return a != b;
            return a.Device.Id != b.Device.Id || a.Device.IconPath != b.Device.IconPath
                || a.Device.Kind != b.Device.Kind || a.Muted != b.Muted;
        }

        void UpdateTrayIcon(OutputStatus status, bool lightTaskbar)
        {
            Icon previous = trayIcon.Icon;
            trayIcon.Icon = IconRenderer.Render(status, lightTaskbar);
            previous?.Dispose();
        }

        void UpdateTrayTooltip(OutputStatus status)
        {
            string text = BuildTooltip(status);
            // Every change is a call into Explorer; while the volume is changing quickly, let the
            // tooltip catch up a few times a second rather than on every step.
            if (text == trayIcon.Text || Environment.TickCount - lastTooltipUpdate < TooltipUpdateMilliseconds)
                return;
            trayIcon.Text = text;
            lastTooltipUpdate = Environment.TickCount;
        }

        static string BuildTooltip(OutputStatus status)
        {
            if (status == null)
                return "AudioFlipper: no output device";

            string volume = status.Muted ? $"Muted ({status.VolumePercent}%)" : $"Volume {status.VolumePercent}%";
            string name = status.Device.FullName;
            int maxNameLength = MaxTooltipLength - volume.Length - 1;
            if (name.Length > maxNameLength)
                name = name.Substring(0, maxNameLength - 1) + "…";
            return name + "\n" + volume;
        }

        void ShowMenuFromStrip()
        {
            // Like the tray icon does, take the foreground first so the menu closes when clicking elsewhere.
            // Open it above the widget, which keeps itself on top and would otherwise cover the menu.
            NativeMethods.SetForegroundWindow(strip.Handle);
            menu.Show(new Point(Cursor.Position.X, strip.Top), ToolStripDropDownDirection.AboveRight);
        }

        void RebuildMenu()
        {
            ToolStripItem[] oldItems = menu.Items.Cast<ToolStripItem>().ToArray();
            menu.Items.Clear();
            foreach (ToolStripItem item in oldItems)
            {
                item.Image?.Dispose();
                item.Dispose();
            }

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
                string label = settings.IsExcluded(id) ? device.FullName + "  (not in rotation)" : device.FullName;
                Bitmap icon = DeviceIcons.Get(device.IconPath, menu.ImageScalingSize.Width);
                var item = new ToolStripMenuItem(label)
                {
                    Checked = id == currentId,
                    // A copy, because menu items may dispose their image and the icon is cached.
                    Image = icon != null ? new Bitmap(icon) : null,
                };
                item.Click += (sender, e) => SwitchTo(id);
                menu.Items.Add(item);
            }

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Rename or change device icons…", null, (sender, e) => OpenSoundControlPanel());
            menu.Items.Add("Sound settings…", null, (sender, e) => OpenSoundSettings());

            menu.Items.Add(new ToolStripSeparator());
            var rotation = new ToolStripMenuItem("Include in click rotation") { Enabled = devices.Count > 0 };
            foreach (AudioDevice device in devices)
            {
                string id = device.Id;
                rotation.DropDownItems.Add(Toggle(device.FullName, !settings.IsExcluded(id), include => settings.SetExcluded(id, !include)));
            }
            menu.Items.Add(rotation);
            menu.Items.Add(Toggle("Show on taskbar", settings.ShowOnTaskbar, value =>
            {
                settings.ShowOnTaskbar = value;
                strip.SetShowOnTaskbar(value);
            }));
            menu.Items.Add(Toggle("Also switch communications device", settings.SwitchCommunications, value => settings.SwitchCommunications = value));
            menu.Items.Add(Toggle("Show popup on change", settings.ShowPopup, value => settings.ShowPopup = value));
            menu.Items.Add(Toggle("Start with Windows", StartupRegistration.IsEnabled(), StartupRegistration.SetEnabled));

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(BuildWebcamMenu());

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (sender, e) => ExitThread());
        }

        ToolStripMenuItem BuildWebcamMenu()
        {
            List<Webcam> webcams;
            try
            {
                webcams = WebcamDevices.GetAll();
            }
            catch (Exception)
            {
                webcams = new List<Webcam>();
            }

            var webcamMenu = new ToolStripMenuItem("Webcam");
            webcamMenu.DropDownItems.Add(Toggle("Show webcam on taskbar", settings.ShowWebcam, value =>
            {
                settings.ShowWebcam = value;
                if (value && string.IsNullOrEmpty(settings.WebcamId) && webcams.Count > 0)
                    SelectWebcam(webcams[0]);
                RefreshWebcam();
            }));

            webcamMenu.DropDownItems.Add(new ToolStripSeparator());
            webcamMenu.DropDownItems.Add(new ToolStripMenuItem("Webcam to control") { Enabled = false });
            if (webcams.Count == 0)
                webcamMenu.DropDownItems.Add(new ToolStripMenuItem("No webcams found") { Enabled = false });
            foreach (Webcam webcam in webcams)
            {
                Webcam choice = webcam;
                var item = new ToolStripMenuItem(webcam.Enabled ? webcam.Name : webcam.Name + "  (off)")
                {
                    Checked = webcam.InstanceId == settings.WebcamId,
                };
                item.Click += (sender, e) =>
                {
                    SelectWebcam(choice);
                    settings.ShowWebcam = true;
                    SaveSettings();
                    RefreshWebcam();
                };
                webcamMenu.DropDownItems.Add(item);
            }

            webcamMenu.DropDownItems.Add(new ToolStripSeparator());
            Webcam selected = webcams.FirstOrDefault(w => w.InstanceId == settings.WebcamId);
            if (selected != null)
            {
                string action = (selected.Enabled ? "Turn off " : "Turn on ") + selected.Name;
                webcamMenu.DropDownItems.Add(new ToolStripMenuItem(action, null, (sender, e) => ToggleWebcam()) { Enabled = !webcamBusy });
            }
            webcamMenu.DropDownItems.Add("Camera settings…", null, (sender, e) => Launch("ms-settings:camera", null, "Couldn't open Camera settings"));
            return webcamMenu;
        }

        void SelectWebcam(Webcam webcam)
        {
            settings.WebcamId = webcam.InstanceId;
            settings.WebcamName = webcam.Name;
        }

        /// <summary>Shows the chosen webcam's state on the widget, or hides the webcam section.</summary>
        void RefreshWebcam()
        {
            WebcamStatus status = null;
            if (settings.ShowWebcam && !string.IsNullOrEmpty(settings.WebcamId))
            {
                bool? enabled = WebcamDevices.IsEnabled(settings.WebcamId);
                if (enabled != null)
                    status = new WebcamStatus(settings.WebcamName ?? "Webcam", enabled.Value, webcamBusy);
            }
            strip.SetWebcam(status);
        }

        async void ToggleWebcam()
        {
            if (webcamBusy || string.IsNullOrEmpty(settings.WebcamId))
                return;

            string name = settings.WebcamName ?? "the webcam";
            bool? enabled = WebcamDevices.IsEnabled(settings.WebcamId);
            if (enabled == null)
            {
                popup.ShowMessage("Webcam not found", name + " isn't connected.");
                return;
            }

            bool turnOn = !enabled.Value;
            webcamBusy = true;
            RefreshWebcam();
            // Windows only shows the UAC prompt up front for the foreground app (otherwise it just
            // flashes on the taskbar), and the widget never takes focus by itself.
            IntPtr owner = strip.Visible ? strip.Handle : IntPtr.Zero;
            if (owner != IntPtr.Zero)
                NativeMethods.SetForegroundWindow(owner);
            WebcamChangeResult result;
            try
            {
                result = await WebcamDevices.SetEnabledAsync(settings.WebcamId, turnOn, owner);
            }
            catch (Exception)
            {
                result = WebcamChangeResult.Failed;
            }
            finally
            {
                webcamBusy = false;
            }
            RefreshWebcam();

            if (result == WebcamChangeResult.RestartNeeded)
                popup.ShowMessage("Restart to finish", "Windows will finish turning " + (turnOn ? "on " : "off ") + name + " after a restart.");
            else if (result == WebcamChangeResult.Failed)
                popup.ShowMessage("Couldn't turn " + (turnOn ? "on " : "off ") + name, "Try it from Camera settings in the Webcam menu.");
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

        void SaveSettings()
        {
            try
            {
                settings.Save();
            }
            catch (Exception ex)
            {
                popup.ShowMessage("Couldn't save settings", ex.Message);
            }
        }

        // The classic Sound control panel (Playback tab), where a device can be renamed or given a
        // different icon via right-click > Properties. AudioFlipper picks up changes within a second.
        void OpenSoundControlPanel() => Launch("control.exe", "mmsys.cpl,,0", "Couldn't open the Sound control panel");

        void OpenSoundSettings() => Launch("ms-settings:sound", null, "Couldn't open Sound settings");

        void Launch(string fileName, string arguments, string errorTitle)
        {
            try
            {
                Process.Start(new ProcessStartInfo(fileName, arguments ?? "") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                popup.ShowMessage(errorTitle, ex.Message);
            }
        }

        protected override void ExitThreadCore()
        {
            refreshTimer.Stop();
            volumeRefreshTimer.Stop();
            strip.SetShowOnTaskbar(false);
            trayIcon.Visible = false;
            base.ExitThreadCore();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                refreshTimer.Dispose();
                volumeRefreshTimer.Dispose();
                trayIcon.Icon?.Dispose();
                trayIcon.Dispose();
                menu.Dispose();
                popup.Dispose();
                strip.Dispose();
                audio.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
