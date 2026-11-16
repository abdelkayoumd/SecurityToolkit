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
    /// Pick two users on the left; the right shows them side by side: business unit, security roles (as each
    /// effectively has them, directly or through a team) and teams, with what only one of them has marked.
    /// </summary>
    internal sealed class CompareUsersFeature : IFeature
    {
        public string Id => "compare-users";
        public string Title => "How do two users differ?";
        public string Glyph => Glyphs.Compare;
        public FeatureGroup Group => FeatureGroup.Compare;
        public int Order => 10;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new CompareUsersView(host);
        }

        private sealed class CompareUsersView : MasterDetailView
        {
            private readonly SentenceBar _sentence;
            private readonly PickBox _pickFirst;
            private readonly PickBox _pickSecond;
            private UserInfo _first;
            private UserInfo _second;
            private CheckBox _chkDifferences;
            private CheckBox _chkDisabled;

            public CompareUsersView(IFeatureHost host)
                : base(host, "Users (Ctrl-click to select two)", "Comparison")
            {
                // "Compare [Ada] with [Bob]  5 differences": the two users can be picked here or Ctrl-clicked on the left.
                _sentence = new SentenceBar();
                _sentence.Words("Compare");
                _pickFirst = _sentence.Choice(new PickBox { Width = 260, Name = "FirstUser" });
                _sentence.Words("with");
                _pickSecond = _sentence.Choice(new PickBox { Width = 260, Name = "SecondUser" });
                _pickFirst.Picked += (s, e) => PickedInSentence();
                _pickSecond.Picked += (s, e) => PickedInSentence();
                Controls.Add(_sentence);
                InfoLabel.Text = "Connect to an environment to get started.";
            }

            /// <summary>Both users chosen in the sentence: select them on the left too, and compare.</summary>
            private void PickedInSentence()
            {
                var first = _pickFirst.Selected<UserInfo>();
                var second = _pickSecond.Selected<UserInfo>();
                if (first == null || second == null)
                {
                    return;
                }

                if (first == second)
                {
                    InfoLabel.Text = "Pick two different users to compare.";
                    _sentence.Answer(null);
                    return;
                }

                _first = first;
                _second = second;
                Master.SelectWhere(row => row == first || row == second);
                ShowComparison();
            }

            protected override string PromptText => "Select two users on the left (Ctrl-click) to compare them.";

            protected override IEnumerable<Control> CreateCommands()
            {
                yield return Command(Theme.GlyphRefresh, "Refresh", () => WithGraph(OnGraphLoaded, refresh: true), strong: true);
                yield return CommandSeparator();
                yield return Command(Theme.GlyphClearSelection, "Clear selection", Master.ClearSelection);
                yield return Command(Theme.GlyphDownload, "Export to Excel", () => ExportUi.SaveGrid(this, Grid, "CompareUsers.xlsx", "Comparison", _sentence.PlainText()));

                // Two users usually share dozens of roles; the gaps are what matter, so hide the rest by default.
                _chkDifferences = CommandCheck("Differences only", ShowComparison, isChecked: true);
                _chkDisabled = CommandCheck("Include disabled users", () => { if (Graph != null) { OnGraphLoaded(); } });
                yield return _chkDifferences;
                yield return _chkDisabled;
            }

            protected override void Reset()
            {
                _first = null;
                _second = null;
                _pickFirst.Clear();
                _pickSecond.Clear();
                _sentence.Answer(null);
                base.Reset();
            }

            protected override void OnGraphLoaded()
            {
                if (Graph == null)
                {
                    return;
                }

                var includeDisabled = _chkDisabled != null && _chkDisabled.Checked;
                var users = Graph.Users.Where(u => includeDisabled || !u.IsDisabled).ToList();
                Master.SetData(users, "FullName=Full Name", "DomainName=User Name", "BusinessUnitName=Business Unit", "Status");
                _pickFirst.SetItems(users, UserText);
                _pickSecond.SetItems(users, UserText);
                if (_first == null)
                {
                    InfoLabel.Text = PromptText;
                }
                else
                {
                    ShowComparison();
                }
            }

            protected override void OnMasterSelectionChanged()
            {
                var picked = Master.GetSelected<UserInfo>();
                if (picked.Count == 0)
                {
                    // Only cleared (by a filter, say): keep showing the last comparison.
                    return;
                }

                if (picked.Count != 2)
                {
                    InfoLabel.Text = picked.Count == 1
                        ? $"{picked[0].FullName} selected. Ctrl-click a second user to compare."
                        : "Select exactly two users to compare.";
                    if (picked.Count == 1)
                    {
                        _pickFirst.Show(u => u == picked[0]);
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
                _pickFirst.Show(u => u == _first);
                _pickSecond.Show(u => u == _second);
                ShowComparison();
            }

            /// <summary>"Ada Lovelace (ada@contoso.com)": the login tells two people with the same name apart.</summary>
            private static string UserText(UserInfo user) => string.IsNullOrEmpty(user.DomainName) ? user.FullName : $"{user.FullName} ({user.DomainName})";

            private void ShowComparison()
            {
                if (Graph == null || _first == null || _second == null)
                {
                    return;
                }

                var all = Graph.CompareUsers(_first, _second);
                // The business unit is always shown, even when it is the same: with nothing else to compare (two users
                // with no roles, say) it is still the one fact worth seeing, and an empty grid looks like a failure.
                var rows = _chkDifferences != null && _chkDifferences.Checked
                    ? all.Where(r => r.IsDifference || r.Category == "Business unit").ToList()
                    : all.ToList();
                var names = SecurityGraph.Labels(_first, _second);

                // The two user columns are headed with the users' own names.
                Grid.SetData(rows, "Category", "Item", "A=" + names.Item1, "B=" + names.Item2, "Result");

                var items = all.Where(r => r.Category != "Business unit").ToList();
                var onlyFirst = items.Count(r => r.Result == "Only " + names.Item1);
                var onlySecond = items.Count(r => r.Result == "Only " + names.Item2);
                var both = items.Count(r => r.Result == "Both");
                var businessUnit = all[0].IsDifference ? $"different business units ({all[0].A} / {all[0].B})" : $"same business unit ({all[0].A})";
                var summary = $"{names.Item1} and {names.Item2}: {businessUnit}; {onlyFirst} only {names.Item1}, {onlySecond} only {names.Item2}, {both} in both";
                var sameRolesAndTeams = !items.Any(r => r.IsDifference);
                InfoLabel.Text = sameRolesAndTeams ? summary + ". No security role or team differs." : summary;

                var differences = all.Count(r => r.IsDifference);
                _sentence.Answer(differences == 0 ? "No difference" : differences == 1 ? "1 difference" : $"{differences} differences",
                    differences == 0 ? SentenceTone.Yes : SentenceTone.Neutral);
            }
        }
    }
}
