using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Services;
using SecurityToolkit.UI;

namespace SecurityToolkit.Features
{
    /// <summary>
    /// Tidy-up report: the checks on the left, each with how many findings it has; the findings on the right. With
    /// no check selected every finding is shown; selecting checks narrows the list. Read-only.
    /// </summary>
    internal sealed class CleanupReportFeature : IFeature
    {
        public string Id => "cleanup-report";
        public string Title => "Cleanup report";
        public string Glyph => Theme.GlyphWarning;
        public FeatureGroup Group => FeatureGroup.Review;
        public int Order => 20;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new CleanupReportView(host);
        }

        private sealed class CleanupReportView : MasterDetailView
        {
            private IReadOnlyList<CleanupCheck> _checks = new CleanupCheck[0];

            public CleanupReportView(IFeatureHost host)
                : base(host, "Checks (select to narrow the findings)", "Findings")
            {
                InfoLabel.Text = "Connect to an environment to get started.";
            }

            protected override string PromptText => "Every finding is shown. Select a check on the left to see only its findings.";

            protected override string EmptyText => Master.SelectedCount == 0 ? "No findings: nothing to report." : "The selected checks found nothing.";

            protected override IEnumerable<Control> CreateCommands()
            {
                yield return Command(Theme.GlyphRefresh, "Refresh", () => WithGraph(OnGraphLoaded, refresh: true), strong: true);
                yield return CommandSeparator();
                yield return Command(Theme.GlyphSelectAll, "Select all checks", Master.SelectAll);
                yield return Command(Theme.GlyphClearSelection, "Clear selection", Master.ClearSelection);
                yield return CommandSeparator();
                yield return Command(Theme.GlyphDownload, "Export to Excel", () => ExportUi.SaveGrid(this, Grid, "CleanupReport.xlsx", "Cleanup report"));
                yield return CommandSeparator();
                yield return Command(Glyphs.Remove, "Remove disabled users from teams", () => Act(CleanupChecks.DisabledInTeams));
                yield return Command(Glyphs.Remove, "Remove roles from disabled users", () => Act(CleanupChecks.DisabledWithRoles));
            }

            /// <summary>
            /// Acts on the findings of one check: the rows selected on the right if any of them belong to it, otherwise
            /// all of its findings. Nothing is sent until the user approves the review.
            /// </summary>
            private void Act(string check)
            {
                Host.EnsureConnection(() =>
                {
                    if (Graph == null)
                    {
                        Host.CreateChangeUi(this).Tell("Wait for the report to load first.");
                        return;
                    }

                    var chosen = Grid.GetSelected<CleanupRow>().Where(r => r.Check == check).ToList();
                    var findings = chosen.Count > 0 ? chosen : _checks.Where(c => c.Title == check).SelectMany(c => c.Rows).ToList();
                    var plan = ChangePlanner.PlanCleanup(Graph, findings);
                    new PlanRunner(Host, Host.CreateChangeUi(this)).Run(plan, () => WithGraph(OnGraphLoaded, refresh: true));
                });
            }

            protected override void Reset()
            {
                _checks = new CleanupCheck[0];
                base.Reset();
            }

            protected override void OnGraphLoaded()
            {
                if (Graph == null)
                {
                    return;
                }

                _checks = CleanupChecks.Run(Graph);
                Master.SetData(_checks, "Title=Check", "Count=Findings");
                ShowFindings(_checks);
            }

            protected override void OnMasterSelectionChanged()
            {
                // An empty selection means "no narrowing": show everything again.
                var picked = Master.GetSelected<CleanupCheck>();
                ShowFindings(picked.Count == 0 ? _checks : picked);
            }

            private void ShowFindings(IReadOnlyList<CleanupCheck> checks)
            {
                var rows = checks.SelectMany(c => c.Rows).ToList();
                Grid.SetData(rows, "Check", "Item", "Detail", "BusinessUnit=Business Unit");

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
