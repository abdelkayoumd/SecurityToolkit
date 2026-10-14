using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Label = System.Windows.Forms.Label;

namespace SecurityToolkit.UI
{
    /// <summary>
    /// The popup behind a column's funnel icon: a search box and a checklist of the column's distinct values.
    /// Apply keeps only the checked values; Clear filter shows everything again.
    /// </summary>
    internal sealed class ColumnFilterPanel : UserControl
    {
        private sealed class Entry
        {
            public Entry(string value) { Value = value; }
            public string Value { get; }
            public override string ToString() => Value.Length == 0 ? "(Blanks)" : Value;
        }

        private readonly List<Entry> _entries;
        private readonly HashSet<string> _checked;
        private readonly TextBox _search;
        private readonly CheckedListBox _list;
        private bool _populating;

        /// <param name="values">Every distinct value of the column.</param>
        /// <param name="selected">The values currently allowed, or null when the column is not filtered (everything is checked).</param>
        public ColumnFilterPanel(IEnumerable<string> values, ISet<string> selected)
        {
            _entries = values.Select(v => new Entry(v)).ToList();
            _checked = selected == null ? new HashSet<string>(_entries.Select(e => e.Value)) : new HashSet<string>(selected);

            Size = new Size(270, 340);
            BackColor = Theme.PanelBackground;
            Font = Theme.FontRegular;

            _search = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
            _search.TextChanged += (s, e) => Populate();
            var searchHost = new Panel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(8, 8, 8, 4) };
            searchHost.Controls.Add(_search);

            var links = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 26, Padding = new Padding(8, 2, 0, 0) };
            links.Controls.Add(CreateLink("Select all", () => CheckAllShown(true)));
            links.Controls.Add(CreateLink("Clear all", () => CheckAllShown(false)));

            _list = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                BorderStyle = BorderStyle.None,
                IntegralHeight = false,
                HorizontalScrollbar = true
            };
            _list.ItemCheck += OnItemCheck;

            var apply = new Button
            {
                Text = "Apply",
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Accent,
                ForeColor = Color.White,
                Size = new Size(80, 28)
            };
            apply.FlatAppearance.BorderSize = 0;
            apply.Click += (s, e) => Applied?.Invoke(this, CheckedValues);

            var clear = new Button
            {
                Text = "Clear filter",
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.PanelBackground,
                ForeColor = Theme.TextPrimary,
                Size = new Size(90, 28)
            };
            clear.FlatAppearance.BorderColor = Theme.BorderStrong;
            clear.Click += (s, e) => Cleared?.Invoke(this, EventArgs.Empty);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 42,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8, 7, 8, 0),
                BackColor = Theme.GroupBackground
            };
            buttons.Controls.Add(apply);
            buttons.Controls.Add(clear);

            // The last control added is laid out first.
            Controls.Add(_list);
            Controls.Add(buttons);
            Controls.Add(links);
            Controls.Add(searchHost);

            Populate();
        }

        /// <summary>Apply was clicked: the values to keep.</summary>
        public event EventHandler<IReadOnlyCollection<string>> Applied;

        /// <summary>Clear filter was clicked.</summary>
        public event EventHandler Cleared;

        public IReadOnlyCollection<string> CheckedValues => _checked.ToList();

        /// <summary>The values the checklist currently lists (after the search box narrows them).</summary>
        public IReadOnlyList<string> ShownValues => _list.Items.Cast<Entry>().Select(e => e.Value).ToList();

        public string SearchText
        {
            get => _search.Text;
            set => _search.Text = value;
        }

        /// <summary>Checks or unchecks every value currently listed, leaving values hidden by the search alone.</summary>
        public void CheckAllShown(bool check)
        {
            foreach (var value in ShownValues)
            {
                if (check)
                {
                    _checked.Add(value);
                }
                else
                {
                    _checked.Remove(value);
                }
            }

            Populate();
        }

        public void FocusSearch()
        {
            _search.Focus();
        }

        private void Populate()
        {
            var text = _search.Text.Trim();
            _populating = true;
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var entry in _entries.Where(e => text.Length == 0 || e.ToString().IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0))
            {
                _list.Items.Add(entry, _checked.Contains(entry.Value));
            }

            _list.EndUpdate();
            _populating = false;
        }

        private void OnItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_populating)
            {
                return;
            }

            var value = ((Entry)_list.Items[e.Index]).Value;
            if (e.NewValue == CheckState.Checked)
            {
                _checked.Add(value);
            }
            else
            {
                _checked.Remove(value);
            }
        }

        private static LinkLabel CreateLink(string text, Action click)
        {
            var link = new LinkLabel { Text = text, AutoSize = true, LinkColor = Theme.Accent, Margin = new Padding(0, 0, 14, 0) };
            link.LinkClicked += (s, e) => click();
            return link;
        }
    }
}
