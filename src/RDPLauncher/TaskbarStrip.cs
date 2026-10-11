using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using RDPLauncher.Displays;

namespace RDPLauncher
{
    /// <summary>What the taskbar widget shows.</summary>
    internal sealed class WidgetContent
    {
        /// <summary>The connection's name, or what to do when there isn't one yet.</summary>
        public string Title = "";

        /// <summary>The second line under the title.</summary>
        public string Detail = "";

        public IReadOnlyList<LocalDisplay> Displays = new LocalDisplay[0];

        /// <summary>The display keys each display button uses; empty when there's nothing to connect to.</summary>
        public IReadOnlyList<IReadOnlyCollection<string>> Buttons = new IReadOnlyCollection<string>[0];

        /// <summary>The button whose session is starting, or -1.</summary>
        public int BusyButton = -1;

        public bool LightTaskbar;
    }

    internal sealed class DisplayToggleEventArgs : EventArgs
    {
        public DisplayToggleEventArgs(int button, string displayKey)
        {
            Button = button;
            DisplayKey = displayKey;
        }

        public int Button { get; }

        public string DisplayKey { get; }
    }

    /// <summary>
    /// A taskbar button in the style of the Windows weather widget: the connection's name, then a
    /// button for each set of displays ("1", "2", "3"). Click a number to start Remote Desktop on
    /// that many displays; hover it to see (and change) which ones. Click the name to pick another
    /// computer, or scroll over it to go through them. Right-click anywhere for the menu, and drag
    /// to move the widget along the taskbar.
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
        const int MinConnectionLogicalWidth = 120;
        const int MaxConnectionLogicalWidth = 220;
        const int ButtonLogicalWidth = 40;
        const int PlacementIntervalMilliseconds = 250;
        const int HoverDelayMilliseconds = 400;
        const int WheelNotch = 120;
        const int NoSection = -1;
        const int ConnectionSection = 0; // Display button i is section i + 1.

        sealed class Section
        {
            public int Id;
            public int X;
            public int Width;
        }

        readonly float dpiScale;
        readonly Font nameFont;
        readonly Font detailFont;
        readonly Font numberFont;
        readonly DisplayFlyout flyout = new DisplayFlyout();
        readonly Timer hoverTimer = new Timer { Interval = HoverDelayMilliseconds };
        readonly Timer placementTimer = new Timer { Interval = PlacementIntervalMilliseconds };
        readonly NativeMethods.WinEventProc onForegroundChanged; // Kept in a field so the GC doesn't collect it.
        readonly Dictionary<string, int> measuredWidths = new Dictionary<string, int>();
        IntPtr foregroundHook;
        int appBarMessage;
        bool shellReportsFullScreen;

        bool shown;
        WidgetContent content = new WidgetContent();
        Rectangle taskbar;
        int offset; // Pixels from the taskbar's left edge; negative means the default position.
        Section[] sections = new Section[0];
        int hovered = NoSection;
        int hoverTarget = NoSection; // The section the hover timer will open the flyout for.
        int flyoutSection = NoSection; // The section the flyout is showing.
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
            detailFont = new Font("Segoe UI", 12f * dpiScale, FontStyle.Regular, GraphicsUnit.Pixel);
            numberFont = new Font("Segoe UI Semibold", 11f * dpiScale, FontStyle.Regular, GraphicsUnit.Pixel);

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
                if (Visible && !pressed && bounds.Contains(Cursor.Position) && IsButton(hoverTarget))
                {
                    flyoutSection = hoverTarget;
                    UpdateFlyout();
                    flyout.ShowAbove(bounds);
                }
            };
            flyout.DisplayToggled += (sender, displayKey) =>
            {
                if (IsButton(flyoutSection))
                    DisplayToggleRequested?.Invoke(this, new DisplayToggleEventArgs(ButtonIndex(flyoutSection), displayKey));
            };
        }

        /// <summary>Raised when the connection's name is clicked.</summary>
        public event EventHandler ConnectionClicked;

        /// <summary>Raised on scrolling over the connection's name, with the notches (positive is up).</summary>
        public event EventHandler<int> ConnectionStepRequested;

        /// <summary>Raised when a display button is clicked, with the button's index.</summary>
        public event EventHandler<int> ConnectRequested;

        /// <summary>Raised when a display is clicked in a display button's flyout.</summary>
        public event EventHandler<DisplayToggleEventArgs> DisplayToggleRequested;

        public event EventHandler MenuRequested;

        /// <summary>Raised after the widget has been dragged to a new position.</summary>
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

        /// <summary>Shows or hides the widget.</summary>
        public void SetShown(bool shown)
        {
            if (shown == this.shown)
                return;
            this.shown = shown;

            // Only keep watching the taskbar while there's something to show on it.
            if (shown)
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
                hoverTimer.Stop();
                flyout.Hide();
            }
            UpdatePlacement(true);
        }

        public void SetContent(WidgetContent content)
        {
            this.content = content;
            if (IsButton(flyoutSection) && ButtonIndex(flyoutSection) < content.Buttons.Count)
                UpdateFlyout();
            else
                flyout.Hide();
            UpdatePlacement(true);
        }

        void UpdateFlyout()
        {
            int button = ButtonIndex(flyoutSection);
            if (button >= 0 && button < content.Buttons.Count)
                flyout.SetContent(content.Title, content.Displays, content.Buttons[button]);
        }

        static bool IsButton(int section) => section > ConnectionSection;

        static int ButtonIndex(int section) => section - 1;

        void UpdatePlacement() => UpdatePlacement(false);

        /// <summary>Moves the widget onto the taskbar and above it, or hides it when it shouldn't show.</summary>
        void UpdatePlacement(bool contentChanged)
        {
            if (IsDisposed)
                return;
            if (!shown
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
                flyout.SetAnchor(SectionScreenBounds(flyoutSection));

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
            appBarMessage = NativeMethods.RegisterWindowMessage("RDPLauncher.AppBarNotification");
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
        // the corner, so start to its right. With left-aligned icons the middle is free. Either way
        // leave room for AudioFlipper's widget, which starts in the same places.
        int DefaultOffset(int barWidth, int width) => Taskbar.IconsAreLeftAligned() ? barWidth / 2 + Px(140) : Px(180 + 280);

        void LayOutSections()
        {
            var laidOut = new List<Section> { new Section { Id = ConnectionSection, X = 0, Width = MeasureConnectionWidth() } };
            int x = laidOut[0].Width;
            for (int i = 0; i < content.Buttons.Count; i++)
            {
                laidOut.Add(new Section { Id = i + 1, X = x, Width = Px(ButtonLogicalWidth) });
                x += Px(ButtonLogicalWidth);
            }
            sections = laidOut.ToArray();
        }

        /// <summary>Fits the connection's name, like the weather widget sizes itself to its text.</summary>
        int MeasureConnectionWidth()
        {
            string measureKey = content.Title + "\n" + content.Detail;
            if (!measuredWidths.TryGetValue(measureKey, out int width))
            {
                using (var measuring = new Bitmap(1, 1))
                using (Graphics graphics = Graphics.FromImage(measuring))
                {
                    graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    float textWidth = Math.Max(
                        graphics.MeasureString(content.Title, nameFont, PointF.Empty, StringFormat.GenericTypographic).Width,
                        graphics.MeasureString(content.Detail, detailFont, PointF.Empty, StringFormat.GenericTypographic).Width);
                    width = Math.Max(Px(MinConnectionLogicalWidth),
                        Math.Min(Px(MaxConnectionLogicalWidth), Px(44) + (int)Math.Ceiling(textWidth) + Px(12)));
                }
                measuredWidths[measureKey] = width;
            }
            return width;
        }

        int SectionAt(Point clientPoint) => sections.FirstOrDefault(s => clientPoint.X >= s.X && clientPoint.X < s.X + s.Width)?.Id ?? NoSection;

        Rectangle SectionScreenBounds(int id)
        {
            Section section = sections.FirstOrDefault(s => s.Id == id);
            return section == null ? Rectangle.Empty : new Rectangle(Left + section.X, Top, section.Width, Height);
        }

        void Render()
        {
            if (Width <= 0 || Height <= 0)
                return;

            Color foreground = content.LightTaskbar ? Color.FromArgb(28, 28, 28) : Color.White;
            Color secondary = content.LightTaskbar ? Color.FromArgb(90, 90, 90) : Color.FromArgb(205, 205, 205);
            Color highlight = Color.FromArgb(content.LightTaskbar ? 18 : 24, foreground);

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

                    foreach (Section section in sections)
                    {
                        if (!dragging && hovered == section.Id)
                        {
                            Shapes.FillRoundedRectangle(graphics, highlight,
                                new RectangleF(section.X + Px(2), Px(4), section.Width - Px(4), Height - Px(8)), Px(4));
                        }

                        if (section.Id == ConnectionSection)
                        {
                            DrawConnection(graphics, section, foreground, secondary);
                            continue;
                        }

                        int button = ButtonIndex(section.Id);
                        if (button == 0)
                        {
                            using (var divider = new Pen(Color.FromArgb(content.LightTaskbar ? 50 : 60, foreground), 1f))
                                graphics.DrawLine(divider, section.X, Px(14), section.X, Height - Px(14));
                        }
                        Color iconColor = content.BusyButton == button ? Color.FromArgb(110, foreground) : foreground;
                        var icon = new RectangleF(section.X + (section.Width - Px(26)) / 2f, Height / 2f - Px(12), Px(26), Px(24));
                        Icons.DrawNumberedMonitor(graphics, icon, iconColor, content.Buttons[button].Count.ToString(), numberFont);
                    }
                }
                NativeMethods.SetLayeredWindowBitmap(Handle, bitmap, Location);
            }
        }

        /// <summary>The Remote Desktop icon, then the connection's name over the detail line, like the weather widget's two lines.</summary>
        void DrawConnection(Graphics graphics, Section section, Color foreground, Color secondary)
        {
            int middle = Height / 2;
            int iconSize = Px(24);
            Icons.DrawRemoteDesktop(graphics, new RectangleF(section.X + Px(10), middle - iconSize / 2f, iconSize, iconSize), foreground);

            float textLeft = section.X + Px(44);
            float textWidth = section.X + section.Width - textLeft - Px(8);
            using (var format = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center })
            using (var foregroundBrush = new SolidBrush(foreground))
            using (var secondaryBrush = new SolidBrush(secondary))
            {
                graphics.DrawString(content.Title, nameFont, foregroundBrush, new RectangleF(textLeft, middle - Px(18), textWidth, Px(18)), format);
                graphics.DrawString(content.Detail, detailFont, secondaryBrush, new RectangleF(textLeft, middle + Px(1), textWidth, Px(17)), format);
            }
        }

        void SetHovered(int section)
        {
            if (section == hovered)
                return;
            hovered = section;
            Render();
            // Display buttons open their flyout after a moment; an open flyout closes itself once
            // the mouse is away from it and its button.
            hoverTimer.Stop();
            if (IsButton(section))
            {
                hoverTarget = section;
                hoverTimer.Start();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            SetHovered(SectionAt(PointToClient(Cursor.Position)));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            SetHovered(NoSection);
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
                SetHovered(SectionAt(e.Location));
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
            // Pressing a button and sliding off the widget before letting go doesn't click it.
            int section = ClientRectangle.Contains(e.Location) ? SectionAt(e.Location) : NoSection;
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
                    else if (section == ConnectionSection)
                    {
                        hoverTimer.Stop();
                        flyout.Hide();
                        ConnectionClicked?.Invoke(this, EventArgs.Empty);
                    }
                    else if (IsButton(section))
                    {
                        hoverTimer.Stop();
                        flyout.Hide();
                        ConnectRequested?.Invoke(this, ButtonIndex(section));
                    }
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
            if (notches != 0 && SectionAt(PointToClient(Cursor.Position)) == ConnectionSection)
                ConnectionStepRequested?.Invoke(this, notches);
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
                detailFont.Dispose();
                numberFont.Dispose();
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
