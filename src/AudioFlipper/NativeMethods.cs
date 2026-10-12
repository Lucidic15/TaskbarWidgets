using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace AudioFlipper
{
    internal static class NativeMethods
    {
        public const int DwmwaWindowCornerPreference = 33;
        public const int DwmwcpRound = 2;

        public static readonly IntPtr HwndTopmost = new IntPtr(-1);
        public const uint SwpNoSize = 0x1;
        public const uint SwpNoMove = 0x2;
        public const uint SwpNoActivate = 0x10;

        public const uint EventSystemForeground = 0x3;
        public const uint WinEventOutOfContext = 0x0;

        public const int QunsRunningD3DFullScreen = 3;

        public const uint GwHwndPrev = 3;
        public const uint AbmNew = 0;
        public const uint AbmRemove = 1;
        public const int AbnFullScreenApp = 2;

        const byte AcSrcOver = 0x0;
        const byte AcSrcAlpha = 0x1;
        const int UlwAlpha = 0x2;

        public delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint threadId, uint time);

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
        public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        /// <summary>Extracts an icon at a specific size; negative indexes are resource IDs.</summary>
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHDefExtractIcon(string iconFile, int index, uint flags, out IntPtr largeIcon, IntPtr smallIcon, uint iconSize);

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
