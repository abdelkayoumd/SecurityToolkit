using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SecurityToolkit.UI
{
    /// <summary>One card of <see cref="QuestionTabs"/>: an icon, the question the tab answers, and a short hint.</summary>
    internal sealed class QuestionTab
    {
        public QuestionTab(string glyph, string question, string hint)
        {
            Glyph = glyph;
            Question = question;
            Hint = hint;
        }

        public string Glyph { get; }
        public string Question { get; }
        public string Hint { get; }
    }

    /// <summary>
    /// Tabs drawn as a row of equal cards, each saying the question it answers ("Can they open this record?") with a
    /// one-line hint. The chosen card has an accent border and accent text, so it is clear which question is on screen.
    /// </summary>
    internal sealed class QuestionTabs : Control
    {
        private const int Gap = 8;
        private const int CardHeight = 50;
        private const int Margin = 10;
        private readonly List<QuestionTab> _tabs = new List<QuestionTab>();
        private int _selected;
        private int _hover = -1;

        public QuestionTabs()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Dock = DockStyle.Top;
            Height = CardHeight + 2 * Margin - 2;
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

        public IReadOnlyList<QuestionTab> Tabs => _tabs;

        public void AddTab(QuestionTab tab)
        {
            _tabs.Add(tab);
            Invalidate();
        }

        /// <summary>Where card <paramref name="index"/> is drawn.</summary>
        internal Rectangle CardBounds(int index)
        {
            var count = Math.Max(1, _tabs.Count);
            var width = (ClientSize.Width - 2 * Margin - (count - 1) * Gap) / count;
            return new Rectangle(Margin + index * (width + Gap), Margin, Math.Max(0, width), CardHeight);
        }

        /// <summary>The card under a point, or -1.</summary>
        internal int TabAt(Point point)
        {
            for (var i = 0; i < _tabs.Count; i++)
            {
                if (CardBounds(i).Contains(point))
                {
                    return i;
                }
            }

            return -1;
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
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (var i = 0; i < _tabs.Count; i++)
            {
                PaintCard(g, i);
            }
        }

        private void PaintCard(Graphics g, int index)
        {
            var tab = _tabs[index];
            var bounds = CardBounds(index);
            if (bounds.Width < 20)
            {
                return;
            }

            var selected = index == _selected;
            var hover = index == _hover && !selected;
            using (var path = Rounded(bounds, 6))
            {
                using (var fill = new SolidBrush(selected ? Theme.AccentSoft : hover ? Theme.Hover : Theme.PanelBackground))
                {
                    g.FillPath(fill, path);
                }

                using (var pen = new Pen(selected ? Theme.Accent : hover ? Theme.BorderStrong : Theme.Border, selected ? 2f : 1f))
                {
                    g.DrawPath(pen, path);
                }
            }

            var accent = selected ? Theme.Accent : Theme.TextPrimary;
            var icon = new Rectangle(bounds.Left + 10, bounds.Top + 8, 20, 20);
            TextRenderer.DrawText(g, tab.Glyph, Theme.FontIcon, icon, selected ? Theme.Accent : Theme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            var textLeft = icon.Right + 8;
            var textWidth = bounds.Right - textLeft - 8;
            const TextFormatFlags line = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(g, tab.Question, Theme.FontBold, new Rectangle(textLeft, bounds.Top + 6, textWidth, 20), accent, line);
            TextRenderer.DrawText(g, tab.Hint, Theme.FontSmall, new Rectangle(textLeft, bounds.Top + 26, textWidth, 18), Theme.TextSecondary, line);
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
