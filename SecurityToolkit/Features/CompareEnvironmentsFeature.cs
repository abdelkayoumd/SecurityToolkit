using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Services;
using SecurityToolkit.UI;
using Label = System.Windows.Forms.Label;

namespace SecurityToolkit.Features
{
    /// <summary>
    /// "How do two environments differ?" Dev against Prod: the roles one side has and the other does not, the
    /// privileges that differ for the roles both have, and the team roles that differ. The other environment is a
    /// second XrmToolBox connection. Read-only on both sides.
    /// </summary>
    internal sealed class CompareEnvironmentsFeature : IFeature
    {
        public string Id => "compare-environments";
        public string Title => "How do two environments differ?";
        public string Glyph => Glyphs.Swap;
        public FeatureGroup Group => FeatureGroup.Compare;
        public int Order => 30;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new CompareEnvironmentsView(host);
        }

        private sealed class CompareEnvironmentsView : MasterDetailView
        {
            private readonly SentenceBar _sentence;
            private readonly Label _here;
            private readonly Label _there;
            private IReadOnlyList<EnvironmentCheck> _checks;
            private string _hereName;
            private string _thereName;

            public CompareEnvironmentsView(IFeatureHost host)
                : base(host, "Checks (select to narrow the differences)", "Differences")
            {
                // "Compare Contoso Dev with Contoso Prod  42 differences"
                _sentence = new SentenceBar();
                _sentence.Words("Compare");
                _here = _sentence.Words();
                _sentence.Words("with");
                _there = _sentence.Words();
                Controls.Add(_sentence);
                ShowNames();

                Host.OtherEnvironmentChanged += (s, e) =>
                {
                    _checks = null;
                    ShowNames();
                    if (Host.Security != null)
                    {
                        LoadData();
                    }
                };
                InfoLabel.Text = "Connect to an environment to get started.";
            }

            protected override string PromptText => Host.OtherSecurity == null
                ? "Choose the other environment (a second connection) to compare this one with."
                : "Every difference is shown. Select a check on the left to see only its differences.";

            protected override string EmptyText => Host.OtherSecurity == null
                ? PromptText
                : Master.SelectedCount == 0 ? "No difference: the two environments match on roles, privileges and team roles." : "The selected checks found no difference.";

            protected override bool IsLoaded => _checks != null;

            protected override IEnumerable<Control> CreateCommands()
            {
                yield return Command(Theme.GlyphRefresh, "Refresh", () => Host.EnsureConnection(() =>
                {
                    Host.Security.Invalidate();
                    Host.OtherSecurity?.Invalidate();
                    LoadData();
                }), strong: true);
                yield return Command(Glyphs.Swap, "Choose the other environment", () => Host.EnsureConnection(Host.ChooseOtherEnvironment));
                yield return CommandSeparator();
                yield return Command(Theme.GlyphSelectAll, "Select all checks", Master.SelectAll);
                yield return Command(Theme.GlyphClearSelection, "Clear selection", Master.ClearSelection);
                yield return Command(Theme.GlyphDownload, "Export to Excel", () => ExportUi.SaveGrid(this, Grid, "CompareEnvironments.xlsx", "Differences", _sentence.PlainText()));
            }

            private void ShowNames()
            {
                _hereName = Host.EnvironmentName ?? "this environment";
                _thereName = Host.OtherEnvironmentName ?? "(choose the other environment)";
                if (string.Equals(_hereName, _thereName, StringComparison.CurrentCultureIgnoreCase))
                {
                    _thereName += " (second connection)";
                }

                _here.Text = _hereName;
                _there.Text = _thereName;
                _sentence.Answer(null);
            }

            protected override void Reset()
            {
                _checks = null;
                ShowNames();
                base.Reset();
            }

            protected override void OnGraphLoaded()
            {
                // The lists come from LoadData, which reads both environments.
            }

            protected override void LoadData()
            {
                ShowNames();
                var other = Host.OtherSecurity;
                if (other == null)
                {
                    _checks = null;
                    Master.SetData(new object[0]);
                    Grid.SetData(new object[0]);
                    InfoLabel.Text = PromptText;
                    return;
                }

                var hereName = _hereName;
                var thereName = _thereName;
                Host.RunAsync("Reading both environments...", security => EnvironmentComparer.Compare(Read(security, hereName), Read(other, thereName)), checks =>
                {
                    _checks = checks;
                    Master.SetData(_checks, "Title=Check", "Count=Differences");
                    ShowDifferences(_checks);
                });
            }

            /// <summary>One side of the comparison: roles, teams, what each role grants and the privilege names.</summary>
            private static EnvironmentSecurity Read(SecurityService security, string name)
            {
                var graph = security.GetGraph();
                return new EnvironmentSecurity
                {
                    Name = name,
                    Graph = graph,
                    Catalog = security.GetPrivilegeCatalog(),
                    OtherPrivileges = security.GetOtherPrivileges(),
                    Grants = security.GetRolePrivileges(EnvironmentSecurity.RolesToRead(graph))
                };
            }

            protected override void OnMasterSelectionChanged()
            {
                var picked = Master.GetSelected<EnvironmentCheck>();
                ShowDifferences(picked.Count == 0 ? _checks : picked);
            }

            private void ShowDifferences(IReadOnlyList<EnvironmentCheck> checks)
            {
                if (_checks == null)
                {
                    return;
                }

                var rows = checks.SelectMany(c => c.Rows).ToList();
                Grid.SetData(rows, "Check", "Item", "Detail", "Here=" + _hereName, "There=" + _thereName);
                var total = _checks.Sum(c => c.Count);
                _sentence.Answer(total == 0 ? "No difference" : total == 1 ? "1 difference" : $"{total} differences", total == 0 ? SentenceTone.Yes : SentenceTone.Neutral);
                InfoLabel.Text = checks.Count == 1 && checks.Count != _checks.Count
                    ? checks[0].Description
                    : $"{_checks.Count(c => c.Count > 0)} of {_checks.Count} checks found differences. Roles, teams, tables and privileges are matched by name.";
            }
        }
    }
}
