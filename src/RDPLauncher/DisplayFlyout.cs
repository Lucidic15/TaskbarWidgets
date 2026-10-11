using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using RDPLauncher.Displays;

namespace RDPLauncher
{
    /// <summary>
    /// Flyout shown above a display button while hovering it: a map of this PC's displays with the
    /// ones the button uses highlighted. Clicking a display adds it to the button or takes it off.
    /// It stays open while the mouse is on its way up from the widget, and closes as soon as the
    /// mouse leaves the flyout.
    /// </summary>
    internal sealed class DisplayFlyout : PopupWindow
    {
        // How long the mouse may be off both the widget and the flyout on its way up to the flyout.
        const int ApproachGraceMilliseconds = 350;

        readonly Timer hideCheckTimer = new Timer { Interval = 100 };
        readonly Font titleFont = new Font("Segoe UI Semibold", 10.5f);
        readonly Font detailFont = new Font("Segoe UI", 9f);

        string connectionName = "";
        IReadOnlyList<LocalDisplay> displays = new LocalDisplay[0];
        HashSet<string> chosen = new HashSet<string>();
        Rectangle anchor;
        int lastCursorNear;
        bool cursorEntered; // Once the mouse has been in the flyout, leaving it closes the flyout.
        string hoveredKey;
        string pressedKey;

        public DisplayFlyout()
        {
            Size = new Size(Px(320), Px(216));
            hideCheckTimer.Tick += (sender, e) => HideWhenCursorLeaves();
        }

        /// <summary>Raised when a display is clicked, with its <see cref="LocalDisplay.Key"/>.</summary>
        public event EventHandler<string> DisplayToggled;

        public void SetContent(string connectionName, IReadOnlyList<LocalDisplay> displays, IReadOnlyCollection<string> chosen)
        {
            this.connectionName = connectionName;
            this.displays = displays;
            this.chosen = new HashSet<string>(chosen);
            if (Visible)
                Invalidate();
        }

        /// <summary>Shows the flyout centered above <paramref name="anchor"/> (the display button).</summary>
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

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible)
                return;
            hideCheckTimer.Stop();
            hoveredKey = null;
            pressedKey = null;
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

        Rectangle MapBounds => new Rectangle(Px(16), Px(62), Width - Px(32), Px(110));

        /// <summary>Where each display is drawn: the real arrangement, scaled to fit the map and centered.</summary>
        List<KeyValuePair<LocalDisplay, RectangleF>> LayOutMap()
        {
            var layout = new List<KeyValuePair<LocalDisplay, RectangleF>>();
            if (displays.Count == 0)
                return layout;

            Rectangle map = MapBounds;
            int left = displays.Min(d => d.Bounds.Left);
            int top = displays.Min(d => d.Bounds.Top);
            int right = displays.Max(d => d.Bounds.Right);
            int bottom = displays.Max(d => d.Bounds.Bottom);
            float scale = Math.Min(map.Width / (float)Math.Max(1, right - left), map.Height / (float)Math.Max(1, bottom - top));
            float offsetX = map.X + (map.Width - (right - left) * scale) / 2;
            float offsetY = map.Y + (map.Height - (bottom - top) * scale) / 2;
            float gap = Px(2); // Separates displays that touch.
            foreach (LocalDisplay display in displays)
            {
                var rect = new RectangleF(offsetX + (display.Bounds.Left - left) * scale, offsetY + (display.Bounds.Top - top) * scale,
                    display.Bounds.Width * scale, display.Bounds.Height * scale);
                layout.Add(new KeyValuePair<LocalDisplay, RectangleF>(display, RectangleF.Inflate(rect, -gap, -gap)));
            }
            return layout;
        }

        string KeyAt(Point point) => LayOutMap().FirstOrDefault(entry => entry.Value.Contains(point)).Key?.Key;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string key = KeyAt(e.Location);
            if (key != hoveredKey)
            {
                hoveredKey = key;
                Cursor = key != null ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
                pressedKey = KeyAt(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            string key = KeyAt(e.Location);
            if (e.Button == MouseButtons.Left && key != null && key == pressedKey)
                DisplayToggled?.Invoke(this, key);
            pressedKey = null;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            cursorEntered = true;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Hide();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Palette colors = Palette.Current();
            Graphics graphics = e.Graphics;
            graphics.Clear(colors.Background);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

            List<LocalDisplay> sessionOrder = DisplayChoices.InSessionOrder(displays, chosen);
            LocalDisplay mainDisplay = sessionOrder.FirstOrDefault();

            TextRenderer.DrawText(graphics, connectionName + " on " + DisplayChoices.Describe(sessionOrder.Count), titleFont,
                new Rectangle(Px(16), Px(12), Width - Px(32), Px(22)), colors.Foreground, flags);
            TextRenderer.DrawText(graphics, "Click a display to add or remove it.", detailFont,
                new Rectangle(Px(16), Px(34), Width - Px(32), Px(18)), colors.Secondary, flags);

            foreach (KeyValuePair<LocalDisplay, RectangleF> entry in LayOutMap())
                DrawDisplay(graphics, entry.Key, entry.Value, chosen.Contains(entry.Key.Key), entry.Key == mainDisplay, colors);

            // What the hovered display is, or what the main display is.
            LocalDisplay described = displays.FirstOrDefault(d => d.Key == hoveredKey) ?? mainDisplay;
            if (described != null)
            {
                string text = described.Name + " · " + described.Resolution;
                if (described == mainDisplay)
                    text += " · main display";
                else if (!chosen.Contains(described.Key))
                    text += " · not used";
                TextRenderer.DrawText(graphics, text, detailFont,
                    new Rectangle(Px(16), Height - Px(36), Width - Px(32), Px(22)), colors.Secondary, flags | TextFormatFlags.HorizontalCenter);
            }

            DrawBorder(graphics, colors.Border);
        }

        /// <summary>
        /// A chosen display is filled with the accent color and ticked; the session's main display also
        /// gets a taskbar along its bottom edge.
        /// </summary>
        void DrawDisplay(Graphics graphics, LocalDisplay display, RectangleF rect, bool isChosen, bool isMain, Palette colors)
        {
            bool isHovered = display.Key == hoveredKey;
            float radius = Px(4);
            if (isChosen)
            {
                Shapes.FillRoundedRectangle(graphics, colors.Accent, rect, radius);
                if (isHovered)
                    Shapes.FillRoundedRectangle(graphics, colors.Hover, rect, radius);
            }
            else
            {
                Shapes.FillRoundedRectangle(graphics, isHovered ? colors.Track : colors.Hover, rect, radius);
                Shapes.DrawRoundedRectangle(graphics, colors.Track, rect, radius, Math.Max(1f, Px(1)));
            }

            Color mark = isChosen ? colors.OnAccent : colors.Secondary;
            if (isMain)
            {
                float barHeight = Math.Max(2f, Math.Min(Px(5), rect.Height * 0.1f));
                Shapes.FillRoundedRectangle(graphics, Color.FromArgb(150, mark),
                    new RectangleF(rect.X + Px(3), rect.Bottom - barHeight - Px(3), rect.Width - Px(6), barHeight), barHeight / 2);
            }
            float check = Math.Min(Px(20), Math.Min(rect.Width, rect.Height) * 0.5f);
            if (isChosen && check >= Px(8))
                Icons.DrawCheck(graphics, new RectangleF(rect.X + (rect.Width - check) / 2, rect.Y + (rect.Height - check) / 2, check, check), mark);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                hideCheckTimer.Dispose();
                titleFont.Dispose();
                detailFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
