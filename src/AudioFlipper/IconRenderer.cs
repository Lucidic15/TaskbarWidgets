using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using AudioFlipper.Audio;

namespace AudioFlipper
{
    /// <summary>
    /// Draws the tray icon: the active device's icon from the Sound control panel, faded with a
    /// mute badge when muted.
    /// </summary>
    internal static class IconRenderer
    {
        public static Icon Render(OutputStatus status, bool lightTaskbar)
        {
            int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
            Color glyphColor = lightTaskbar ? Color.FromArgb(28, 28, 28) : Color.White;

            using (var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.Clear(Color.Transparent);
                    DeviceIcons.Draw(graphics, status, new Rectangle(0, 0, size, size), glyphColor);
                }

                // GetHicon's handle isn't owned by Icon.FromHandle, so copy it and free the original.
                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon borrowed = Icon.FromHandle(handle))
                        return (Icon)borrowed.Clone();
                }
                finally
                {
                    NativeMethods.DestroyIcon(handle);
                }
            }
        }
    }
}
