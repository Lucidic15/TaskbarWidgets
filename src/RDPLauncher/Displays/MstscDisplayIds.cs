using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;

namespace RDPLauncher.Displays
{
    /// <summary>
    /// Finds the IDs Remote Desktop Connection uses for each display (for the .rdp file's
    /// "selectedmonitors" setting). They aren't the numbers Windows Settings shows and can change
    /// when displays are plugged in, so they're read from "mstsc /l" each time they're needed.
    /// </summary>
    internal static class MstscDisplayIds
    {
        const int TimeoutMilliseconds = 8000;
        const int PollMilliseconds = 20;
        const int UiAutomationAfterMilliseconds = 300;

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
        /// Connection didn't list them. Blocks for up to a few seconds; call it off the UI thread.
        /// </summary>
        public static Dictionary<string, int> Query(IReadOnlyList<LocalDisplay> displays)
        {
            string text = ReadMonitorList();
            if (text == null)
                return null;

            List<Entry> entries = DisplayLine.Matches(text).Cast<Match>()
                .Select(match => new Entry
                {
                    Id = int.Parse(match.Groups[1].Value),
                    Bounds = Rectangle.FromLTRB(int.Parse(match.Groups[4].Value), int.Parse(match.Groups[5].Value),
                        int.Parse(match.Groups[6].Value) + 1, int.Parse(match.Groups[7].Value) + 1),
                })
                .GroupBy(entry => entry.Id).Select(group => group.First())
                .ToList();
            return Match(entries, displays);
        }

        /// <summary>
        /// The IDs for the given displays, in the same order, from <paramref name="ids"/> (which may be
        /// null or incomplete). Displays it doesn't have get their place in Windows' own list, which is
        /// often but not always the same; <paramref name="guessed"/> says whether that happened.
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
                    ordered.Add(display.EnumerationIndex);
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
        /// Runs "mstsc /l", which shows the list in a message box, reads the text from the box and closes it.
        /// </summary>
        static string ReadMonitorList()
        {
            var startInfo = new ProcessStartInfo(Rdp.SessionLauncher.MstscPath, "/l")
            {
                // Asks for the box to start hidden (which .NET Framework only passes on through the
                // shell); the text can be read either way.
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using (Process process = Process.Start(startInfo))
            {
                if (process == null)
                    return null;
                try
                {
                    var firstSeen = new Dictionary<IntPtr, long>();
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    while (stopwatch.ElapsedMilliseconds < TimeoutMilliseconds && !process.HasExited)
                    {
                        foreach (IntPtr window in TopLevelWindowsOf(process.Id))
                        {
                            if (!firstSeen.ContainsKey(window))
                                firstSeen[window] = stopwatch.ElapsedMilliseconds;

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
                        Thread.Sleep(PollMilliseconds);
                    }
                    return null;
                }
                finally
                {
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

        static List<IntPtr> TopLevelWindowsOf(int processId)
        {
            var windows = new List<IntPtr>();
            NativeMethods.EnumWindows((window, data) =>
            {
                NativeMethods.GetWindowThreadProcessId(window, out uint owner);
                if (owner == processId)
                    windows.Add(window);
                return true;
            }, IntPtr.Zero);
            return windows;
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
    }
}
