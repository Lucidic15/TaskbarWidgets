using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace RDPLauncher
{
    /// <summary>Line icons drawn to match the Windows 11 taskbar, scaled to any size.</summary>
    internal static class Icons
    {
        /// <summary>A monitor with an arrow on its screen: Remote Desktop.</summary>
        public static void DrawRemoteDesktop(Graphics graphics, RectangleF box, Color color)
        {
            float s = Math.Min(box.Width, box.Height);
            float stroke = Math.Max(1.25f, s * 0.08f);
            RectangleF screen = DrawMonitor(graphics, box, color, stroke);

            // An arrow pointing up and away: connecting out to another computer.
            using (var pen = new Pen(color, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                float left = screen.X + screen.Width * 0.32f;
                float right = screen.X + screen.Width * 0.68f;
                float top = screen.Y + screen.Height * 0.26f;
                float bottom = screen.Y + screen.Height * 0.74f;
                float head = screen.Width * 0.2f;
                graphics.DrawLine(pen, left, bottom, right, top);
                graphics.DrawLines(pen, new[] { new PointF(right - head, top), new PointF(right, top), new PointF(right, top + head) });
            }
        }

        /// <summary>A monitor outline with a number on its screen, for the display buttons; the font is sized in pixels.</summary>
        public static void DrawNumberedMonitor(Graphics graphics, RectangleF box, Color color, string number, Font font)
        {
            float stroke = Math.Max(1.25f, Math.Min(box.Width, box.Height) * 0.07f);
            RectangleF screen = DrawMonitor(graphics, box, color, stroke);

            // The screen is shorter than a line of text, which DrawString won't draw into, so draw the
            // digits as a shape and center what's actually inked.
            using (var path = new GraphicsPath())
            {
                path.AddString(number, font.FontFamily, (int)font.Style, font.Size, PointF.Empty, StringFormat.GenericDefault);
                RectangleF inked = path.GetBounds();
                if (inked.Width <= 0 || inked.Height <= 0)
                    return;
                using (var transform = new Matrix())
                {
                    transform.Translate(
                        screen.X + (screen.Width - inked.Width) / 2 - inked.X,
                        screen.Y + (screen.Height - inked.Height) / 2 - inked.Y);
                    path.Transform(transform);
                }
                using (var brush = new SolidBrush(color))
                    graphics.FillPath(brush, path);
            }
        }

        /// <summary>A tick, as on a selected display in the display picker.</summary>
        public static void DrawCheck(Graphics graphics, RectangleF box, Color color)
        {
            float s = Math.Min(box.Width, box.Height);
            using (var pen = new Pen(color, Math.Max(1.5f, s * 0.12f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                graphics.DrawLines(pen, new[]
                {
                    new PointF(box.X + s * 0.2f, box.Y + s * 0.53f),
                    new PointF(box.X + s * 0.41f, box.Y + s * 0.73f),
                    new PointF(box.X + s * 0.8f, box.Y + s * 0.3f),
                });
            }
        }

        /// <summary>Outlines a monitor on its stand, filling <paramref name="box"/>; returns the inside of its screen.</summary>
        static RectangleF DrawMonitor(Graphics graphics, RectangleF box, Color color, float stroke)
        {
            float half = stroke / 2;
            var screen = new RectangleF(box.X + half, box.Y + box.Height * 0.1f + half, box.Width - stroke, box.Height * 0.62f - stroke);
            Shapes.DrawRoundedRectangle(graphics, color, screen, box.Width * 0.09f, stroke);

            using (var pen = new Pen(color, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float middle = box.X + box.Width / 2;
                float baseY = box.Y + box.Height * 0.9f - half;
                graphics.DrawLine(pen, middle, screen.Bottom + half, middle, baseY);
                graphics.DrawLine(pen, box.X + box.Width * 0.3f, baseY, box.X + box.Width * 0.7f, baseY);
            }
            return RectangleF.Inflate(screen, -half, -half);
        }
    }
}
