using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using SecurityToolkit.UI;

namespace SecurityToolkit.Features
{
    /// <summary>
    /// R-05: pick one or more tables on the left, a privilege and a minimum depth on the command bar; the security
    /// roles that grant it (at that depth or wider) appear on the right.
    /// </summary>
    internal sealed class PrivilegeFinderFeature : IFeature
    {
        public string Id => "privilege-finder";
        public string Title => "Privilege finder";
        public string Glyph => Glyphs.Lock;
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
            private IReadOnlyList<TableInfo> _tables;
            private IReadOnlyList<TableInfo> _picked = new TableInfo[0];
            private ComboBox _cmbPrivilege;
            private ComboBox _cmbDepth;

            public PrivilegeFinderView(IFeatureHost host)
                : base(host, "Tables (Ctrl/Shift-click to select several)", "Roles that grant the privilege")
            {
                InfoLabel.Text = "Connect to an environment to get started.";
            }

            protected override string PromptText => "Select one or more tables on the left, then a privilege, to see which roles grant it.";

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

                _cmbPrivilege = Combo(Enum.GetValues(typeof(PrivilegeKind)).Cast<PrivilegeKind>().Select(k => new Choice<PrivilegeKind>(k, Privileges.Label(k))), 110);
                _cmbPrivilege.SelectedIndex = 1; // Read
                _cmbDepth = Combo(new[] { PrivilegeDepth.User, PrivilegeDepth.BusinessUnit, PrivilegeDepth.ParentChildBusinessUnits, PrivilegeDepth.Organization }
                    .Select(d => new Choice<PrivilegeDepth>(d, Privileges.Label(d))), 190);
                _cmbDepth.SelectedIndex = 0; // User or wider = any access

                yield return CommandLabel("Privilege");
                yield return _cmbPrivilege;
                yield return CommandLabel("At least");
                yield return _cmbDepth;
                yield return CommandSeparator();
                yield return Command(Theme.GlyphDownload, "Export to Excel", () => ExportUi.SaveGrid(this, Grid, "RolesWithPrivilege.xlsx", "Roles"));
            }

            private ComboBox Combo<T>(IEnumerable<Choice<T>> items, int width)
            {
                var box = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Width = width,
                    Font = Theme.FontRegular,
                    Margin = new Padding(2, 9, 6, 0)
                };
                box.Items.AddRange(items.Cast<object>().ToArray());
                box.SelectedIndexChanged += (s, e) => FindRoles();
                return box;
            }

            protected override void Reset()
            {
                _tables = null;
                _picked = new TableInfo[0];
                base.Reset();
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
                if (_picked.Count == 0)
                {
                    InfoLabel.Text = PromptText;
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
                if (_picked.Count == 0 || _cmbPrivilege == null || _cmbDepth == null || _cmbPrivilege.SelectedItem == null || _cmbDepth.SelectedItem == null)
                {
                    return;
                }

                var tables = _picked;
                var kind = ((Choice<PrivilegeKind>)_cmbPrivilege.SelectedItem).Value;
                var depth = ((Choice<PrivilegeDepth>)_cmbDepth.SelectedItem).Value;

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
                    var wider = depth == PrivilegeDepth.Organization ? string.Empty : " or wider";
                    InfoLabel.Text = $"{rows.Count} role(s) grant {Privileges.Label(kind)} on {tables.Count} table(s) at {Privileges.Label(depth)} level{wider}";
                });
            }
        }
    }
}
