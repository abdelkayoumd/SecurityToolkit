using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SecurityToolkit.UI
{
    /// <summary>
    /// A themed grid used by every list. Click a column title to sort (again to reverse); click its funnel to keep
    /// only chosen values of that column. A search box filters across all columns, and all filters combine.
    /// What the grid shows after filtering and sorting is what gets exported.
    /// </summary>
    internal sealed class FilterableGrid : UserControl
    {
        private const int EmSetCueBanner = 0x1501;
        private const int HeaderHeight = 34;
        private const int FunnelWidth = 26;
        private const int SortArrowWidth = 18;

        private readonly TextBox _filter;
        private readonly DataGridView _grid;
        private readonly Label _count;
        private readonly LinkLabel _clearFilters;
        private readonly Dictionary<string, HashSet<string>> _valueFilters = new Dictionary<string, HashSet<string>>();
        private IReadOnlyList<object> _all = new object[0];
        private Type _itemType = typeof(object);
        private Dictionary<string, PropertyInfo> _properties = new Dictionary<string, PropertyInfo>();
        private PropertyInfo[] _stringProperties = new PropertyInfo[0];
        private Dictionary<string, string> _columnMap = new Dictionary<string, string>();
        private List<string> _columnOrder = new List<string>();
        private string _signature = string.Empty;
        private ToolStripDropDown _popup;
        private readonly Label _empty;

        public FilterableGrid()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.PageBackground;

            _filter = new TextBox { Dock = DockStyle.Top, Font = Theme.FontRegular, BorderStyle = BorderStyle.FixedSingle };
            _filter.TextChanged += (s, e) => ApplyFilter();
            _filter.HandleCreated += (s, e) => SetCue(_filter, "Search all columns");
            var filterHost = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(8, 6, 8, 4), BackColor = Theme.PageBackground };
            filterHost.Controls.Add(_filter);

            _count = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0),
                Font = Theme.FontSmall,
                ForeColor = Theme.TextSecondary
            };
            _clearFilters = new LinkLabel
            {
                Text = "Clear filters",
                Dock = DockStyle.Right,
                AutoSize = false,
                Width = 90,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = Theme.FontSmall,
                LinkColor = Theme.Accent,
                Visible = false
            };
            _clearFilters.LinkClicked += (s, e) => ClearFilters();
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 24 };
            footer.Controls.Add(_count);
            footer.Controls.Add(_clearFilters);

            _grid = new QuietGrid
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
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = HeaderHeight,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                Font = Theme.FontRegular
            };
            _grid.DefaultCellStyle.SelectionBackColor = Theme.AccentSelected;
            _grid.DefaultCellStyle.SelectionForeColor = Theme.TextPrimary;
            _grid.DefaultCellStyle.ForeColor = Theme.TextPrimary;
            _grid.RowTemplate.Height = 28;
            _grid.SelectionChanged += (s, e) => SelectionChanged?.Invoke(this, EventArgs.Empty);
            _grid.CellPainting += PaintHeaderCell;
            _grid.CellPainting += PaintBodyCell;
            _grid.CellToolTipTextNeeded += (s, e) =>
            {
                // A custom-drawn cell may clip its text at the cell edge, so the hover tip carries the whole of it.
                if (BodyCellRenderer != null && e.RowIndex >= 0 && e.ColumnIndex >= 0 && string.IsNullOrEmpty(e.ToolTipText))
                {
                    e.ToolTipText = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value as string ?? string.Empty;
                }
            };
            _grid.ColumnHeaderMouseClick += OnHeaderClick;

            // What to say in the empty grid, so a blank area never looks like a failure.
            _empty = new Label
            {
                Visible = false,
                AutoSize = false,
                Height = 56,
                Location = new Point(16, HeaderHeight + 16),
                ForeColor = Theme.TextSecondary,
                BackColor = Theme.PanelBackground,
                Font = Theme.FontRegular
            };
            _grid.Controls.Add(_empty);
            _grid.Resize += (s, e) => _empty.Width = Math.Max(0, _grid.Width - 32);

            // The last control added is laid out first: the search box on top, the footer at the bottom.
            Controls.Add(_grid);
            Controls.Add(footer);
            Controls.Add(filterHost);
        }

        /// <summary>Said in the middle of the grid while it has no rows. Overridden by <see cref="EmptyMessageProvider"/> when that is set.</summary>
        public string EmptyMessage { get; set; }

        /// <summary>Asked each time the grid is empty, so the message can depend on the state of the screen (connected? anything selected?).</summary>
        public Func<string> EmptyMessageProvider { get; set; }

        /// <summary>Shows or hides the empty-grid message for the rows the grid has now.</summary>
        public void RefreshEmptyMessage()
        {
            UpdateEmptyMessage(_grid.Rows.Count, _filter.Text.Trim().Length > 0 || _valueFilters.Count > 0);
        }

        private void UpdateEmptyMessage(int rows, bool filtering)
        {
            var text = rows > 0 ? null : filtering ? "No records match the search or the filters." : (EmptyMessageProvider?.Invoke() ?? EmptyMessage);
            _empty.Text = text ?? string.Empty;
            _empty.Width = Math.Max(0, _grid.Width - 32);
            _empty.Visible = !string.IsNullOrEmpty(text);
        }

        public event EventHandler SelectionChanged;

        public int SelectedCount => _grid.SelectedRows.Count;

        /// <summary>Draws the content of a body cell. Returns false to leave the cell to the grid's own painting.</summary>
        public delegate bool CellRenderer(Graphics graphics, Rectangle bounds, string text, bool selected);

        /// <summary>
        /// Optional: lets a screen draw its cells (an icon and a word, say). The grid paints the cell background,
        /// selection and borders; the renderer paints what is inside.
        /// </summary>
        public CellRenderer BodyCellRenderer { get; set; }

        /// <summary>Height of each row, in pixels. Set before binding data.</summary>
        public int RowHeight
        {
            get => _grid.RowTemplate.Height;
            set => _grid.RowTemplate.Height = value;
        }

        /// <summary>Binds the grid. <paramref name="columns"/> are "Property=Header" pairs; when given, other properties are hidden.</summary>
        public void SetData<T>(IEnumerable<T> items, params string[] columns)
        {
            _all = items.Cast<object>().ToList();
            _itemType = typeof(T);
            _properties = typeof(T).GetProperties().ToDictionary(p => p.Name);
            _stringProperties = _properties.Values.Where(p => p.PropertyType == typeof(string)).ToArray();
            _columnMap = columns.Select(c => c.Split(new[] { '=' }, 2)).ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : p[0]);
            _columnOrder = columns.Select(c => c.Split('=')[0]).ToList();

            // A different table shape means the old column filters no longer apply; the same shape keeps them.
            var signature = typeof(T).FullName + "|" + string.Join(",", columns);
            if (signature != _signature)
            {
                _signature = signature;
                _valueFilters.Clear();
            }

            ApplyFilter();
        }

        public IReadOnlyList<T> GetSelected<T>()
        {
            return _grid.SelectedRows.Cast<DataGridViewRow>().OrderBy(r => r.Index).Select(r => r.DataBoundItem).OfType<T>().ToList();
        }

        public IReadOnlyList<T> GetVisible<T>()
        {
            return _grid.Rows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem).OfType<T>().ToList();
        }

        /// <summary>The table as displayed: visible columns in display order, rows in current filter and sort order.</summary>
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

        /// <summary>Gives a column a larger or smaller share of the width than the others (the default is 100). Call after <see cref="SetData{T}"/>.</summary>
        public void SetColumnWeight(string property, float weight)
        {
            var column = _grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.DataPropertyName == property);
            if (column != null)
            {
                column.FillWeight = weight;
            }
        }

        /// <summary>The narrowest a column may get before the grid scrolls sideways instead. Call after <see cref="SetData{T}"/>.</summary>
        public void SetColumnMinimumWidth(string property, int pixels)
        {
            var column = _grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.DataPropertyName == property);
            if (column != null)
            {
                column.MinimumWidth = pixels;
            }
        }

        /// <summary>Redraws the cells after properties of the listed objects changed in place, keeping the selection.</summary>
        public void RefreshCells()
        {
            _grid.Invalidate();
        }

        /// <summary>Sorts as if the user had clicked the title of the column bound to <paramref name="property"/>.</summary>
        public void SortBy(string property, bool ascending = true)
        {
            var column = _grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.DataPropertyName == property);
            if (column != null)
            {
                _grid.Sort(column, ascending ? ListSortDirection.Ascending : ListSortDirection.Descending);
            }
        }

        /// <summary>Every distinct value of a column across all rows (not only the filtered ones), sorted.</summary>
        public IReadOnlyList<string> GetColumnValues(string property)
        {
            if (!_properties.TryGetValue(property, out var info))
            {
                return new string[0];
            }

            return _all.Select(item => info.GetValue(item)?.ToString() ?? string.Empty)
                .Distinct().OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>Keeps only rows whose value in the column is one of <paramref name="allowed"/>; null or everything removes the filter.</summary>
        public void SetValueFilter(string property, IEnumerable<string> allowed)
        {
            var set = allowed == null ? null : new HashSet<string>(allowed);
            if (set == null || GetColumnValues(property).All(set.Contains))
            {
                _valueFilters.Remove(property);
            }
            else
            {
                _valueFilters[property] = set;
            }

            ApplyFilter();
        }

        public bool HasValueFilter(string property)
        {
            return _valueFilters.ContainsKey(property);
        }

        public void ClearFilters()
        {
            _valueFilters.Clear();
            if (_filter.Text.Length > 0)
            {
                _filter.Text = string.Empty; // its TextChanged re-applies the filter
            }
            else
            {
                ApplyFilter();
            }
        }

        private bool Matches(object item, string search)
        {
            if (search.Length > 0 && !_stringProperties.Any(p => ((string)p.GetValue(item) ?? string.Empty).IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0))
            {
                return false;
            }

            foreach (var filter in _valueFilters)
            {
                if (_properties.TryGetValue(filter.Key, out var property) && !filter.Value.Contains(property.GetValue(item)?.ToString() ?? string.Empty))
                {
                    return false;
                }
            }

            return true;
        }

        private void ApplyFilter()
        {
            var search = _filter.Text.Trim();
            var filtering = search.Length > 0 || _valueFilters.Count > 0;
            var matches = filtering ? _all.Where(item => Matches(item, search)).ToList() : _all.ToList();

            // Rebinding resets the sort; remember it so filtering does not undo what the user chose.
            var sortedProperty = _grid.SortedColumn?.DataPropertyName;
            var sortOrder = _grid.SortOrder;

            var listType = typeof(SortableBindingList<>).MakeGenericType(_itemType);
            var typed = typeof(Enumerable).GetMethod(nameof(Enumerable.Cast)).MakeGenericMethod(_itemType).Invoke(null, new object[] { matches });
            _grid.DataSource = Activator.CreateInstance(listType, typed);

            // A freshly bound grid selects its first row by itself; nothing should be selected until the user chooses.
            _grid.ClearSelection();
            _grid.CurrentCell = null;

            foreach (DataGridViewColumn column in _grid.Columns)
            {
                // Sorting is driven by OnHeaderClick, because a click on the funnel must not sort.
                column.SortMode = DataGridViewColumnSortMode.Programmatic;
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

            // Columns appear in the order the caller listed them, not in the order the class declares its properties.
            var position = 0;
            foreach (var property in _columnOrder)
            {
                var column = _grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.DataPropertyName == property && c.Visible);
                if (column != null)
                {
                    column.DisplayIndex = position++;
                }
            }

            if (sortedProperty != null && sortOrder != SortOrder.None)
            {
                SortBy(sortedProperty, sortOrder == SortOrder.Ascending);
            }

            _count.Text = filtering ? $"{matches.Count} of {_all.Count} records" : $"{_all.Count} records";
            _clearFilters.Visible = filtering;
            UpdateEmptyMessage(matches.Count, filtering);
            _grid.Invalidate();
        }

        private void OnHeaderClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.ColumnIndex < 0)
            {
                return;
            }

            var column = _grid.Columns[e.ColumnIndex];
            if (e.X >= column.Width - FunnelWidth)
            {
                ShowFilterPopup(column);
                return;
            }

            var ascending = !(_grid.SortedColumn == column && _grid.SortOrder == SortOrder.Ascending);
            _grid.Sort(column, ascending ? ListSortDirection.Ascending : ListSortDirection.Descending);
        }

        private void ShowFilterPopup(DataGridViewColumn column)
        {
            var property = column.DataPropertyName;
            var panel = new ColumnFilterPanel(GetColumnValues(property), _valueFilters.TryGetValue(property, out var current) ? current : null);

            _popup?.Dispose();
            var popup = new ToolStripDropDown { AutoClose = true, Padding = Padding.Empty, DropShadowEnabled = true };
            popup.Items.Add(new ToolStripControlHost(panel) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = panel.Size });
            panel.Applied += (s, values) =>
            {
                popup.Close();
                SetValueFilter(property, values);
            };
            panel.Cleared += (s, args) =>
            {
                popup.Close();
                SetValueFilter(property, null);
            };
            _popup = popup;

            var left = _grid.GetColumnDisplayRectangle(column.Index, false).Left;
            popup.Show(_grid, new Point(left, HeaderHeight));
            panel.FocusSearch();
        }

        private void PaintBodyCell(object sender, DataGridViewCellPaintingEventArgs e)
        {
            var renderer = BodyCellRenderer;
            if (renderer == null || e.RowIndex < 0 || e.ColumnIndex < 0 || !(e.Value is string text) || text.Length == 0)
            {
                return;
            }

            var selected = (e.State & DataGridViewElementStates.Selected) != 0;
            e.PaintBackground(e.CellBounds, selected);
            if (renderer(e.Graphics, e.CellBounds, text, selected))
            {
                e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Draws column headers ourselves: the title, the sort arrow and the funnel (blue while the column is filtered).
        /// </summary>
        private void PaintHeaderCell(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex != -1 || e.ColumnIndex < 0)
            {
                return;
            }

            e.Handled = true;
            var g = e.Graphics;
            var bounds = e.CellBounds;
            var column = _grid.Columns[e.ColumnIndex];

            using (var background = new SolidBrush(Theme.GroupBackground))
            using (var border = new Pen(Theme.Border))
            {
                g.FillRectangle(background, bounds);
                g.DrawLine(border, bounds.Right - 1, bounds.Top, bounds.Right - 1, bounds.Bottom - 1);
                g.DrawLine(border, bounds.Left, bounds.Bottom - 1, bounds.Right - 1, bounds.Bottom - 1);
            }

            var sortDirection = column.HeaderCell.SortGlyphDirection;
            var arrowRoom = sortDirection != SortOrder.None ? SortArrowWidth : 0;
            TextRenderer.DrawText(g, e.FormattedValue?.ToString() ?? string.Empty, Theme.FontBold,
                new Rectangle(bounds.Left + 4, bounds.Top, Math.Max(0, bounds.Width - FunnelWidth - arrowRoom - 4), bounds.Height), Theme.TextPrimary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            var direction = sortDirection;
            if (direction != SortOrder.None)
            {
                var x = bounds.Right - FunnelWidth - SortArrowWidth / 2;
                var y = bounds.Top + bounds.Height / 2;
                var points = direction == SortOrder.Ascending
                    ? new[] { new Point(x - 4, y + 2), new Point(x + 4, y + 2), new Point(x, y - 3) }
                    : new[] { new Point(x - 4, y - 2), new Point(x + 4, y - 2), new Point(x, y + 3) };
                using (var brush = new SolidBrush(Theme.TextSecondary))
                {
                    g.FillPolygon(brush, points);
                }
            }

            var active = _valueFilters.ContainsKey(column.DataPropertyName);
            TextRenderer.DrawText(g, Glyphs.Filter, Theme.FontIconSmall,
                new Rectangle(bounds.Right - FunnelWidth, bounds.Top, FunnelWidth - 2, bounds.Height),
                active ? Theme.Accent : Theme.TextDisabled,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        /// <summary>
        /// A DataGridView that receives keyboard focus with nothing selected normally selects its first row by
        /// itself. Here selection is only ever the user's own act: clicking a row, or arrowing to one. Several
        /// screens act on "what is selected" (the write features above all), so a row must never be selected by
        /// accident.
        /// </summary>
        private sealed class QuietGrid : DataGridView
        {
            protected override void OnEnter(EventArgs e)
            {
                var selected = SelectedRows.Cast<DataGridViewRow>().Select(r => r.Index).ToList();
                var current = CurrentCell;
                base.OnEnter(e);

                // A click on a row selects that row itself. Anything else (Tab, a view being shown) must leave the
                // selection exactly as it was: focus can add a first-row selection, or replace an existing one with it.
                if ((MouseButtons & MouseButtons.Left) != 0)
                {
                    return;
                }

                if (!ReferenceEquals(CurrentCell, current) || SelectedRows.Count != selected.Count)
                {
                    CurrentCell = current;
                    ClearSelection();
                    foreach (var index in selected.Where(i => i < Rows.Count))
                    {
                        Rows[index].Selected = true;
                    }
                }
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, string lParam);

        private static void SetCue(TextBox box, string text)
        {
            SendMessage(box.Handle, EmSetCueBanner, new IntPtr(1), text);
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
