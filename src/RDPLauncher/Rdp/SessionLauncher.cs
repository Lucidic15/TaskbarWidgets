using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace RDPLauncher.Rdp
{
    /// <summary>
    /// Starts Remote Desktop Connection (mstsc.exe) on the chosen displays.
    /// </summary>
    /// <remarks>
    /// Since April 2026 Windows shows a security warning every time an .rdp file is opened, but not
    /// for a computer typed into Remote Desktop Connection or given with "mstsc /v". So a computer
    /// added by name is started with /v, after setting the displays in the settings Remote Desktop
    /// Connection uses for that (Default.rdp, which its own Display tab changes too). A saved .rdp
    /// file is copied with the displays set and the copy is opened, leaving the original as it was.
    /// </remarks>
    internal static class SessionLauncher
    {
        static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RDPLauncher");

        static readonly string SessionFolder = Path.Combine(AppDataFolder, "Sessions");

        // A copy of Default.rdp from before RDPLauncher first changed it.
        static readonly string DefaultRdpBackupPath = Path.Combine(AppDataFolder, "Default.rdp.backup");

        static readonly string[] DisplaySettings = { "screen mode id", "use multimon", "span monitors", "selectedmonitors" };

        public static string MstscPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mstsc.exe");

        /// <summary>
        /// Where Remote Desktop Connection keeps the settings it uses for a computer typed into its
        /// "Computer" box (a hidden file in Documents).
        /// </summary>
        public static string DefaultRdpPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Default.rdp");

        /// <summary>
        /// Starts a full-screen session on the displays with the given mstsc IDs, the session's main
        /// display first; null for all displays.
        /// </summary>
        public static void StartFullScreen(Connection connection, IReadOnlyList<int> displayIds) =>
            Start(connection, file =>
            {
                file.Set("screen mode id", "i", "2");
                file.Set("use multimon", "i", "1");
                file.Set("span monitors", "i", "0");
                if (displayIds == null)
                    file.Remove("selectedmonitors");
                else
                    file.Set("selectedmonitors", "s", string.Join(",", displayIds));
            });

        /// <summary>Starts a session in a window.</summary>
        public static void StartWindowed(Connection connection) =>
            Start(connection, file =>
            {
                file.Set("screen mode id", "i", "1");
                file.Set("use multimon", "i", "0");
                file.Remove("selectedmonitors");
            });

        /// <summary>
        /// Opens the connection's settings in Remote Desktop Connection, to change the ones other than
        /// displays: its .rdp file, or for a computer name the defaults (Default.rdp).
        /// </summary>
        public static void Edit(Connection connection)
        {
            string path = connection.RdpFile ?? DefaultRdpPath;
            // Without a Default.rdp yet, just open Remote Desktop Connection; it saves one on connecting.
            string arguments = File.Exists(path) ? "/edit " + Quote(path) : "";
            Run(arguments);
        }

        static void Start(Connection connection, Action<RdpFile> setDisplays)
        {
            if (connection.RdpFile != null)
            {
                RdpFile file = RdpFile.Load(connection.RdpFile); // Not finding it is worth an error message.
                file.DropSignatureIfCovers(DisplaySettings);
                setDisplays(file);
                // Remote Desktop Connection shows the file name in its title bar, so name it after the connection.
                string copy = Path.Combine(SessionFolder, SafeFileName(connection.Name) + ".rdp");
                file.Save(copy);
                Run(Quote(copy));
            }
            else
            {
                bool existed = File.Exists(DefaultRdpPath);
                RdpFile defaults = existed ? RdpFile.Load(DefaultRdpPath) : RdpFile.Empty();
                if (existed && !File.Exists(DefaultRdpBackupPath))
                {
                    Directory.CreateDirectory(AppDataFolder);
                    File.Copy(DefaultRdpPath, DefaultRdpBackupPath);
                }
                setDisplays(defaults);
                defaults.Save(DefaultRdpPath);
                if (!existed)
                    File.SetAttributes(DefaultRdpPath, File.GetAttributes(DefaultRdpPath) | FileAttributes.Hidden); // As Remote Desktop Connection keeps it.
                Run("/v:" + connection.Host); // Computer names have no spaces (the add dialog doesn't take them).
            }
        }

        static void Run(string arguments)
        {
            using (Process.Start(new ProcessStartInfo(MstscPath, arguments) { UseShellExecute = false }))
            {
            }
        }

        static string SafeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string safe = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return safe.Length > 0 ? safe : "Remote Desktop";
        }

        static string Quote(string path) => "\"" + path + "\"";
    }
}
