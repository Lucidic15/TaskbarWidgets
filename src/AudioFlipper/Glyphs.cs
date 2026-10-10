using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using AudioFlipper.Audio;

namespace AudioFlipper
{
    /// <summary>Draws the device icons from the Windows icon font.</summary>
    internal static class Glyphs
    {
        // Code points shared by Segoe Fluent Icons (Windows 11) and Segoe MDL2 Assets (Windows 10).
        public const string Speakers = "\uE7F5";
        public const string Headphones = "\uE7F6";
        public const string Headset = "\uE95B";
        public const string Display = "\uE7F4";
        public const string Microphone = "\uE720";
        public const string Mute = "\uE74F";
        public const string Warning = "\uE7BA";

        static readonly string IconFontName = FindIconFont();

        /// <summary>The glyph for the device's kind; the mute glyph when there's no device.</summary>
        public static string For(AudioStatus status)
        {
            if (status == null)
                return Mute;

            switch (status.Device.Kind)
            {
                case DeviceKind.Headphones:
                    return Headphones;
                case DeviceKind.Headset:
                    return Headset;
                case DeviceKind.Display:
                    return Display;
                case DeviceKind.Microphone:
                    return Microphone;
                default:
                    return Speakers;
            }
        }

        /// <summary>Draws the glyph scaled to fit and centered in <paramref name="target"/>.</summary>
        public static void Draw(Graphics graphics, string glyph, RectangleF target, Color color)
        {
            // Without an icon font (very old Windows), fall back to a musical note.
            string fontName = IconFontName ?? "Segoe UI Symbol";
            string text = IconFontName != null ? glyph : "♪";

            using (var family = new FontFamily(fontName))
            using (var path = new GraphicsPath())
            {
                path.AddString(text, family, (int)FontStyle.Regular, 100f, PointF.Empty, StringFormat.GenericTypographic);
                RectangleF bounds = path.GetBounds();
                if (bounds.Width <= 0 || bounds.Height <= 0)
                    return;

                float scale = Math.Min(target.Width / bounds.Width, target.Height / bounds.Height);
                using (var transform = new Matrix())
                {
                    transform.Translate(
                        target.X + (target.Width - bounds.Width * scale) / 2,
                        target.Y + (target.Height - bounds.Height * scale) / 2);
                    transform.Scale(scale, scale);
                    transform.Translate(-bounds.X, -bounds.Y);
                    path.Transform(transform);
                }

                using (var brush = new SolidBrush(color))
                    graphics.FillPath(brush, path);
            }
        }

        static string FindIconFont()
        {
            using (var fonts = new InstalledFontCollection())
            {
                return new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" }
                    .FirstOrDefault(name => fonts.Families.Any(family => family.Name == name));
            }
        }
    }
}
