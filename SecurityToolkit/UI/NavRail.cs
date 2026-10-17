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
        private const int SearchHeight = 38;
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
        private readonly HashSet<FeatureGroup> _collapsedGroups = new HashSet<FeatureGroup>();
        private readonly TextBox _search;
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
            public string Tip;
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

            // "Find a screen": with this many screens, typing a word is quicker than scanning the list.
            _search = new TextBox
            {
                Name = "FindScreen",
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.FontRegular,
                Location = new Point(8, ToggleHeight + 6),
                Width = ExpandedWidth - 16,
                Cursor = Cursors.IBeam
            };
            _search.HandleCreated += (s, e) => SendMessage(_search.Handle, EmSetCueBanner, (IntPtr)1, "Find a screen");
            _search.TextChanged += (s, e) =>
            {
                _scroll = 0;
                ClampScroll();
                Invalidate();
            };
            _search.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    var first = _rows.FindIndex(r => !r.IsHeading && IsShown(r));
                    if (first >= 0)
                    {
                        Select(_rows[first].Id);
                    }

                    e.Handled = e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    _search.Clear();
                    e.Handled = e.SuppressKeyPress = true;
                }
            };
            Controls.Add(_search);
        }

        private const int EmSetCueBanner = 0x1501;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        /// <summary>The text typed in the "Find a screen" box; empty when nothing is being searched for.</summary>
        internal string SearchText
        {
            get => _search.Text;
            set => _search.Text = value;
        }

        /// <summary>The y of the first row: below the toggle, and below the search box when the rail is expanded.</summary>
        internal int ContentTop => ToggleHeight + (_expanded ? SearchHeight : 0);

        private bool Filtering => _expanded && _search.Text.Trim().Length > 0;

        /// <summary>Whether a group's items are folded away behind its heading. Not while searching, and not in icon-only mode.</summary>
        internal bool IsCollapsed(FeatureGroup group) => _expanded && !Filtering && _collapsedGroups.Contains(group);

        /// <summary>Folds or unfolds a group (its heading is clicked).</summary>
        internal void ToggleGroup(FeatureGroup group)
        {
            if (!_collapsedGroups.Remove(group))
            {
                _collapsedGroups.Add(group);
            }

            ClampScroll();
            Invalidate();
        }

        /// <summary>An item matches a search when its title or its group's heading contains what was typed.</summary>
        private bool Matches(Row row)
        {
            var text = _search.Text.Trim();
            return row.Title.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0
                   || (row.Group != FeatureGroup.Start && HeadingText(row.Group).IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }

        /// <summary>Whether a row takes any room: hidden by a search or by a folded group.</summary>
        private bool IsShown(Row row)
        {
            if (!_expanded)
            {
                return true;
            }

            if (row.IsHeading)
            {
                return !Filtering || _rows.Any(r => !r.IsHeading && r.Group == row.Group && Matches(r));
            }

            return Filtering ? Matches(row) : !IsCollapsed(row.Group);
        }

        public event EventHandler<string> ItemSelected;

        public string SelectedId => _selected >= 0 && _selected < _rows.Count ? _rows[_selected].Id : null;

        /// <summary>How far the list is scrolled, in pixels.</summary>
        internal int ScrollOffset => _scroll;

        /// <summary>The furthest the list can scroll; zero when everything fits.</summary>
        internal int MaxScroll => Math.Max(0, ContentHeight - Viewport);

        /// <summary>What the rail lists, top to bottom: item titles, and group headings marked with a leading "#".</summary>
        internal IReadOnlyList<string> RowLabels => _rows.Select(r => r.IsHeading ? "#" + r.Title : r.Title).ToList();

        /// <summary>The rows that take room right now (not folded away, not hidden by a search), same notation as <see cref="RowLabels"/>.</summary>
        internal IReadOnlyList<string> VisibleRowLabels => _rows.Where(IsShown).Select(r => r.IsHeading ? "#" + r.Title : r.Title).ToList();

        public bool Expanded
        {
            get => _expanded;
            set
            {
                _expanded = value;
                _search.Visible = value;
                Width = value ? ExpandedWidth : CollapsedWidth;
                ClampScroll();
                Invalidate();
            }
        }

        /// <summary>Adds an item. The first item of each new group (other than Start) gets that group's heading above it.</summary>
        public void AddItem(string id, string glyph, string title, FeatureGroup group = FeatureGroup.Start, string tip = null)
        {
            var last = _rows.LastOrDefault();
            if (group != FeatureGroup.Start && (last == null || last.Group != group))
            {
                _rows.Add(new Row { Group = group, IsHeading = true, Title = HeadingText(group) });
            }

            _rows.Add(new Row { Id = id, Glyph = glyph, Title = title, Group = group, Tip = tip });
            Invalidate();
        }

        public void Select(string id)
        {
            var index = _rows.FindIndex(r => !r.IsHeading && string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
            if (index < 0 || index == _selected)
            {
                return;
            }

            // Opening a screen from elsewhere (a Home card) must show it in the menu: unfold its group, drop a search that hides it.
            _collapsedGroups.Remove(_rows[index].Group);
            if (Filtering && !Matches(_rows[index]))
            {
                _search.Clear();
            }

            _selected = index;
            EnsureVisible(index);
            Invalidate();
            ItemSelected?.Invoke(this, _rows[index].Id);
        }

        private static string HeadingText(FeatureGroup group)
        {
            switch (group)
            {
                case FeatureGroup.LookUp: return "LOOK UP";
                case FeatureGroup.UnderstandAccess: return "UNDERSTAND ACCESS";
                case FeatureGroup.Compare: return "COMPARE";
                case FeatureGroup.Review: return "REVIEW";
                default: return "CHANGE";
            }
        }

        private int Viewport => Math.Max(0, Height - ContentTop);
        private int ContentHeight => _rows.Sum(RowHeight);

        private int RowHeight(Row row)
        {
            if (!IsShown(row))
            {
                return 0;
            }

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

            if (point.Y < ContentTop)
            {
                return -3; // the search box
            }

            var y = point.Y - ContentTop + _scroll;
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

        /// <summary>
        /// What to say when the mouse rests on an item: what the screen does. With only the icons showing, its name goes first,
        /// since the icon alone does not say which screen it is.
        /// </summary>
        private string TipFor(Row row)
        {
            if (_expanded)
            {
                return row.Tip ?? string.Empty;
            }

            return string.IsNullOrEmpty(row.Tip) ? row.Title : row.Title + ": " + row.Tip;
        }

        /// <summary>The hover tip of the item under a point; empty for headings, gaps and items without one.</summary>
        internal string TipAt(Point point)
        {
            var row = RowAt(point);
            return row >= 0 && !_rows[row].IsHeading ? TipFor(_rows[row]) : string.Empty;
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
                _toolTip.SetToolTip(this, hover >= 0 ? TipFor(_rows[hover]) : string.Empty);
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
            else if (row >= 0 && _rows[row].IsHeading)
            {
                if (_expanded && !Filtering)
                {
                    ToggleGroup(_rows[row].Group);
                }
            }
            else if (row >= 0)
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
            g.SetClip(new Rectangle(0, ContentTop, Width - 1, Viewport));
            var top = ContentTop - _scroll;
            for (var i = 0; i < _rows.Count; i++)
            {
                var height = RowHeight(_rows[i]);
                if (height > 0 && top + height > ContentTop && top < Height)
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

            // A chevron on the right says the heading can be clicked to fold the group (not while a search is showing).
            if (!Filtering)
            {
                TextRenderer.DrawText(g, IsCollapsed(row.Group) ? Theme.GlyphChevronRight : Theme.GlyphChevronDown, Theme.FontIconSmall,
                    new Rectangle(Width - 30, top + 8, 20, height - 8), HeadingColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

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
                // The Change items wear amber, so they never look like read-only screens.
                selected ? Theme.Accent : row.Group == FeatureGroup.Change ? Theme.Warning : Theme.TextSecondary,
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
                var rect = new Rectangle(0, ContentTop, Width - 1, fade);
                using (var brush = new LinearGradientBrush(rect, Theme.PanelBackground, Color.FromArgb(0, Theme.PanelBackground), LinearGradientMode.Vertical))
                {
                    g.FillRectangle(brush, rect);
                }
            }

            var thumbHeight = Math.Max(24, Viewport * Viewport / ContentHeight);
            var thumbTop = ContentTop + (int)((long)(Viewport - thumbHeight) * _scroll / MaxScroll);
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
