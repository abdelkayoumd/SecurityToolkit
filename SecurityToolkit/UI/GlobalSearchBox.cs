using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Features;
using SecurityToolkit.Models;
using SecurityToolkit.Services;

namespace SecurityToolkit.UI
{
    /// <summary>
    /// The search box of the title bar: type part of a user, team, role or table name, and the matches drop down
    /// under it. Enter (or a click) on a match opens the list of screens that can be opened on it.
    /// Up and Down move through the matches while typing; Escape closes them.
    /// </summary>
    internal sealed class GlobalSearchBox : Panel
    {
        private const int ListWidth = 460;
        private const int RowHeight = 40;
        private const int MaxRows = 10;

        private readonly TextBox _box;
        private readonly ListBox _list;
        private readonly Timer _typing;
        private IReadOnlyList<SearchHit> _hits = new SearchHit[0];
        private bool _requested;

        public GlobalSearchBox()
        {
            Width = 320;
            Height = 26;
            Padding = new Padding(6, 4, 6, 3);
            BackColor = Color.White;
            Margin = new Padding(0, 7, 0, 7);

            _box = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = Theme.FontRegular, Name = "GlobalSearch" };
            TextCue.Set(_box, "Find a user, team, role or table (Ctrl+K)");
            Controls.Add(_box);

            _list = new ListBox
            {
                Visible = false,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = RowHeight,
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false,
                Font = Theme.FontRegular,
                Name = "GlobalSearchResults"
            };
            _list.DrawItem += DrawHit;
            _list.MouseClick += (s, e) =>
            {
                var index = _list.IndexFromPoint(e.Location);
                if (index >= 0)
                {
                    _list.SelectedIndex = index;
                    OpenMenu(index);
                }
            };
            _list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && _list.SelectedIndex >= 0)
                {
                    OpenMenu(_list.SelectedIndex);
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    CloseList();
                    _box.Focus();
                }
            };

            _typing = new Timer { Interval = 150 };
            _typing.Tick += (s, e) =>
            {
                _typing.Stop();
                Search();
            };
            _box.TextChanged += (s, e) =>
            {
                _typing.Stop();
                _typing.Start();
            };
            _box.KeyDown += OnBoxKeyDown;
            _box.Enter += (s, e) =>
            {
                if (Query.Trim().Length == 0)
                {
                    ShowRecent();
                }
            };
            _box.LostFocus += (s, e) => BeginInvoke(new Action(() =>
            {
                // Clicking a match moves the focus to the list; anywhere else closes it.
                if (!_list.Focused && !_menuOpen)
                {
                    CloseList();
                }
            }));
        }

        /// <summary>Where the list of matches is drawn: the shell, so it can sit over the screens below the title bar.</summary>
        public Control OverlayHost { get; set; }

        /// <summary>What there is to search: the security graph and the tables, or nulls while they are not loaded.</summary>
        public Func<Tuple<SecurityGraph, IReadOnlyList<TableInfo>>> Source { get; set; }

        /// <summary>Asked once when there is nothing to search yet: load the data, then call <see cref="Search"/>.</summary>
        public Action<Action> RequestData { get; set; }

        /// <summary>The users, teams, roles and tables opened lately: shown while the box is empty.</summary>
        public Func<IReadOnlyList<Subject>> Recent { get; set; }

        /// <summary>Opens a screen on a subject.</summary>
        public Action<string, Subject> Open { get; set; }

        /// <summary>The screen being shown, which the menu leaves out.</summary>
        public Func<string> CurrentFeatureId { get; set; }

        public string Query
        {
            get => _box.Text;
            set => _box.Text = value;
        }

        public IReadOnlyList<SearchHit> Hits => _hits;

        public bool ListShown => _list.Visible;

        private bool _menuOpen;

        public void FocusBox()
        {
            _box.Focus();
            _box.SelectAll();
        }

        /// <summary>Runs the search for what is typed and shows the matches.</summary>
        public void Search()
        {
            var source = Source?.Invoke();
            var graph = source?.Item1;
            var tables = source?.Item2;
            if (Query.Trim().Length == 0)
            {
                ShowRecent();
                return;
            }

            if (graph == null && !_requested && RequestData != null)
            {
                // The first search of a connection loads what it searches; the matches show once it has.
                _requested = true;
                RequestData(Search);
                return;
            }

            _hits = GlobalSearch.Find(Query, graph, tables);
            ShowList();
        }

        /// <summary>With nothing typed, the list shows what was opened lately (or nothing, when that is empty).</summary>
        private void ShowRecent()
        {
            var recent = Recent?.Invoke() ?? new Subject[0];
            if (recent.Count == 0)
            {
                _hits = new SearchHit[0];
                CloseList();
                return;
            }

            _hits = recent.Select(s => new SearchHit { Subject = s, Detail = "Opened lately · " + s.KindText }).ToList();
            ShowList();
        }

        /// <summary>The connection changed: what was searched belongs to the old one.</summary>
        public void Reset()
        {
            _requested = false;
            _hits = new SearchHit[0];
            CloseList();
        }

        /// <summary>The menu of screens for a match (internal so tests can read it without showing it).</summary>
        internal ContextMenuStrip BuildMenu(SearchHit hit)
        {
            var menu = new ContextMenuStrip { Font = Theme.FontRegular, ShowImageMargin = false };
            menu.Items.Add(new ToolStripLabel(SubjectActions.Heading(hit.Subject)) { Font = Theme.FontBold, ForeColor = Theme.TextSecondary });
            menu.Items.AddRange(SubjectActions.MenuItems(hit.Subject, CurrentFeatureId?.Invoke(), (featureId, subject) =>
            {
                CloseList();
                Open?.Invoke(featureId, subject);
            }).ToArray());
            return menu;
        }

        private void OnBoxKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Down:
                case Keys.Up:
                    if (_list.Visible && _list.Items.Count > 0)
                    {
                        var next = _list.SelectedIndex + (e.KeyCode == Keys.Down ? 1 : -1);
                        _list.SelectedIndex = Math.Max(0, Math.Min(_list.Items.Count - 1, next));
                    }

                    e.Handled = true;
                    break;
                case Keys.Enter:
                    _typing.Stop();
                    if (!_list.Visible)
                    {
                        Search();
                    }

                    if (_list.Visible && _list.Items.Count > 0)
                    {
                        OpenMenu(Math.Max(0, _list.SelectedIndex));
                    }

                    e.SuppressKeyPress = true;
                    break;
                case Keys.Escape:
                    if (_list.Visible)
                    {
                        CloseList();
                    }
                    else
                    {
                        _box.Clear();
                    }

                    e.SuppressKeyPress = true;
                    break;
            }
        }

        private void ShowList()
        {
            var host = OverlayHost ?? Parent;
            if (host == null)
            {
                return;
            }

            _list.BeginUpdate();
            _list.Items.Clear();
            if (_hits.Count == 0)
            {
                _list.Items.Add("Nothing matches \"" + Query.Trim() + "\".");
            }
            else
            {
                _list.Items.AddRange(_hits.Cast<object>().ToArray());
            }

            _list.EndUpdate();
            if (_hits.Count > 0)
            {
                _list.SelectedIndex = 0;
            }

            if (_list.Parent != host)
            {
                host.Controls.Add(_list);
            }

            var below = host.PointToClient(PointToScreen(new Point(0, Height + 2)));
            var width = Math.Min(ListWidth, Math.Max(Width, host.ClientSize.Width - 16));
            var left = Math.Max(8, Math.Min(below.X, host.ClientSize.Width - width - 8));
            _list.Bounds = new Rectangle(left, below.Y, width, RowHeight * Math.Max(1, Math.Min(MaxRows, _list.Items.Count)) + 2);
            _list.Visible = true;
            _list.BringToFront();
        }

        private void CloseList()
        {
            _list.Visible = false;
        }

        private void OpenMenu(int index)
        {
            if (index < 0 || index >= _hits.Count)
            {
                return;
            }

            var menu = BuildMenu(_hits[index]);
            var row = _list.GetItemRectangle(index);
            _menuOpen = true;
            menu.Closed += (s, e) =>
            {
                _menuOpen = false;
                BeginInvoke(new Action(menu.Dispose));
            };
            menu.Show(_list, new Point(Math.Min(row.Left + 40, _list.Width - 40), row.Bottom));

            // The first screen is ready for Enter.
            var first = menu.Items.OfType<ToolStripMenuItem>().FirstOrDefault();
            first?.Select();
        }

        private void DrawHit(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
            {
                return;
            }

            var selected = (e.State & DrawItemState.Selected) != 0;
            using (var back = new SolidBrush(selected ? Theme.AccentSelected : Theme.PanelBackground))
            {
                e.Graphics.FillRectangle(back, e.Bounds);
            }

            if (!(_list.Items[e.Index] is SearchHit hit))
            {
                TextRenderer.DrawText(e.Graphics, _list.Items[e.Index].ToString(), Theme.FontRegular, new Rectangle(e.Bounds.Left + 12, e.Bounds.Top, e.Bounds.Width - 16, e.Bounds.Height),
                    Theme.TextSecondary, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                return;
            }

            var icon = new Rectangle(e.Bounds.Left + 8, e.Bounds.Top, 24, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, Glyph(hit.Subject.Kind), Theme.FontIcon, icon, Theme.Accent, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);

            var text = new Rectangle(icon.Right + 6, e.Bounds.Top + 3, e.Bounds.Width - icon.Right - 14, 18);
            TextRenderer.DrawText(e.Graphics, hit.Subject.Name, Theme.FontBold, text, Theme.TextPrimary, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            var detail = new Rectangle(text.Left, text.Bottom, text.Width, 16);
            TextRenderer.DrawText(e.Graphics, hit.Detail, Theme.FontSmall, detail, Theme.TextSecondary, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private static string Glyph(SubjectKind kind)
        {
            switch (kind)
            {
                case SubjectKind.User: return Glyphs.Contact;
                case SubjectKind.Team: return Glyphs.Team;
                case SubjectKind.Role: return Glyphs.Shield;
                default: return Glyphs.Grid;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _typing.Dispose();
                _list.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
