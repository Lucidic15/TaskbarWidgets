using System;
using System.Drawing;
using System.Windows.Forms;

namespace AudioFlipper
{
    /// <summary>
    /// Base for AudioFlipper's popups: a borderless, always-on-top window that never takes focus
    /// (so it doesn't interrupt typing), with Windows 11 rounded corners.
    /// </summary>
    internal abstract class PopupWindow : Form
    {
        const int WsExTopmost = 0x8;
        const int WsExToolWindow = 0x80;
        const int WsExNoActivate = 0x08000000;

        readonly float dpiScale;
        bool roundedByWindows;

        protected PopupWindow()
        {
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero))
                dpiScale = screen.DpiX / 96f;

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            DoubleBuffered = true;
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

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Windows 11 rounded corners; fails harmlessly on Windows 10.
            int preference = NativeMethods.DwmwcpRound;
            roundedByWindows = NativeMethods.DwmSetWindowAttribute(
                Handle, NativeMethods.DwmwaWindowCornerPreference, ref preference, sizeof(int)) == 0;
        }

        /// <summary>Draws a 1px border, except on Windows 11 where Windows draws one around rounded windows.</summary>
        protected void DrawBorder(Graphics graphics, Color color)
        {
            if (roundedByWindows)
                return;
            using (var pen = new Pen(color))
                graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        protected int Px(int value) => (int)Math.Round(value * dpiScale);
    }

    /// <summary>Popup colors for the "default app mode" (light or dark) chosen in Windows.</summary>
    internal sealed class Palette
    {
        static readonly Palette Light = new Palette
        {
            Background = Color.FromArgb(243, 243, 243),
            Foreground = Color.FromArgb(27, 27, 27),
            Secondary = Color.FromArgb(96, 96, 96),
            Accent = Color.FromArgb(0, 95, 184),
            Track = Color.FromArgb(196, 196, 196),
            Border = Color.FromArgb(213, 213, 213),
            Hover = Color.FromArgb(14, 0, 0, 0),
            ThumbRing = Color.White,
        };

        static readonly Palette Dark = new Palette
        {
            Background = Color.FromArgb(32, 32, 32),
            Foreground = Color.White,
            Secondary = Color.FromArgb(197, 197, 197),
            Accent = Color.FromArgb(96, 205, 255),
            Track = Color.FromArgb(80, 80, 80),
            Border = Color.FromArgb(58, 58, 58),
            Hover = Color.FromArgb(16, 255, 255, 255),
            ThumbRing = Color.FromArgb(69, 69, 69),
        };

        public Color Background { get; private set; }
        public Color Foreground { get; private set; }
        public Color Secondary { get; private set; }
        public Color Accent { get; private set; }
        public Color Track { get; private set; }
        public Color Border { get; private set; }
        public Color Hover { get; private set; }
        public Color ThumbRing { get; private set; }

        public static Palette Current() => Theme.AppsUseLightTheme() ? Light : Dark;
    }
}
