using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SecurityToolkit.UI
{
    /// <summary>
    /// A dropdown that looks like a combo box but lets several of its items be ticked: one, a few, or all of them.
    /// The choice is applied when the list closes, so ticking three items runs one search, not three.
    /// At least one item always stays ticked.
    /// </summary>
    internal sealed class CheckedDropDown : Control
    {
        private readonly List<string> _items;
        private readonly string _noun;
        private bool[] _checked;
        private ToolStripDropDown _popup;
        private CheckedListBox _list;
        private DateTime _closedAt = DateTime.MinValue;
        private bool _hot;

        /// <param name="items">The choices, in display order.</param>
        /// <param name="noun">What the items are, in the plural ("privileges"): used for "All privileges" and "4 privileges".</param>
        public CheckedDropDown(IEnumerable<string> items, string noun)
        {
            _items = items.ToList();
            _noun = noun;
            _checked = new bool[_items.Count];
            if (_items.Count > 0)
            {
                _checked[0] = true;
            }

            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Font = Theme.FontRegular;
            Size = new Size(150, 23);
            TabStop = true;
        }

        /// <summary>The ticked items changed (raised once, when the list closes or <see cref="SetChecked"/> is called).</summary>
        public event EventHandler CheckedChanged;

        public IReadOnlyList<string> Items => _items;

        /// <summary>The positions of the ticked items, in display order.</summary>
        public IReadOnlyList<int> CheckedIndices => Enumerable.Range(0, _items.Count).Where(i => _checked[i]).ToList();

        public bool AllChecked => _checked.All(c => c);

        /// <summary>What the closed dropdown shows: the item, two items, "All privileges" or "4 privileges".</summary>
        public override string Text
        {
            get
            {
                var picked = CheckedIndices;
                if (picked.Count == _items.Count && _items.Count > 1)
                {
                    return "All " + _noun;
                }

                return picked.Count <= 2 ? string.Join(", ", picked.Select(i => _items[i])) : picked.Count + " " + _noun;
            }
            set { }
        }

        /// <summary>Ticks exactly these items. An empty choice is ignored: one item always stays ticked.</summary>
        public void SetChecked(IEnumerable<int> indices)
        {
            var wanted = new bool[_items.Count];
            foreach (var index in indices.Where(i => i >= 0 && i < _items.Count))
            {
                wanted[index] = true;
            }

            if (!wanted.Any(c => c) || wanted.SequenceEqual(_checked))
            {
                return;
            }

            _checked = wanted;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }

        public void CheckAll()
        {
            SetChecked(Enumerable.Range(0, _items.Count));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var back = new SolidBrush(_hot ? Theme.GroupBackground : Theme.PanelBackground))
            {
                g.FillRectangle(back, bounds);
            }

            using (var pen = new Pen(Focused ? Theme.Accent : Theme.BorderStrong))
            {
                g.DrawRectangle(pen, bounds);
            }

            const int arrowWidth = 20;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(4, 0, Math.Max(0, Width - arrowWidth - 4), Height), Theme.TextPrimary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, Theme.GlyphChevronDown, Theme.FontIconSmall, new Rectangle(Width - arrowWidth, 0, arrowWidth, Height), Theme.TextSecondary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hot = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hot = false;
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            // A click on the dropdown while its list is open closes the list (auto-close); it must not reopen it.
            if (e.Button == MouseButtons.Left && (DateTime.UtcNow - _closedAt).TotalMilliseconds > 250)
            {
                Open();
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Down || keyData == Keys.Up || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter || e.KeyCode == Keys.Down || e.KeyCode == Keys.F4)
            {
                Open();
                e.Handled = true;
            }
        }

        /// <summary>Shows the checklist under the control.</summary>
        public void Open()
        {
            _popup?.Dispose();

            _list = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                BorderStyle = BorderStyle.None,
                IntegralHeight = false,
                Font = Theme.FontRegular
            };
            for (var i = 0; i < _items.Count; i++)
            {
                _list.Items.Add(_items[i], _checked[i]);
            }

            var links = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 26, Padding = new Padding(6, 4, 0, 0), BackColor = Theme.PanelBackground };
            links.Controls.Add(Link("Select all", () => TickAll(true)));
            links.Controls.Add(Link("Clear all", () => TickAll(false)));

            var panel = new Panel
            {
                Size = new Size(Math.Max(Width, 170), 26 + _items.Count * (_list.ItemHeight + 1) + 8),
                BackColor = Theme.PanelBackground,
                Padding = new Padding(6, 0, 2, 4)
            };
            panel.Controls.Add(_list);
            panel.Controls.Add(links);

            var popup = new ToolStripDropDown { AutoClose = true, Padding = Padding.Empty, DropShadowEnabled = true };
            popup.Items.Add(new ToolStripControlHost(panel) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = panel.Size });
            _list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape)
                {
                    popup.Close();
                    e.Handled = true;
                }
            };
            popup.Closed += (s, e) =>
            {
                _closedAt = DateTime.UtcNow;
                Commit();
            };
            _popup = popup;
            popup.Show(this, new Point(0, Height));
            _list.Focus();
        }

        private void TickAll(bool tick)
        {
            for (var i = 0; i < _list.Items.Count; i++)
            {
                _list.SetItemChecked(i, tick);
            }
        }

        /// <summary>Applies what the list has ticked; with nothing ticked the previous choice stays.</summary>
        private void Commit()
        {
            if (_list == null)
            {
                return;
            }

            var picked = _list.CheckedIndices.Cast<int>().ToList();
            _list = null;
            SetChecked(picked);
        }

        private static LinkLabel Link(string text, Action click)
        {
            var link = new LinkLabel { Text = text, AutoSize = true, LinkColor = Theme.Accent, Margin = new Padding(0, 0, 14, 0) };
            link.LinkClicked += (s, e) => click();
            return link;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _popup?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
