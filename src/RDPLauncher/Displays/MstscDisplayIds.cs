using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;

namespace RDPLauncher.Displays
{
    /// <summary>
    /// Finds the IDs Remote Desktop Connection uses for each display (for the .rdp "selectedmonitors"
    /// setting). They aren't the numbers Windows Settings shows, so they're read from "mstsc /l";
    /// RDPLauncher remembers them until the displays change.
    /// </summary>
    internal static class MstscDisplayIds
    {
        const int TimeoutMilliseconds = 5000;
        const int PollMilliseconds = 20;
        const int UiAutomationAfterMilliseconds = 300;
        const string DialogClass = "#32770";

        /// <summary>What was found when "mstsc /l" couldn't be read, to see why.</summary>
        public static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RDPLauncher", "display-numbers.log");

        // "mstsc /l" shows lines like "0: 1920 x 1080; (0, 0, 1919, 1079)": the ID, the size, and
        // the left, top, right and bottom pixels.
        static readonly Regex DisplayLine = new Regex(
            @"(\d+)\s*:\s*(\d+)\s*[x×]\s*(\d+)\s*;\s*\(\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*\)",
            RegexOptions.CultureInvariant);

        sealed class Entry
        {
            public int Id;
            public Rectangle Bounds;
        }

        /// <summary>
        /// The mstsc ID of each display, by <see cref="LocalDisplay.Key"/>, or null if Remote Desktop
        /// Connection's list couldn't be read (see <see cref="LogPath"/>). Takes up to a few seconds;
        /// call it off the UI thread.
        /// </summary>
        public static Dictionary<string, int> Query(IReadOnlyList<LocalDisplay> displays)
        {
            var log = new StringBuilder();
            string text = ReadMonitorList(log);
            Dictionary<string, int> ids = null;
            if (text != null)
            {
                List<Entry> entries = DisplayLine.Matches(text).Cast<Match>()
                    .Select(match => new Entry
                    {
                        Id = int.Parse(match.Groups[1].Value),
                        Bounds = Rectangle.FromLTRB(int.Parse(match.Groups[4].Value), int.Parse(match.Groups[5].Value),
                            int.Parse(match.Groups[6].Value) + 1, int.Parse(match.Groups[7].Value) + 1),
                    })
                    .GroupBy(entry => entry.Id).Select(group => group.First())
                    .ToList();
                ids = Match(entries, displays);
                if (ids == null || ids.Count < displays.Count)
                    log.AppendLine("Read the list, but it doesn't match the displays:").AppendLine(text);
            }

            if (ids == null || ids.Count < displays.Count)
            {
                log.AppendLine().AppendLine("Displays:");
                foreach (LocalDisplay display in displays)
                    log.AppendLine("  " + display.DeviceName + " (output " + display.OutputIndex + "): " + display.Key + (display.IsPrimary ? ", main" : ""));
                WriteLog(log);
            }
            return ids;
        }

        /// <summary>Whether <paramref name="ids"/> has an ID for every display.</summary>
        public static bool CoversAll(IReadOnlyDictionary<string, int> ids, IEnumerable<LocalDisplay> displays) =>
            ids != null && displays.All(display => ids.ContainsKey(display.Key));

        /// <summary>
        /// The IDs for the given displays, in the same order, from <paramref name="ids"/> (which may be
        /// null or incomplete). Displays it doesn't have get their place in Windows' list of display
        /// outputs, which Remote Desktop's IDs seem to follow; <paramref name="guessed"/> says whether
        /// that happened.
        /// </summary>
        public static List<int> InOrder(IEnumerable<LocalDisplay> displays, IReadOnlyDictionary<string, int> ids, out bool guessed)
        {
            guessed = false;
            var ordered = new List<int>();
            foreach (LocalDisplay display in displays)
            {
                if (ids != null && ids.TryGetValue(display.Key, out int id))
                {
                    ordered.Add(id);
                }
                else
                {
                    ordered.Add(display.OutputIndex);
                    guessed = true;
                }
            }
            return ordered;
        }

        /// <summary>
        /// Pairs mstsc's list with the displays by position. When the positions don't line up exactly
        /// (scaling can make them differ) but there are as many of each, pairs them in left-to-right order.
        /// </summary>
        static Dictionary<string, int> Match(List<Entry> entries, IReadOnlyList<LocalDisplay> displays)
        {
            var ids = new Dictionary<string, int>();
            foreach (LocalDisplay display in displays)
            {
                Entry entry = entries.FirstOrDefault(e => e.Bounds.Location == display.Bounds.Location);
                if (entry != null)
                    ids[display.Key] = entry.Id;
            }
            if (ids.Count == displays.Count && ids.Values.Distinct().Count() == ids.Count)
                return ids;

            if (entries.Count != displays.Count)
                return ids.Count > 0 ? ids : null;
            ids.Clear();
            List<Entry> ordered = entries.OrderBy(e => e.Bounds.X).ThenBy(e => e.Bounds.Y).ToList();
            List<LocalDisplay> orderedDisplays = displays.OrderBy(d => d.Bounds.X).ThenBy(d => d.Bounds.Y).ToList();
            for (int i = 0; i < ordered.Count; i++)
                ids[orderedDisplays[i].Key] = ordered[i].Id;
            return ids;
        }

        /// <summary>
        /// Runs "mstsc /l", which shows the list in a message box, moves the box off the screen, reads
        /// its text and closes it. Null if it couldn't, with what it found in <paramref name="log"/>.
        /// </summary>
        static string ReadMonitorList(StringBuilder log)
        {
            HashSet<int> alreadyRunning = MstscProcessIds();
            var checkedProcesses = new Dictionary<uint, bool>();
            var firstSeen = new Dictionary<IntPtr, long>();

            using (Process process = Process.Start(new ProcessStartInfo(Rdp.SessionLauncher.MstscPath, "/l") { UseShellExecute = false }))
            {
                if (process == null)
                {
                    log.AppendLine("mstsc /l didn't start.");
                    return null;
                }

                // The box normally belongs to the process just started, but accept a dialog from any
                // mstsc that wasn't already running, in case it hands the work over.
                bool IsCandidate(IntPtr window, uint owner)
                {
                    if (owner == process.Id)
                        return true;
                    if (alreadyRunning.Contains((int)owner) || ClassOf(window) != DialogClass)
                        return false;
                    if (!checkedProcesses.TryGetValue(owner, out bool isMstsc))
                        checkedProcesses[owner] = isMstsc = IsMstsc(owner);
                    return isMstsc;
                }

                try
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    while (stopwatch.ElapsedMilliseconds < TimeoutMilliseconds)
                    {
                        foreach (IntPtr window in TopLevelWindows(IsCandidate))
                        {
                            if (!firstSeen.ContainsKey(window))
                            {
                                firstSeen[window] = stopwatch.ElapsedMilliseconds;
                                // Out of sight while it's read; it stays "shown" so it can still be read.
                                if (ClassOf(window) == DialogClass)
                                {
                                    NativeMethods.SetWindowPos(window, IntPtr.Zero, -32000, -32000, 0, 0,
                                        NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
                                }
                            }

                            string text = ReadWindowTexts(window);
                            if (DisplayLine.IsMatch(text))
                                return text;

                            // A task dialog draws its text itself, where only UI Automation can read it.
                            if (stopwatch.ElapsedMilliseconds - firstSeen[window] >= UiAutomationAfterMilliseconds)
                            {
                                text = ReadAutomationNames(window);
                                if (DisplayLine.IsMatch(text))
                                    return text;
                            }
                        }
                        if (process.HasExited && firstSeen.Count == 0 && stopwatch.ElapsedMilliseconds > 1500)
                            break;
                        Thread.Sleep(PollMilliseconds);
                    }

                    DescribeFailure(log, process, firstSeen.Keys);
                    return null;
                }
                finally
                {
                    foreach (IntPtr window in firstSeen.Keys)
                        NativeMethods.PostMessage(window, NativeMethods.WmClose, IntPtr.Zero, IntPtr.Zero);
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                            process.WaitForExit(2000);
                        }
                    }
                    catch (Exception)
                    {
                        // It has exited on its own in the meantime.
                    }
                }
            }
        }

        static void DescribeFailure(StringBuilder log, Process process, IEnumerable<IntPtr> windows)
        {
            log.AppendLine("Couldn't read the list from \"mstsc /l\" (" + DateTime.Now.ToString("s") + ").");
            log.AppendLine("Windows " + Environment.OSVersion.Version + ", mstsc " + FileVersion(Rdp.SessionLauncher.MstscPath));
            log.AppendLine(process.HasExited ? "mstsc exited with code " + process.ExitCode + "." : "mstsc was still running.");
            if (!windows.Any())
                log.AppendLine("It showed no windows.");
            foreach (IntPtr window in windows)
            {
                NativeMethods.GetWindowThreadProcessId(window, out uint owner);
                log.AppendLine("Window " + window.ToInt64().ToString("X") + " of process " + owner + ", class " + ClassOf(window)
                    + (NativeMethods.IsWindowVisible(window) ? ", visible" : ", hidden") + ":");
                log.AppendLine("  Title: " + NativeMethods.GetWindowTextOf(window));
                NativeMethods.EnumChildWindows(window, (child, data) =>
                {
                    log.AppendLine("  " + ClassOf(child) + ": " + NativeMethods.GetWindowTextOf(child));
                    return true;
                }, IntPtr.Zero);
                log.AppendLine("  UI Automation: " + ReadAutomationNames(window).Replace("\n", " | "));
            }
        }

        static void WriteLog(StringBuilder log)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                File.WriteAllText(LogPath, log.ToString());
            }
            catch (Exception)
            {
                // Only for finding out what went wrong.
            }
        }

        static HashSet<int> MstscProcessIds()
        {
            var ids = new HashSet<int>();
            foreach (Process process in Process.GetProcessesByName("mstsc"))
            {
                using (process)
                    ids.Add(process.Id);
            }
            return ids;
        }

        static bool IsMstsc(uint processId)
        {
            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                    return string.Equals(process.ProcessName, "mstsc", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        static List<IntPtr> TopLevelWindows(Func<IntPtr, uint, bool> include)
        {
            var windows = new List<IntPtr>();
            NativeMethods.EnumWindows((window, data) =>
            {
                NativeMethods.GetWindowThreadProcessId(window, out uint owner);
                if (include(window, owner))
                    windows.Add(window);
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        static string ClassOf(IntPtr window)
        {
            var className = new StringBuilder(256);
            NativeMethods.GetClassName(window, className, className.Capacity);
            return className.ToString();
        }

        /// <summary>The text of a window and of all the controls in it (a message box keeps its text in a label).</summary>
        static string ReadWindowTexts(IntPtr window)
        {
            var text = new StringBuilder(NativeMethods.GetWindowTextOf(window));
            NativeMethods.EnumChildWindows(window, (child, data) =>
            {
                text.Append('\n').Append(NativeMethods.GetWindowTextOf(child));
                return true;
            }, IntPtr.Zero);
            return text.ToString();
        }

        static string ReadAutomationNames(IntPtr window)
        {
            try
            {
                var text = new StringBuilder();
                AutomationElement root = AutomationElement.FromHandle(window);
                foreach (AutomationElement element in root.FindAll(TreeScope.Descendants, Condition.TrueCondition))
                    text.Append('\n').Append(element.Current.Name);
                return text.ToString();
            }
            catch (Exception)
            {
                return "";
            }
        }

        static string FileVersion(string path)
        {
            try
            {
                return FileVersionInfo.GetVersionInfo(path).FileVersion;
            }
            catch (Exception)
            {
                return "unknown";
            }
        }
    }
}
