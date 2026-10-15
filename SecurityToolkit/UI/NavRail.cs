using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Features;

namespace SecurityToolkit.UI
{
    /// <summary>
    /// The vertical feature menu on the left: items under bold group headings (EXPLORE, CHANGE), collapsible to
    /// icons only (headings become dividers). When the window is too short for every item the list scrolls, with a
    /// slim scrollbar and a fade at the edge that still has more.
    /// </summary>
    internal sealed class NavRail : Control
    {
        public const int ExpandedWidth = 210;
        public const int CollapsedWidth = 48;
        private const int ToggleHeight = 40;
        private const int ItemHeight = 36;
        private const int HeadingHeight = 30;
        private const int DividerHeight = 17;
        private const int WheelStep = ItemHeight;

        private static readonly Font HeadingFont = new Font("Segoe UI", 8F, FontStyle.Bold);
        private static readonly Font TagFont = new Font("Segoe UI", 7.5F);
        private static readonly Color HeadingColor = Theme.TextSecondary;
        private static readonly Color ChangeDivider = Color.FromArgb(240, 195, 109);

        private readonly List<Row> _rows = new List<Row>();
        private readonly ToolTip _toolTip = new ToolTip();
        private int _selected = -1;
        private int _hover = -1;
        private bool _toggleHover;
        private bool _expanded = true;
        private int _scroll;

        private sealed class Row
        {
            public string Id;
            public string Glyph;
            public string Title;
            public FeatureGroup Group;
            public bool IsHeading;
        }

        public NavRail()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Dock = DockStyle.Left;
            Width = ExpandedWidth;
            Font = Theme.FontRegular;
            Cursor = Cursors.Hand;
        }

        public event EventHandler<string> ItemSelected;

        public string SelectedId => _selected >= 0 && _selected < _rows.Count ? _rows[_selected].Id : null;

        /// <summary>How far the list is scrolled, in pixels.</summary>
        internal int ScrollOffset => _scroll;

        /// <summary>The furthest the list can scroll; zero when everything fits.</summary>
        internal int MaxScroll => Math.Max(0, ContentHeight - Viewport);

        /// <summary>What the rail lists, top to bottom: item titles, and group headings marked with a leading "#".</summary>
        internal IReadOnlyList<string> RowLabels => _rows.Select(r => r.IsHeading ? "#" + r.Title : r.Title).ToList();

        public bool Expanded
        {
            get => _expanded;
            set
            {
                _expanded = value;
                Width = value ? ExpandedWidth : CollapsedWidth;
                ClampScroll();
                Invalidate();
            }
        }

        /// <summary>Adds an item. The first item of each new group (other than Start) gets that group's heading above it.</summary>
        public void AddItem(string id, string glyph, string title, FeatureGroup group = FeatureGroup.Start)
        {
            var last = _rows.LastOrDefault();
            if (group != FeatureGroup.Start && (last == null || last.Group != group))
            {
                _rows.Add(new Row { Group = group, IsHeading = true, Title = HeadingText(group) });
            }

            _rows.Add(new Row { Id = id, Glyph = glyph, Title = title, Group = group });
            Invalidate();
        }

        public void Select(string id)
        {
            var index = _rows.FindIndex(r => !r.IsHeading && string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
            if (index < 0 || index == _selected)
            {
                return;
            }

            _selected = index;
            EnsureVisible(index);
            Invalidate();
            ItemSelected?.Invoke(this, _rows[index].Id);
        }

        private static string HeadingText(FeatureGroup group)
        {
            return group == FeatureGroup.Change ? "CHANGE" : "EXPLORE";
        }

        private int Viewport => Math.Max(0, Height - ToggleHeight);
        private int ContentHeight => _rows.Sum(RowHeight);

        private int RowHeight(Row row)
        {
            if (!row.IsHeading)
            {
                return ItemHeight;
            }

            return _expanded ? HeadingHeight : DividerHeight;
        }

        private int RowTop(int index)
        {
            var top = 0;
            for (var i = 0; i < index; i++)
            {
                top += RowHeight(_rows[i]);
            }

            return top;
        }

        private void ClampScroll()
        {
            _scroll = Math.Max(0, Math.Min(_scroll, MaxScroll));
        }

        private void EnsureVisible(int index)
        {
            var top = RowTop(index);
            var bottom = top + RowHeight(_rows[index]);
            if (top < _scroll)
            {
                _scroll = top;
            }
            else if (bottom > _scroll + Viewport)
            {
                _scroll = bottom - Viewport;
            }

            ClampScroll();
        }

        /// <summary>The row under a point, or -1 for none; -2 for the toggle row.</summary>
        internal int RowAt(Point point)
        {
            if (point.Y < ToggleHeight)
            {
                return -2;
            }

            var y = point.Y - ToggleHeight + _scroll;
            var top = 0;
            for (var i = 0; i < _rows.Count; i++)
            {
                var height = RowHeight(_rows[i]);
                if (y >= top && y < top + height)
                {
                    return i;
                }

                top += height;
            }

            return -1;
        }

        /// <summary>The id of the item under a point (headings and gaps have none).</summary>
        internal string ItemIdAt(Point point)
        {
            var row = RowAt(point);
            return row >= 0 && !_rows[row].IsHeading ? _rows[row].Id : null;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (MaxScroll > 0)
            {
                _scroll -= Math.Sign(e.Delta) * WheelStep;
                ClampScroll();
                Invalidate();
            }

            base.OnMouseWheel(e);
        }

        protected override void OnResize(EventArgs e)
        {
            ClampScroll();
            base.OnResize(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var row = RowAt(e.Location);
            var toggle = row == -2;
            var hover = row >= 0 && !_rows[row].IsHeading ? row : -1;
            if (hover != _hover || toggle != _toggleHover)
            {
                _hover = hover;
                _toggleHover = toggle;
                // Collapsed, only the icons show; say what each one is.
                _toolTip.SetToolTip(this, !_expanded && hover >= 0 ? _rows[hover].Title : string.Empty);
                Invalidate();
            }

            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = -1;
            _toggleHover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            var row = RowAt(e.Location);
            if (row == -2)
            {
                Expanded = !Expanded;
            }
            else if (row >= 0 && !_rows[row].IsHeading)
            {
                Select(_rows[row].Id);
            }

            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.PanelBackground);

            // The scrolling area first, clipped so rows slide under the fixed toggle row.
            g.SetClip(new Rectangle(0, ToggleHeight, Width - 1, Viewport));
            var top = ToggleHeight - _scroll;
            for (var i = 0; i < _rows.Count; i++)
            {
                var height = RowHeight(_rows[i]);
                if (top + height > ToggleHeight && top < Height)
                {
                    if (_rows[i].IsHeading)
                    {
                        DrawHeading(g, _rows[i], top, height);
                    }
                    else
                    {
                        DrawItem(g, _rows[i], new Rectangle(0, top, Width - 1, height), i == _selected, i == _hover);
                    }
                }

                top += height;
            }

            DrawScrollCues(g);
            g.ResetClip();

            DrawToggle(g);
            using (var pen = new Pen(Theme.Border))
            {
                g.DrawLine(pen, Width - 1, 0, Width - 1, Height);
            }
        }

        private void DrawToggle(Graphics g)
        {
            var bounds = new Rectangle(0, 0, Width - 1, ToggleHeight);
            using (var brush = new SolidBrush(_toggleHover ? Theme.Hover : Theme.PanelBackground))
            using (var pen = new Pen(Theme.Border))
            {
                g.FillRectangle(brush, bounds);
                g.DrawLine(pen, 0, ToggleHeight - 1, Width - 1, ToggleHeight - 1);
            }

            TextRenderer.DrawText(g, Glyphs.Menu, Theme.FontIcon, new Rectangle(0, 0, CollapsedWidth, ToggleHeight - 1), Theme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private void DrawHeading(Graphics g, Row row, int top, int height)
        {
            if (!_expanded)
            {
                // A thin divider stands in for the heading; the Change group's is amber to match its tag.
                using (var pen = new Pen(row.Group == FeatureGroup.Change ? ChangeDivider : Theme.Border))
                {
                    g.DrawLine(pen, 10, top + height / 2, Width - 11, top + height / 2);
                }

                return;
            }

            var text = row.Title;
            var textBounds = new Rectangle(14, top + 8, Width - 28, height - 8);
            TextRenderer.DrawText(g, text, HeadingFont, textBounds, HeadingColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            if (row.Group == FeatureGroup.Change)
            {
                DrawTag(g, "writes to your org", 14 + TextRenderer.MeasureText(text, HeadingFont, Size.Empty, TextFormatFlags.NoPadding).Width + 8, top + 8 + (height - 8) / 2);
            }
        }

        private static void DrawTag(Graphics g, string text, int x, int centerY)
        {
            var size = TextRenderer.MeasureText(text, TagFont, Size.Empty, TextFormatFlags.NoPadding);
            var rect = new Rectangle(x, centerY - (size.Height + 2) / 2, size.Width + 10, size.Height + 2);
            using (var path = RoundedRectangle(rect, 3))
            using (var brush = new SolidBrush(Theme.WarningBackground))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillPath(brush, path);
                g.SmoothingMode = SmoothingMode.Default;
            }

            TextRenderer.DrawText(g, text, TagFont, rect, Theme.Warning,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private static GraphicsPath RoundedRectangle(Rectangle r, int radius)
        {
            var d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void DrawItem(Graphics g, Row row, Rectangle bounds, bool selected, bool hover)
        {
            if (selected || hover)
            {
                using (var brush = new SolidBrush(selected ? Theme.AccentSelected : Theme.Hover))
                {
                    g.FillRectangle(brush, bounds);
                }
            }

            if (selected)
            {
                using (var brush = new SolidBrush(Theme.Accent))
                {
                    g.FillRectangle(brush, bounds.X, bounds.Y + 7, 3, bounds.Height - 14);
                }
            }

            TextRenderer.DrawText(g, row.Glyph, Theme.FontIcon, new Rectangle(0, bounds.Y, CollapsedWidth, bounds.Height),
                selected ? Theme.Accent : Theme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            if (_expanded)
            {
                TextRenderer.DrawText(g, row.Title, selected ? Theme.FontBold : Theme.FontRegular,
                    new Rectangle(CollapsedWidth, bounds.Y, Width - CollapsedWidth - 8, bounds.Height),
                    selected ? Theme.TextPrimary : Theme.TextSecondary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
        }

        /// <summary>A slim scrollbar, and a fade along each edge that has more beyond it.</summary>
        private void DrawScrollCues(Graphics g)
        {
            if (MaxScroll <= 0)
            {
                return;
            }

            const int fade = 22;
            if (_scroll < MaxScroll)
            {
                var rect = new Rectangle(0, Height - fade, Width - 1, fade);
                using (var brush = new LinearGradientBrush(rect, Color.FromArgb(0, Theme.PanelBackground), Theme.PanelBackground, LinearGradientMode.Vertical))
                {
                    g.FillRectangle(brush, rect);
                }
            }

            if (_scroll > 0)
            {
                var rect = new Rectangle(0, ToggleHeight, Width - 1, fade);
                using (var brush = new LinearGradientBrush(rect, Theme.PanelBackground, Color.FromArgb(0, Theme.PanelBackground), LinearGradientMode.Vertical))
                {
                    g.FillRectangle(brush, rect);
                }
            }

            var thumbHeight = Math.Max(24, Viewport * Viewport / ContentHeight);
            var thumbTop = ToggleHeight + (int)((long)(Viewport - thumbHeight) * _scroll / MaxScroll);
            using (var brush = new SolidBrush(Color.FromArgb(200, 198, 196)))
            {
                g.FillRectangle(brush, Width - 7, thumbTop, 4, thumbHeight);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _toolTip.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
