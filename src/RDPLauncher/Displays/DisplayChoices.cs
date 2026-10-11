using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace RDPLauncher.Displays
{
    /// <summary>Which displays each of the widget's display buttons starts Remote Desktop on.</summary>
    internal static class DisplayChoices
    {
        /// <summary>
        /// One button per number of displays: 1 is the main display, 2 adds the display nearest to
        /// it, and so on up to all of them.
        /// </summary>
        public static List<List<string>> Defaults(IReadOnlyList<LocalDisplay> displays)
        {
            LocalDisplay primary = displays.FirstOrDefault(d => d.IsPrimary) ?? displays.FirstOrDefault();
            if (primary == null)
                return new List<List<string>>();

            List<LocalDisplay> nearestFirst = displays
                .OrderBy(d => d == primary ? 0 : 1)
                .ThenBy(d => Distance(Center(d.Bounds), Center(primary.Bounds)))
                .ThenBy(d => d.Bounds.X)
                .ThenBy(d => d.Bounds.Y)
                .ToList();
            var choices = new List<List<string>>();
            for (int count = 1; count <= nearestFirst.Count; count++)
                choices.Add(nearestFirst.Take(count).Select(d => d.Key).ToList());
            return choices;
        }

        /// <summary>
        /// The chosen displays in the order Remote Desktop gets them. The first becomes the main
        /// display (with the taskbar) in the session: this PC's main display if it's chosen,
        /// otherwise the leftmost chosen one.
        /// </summary>
        public static List<LocalDisplay> InSessionOrder(IReadOnlyList<LocalDisplay> displays, ICollection<string> chosenKeys) =>
            displays
                .Where(d => chosenKeys.Contains(d.Key))
                .OrderBy(d => d.IsPrimary ? 0 : 1)
                .ThenBy(d => d.Bounds.X)
                .ThenBy(d => d.Bounds.Y)
                .ToList();

        /// <summary>Saved buttons for this arrangement of displays, if they all still fit it; otherwise the defaults.</summary>
        public static List<List<string>> ForSetup(IReadOnlyList<LocalDisplay> displays, List<List<string>> saved)
        {
            var keys = new HashSet<string>(displays.Select(d => d.Key));
            if (saved != null && saved.Count > 0 && saved.All(choice => choice.Count > 0 && choice.All(keys.Contains)))
                return saved;
            return Defaults(displays);
        }

        public static string Describe(int count) => count == 1 ? "1 display" : count + " displays";

        static PointF Center(Rectangle bounds) => new PointF(bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f);

        static double Distance(PointF a, PointF b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }
}
