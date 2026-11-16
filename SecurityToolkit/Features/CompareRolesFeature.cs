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
        public string Title => "How do two roles differ?";
        public string Glyph => Glyphs.CompareRoles;
        public FeatureGroup Group => FeatureGroup.Compare;
        public int Order => 20;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new CompareRolesView(host);
        }

        private sealed class Loaded
        {
            public IReadOnlyList<TableInfo> Tables { get; set; }
            public IReadOnlyDictionary<Guid, IReadOnlyList<PrivilegeTarget>> Catalog { get; set; }
            public IReadOnlyDictionary<Guid, IReadOnlyList<RoleGrant>> Grants { get; set; }
        }

        private sealed class CompareRolesView : MasterDetailView
        {
            private readonly SentenceBar _sentence;
            private readonly PickBox _pickFirst;
            private readonly PickBox _pickSecond;
            private RoleRow _first;
            private RoleRow _second;
            private IReadOnlyList<RoleDiffRow> _all = new RoleDiffRow[0];
            private CheckBox _chkDifferences;

            public CompareRolesView(IFeatureHost host)
                : base(host, "Roles (Ctrl-click to select two)", "Comparison")
            {
                // "Compare [Salesperson] with [Sales Manager]  12 differences": pick here or Ctrl-click on the left.
                _sentence = new SentenceBar();
                _sentence.Words("Compare");
                _pickFirst = _sentence.Choice(new PickBox { Width = 260, Name = "FirstRole" });
                _sentence.Words("with");
                _pickSecond = _sentence.Choice(new PickBox { Width = 260, Name = "SecondRole" });
                _pickFirst.Picked += (s, e) => PickedInSentence();
                _pickSecond.Picked += (s, e) => PickedInSentence();
                Controls.Add(_sentence);

                // Depths are drawn as in the Dynamics 365 security role editor: an icon and the depth in words.
                Grid.BodyCellRenderer = DepthCell.Paint;
                InfoLabel.Text = "Connect to an environment to get started.";
            }

            protected override string PromptText => "Select two roles on the left (Ctrl-click) to compare what they grant.";

            protected override int MasterWidthPercent => 24;

            protected override IEnumerable<Control> CreateCommands()
            {
                yield return Command(Theme.GlyphRefresh, "Refresh", Reload, strong: true);
                yield return CommandSeparator();
                yield return Command(Theme.GlyphClearSelection, "Clear selection", Master.ClearSelection);
                yield return Command(Theme.GlyphDownload, "Export to Excel", () => ExportUi.SaveGrid(this, Grid, "CompareRoles.xlsx", "Comparison", _sentence.PlainText()));

                // Two roles usually share most of their privileges; the gaps are what matter.
                _chkDifferences = CommandCheck("Differences only", ShowRows, isChecked: true);
                yield return _chkDifferences;
            }

            protected override void Reset()
            {
                _first = null;
                _second = null;
                _all = new RoleDiffRow[0];
                _pickFirst.Clear();
                _pickSecond.Clear();
                _sentence.Answer(null);
                base.Reset();
            }

            /// <summary>Both roles chosen in the sentence: select them on the left too, and compare.</summary>
            private void PickedInSentence()
            {
                var first = _pickFirst.Selected<RoleRow>();
                var second = _pickSecond.Selected<RoleRow>();
                if (first == null || second == null)
                {
                    return;
                }

                if (first == second)
                {
                    InfoLabel.Text = "Pick two different roles to compare.";
                    _sentence.Answer(null);
                    return;
                }

                _first = first;
                _second = second;
                Master.SelectWhere(row => row == first || row == second);
                Compare();
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

                var roles = RoleComparer.RolesToPick(Graph);
                Master.SetData(roles, "Name", "Type");
                _pickFirst.SetItems(roles, r => r.Name);
                _pickSecond.SetItems(roles, r => r.Name);
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
                    if (picked.Count == 1)
                    {
                        _pickFirst.Show(r => r == picked[0]);
                    }

                    return;
                }

                // Already compared (the two were picked in the sentence): keep their order.
                if (picked.Contains(_first) && picked.Contains(_second))
                {
                    return;
                }

                _first = picked[0];
                _second = picked[1];
                _pickFirst.Show(r => r == _first);
                _pickSecond.Show(r => r == _second);
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
                Grid.SetColumnMinimumWidth("A", 200);
                Grid.SetColumnMinimumWidth("B", 200);
                Grid.SetColumnWeight("A", 170);
                Grid.SetColumnWeight("B", 170);
                Grid.SetColumnWeight("Result", 170);

                InfoLabel.Text = RoleComparer.Summarize(_all, _first, _second);
                var differences = _all.Count(r => r.IsDifference);
                _sentence.Answer(differences == 0 ? "No difference" : differences == 1 ? "1 difference" : $"{differences} differences",
                    differences == 0 ? SentenceTone.Yes : SentenceTone.Neutral);
            }
        }
    }
}
