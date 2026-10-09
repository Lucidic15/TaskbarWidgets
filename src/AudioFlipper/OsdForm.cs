using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AudioFlipper.Audio;

namespace AudioFlipper
{
    /// <summary>
    /// Small popup shown above the taskbar after the output changes, naming the device and
    /// showing its volume. It never takes focus and hides itself after a moment.
    /// </summary>
    internal sealed class OsdForm : Form
    {
        const int WsExTopmost = 0x8;
        const int WsExToolWindow = 0x80;
        const int WsExNoActivate = 0x08000000;
        const int VisibleMilliseconds = 1800;

        readonly Timer hideTimer = new Timer { Interval = VisibleMilliseconds };
        readonly Font titleFont = new Font("Segoe UI Semibold", 10.5f);
        readonly Font detailFont = new Font("Segoe UI", 9f);
        readonly float dpiScale;

        bool roundedByWindows;
        string glyph = Glyphs.Speakers;
        string title = "";
        string detail = "";
        int? volumePercent;
        bool muted;

        public OsdForm()
        {
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero))
                dpiScale = screen.DpiX / 96f;

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            DoubleBuffered = true;
            Size = new Size(Px(320), Px(72));

            hideTimer.Tick += (sender, e) =>
            {
                hideTimer.Stop();
                Hide();
            };
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams createParams = base.CreateParams;
                createParams.ExStyle |= WsExTopmost | WsExToolWindow | WsExNoActivate;
                return createParams;
            }
        }

        public void ShowStatus(OutputStatus status)
        {
            if (status == null)
            {
                ShowMessage("No output device", "Enable an output in Windows Sound settings.");
                return;
            }

            glyph = Glyphs.For(status);
            title = status.Device.Name;
            detail = null;
            volumePercent = status.VolumePercent;
            muted = status.Muted;
            Present();
        }

        public void ShowMessage(string title, string detail)
        {
            glyph = Glyphs.Warning;
            this.title = title;
            this.detail = detail;
            volumePercent = null;
            muted = false;
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

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Windows 11 rounded corners; fails harmlessly on Windows 10.
            int preference = NativeMethods.DwmwcpRound;
            roundedByWindows = NativeMethods.DwmSetWindowAttribute(
                Handle, NativeMethods.DwmwaWindowCornerPreference, ref preference, sizeof(int)) == 0;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            hideTimer.Stop();
            Hide();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            bool light = Theme.AppsUseLightTheme();
            Color background = light ? Color.FromArgb(243, 243, 243) : Color.FromArgb(32, 32, 32);
            Color foreground = light ? Color.FromArgb(27, 27, 27) : Color.White;
            Color secondary = light ? Color.FromArgb(96, 96, 96) : Color.FromArgb(197, 197, 197);
            Color accent = light ? Color.FromArgb(0, 95, 184) : Color.FromArgb(96, 205, 255);
            Color track = light ? Color.FromArgb(196, 196, 196) : Color.FromArgb(80, 80, 80);
            Color border = light ? Color.FromArgb(213, 213, 213) : Color.FromArgb(58, 58, 58);

            Graphics graphics = e.Graphics;
            graphics.Clear(background);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Glyphs.Draw(graphics, glyph, new RectangleF(Px(18), Px(20), Px(32), Px(32)), foreground);

            int textLeft = Px(66);
            int textWidth = Width - textLeft - Px(18);
            const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

            TextRenderer.DrawText(graphics, title, titleFont,
                new Rectangle(textLeft, Px(12), textWidth, Px(24)), foreground, flags | TextFormatFlags.Left);

            if (volumePercent is int volume)
            {
                int labelWidth = Px(48);
                TextRenderer.DrawText(graphics, muted ? "Muted" : volume + "%", detailFont,
                    new Rectangle(Width - Px(18) - labelWidth, Px(38), labelWidth, Px(22)), secondary, flags | TextFormatFlags.Right);

                var bar = new RectangleF(textLeft, Px(47), textWidth - labelWidth - Px(10), Px(4));
                FillPill(graphics, track, bar);
                if (!muted)
                    FillPill(graphics, accent, new RectangleF(bar.X, bar.Y, bar.Width * Math.Min(100, volume) / 100f, bar.Height));
            }
            else
            {
                TextRenderer.DrawText(graphics, detail, detailFont,
                    new Rectangle(textLeft, Px(38), textWidth, Px(22)), secondary, flags | TextFormatFlags.Left);
            }

            // Windows 11 draws its own border around rounded windows.
            if (!roundedByWindows)
            {
                using (var pen = new Pen(border))
                    graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
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

        int Px(int value) => (int)Math.Round(value * dpiScale);

        static void FillPill(Graphics graphics, Color color, RectangleF rect)
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
    }
}
