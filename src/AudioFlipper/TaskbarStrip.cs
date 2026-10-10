using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;
using AudioFlipper.Audio;
using AudioFlipper.Webcams;

namespace AudioFlipper
{
    /// <summary>
    /// A taskbar button in the style of the Windows weather widget that always shows the active
    /// output's icon, name and volume.
    /// Click to switch output, scroll to change volume, middle-click to mute, right-click for the menu,
    /// and drag to move it along the taskbar. Hovering opens a flyout with a volume slider.
    /// Optionally, a webcam section on the right shows whether a chosen webcam is on (a camera icon,
    /// struck through when off), and turns it on or off when clicked.
    /// </summary>
    /// <remarks>
    /// Windows 11 has no API for adding things to the taskbar, so this is a transparent, always-on-top
    /// window kept over the taskbar. It re-raises itself when the taskbar is clicked, and hides when the
    /// taskbar auto-hides or a full-screen app is in front.
    /// </remarks>
    internal sealed class TaskbarStrip : Form
    {
        const int WsExTopmost = 0x8;
        const int WsExToolWindow = 0x80;
        const int WsExLayered = 0x80000;
        const int WsExNoActivate = 0x08000000;
        const int MinLogicalWidth = 140;
        const int MaxLogicalWidth = 260;
        const int PlacementIntervalMilliseconds = 250;
        const int HoverDelayMilliseconds = 400;
        const int WheelNotch = 120;

        readonly float dpiScale;
        readonly Font nameFont;
        readonly Font volumeFont;
        readonly VolumeFlyout flyout = new VolumeFlyout();
        readonly Timer hoverTimer = new Timer { Interval = HoverDelayMilliseconds };
        readonly Timer placementTimer = new Timer { Interval = PlacementIntervalMilliseconds };
        readonly NativeMethods.WinEventProc onForegroundChanged; // Kept in a field so the GC doesn't collect it.
        IntPtr foregroundHook;

        enum Part
        {
            None,
            Audio,
            Webcam,
        }

        bool showOnTaskbar;
        OutputStatus status;
        WebcamStatus webcam; // Null hides the webcam section.
        bool lightTaskbar;
        Rectangle taskbar;
        int offset; // Pixels from the taskbar's left edge; negative means the default position.
        int audioWidth; // The audio section is on the left; the webcam section, if shown, fills the rest.
        Part hovered;
        bool pressed;
        bool dragging;
        int dragStartCursorX;
        int dragStartOffset;
        int wheelRemainder;
        string measuredName;
        int measuredWidth;

        public TaskbarStrip(int offset)
        {
            this.offset = offset;
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero))
                dpiScale = screen.DpiX / 96f;
            // Same type sizes as the taskbar's weather widget.
            nameFont = new Font("Segoe UI Semibold", 12f * dpiScale, FontStyle.Regular, GraphicsUnit.Pixel);
            volumeFont = new Font("Segoe UI", 12f * dpiScale, FontStyle.Regular, GraphicsUnit.Pixel);

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;

            placementTimer.Tick += (sender, e) => UpdatePlacement();
            onForegroundChanged = (hook, eventType, window, objectId, childId, threadId, time) => UpdatePlacement();

            hoverTimer.Tick += (sender, e) =>
            {
                hoverTimer.Stop();
                if (Visible && !pressed && AudioBounds.Contains(Cursor.Position))
                    flyout.ShowAbove(AudioBounds);
            };
            flyout.VolumeChangeRequested += (sender, level) => VolumeChangeRequested?.Invoke(this, level);
            flyout.VolumeStepsRequested += (sender, notches) => VolumeStepsRequested?.Invoke(this, notches);
            flyout.MuteRequested += (sender, e) => MuteRequested?.Invoke(this, e);
        }

        public event EventHandler NextRequested;

        public event EventHandler MuteRequested;

        /// <summary>Raised on mouse-wheel scrolling with the number of notches (positive is up).</summary>
        public event EventHandler<int> VolumeStepsRequested;

        /// <summary>Raised from the flyout's slider with the new volume from 0 to 1.</summary>
        public event EventHandler<float> VolumeChangeRequested;

        public event EventHandler MenuRequested;

        public event EventHandler WebcamToggleRequested;

        /// <summary>Raised after the strip has been dragged to a new position.</summary>
        public event EventHandler OffsetChanged;

        public int Offset => offset;

        /// <summary>Screen bounds of the audio section, which the volume flyout opens above.</summary>
        Rectangle AudioBounds => new Rectangle(Left, Top, audioWidth, Height);

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams createParams = base.CreateParams;
                createParams.ExStyle |= WsExTopmost | WsExToolWindow | WsExLayered | WsExNoActivate;
                return createParams;
            }
        }

        public void SetShowOnTaskbar(bool show)
        {
            showOnTaskbar = show;
            if (show)
            {
                placementTimer.Start();
                if (foregroundHook == IntPtr.Zero)
                {
                    foregroundHook = NativeMethods.SetWinEventHook(NativeMethods.EventSystemForeground, NativeMethods.EventSystemForeground,
                        IntPtr.Zero, onForegroundChanged, 0, 0, NativeMethods.WinEventOutOfContext);
                }
            }
            else
            {
                placementTimer.Stop();
                Unhook();
            }
            UpdatePlacement();
        }

        public void SetStatus(OutputStatus status, bool lightTaskbar)
        {
            this.status = status;
            this.lightTaskbar = lightTaskbar;
            flyout.SetStatus(status);
            UpdatePlacement(true);
        }

        public void SetWebcam(WebcamStatus webcam)
        {
            if (WebcamStatus.AreEqual(webcam, this.webcam))
                return;
            this.webcam = webcam;
            UpdatePlacement(true);
        }

        void UpdatePlacement() => UpdatePlacement(false);

        /// <summary>Moves the strip onto the taskbar and above it, or hides it when it shouldn't show.</summary>
        void UpdatePlacement(bool contentChanged)
        {
            if (IsDisposed)
                return;
            if (!showOnTaskbar
                || !Taskbar.TryGetVisibleBounds(out Rectangle bar, out Rectangle monitor)
                || Taskbar.IsFullScreenAppActive(monitor, Handle))
            {
                if (Visible)
                    Hide();
                flyout.Hide();
                return;
            }

            audioWidth = MeasureAudioWidth();
            int width = Math.Min(audioWidth + (webcam != null ? Px(44) : 0), bar.Width);
            int preferredOffset = offset >= 0 ? offset : DefaultOffset(bar.Width, width);
            int x = bar.Left + Math.Max(0, Math.Min(preferredOffset, bar.Width - width));
            var bounds = new Rectangle(x, bar.Top, width, bar.Height);
            taskbar = bar;

            if (contentChanged || bounds != Bounds || !Visible)
            {
                Bounds = bounds;
                Render();
            }
            if (!Visible)
                Show();
            flyout.SetAnchor(AudioBounds);

            // The taskbar comes to the front whenever it's clicked; stay above it.
            NativeMethods.SetWindowPos(Handle, NativeMethods.HwndTopmost, 0, 0, 0, 0,
                NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
        }

        // With centered taskbar icons the left end is free, but the Widgets (weather) button sits in
        // the corner, so start just to its right. With left-aligned icons the middle is free.
        int DefaultOffset(int barWidth, int width) => Taskbar.IconsAreLeftAligned() ? (barWidth - width) / 2 : Px(180);

        /// <summary>Fits the device name, like the weather widget sizes itself to its text.</summary>
        int MeasureAudioWidth()
        {
            string name = status?.Device.Name ?? "No output device";
            if (name != measuredName)
            {
                measuredName = name;
                measuredWidth = Math.Max(Px(MinLogicalWidth), Math.Min(Px(MaxLogicalWidth), Px(44) + MeasureText(name, nameFont) + Px(20)));
            }
            return measuredWidth;
        }

        static int MeasureText(string text, Font font)
        {
            using (var measuring = new Bitmap(1, 1))
            using (Graphics graphics = Graphics.FromImage(measuring))
            {
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                return (int)Math.Ceiling(graphics.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic).Width);
            }
        }

        Part PartAt(Point clientPoint) => webcam != null && clientPoint.X >= audioWidth ? Part.Webcam : Part.Audio;

        void Render()
        {
            if (Width <= 0 || Height <= 0)
                return;

            Color foreground = lightTaskbar ? Color.FromArgb(28, 28, 28) : Color.White;
            Color secondary = lightTaskbar ? Color.FromArgb(90, 90, 90) : Color.FromArgb(205, 205, 205);
            Color accent = lightTaskbar ? Color.FromArgb(0, 95, 184) : Color.FromArgb(96, 205, 255);
            Color track = Color.FromArgb(lightTaskbar ? 60 : 80, foreground);

            using (var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    // Nearly invisible, but fully transparent pixels would let clicks fall through to the taskbar.
                    graphics.Clear(Color.FromArgb(1, 0, 0, 0));
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                    Color highlight = Color.FromArgb(lightTaskbar ? 18 : 24, foreground);
                    int audioRight = webcam != null ? audioWidth : Width;
                    if (dragging)
                        Shapes.FillRoundedRectangle(graphics, highlight, new RectangleF(Px(2), Px(4), Width - Px(4), Height - Px(8)), Px(4));
                    else if (hovered == Part.Audio)
                        Shapes.FillRoundedRectangle(graphics, highlight, new RectangleF(Px(2), Px(4), audioRight - Px(4), Height - Px(8)), Px(4));
                    else if (hovered == Part.Webcam)
                        Shapes.FillRoundedRectangle(graphics, highlight, new RectangleF(audioRight + Px(2), Px(4), Width - audioRight - Px(4), Height - Px(8)), Px(4));

                    int middle = Height / 2;
                    int iconSize = Px(24);
                    DeviceIcons.Draw(graphics, status, new Rectangle(Px(10), middle - iconSize / 2, iconSize, iconSize), foreground);

                    float textLeft = Px(44);
                    float textWidth = audioRight - textLeft - Px(10);
                    using (var format = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center })
                    using (var foregroundBrush = new SolidBrush(foreground))
                    using (var secondaryBrush = new SolidBrush(secondary))
                    {
                        string name = status?.Device.Name ?? "No output device";
                        graphics.DrawString(name, nameFont, foregroundBrush, new RectangleF(textLeft, middle - Px(18), textWidth, Px(18)), format);

                        if (status != null)
                        {
                            // Second line, like the weather widget's "Sunny": "45%" followed by a level bar.
                            float labelWidth = Px(38);
                            graphics.DrawString(status.Muted ? "Muted" : status.VolumePercent + "%", volumeFont, secondaryBrush,
                                new RectangleF(textLeft, middle + Px(1), labelWidth + Px(8), Px(17)), format);

                            var bar = new RectangleF(textLeft + labelWidth + Px(4), middle + Px(8), textWidth - labelWidth - Px(4), Math.Max(2, Px(3)));
                            Shapes.FillPill(graphics, track, bar);
                            if (!status.Muted)
                                Shapes.FillPill(graphics, accent, new RectangleF(bar.X, bar.Y, bar.Width * Math.Min(100, status.VolumePercent) / 100f, bar.Height));
                        }

                        if (webcam != null)
                        {
                            // A thin divider, then the camera icon, struck through when off.
                            using (var divider = new Pen(Color.FromArgb(lightTaskbar ? 50 : 60, foreground), 1f))
                                graphics.DrawLine(divider, audioRight, Px(14), audioRight, Height - Px(14));

                            Color iconColor = webcam.Busy ? Color.FromArgb(110, foreground) : foreground;
                            Color behindIcon = !dragging && hovered == Part.Webcam ? highlight : Color.FromArgb(1, 0, 0, 0);
                            int camera = Px(20);
                            DrawWebcamIcon(graphics, new RectangleF(audioRight + Px(12), middle - camera / 2f, camera, camera), iconColor, !webcam.Enabled, behindIcon);
                        }
                    }
                }
                NativeMethods.SetLayeredWindowBitmap(Handle, bitmap, Location);
            }
        }

        /// <summary>A video-camera outline: a body and a lens, with a slash through it when off.</summary>
        static void DrawWebcamIcon(Graphics graphics, RectangleF box, Color color, bool off, Color behind)
        {
            float s = box.Width;
            using (var brush = new SolidBrush(color))
            {
                Shapes.FillRoundedRectangle(graphics, color, new RectangleF(box.X + s * 0.04f, box.Y + s * 0.26f, s * 0.62f, s * 0.48f), s * 0.1f);
                graphics.FillPolygon(brush, new[]
                {
                    new PointF(box.X + s * 0.70f, box.Y + s * 0.42f),
                    new PointF(box.X + s * 0.96f, box.Y + s * 0.27f),
                    new PointF(box.X + s * 0.96f, box.Y + s * 0.73f),
                    new PointF(box.X + s * 0.70f, box.Y + s * 0.58f),
                });
            }
            if (!off)
                return;

            var start = new PointF(box.X + s * 0.06f, box.Y + s * 0.06f);
            var end = new PointF(box.X + s * 0.94f, box.Y + s * 0.94f);
            // Cut a gap around the slash so it reads clearly against the icon.
            CompositingMode previous = graphics.CompositingMode;
            graphics.CompositingMode = CompositingMode.SourceCopy;
            using (var gap = new Pen(behind, s * 0.26f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                graphics.DrawLine(gap, start, end);
            graphics.CompositingMode = previous;
            using (var slash = new Pen(color, s * 0.1f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                graphics.DrawLine(slash, start, end);
        }

        void SetHovered(Part part)
        {
            if (part == hovered)
                return;
            hovered = part;
            Render();
            // Only the audio section opens the volume flyout; an open flyout closes itself once
            // the mouse is away from it and the audio section.
            hoverTimer.Stop();
            if (part == Part.Audio)
                hoverTimer.Start();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            SetHovered(PartAt(PointToClient(Cursor.Position)));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            SetHovered(Part.None);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;
            pressed = true;
            dragging = false;
            dragStartCursorX = Cursor.Position.X;
            dragStartOffset = Left - taskbar.Left;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!pressed)
            {
                SetHovered(PartAt(e.Location));
                return;
            }

            int distance = Cursor.Position.X - dragStartCursorX;
            if (!dragging && Math.Abs(distance) < SystemInformation.DragSize.Width)
                return;
            if (!dragging)
            {
                hoverTimer.Stop();
                flyout.Hide();
            }
            dragging = true;
            offset = Math.Max(0, dragStartOffset + distance);
            UpdatePlacement();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            switch (e.Button)
            {
                case MouseButtons.Left:
                    bool wasDragging = dragging;
                    pressed = false;
                    dragging = false;
                    if (wasDragging)
                    {
                        offset = Left - taskbar.Left; // Store where it actually ended up after clamping.
                        Render();
                        OffsetChanged?.Invoke(this, EventArgs.Empty);
                    }
                    else if (PartAt(e.Location) == Part.Webcam)
                    {
                        WebcamToggleRequested?.Invoke(this, EventArgs.Empty);
                    }
                    else
                    {
                        NextRequested?.Invoke(this, EventArgs.Empty);
                    }
                    break;
                case MouseButtons.Middle:
                    if (PartAt(e.Location) == Part.Audio)
                        MuteRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case MouseButtons.Right:
                    hoverTimer.Stop();
                    flyout.Hide();
                    MenuRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            // Precision touchpads send fractions of a notch; add them up.
            wheelRemainder += e.Delta;
            int notches = wheelRemainder / WheelNotch;
            wheelRemainder -= notches * WheelNotch;
            if (notches != 0)
                VolumeStepsRequested?.Invoke(this, notches);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Unhook();
                placementTimer.Dispose();
                hoverTimer.Dispose();
                flyout.Dispose();
                nameFont.Dispose();
                volumeFont.Dispose();
            }
            base.Dispose(disposing);
        }

        void Unhook()
        {
            if (foregroundHook == IntPtr.Zero)
                return;
            NativeMethods.UnhookWinEvent(foregroundHook);
            foregroundHook = IntPtr.Zero;
        }

        int Px(int value) => (int)Math.Round(value * dpiScale);
    }
}
