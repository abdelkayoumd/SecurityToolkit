using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace SecurityToolkit.UI
{
    /// <summary>
    /// A themed, sortable grid with a free-text filter on top and a record count below. Every feature that lists
    /// users, roles, teams or business units uses it, and its visible content feeds the Excel export.
    /// </summary>
    internal sealed class FilterableGrid : UserControl
    {
        private readonly TextBox _filter;
        private readonly DataGridView _grid;
        private readonly Label _count;
        private IReadOnlyList<object> _all = new object[0];
        private Type _itemType = typeof(object);
        private PropertyInfo[] _filterProperties = new PropertyInfo[0];

        public FilterableGrid()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.PageBackground;

            _filter = new TextBox { Dock = DockStyle.Top, Font = Theme.FontRegular, BorderStyle = BorderStyle.FixedSingle };
            _filter.TextChanged += (s, e) => ApplyFilter();
            var filterHost = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(8, 6, 8, 4), BackColor = Theme.PageBackground };
            filterHost.Controls.Add(_filter);

            _count = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0),
                Font = Theme.FontSmall,
                ForeColor = Theme.TextSecondary
            };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                MultiSelect = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
                BackgroundColor = Theme.PanelBackground,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Theme.Border,
                EnableHeadersVisualStyles = false,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
                ColumnHeadersHeight = 32,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                Font = Theme.FontRegular
            };
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.GroupBackground;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.TextPrimary;
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.GroupBackground;
            _grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.TextPrimary;
            _grid.ColumnHeadersDefaultCellStyle.Font = Theme.FontBold;
            _grid.DefaultCellStyle.SelectionBackColor = Theme.AccentSelected;
            _grid.DefaultCellStyle.SelectionForeColor = Theme.TextPrimary;
            _grid.DefaultCellStyle.ForeColor = Theme.TextPrimary;
            _grid.RowTemplate.Height = 28;
            _grid.SelectionChanged += (s, e) => SelectionChanged?.Invoke(this, EventArgs.Empty);

            Controls.Add(_grid);
            Controls.Add(_count);
            Controls.Add(filterHost);
        }

        public event EventHandler SelectionChanged;

        public int SelectedCount => _grid.SelectedRows.Count;

        /// <summary>Binds the grid. <paramref name="columns"/> are "Property=Header" pairs; when given, other properties are hidden.</summary>
        public void SetData<T>(IEnumerable<T> items, params string[] columns)
        {
            _all = items.Cast<object>().ToList();
            _itemType = typeof(T);
            _filterProperties = typeof(T).GetProperties().Where(p => p.PropertyType == typeof(string)).ToArray();
            _columnMap = columns.Select(c => c.Split(new[] { '=' }, 2)).ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : p[0]);
            ApplyFilter();
        }

        private Dictionary<string, string> _columnMap = new Dictionary<string, string>();

        public IReadOnlyList<T> GetSelected<T>()
        {
            return _grid.SelectedRows.Cast<DataGridViewRow>().OrderBy(r => r.Index).Select(r => r.DataBoundItem).OfType<T>().ToList();
        }

        public IReadOnlyList<T> GetVisible<T>()
        {
            return _grid.Rows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem).OfType<T>().ToList();
        }

        /// <summary>The table as displayed: visible columns in display order, rows in current sort order.</summary>
        public KeyValuePair<List<string>, List<IReadOnlyList<string>>> GetTable()
        {
            var columns = _grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).ToList();
            var headers = columns.Select(c => c.HeaderText).ToList();
            var rows = _grid.Rows.Cast<DataGridViewRow>()
                .Select(r => (IReadOnlyList<string>)columns.Select(c => r.Cells[c.Index].Value?.ToString() ?? string.Empty).ToList())
                .ToList();
            return new KeyValuePair<List<string>, List<IReadOnlyList<string>>>(headers, rows);
        }

        public void SelectAll()
        {
            _grid.SelectAll();
        }

        public void ClearSelection()
        {
            _grid.ClearSelection();
        }

        private void ApplyFilter()
        {
            var text = _filter.Text.Trim();
            var matches = string.IsNullOrEmpty(text)
                ? _all
                : _all.Where(item => _filterProperties.Any(p => ((string)p.GetValue(item) ?? string.Empty).IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0)).ToList();

            var listType = typeof(SortableBindingList<>).MakeGenericType(_itemType);
            var typed = typeof(Enumerable).GetMethod(nameof(Enumerable.Cast)).MakeGenericMethod(_itemType).Invoke(null, new object[] { matches });
            _grid.DataSource = Activator.CreateInstance(listType, typed);

            foreach (DataGridViewColumn column in _grid.Columns)
            {
                if (_columnMap.Count == 0)
                {
                    continue;
                }

                if (_columnMap.TryGetValue(column.DataPropertyName, out var header))
                {
                    column.HeaderText = header;
                    column.Visible = true;
                }
                else
                {
                    column.Visible = false;
                }
            }

            _count.Text = string.IsNullOrEmpty(text) ? $"{_all.Count} records" : $"{matches.Count()} of {_all.Count} records";
        }
    }
}
