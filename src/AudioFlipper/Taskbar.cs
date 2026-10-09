using System;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AudioFlipper
{
    /// <summary>Information about the main Windows taskbar.</summary>
    internal static class Taskbar
    {
        static readonly string[] ShellWindowClasses = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

        /// <summary>
        /// Bounds of the main taskbar and of the monitor it's on, when the taskbar is horizontal
        /// and currently on screen (not slid away by auto-hide).
        /// </summary>
        public static bool TryGetVisibleBounds(out Rectangle taskbar, out Rectangle monitor)
        {
            taskbar = Rectangle.Empty;
            monitor = Rectangle.Empty;

            IntPtr window = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (window == IntPtr.Zero || !NativeMethods.IsWindowVisible(window) || !NativeMethods.GetWindowRect(window, out NativeMethods.Rect rect))
                return false;

            taskbar = rect.ToRectangle();
            monitor = Screen.FromHandle(window).Bounds;
            bool horizontal = taskbar.Width > taskbar.Height && taskbar.Height > 0;
            bool onScreen = Rectangle.Intersect(taskbar, monitor).Height >= taskbar.Height - 2;
            return horizontal && onScreen;
        }

        /// <summary>Whether taskbar icons are left-aligned rather than centered (Windows 11 setting).</summary>
        public static bool IconsAreLeftAligned()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"))
                    return key?.GetValue("TaskbarAl") is int alignment && alignment == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Whether a full-screen app (game, video, presentation) is in front on the given monitor.</summary>
        public static bool IsFullScreenAppActive(Rectangle monitor, IntPtr ignoreWindow)
        {
            if (NativeMethods.SHQueryUserNotificationState(out int state) == 0 && state == NativeMethods.QunsRunningD3DFullScreen)
                return true;

            IntPtr window = NativeMethods.GetForegroundWindow();
            if (window == IntPtr.Zero || window == ignoreWindow || !NativeMethods.GetWindowRect(window, out NativeMethods.Rect rect))
                return false;

            // The desktop and the taskbar itself cover the screen but aren't full-screen apps.
            var className = new StringBuilder(256);
            NativeMethods.GetClassName(window, className, className.Capacity);
            if (ShellWindowClasses.Contains(className.ToString()))
                return false;

            return rect.ToRectangle().Contains(monitor);
        }
    }
}
