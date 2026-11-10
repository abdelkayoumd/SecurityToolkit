using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SecurityToolkit.UI
{
    /// <summary>
    /// A row of text tabs: the selected one is bold with an accent underline. It only says which tab is chosen; the
    /// screen decides what to show.
    /// </summary>
    internal sealed class TabStrip : Control
    {
        private const int TabPadding = 14;
        private readonly List<string> _tabs = new List<string>();
        private int _selected;
        private int _hover = -1;

        public TabStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Dock = DockStyle.Top;
            Height = 32;
            Cursor = Cursors.Hand;
            BackColor = Theme.PageBackground;
        }

        public event EventHandler SelectedIndexChanged;

        public int SelectedIndex
        {
            get => _selected;
            set
            {
                if (value < 0 || value >= _tabs.Count || value == _selected)
                {
                    return;
                }

                _selected = value;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public IReadOnlyList<string> Tabs => _tabs;

        public void AddTab(string text)
        {
            _tabs.Add(text);
            Invalidate();
        }

        public void SetText(int index, string text)
        {
            _tabs[index] = text;
            Invalidate();
        }

        /// <summary>The tab under a point, or -1.</summary>
        internal int TabAt(Point point)
        {
            var left = 0;
            for (var i = 0; i < _tabs.Count; i++)
            {
                var width = WidthOf(i);
                if (point.X >= left && point.X < left + width)
                {
                    return i;
                }

                left += width;
            }

            return -1;
        }

        private int WidthOf(int index)
        {
            var font = index == _selected ? Theme.FontBold : Theme.FontRegular;
            return TextRenderer.MeasureText(_tabs[index], font, Size.Empty, TextFormatFlags.NoPadding).Width + 2 * TabPadding;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var hover = TabAt(e.Location);
            if (hover != _hover)
            {
                _hover = hover;
                Invalidate();
            }

            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            var tab = TabAt(e.Location);
            if (e.Button == MouseButtons.Left && tab >= 0)
            {
                SelectedIndex = tab;
            }

            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.PageBackground);
            using (var border = new Pen(Theme.Border))
            {
                g.DrawLine(border, 0, Height - 1, Width, Height - 1);
            }

            var left = 0;
            for (var i = 0; i < _tabs.Count; i++)
            {
                var width = WidthOf(i);
                var selected = i == _selected;
                TextRenderer.DrawText(g, _tabs[i], selected ? Theme.FontBold : Theme.FontRegular, new Rectangle(left, 0, width, Height - 2),
                    selected ? Theme.TextPrimary : Theme.TextSecondary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                if (selected || i == _hover)
                {
                    using (var brush = new SolidBrush(selected ? Theme.Accent : Theme.Border))
                    {
                        g.FillRectangle(brush, left + 4, Height - 3, width - 8, 3);
                    }
                }

                left += width;
            }
        }
    }
}
