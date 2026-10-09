using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AudioFlipper.Audio;

namespace AudioFlipper
{
    /// <summary>
    /// Flyout shown above the taskbar widget while hovering it, like the Windows 11 volume flyout:
    /// the device's name, a volume slider, and the device icon as a mute button. It stays open while
    /// the mouse is over the widget or the flyout and closes shortly after it leaves both.
    /// </summary>
    internal sealed class VolumeFlyout : PopupWindow
    {
        const int HideDelayMilliseconds = 350;
        const int WheelNotch = 120;

        readonly Timer hideCheckTimer = new Timer { Interval = 100 };
        readonly Font titleFont = new Font("Segoe UI Semibold", 10.5f);
        readonly Font detailFont = new Font("Segoe UI", 9f);
        readonly Font valueFont = new Font("Segoe UI", 10.5f);

        OutputStatus status;
        Rectangle anchor;
        int lastCursorInside;
        bool draggingSlider;
        int dragPercent;
        bool hoveringSlider;
        bool hoveringMute;
        bool pressingMute;
        int wheelRemainder;

        public VolumeFlyout()
        {
            Size = new Size(Px(320), Px(104));
            hideCheckTimer.Tick += (sender, e) => HideWhenCursorLeaves();
        }

        /// <summary>Raised while dragging or clicking the slider, with the new volume in percent.</summary>
        public event EventHandler<int> VolumeChangeRequested;

        /// <summary>Raised on mouse-wheel scrolling with the number of notches (positive is up).</summary>
        public event EventHandler<int> VolumeStepsRequested;

        public event EventHandler MuteRequested;

        /// <summary>Shows the flyout centered above <paramref name="anchor"/> (the taskbar widget).</summary>
        public void ShowAbove(Rectangle anchor)
        {
            this.anchor = anchor;
            PositionAboveAnchor();
            lastCursorInside = Environment.TickCount;
            Invalidate();
            if (!Visible)
                Show();
            hideCheckTimer.Start();
        }

        /// <summary>Follows the widget when it moves or changes width.</summary>
        public void SetAnchor(Rectangle anchor)
        {
            if (anchor == this.anchor)
                return;
            this.anchor = anchor;
            if (Visible)
                PositionAboveAnchor();
        }

        public void SetStatus(OutputStatus status)
        {
            this.status = status;
            if (Visible)
                Invalidate();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible)
                return;
            hideCheckTimer.Stop();
            draggingSlider = false;
            pressingMute = false;
        }

        void PositionAboveAnchor()
        {
            Rectangle workArea = Screen.FromRectangle(anchor).WorkingArea;
            int x = anchor.Left + (anchor.Width - Width) / 2;
            x = Math.Max(workArea.Left + Px(8), Math.Min(x, workArea.Right - Width - Px(8)));
            int y = Math.Min(anchor.Top, workArea.Bottom) - Height - Px(8);
            Location = new Point(x, y);
        }

        void HideWhenCursorLeaves()
        {
            Point cursor = Cursor.Position;
            // The gap between the widget and the flyout counts as inside, so the mouse can cross it.
            Rectangle gap = Rectangle.FromLTRB(anchor.Left, Bottom, anchor.Right, Math.Max(Bottom, anchor.Top));
            if (draggingSlider || Bounds.Contains(cursor) || anchor.Contains(cursor) || gap.Contains(cursor))
                lastCursorInside = Environment.TickCount;
            else if (Environment.TickCount - lastCursorInside > HideDelayMilliseconds)
                Hide();
        }

        int DisplayedPercent => draggingSlider ? dragPercent : status?.VolumePercent ?? 0;

        Rectangle MuteButtonBounds => new Rectangle(Px(10), Px(58), Px(36), Px(36));

        RectangleF TrackBounds => new RectangleF(Px(60), Px(74), Width - Px(60) - Px(62), Px(4));

        Rectangle SliderHitBounds => Rectangle.FromLTRB(Px(50), Px(58), Width - Px(52), Px(94));

        Rectangle ValueBounds => new Rectangle(Width - Px(54), Px(64), Px(40), Px(24));

        void SetVolumeFromX(int x)
        {
            RectangleF track = TrackBounds;
            float fraction = Math.Max(0f, Math.Min(1f, (x - track.X) / track.Width));
            int percent = (int)Math.Round(fraction * 100);
            if (percent == dragPercent)
                return;
            dragPercent = percent;
            Invalidate();
            VolumeChangeRequested?.Invoke(this, percent);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || status == null)
                return;

            if (MuteButtonBounds.Contains(e.Location))
            {
                pressingMute = true;
                Invalidate();
            }
            else if (SliderHitBounds.Contains(e.Location))
            {
                draggingSlider = true;
                dragPercent = status.VolumePercent;
                SetVolumeFromX(e.X);
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (draggingSlider)
            {
                SetVolumeFromX(e.X);
                return;
            }

            bool overMute = MuteButtonBounds.Contains(e.Location);
            bool overSlider = SliderHitBounds.Contains(e.Location);
            if (overMute != hoveringMute || overSlider != hoveringSlider)
            {
                hoveringMute = overMute;
                hoveringSlider = overSlider;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left)
                return;

            if (draggingSlider)
            {
                draggingSlider = false;
                Invalidate();
            }
            else if (pressingMute)
            {
                pressingMute = false;
                Invalidate();
                if (MuteButtonBounds.Contains(e.Location))
                    MuteRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoveringMute = false;
            hoveringSlider = false;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            wheelRemainder += e.Delta;
            int notches = wheelRemainder / WheelNotch;
            wheelRemainder -= notches * WheelNotch;
            if (notches != 0)
                VolumeStepsRequested?.Invoke(this, notches);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Palette colors = Palette.Current();
            Graphics graphics = e.Graphics;
            graphics.Clear(colors.Background);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

            if (status == null)
            {
                TextRenderer.DrawText(graphics, "No output device", titleFont,
                    new Rectangle(Px(16), Px(12), Width - Px(32), Px(22)), colors.Foreground, flags);
                DrawBorder(graphics, colors.Border);
                return;
            }

            // Name and hardware, as on the Sound control panel's Playback tab.
            TextRenderer.DrawText(graphics, status.Device.Name, titleFont,
                new Rectangle(Px(16), Px(12), Width - Px(32), Px(22)), colors.Foreground, flags);
            if (!string.IsNullOrEmpty(status.Device.Description))
            {
                TextRenderer.DrawText(graphics, status.Device.Description, detailFont,
                    new Rectangle(Px(16), Px(33), Width - Px(32), Px(18)), colors.Secondary, flags);
            }

            // Device icon, which doubles as the mute button.
            Rectangle button = MuteButtonBounds;
            if (hoveringMute || pressingMute)
                Shapes.FillRoundedRectangle(graphics, colors.Hover, button, Px(4));
            int iconSize = Px(24);
            DeviceIcons.Draw(graphics, status,
                new Rectangle(button.X + (button.Width - iconSize) / 2, button.Y + (button.Height - iconSize) / 2, iconSize, iconSize),
                colors.Foreground);

            // Slider, drawn like the Windows 11 one: an accent fill and a ring with a dot that grows on hover.
            int percent = DisplayedPercent;
            RectangleF track = TrackBounds;
            Color fill = status.Muted && !draggingSlider ? colors.Secondary : colors.Accent;
            Shapes.FillPill(graphics, colors.Track, track);
            Shapes.FillPill(graphics, fill, new RectangleF(track.X, track.Y, track.Width * percent / 100f, track.Height));

            var center = new PointF(track.X + track.Width * percent / 100f, track.Y + track.Height / 2);
            float ring = Px(10);
            float dot = draggingSlider ? Px(5) : hoveringSlider ? Px(7) : Px(6);
            using (var ringBrush = new SolidBrush(colors.ThumbRing))
                graphics.FillEllipse(ringBrush, center.X - ring, center.Y - ring, ring * 2, ring * 2);
            using (var ringPen = new Pen(colors.Border))
                graphics.DrawEllipse(ringPen, center.X - ring, center.Y - ring, ring * 2, ring * 2);
            using (var dotBrush = new SolidBrush(fill))
                graphics.FillEllipse(dotBrush, center.X - dot, center.Y - dot, dot * 2, dot * 2);

            TextRenderer.DrawText(graphics, percent.ToString(), valueFont, ValueBounds, colors.Foreground, flags | TextFormatFlags.Right);

            DrawBorder(graphics, colors.Border);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                hideCheckTimer.Dispose();
                titleFont.Dispose();
                detailFont.Dispose();
                valueFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
