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
    /// R-05: pick one or more tables on the left; the roles that grant a privilege on them appear on the right.
    /// The privileges (one, several or all) and the minimum depth are chosen inside the sentence above the lists:
    /// "7 role(s) grant [Delete] on 1 table(s) at [Organization] level". With several privileges the result has
    /// one depth column per privilege, and a role is listed when it grants at least one of them.
    /// </summary>
    internal sealed class PrivilegeFinderFeature : IFeature
    {
        public string Id => "privilege-finder";
        public string Title => "Privilege finder";
        public string Glyph => Glyphs.Lock;
        public FeatureGroup Group => FeatureGroup.UnderstandAccess;
        public int Order => 30;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new PrivilegeFinderView(host);
        }

        private sealed class PrivilegeFinderView : MasterDetailView
        {
            private readonly SentenceBar _sentence;
            private readonly Label _lead;
            private readonly Label _middle;
            private readonly Label _tail;
            private readonly CheckedDropDown _privileges;
            private readonly ComboBox _cmbDepth;
            private IReadOnlyList<TableInfo> _tables;
            private IReadOnlyList<TableInfo> _picked = new TableInfo[0];

            public PrivilegeFinderView(IFeatureHost host)
                : base(host, "Tables (Ctrl/Shift-click to select several)", "Roles that grant the privilege")
            {
                // "7 role(s) grant [Delete] on 1 table(s) at [Organization] level"
                _sentence = new SentenceBar { Visible = false };
                _lead = _sentence.Words();
                _privileges = _sentence.Choice(SentenceChoices.PrivilegeList());
                _privileges.CheckedChanged += (s, e) => FindRoles();
                _middle = _sentence.Words();
                _cmbDepth = _sentence.Choice(SentenceChoices.Depth()); // starts at User or wider = any access
                _cmbDepth.SelectedIndexChanged += (s, e) => FindRoles();
                _tail = _sentence.Words();

                // Added last, so it is laid out first and sits above the (then hidden) one-line message.
                Controls.Add(_sentence);
                ShowSentence(null);

                // Depths are drawn as in the Dynamics 365 security role editor: an icon and the depth in words.
                Grid.BodyCellRenderer = DepthCell.Paint;
                InfoLabel.Text = "Connect to an environment to get started.";
            }

            protected override string PromptText => "Select one or more tables on the left to see which roles grant the privilege.";

            // This feature lists tables, not the security graph, so it has its own notion of "loaded".
            protected override bool IsLoaded => _tables != null;

            protected override void LoadData()
            {
                Host.RunAsync("Loading tables...", security => security.GetTables(), tables =>
                {
                    _tables = tables;
                    FillTables();
                });
            }

            protected override IEnumerable<Control> CreateCommands()
            {
                yield return Command(Theme.GlyphRefresh, "Refresh", ReloadTables, strong: true);
                yield return CommandSeparator();
                yield return Command(Theme.GlyphDownload, "Export to Excel", () => ExportUi.SaveGrid(this, Grid, "RolesWithPrivilege.xlsx", "Roles", SentenceText()));
            }

            /// <summary>
            /// Writes the sentence. With <paramref name="roleCount"/> null (nothing searched yet) it reads as a
            /// question to fill in; otherwise it states the result.
            /// </summary>
            private void ShowSentence(int? roleCount)
            {
                var depth = SelectedDepth;
                var any = _privileges.CheckedIndices.Count > 1 ? " any of" : string.Empty;
                _lead.Text = (roleCount.HasValue ? $"{roleCount.Value} role(s) grant" : "Roles that grant") + any;
                _middle.Text = _picked.Count == 0 ? "on the selected table(s) at" : $"on {_picked.Count} table(s) at";
                _tail.Text = depth == PrivilegeDepth.Organization ? "level" : "level or wider";
            }

            private PrivilegeDepth SelectedDepth => SentenceChoices.DepthOf(_cmbDepth);

            private IReadOnlyList<PrivilegeKind> SelectedKinds => SentenceChoices.PrivilegesOf(_privileges);

            /// <summary>The sentence as plain text, with every chosen privilege named: what an export says it shows.</summary>
            private string SentenceText()
            {
                var kinds = _privileges.AllChecked ? "all privileges" : string.Join(", ", SelectedKinds.Select(Privileges.Label));
                return $"{_lead.Text} {kinds} {_middle.Text} {_cmbDepth.Text} {_tail.Text}".Replace(" any of all privileges", " any privilege");
            }

            protected override void Reset()
            {
                _tables = null;
                _picked = new TableInfo[0];
                base.Reset();
                _sentence.Visible = false;
                InfoLabel.Visible = true;
                ShowSentence(null);
            }

            protected override void OnGraphLoaded()
            {
                // Nothing to do: the left list comes from LoadData, not from the security graph.
            }

            private void ReloadTables()
            {
                Host.EnsureConnection(() =>
                {
                    Host.Security.Invalidate();
                    LoadData();
                });
            }

            private void FillTables()
            {
                Master.SetData(_tables, "DisplayName=Table", "LogicalName=Logical name");

                // Once there is data, the sentence replaces the one-line message.
                InfoLabel.Visible = false;
                _sentence.Visible = true;
                if (_picked.Count == 0)
                {
                    ShowSentence(null);
                }
                else
                {
                    FindRoles();
                }
            }

            protected override void OnMasterSelectionChanged()
            {
                var picked = Master.GetSelected<TableInfo>();
                if (picked.Count == 0)
                {
                    // Only cleared (by a filter, say): keep showing the last choice.
                    return;
                }

                _picked = picked;
                FindRoles();
            }

            private void FindRoles()
            {
                // The combos exist from the start; they raise this while the view is still being built.
                if (_privileges == null || _cmbDepth == null || _cmbDepth.SelectedItem == null)
                {
                    return;
                }

                ShowSentence(null);
                if (_picked.Count == 0 || !IsLoaded)
                {
                    return;
                }

                var tables = _picked;
                var kinds = SelectedKinds;
                var depth = SelectedDepth;

                Host.RunAsync("Finding roles...", security =>
                {
                    var rows = new List<RolePrivilegeRow>();
                    foreach (var table in tables)
                    {
                        foreach (var row in security.FindRolesWithPrivileges(table.LogicalName, kinds, depth))
                        {
                            row.Table = table.DisplayName;
                            rows.Add(row);
                        }
                    }

                    return rows;
                }, rows =>
                {
                    if (kinds.Count == 1)
                    {
                        Grid.SetData(rows, "Table", "Role", "BusinessUnit=Business Unit", "Depth");
                        Grid.SetColumnMinimumWidth("Depth", 200);
                    }
                    else
                    {
                        // One depth column per chosen privilege, like a row of the security role editor.
                        var columns = new List<string> { "Table", "Role", "BusinessUnit=Business Unit" };
                        columns.AddRange(kinds.Select(k => k + "=" + Privileges.Label(k)));
                        Grid.SetData(rows, columns.ToArray());
                        foreach (var kind in kinds)
                        {
                            Grid.SetColumnMinimumWidth(kind.ToString(), kinds.Count > 3 ? 130 : 200);
                        }
                    }

                    ShowSentence(rows.Count);
                });
            }
        }
    }
}
