using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;

namespace RDPLauncher
{
    /// <summary>Draws icons from the Windows icon font.</summary>
    internal static class Glyphs
    {
        // Code point shared by Segoe Fluent Icons (Windows 11) and Segoe MDL2 Assets (Windows 10).
        public const string Warning = "";

        static readonly string IconFontName = FindIconFont();

        /// <summary>Draws the glyph scaled to fit and centered in <paramref name="target"/>.</summary>
        public static void Draw(Graphics graphics, string glyph, RectangleF target, Color color)
        {
            // Without an icon font (very old Windows), fall back to an exclamation mark.
            string fontName = IconFontName ?? "Segoe UI Symbol";
            string text = IconFontName != null ? glyph : "!";

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
