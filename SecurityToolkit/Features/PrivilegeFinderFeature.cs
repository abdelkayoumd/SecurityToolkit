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
    /// The privilege and the minimum depth are chosen inside the sentence above the lists:
    /// "7 role(s) grant [Delete] on 1 table(s) at [Organization] level".
    /// </summary>
    internal sealed class PrivilegeFinderFeature : IFeature
    {
        public string Id => "privilege-finder";
        public string Title => "Privilege finder";
        public string Glyph => Glyphs.Lock;
        public FeatureGroup Group => FeatureGroup.Explore;
        public int Order => 50;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new PrivilegeFinderView(host);
        }

        private sealed class Choice<T>
        {
            public Choice(T value, string text) { Value = value; Text = text; }
            public T Value { get; }
            public string Text { get; }
            public override string ToString() => Text;
        }

        private sealed class PrivilegeFinderView : MasterDetailView
        {
            private readonly FlowLayoutPanel _sentence;
            private readonly Label _lead;
            private readonly Label _middle;
            private readonly Label _tail;
            private readonly ComboBox _cmbPrivilege;
            private readonly ComboBox _cmbDepth;
            private IReadOnlyList<TableInfo> _tables;
            private IReadOnlyList<TableInfo> _picked = new TableInfo[0];

            public PrivilegeFinderView(IFeatureHost host)
                : base(host, "Tables (Ctrl/Shift-click to select several)", "Roles that grant the privilege")
            {
                _lead = SentenceLabel();
                _middle = SentenceLabel();
                _tail = SentenceLabel();
                _cmbPrivilege = Combo(Enum.GetValues(typeof(PrivilegeKind)).Cast<PrivilegeKind>().Select(k => new Choice<PrivilegeKind>(k, Privileges.Label(k))), 110);
                _cmbDepth = Combo(new[] { PrivilegeDepth.User, PrivilegeDepth.BusinessUnit, PrivilegeDepth.ParentChildBusinessUnits, PrivilegeDepth.Organization }
                    .Select(d => new Choice<PrivilegeDepth>(d, Privileges.Label(d))), 190);

                _sentence = new FlowLayoutPanel
                {
                    Dock = DockStyle.Top,
                    Height = 34,
                    WrapContents = false,
                    AutoScroll = false,
                    Padding = new Padding(10, 0, 0, 0),
                    BackColor = Theme.PageBackground,
                    Visible = false
                };
                _sentence.Controls.Add(_lead);
                _sentence.Controls.Add(_cmbPrivilege);
                _sentence.Controls.Add(_middle);
                _sentence.Controls.Add(_cmbDepth);
                _sentence.Controls.Add(_tail);

                // Added last, so it is laid out first and sits above the (then hidden) one-line message.
                Controls.Add(_sentence);

                _cmbPrivilege.SelectedIndex = 1; // Read
                _cmbDepth.SelectedIndex = 0;     // User or wider = any access
                ShowSentence(null);

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
                yield return Command(Theme.GlyphDownload, "Export to Excel", () => ExportUi.SaveGrid(this, Grid, "RolesWithPrivilege.xlsx", "Roles"));
            }

            private static Label SentenceLabel()
            {
                return new Label
                {
                    AutoSize = true,
                    Font = Theme.FontBold,
                    ForeColor = Theme.TextSecondary,
                    Margin = new Padding(0, 9, 4, 0)
                };
            }

            private ComboBox Combo<T>(IEnumerable<Choice<T>> items, int width)
            {
                var box = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Width = width,
                    Font = Theme.FontRegular,
                    Margin = new Padding(0, 6, 6, 0)
                };
                box.Items.AddRange(items.Cast<object>().ToArray());
                box.SelectedIndexChanged += (s, e) => FindRoles();
                return box;
            }

            /// <summary>
            /// Writes the sentence. With <paramref name="roleCount"/> null (nothing searched yet) it reads as a
            /// question to fill in; otherwise it states the result.
            /// </summary>
            private void ShowSentence(int? roleCount)
            {
                var depth = SelectedDepth;
                _lead.Text = roleCount.HasValue ? $"{roleCount.Value} role(s) grant" : "Roles that grant";
                _middle.Text = _picked.Count == 0 ? "on the selected table(s) at" : $"on {_picked.Count} table(s) at";
                _tail.Text = depth == PrivilegeDepth.Organization ? "level" : "level or wider";
            }

            private PrivilegeDepth SelectedDepth => ((Choice<PrivilegeDepth>)_cmbDepth.SelectedItem).Value;

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
                if (_cmbPrivilege == null || _cmbDepth == null || _cmbPrivilege.SelectedItem == null || _cmbDepth.SelectedItem == null)
                {
                    return;
                }

                ShowSentence(null);
                if (_picked.Count == 0 || !IsLoaded)
                {
                    return;
                }

                var tables = _picked;
                var kind = ((Choice<PrivilegeKind>)_cmbPrivilege.SelectedItem).Value;
                var depth = SelectedDepth;

                Host.RunAsync("Finding roles...", security =>
                {
                    var rows = new List<RolePrivilegeRow>();
                    foreach (var table in tables)
                    {
                        foreach (var row in security.FindRolesWithPrivilege(table.LogicalName, kind, depth))
                        {
                            row.Table = table.DisplayName;
                            rows.Add(row);
                        }
                    }

                    return rows;
                }, rows =>
                {
                    Grid.SetData(rows, "Table", "Role", "BusinessUnit=Business Unit", "Depth");
                    ShowSentence(rows.Count);
                });
            }
        }
    }
}
