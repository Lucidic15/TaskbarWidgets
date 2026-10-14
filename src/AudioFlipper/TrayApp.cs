using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Media;
using System.Windows.Forms;
using AudioFlipper.Audio;
using AudioFlipper.Webcams;

namespace AudioFlipper
{
    /// <summary>
    /// Runs the taskbar widget and the notification-area icon. The tray icon is for the output:
    /// left-click switches to the next output, middle-click toggles mute and right-click opens the
    /// menu. The widget does the same for each of its sections (output, input, webcam).
    /// </summary>
    internal sealed class TrayApp : ApplicationContext
    {
        const int RefreshIntervalMilliseconds = 500;
        const int MaxTooltipLength = 63; // NotifyIcon.Text limit on .NET Framework.
        const int VolumeStepPercent = 2; // Same step as the Windows volume flyout.
        const int VolumeRefreshMilliseconds = 30; // How often the widget catches up while the volume is changing.
        const int TooltipUpdateMilliseconds = 250;
        const int DingAfterScrollMilliseconds = 250; // Ding once scrolling pauses, not on every notch.

        readonly AudioDeviceManager audio = new AudioDeviceManager();
        readonly Settings settings = Settings.Load();
        readonly NotifyIcon trayIcon = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly Timer refreshTimer = new Timer { Interval = RefreshIntervalMilliseconds };
        readonly Timer volumeRefreshTimer = new Timer { Interval = VolumeRefreshMilliseconds };
        readonly Timer dingTimer = new Timer { Interval = DingAfterScrollMilliseconds };
        readonly OsdForm popup = new OsdForm();
        readonly TaskbarStrip strip;

        AudioStatus lastOutput;
        AudioStatus lastInput;
        bool lastLightTaskbar;
        bool hasRefreshed;
        bool webcamBusy;
        int lastTooltipUpdate;

        public TrayApp()
        {
            StartupRegistration.RepairMovedExe();
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
            strip.NextRequested += (sender, e) => SwitchToNext(e.Flow);
            strip.MuteRequested += (sender, e) => ToggleMute(e.Flow);
            strip.VolumeStepsRequested += (sender, e) => ChangeVolume(e.Flow, e.Notches * VolumeStepPercent);
            strip.VolumeChangeRequested += (sender, e) => SetVolume(e.Flow, e.Level);
            strip.VolumeChangeFinished += (sender, e) =>
            {
                if (e.Flow == EDataFlow.Render)
                    Ding();
            };
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
            dingTimer.Tick += (sender, e) => Ding();
            volumeRefreshTimer.Tick += (sender, e) =>
            {
                volumeRefreshTimer.Stop();
                RefreshStatus(false);
            };

            RefreshStatus(false);
            RefreshWebcam();
            trayIcon.Visible = true;
            strip.SetSections(settings.ShowOutput, settings.ShowInput);
            refreshTimer.Start();
        }

        void OnTrayIconClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                SwitchToNext(EDataFlow.Render);
            else if (e.Button == MouseButtons.Middle)
                ToggleMute(EDataFlow.Render);
        }

        void SwitchToNext(EDataFlow flow)
        {
            try
            {
                if (DeviceCycler.SwitchToNext(audio, flow, settings) == null)
                {
                    if (flow == EDataFlow.Render)
                        popup.ShowMessage("No output devices", "Enable an output in Windows Sound settings.");
                    else
                        popup.ShowMessage("No input devices", "Enable a microphone in Windows Sound settings.");
                    return;
                }
                RefreshStatus(flow == EDataFlow.Render);
            }
            catch (Exception ex)
            {
                popup.ShowMessage(flow == EDataFlow.Render ? "Couldn't switch output" : "Couldn't switch input", ex.Message);
            }
        }

        void SwitchTo(string deviceId, EDataFlow flow)
        {
            try
            {
                audio.SetDefaultDevice(deviceId, settings.SwitchesCommunications(flow));
                RefreshStatus(flow == EDataFlow.Render);
            }
            catch (Exception ex)
            {
                popup.ShowMessage(flow == EDataFlow.Render ? "Couldn't switch output" : "Couldn't switch input", ex.Message);
            }
        }

        void ToggleMute(EDataFlow flow)
        {
            try
            {
                audio.ToggleDefaultMute(flow);
                RefreshStatus(flow == EDataFlow.Render);
            }
            catch (Exception ex)
            {
                popup.ShowMessage("Couldn't toggle mute", ex.Message);
            }
        }

        void SetVolume(EDataFlow flow, float level)
        {
            try
            {
                audio.SetDefaultVolume(flow, level);
                RefreshSoon();
            }
            catch (Exception ex)
            {
                popup.ShowMessage("Couldn't change volume", ex.Message);
            }
        }

        void ChangeVolume(EDataFlow flow, int percentagePoints)
        {
            try
            {
                audio.ChangeDefaultVolume(flow, percentagePoints);
                RefreshSoon();
                if (flow == EDataFlow.Render && settings.VolumeDing)
                {
                    dingTimer.Stop();
                    dingTimer.Start();
                }
            }
            catch (Exception ex)
            {
                popup.ShowMessage("Couldn't change volume", ex.Message);
            }
        }

        /// <summary>
        /// Plays the Windows "Default Beep" (the ding Windows' own volume slider makes) on the
        /// default output, so the new volume can be heard. Uses the sound from the Windows sound scheme.
        /// </summary>
        void Ding()
        {
            dingTimer.Stop();
            if (settings.VolumeDing)
                SystemSounds.Beep.Play();
        }

        // Slider drags and fast scrolling change the volume dozens of times a second; refresh the
        // widget at a steady rate instead of after every change, so the slider stays smooth.
        void RefreshSoon()
        {
            if (!volumeRefreshTimer.Enabled)
                volumeRefreshTimer.Start();
        }

        /// <summary>
        /// Polls the default output and input with their volumes, updating the widget, icon and
        /// tooltip when anything changed. The popup is for the output: it's shown when asked to, or
        /// when the output was changed elsewhere (Windows settings, plugging in headphones,
        /// "AudioFlipper.exe --next"), but only while the widget isn't showing the output already.
        /// </summary>
        void RefreshStatus(bool showPopup)
        {
            // On an error (typically a device disappearing mid-poll) keep what's shown and try
            // again on the next tick.
            AudioStatus output = PollStatus(EDataFlow.Render, lastOutput);
            AudioStatus input = settings.ShowInput ? PollStatus(EDataFlow.Capture, lastInput) : null;

            bool lightTaskbar = Theme.TaskbarUsesLightTheme();
            bool outputChanged = hasRefreshed && output?.Device.Id != lastOutput?.Device.Id;

            bool themeChanged = !hasRefreshed || lightTaskbar != lastLightTaskbar;
            if (themeChanged || !AudioStatus.AreEqual(output, lastOutput) || !AudioStatus.AreEqual(input, lastInput))
                strip.SetStatus(output, input, lightTaskbar);
            if (themeChanged || TrayIconDiffers(output, lastOutput))
                UpdateTrayIcon(output, lightTaskbar);
            UpdateTrayTooltip(output);

            lastOutput = output;
            lastInput = input;
            lastLightTaskbar = lightTaskbar;
            hasRefreshed = true;

            if ((showPopup || outputChanged) && settings.ShowPopup && !strip.ShowsOutput)
                popup.ShowStatus(output);
        }

        AudioStatus PollStatus(EDataFlow flow, AudioStatus last)
        {
            try
            {
                return audio.GetDefaultStatus(flow);
            }
            catch (Exception)
            {
                return last;
            }
        }

        // The tray icon shows the device's icon and whether it's muted, not the volume level.
        static bool TrayIconDiffers(AudioStatus a, AudioStatus b)
        {
            if (a == null || b == null)
                return a != b;
            return a.Device.Id != b.Device.Id || a.Device.IconPath != b.Device.IconPath
                || a.Device.Kind != b.Device.Kind || a.Muted != b.Muted;
        }

        void UpdateTrayIcon(AudioStatus status, bool lightTaskbar)
        {
            Icon previous = trayIcon.Icon;
            trayIcon.Icon = IconRenderer.Render(status, lightTaskbar);
            previous?.Dispose();
        }

        void UpdateTrayTooltip(AudioStatus status)
        {
            string text = BuildTooltip(status);
            // Every change is a call into Explorer; while the volume is changing quickly, let the
            // tooltip catch up a few times a second rather than on every step.
            if (text == trayIcon.Text || Environment.TickCount - lastTooltipUpdate < TooltipUpdateMilliseconds)
                return;
            trayIcon.Text = text;
            lastTooltipUpdate = Environment.TickCount;
        }

        static string BuildTooltip(AudioStatus status)
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
            DisposeMenuItems(menu.Items);
            menu.Items.Add(BuildAudioMenu(EDataFlow.Render));
            menu.Items.Add(BuildAudioMenu(EDataFlow.Capture));
            menu.Items.Add(BuildWebcamMenu());
            menu.Items.Add(new ToolStripSeparator());
            if (BuildLinkMenu() is ToolStripMenuItem linkMenu)
                menu.Items.Add(linkMenu);
            menu.Items.Add(Toggle("Start with Windows", StartupRegistration.IsEnabled(), StartupRegistration.SetEnabled));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (sender, e) => ExitThread());
        }

        /// <summary>
        /// Linking the widget with RDPLauncher's into one, on either side; shown while RDPLauncher is
        /// running (or they're linked).
        /// </summary>
        ToolStripMenuItem BuildLinkMenu()
        {
            WidgetLink.State link = WidgetLink.Read();
            if (!link.Linked && !WidgetLink.PartnerRunning())
                return null;

            var linkMenu = new ToolStripMenuItem("Link with " + WidgetLink.Partner);
            void AddChoice(string text, bool isChecked, bool linked, bool selfOnLeft)
            {
                var item = new ToolStripMenuItem(text) { Checked = isChecked };
                item.Click += (sender, e) =>
                {
                    try
                    {
                        strip.SetLink(linked, selfOnLeft);
                    }
                    catch (Exception ex)
                    {
                        popup.ShowMessage("Couldn't change the link", ex.Message);
                    }
                };
                linkMenu.DropDownItems.Add(item);
            }
            AddChoice("Not linked", !link.Linked, false, link.SelfOnLeft);
            AddChoice(WidgetLink.Partner + " on the left", link.Linked && !link.SelfOnLeft, true, false);
            AddChoice(WidgetLink.Partner + " on the right", link.Linked && link.SelfOnLeft, true, true);
            return linkMenu;
        }

        // Disposes the previous menu, including the copies of device icons the items show.
        static void DisposeMenuItems(ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items.Cast<ToolStripItem>().ToArray())
            {
                if (item is ToolStripDropDownItem dropDownItem)
                    DisposeMenuItems(dropDownItem.DropDownItems);
                item.Image?.Dispose();
                item.Dispose();
            }
        }

        /// <summary>The Output or Input submenu: showing it on the widget, the devices and their settings.</summary>
        ToolStripMenuItem BuildAudioMenu(EDataFlow flow)
        {
            bool isOutput = flow == EDataFlow.Render;
            var submenu = new ToolStripMenuItem(isOutput ? "Output" : "Input");
            ToolStripItemCollection items = submenu.DropDownItems;

            items.Add(Toggle("Show on taskbar", isOutput ? settings.ShowOutput : settings.ShowInput, value =>
            {
                if (isOutput)
                    settings.ShowOutput = value;
                else
                    settings.ShowInput = value;
                // Poll first so a newly shown section appears with its device straight away.
                RefreshStatus(false);
                strip.SetSections(settings.ShowOutput, settings.ShowInput);
            }));
            items.Add(new ToolStripSeparator());

            List<AudioDevice> devices;
            string currentId;
            try
            {
                devices = audio.GetActiveDevices(flow);
                currentId = audio.GetDefaultDeviceId(flow);
            }
            catch (Exception ex)
            {
                devices = new List<AudioDevice>();
                currentId = null;
                items.Add(new ToolStripMenuItem("Error: " + ex.Message) { Enabled = false });
            }

            items.Add(new ToolStripMenuItem(isOutput ? "Output device" : "Input device") { Enabled = false });
            if (devices.Count == 0)
                items.Add(new ToolStripMenuItem(isOutput ? "No active outputs" : "No active inputs") { Enabled = false });
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
                item.Click += (sender, e) => SwitchTo(id, flow);
                items.Add(item);
            }

            items.Add(new ToolStripSeparator());
            var rotation = new ToolStripMenuItem("Include in click rotation") { Enabled = devices.Count > 0 };
            foreach (AudioDevice device in devices)
            {
                string id = device.Id;
                rotation.DropDownItems.Add(Toggle(device.FullName, !settings.IsExcluded(id), include => settings.SetExcluded(id, !include)));
            }
            items.Add(rotation);
            items.Add(Toggle("Also switch communications device", settings.SwitchesCommunications(flow), value =>
            {
                if (isOutput)
                    settings.SwitchCommunications = value;
                else
                    settings.SwitchInputCommunications = value;
            }));
            if (isOutput)
            {
                items.Add(Toggle("Ding when changing volume", settings.VolumeDing, value => settings.VolumeDing = value));
                items.Add(Toggle("Show popup on change", settings.ShowPopup, value => settings.ShowPopup = value));
            }

            items.Add(new ToolStripSeparator());
            items.Add("Rename or change device icons…", null, (sender, e) => OpenSoundControlPanel(flow));
            items.Add("Sound settings…", null, (sender, e) => OpenSoundSettings());
            return submenu;
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
            webcamMenu.DropDownItems.Add(Toggle("Show on taskbar", settings.ShowWebcam, value =>
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
                string nickname = settings.GetWebcamNickname(webcam.InstanceId);
                string label = nickname != null ? nickname + " (" + webcam.Name + ")" : webcam.Name;
                var item = new ToolStripMenuItem(webcam.Enabled ? label : label + "  (off)")
                {
                    Checked = webcam.InstanceId == settings.WebcamId,
                };
                item.Click += (sender, e) =>
                {
                    SelectWebcam(choice);
                    settings.ShowWebcam = true;
                    SaveSettings();
                    RefreshWebcam();
                    // The no-prompt tasks are tied to one webcam; set them up for the new one.
                    if (settings.WebcamTasksId != null && settings.WebcamTasksId != choice.InstanceId)
                        SetUpWebcamWithoutPrompts(true);
                };
                webcamMenu.DropDownItems.Add(item);
            }

            webcamMenu.DropDownItems.Add(new ToolStripSeparator());
            Webcam selected = webcams.FirstOrDefault(w => w.InstanceId == settings.WebcamId);
            if (selected != null)
            {
                string action = (selected.Enabled ? "Turn off " : "Turn on ") + WebcamDisplayName();
                webcamMenu.DropDownItems.Add(new ToolStripMenuItem(action, null, (sender, e) => ToggleWebcam()) { Enabled = !webcamBusy });

                bool noPrompt = settings.WebcamTasksId != null && settings.WebcamTasksId == settings.WebcamId;
                var noPromptItem = new ToolStripMenuItem("Switch without asking each time") { Checked = noPrompt };
                noPromptItem.Click += (sender, e) => SetUpWebcamWithoutPrompts(!noPrompt);
                webcamMenu.DropDownItems.Add(noPromptItem);
            }
            if (selected != null)
                webcamMenu.DropDownItems.Add("Rename…", null, (sender, e) => RenameWebcam(selected));
            webcamMenu.DropDownItems.Add("Camera settings…", null, (sender, e) => Launch("ms-settings:camera", null, "Couldn't open Camera settings"));
            return webcamMenu;
        }

        /// <summary>
        /// Sets up (or removes) the scheduled tasks that let the chosen webcam be switched without a
        /// UAC prompt every time. Either way asks for administrator rights once.
        /// </summary>
        async void SetUpWebcamWithoutPrompts(bool enable)
        {
            try
            {
                IntPtr owner = BringForwardForPrompt();
                if (enable)
                {
                    string id = settings.WebcamId;
                    bool? done = await WebcamTasks.InstallAsync(id, owner);
                    if (done == true)
                        settings.WebcamTasksId = id;
                    else if (done == false)
                        popup.ShowMessage("Couldn't set up the webcam switch", "Windows didn't allow it. Switching will keep asking each time.");
                }
                else
                {
                    bool? done = await WebcamTasks.RemoveAsync(owner);
                    if (done == true)
                        settings.WebcamTasksId = null;
                    else if (done == false)
                        popup.ShowMessage("Couldn't remove the webcam switch", "Windows didn't allow it.");
                }
                SaveSettings();
            }
            catch (Exception ex)
            {
                popup.ShowMessage("Couldn't change the webcam switch", ex.Message);
            }
        }

        // Windows only shows a UAC prompt up front for the foreground app (otherwise it just flashes
        // on the taskbar), and the widget never takes focus by itself.
        IntPtr BringForwardForPrompt()
        {
            if (!strip.Visible)
                return IntPtr.Zero;
            NativeMethods.SetForegroundWindow(strip.Handle);
            return strip.Handle;
        }

        /// <summary>
        /// Names the webcam in AudioFlipper. Windows has no way to rename a webcam, so this is
        /// AudioFlipper's own name for it, shown on the widget and in the menu.
        /// </summary>
        void RenameWebcam(Webcam webcam)
        {
            using (var dialog = new RenameDialog(
                "Rename webcam",
                "Name to show for this webcam in AudioFlipper. Leave it empty to use its own name, \"" + webcam.Name + "\".",
                settings.GetWebcamNickname(webcam.InstanceId) ?? webcam.Name))
            {
                if (dialog.ShowDialog() != DialogResult.OK)
                    return;
                string name = dialog.EnteredName;
                settings.SetWebcamNickname(webcam.InstanceId, name == webcam.Name ? null : name);
            }
            SaveSettings();
            RefreshWebcam();
        }

        /// <summary>The chosen webcam's name: the one given in AudioFlipper, or its own.</summary>
        string WebcamDisplayName() => settings.GetWebcamNickname(settings.WebcamId) ?? settings.WebcamName ?? "Webcam";

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
                    status = new WebcamStatus(WebcamDisplayName(), enabled.Value, webcamBusy);
            }
            strip.SetWebcam(status);
        }

        async void ToggleWebcam()
        {
            if (webcamBusy || string.IsNullOrEmpty(settings.WebcamId))
                return;

            string name = WebcamDisplayName();
            bool? enabled = WebcamDevices.IsEnabled(settings.WebcamId);
            if (enabled == null)
            {
                popup.ShowMessage("Webcam not found", name + " isn't connected.");
                return;
            }

            bool turnOn = !enabled.Value;
            webcamBusy = true;
            RefreshWebcam();
            WebcamChangeResult result;
            try
            {
                // With "Switch without asking each time" set up for this webcam, start its scheduled
                // task; otherwise (or if the task has gone missing) ask for administrator rights.
                WebcamChangeResult? viaTask = settings.WebcamTasksId == settings.WebcamId
                    ? await WebcamTasks.TrySetEnabledAsync(settings.WebcamId, turnOn)
                    : null;
                result = viaTask ?? await WebcamDevices.SetEnabledAsync(settings.WebcamId, turnOn, BringForwardForPrompt());
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

        // The classic Sound control panel (Playback or Recording tab), where a device can be renamed or
        // given a different icon via right-click > Properties. AudioFlipper picks up changes within a second.
        void OpenSoundControlPanel(EDataFlow flow) =>
            Launch("control.exe", flow == EDataFlow.Render ? "mmsys.cpl,,0" : "mmsys.cpl,,1", "Couldn't open the Sound control panel");

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
            dingTimer.Stop();
            strip.SetSections(false, false);
            strip.SetWebcam(null);
            trayIcon.Visible = false;
            base.ExitThreadCore();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                refreshTimer.Dispose();
                volumeRefreshTimer.Dispose();
                dingTimer.Dispose();
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
