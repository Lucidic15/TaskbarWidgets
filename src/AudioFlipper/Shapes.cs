using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace AudioFlipper
{
    internal static class Shapes
    {
        /// <summary>Fills a rectangle with fully rounded ends, as used for volume bars.</summary>
        public static void FillPill(Graphics graphics, Color color, RectangleF rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            float diameter = Math.Min(rect.Width, rect.Height);
            using (var path = new GraphicsPath())
            using (var brush = new SolidBrush(color))
            {
                path.AddArc(rect.X, rect.Y, diameter, diameter, 90, 180);
                path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 180);
                path.CloseFigure();
                graphics.FillPath(brush, path);
            }
        }

        public static void FillRoundedRectangle(Graphics graphics, Color color, RectangleF rect, float radius)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            float diameter = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            using (var path = new GraphicsPath())
            using (var brush = new SolidBrush(color))
            {
                path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
                path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
                path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
                path.CloseFigure();
                graphics.FillPath(brush, path);
            }
        }
    }
}
