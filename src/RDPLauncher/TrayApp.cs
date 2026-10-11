using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using RDPLauncher.Displays;
using RDPLauncher.Rdp;

namespace RDPLauncher
{
    /// <summary>
    /// Runs the taskbar widget and the notification-area icon. The widget's number buttons start
    /// Remote Desktop on that many displays, and clicking the computer's name goes to the next
    /// computer. The tray icon and right-click menu have the list of computers and the settings.
    /// </summary>
    internal sealed class TrayApp : ApplicationContext
    {
        const int RefreshIntervalMilliseconds = 1000;
        const int MaxTooltipLength = 63; // NotifyIcon.Text limit on .NET Framework.
        // Keeps a button busy for a moment after starting a session, so a double-click starts one session.
        const int StartCooldownMilliseconds = 2000;

        readonly Settings settings = Settings.Load();
        readonly NotifyIcon trayIcon = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly ContextMenuStrip connectionsMenu = new ContextMenuStrip();
        readonly Timer refreshTimer = new Timer { Interval = RefreshIntervalMilliseconds };
        readonly OsdForm popup = new OsdForm();
        readonly TaskbarStrip strip;

        List<LocalDisplay> displays = new List<LocalDisplay>();
        string setupKey;
        string displayDetails; // Also changes when the same arrangement comes back on different connectors.
        List<List<string>> buttons = new List<List<string>>();
        // Arrangements of outputs whose display IDs "mstsc /l" didn't give this time; they're guessed
        // instead of making every connection wait for it again.
        readonly HashSet<string> unreadableOutputs = new HashSet<string>();
        string connectionAddress; // The current connection's address, shown under its name.
        bool starting;
        int busyButton = -1;
        string lastContentSignature;
        bool? lastLightTaskbar;

        public TrayApp()
        {
            StartupRegistration.RepairMovedExe();
            menu.Items.Add("Loading…"); // Replaced every time the menu opens.
            menu.Opening += (sender, e) =>
            {
                ConnectionChanged(); // An .rdp file may have been edited since.
                RebuildMenu();
                e.Cancel = false;
            };
            connectionsMenu.Items.Add("Loading…");
            connectionsMenu.Opening += (sender, e) =>
            {
                ConnectionChanged();
                DisposeMenuItems(connectionsMenu.Items);
                AddConnectionItems(connectionsMenu.Items);
                e.Cancel = false;
            };

            trayIcon.ContextMenuStrip = menu;
            trayIcon.MouseClick += (sender, e) =>
            {
                // Left-click opens the same menu as right-click.
                if (e.Button == MouseButtons.Left)
                    typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(trayIcon, null);
            };

            strip = new TaskbarStrip(settings.TaskbarOffset);
            strip.ConnectRequested += (sender, button) => ConnectWithButton(button);
            strip.ConnectionClicked += (sender, e) =>
            {
                // Goes to the next computer; with only one there's nowhere to go, so offer to add another.
                if (settings.Connections.Count == 0)
                    AddComputer();
                else if (settings.Connections.Count == 1)
                    ShowMenuFromStrip(connectionsMenu);
                else
                    StepConnection(1);
            };
            strip.ConnectionStepRequested += (sender, notches) => StepConnection(-notches);
            strip.DisplayToggleRequested += (sender, e) => ToggleDisplay(e.Button, e.DisplayKey);
            strip.MenuRequested += (sender, e) => ShowMenuFromStrip(menu);
            strip.OffsetChanged += (sender, e) =>
            {
                settings.TaskbarOffset = strip.Offset;
                SaveSettings();
            };

            refreshTimer.Tick += (sender, e) =>
            {
                RefreshDisplays();
                RefreshWidget();
            };

            RefreshDisplays();
            ConnectionChanged();
            trayIcon.Visible = true;
            strip.SetShown(settings.ShowOnTaskbar);
            refreshTimer.Start();
        }

        /// <summary>Notices displays being added, removed or rearranged, and picks the display buttons for the new arrangement.</summary>
        void RefreshDisplays()
        {
            try
            {
                List<LocalDisplay> current = LocalDisplay.GetAll();
                string key = LocalDisplay.SetupKey(current);
                string details = key + "|" + string.Join(";", current.Select(d => d.DeviceName + "#" + d.OutputIndex));
                if (details == displayDetails)
                    return;
                LocalDisplay.LoadNames(current);
                displays = current;
                displayDetails = details;
                if (key != setupKey)
                {
                    setupKey = key;
                    buttons = DisplayChoices.ForSetup(displays, settings.GetDisplayButtons(key));
                }
            }
            catch (Exception)
            {
                // Displays changing mid-way; try again on the next tick.
            }
        }

        /// <summary>Updates the widget, tray icon and tooltip when anything they show has changed.</summary>
        void RefreshWidget()
        {
            bool lightTaskbar = Theme.TaskbarUsesLightTheme();
            if (lightTaskbar != lastLightTaskbar)
            {
                Icon previous = trayIcon.Icon;
                trayIcon.Icon = IconRenderer.Render(lightTaskbar);
                previous?.Dispose();
                lastLightTaskbar = lightTaskbar;
            }

            Connection connection = settings.CurrentConnection;
            var content = new WidgetContent
            {
                // The connection's name over its address, or just the address when it has no other name.
                Title = connection?.Name ?? "Remote Desktop",
                Detail = connection == null ? "Click to add a computer"
                    : connectionAddress != null && !string.Equals(connectionAddress, connection.Name, StringComparison.OrdinalIgnoreCase) ? connectionAddress
                    : "",
                Displays = displays,
                Buttons = connection == null ? new List<IReadOnlyCollection<string>>()
                    : buttons.Select(b => (IReadOnlyCollection<string>)b.ToList()).ToList(),
                BusyButton = busyButton,
                LightTaskbar = lightTaskbar,
            };

            string signature = string.Join("\n", content.Title, content.Detail, displayDetails, busyButton, lightTaskbar,
                string.Join("|", content.Buttons.Select(b => string.Join(";", b))));
            if (signature != lastContentSignature)
            {
                lastContentSignature = signature;
                strip.SetContent(content);
            }

            string tooltip = connection == null ? "RDPLauncher: no computer added" : "Remote Desktop: " + connection.Name;
            if (tooltip.Length > MaxTooltipLength)
                tooltip = tooltip.Substring(0, MaxTooltipLength - 1) + "…";
            if (trayIcon.Text != tooltip)
                trayIcon.Text = tooltip;
        }

        /// <summary>After switching to (or adding or removing) a connection.</summary>
        void ConnectionChanged()
        {
            connectionAddress = settings.CurrentConnection?.ReadAddress();
            RefreshWidget();
        }

        void ConnectWithButton(int button)
        {
            Connection connection = settings.CurrentConnection;
            if (connection == null)
            {
                AddComputer();
                return;
            }
            if (button >= 0 && button < buttons.Count)
                StartSession(connection, buttons[button].ToList(), button);
        }

        /// <summary>Starts Remote Desktop full screen on the chosen displays.</summary>
        async void StartSession(Connection connection, List<string> chosenKeys, int button)
        {
            if (starting)
                return;
            starting = true;
            busyButton = button;
            RefreshWidget();
            try
            {
                List<LocalDisplay> all = displays;
                List<LocalDisplay> ordered = DisplayChoices.InSessionOrder(all, chosenKeys);
                if (ordered.Count >= all.Count)
                {
                    // All of them, which needs no IDs.
                    SessionLauncher.StartFullScreen(connection, null);
                }
                else
                {
                    Dictionary<string, int> ids = await DisplayIds(all);
                    SessionLauncher.StartFullScreen(connection, MstscDisplayIds.InOrder(ordered, ids, out _));
                }
                await Task.Delay(StartCooldownMilliseconds);
            }
            catch (Exception ex)
            {
                popup.ShowMessage("Couldn't start Remote Desktop", ex.Message);
            }
            finally
            {
                starting = false;
                busyButton = -1;
                RefreshWidget();
            }
        }

        /// <summary>
        /// Remote Desktop's display IDs: the remembered ones, or else read from "mstsc /l" and
        /// remembered. When they can't be read, says so once and leaves them to be guessed.
        /// </summary>
        async Task<Dictionary<string, int>> DisplayIds(List<LocalDisplay> all)
        {
            string outputsKey = LocalDisplay.OutputsKey(all);
            Dictionary<string, int> ids = settings.GetDisplayIds(outputsKey);
            if (MstscDisplayIds.CoversAll(ids, all) || unreadableOutputs.Contains(outputsKey))
                return ids;

            ids = await Task.Run(() => MstscDisplayIds.Query(all));
            if (MstscDisplayIds.CoversAll(ids, all))
            {
                settings.SetDisplayIds(outputsKey, ids);
                SaveSettings();
            }
            else
            {
                unreadableOutputs.Add(outputsKey);
                popup.ShowMessage("Couldn't read Remote Desktop's display numbers",
                    @"Guessed them. Details: %LOCALAPPDATA%\RDPLauncher\display-numbers.log");
            }
            return ids;
        }

        /// <summary>Forgets the display IDs for these displays, so they're read again on the next connection.</summary>
        void ReadDisplayIdsAgain()
        {
            string outputsKey = LocalDisplay.OutputsKey(displays);
            settings.SetDisplayIds(outputsKey, null);
            unreadableOutputs.Remove(outputsKey);
            SaveSettings();
        }

        /// <summary>Adds the display to the button's displays, or takes it off (but never the last one).</summary>
        void ToggleDisplay(int button, string displayKey)
        {
            if (button < 0 || button >= buttons.Count || displays.All(d => d.Key != displayKey))
                return;
            List<string> choice = buttons[button];
            if (choice.Contains(displayKey))
            {
                if (choice.Count == 1)
                    return;
                choice.Remove(displayKey);
            }
            else
            {
                choice.Add(displayKey);
            }
            settings.SetDisplayButtons(setupKey, buttons);
            SaveSettings();
            RefreshWidget();
        }

        void ResetDisplayButtons()
        {
            settings.SetDisplayButtons(setupKey, null);
            buttons = DisplayChoices.Defaults(displays);
            SaveSettings();
            RefreshWidget();
        }

        void StepConnection(int steps)
        {
            List<Connection> connections = settings.Connections;
            if (connections.Count < 2)
                return;
            int index = Math.Max(0, connections.IndexOf(settings.CurrentConnection));
            int next = ((index + steps) % connections.Count + connections.Count) % connections.Count;
            SelectConnection(connections[next]);
        }

        void SelectConnection(Connection connection)
        {
            settings.CurrentConnectionKey = connection.Key;
            SaveSettings();
            ConnectionChanged();
        }

        void ShowMenuFromStrip(ContextMenuStrip stripMenu)
        {
            // Like the tray icon does, take the foreground first so the menu closes when clicking elsewhere.
            // Open it above the widget, which keeps itself on top and would otherwise cover the menu.
            NativeMethods.SetForegroundWindow(strip.Handle);
            stripMenu.Show(new Point(Cursor.Position.X, strip.Top), ToolStripDropDownDirection.AboveRight);
        }

        void RebuildMenu()
        {
            DisposeMenuItems(menu.Items);
            Connection current = settings.CurrentConnection;
            var computers = new ToolStripMenuItem("Computers");
            AddConnectionItems(computers.DropDownItems);
            menu.Items.Add(computers);
            if (current != null)
            {
                menu.Items.Add("Rename " + current.Name + "…", null, (sender, e) => RenameConnection(current));
                string editLabel = current.RdpFile != null ? "Edit " + Path.GetFileName(current.RdpFile) + "…" : "Default Remote Desktop settings…";
                menu.Items.Add(editLabel, null, (sender, e) => EditConnection(current));
            }
            menu.Items.Add(new ToolStripMenuItem("Reset display buttons", null, (sender, e) => ResetDisplayButtons())
            {
                Enabled = setupKey != null && settings.GetDisplayButtons(setupKey) != null,
            });
            menu.Items.Add(new ToolStripMenuItem("Read display numbers again", null, (sender, e) => ReadDisplayIdsAgain())
            {
                ToolTipText = "If Remote Desktop opens on the wrong displays, the next connection asks it for its display numbers again.",
                Enabled = displays.Count > 1,
            });

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Toggle("Show on taskbar", settings.ShowOnTaskbar, value =>
            {
                settings.ShowOnTaskbar = value;
                strip.SetShown(value);
            }));
            menu.Items.Add(Toggle("Start with Windows", StartupRegistration.IsEnabled(), StartupRegistration.SetEnabled));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (sender, e) => ExitThread());
        }

        /// <summary>The computers to pick from, and adding and removing them.</summary>
        void AddConnectionItems(ToolStripItemCollection items)
        {
            Connection current = settings.CurrentConnection;
            foreach (Connection connection in settings.Connections)
            {
                Connection choice = connection;
                string label = connection.Host != null && connection.Name != connection.Host ? connection.Name + "  (" + connection.Host + ")" : connection.Name;
                var item = new ToolStripMenuItem(label) { Checked = current != null && connection.Key == current.Key };
                if (connection.RdpFile != null)
                    item.ToolTipText = connection.RdpFile;
                item.Click += (sender, e) => SelectConnection(choice);
                items.Add(item);
            }
            if (settings.Connections.Count > 0)
                items.Add(new ToolStripSeparator());

            items.Add("Add a computer…", null, (sender, e) => AddComputer());
            items.Add("Add a saved .rdp file…", null, (sender, e) => AddRdpFile());

            List<string> recent = Connection.RecentComputers()
                .Where(computer => settings.Connections.All(c => !string.Equals(c.Host, computer, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (recent.Count > 0)
            {
                var recentItem = new ToolStripMenuItem("Add a recent computer");
                foreach (string computer in recent)
                {
                    string host = computer;
                    recentItem.DropDownItems.Add(host, null, (sender, e) => AddConnection(Connection.ForHost(host)));
                }
                items.Add(recentItem);
            }

            if (current != null)
            {
                items.Add(new ToolStripSeparator());
                items.Add("Remove " + current.Name + " from the list", null, (sender, e) =>
                {
                    settings.RemoveConnection(current);
                    SaveSettings();
                    ConnectionChanged();
                });
            }
        }

        void AddComputer()
        {
            using (var dialog = new InputDialog("Add a computer",
                "Computer name or IP address, as you'd type it in Remote Desktop Connection\n(for example workpc, workpc.contoso.com or 192.168.1.20:3390):",
                "Add", "", Connection.RecentComputers(), allowSpaces: false, allowEmpty: false))
            {
                if (dialog.ShowDialog() == DialogResult.OK && dialog.Value.Length > 0)
                    AddConnection(Connection.ForHost(dialog.Value));
            }
        }

        /// <summary>Names the connection on the widget; an empty name goes back to showing its address.</summary>
        void RenameConnection(Connection connection)
        {
            string address = connection.ReadAddress() ?? connection.Name;
            using (var dialog = new InputDialog("Rename " + connection.Name,
                "Name to show for " + address + " (leave it empty to show just the address):",
                "Rename", connection.CustomName ?? "", null, allowSpaces: true, allowEmpty: true))
            {
                if (dialog.ShowDialog() != DialogResult.OK)
                    return;
                connection.CustomName = dialog.Value.Length > 0 ? dialog.Value : null;
                SaveSettings();
                ConnectionChanged();
            }
        }

        void AddRdpFile()
        {
            using (var owner = new DialogOwner())
            using (var dialog = new OpenFileDialog
            {
                Title = "Add a saved .rdp file",
                Filter = "Remote Desktop connections (*.rdp)|*.rdp|All files (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            })
            {
                if (dialog.ShowDialog(owner) == DialogResult.OK)
                    AddConnection(Connection.ForFile(dialog.FileName));
            }
        }

        void AddConnection(Connection connection)
        {
            settings.AddConnection(connection);
            SaveSettings();
            ConnectionChanged();
        }

        void EditConnection(Connection connection)
        {
            try
            {
                SessionLauncher.Edit(connection);
            }
            catch (Exception ex)
            {
                popup.ShowMessage("Couldn't open Remote Desktop Connection", ex.Message);
            }
        }

        // Disposes the previous menu's items.
        static void DisposeMenuItems(ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items.Cast<ToolStripItem>().ToArray())
            {
                if (item is ToolStripDropDownItem dropDownItem)
                    DisposeMenuItems(dropDownItem.DropDownItems);
                item.Dispose();
            }
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

        protected override void ExitThreadCore()
        {
            refreshTimer.Stop();
            strip.SetShown(false);
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
                connectionsMenu.Dispose();
                popup.Dispose();
                strip.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// An invisible, always-on-top window to open a file dialog over, so the dialog comes up in
        /// front rather than behind whatever was active before the menu.
        /// </summary>
        sealed class DialogOwner : Form
        {
            public DialogOwner()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                // In the middle of the screen with the mouse, since the dialog centers itself on its owner.
                Rectangle workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
                Location = new Point(workArea.Left + workArea.Width / 2, workArea.Top + workArea.Height / 2);
                Size = new Size(1, 1);
                TopMost = true;
                Opacity = 0;
                Show();
                Activate();
            }
        }
    }
}
