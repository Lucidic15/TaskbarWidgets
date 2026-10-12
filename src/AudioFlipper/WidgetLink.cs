using System;
using System.Drawing;
using Microsoft.Win32;

namespace AudioFlipper
{
    /// <summary>
    /// Links AudioFlipper's and RDPLauncher's taskbar widgets (this file is in both apps). Linked,
    /// they sit side by side as one widget, in the order chosen, with a divider between them, and
    /// dragging either moves both.
    /// </summary>
    /// <remarks>
    /// The link (on or off, which app is on the left, and where the pair is) is kept in
    /// HKCU\Software\TaskbarWidgetLink, which both apps read. Each widget window is titled
    /// "&lt;app&gt; taskbar widget" so the other can find it, read its width and post it a message
    /// whenever something changes; each also re-checks on its own placement timer.
    /// </remarks>
    internal static class WidgetLink
    {
        public const string Self = "AudioFlipper";
        public const string Partner = "RDPLauncher";

        const string KeyPath = @"Software\TaskbarWidgetLink";
        static int changedMessage;

        /// <summary>The link as both apps see it.</summary>
        public sealed class State
        {
            public bool Linked;

            /// <summary>The app on the left: "AudioFlipper" or "RDPLauncher".</summary>
            public string LeftApp = "";

            /// <summary>The pair's left edge in pixels from the taskbar's left edge; negative for the default.</summary>
            public int Offset = -1;

            public bool SelfOnLeft => LeftApp != Partner;
        }

        public static string WindowTitle(string app) => app + " taskbar widget";

        /// <summary>Posted to the other widget when the link, or this widget's size or place, changes.</summary>
        public static int ChangedMessage =>
            changedMessage != 0 ? changedMessage : (changedMessage = NativeMethods.RegisterWindowMessage("TaskbarWidgetLink.Changed"));

        public static State Read()
        {
            var state = new State();
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    if (key == null)
                        return state;
                    state.Linked = key.GetValue("Linked") is int linked && linked != 0;
                    state.LeftApp = key.GetValue("Left") as string ?? "";
                    if (key.GetValue("Offset") is string offset && int.TryParse(offset, out int value))
                        state.Offset = value;
                }
            }
            catch (Exception)
            {
                // Not linked, then.
            }
            return state;
        }

        public static void Write(bool linked, string leftApp, int offset)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                key.SetValue("Linked", linked ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("Left", leftApp ?? "");
                key.SetValue("Offset", offset.ToString());
            }
        }

        /// <summary>Moves the pair (while it's dragged).</summary>
        public static void WriteOffset(int offset)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath))
                    key.SetValue("Offset", offset.ToString());
            }
            catch (Exception)
            {
                // It moves back on the next placement; nothing lost.
            }
        }

        /// <summary>Whether the other app is running with a version that can link.</summary>
        public static bool PartnerRunning() => NativeMethods.FindWindow(null, WindowTitle(Partner)) != IntPtr.Zero;

        /// <summary>The other app's widget, when it's on the taskbar right now.</summary>
        public static bool TryGetPartnerBounds(out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            IntPtr window = NativeMethods.FindWindow(null, WindowTitle(Partner));
            if (window == IntPtr.Zero || !NativeMethods.IsWindowVisible(window) || !NativeMethods.GetWindowRect(window, out NativeMethods.Rect rect))
                return false;
            bounds = rect.ToRectangle();
            return bounds.Width > 0;
        }

        public static void NotifyPartner()
        {
            IntPtr window = NativeMethods.FindWindow(null, WindowTitle(Partner));
            if (window != IntPtr.Zero)
                NativeMethods.PostMessage(window, (uint)ChangedMessage, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>
        /// Where a linked widget goes: the pair starts at <paramref name="groupOffset"/> (kept on the
        /// taskbar), with the left app first. Both apps work it out the same way from the same numbers,
        /// so they meet without a gap. Without the other widget showing, this one takes the pair's place.
        /// </summary>
        /// <returns>This widget's left edge; <paramref name="groupLeft"/> is the pair's.</returns>
        public static int PlaceInGroup(int barLeft, int barWidth, int ownWidth, int partnerWidth, bool selfOnLeft,
            int groupOffset, out int groupLeft)
        {
            int total = Math.Min(ownWidth + partnerWidth, barWidth);
            groupLeft = barLeft + Math.Max(0, Math.Min(groupOffset, barWidth - total));
            return selfOnLeft || partnerWidth == 0 ? groupLeft : Math.Min(groupLeft + partnerWidth, barLeft + barWidth - ownWidth);
        }
    }
}
