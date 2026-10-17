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
    /// Pick two roles on the left; the right lists, table by table and privilege by privilege, where they differ:
    /// what only one of them grants, and where one reaches further. Read-only.
    /// </summary>
    internal sealed class CompareRolesFeature : IFeature
    {
        public string Id => "compare-roles";
        public string Title => "Compare roles";
        public string Glyph => Glyphs.Compare;
        public FeatureGroup Group => FeatureGroup.Explore;
        public int Order => 26;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new CompareRolesView(host);
        }

        private sealed class Loaded
        {
            public IReadOnlyList<TableInfo> Tables { get; set; }
            public IReadOnlyDictionary<Guid, PrivilegeTarget> Catalog { get; set; }
            public IReadOnlyDictionary<Guid, IReadOnlyList<RoleGrant>> Grants { get; set; }
        }

        private sealed class CompareRolesView : MasterDetailView
        {
            private RoleRow _first;
            private RoleRow _second;
            private IReadOnlyList<RoleDiffRow> _all = new RoleDiffRow[0];
            private CheckBox _chkDifferences;

            public CompareRolesView(IFeatureHost host)
                : base(host, "Roles (Ctrl-click to select two)", "Comparison")
            {
                InfoLabel.Text = "Connect to an environment to get started.";
            }

            protected override string PromptText => "Select two roles on the left (Ctrl-click) to compare what they grant.";

            protected override int MasterWidthPercent => 24;

            protected override IEnumerable<Control> CreateCommands()
            {
                yield return Command(Theme.GlyphRefresh, "Refresh", Reload, strong: true);
                yield return CommandSeparator();
                yield return Command(Theme.GlyphClearSelection, "Clear selection", Master.ClearSelection);
                yield return Command(Theme.GlyphDownload, "Export to Excel", () => ExportUi.SaveGrid(this, Grid, "CompareRoles.xlsx", "Comparison"));

                // Two roles usually share most of their privileges; the gaps are what matter.
                _chkDifferences = CommandCheck("Differences only", ShowRows, isChecked: true);
                yield return _chkDifferences;
            }

            protected override void Reset()
            {
                _first = null;
                _second = null;
                _all = new RoleDiffRow[0];
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
                if (Graph == null)
                {
                    return;
                }

                Master.SetData(RoleComparer.RolesToPick(Graph), "Name", "Type");
                if (_first == null)
                {
                    InfoLabel.Text = PromptText;
                }
                else
                {
                    Compare();
                }
            }

            protected override void OnMasterSelectionChanged()
            {
                var picked = Master.GetSelected<RoleRow>();
                if (picked.Count == 0)
                {
                    // Only cleared (by a filter, say): keep showing the last comparison.
                    return;
                }

                if (picked.Count != 2)
                {
                    InfoLabel.Text = picked.Count == 1
                        ? $"{picked[0].Name} selected. Ctrl-click a second role to compare."
                        : "Select exactly two roles to compare.";
                    return;
                }

                _first = picked[0];
                _second = picked[1];
                Compare();
            }

            private void Compare()
            {
                if (Graph == null || _first == null || _second == null)
                {
                    return;
                }

                var a = _first;
                var b = _second;
                Host.RunAsync("Reading what the roles grant...", security => new Loaded
                {
                    Tables = security.GetTables(),
                    Catalog = security.GetPrivilegeCatalog(),
                    Grants = security.GetRolePrivileges(new[] { a.RoleId, b.RoleId })
                }, loaded =>
                {
                    _all = RoleComparer.Compare(a, b, loaded.Tables, loaded.Catalog, loaded.Grants[a.RoleId], loaded.Grants[b.RoleId]);
                    ShowRows();
                });
            }

            private void ShowRows()
            {
                if (_first == null || _second == null)
                {
                    return;
                }

                var rows = _chkDifferences != null && _chkDifferences.Checked ? _all.Where(r => r.IsDifference).ToList() : _all.ToList();
                var names = RoleComparer.Labels(_first, _second);

                // The two role columns are headed with the roles' own names.
                Grid.SetData(rows, "Table", "Privilege", "A=" + names.Item1, "B=" + names.Item2, "Result");
                Grid.SetColumnWeight("Table", 140);
                Grid.SetColumnWeight("Privilege", 90);
                Grid.SetColumnWeight("A", 170);
                Grid.SetColumnWeight("B", 170);
                Grid.SetColumnWeight("Result", 170);

                InfoLabel.Text = RoleComparer.Summarize(_all, _first, _second);
            }
        }
    }
}
