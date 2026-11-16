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
        public FeatureGroup Group => FeatureGroup.UnderstandAccess;
        public int Order => 20;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new ExplainAccessView(host);
        }

        private sealed class Loaded
        {
            public SecurityGraph Graph { get; set; }
            public IReadOnlyList<TableInfo> Tables { get; set; }
        }

        private sealed class ExplainAccessView : MasterDetailView
        {
            private readonly SentenceBar _sentence;
            private readonly Label _who;
            private readonly ComboBox _cmbPrivilege;
            private readonly PickBox _cmbTable;
            private IReadOnlyList<TableInfo> _tables;
            private IReadOnlyList<UserInfo> _users = new UserInfo[0];
            private CheckBox _chkDisabled;

            public ExplainAccessView(IFeatureHost host)
                : base(host, "Users (Ctrl/Shift-click to select several)", "How they get it")
            {
                // "Why can [Ada] [Read] on [Account]?  Yes, at Organization level"
                _sentence = new SentenceBar();
                _sentence.Words("Why can");
                _who = _sentence.Words(Who(_users));
                _cmbPrivilege = _sentence.Choice(SentenceChoices.Privilege());
                _cmbPrivilege.SelectedIndexChanged += (s, e) => Explain();
                _sentence.Words("on");
                _cmbTable = _sentence.Choice(new PickBox { Width = 280, Name = "Table" }); // hundreds of tables: typing narrows the list
                _cmbTable.Picked += (s, e) => Explain();
                _sentence.Words("?");

                // Added last, so it is laid out first and sits above the one-line explanation.
                Controls.Add(_sentence);
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
                yield return Command(Theme.GlyphDownload, "Export to Excel", () => ExportUi.SaveGrid(this, Grid, "ExplainAccess.xlsx", "Access", _sentence.PlainText()));
                _chkDisabled = CommandCheck("Include disabled users", () => { if (IsLoaded) { FillUsers(); } });
                yield return _chkDisabled;
            }

            protected override void Reset()
            {
                _tables = null;
                _users = new UserInfo[0];
                _cmbTable.Clear();
                _who.Text = Who(_users);
                _sentence.Answer(null);
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
                _cmbTable.SetItems(_tables, SentenceChoices.TableText);
                if (current != null)
                {
                    _cmbTable.Show(t => ((TableInfo)t).LogicalName == current);
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
                _who.Text = Who(_users);
                Explain();
            }

            private TableInfo SelectedTable => _cmbTable.Selected<TableInfo>();

            /// <summary>Who the question is about, as the sentence names them: "Ada", "these 3 users".</summary>
            private static string Who(IReadOnlyList<UserInfo> users)
            {
                return users.Count == 0 ? "the selected users" : users.Count == 1 ? users[0].FullName : $"these {users.Count} users";
            }

            private void Explain()
            {
                var table = SelectedTable;
                _sentence.Answer(null);
                if (!IsLoaded || _users.Count == 0 || table == null || _cmbPrivilege.SelectedItem == null)
                {
                    return;
                }

                var users = _users;
                var kind = SentenceChoices.PrivilegeOf(_cmbPrivilege);
                var graph = Graph;

                // Only the roles these users hold matter, so only their privileges are read.
                var roleIds = users.SelectMany(u => AccessMatrixBuilder.RoleIdsOf(graph, u)).Distinct().ToList();
                Host.RunAsync("Checking access...", security => new KeyValuePair<IReadOnlyDictionary<Guid, PrivilegeDepth>, GrantCoverage>(
                    security.GetRoleGrantsFor(roleIds, table.LogicalName, kind), security.GetGrantCoverage(roleIds, table.LogicalName, kind)), result =>
                {
                    var grants = result.Key;
                    var coverage = result.Value;
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
                    InfoLabel.Text = Describe(answers, kind, table.DisplayName, coverage);
                    ShowAnswer(answers);
                });
            }

            /// <summary>The short answer at the end of the question: "Yes, at Organization level", "No", "2 of 3 can".</summary>
            private void ShowAnswer(IReadOnlyList<AccessAnswer> answers)
            {
                var yes = answers.Count(a => a.HasAccess);
                if (answers.Count == 1)
                {
                    _sentence.Answer(yes == 1 ? $"Yes, at {Privileges.Label(answers[0].Best)} level" : "No: no role grants it",
                        yes == 1 ? SentenceTone.Yes : SentenceTone.No);
                    return;
                }

                _sentence.Answer(yes == answers.Count ? $"All {yes} can" : yes == 0 ? $"None of the {answers.Count} can" : $"{yes} of {answers.Count} can",
                    yes == answers.Count ? SentenceTone.Yes : yes == 0 ? SentenceTone.No : SentenceTone.Neutral);
            }

            private static string Describe(IReadOnlyList<AccessAnswer> answers, PrivilegeKind kind, string table, GrantCoverage coverage)
            {
                if (answers.Count == 1)
                {
                    var text = AccessExplainer.Summarize(answers[0], kind, table);
                    if (!answers[0].HasAccess)
                    {
                        // So a "none" can be told from "nothing was read": what the answer was based on.
                        text += " " + AccessExplainer.DescribeCoverage(coverage, kind, table);
                    }

                    return AccessExplainer.NeedsTeamCaveat(answers[0])
                        ? text + " Roles through a team may apply only to records the team owns, depending on its member privilege inheritance."
                        : text;
                }

                var withAccess = answers.Count(a => a.HasAccess);
                return $"{withAccess} of {answers.Count} selected users have a role that grants {Privileges.Label(kind)} on {table}.";
            }

        }
    }
}
