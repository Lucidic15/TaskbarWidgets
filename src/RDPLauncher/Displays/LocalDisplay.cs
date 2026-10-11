using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;

namespace RDPLauncher.Displays
{
    /// <summary>One of this PC's displays, as Windows arranges them (Settings > System > Display).</summary>
    internal sealed class LocalDisplay
    {
        public LocalDisplay(Rectangle bounds, bool isPrimary, string deviceName, int outputIndex)
        {
            Bounds = bounds;
            IsPrimary = isPrimary;
            DeviceName = deviceName;
            OutputIndex = outputIndex;
            Name = "Display";
        }

        /// <summary>Position and size in physical pixels, the same coordinates "mstsc /l" lists.</summary>
        public Rectangle Bounds { get; }

        public bool IsPrimary { get; }

        /// <summary>The GDI device name, like \\.\DISPLAY1.</summary>
        public string DeviceName { get; }

        /// <summary>
        /// Position in Windows' list of display outputs (\\.\DISPLAY1 is 0), counting unused ones.
        /// Remote Desktop's display IDs seem to follow it (they skip numbers the same way), so it's
        /// the guess at the ID when "mstsc /l" can't be read.
        /// </summary>
        public int OutputIndex { get; }

        /// <summary>The monitor's name, like "DELL U2720Q", or "Built-in display".</summary>
        public string Name { get; set; }

        /// <summary>Identifies the display by where it is, which is what matters to Remote Desktop.</summary>
        public string Key => Bounds.Width + "x" + Bounds.Height + "@" + Bounds.X + "," + Bounds.Y;

        public string Resolution => Bounds.Width + " × " + Bounds.Height;

        /// <summary>
        /// The displays, left to right (then top to bottom). Positions are in physical pixels even
        /// where displays use different scaling, so they line up with what Remote Desktop sees.
        /// </summary>
        public static List<LocalDisplay> GetAll()
        {
            var displays = new List<LocalDisplay>();
            Dictionary<string, int> outputIndexes = OutputIndexes();
            // Windows scales monitor coordinates for apps that aren't per-monitor DPI aware; ask
            // for real pixels just while listing them.
            IntPtr previousContext = IntPtr.Zero;
            try
            {
                previousContext = NativeMethods.SetThreadDpiAwarenessContext(NativeMethods.DpiAwarenessContextPerMonitorAwareV2);
            }
            catch (EntryPointNotFoundException)
            {
                // Before Windows 10 1607; the coordinates are scaled but still consistent.
            }

            try
            {
                NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, dc, rect, data) =>
                {
                    var info = new NativeMethods.MonitorInfoEx { Size = Marshal.SizeOf(typeof(NativeMethods.MonitorInfoEx)) };
                    if (NativeMethods.GetMonitorInfo(monitor, ref info))
                    {
                        int outputIndex = info.DeviceName != null && outputIndexes.TryGetValue(info.DeviceName, out int index) ? index : displays.Count;
                        displays.Add(new LocalDisplay(info.Monitor.ToRectangle(), (info.Flags & NativeMethods.MonitorInfoFPrimary) != 0,
                            info.DeviceName, outputIndex));
                    }
                    return true;
                }, IntPtr.Zero);
            }
            finally
            {
                if (previousContext != IntPtr.Zero)
                    NativeMethods.SetThreadDpiAwarenessContext(previousContext);
            }

            return displays.OrderBy(d => d.Bounds.X).ThenBy(d => d.Bounds.Y).ToList();
        }

        static Dictionary<string, int> OutputIndexes()
        {
            var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var device = new NativeMethods.DisplayDevice { Size = Marshal.SizeOf(typeof(NativeMethods.DisplayDevice)) };
            for (uint i = 0; i < 64 && NativeMethods.EnumDisplayDevices(null, i, ref device, 0); i++)
            {
                if (!string.IsNullOrEmpty(device.DeviceName))
                    indexes[device.DeviceName] = (int)i;
                device.Size = Marshal.SizeOf(typeof(NativeMethods.DisplayDevice));
            }
            return indexes;
        }

        /// <summary>Fills in each display's monitor name.</summary>
        public static void LoadNames(IList<LocalDisplay> displays)
        {
            Dictionary<string, string> names = DisplayNames.Get();
            foreach (LocalDisplay display in displays)
            {
                if (display.DeviceName != null && names.TryGetValue(display.DeviceName, out string name))
                    display.Name = name;
            }
        }

        /// <summary>Identifies an arrangement of displays, so display buttons can be remembered for each one.</summary>
        public static string SetupKey(IEnumerable<LocalDisplay> displays) =>
            string.Join(";", displays.Select(d => (d.IsPrimary ? "*" : "") + d.Key).OrderBy(key => key, StringComparer.Ordinal));

        /// <summary>
        /// Identifies the arrangement and which outputs the displays are on: Remote Desktop's display
        /// IDs stay the same while this does, so they're remembered for it.
        /// </summary>
        public static string OutputsKey(IEnumerable<LocalDisplay> displays) =>
            string.Join(";", displays.Select(d => (d.IsPrimary ? "*" : "") + d.Key + "=" + d.DeviceName).OrderBy(key => key, StringComparer.Ordinal));
    }
}
