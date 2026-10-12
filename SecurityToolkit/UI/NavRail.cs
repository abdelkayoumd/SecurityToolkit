using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SecurityToolkit.UI
{
    /// <summary>The vertical feature menu on the left: a glyph and a title per item, collapsible to glyphs only.</summary>
    internal sealed class NavRail : Control
    {
        public const int ExpandedWidth = 210;
        public const int CollapsedWidth = 48;
        private const int ItemHeight = 44;
        private const int ToggleIndex = -1;

        private readonly List<Item> _items = new List<Item>();
        private int _selected = -1;
        private int _hover = -2;
        private bool _expanded = true;

        private sealed class Item
        {
            public string Id;
            public string Glyph;
            public string Title;
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

        public string SelectedId => _selected >= 0 && _selected < _items.Count ? _items[_selected].Id : null;

        public bool Expanded
        {
            get => _expanded;
            set
            {
                _expanded = value;
                Width = value ? ExpandedWidth : CollapsedWidth;
                Invalidate();
            }
        }

        public void AddItem(string id, string glyph, string title)
        {
            _items.Add(new Item { Id = id, Glyph = glyph, Title = title });
            Invalidate();
        }

        public void Select(string id)
        {
            var index = _items.FindIndex(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
            if (index < 0 || index == _selected)
            {
                return;
            }

            _selected = index;
            Invalidate();
            ItemSelected?.Invoke(this, _items[index].Id);
        }

        private int IndexAt(Point p)
        {
            var row = p.Y / ItemHeight;
            return row == 0 ? ToggleIndex : row - 1 < _items.Count ? row - 1 : -2;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var index = IndexAt(e.Location);
            if (index != _hover)
            {
                _hover = index;
                Invalidate();
            }

            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = -2;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            var index = IndexAt(e.Location);
            if (index == ToggleIndex)
            {
                Expanded = !Expanded;
            }
            else if (index >= 0)
            {
                Select(_items[index].Id);
            }

            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.PanelBackground);
            DrawRow(g, 0, Glyphs.Menu, null, false, _hover == ToggleIndex);
            for (var i = 0; i < _items.Count; i++)
            {
                DrawRow(g, i + 1, _items[i].Glyph, _items[i].Title, i == _selected, i == _hover);
            }

            using (var pen = new Pen(Theme.Border))
            {
                g.DrawLine(pen, Width - 1, 0, Width - 1, Height);
            }
        }

        private void DrawRow(Graphics g, int row, string glyph, string title, bool selected, bool hover)
        {
            var bounds = new Rectangle(0, row * ItemHeight, Width - 1, ItemHeight);
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
                    g.FillRectangle(brush, bounds.X, bounds.Y + 8, 3, ItemHeight - 16);
                }
            }

            TextRenderer.DrawText(g, glyph, Theme.FontIcon, new Rectangle(0, bounds.Y, CollapsedWidth, ItemHeight),
                selected ? Theme.Accent : Theme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            if (title != null && _expanded)
            {
                TextRenderer.DrawText(g, title, selected ? Theme.FontBold : Theme.FontRegular,
                    new Rectangle(CollapsedWidth, bounds.Y, Width - CollapsedWidth - 8, ItemHeight),
                    selected ? Theme.TextPrimary : Theme.TextSecondary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
        }
    }
}
