using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AudioFlipper.Audio;
using AudioFlipper.Webcams;

namespace AudioFlipper
{
    internal sealed class AudioFlowEventArgs : EventArgs
    {
        public AudioFlowEventArgs(EDataFlow flow) => Flow = flow;

        public EDataFlow Flow { get; }
    }

    internal sealed class VolumeStepsEventArgs : EventArgs
    {
        public VolumeStepsEventArgs(EDataFlow flow, int notches)
        {
            Flow = flow;
            Notches = notches;
        }

        public EDataFlow Flow { get; }

        /// <summary>Mouse-wheel notches; positive is up.</summary>
        public int Notches { get; }
    }

    internal sealed class VolumeLevelEventArgs : EventArgs
    {
        public VolumeLevelEventArgs(EDataFlow flow, float level)
        {
            Flow = flow;
            Level = level;
        }

        public EDataFlow Flow { get; }

        /// <summary>Volume from 0 to 1.</summary>
        public float Level { get; }
    }

    /// <summary>
    /// A taskbar button in the style of the Windows weather widget, with up to three sections that
    /// can each be shown or hidden:
    /// - Output: the active output's icon, name and volume. Click to switch output, scroll to change
    ///   volume, middle-click to mute, hover for a volume slider.
    /// - Input: the same for the active microphone.
    /// - Webcam: a camera icon, struck through while the chosen webcam is off; click to switch it.
    /// Right-click anywhere for the menu, and drag to move the widget along the taskbar.
    /// </summary>
    /// <remarks>
    /// Windows 11 has no API for adding things to the taskbar, so this is a transparent, always-on-top
    /// window kept just above the taskbar. It moves back above the taskbar when the taskbar is clicked,
    /// and hides when the taskbar auto-hides or a full-screen app is in front. To know about full-screen
    /// apps it registers as an app bar (without reserving any screen space), the same way the taskbar
    /// learns to step aside for them.
    /// </remarks>
    internal sealed class TaskbarStrip : Form
    {
        const int WsExTopmost = 0x8;
        const int WsExToolWindow = 0x80;
        const int WsExLayered = 0x80000;
        const int WsExNoActivate = 0x08000000;
        const int MinAudioLogicalWidth = 140;
        const int MaxAudioLogicalWidth = 260;
        const int WebcamLogicalWidth = 44;
        const int PlacementIntervalMilliseconds = 250;
        const int HoverDelayMilliseconds = 400;
        const int WheelNotch = 120;

        enum Part
        {
            None,
            Output,
            Input,
            Webcam,
        }

        sealed class Section
        {
            public Part Part;
            public int X;
            public int Width;
        }

        readonly float dpiScale;
        readonly Font nameFont;
        readonly Font volumeFont;
        readonly VolumeFlyout flyout = new VolumeFlyout();
        readonly Timer hoverTimer = new Timer { Interval = HoverDelayMilliseconds };
        readonly Timer placementTimer = new Timer { Interval = PlacementIntervalMilliseconds };
        readonly NativeMethods.WinEventProc onForegroundChanged; // Kept in a field so the GC doesn't collect it.
        readonly Dictionary<string, int> measuredWidths = new Dictionary<string, int>();
        IntPtr foregroundHook;
        int appBarMessage;
        bool shellReportsFullScreen;

        bool showOutput;
        bool showInput;
        AudioStatus output;
        AudioStatus input;
        WebcamStatus webcam; // Null hides the webcam section.
        bool lightTaskbar;
        Rectangle taskbar;
        int offset; // Pixels from the taskbar's left edge; negative means the default position.
        Section[] sections = new Section[0];
        Part hovered;
        Part hoverTarget; // The section the hover timer will open the flyout for.
        Part flyoutPart; // The section the flyout is showing.
        bool pressed;
        bool dragging;
        int dragStartCursorX;
        int dragStartOffset;
        int wheelRemainder;

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
                Rectangle bounds = SectionScreenBounds(hoverTarget);
                if (Visible && !pressed && bounds.Contains(Cursor.Position))
                {
                    flyoutPart = hoverTarget;
                    flyout.SetStatus(StatusOf(flyoutPart));
                    flyout.ShowAbove(bounds);
                }
            };
            flyout.VolumeChangeRequested += (sender, level) =>
            {
                if (FlowOf(flyoutPart) is EDataFlow flow)
                    VolumeChangeRequested?.Invoke(this, new VolumeLevelEventArgs(flow, level));
            };
            flyout.VolumeStepsRequested += (sender, notches) =>
            {
                if (FlowOf(flyoutPart) is EDataFlow flow)
                    VolumeStepsRequested?.Invoke(this, new VolumeStepsEventArgs(flow, notches));
            };
            flyout.MuteRequested += (sender, e) =>
            {
                if (FlowOf(flyoutPart) is EDataFlow flow)
                    MuteRequested?.Invoke(this, new AudioFlowEventArgs(flow));
            };
        }

        /// <summary>Raised when the output or input section is clicked.</summary>
        public event EventHandler<AudioFlowEventArgs> NextRequested;

        public event EventHandler<AudioFlowEventArgs> MuteRequested;

        public event EventHandler<VolumeStepsEventArgs> VolumeStepsRequested;

        /// <summary>Raised from the flyout's slider.</summary>
        public event EventHandler<VolumeLevelEventArgs> VolumeChangeRequested;

        public event EventHandler MenuRequested;

        public event EventHandler WebcamToggleRequested;

        /// <summary>Raised after the widget has been dragged to a new position.</summary>
        public event EventHandler OffsetChanged;

        public int Offset => offset;

        /// <summary>Whether the output section is on screen right now.</summary>
        public bool ShowsOutput => Visible && showOutput;

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

        bool HasSections => showOutput || showInput || webcam != null;

        /// <summary>Shows or hides the output and input sections. The widget hides when no section is shown.</summary>
        public void SetSections(bool showOutput, bool showInput)
        {
            if (showOutput == this.showOutput && showInput == this.showInput)
                return;
            this.showOutput = showOutput;
            this.showInput = showInput;
            SectionsChanged();
        }

        public void SetStatus(AudioStatus output, AudioStatus input, bool lightTaskbar)
        {
            this.output = output;
            this.input = input;
            this.lightTaskbar = lightTaskbar;
            if (flyoutPart != Part.None)
                flyout.SetStatus(StatusOf(flyoutPart));
            UpdatePlacement(true);
        }

        /// <summary>The webcam's state, or null to hide the webcam section.</summary>
        public void SetWebcam(WebcamStatus webcam)
        {
            if (WebcamStatus.AreEqual(webcam, this.webcam))
                return;
            bool sectionsChanged = (webcam == null) != (this.webcam == null);
            this.webcam = webcam;
            if (sectionsChanged)
                SectionsChanged();
            else
                UpdatePlacement(true);
        }

        void SectionsChanged()
        {
            // Only keep watching the taskbar while there's something to show on it.
            if (HasSections)
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
            if (!SectionShown(flyoutPart))
                flyout.Hide();
            UpdatePlacement(true);
        }

        bool SectionShown(Part part) =>
            part == Part.Output ? showOutput : part == Part.Input ? showInput : part == Part.Webcam && webcam != null;

        void UpdatePlacement() => UpdatePlacement(false);

        /// <summary>Moves the widget onto the taskbar and above it, or hides it when it shouldn't show.</summary>
        void UpdatePlacement(bool contentChanged)
        {
            if (IsDisposed)
                return;
            if (!HasSections
                || !Taskbar.TryGetVisibleBounds(out Rectangle bar, out Rectangle monitor)
                || Taskbar.IsFullScreenAppActive(monitor, Handle, shellReportsFullScreen))
            {
                if (Visible)
                    Hide();
                flyout.Hide();
                return;
            }

            LayOutSections();
            int width = Math.Min(sections.Sum(s => s.Width), bar.Width);
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
            if (flyout.Visible)
                flyout.SetAnchor(SectionScreenBounds(flyoutPart));

            StayAboveTaskbar();
        }

        /// <summary>
        /// The taskbar comes to the front whenever it's clicked, so move back above it. Only just above
        /// it, though: windows that belong over the taskbar, like a screenshot tool's overlay or an
        /// always-on-top window, stay over the widget too.
        /// </summary>
        void StayAboveTaskbar()
        {
            IntPtr taskbarWindow = Taskbar.FindWindow();
            if (taskbarWindow == IntPtr.Zero)
                return;
            for (IntPtr window = NativeMethods.GetWindow(taskbarWindow, NativeMethods.GwHwndPrev);
                 window != IntPtr.Zero;
                 window = NativeMethods.GetWindow(window, NativeMethods.GwHwndPrev))
            {
                if (window == Handle)
                    return; // Already above the taskbar.
            }

            // Placing the widget after (below) the window just above the taskbar puts it directly over the taskbar.
            IntPtr justAbove = NativeMethods.GetWindow(taskbarWindow, NativeMethods.GwHwndPrev);
            NativeMethods.SetWindowPos(Handle, justAbove != IntPtr.Zero ? justAbove : NativeMethods.HwndTopmost, 0, 0, 0, 0,
                NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            appBarMessage = NativeMethods.RegisterWindowMessage("AudioFlipper.AppBarNotification");
            NativeMethods.AppBarData data = AppBarData();
            NativeMethods.SHAppBarMessage(NativeMethods.AbmNew, ref data);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            NativeMethods.AppBarData data = AppBarData();
            NativeMethods.SHAppBarMessage(NativeMethods.AbmRemove, ref data);
            base.OnHandleDestroyed(e);
        }

        NativeMethods.AppBarData AppBarData() => new NativeMethods.AppBarData
        {
            Size = Marshal.SizeOf(typeof(NativeMethods.AppBarData)),
            Window = Handle,
            CallbackMessage = appBarMessage,
        };

        protected override void WndProc(ref Message m)
        {
            // Windows says a full-screen app opened or closed (lParam is true while one is open).
            if (appBarMessage != 0 && m.Msg == appBarMessage && m.WParam.ToInt64() == NativeMethods.AbnFullScreenApp)
            {
                shellReportsFullScreen = m.LParam != IntPtr.Zero;
                UpdatePlacement();
                return;
            }
            base.WndProc(ref m);
        }

        // With centered taskbar icons the left end is free, but the Widgets (weather) button sits in
        // the corner, so start just to its right. With left-aligned icons the middle is free.
        int DefaultOffset(int barWidth, int width) => Taskbar.IconsAreLeftAligned() ? (barWidth - width) / 2 : Px(180);

        void LayOutSections()
        {
            var laidOut = new List<Section>();
            int x = 0;
            void Add(Part part, int width)
            {
                laidOut.Add(new Section { Part = part, X = x, Width = width });
                x += width;
            }

            if (showOutput)
                Add(Part.Output, MeasureAudioWidth(NameOf(Part.Output)));
            if (showInput)
                Add(Part.Input, MeasureAudioWidth(NameOf(Part.Input)));
            if (webcam != null)
                Add(Part.Webcam, Px(WebcamLogicalWidth));
            sections = laidOut.ToArray();
        }

        string NameOf(Part part)
        {
            AudioStatus status = StatusOf(part);
            if (status != null)
                return status.Device.Name;
            return part == Part.Input ? "No input device" : "No output device";
        }

        AudioStatus StatusOf(Part part) => part == Part.Output ? output : part == Part.Input ? input : null;

        static EDataFlow? FlowOf(Part part) =>
            part == Part.Output ? EDataFlow.Render : part == Part.Input ? EDataFlow.Capture : (EDataFlow?)null;

        /// <summary>Fits the device name, like the weather widget sizes itself to its text.</summary>
        int MeasureAudioWidth(string name)
        {
            if (!measuredWidths.TryGetValue(name, out int width))
            {
                using (var measuring = new Bitmap(1, 1))
                using (Graphics graphics = Graphics.FromImage(measuring))
                {
                    graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    int textWidth = (int)Math.Ceiling(graphics.MeasureString(name, nameFont, PointF.Empty, StringFormat.GenericTypographic).Width);
                    width = Math.Max(Px(MinAudioLogicalWidth), Math.Min(Px(MaxAudioLogicalWidth), Px(44) + textWidth + Px(20)));
                }
                measuredWidths[name] = width;
            }
            return width;
        }

        Part PartAt(Point clientPoint) => sections.FirstOrDefault(s => clientPoint.X >= s.X && clientPoint.X < s.X + s.Width)?.Part ?? Part.None;

        Rectangle SectionScreenBounds(Part part)
        {
            Section section = sections.FirstOrDefault(s => s.Part == part);
            return section == null ? Rectangle.Empty : new Rectangle(Left + section.X, Top, section.Width, Height);
        }

        void Render()
        {
            if (Width <= 0 || Height <= 0)
                return;

            Color foreground = lightTaskbar ? Color.FromArgb(28, 28, 28) : Color.White;
            Color highlight = Color.FromArgb(lightTaskbar ? 18 : 24, foreground);

            using (var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    // Nearly invisible, but fully transparent pixels would let clicks fall through to the taskbar.
                    graphics.Clear(Color.FromArgb(1, 0, 0, 0));
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                    if (dragging)
                        Shapes.FillRoundedRectangle(graphics, highlight, new RectangleF(Px(2), Px(4), Width - Px(4), Height - Px(8)), Px(4));

                    for (int i = 0; i < sections.Length; i++)
                    {
                        Section section = sections[i];
                        bool isHovered = !dragging && hovered == section.Part;
                        if (isHovered)
                        {
                            Shapes.FillRoundedRectangle(graphics, highlight,
                                new RectangleF(section.X + Px(2), Px(4), section.Width - Px(4), Height - Px(8)), Px(4));
                        }
                        if (i > 0)
                        {
                            using (var divider = new Pen(Color.FromArgb(lightTaskbar ? 50 : 60, foreground), 1f))
                                graphics.DrawLine(divider, section.X, Px(14), section.X, Height - Px(14));
                        }

                        if (section.Part == Part.Webcam)
                        {
                            Color iconColor = webcam.Busy ? Color.FromArgb(110, foreground) : foreground;
                            Color behindIcon = isHovered ? highlight : Color.FromArgb(1, 0, 0, 0);
                            int camera = Px(20);
                            DrawWebcamIcon(graphics, new RectangleF(section.X + Px(12), Height / 2 - camera / 2f, camera, camera),
                                iconColor, !webcam.Enabled, behindIcon);
                        }
                        else
                        {
                            DrawAudioSection(graphics, section, StatusOf(section.Part), NameOf(section.Part), foreground);
                        }
                    }
                }
                NativeMethods.SetLayeredWindowBitmap(Handle, bitmap, Location);
            }
        }

        /// <summary>Device icon, then its name over "45%" and a level bar, like the weather widget's two lines.</summary>
        void DrawAudioSection(Graphics graphics, Section section, AudioStatus status, string name, Color foreground)
        {
            Color secondary = lightTaskbar ? Color.FromArgb(90, 90, 90) : Color.FromArgb(205, 205, 205);
            Color accent = lightTaskbar ? Color.FromArgb(0, 95, 184) : Color.FromArgb(96, 205, 255);
            Color track = Color.FromArgb(lightTaskbar ? 60 : 80, foreground);

            int middle = Height / 2;
            int iconSize = Px(24);
            DeviceIcons.Draw(graphics, status, new Rectangle(section.X + Px(10), middle - iconSize / 2, iconSize, iconSize), foreground);

            float textLeft = section.X + Px(44);
            float textWidth = section.X + section.Width - textLeft - Px(10);
            using (var format = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center })
            using (var foregroundBrush = new SolidBrush(foreground))
            using (var secondaryBrush = new SolidBrush(secondary))
            {
                graphics.DrawString(name, nameFont, foregroundBrush, new RectangleF(textLeft, middle - Px(18), textWidth, Px(18)), format);
                if (status == null)
                    return;

                float labelWidth = Px(38);
                graphics.DrawString(status.Muted ? "Muted" : status.VolumePercent + "%", volumeFont, secondaryBrush,
                    new RectangleF(textLeft, middle + Px(1), labelWidth + Px(8), Px(17)), format);

                var bar = new RectangleF(textLeft + labelWidth + Px(4), middle + Px(8), textWidth - labelWidth - Px(4), Math.Max(2, Px(3)));
                Shapes.FillPill(graphics, track, bar);
                if (!status.Muted)
                    Shapes.FillPill(graphics, accent, new RectangleF(bar.X, bar.Y, bar.Width * Math.Min(100, status.VolumePercent) / 100f, bar.Height));
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
            // The output and input sections open the volume flyout after a moment; an open flyout
            // closes itself once the mouse is away from it and its section.
            hoverTimer.Stop();
            if (FlowOf(part) != null)
            {
                hoverTarget = part;
                hoverTimer.Start();
            }
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
            Part part = PartAt(e.Location);
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
                    else if (part == Part.Webcam)
                    {
                        WebcamToggleRequested?.Invoke(this, EventArgs.Empty);
                    }
                    else if (FlowOf(part) is EDataFlow flow)
                    {
                        NextRequested?.Invoke(this, new AudioFlowEventArgs(flow));
                    }
                    break;
                case MouseButtons.Middle:
                    if (FlowOf(part) is EDataFlow muteFlow)
                        MuteRequested?.Invoke(this, new AudioFlowEventArgs(muteFlow));
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
            if (notches != 0 && FlowOf(PartAt(PointToClient(Cursor.Position))) is EDataFlow flow)
                VolumeStepsRequested?.Invoke(this, new VolumeStepsEventArgs(flow, notches));
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
