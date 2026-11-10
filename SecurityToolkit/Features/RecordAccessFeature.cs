using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using SecurityToolkit.UI;
using Label = System.Windows.Forms.Label;

namespace SecurityToolkit.Features
{
    /// <summary>One user or team in the list on the left.</summary>
    internal sealed class PrincipalRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string UserName { get; set; }
        public string BusinessUnit { get; set; }
        public Guid? BusinessUnitId { get; set; }

        /// <summary>A user's status, or a team's type.</summary>
        public string Detail { get; set; }
        public bool IsTeam { get; set; }

        public AccessPrincipal ToPrincipal()
        {
            return new AccessPrincipal { Id = Id, Name = Name, IsTeam = IsTeam, BusinessUnitId = BusinessUnitId, BusinessUnitName = BusinessUnit };
        }
    }

    /// <summary>
    /// "Can this user (or team) reach this record?" Pick a user or team on the left and a record above (a pasted link or
    /// id, or a name to search for). The right says, for each right, whether the principal has it and why: the role that
    /// reaches the record, a share, or what falls short. A second tab lists who the record is shared with. The answer
    /// itself comes from the platform; the reasons come from the roles, ownership and sharing.
    /// </summary>
    internal sealed class RecordAccessFeature : IFeature
    {
        public string Id => "record-access";
        public string Title => "Record access";
        public string Glyph => Glyphs.RecordAccess;
        public FeatureGroup Group => FeatureGroup.UnderstandAccess;
        public int Order => 25;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new RecordAccessView(host);
        }

        private sealed class Choice<T>
        {
            public Choice(T value, string text) { Value = value; Text = text; }
            public T Value { get; }
            public string Text { get; }
            public override string ToString() => Text;
        }

        private sealed class Loaded
        {
            public SecurityGraph Graph { get; set; }
            public IReadOnlyList<TableInfo> Tables { get; set; }
        }

        private sealed class RecordLoaded
        {
            public RecordFacts Facts { get; set; }
            public IReadOnlyList<ShareRow> Shares { get; set; }
        }

        private sealed class AccessChecked
        {
            public IReadOnlyDictionary<PrivilegeKind, IReadOnlyDictionary<Guid, PrivilegeDepth>> Grants { get; set; }
            public int? PlatformMask { get; set; }
            public string PlatformError { get; set; }
        }

        private sealed class RecordAccessView : MasterDetailView
        {
            private const string AccessTab = "Access to the record";
            private const string SharingTab = "Shared with";

            private readonly ComboBox _cmbTable;
            private readonly TextBox _txtRecord;
            private readonly ComboBox _cmbMatches;
            private readonly TabStrip _tabs;
            private readonly FilterableGrid _sharesGrid;
            private readonly Label _sideHeading;
            private ComboBox _cmbPrincipal;
            private CheckBox _chkDisabled;
            private IReadOnlyList<TableInfo> _tables;
            private RecordFacts _record;
            private IReadOnlyList<ShareRow> _shares = new ShareRow[0];
            private AccessPrincipal _principal;
            private bool _picking;

            public RecordAccessView(IFeatureHost host)
                : base(host, "Users (select one)", AccessTab)
            {
                Master.MultiSelect = false;
                Master.CellToolTip = (item, column) => column == "Name" && item is PrincipalRow row && !string.IsNullOrEmpty(row.UserName) ? row.UserName : null;
                Grid.BodyCellRenderer = PaintResult;
                Grid.RowHeight = 34;
                // The reason can be longer than the cell: the full text shows when the mouse rests on it.
                Grid.CellToolTip = (item, column) => column == "Why" && item is RecordAccessRow row ? row.Why : null;
                InfoLabel.AutoEllipsis = true;

                // The record: a table, then a link, an id, or a name.
                _cmbTable = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDown,
                    Width = 250,
                    Font = Theme.FontRegular,
                    Margin = new Padding(0, 6, 6, 0),
                    AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                    AutoCompleteSource = AutoCompleteSource.ListItems,
                    Name = "RecordTable"
                };
                _txtRecord = new TextBox { Width = 330, Font = Theme.FontRegular, Margin = new Padding(0, 6, 6, 0), Name = "RecordText", BorderStyle = BorderStyle.FixedSingle };
                TextCue.Set(_txtRecord, "paste a record link or id, or type a name");
                _txtRecord.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter)
                    {
                        FindRecord();
                        e.SuppressKeyPress = true;
                    }
                };
                var find = new Button { Text = "Find", Width = 64, Height = 25, Margin = new Padding(0, 5, 8, 0), Name = "FindRecord", FlatStyle = FlatStyle.System };
                find.Click += (s, e) => FindRecord();
                _cmbMatches = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320, Font = Theme.FontRegular, Margin = new Padding(0, 6, 0, 0), Visible = false, Name = "RecordMatches" };
                _cmbMatches.SelectedIndexChanged += (s, e) =>
                {
                    if (!_picking && _cmbMatches.SelectedItem is RecordMatch match && SelectedTable != null)
                    {
                        LoadRecord(SelectedTable.LogicalName, match.Id);
                    }
                };

                var sentence = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, WrapContents = false, Padding = new Padding(10, 0, 0, 0), BackColor = Theme.PageBackground };
                sentence.Controls.Add(SentenceLabel("Record in"));
                sentence.Controls.Add(_cmbTable);
                sentence.Controls.Add(_txtRecord);
                sentence.Controls.Add(find);
                sentence.Controls.Add(_cmbMatches);

                // Two tabs on the right: the access itself, and who the record is shared with.
                _tabs = new TabStrip { Name = "RecordAccessTabs" };
                _tabs.AddTab(AccessTab);
                _tabs.AddTab(SharingTab);
                _sharesGrid = new FilterableGrid { Visible = false, EmptyMessage = "Nobody has been given access to this record by sharing." };

                var side = Grid.Parent;
                _sideHeading = side.Controls.OfType<Label>().First();
                _sideHeading.Visible = false; // the tabs replace it
                side.Controls.Remove(Grid);
                var results = new Panel { Dock = DockStyle.Fill };
                results.Controls.Add(Grid);
                results.Controls.Add(_sharesGrid);
                side.Controls.Add(results);
                side.Controls.Add(_tabs);
                _tabs.SelectedIndexChanged += (s, e) =>
                {
                    Grid.Visible = _tabs.SelectedIndex == 0;
                    _sharesGrid.Visible = _tabs.SelectedIndex == 1;
                };

                // Added last, so it is laid out first and sits above the one-line summary.
                Controls.Add(sentence);
                InfoLabel.Text = "Connect to an environment to get started.";
            }

            protected override int MasterWidthPercent => 30;

            protected override string PromptText => "Choose a record above, then select a user or a team on the left.";

            protected override string EmptyText => _record == null
                ? "Pick a record above: paste its link or id, or type a name and press Find."
                : "Select a user or a team on the left to see their access to this record.";

            protected override bool IsLoaded => Graph != null && _tables != null;

            private TableInfo SelectedTable => (_cmbTable.SelectedItem as Choice<TableInfo>)?.Value;

            protected override void LoadData()
            {
                Host.RunAsync("Loading security data...", security => new Loaded { Graph = security.GetGraph(), Tables = security.GetTables() }, loaded =>
                {
                    Graph = loaded.Graph;
                    _tables = loaded.Tables;
                    OnGraphLoaded();
                });
            }

            protected override IEnumerable<Control> CreateCommands()
            {
                yield return Command(Theme.GlyphRefresh, "Refresh", Reload, strong: true);
                yield return CommandSeparator();
                yield return Command(Theme.GlyphClearSelection, "Clear selection", Master.ClearSelection);
                yield return Command(Theme.GlyphDownload, "Export to Excel", () => ExportUi.SaveGrid(this, _tabs.SelectedIndex == 0 ? Grid : _sharesGrid,
                    _tabs.SelectedIndex == 0 ? "RecordAccess.xlsx" : "RecordSharing.xlsx", _tabs.SelectedIndex == 0 ? "Record access" : "Shared with"));
                yield return new Label { Text = "Check access for", AutoSize = true, ForeColor = Theme.TextSecondary, Margin = new Padding(14, 12, 4, 0) };
                _cmbPrincipal = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90, Margin = new Padding(0, 8, 8, 0), Font = Theme.FontRegular, Name = "PrincipalType" };
                _cmbPrincipal.Items.AddRange(new object[] { "a user", "a team" });
                _cmbPrincipal.SelectedIndex = 0;
                _cmbPrincipal.SelectedIndexChanged += (s, e) =>
                {
                    _principal = null;
                    if (IsLoaded)
                    {
                        FillPrincipals();
                    }

                    ShowResult();
                };
                yield return _cmbPrincipal;
                _chkDisabled = CommandCheck("Include disabled users", () => { if (IsLoaded) { FillPrincipals(); } });
                yield return _chkDisabled;
            }

            protected override void Reset()
            {
                _tables = null;
                _record = null;
                _shares = new ShareRow[0];
                _principal = null;
                _cmbTable.Items.Clear();
                _cmbTable.Text = string.Empty;
                _cmbMatches.Items.Clear();
                _cmbMatches.Visible = false;
                _sharesGrid.SetData(new object[0]);
                _tabs.SetText(1, SharingTab);
                base.Reset();
            }

            private void Reload()
            {
                Host.EnsureConnection(() =>
                {
                    Host.Security.Invalidate();
                    LoadData();
                });
            }

            protected override void OnGraphLoaded()
            {
                if (!IsLoaded)
                {
                    return;
                }

                var current = SelectedTable?.LogicalName;
                _cmbTable.Items.Clear();
                _cmbTable.Items.AddRange(_tables.Select(t => (object)new Choice<TableInfo>(t, $"{t.DisplayName} ({t.LogicalName})")).ToArray());
                if (current != null)
                {
                    _cmbTable.SelectedIndex = _tables.ToList().FindIndex(t => t.LogicalName == current);
                }

                FillPrincipals();
                ShowResult();
            }

            private void FillPrincipals()
            {
                var teams = _cmbPrincipal != null && _cmbPrincipal.SelectedIndex == 1;
                Master.Parent.Controls.OfType<Label>().First().Text = teams ? "Teams (select one)" : "Users (select one)";
                if (teams)
                {
                    Master.SetData(Graph.Teams.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).Select(t => new PrincipalRow
                    {
                        Id = t.Id, Name = t.Name, BusinessUnit = t.BusinessUnitName, BusinessUnitId = t.BusinessUnitId, Detail = t.TypeName + " team", IsTeam = true
                    }).ToList(), "Name=Team", "BusinessUnit=Business Unit", "Detail=Type");
                }
                else
                {
                    var includeDisabled = _chkDisabled != null && _chkDisabled.Checked;
                    Master.SetData(Graph.Users.Where(u => includeDisabled || !u.IsDisabled).Select(u => new PrincipalRow
                    {
                        Id = u.Id, Name = u.FullName, UserName = u.DomainName, BusinessUnit = u.BusinessUnitName, BusinessUnitId = u.BusinessUnitId, Detail = u.Status
                    }).ToList(), "Name=Full Name", "BusinessUnit=Business Unit", "Detail=Status");
                }
            }

            protected override void OnMasterSelectionChanged()
            {
                var picked = Master.GetSelected<PrincipalRow>();
                if (picked.Count == 0)
                {
                    // Only cleared (by a filter, say): keep showing the last answer.
                    return;
                }

                _principal = picked[0].ToPrincipal();
                ShowResult();
            }

            // ----- finding the record -----

            private void FindRecord()
            {
                Host.EnsureConnection(() =>
                {
                    if (!IsLoaded)
                    {
                        return;
                    }

                    var text = _txtRecord.Text.Trim();
                    if (text.Length == 0)
                    {
                        InfoLabel.Text = "Paste a record link or id, or type part of its name, then press Find.";
                        return;
                    }

                    var table = SelectedTable?.LogicalName;
                    if (RecordLinks.TryParse(text, out var linkTable, out var id))
                    {
                        if (linkTable != null)
                        {
                            var index = _tables.ToList().FindIndex(t => t.LogicalName == linkTable);
                            if (index >= 0)
                            {
                                _cmbTable.SelectedIndex = index;
                            }

                            table = linkTable;
                        }

                        if (table == null)
                        {
                            InfoLabel.Text = "A bare id does not say which table the record is in: choose the table first, or paste the whole link.";
                            return;
                        }

                        _cmbMatches.Visible = false;
                        LoadRecord(table, id);
                        return;
                    }

                    if (table == null)
                    {
                        InfoLabel.Text = "Choose the table to search in first.";
                        return;
                    }

                    Host.RunAsync("Searching...", security => security.SearchRecords(table, text), matches =>
                    {
                        _picking = true;
                        _cmbMatches.Items.Clear();
                        _cmbMatches.Items.AddRange(matches.Cast<object>().ToArray());
                        _cmbMatches.Visible = matches.Count > 0;
                        _picking = false;
                        if (matches.Count == 0)
                        {
                            InfoLabel.Text = $"No {SelectedTable.DisplayName} record has a name containing \"{text}\".";
                        }
                        else if (matches.Count == 1)
                        {
                            _cmbMatches.SelectedIndex = 0;
                        }
                        else
                        {
                            InfoLabel.Text = $"{matches.Count} records match: pick one from the list.";
                        }
                    });
                });
            }

            private void LoadRecord(string table, Guid id)
            {
                Host.RunAsync("Reading the record...", security => new RecordLoaded { Facts = security.GetRecordFacts(table, id), Shares = security.GetShares(table, id) }, loaded =>
                {
                    _record = WithNames(loaded.Facts);
                    _shares = loaded.Shares.Select(WithName).ToList();
                    _sharesGrid.SetData(_shares, "Principal", "Type", "Rights");
                    _sharesGrid.SetColumnWeight("Rights", 240);
                    _tabs.SetText(1, $"{SharingTab} ({_shares.Count})");
                    ShowResult();
                });
            }

            /// <summary>Owner and business unit names the platform did not send back, from what is already loaded.</summary>
            private RecordFacts WithNames(RecordFacts facts)
            {
                if (string.IsNullOrEmpty(facts.OwnerName) && facts.OwnerId.HasValue)
                {
                    facts.OwnerName = facts.OwnerType == "team"
                        ? Graph.Teams.FirstOrDefault(t => t.Id == facts.OwnerId)?.Name
                        : Graph.Users.FirstOrDefault(u => u.Id == facts.OwnerId)?.FullName;
                }

                if (string.IsNullOrEmpty(facts.BusinessUnitName) && facts.BusinessUnitId.HasValue)
                {
                    facts.BusinessUnitName = Graph.BusinessUnits.FirstOrDefault(b => b.Id == facts.BusinessUnitId)?.Name;
                }

                return facts;
            }

            private ShareRow WithName(ShareRow share)
            {
                if (string.IsNullOrEmpty(share.Principal))
                {
                    share.Principal = share.Type == "Team"
                        ? Graph.Teams.FirstOrDefault(t => t.Id == share.PrincipalId)?.Name
                        : Graph.Users.FirstOrDefault(u => u.Id == share.PrincipalId)?.FullName;
                }

                return share;
            }

            // ----- the answer -----

            private void ShowResult()
            {
                if (Graph == null || _record == null || _principal == null)
                {
                    if (_record != null)
                    {
                        InfoLabel.Text = RecordSummary(_record);
                    }
                    else if (Graph != null)
                    {
                        InfoLabel.Text = PromptText;
                    }

                    Grid.SetData(new object[0]);
                    return;
                }

                var graph = Graph;
                var record = _record;
                var principal = _principal;
                var roleIds = principal.IsTeam
                    ? graph.RolesOfTeam(principal.Id).Select(r => r.Id).ToList()
                    : AccessMatrixBuilder.RoleIdsOf(graph, graph.Users.First(u => u.Id == principal.Id));
                var shares = _shares;

                Host.RunAsync("Checking access...", security =>
                {
                    var checkedAccess = new AccessChecked
                    {
                        Grants = RecordAccessAnalyzer.Rights.ToDictionary(k => k, k => security.GetRoleGrantsFor(roleIds, record.Table, k))
                    };
                    try
                    {
                        checkedAccess.PlatformMask = security.GetPrincipalAccess(principal, record.Table, record.Id);
                    }
                    catch (Exception ex)
                    {
                        checkedAccess.PlatformError = ex.Message;
                    }

                    return checkedAccess;
                }, result =>
                {
                    var rows = RecordAccessAnalyzer.Analyze(graph, principal, record, result.Grants, shares, result.PlatformMask);
                    Grid.SetData(rows, "Right", "Result", "Why");
                    Grid.SetColumnWeight("Right", 70);
                    Grid.SetColumnWeight("Result", 70);
                    Grid.SetColumnWeight("Why", 560);

                    var allowed = rows.Count(r => r.Result == "Yes");
                    var basis = result.PlatformMask.HasValue
                        ? string.Empty
                        : $" The platform's own answer could not be read ({result.PlatformError}), so this is based on roles and shares only.";
                    var differs = rows.Any(r => r.Differs) ? " Rows marked with a warning differ from my own reading." : string.Empty;
                    InfoLabel.Text = $"{RecordSummary(record)} {principal.Name} has {allowed} of {rows.Count} rights; {RecordAccessAnalyzer.Relation(graph, principal, record)}.{basis}{differs}";
                });
            }

            private static string RecordSummary(RecordFacts record)
            {
                var name = string.IsNullOrWhiteSpace(record.Name) ? record.Id.ToString() : record.Name;
                var owner = record.Ownership == TableOwnership.UserOrTeam && record.OwnerName != null
                    ? $" Owner: {record.OwnerName} ({(record.OwnedByTeam ? "team" : "user")}), business unit {record.BusinessUnitName ?? "unknown"}."
                    : record.Ownership == TableOwnership.BusinessUnit ? $" Business unit: {record.BusinessUnitName ?? "unknown"}." : " Owned by the organization.";
                return $"{name} ({record.TableDisplayName}).{owner}";
            }

            /// <summary>"Yes" and "No" drawn with a green tick and a red cross.</summary>
            private static bool PaintResult(Graphics g, Rectangle bounds, string text, bool selected)
            {
                if (text != "Yes" && text != "No")
                {
                    return false;
                }

                var yes = text == "Yes";
                var color = yes ? Color.FromArgb(16, 124, 16) : Color.FromArgb(164, 38, 44);
                var icon = new Rectangle(bounds.Left + 8, bounds.Top + (bounds.Height - 18) / 2, 18, 18);
                TextRenderer.DrawText(g, yes ? "" : "", Theme.FontIcon, icon, color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, text, Theme.FontBold, new Rectangle(icon.Right + 4, bounds.Top, bounds.Width - 34, bounds.Height), color,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                return true;
            }

            private static Label SentenceLabel(string text)
            {
                return new Label { Text = text, AutoSize = true, Font = Theme.FontBold, ForeColor = Theme.TextSecondary, Margin = new Padding(0, 10, 6, 0) };
            }
        }
    }
}
