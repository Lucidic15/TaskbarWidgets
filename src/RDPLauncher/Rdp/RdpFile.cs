using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RDPLauncher.Rdp
{
    /// <summary>
    /// The settings in an .rdp file: one "name:type:value" line each, like "use multimon:i:1".
    /// Lines that aren't settings are kept as they are.
    /// </summary>
    internal sealed class RdpFile
    {
        readonly List<string> lines;

        RdpFile(IEnumerable<string> lines) => this.lines = lines.ToList();

        public static RdpFile Empty() => new RdpFile(new string[0]);

        /// <summary>Reads an .rdp file. Remote Desktop Connection saves them as UTF-16; others may be UTF-8.</summary>
        public static RdpFile Load(string path) => new RdpFile(File.ReadAllLines(path));

        /// <summary>The value of a setting, or null when the file doesn't have it.</summary>
        public string Get(string name)
        {
            int index = IndexOf(name);
            if (index < 0)
                return null;
            string line = lines[index];
            int typeEnd = line.IndexOf(':', line.IndexOf(':') + 1);
            return typeEnd < 0 ? "" : line.Substring(typeEnd + 1);
        }

        /// <summary>Sets a setting; <paramref name="type"/> is "i" for numbers and "s" for text.</summary>
        public void Set(string name, string type, string value)
        {
            string line = name + ":" + type + ":" + value;
            int index = IndexOf(name);
            if (index >= 0)
                lines[index] = line;
            else
                lines.Add(line);
        }

        public void Remove(string name)
        {
            for (int index = IndexOf(name); index >= 0; index = IndexOf(name))
                lines.RemoveAt(index);
        }

        /// <summary>
        /// A signed file (from an IT department or Azure Virtual Desktop) has a signature covering the
        /// settings named in "signscope". Changing any of those breaks the signature, so drop it: Remote
        /// Desktop Connection then treats the file as unsigned (and asks to confirm the connection)
        /// rather than as tampered with.
        /// </summary>
        public void DropSignatureIfCovers(IEnumerable<string> changedNames)
        {
            string scope = Get("signscope");
            if (scope == null || Get("signature") == null)
                return;
            var covered = new HashSet<string>(scope.Split(',').Select(Normalize));
            if (changedNames.Any(name => covered.Contains(Normalize(name))))
            {
                Remove("signature");
                Remove("signscope");
            }
        }

        /// <summary>Saves the file as UTF-16, like Remote Desktop Connection does, keeping its attributes.</summary>
        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            // Windows won't overwrite a hidden file (as Default.rdp is) by recreating it, so unhide it meanwhile.
            FileAttributes? attributes = File.Exists(path) ? File.GetAttributes(path) : (FileAttributes?)null;
            if (attributes != null && (attributes.Value & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                File.SetAttributes(path, attributes.Value & ~(FileAttributes.Hidden | FileAttributes.System));
            try
            {
                File.WriteAllLines(path, lines, Encoding.Unicode);
            }
            finally
            {
                if (attributes != null)
                    File.SetAttributes(path, attributes.Value);
            }
        }

        int IndexOf(string name)
        {
            string wanted = Normalize(name);
            for (int i = 0; i < lines.Count; i++)
            {
                int separator = lines[i].IndexOf(':');
                if (separator > 0 && Normalize(lines[i].Substring(0, separator)) == wanted)
                    return i;
            }
            return -1;
        }

        // Setting names ignore case, and "signscope" writes them in a different style ("Full Address"
        // for "full address", "UseMultimon" for "use multimon"), so compare them without spaces.
        static string Normalize(string name) => name.Replace(" ", "").Trim().ToLowerInvariant();
    }
}
