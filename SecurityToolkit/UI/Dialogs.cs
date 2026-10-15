using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Services;
using Label = System.Windows.Forms.Label;

namespace SecurityToolkit.UI
{
    /// <summary>A themed modal shell: a title strip, content, and a right-aligned button row.</summary>
    internal abstract class ToolkitDialog : Form
    {
        protected ToolkitDialog(string title, Size size)
        {
            Text = title;
            Size = size;
            MinimumSize = new Size(480, 320);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = true;
            BackColor = Theme.PageBackground;
            Font = Theme.FontRegular;
        }

        protected static Panel CreateButtonRow(out Button primary, string primaryText, out Button secondary, string secondaryText)
        {
            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8, 8, 8, 0),
                BackColor = Theme.PanelBackground
            };
            primary = new Button
            {
                Text = primaryText,
                AutoSize = true,
                MinimumSize = new Size(96, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Accent,
                ForeColor = Color.White,
                DialogResult = DialogResult.OK
            };
            primary.FlatAppearance.BorderSize = 0;
            secondary = new Button
            {
                Text = secondaryText,
                AutoSize = true,
                MinimumSize = new Size(96, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.PanelBackground,
                ForeColor = Theme.TextPrimary,
                DialogResult = DialogResult.Cancel
            };
            secondary.FlatAppearance.BorderColor = Theme.BorderStrong;
            row.Controls.Add(primary);
            row.Controls.Add(secondary);
            return row;
        }

        protected static Label CreateHeading(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Top,
                Height = 40,
                Padding = new Padding(12, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = Theme.FontBold,
                ForeColor = Theme.TextPrimary,
                BackColor = Theme.PanelBackground
            };
        }
    }

    /// <summary>Lets the user pick one or many records (users, roles, teams, business units) from a filterable list.</summary>
    internal sealed class PickerDialog<T> : ToolkitDialog
    {
        private readonly FilterableGrid _grid = new FilterableGrid();
        private readonly Button _ok;

        private PickerDialog(string title, IEnumerable<T> items, bool multiple, string[] columns)
            : base(title, new Size(720, 520))
        {
            var buttons = CreateButtonRow(out _ok, "Select", out var cancel, "Cancel");
            _grid.SetData(items, columns);
            _grid.SelectionChanged += (s, e) => UpdateOk(multiple);
            _grid.DoubleClick += (s, e) => { if (_ok.Enabled) { DialogResult = DialogResult.OK; } };
            Controls.Add(_grid);
            Controls.Add(buttons);
            Controls.Add(CreateHeading(title));
            AcceptButton = _ok;
            CancelButton = cancel;
            UpdateOk(multiple);
        }

        private void UpdateOk(bool multiple)
        {
            _ok.Enabled = multiple ? _grid.SelectedCount > 0 : _grid.SelectedCount == 1;
        }

        /// <summary>Returns the picked records, or null when cancelled.</summary>
        public static IReadOnlyList<T> Pick(IWin32Window owner, string title, IEnumerable<T> items, bool multiple, params string[] columns)
        {
            using (var dialog = new PickerDialog<T>(title, items, multiple, columns))
            {
                dialog._grid.ClearSelection();
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog._grid.GetSelected<T>() : null;
            }
        }
    }
}
