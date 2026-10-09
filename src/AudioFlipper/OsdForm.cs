using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AudioFlipper.Audio;

namespace AudioFlipper
{
    /// <summary>
    /// Small popup shown above the taskbar after the output changes, naming the device and
    /// showing its volume. It hides itself after a moment.
    /// </summary>
    internal sealed class OsdForm : PopupWindow
    {
        const int VisibleMilliseconds = 1800;

        readonly Timer hideTimer = new Timer { Interval = VisibleMilliseconds };
        readonly Font titleFont = new Font("Segoe UI Semibold", 10.5f);
        readonly Font detailFont = new Font("Segoe UI", 9f);

        OutputStatus status; // Null while showing a message.
        string title = "";
        string detail = "";

        public OsdForm()
        {
            Size = new Size(Px(320), Px(72));
            hideTimer.Tick += (sender, e) =>
            {
                hideTimer.Stop();
                Hide();
            };
        }

        public void ShowStatus(OutputStatus status)
        {
            if (status == null)
            {
                ShowMessage("No output device", "Enable an output in Windows Sound settings.");
                return;
            }

            this.status = status;
            title = status.Device.Name;
            detail = null;
            Present();
        }

        public void ShowMessage(string title, string detail)
        {
            status = null;
            this.title = title;
            this.detail = detail;
            Present();
        }

        void Present()
        {
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

            var iconBounds = new Rectangle(Px(18), Px(20), Px(32), Px(32));
            if (status != null)
                DeviceIcons.Draw(graphics, status, iconBounds, colors.Foreground);
            else
                Glyphs.Draw(graphics, Glyphs.Warning, iconBounds, colors.Foreground);

            int textLeft = Px(66);
            int textWidth = Width - textLeft - Px(18);
            const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

            TextRenderer.DrawText(graphics, title, titleFont,
                new Rectangle(textLeft, Px(12), textWidth, Px(24)), colors.Foreground, flags | TextFormatFlags.Left);

            if (status != null)
            {
                int labelWidth = Px(48);
                TextRenderer.DrawText(graphics, status.Muted ? "Muted" : status.VolumePercent + "%", detailFont,
                    new Rectangle(Width - Px(18) - labelWidth, Px(38), labelWidth, Px(22)), colors.Secondary, flags | TextFormatFlags.Right);

                var bar = new RectangleF(textLeft, Px(47), textWidth - labelWidth - Px(10), Px(4));
                Shapes.FillPill(graphics, colors.Track, bar);
                if (!status.Muted)
                    Shapes.FillPill(graphics, colors.Accent, new RectangleF(bar.X, bar.Y, bar.Width * Math.Min(100, status.VolumePercent) / 100f, bar.Height));
            }
            else
            {
                TextRenderer.DrawText(graphics, detail, detailFont,
                    new Rectangle(textLeft, Px(38), textWidth, Px(22)), colors.Secondary, flags | TextFormatFlags.Left);
            }

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
