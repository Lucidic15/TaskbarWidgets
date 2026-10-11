using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RDPLauncher
{
    /// <summary>
    /// Small popup shown above the taskbar with a message, such as why Remote Desktop couldn't
    /// start. It hides itself after a moment.
    /// </summary>
    internal sealed class OsdForm : PopupWindow
    {
        const int VisibleMilliseconds = 4000;

        readonly Timer hideTimer = new Timer { Interval = VisibleMilliseconds };
        readonly Font titleFont = new Font("Segoe UI Semibold", 10.5f);
        readonly Font detailFont = new Font("Segoe UI", 9f);

        string title = "";
        string detail = "";

        public OsdForm()
        {
            Size = new Size(Px(360), Px(84));
            hideTimer.Tick += (sender, e) =>
            {
                hideTimer.Stop();
                Hide();
            };
        }

        public void ShowMessage(string title, string detail)
        {
            this.title = title;
            this.detail = detail ?? "";

            // Bottom-right of the screen that was clicked on, just above the taskbar.
            Rectangle workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(workArea.Right - Width - Px(12), workArea.Bottom - Height - Px(12));
            Invalidate();
            if (!Visible)
                Show();

            hideTimer.Stop();
            hideTimer.Start();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            hideTimer.Stop();
            Hide();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Palette colors = Palette.Current();
            Graphics graphics = e.Graphics;
            graphics.Clear(colors.Background);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Glyphs.Draw(graphics, Glyphs.Warning, new Rectangle(Px(18), Px(28), Px(28), Px(28)), colors.Foreground);

            int textLeft = Px(62);
            int textWidth = Width - textLeft - Px(18);
            const TextFormatFlags flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Left;

            TextRenderer.DrawText(graphics, title, titleFont, new Rectangle(textLeft, Px(12), textWidth, Px(24)), colors.Foreground,
                flags | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            // Up to two lines, since error messages can be long.
            TextRenderer.DrawText(graphics, detail, detailFont, new Rectangle(textLeft, Px(38), textWidth, Px(36)), colors.Secondary,
                flags | TextFormatFlags.WordBreak);

            DrawBorder(graphics, colors.Border);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                hideTimer.Dispose();
                titleFont.Dispose();
                detailFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
