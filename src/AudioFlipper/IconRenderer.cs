using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using AudioFlipper.Audio;

namespace AudioFlipper
{
    /// <summary>
    /// Draws the tray icon: a glyph for the kind of device (speakers, headphones, headset,
    /// monitor) with the volume level as a bar along the bottom.
    /// </summary>
    internal static class IconRenderer
    {
        public static Icon Render(OutputStatus status, bool lightTaskbar)
        {
            int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
            Color foreground = lightTaskbar ? Color.FromArgb(28, 28, 28) : Color.White;

            using (var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.Clear(Color.Transparent);

                    if (status == null)
                    {
                        Glyphs.Draw(graphics, Glyphs.For(null), new RectangleF(0, 0, size, size), foreground);
                    }
                    else
                    {
                        int barHeight = Math.Max(2, size / 8);
                        int barTop = size - barHeight;
                        float glyphHeight = barTop - Math.Max(1, size / 16);
                        Glyphs.Draw(graphics, Glyphs.For(status), new RectangleF(0, 0, size, glyphHeight), foreground);

                        using (var track = new SolidBrush(Color.FromArgb(90, foreground)))
                            graphics.FillRectangle(track, 0, barTop, size, barHeight);
                        if (!status.Muted)
                        {
                            int filled = (int)Math.Round(size * Math.Min(100, status.VolumePercent) / 100.0);
                            using (var fill = new SolidBrush(foreground))
                                graphics.FillRectangle(fill, 0, barTop, filled, barHeight);
                        }
                    }
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
