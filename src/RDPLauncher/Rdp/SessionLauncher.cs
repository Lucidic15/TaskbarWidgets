using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace RDPLauncher.Rdp
{
    /// <summary>
    /// Starts Remote Desktop Connection (mstsc.exe) on the chosen displays. It writes a copy of the
    /// connection's .rdp file (or of Default.rdp, for a computer name) with the display settings
    /// changed, and opens that, so the original file is never edited.
    /// </summary>
    internal static class SessionLauncher
    {
        static readonly string SessionFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RDPLauncher", "Sessions");

        static readonly string[] DisplaySettings = { "screen mode id", "use multimon", "span monitors", "selectedmonitors" };

        public static string MstscPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mstsc.exe");

        /// <summary>
        /// Starts a full-screen session on the displays with the given mstsc IDs, the session's main
        /// display first; null for all displays.
        /// </summary>
        public static void StartFullScreen(Connection connection, IReadOnlyList<int> displayIds)
        {
            RdpFile file = LoadSettings(connection);
            file.DropSignatureIfCovers(DisplaySettings);
            file.Set("screen mode id", "i", "2");
            file.Set("use multimon", "i", "1");
            file.Set("span monitors", "i", "0");
            if (displayIds == null)
                file.Remove("selectedmonitors");
            else
                file.Set("selectedmonitors", "s", string.Join(",", displayIds));
            Start(connection, file);
        }

        /// <summary>Starts a session in a window.</summary>
        public static void StartWindowed(Connection connection)
        {
            RdpFile file = LoadSettings(connection);
            file.DropSignatureIfCovers(DisplaySettings);
            file.Set("screen mode id", "i", "1");
            file.Set("use multimon", "i", "0");
            file.Remove("selectedmonitors");
            Start(connection, file);
        }

        /// <summary>
        /// Opens the connection's settings in Remote Desktop Connection, to change the ones other than
        /// displays: its .rdp file, or for a computer name the defaults (Default.rdp).
        /// </summary>
        public static void Edit(Connection connection)
        {
            string path = connection.RdpFile ?? DefaultRdpPath;
            // Without a Default.rdp yet, just open Remote Desktop Connection; it saves one on connecting.
            string arguments = File.Exists(path) ? "/edit " + Quote(path) : "";
            using (Process.Start(new ProcessStartInfo(MstscPath, arguments) { UseShellExecute = false }))
            {
            }
        }

        /// <summary>
        /// Where Remote Desktop Connection keeps the settings it uses for a computer typed into its
        /// "Computer" box (a hidden file in Documents).
        /// </summary>
        public static string DefaultRdpPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Default.rdp");

        static RdpFile LoadSettings(Connection connection)
        {
            if (connection.RdpFile != null)
                return RdpFile.Load(connection.RdpFile); // Not finding it is worth an error message.

            RdpFile file;
            try
            {
                file = File.Exists(DefaultRdpPath) ? RdpFile.Load(DefaultRdpPath) : RdpFile.Empty();
            }
            catch (Exception)
            {
                file = RdpFile.Empty();
            }
            file.Set("full address", "s", connection.Host);
            // Default.rdp has the user name for whichever computer was used last; use the one
            // Remote Desktop Connection remembers for this computer instead, as it would itself.
            file.Remove("username");
            string userName = UserNameHint(connection.Host);
            if (!string.IsNullOrEmpty(userName))
                file.Set("username", "s", userName);
            return file;
        }

        static string UserNameHint(string host)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Terminal Server Client\Servers\" + host))
                    return key?.GetValue("UsernameHint") as string;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static void Start(Connection connection, RdpFile file)
        {
            // Remote Desktop Connection shows the file name in its title bar, so name it after the connection.
            string path = Path.Combine(SessionFolder, SafeFileName(connection.Name) + ".rdp");
            file.Save(path);
            using (Process.Start(new ProcessStartInfo(MstscPath, Quote(path)) { UseShellExecute = false }))
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
