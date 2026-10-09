using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;
using AudioFlipper.Audio;

namespace AudioFlipper
{
    /// <summary>
    /// A taskbar button in the style of the Windows weather widget that always shows the active
    /// output's icon, name and volume.
    /// Click to switch output, scroll to change volume, middle-click to mute, right-click for the menu,
    /// and drag to move it along the taskbar.
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
        const int WheelNotch = 120;

        readonly float dpiScale;
        readonly Font nameFont;
        readonly Font volumeFont;
        readonly ToolTip toolTip = new ToolTip { ShowAlways = true };
        readonly Timer placementTimer = new Timer { Interval = PlacementIntervalMilliseconds };
        readonly NativeMethods.WinEventProc onForegroundChanged; // Kept in a field so the GC doesn't collect it.
        IntPtr foregroundHook;

        bool showOnTaskbar;
        OutputStatus status;
        bool lightTaskbar;
        Rectangle taskbar;
        int offset; // Pixels from the taskbar's left edge; negative means the default position.
        bool hovered;
        bool pressed;
        bool dragging;
        int dragStartCursorX;
        int dragStartOffset;
        int wheelRemainder;
        string toolTipText;
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
        }

        public event EventHandler NextRequested;

        public event EventHandler MuteRequested;

        /// <summary>Raised on mouse-wheel scrolling with the number of notches (positive is up).</summary>
        public event EventHandler<int> VolumeStepsRequested;

        public event EventHandler MenuRequested;

        /// <summary>Raised after the strip has been dragged to a new position.</summary>
        public event EventHandler OffsetChanged;

        public int Offset => offset;

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
            UpdateToolTip();
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
                return;
            }

            int width = Math.Min(MeasureWidth(), bar.Width);
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

            // The taskbar comes to the front whenever it's clicked; stay above it.
            NativeMethods.SetWindowPos(Handle, NativeMethods.HwndTopmost, 0, 0, 0, 0,
                NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
        }

        // With centered taskbar icons the left end is free, but the Widgets (weather) button sits in
        // the corner, so start just to its right. With left-aligned icons the middle is free.
        int DefaultOffset(int barWidth, int width) => Taskbar.IconsAreLeftAligned() ? (barWidth - width) / 2 : Px(180);

        /// <summary>Fits the device name, like the weather widget sizes itself to its text.</summary>
        int MeasureWidth()
        {
            string name = status?.Device.Name ?? "No output device";
            if (name == measuredName)
                return measuredWidth;

            using (var measuring = new Bitmap(1, 1))
            using (Graphics graphics = Graphics.FromImage(measuring))
            {
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                int textWidth = (int)Math.Ceiling(graphics.MeasureString(name, nameFont, PointF.Empty, StringFormat.GenericTypographic).Width);
                measuredName = name;
                measuredWidth = Math.Max(Px(MinLogicalWidth), Math.Min(Px(MaxLogicalWidth), Px(44) + textWidth + Px(20)));
                return measuredWidth;
            }
        }

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
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                    if (hovered || dragging)
                    {
                        Shapes.FillRoundedRectangle(graphics, Color.FromArgb(lightTaskbar ? 18 : 24, foreground),
                            new RectangleF(Px(2), Px(4), Width - Px(4), Height - Px(8)), Px(4));
                    }

                    int middle = Height / 2;
                    int iconSize = Px(24);
                    DeviceIcons.Draw(graphics, status, new Rectangle(Px(10), middle - iconSize / 2, iconSize, iconSize), foreground);

                    float textLeft = Px(44);
                    float textWidth = Width - textLeft - Px(10);
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
                    }
                }
                NativeMethods.SetLayeredWindowBitmap(Handle, bitmap, Location);
            }
        }

        void UpdateToolTip()
        {
            string text = status == null
                ? "No output device"
                : status.Device.FullName + "\n" + (status.Muted ? "Muted" : "Volume " + status.VolumePercent + "%")
                    + "\nClick: next output · Scroll: volume · Drag: move";
            if (text == toolTipText)
                return;
            toolTipText = text;
            toolTip.SetToolTip(this, text);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hovered = true;
            Render();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovered = false;
            Render();
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
                return;

            int distance = Cursor.Position.X - dragStartCursorX;
            if (!dragging && Math.Abs(distance) < SystemInformation.DragSize.Width)
                return;
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
                    else
                    {
                        NextRequested?.Invoke(this, EventArgs.Empty);
                    }
                    break;
                case MouseButtons.Middle:
                    MuteRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case MouseButtons.Right:
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
                toolTip.Dispose();
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
