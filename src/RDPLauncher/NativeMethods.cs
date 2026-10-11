using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace RDPLauncher
{
    internal static class NativeMethods
    {
        public const int DwmwaWindowCornerPreference = 33;
        public const int DwmwcpRound = 2;

        public static readonly IntPtr HwndTopmost = new IntPtr(-1);
        public const uint SwpNoSize = 0x1;
        public const uint SwpNoMove = 0x2;
        public const uint SwpNoZOrder = 0x4;
        public const uint SwpNoActivate = 0x10;

        public const uint EventSystemForeground = 0x3;
        public const uint WinEventOutOfContext = 0x0;

        public const int QunsRunningD3DFullScreen = 3;

        public const uint GwHwndPrev = 3;
        public const uint AbmNew = 0;
        public const uint AbmRemove = 1;
        public const int AbnFullScreenApp = 2;

        public const uint MonitorInfoFPrimary = 0x1;
        public static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new IntPtr(-4);

        public const uint WmClose = 0x0010;
        public const uint WmGetText = 0x000D;
        public const uint WmGetTextLength = 0x000E;
        public const uint SmtoAbortIfHung = 0x2;

        const byte AcSrcOver = 0x0;
        const byte AcSrcAlpha = 0x1;
        const int UlwAlpha = 0x2;

        public delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint threadId, uint time);

        public delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);

        public delegate bool EnumWindowsProc(IntPtr window, IntPtr data);

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct AppBarData
        {
            public int Size;
            public IntPtr Window;
            public int CallbackMessage;
            public int Edge;
            public Rect Bounds;
            public IntPtr LParam;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MonitorInfoEx
        {
            public int Size;
            public Rect Monitor;
            public Rect WorkArea;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DisplayDevice
        {
            public int Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct BlendFunction
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr icon);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr window, out Rect rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint processId, uint threadId, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWinEvent(IntPtr hook);

        [DllImport("shell32.dll")]
        public static extern int SHQueryUserNotificationState(out int state);

        [DllImport("shell32.dll")]
        public static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int RegisterWindowMessage(string name);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr window, uint relationship);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

        /// <summary>With a null device, lists the display outputs (\\.\DISPLAY1 and so on), including unused ones.</summary>
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice displayDevice, uint flags);

        /// <summary>Windows 10 1607 and later; returns the previous context.</summary>
        [DllImport("user32.dll")]
        public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr data);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, StringBuilder lParam, uint flags, uint timeout, out IntPtr result);

        /// <summary>
        /// Reads a window's text, including another process's: unlike GetWindowText, WM_GETTEXT is
        /// answered by the window itself. Empty if it doesn't answer within a moment.
        /// </summary>
        public static string GetWindowTextOf(IntPtr window)
        {
            const uint timeout = 200;
            if (SendMessageTimeout(window, WmGetTextLength, IntPtr.Zero, IntPtr.Zero, SmtoAbortIfHung, timeout, out IntPtr length) == IntPtr.Zero)
                return "";
            int capacity = (int)Math.Min(length.ToInt64(), 65536) + 1;
            if (capacity <= 1)
                return "";
            var text = new StringBuilder(capacity);
            if (SendMessageTimeout(window, WmGetText, new IntPtr(capacity), text, SmtoAbortIfHung, timeout, out _) == IntPtr.Zero)
                return "";
            return text.ToString();
        }

        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr window);

        [DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr window, IntPtr dc);

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool DeleteDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool DeleteObject(IntPtr gdiObject);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screenDc, ref Point position, ref Size size,
            IntPtr sourceDc, ref Point sourcePosition, int colorKey, ref BlendFunction blend, int flags);

        /// <summary>Sets a WS_EX_LAYERED window's content and position from a 32-bit ARGB bitmap, keeping per-pixel transparency.</summary>
        public static void SetLayeredWindowBitmap(IntPtr window, Bitmap bitmap, Point position)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memoryDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr previousBitmap = IntPtr.Zero;
            try
            {
                hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                previousBitmap = SelectObject(memoryDc, hBitmap);
                var size = new Size(bitmap.Width, bitmap.Height);
                var sourcePosition = Point.Empty;
                var blend = new BlendFunction
                {
                    BlendOp = AcSrcOver,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AcSrcAlpha,
                };
                UpdateLayeredWindow(window, screenDc, ref position, ref size, memoryDc, ref sourcePosition, 0, ref blend, UlwAlpha);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDc);
                if (hBitmap != IntPtr.Zero)
                {
                    SelectObject(memoryDc, previousBitmap);
                    DeleteObject(hBitmap);
                }
                DeleteDC(memoryDc);
            }
        }
    }
}
