using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Services;
using SecurityToolkit.UI;

namespace SecurityToolkit.Features
{
    /// <summary>
    /// Security review: which roles can delete across the whole organization, and who holds them. Checks on the left
    /// with their counts, findings on the right; with no check selected every finding is shown. Read-only.
    /// </summary>
    internal sealed class PrivilegedUsersFeature : IFeature
    {
        public string Id => "privileged-users";
        public string Title => "Privileged users";
        public string Glyph => Glyphs.Shield;
        public FeatureGroup Group => FeatureGroup.Explore;
        public int Order => 55;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new PrivilegedUsersView(host);
        }

        private sealed class PrivilegedUsersView : MasterDetailView
        {
            private IReadOnlyList<PrivilegedCheck> _checks = new PrivilegedCheck[0];
            private OrgDeleteReach _reach;

            public PrivilegedUsersView(IFeatureHost host)
                : base(host, "Checks (select to narrow the findings)", "Findings")
            {
                InfoLabel.Text = "Connect to an environment to get started.";
            }

            protected override int MasterWidthPercent => 26;

            protected override string PromptText => "Every finding is shown. Select a check on the left to see only its findings.";

            protected override string EmptyText => Master.SelectedCount == 0 ? "No findings: nothing to report." : "The selected checks found nothing.";

            protected override bool IsLoaded => Graph != null && _reach != null;

            protected override IEnumerable<Control> CreateCommands()
            {
                yield return Command(Theme.GlyphRefresh, "Refresh", Reload, strong: true);
                yield return CommandSeparator();
                yield return Command(Theme.GlyphSelectAll, "Select all checks", Master.SelectAll);
                yield return Command(Theme.GlyphClearSelection, "Clear selection", Master.ClearSelection);
                yield return CommandSeparator();
                yield return Command(Theme.GlyphDownload, "Export to Excel", () => ExportUi.SaveGrid(this, Grid, "PrivilegedUsers.xlsx", "Privileged users"));
            }

            protected override void Reset()
            {
                _checks = new PrivilegedCheck[0];
                _reach = null;
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

            protected override void LoadData()
            {
                Host.RunAsync("Measuring what each role can delete...", security =>
                {
                    var graph = security.GetGraph();
                    var reach = security.GetOrganizationDeleteReach(graph.Roles.Select(r => r.RootKey).Distinct().ToList());
                    return new KeyValuePair<SecurityGraph, OrgDeleteReach>(graph, reach);
                }, loaded =>
                {
                    Graph = loaded.Key;
                    _reach = loaded.Value;
                    OnGraphLoaded();
                });
            }

            protected override void OnGraphLoaded()
            {
                if (Graph == null || _reach == null)
                {
                    return;
                }

                _checks = PrivilegedUsers.Run(Graph, _reach);
                Master.SetData(_checks, "Title=Check", "Count=Findings");
                ShowFindings(_checks);
            }

            protected override void OnMasterSelectionChanged()
            {
                var picked = Master.GetSelected<PrivilegedCheck>();
                ShowFindings(picked.Count == 0 ? _checks : picked);
            }

            private void ShowFindings(IReadOnlyList<PrivilegedCheck> checks)
            {
                var rows = checks.SelectMany(c => c.Rows).ToList();
                Grid.SetData(rows, "Check", "Subject=User or role", "Detail=Roles", "BusinessUnit=Business Unit", "Status");
                Grid.SetColumnWeight("Check", 150);
                Grid.SetColumnWeight("Subject", 110);
                Grid.SetColumnWeight("Detail", 300);
                Grid.SetColumnWeight("BusinessUnit", 90);
                Grid.SetColumnWeight("Status", 90);

                if (checks.Count == 1 && checks.Count != _checks.Count)
                {
                    InfoLabel.Text = checks[0].Count == 0 ? $"{checks[0].Title}: nothing found. {checks[0].Description}" : checks[0].Description;
                }
                else if (checks.Count == _checks.Count)
                {
                    InfoLabel.Text = $"{rows.Count} finding(s) across {_checks.Count} checks. {PromptText}";
                }
                else
                {
                    InfoLabel.Text = $"{rows.Count} finding(s) in {checks.Count} selected checks.";
                }
            }
        }
    }
}
