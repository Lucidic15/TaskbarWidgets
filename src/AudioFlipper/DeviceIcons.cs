using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using AudioFlipper.Audio;

namespace AudioFlipper
{
    /// <summary>
    /// The icons chosen for each device in the Sound control panel (Properties → Change Icon).
    /// </summary>
    internal static class DeviceIcons
    {
        static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The device's Windows icon at the given pixel size, or null if it can't be loaded.
        /// The bitmap is shared; don't dispose it.</summary>
        public static Bitmap Get(string iconPath, int size)
        {
            if (string.IsNullOrWhiteSpace(iconPath) || size <= 0)
                return null;

            string key = size + "|" + iconPath;
            if (!Cache.TryGetValue(key, out Bitmap bitmap))
            {
                bitmap = Load(iconPath, size);
                Cache[key] = bitmap; // Failures are cached too, so they aren't retried on every poll.
            }
            return bitmap;
        }

        /// <summary>
        /// Draws the active device's Windows icon, or a glyph in <paramref name="glyphColor"/> when
        /// there's no device or its icon can't be loaded. Muted devices are faded with a mute badge.
        /// </summary>
        public static void Draw(Graphics graphics, AudioStatus status, Rectangle bounds, Color glyphColor)
        {
            Bitmap icon = status == null ? null : Get(status.Device.IconPath, Math.Min(bounds.Width, bounds.Height));
            bool muted = status != null && status.Muted;
            if (icon == null)
            {
                Glyphs.Draw(graphics, Glyphs.For(status), bounds, muted ? Color.FromArgb(115, glyphColor) : glyphColor);
            }
            else
            {
                InterpolationMode previousInterpolation = graphics.InterpolationMode;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                using (var attributes = new ImageAttributes())
                {
                    if (muted)
                        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = 0.45f });
                    graphics.DrawImage(icon, bounds, 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, attributes);
                }
                graphics.InterpolationMode = previousInterpolation;
            }
            if (muted)
                DrawMuteBadge(graphics, bounds);
        }

        static void DrawMuteBadge(Graphics graphics, Rectangle bounds)
        {
            float size = bounds.Width * 0.6f;
            var badge = new RectangleF(bounds.Right - size, bounds.Bottom - size, size, size);
            SmoothingMode previousSmoothing = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var fill = new SolidBrush(Color.FromArgb(196, 43, 28)))
                graphics.FillEllipse(fill, badge);
            using (var pen = new Pen(Color.White, Math.Max(1.2f, size / 7f)))
            {
                float inset = size * 0.32f;
                graphics.DrawLine(pen, badge.Left + inset, badge.Top + inset, badge.Right - inset, badge.Bottom - inset);
                graphics.DrawLine(pen, badge.Right - inset, badge.Top + inset, badge.Left + inset, badge.Bottom - inset);
            }
            graphics.SmoothingMode = previousSmoothing;
        }

        static Bitmap Load(string iconPath, int size)
        {
            // Icon paths look like "%SystemRoot%\System32\mmres.dll,-3011"; a negative index is a resource ID.
            string file = iconPath.Trim().TrimStart('@');
            int index = 0;
            int comma = file.LastIndexOf(',');
            if (comma > 0 && int.TryParse(file.Substring(comma + 1).Trim(), out int parsedIndex))
            {
                index = parsedIndex;
                file = file.Substring(0, comma);
            }
            file = Environment.ExpandEnvironmentVariables(file.Trim().Trim('"'));

            try
            {
                if (NativeMethods.SHDefExtractIcon(file, index, 0, out IntPtr handle, IntPtr.Zero, (uint)size) != 0 || handle == IntPtr.Zero)
                    return null;
                try
                {
                    using (Icon icon = Icon.FromHandle(handle))
                        return icon.ToBitmap();
                }
                finally
                {
                    NativeMethods.DestroyIcon(handle);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
