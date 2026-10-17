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
    /// <summary>
    /// "Why does this user have access?" Pick users on the left, a privilege and a table in the sentence above the
    /// results, and see every role that grants it: the role, how the user holds it (directly or through which team),
    /// and how far it reaches, with the strongest route marked.
    /// </summary>
    internal sealed class ExplainAccessFeature : IFeature
    {
        public string Id => "explain-access";
        public string Title => "Explain access";
        public string Glyph => Glyphs.Help;
        public FeatureGroup Group => FeatureGroup.Explore;
        public int Order => 52;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new ExplainAccessView(host);
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

        private sealed class ExplainAccessView : MasterDetailView
        {
            private readonly ComboBox _cmbPrivilege;
            private readonly ComboBox _cmbTable;
            private IReadOnlyList<TableInfo> _tables;
            private IReadOnlyList<UserInfo> _users = new UserInfo[0];
            private CheckBox _chkDisabled;

            public ExplainAccessView(IFeatureHost host)
                : base(host, "Users (Ctrl/Shift-click to select several)", "How they get it")
            {
                _cmbPrivilege = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Font = Theme.FontRegular, Margin = new Padding(0, 6, 6, 0) };
                _cmbPrivilege.Items.AddRange(Enum.GetValues(typeof(PrivilegeKind)).Cast<PrivilegeKind>()
                    .Select(k => (object)new Choice<PrivilegeKind>(k, Privileges.Label(k))).ToArray());
                _cmbPrivilege.SelectedIndex = 1; // Read
                _cmbPrivilege.SelectedIndexChanged += (s, e) => Explain();

                // Hundreds of tables: typing narrows the list.
                _cmbTable = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDown,
                    Width = 280,
                    Font = Theme.FontRegular,
                    Margin = new Padding(0, 6, 6, 0),
                    AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                    AutoCompleteSource = AutoCompleteSource.ListItems
                };
                _cmbTable.SelectedIndexChanged += (s, e) => Explain();
                _cmbTable.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter)
                    {
                        PickTypedTable();
                        e.SuppressKeyPress = true;
                    }
                };

                var sentence = new FlowLayoutPanel
                {
                    Dock = DockStyle.Top,
                    Height = 34,
                    WrapContents = false,
                    Padding = new Padding(10, 0, 0, 0),
                    BackColor = Theme.PageBackground
                };
                sentence.Controls.Add(SentenceLabel("Why can the selected users"));
                sentence.Controls.Add(_cmbPrivilege);
                sentence.Controls.Add(SentenceLabel("on"));
                sentence.Controls.Add(_cmbTable);
                sentence.Controls.Add(SentenceLabel("?"));

                // Added last, so it is laid out first and sits above the one-line result.
                Controls.Add(sentence);
                // Depths are drawn as in the Dynamics 365 security role editor: an icon and the depth in words.
                Grid.BodyCellRenderer = DepthCell.Paint;
                InfoLabel.Text = "Connect to an environment to get started.";
            }

            protected override string PromptText => "Select users on the left, then choose a privilege and a table above.";

            protected override bool IsLoaded => Graph != null && _tables != null;

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
                yield return Command(Theme.GlyphDownload, "Export to Excel", () => ExportUi.SaveGrid(this, Grid, "ExplainAccess.xlsx", "Access"));
                _chkDisabled = CommandCheck("Include disabled users", () => { if (IsLoaded) { FillUsers(); } });
                yield return _chkDisabled;
            }

            protected override void Reset()
            {
                _tables = null;
                _users = new UserInfo[0];
                _cmbTable.Items.Clear();
                _cmbTable.Text = string.Empty;
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
                    var index = _tables.ToList().FindIndex(t => t.LogicalName == current);
                    _cmbTable.SelectedIndex = index;
                }

                FillUsers();
            }

            private void FillUsers()
            {
                var includeDisabled = _chkDisabled != null && _chkDisabled.Checked;
                Master.SetData(Graph.Users.Where(u => includeDisabled || !u.IsDisabled).ToList(),
                    "FullName=Full Name", "DomainName=User Name", "BusinessUnitName=Business Unit", "Status");
                if (_users.Count == 0 || SelectedTable == null)
                {
                    InfoLabel.Text = PromptText;
                }
            }

            protected override void OnMasterSelectionChanged()
            {
                var picked = Master.GetSelected<UserInfo>();
                if (picked.Count == 0)
                {
                    // Only cleared (by a filter, say): keep showing the last answer.
                    return;
                }

                _users = picked;
                Explain();
            }

            private TableInfo SelectedTable => (_cmbTable.SelectedItem as Choice<TableInfo>)?.Value;

            /// <summary>Enter in the table box picks the table whose name was typed, if there is exactly one such table.</summary>
            private void PickTypedTable()
            {
                var typed = _cmbTable.Text.Trim();
                if (typed.Length == 0 || _tables == null)
                {
                    return;
                }

                var matches = _tables.Where(t => string.Equals(t.DisplayName, typed, StringComparison.CurrentCultureIgnoreCase)
                                                 || string.Equals(t.LogicalName, typed, StringComparison.CurrentCultureIgnoreCase)
                                                 || $"{t.DisplayName} ({t.LogicalName})".Equals(typed, StringComparison.CurrentCultureIgnoreCase)).ToList();
                if (matches.Count == 1)
                {
                    _cmbTable.SelectedIndex = _tables.ToList().IndexOf(matches[0]);
                }
            }

            private void Explain()
            {
                var table = SelectedTable;
                if (!IsLoaded || _users.Count == 0 || table == null || _cmbPrivilege.SelectedItem == null)
                {
                    return;
                }

                var users = _users;
                var kind = ((Choice<PrivilegeKind>)_cmbPrivilege.SelectedItem).Value;
                var graph = Graph;

                // Only the roles these users hold matter, so only their privileges are read.
                var roleIds = users.SelectMany(u => AccessMatrixBuilder.RoleIdsOf(graph, u)).Distinct().ToList();
                Host.RunAsync("Checking access...", security => security.GetRoleGrantsFor(roleIds, table.LogicalName, kind), grants =>
                {
                    var answers = users.Select(u => AccessExplainer.Explain(graph, u, grants)).ToList();
                    var rows = new List<AccessReasonRow>();
                    foreach (var answer in answers)
                    {
                        if (answer.HasAccess)
                        {
                            rows.AddRange(answer.Routes);
                        }
                        else
                        {
                            rows.Add(new AccessReasonRow { User = answer.User.FullName, Role = "(no role grants this)", RoleBusinessUnit = string.Empty, Via = string.Empty, Depth = string.Empty, Strongest = string.Empty });
                        }
                    }

                    Grid.SetData(rows, "User", "Role", "RoleBusinessUnit=Role's business unit", "Via", "Depth", "Strongest");
                    Grid.SetColumnMinimumWidth("Depth", 200);
                    InfoLabel.Text = Describe(answers, kind, table.DisplayName);
                });
            }

            private static string Describe(IReadOnlyList<AccessAnswer> answers, PrivilegeKind kind, string table)
            {
                if (answers.Count == 1)
                {
                    var text = AccessExplainer.Summarize(answers[0], kind, table);
                    return AccessExplainer.NeedsTeamCaveat(answers[0])
                        ? text + " Roles through a team may apply only to records the team owns, depending on its member privilege inheritance."
                        : text;
                }

                var withAccess = answers.Count(a => a.HasAccess);
                return $"{withAccess} of {answers.Count} selected users have a role that grants {Privileges.Label(kind)} on {table}.";
            }

            private static Label SentenceLabel(string text)
            {
                return new Label
                {
                    Text = text,
                    AutoSize = true,
                    Font = Theme.FontBold,
                    ForeColor = Theme.TextSecondary,
                    Margin = new Padding(0, 9, 6, 0)
                };
            }
        }
    }
}
