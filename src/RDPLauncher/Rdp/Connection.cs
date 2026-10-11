using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace RDPLauncher.Rdp
{
    /// <summary>
    /// A computer to connect to: either just its name (using the settings Remote Desktop Connection
    /// remembers in Default.rdp) or a saved .rdp file with its own settings.
    /// </summary>
    internal sealed class Connection
    {
        const string HostPrefix = "host:";
        const string FilePrefix = "file:";

        Connection(string host, string rdpFile)
        {
            Host = host;
            RdpFile = rdpFile;
        }

        /// <summary>The computer name or address, for connections without an .rdp file.</summary>
        public string Host { get; }

        /// <summary>The path of the saved .rdp file, for connections that have one.</summary>
        public string RdpFile { get; }

        /// <summary>The name given to the connection with Rename, or null.</summary>
        public string CustomName { get; set; }

        /// <summary>The name given to it, else the .rdp file's name, else the computer name or address.</summary>
        public string Name =>
            !string.IsNullOrWhiteSpace(CustomName) ? CustomName
            : RdpFile != null ? Path.GetFileNameWithoutExtension(RdpFile)
            : Host;

        /// <summary>Identifies the connection (whatever it's called) in the settings file.</summary>
        public string Key => RdpFile != null ? FilePrefix + RdpFile : HostPrefix + Host;

        public static Connection ForHost(string host) => new Connection(host.Trim(), null);

        public static Connection ForFile(string path) => new Connection(null, Path.GetFullPath(path));

        public static Connection FromKey(string key)
        {
            if (key.StartsWith(HostPrefix, StringComparison.OrdinalIgnoreCase) && key.Length > HostPrefix.Length)
                return ForHost(key.Substring(HostPrefix.Length));
            if (key.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase) && key.Length > FilePrefix.Length)
                return new Connection(null, key.Substring(FilePrefix.Length));
            return null;
        }

        /// <summary>
        /// The computer the connection goes to: the name itself, or the .rdp file's "full address".
        /// Null if the file can't be read.
        /// </summary>
        public string ReadAddress()
        {
            if (RdpFile == null)
                return Host;
            try
            {
                string address = Rdp.RdpFile.Load(RdpFile).Get("full address");
                return string.IsNullOrWhiteSpace(address) ? null : address.Trim();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Computers recently connected to with Remote Desktop Connection, newest first (the list
        /// its "Computer" box suggests).
        /// </summary>
        public static List<string> RecentComputers()
        {
            var computers = new List<string>();
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Terminal Server Client\Default"))
                {
                    if (key == null)
                        return computers;
                    for (int i = 0; i < 10; i++)
                    {
                        if (key.GetValue("MRU" + i) is string computer && !string.IsNullOrWhiteSpace(computer))
                            computers.Add(computer.Trim());
                    }
                }
            }
            catch (Exception)
            {
                // No suggestions then.
            }
            return computers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
