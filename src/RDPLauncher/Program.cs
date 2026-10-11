using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using RDPLauncher.Displays;
using RDPLauncher.Rdp;

namespace RDPLauncher
{
    internal static class Program
    {
        const string Usage = "Usage: RDPLauncher.exe [--connect <computer or name>] [--displays <number>|all|window]";

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0)
                return ConnectOnce(args);

            using (new Mutex(true, @"Local\RDPLauncher.SingleInstance", out bool isFirstInstance))
            {
                if (!isFirstInstance)
                    return 0; // Already running in the tray.

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var app = new TrayApp())
                    Application.Run(app);
            }
            return 0;
        }

        /// <summary>
        /// "RDPLauncher.exe --connect workpc --displays 2" starts Remote Desktop and exits, without the
        /// widget. --connect takes a computer from the list (by name) or any computer name, and
        /// defaults to the computer picked in the widget; --displays defaults to all. Useful for
        /// shortcuts with a keyboard hotkey.
        /// </summary>
        static int ConnectOnce(string[] args)
        {
            try
            {
                string computer = ValueAfter(args, "--connect");
                string displaysArgument = ValueAfter(args, "--displays") ?? "all";
                if (computer == null && ValueAfter(args, "--displays") == null)
                    return Fail(Usage);

                Settings settings = Settings.Load();
                Connection connection = computer == null ? settings.CurrentConnection : FindConnection(settings, computer);
                if (connection == null)
                    return Fail("Add a computer in RDPLauncher first, or name one with --connect.");

                if (string.Equals(displaysArgument, "window", StringComparison.OrdinalIgnoreCase))
                {
                    SessionLauncher.StartWindowed(connection);
                    return 0;
                }

                List<LocalDisplay> displays = LocalDisplay.GetAll();
                int count;
                if (string.Equals(displaysArgument, "all", StringComparison.OrdinalIgnoreCase))
                    count = displays.Count;
                else if (!int.TryParse(displaysArgument, out count) || count < 1)
                    return Fail(Usage);

                if (count >= displays.Count)
                {
                    SessionLauncher.StartFullScreen(connection, null);
                    return 0;
                }

                // The widget's button for that many displays, or the default choice.
                List<List<string>> buttons = DisplayChoices.ForSetup(displays, settings.GetDisplayButtons(LocalDisplay.SetupKey(displays)));
                List<string> chosen = buttons.FirstOrDefault(b => b.Count == count) ?? DisplayChoices.Defaults(displays)[count - 1];
                List<LocalDisplay> ordered = DisplayChoices.InSessionOrder(displays, chosen);
                // Remote Desktop's display IDs as the widget remembered them, or else read and remembered.
                string outputsKey = LocalDisplay.OutputsKey(displays);
                Dictionary<string, int> ids = settings.GetDisplayIds(outputsKey);
                if (!MstscDisplayIds.CoversAll(ids, displays))
                {
                    ids = MstscDisplayIds.Query(displays);
                    if (MstscDisplayIds.CoversAll(ids, displays))
                    {
                        settings.SetDisplayIds(outputsKey, ids);
                        TrySave(settings);
                    }
                }
                SessionLauncher.StartFullScreen(connection, MstscDisplayIds.InOrder(ordered, ids, out _));
                return 0;
            }
            catch (Exception ex)
            {
                return Fail("Couldn't start Remote Desktop: " + ex.Message);
            }
        }

        /// <summary>A connection from the list by its name or computer, else a computer name or .rdp file as given.</summary>
        static Connection FindConnection(Settings settings, string computer)
        {
            Connection saved = settings.Connections.FirstOrDefault(c =>
                string.Equals(c.Name, computer, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Host, computer, StringComparison.OrdinalIgnoreCase));
            if (saved != null)
                return saved;
            if (computer.EndsWith(".rdp", StringComparison.OrdinalIgnoreCase) && File.Exists(computer))
                return Connection.ForFile(computer);
            return Connection.ForHost(computer);
        }

        static void TrySave(Settings settings)
        {
            try
            {
                settings.Save();
            }
            catch (Exception)
            {
                // They'll be read again next time.
            }
        }

        static string ValueAfter(string[] args, string option)
        {
            int index = Array.FindIndex(args, arg => string.Equals(arg, option, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        static int Fail(string message)
        {
            MessageBox.Show(message, "RDPLauncher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
    }
}
