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
    /// the mouse is on its way up from the widget, and closes as soon as the mouse leaves the flyout.
    /// </summary>
    internal sealed class VolumeFlyout : PopupWindow
    {
        // How long the mouse may be off both the widget and the flyout on its way up to the flyout.
        const int ApproachGraceMilliseconds = 350;
        const int WheelNotch = 120;

        readonly Timer hideCheckTimer = new Timer { Interval = 100 };
        readonly Font titleFont = new Font("Segoe UI Semibold", 10.5f);
        readonly Font detailFont = new Font("Segoe UI", 9f);
        readonly Font valueFont = new Font("Segoe UI", 10.5f);

        AudioStatus status;
        Rectangle anchor;
        int lastCursorNear;
        bool cursorEntered; // Once the mouse has been in the flyout, leaving it closes the flyout.
        bool draggingSlider;
        float dragLevel;
        // After a drag, keep showing where the slider was released until the new volume comes back
        // from Windows, so the thumb doesn't jump back for a moment.
        bool holdingDragLevel;
        int releasedAt;
        bool hoveringSlider;
        bool hoveringMute;
        bool pressingMute;
        int wheelRemainder;

        public VolumeFlyout()
        {
            Size = new Size(Px(320), Px(104));
            hideCheckTimer.Tick += (sender, e) => HideWhenCursorLeaves();
        }

        /// <summary>Raised while dragging or clicking the slider, with the new volume from 0 to 1.</summary>
        public event EventHandler<float> VolumeChangeRequested;

        /// <summary>Raised when the slider is let go after dragging or clicking it.</summary>
        public event EventHandler VolumeChangeFinished;

        /// <summary>Raised on mouse-wheel scrolling with the number of notches (positive is up).</summary>
        public event EventHandler<int> VolumeStepsRequested;

        public event EventHandler MuteRequested;

        /// <summary>Shows the flyout centered above <paramref name="anchor"/> (the taskbar widget).</summary>
        public void ShowAbove(Rectangle anchor)
        {
            this.anchor = anchor;
            PositionAboveAnchor();
            lastCursorNear = Environment.TickCount;
            Invalidate();
            if (!Visible)
            {
                cursorEntered = false;
                Show();
            }
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

        public void SetStatus(AudioStatus status)
        {
            this.status = status;
            if (holdingDragLevel && status != null && Math.Abs(status.VolumeLevel - dragLevel) < 0.005f)
                holdingDragLevel = false;
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
            holdingDragLevel = false;
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

        // Backs up OnMouseLeave, which Windows doesn't always send, and handles the trip up from the widget.
        void HideWhenCursorLeaves()
        {
            if (draggingSlider)
                return;

            Point cursor = Cursor.Position;
            if (Bounds.Contains(cursor))
            {
                cursorEntered = true;
                return;
            }
            if (cursorEntered)
            {
                Hide();
                return;
            }

            // Not in the flyout yet: the widget and the gap above it count as on the way.
            Rectangle gap = Rectangle.FromLTRB(anchor.Left, Bottom, anchor.Right, Math.Max(Bottom, anchor.Top));
            if (anchor.Contains(cursor) || gap.Contains(cursor))
                lastCursorNear = Environment.TickCount;
            else if (Environment.TickCount - lastCursorNear > ApproachGraceMilliseconds)
                Hide();
        }

        float DisplayedLevel =>
            draggingSlider || (holdingDragLevel && Environment.TickCount - releasedAt < 500) ? dragLevel : status?.VolumeLevel ?? 0f;

        Rectangle MuteButtonBounds => new Rectangle(Px(10), Px(58), Px(36), Px(36));

        RectangleF TrackBounds => new RectangleF(Px(60), Px(74), Width - Px(60) - Px(62), Px(4));

        Rectangle SliderHitBounds => Rectangle.FromLTRB(Px(50), Px(58), Width - Px(52), Px(94));

        Rectangle ValueBounds => new Rectangle(Width - Px(54), Px(64), Px(40), Px(24));

        void SetVolumeFromX(int x)
        {
            RectangleF track = TrackBounds;
            float level = Math.Max(0f, Math.Min(1f, (x - track.X) / track.Width));
            if (level == dragLevel)
                return;
            dragLevel = level;
            // Repaint right away, before changing the volume, so the thumb keeps up with the mouse.
            Invalidate();
            Update();
            VolumeChangeRequested?.Invoke(this, level);
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
                dragLevel = status.VolumeLevel;
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
                holdingDragLevel = true;
                releasedAt = Environment.TickCount;
                Invalidate();
                VolumeChangeFinished?.Invoke(this, EventArgs.Empty);
                if (!ClientRectangle.Contains(e.Location))
                    Hide(); // Released outside the flyout.
            }
            else if (pressingMute)
            {
                pressingMute = false;
                Invalidate();
                if (MuteButtonBounds.Contains(e.Location))
                    MuteRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            cursorEntered = true;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoveringMute = false;
            hoveringSlider = false;
            if (draggingSlider)
                Invalidate();
            else
                Hide();
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
                TextRenderer.DrawText(graphics, "No device", titleFont,
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
            float level = DisplayedLevel;
            RectangleF track = TrackBounds;
            Color fill = status.Muted && !draggingSlider ? colors.Secondary : colors.Accent;
            Shapes.FillPill(graphics, colors.Track, track);
            Shapes.FillPill(graphics, fill, new RectangleF(track.X, track.Y, track.Width * level, track.Height));

            var center = new PointF(track.X + track.Width * level, track.Y + track.Height / 2);
            float ring = Px(10);
            float dot = draggingSlider ? Px(5) : hoveringSlider ? Px(7) : Px(6);
            using (var ringBrush = new SolidBrush(colors.ThumbRing))
                graphics.FillEllipse(ringBrush, center.X - ring, center.Y - ring, ring * 2, ring * 2);
            using (var ringPen = new Pen(colors.Border))
                graphics.DrawEllipse(ringPen, center.X - ring, center.Y - ring, ring * 2, ring * 2);
            using (var dotBrush = new SolidBrush(fill))
                graphics.FillEllipse(dotBrush, center.X - dot, center.Y - dot, dot * 2, dot * 2);

            TextRenderer.DrawText(graphics, ((int)Math.Round(level * 100)).ToString(), valueFont, ValueBounds, colors.Foreground, flags | TextFormatFlags.Right);

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
