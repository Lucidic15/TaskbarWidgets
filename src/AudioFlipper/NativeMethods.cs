using System;
using System.Runtime.InteropServices;

namespace AudioFlipper
{
    internal static class NativeMethods
    {
        public const int DwmwaWindowCornerPreference = 33;
        public const int DwmwcpRound = 2;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr icon);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    }
}
