using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AnstossTrainer.Shared
{
    /// <summary>
    /// Zooms an absolutely laid-out view to fill whatever size its host gives it. Every view was
    /// designed pixel-exact for a fixed 630x423 content area, with no anchors, docks or layout
    /// panels, so instead of re-flowing the controls this remembers each one's design bounds and
    /// font once and maps them onto the new size: positions and sizes stretch per axis, fonts (and
    /// RichTextBox zoom) grow with the smaller of the two factors so text always fits its control.
    /// </summary>
    internal sealed class ViewScaler : IDisposable
    {
        private const int WM_SETREDRAW = 0x000B;

        // Font sizes are snapped to quarter points so a live window drag doesn't create a new font
        // for every pixel, and doesn't flicker text between nearly identical sizes.
        private const float FontStep = 0.25f;

        private readonly Control root;
        private readonly Size baseSize;
        private readonly HashSet<Control> tracked = new HashSet<Control>();
        private readonly Dictionary<Control, Rectangle> baseBounds = new Dictionary<Control, Rectangle>();
        private readonly Dictionary<Control, Font> baseFonts = new Dictionary<Control, Font>();
        private readonly Dictionary<ColumnHeader, int> baseColumnWidths = new Dictionary<ColumnHeader, int>();
        private readonly List<RichTextBox> richTextBoxes = new List<RichTextBox>();
        private readonly List<ScrollableControl> scrollables = new List<ScrollableControl>();
        private Dictionary<Font, Font> scaledFonts = new Dictionary<Font, Font>();

        /// <summary>Horizontal factor currently applied, relative to the design size.</summary>
        internal float ScaleX { get; private set; } = 1f;
        /// <summary>Vertical factor currently applied, relative to the design size.</summary>
        internal float ScaleY { get; private set; } = 1f;
        /// <summary>Factor fonts are currently scaled by - the smaller of the two axes.</summary>
        internal float FontScale => Math.Min(this.ScaleX, this.ScaleY);

        /// <summary>
        /// Takes the root's current layout as the design state. Call it once the root has been laid
        /// out at its unscaled size (i.e. after any DPI auto-scaling) and before anything in it has
        /// been scrolled, since everything is measured against what the controls look like right now.
        /// </summary>
        internal ViewScaler(Control root)
        {
            this.root = root;
            this.baseSize = root.Size;

            // A ContainerControl in AutoScaleMode.Font rescales all of its children by itself as
            // soon as its font changes - which would compound with the scaling done here.
            if (root is ContainerControl container)
                container.AutoScaleMode = AutoScaleMode.None;

            // The root's font is always set explicitly, so every child that merely inherits it
            // follows along without having to be tracked itself.
            this.baseFonts[root] = root.Font;
            this.root.ControlAdded += this.OnControlAdded;
            this.root.ControlRemoved += this.OnControlRemoved;
            foreach (Control child in root.Controls)
                this.Track(child);
        }

        /// <summary>Resizes the root to <paramref name="size"/> and scales all its content to match.</summary>
        internal void ScaleTo(Size size)
        {
            if (this.baseSize.Width <= 0 || this.baseSize.Height <= 0 || size.Width <= 0 || size.Height <= 0)
                return;

            float previousX = this.ScaleX;
            float previousY = this.ScaleY;
            this.ScaleX = size.Width / (float)this.baseSize.Width;
            this.ScaleY = size.Height / (float)this.baseSize.Height;

            bool redrawSuspended = this.root.IsHandleCreated;
            if (redrawSuspended)
                SendMessage(this.root.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);

            // Inside a scrolled-down panel a child's Location is relative to the visible origin, not
            // the content's - so scroll back to the top while the design positions are applied, then
            // return to the same spot of the (now larger or smaller) content.
            List<Point> scrollPositions = new List<Point>();
            foreach (ScrollableControl scrollable in this.scrollables)
            {
                scrollPositions.Add(scrollable.AutoScrollPosition);
                scrollable.AutoScrollPosition = Point.Empty;
            }

            this.root.SuspendLayout();
            try
            {
                // Fonts first: auto-sized controls (labels, single-line text boxes, combo boxes) take
                // their size from the font, and should already have it when they get repositioned.
                // A RichTextBox applies any font change - even one merely inherited from its parent -
                // to its entire content, flattening the RTF's own sizes and styles, so its content is
                // put back afterwards and the zoom below does its scaling instead.
                List<KeyValuePair<RichTextBox, string>> contents = new List<KeyValuePair<RichTextBox, string>>();
                List<Font> fontsBefore = new List<Font>();
                foreach (RichTextBox richTextBox in this.richTextBoxes)
                {
                    contents.Add(new KeyValuePair<RichTextBox, string>(richTextBox, richTextBox.Rtf));
                    fontsBefore.Add(richTextBox.Font);
                }
                this.ApplyFonts();
                for (int i = 0; i < contents.Count; i++)
                {
                    if (!ReferenceEquals(contents[i].Key.Font, fontsBefore[i]))
                        contents[i].Key.Rtf = contents[i].Value;
                }
                foreach (KeyValuePair<Control, Rectangle> entry in this.baseBounds)
                    entry.Key.Bounds = this.ScaleRectangle(entry.Value);
                foreach (KeyValuePair<ColumnHeader, int> entry in this.baseColumnWidths)
                    entry.Key.Width = (int)Math.Round(entry.Value * this.ScaleX);
                foreach (RichTextBox richTextBox in this.richTextBoxes)
                    this.ApplyZoom(richTextBox);
                this.root.Size = size;
            }
            finally
            {
                this.root.ResumeLayout();
                // AutoScrollPosition reads back negative but is set with positive offsets.
                for (int i = 0; i < scrollPositions.Count; i++)
                {
                    this.scrollables[i].AutoScrollPosition = new Point(
                        (int)Math.Round(-scrollPositions[i].X * this.ScaleX / previousX),
                        (int)Math.Round(-scrollPositions[i].Y * this.ScaleY / previousY));
                }
                if (redrawSuspended)
                {
                    SendMessage(this.root.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
                    this.root.Invalidate(true);
                }
            }
        }

        /// <summary>Stops following the root's control tree and releases the scaled fonts.</summary>
        public void Dispose()
        {
            this.root.ControlAdded -= this.OnControlAdded;
            this.root.ControlRemoved -= this.OnControlRemoved;
            foreach (Control control in new List<Control>(this.tracked))
                this.Untrack(control);
            this.DisposeScaledFonts(this.scaledFonts, null);
            this.scaledFonts.Clear();
        }

        // Scaling the edges rather than location and size separately keeps neighbouring controls
        // that touched in the design touching after rounding.
        private Rectangle ScaleRectangle(Rectangle bounds)
        {
            int left = (int)Math.Round(bounds.Left * this.ScaleX);
            int top = (int)Math.Round(bounds.Top * this.ScaleY);
            int right = (int)Math.Round(bounds.Right * this.ScaleX);
            int bottom = (int)Math.Round(bounds.Bottom * this.ScaleY);
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private void ApplyFonts()
        {
            Dictionary<Font, Font> previous = this.scaledFonts;
            Dictionary<Font, Font> current = new Dictionary<Font, Font>();
            foreach (KeyValuePair<Control, Font> entry in this.baseFonts)
            {
                if (!current.TryGetValue(entry.Value, out Font scaled))
                {
                    scaled = this.ScaleFont(entry.Value, previous);
                    current[entry.Value] = scaled;
                }
                entry.Key.Font = scaled;
            }
            this.scaledFonts = current;
            this.DisposeScaledFonts(previous, current);
        }

        private Font ScaleFont(Font font, Dictionary<Font, Font> previous)
        {
            float size = (float)(Math.Round(font.Size * this.FontScale / FontStep) * FontStep);
            if (size <= 0f || size == font.Size)
                return font;
            if (previous.TryGetValue(font, out Font reused) && reused.Size == size)
                return reused;
            return new Font(font.FontFamily, size, font.Style, font.Unit, font.GdiCharSet, font.GdiVerticalFont);
        }

        // Only fonts this class created itself get disposed - never a design-time font, and never
        // one that's still handed out at the current scale.
        private void DisposeScaledFonts(Dictionary<Font, Font> fonts, Dictionary<Font, Font> keep)
        {
            HashSet<Font> stillUsed = keep == null ? new HashSet<Font>() : new HashSet<Font>(keep.Values);
            foreach (KeyValuePair<Font, Font> entry in fonts)
            {
                if (!ReferenceEquals(entry.Key, entry.Value) && !stillUsed.Contains(entry.Value))
                    entry.Value.Dispose();
            }
        }

        // RichTextBox content carries its own per-run fonts in the RTF, so its zoom factor scales the
        // whole document instead of its font. Bouncing through 1 forces the zoom to be re-sent, since the control drops it silently
        // whenever its content is replaced while the property keeps reporting the old value.
        private void ApplyZoom(RichTextBox richTextBox)
        {
            float zoom = Math.Max(1f / 64f, Math.Min(64f, this.FontScale));
            richTextBox.ZoomFactor = 1f;
            richTextBox.ZoomFactor = zoom;
        }

        private void Track(Control control)
        {
            if (!this.tracked.Add(control))
                return;

            // Docked controls and tab pages are positioned by their parent - setting their bounds
            // here would only fight that layout - but their children still need scaling.
            if (control.Dock == DockStyle.None && !(control is TabPage))
                this.baseBounds[control] = control.Bounds;

            if (control is ScrollableControl scrollable && scrollable.AutoScroll)
                this.scrollables.Add(scrollable);

            if (control is RichTextBox richTextBox)
                this.richTextBoxes.Add(richTextBox);
            else if (control.Parent != null && !control.Font.Equals(control.Parent.Font))
                this.baseFonts[control] = control.Font;

            if (control is ListView listView)
            {
                foreach (ColumnHeader column in listView.Columns)
                    this.baseColumnWidths[column] = column.Width;
            }

            control.ControlAdded += this.OnControlAdded;
            control.ControlRemoved += this.OnControlRemoved;
            foreach (Control child in control.Controls)
                this.Track(child);
        }

        private void Untrack(Control control)
        {
            control.ControlAdded -= this.OnControlAdded;
            control.ControlRemoved -= this.OnControlRemoved;
            foreach (Control child in control.Controls)
                this.Untrack(child);

            this.tracked.Remove(control);
            this.baseBounds.Remove(control);
            this.baseFonts.Remove(control);
            this.richTextBoxes.Remove(control as RichTextBox);
            this.scrollables.Remove(control as ScrollableControl);
            if (control is ListView listView)
            {
                foreach (ColumnHeader column in listView.Columns)
                    this.baseColumnWidths.Remove(column);
            }
        }

        // Controls created at runtime (e.g. CoachView's bonus-country rows) are placed in design
        // coordinates of their parent's content, so they're tracked as-is and immediately brought to
        // the current scale (and scroll position).
        private void OnControlAdded(object sender, ControlEventArgs e)
        {
            this.Track(e.Control);
            this.ApplyTo(e.Control);
        }

        private void OnControlRemoved(object sender, ControlEventArgs e) => this.Untrack(e.Control);

        private void ApplyTo(Control control)
        {
            if (this.baseFonts.TryGetValue(control, out Font font))
            {
                if (!this.scaledFonts.TryGetValue(font, out Font scaled))
                {
                    scaled = this.ScaleFont(font, this.scaledFonts);
                    this.scaledFonts[font] = scaled;
                }
                control.Font = scaled;
            }
            if (this.baseBounds.TryGetValue(control, out Rectangle bounds))
            {
                Rectangle scaled = this.ScaleRectangle(bounds);
                Point scroll = ScrollOffset(control);
                scaled.Offset(scroll.X, scroll.Y);
                control.Bounds = scaled;
            }
            if (control is RichTextBox richTextBox)
                this.ApplyZoom(richTextBox);
            foreach (Control child in control.Controls)
                this.ApplyTo(child);
        }

        // How far the control's parent is scrolled - zero or negative, the way AutoScrollPosition
        // reads back. Locations inside a scrolled panel are offset by exactly this much.
        private static Point ScrollOffset(Control control) =>
            control.Parent is ScrollableControl parent && parent.AutoScroll ? parent.AutoScrollPosition : Point.Empty;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
