using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Models;
using SecurityToolkit.UI;

namespace SecurityToolkit.Features
{
    /// <summary>R-02: pick one or more teams in the list on the left; their members appear on the right.</summary>
    internal sealed class TeamMembersFeature : IFeature
    {
        public string Id => "team-members";
        public string Title => "Team members";
        public string Glyph => Glyphs.Group;
        public int Order => 20;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new TeamMembersView(host);
        }

        private sealed class TeamMembersView : MasterDetailView
        {
            private IReadOnlyList<Guid> _teamIds = new Guid[0];
            private CheckBox _chkDisabled;

            public TeamMembersView(IFeatureHost host) : base(host, "Teams (Ctrl/Shift-click to select several)", "Members")
            {
                InfoLabel.Text = "Connect to an environment to get started.";
            }

            protected override string PromptText => "Select one or more teams on the left to see their members.";

            protected override IEnumerable<Control> CreateCommands()
            {
                yield return Command(Theme.GlyphRefresh, "Refresh", () => WithGraph(OnGraphLoaded, refresh: true), strong: true);
                yield return CommandSeparator();
                yield return Command(Theme.GlyphSelectAll, "Select all teams", Master.SelectAll);
                yield return Command(Theme.GlyphClearSelection, "Clear selection", Master.ClearSelection);
                yield return CommandSeparator();
                yield return Command(Theme.GlyphDownload, "Export to Excel", () => ExportUi.SaveGrid(this, Grid, "TeamMembers.xlsx", "Team members"));
                _chkDisabled = CommandCheck("Include disabled users", ShowMembers);
                yield return _chkDisabled;
            }

            protected override void Reset()
            {
                _teamIds = new Guid[0];
                base.Reset();
            }

            protected override void OnGraphLoaded()
            {
                if (Graph == null)
                {
                    return;
                }

                Master.SetData(Graph.Teams, "Name=Team", "TypeName=Type", "BusinessUnitName=Business Unit");
                if (_teamIds.Count == 0)
                {
                    InfoLabel.Text = PromptText;
                }
                else
                {
                    ShowMembers();
                }
            }

            protected override void OnMasterSelectionChanged()
            {
                var picked = Master.GetSelected<TeamInfo>();
                if (picked.Count == 0)
                {
                    // Only cleared (by a filter, say): keep showing the last choice.
                    return;
                }

                _teamIds = picked.Select(t => t.Id).ToList();
                ShowMembers();
            }

            private void ShowMembers()
            {
                if (Graph == null || _teamIds.Count == 0)
                {
                    return;
                }

                var rows = Graph.TeamMembers(_teamIds, _chkDisabled != null && _chkDisabled.Checked);
                Grid.SetData(rows, "Team", "TeamType=Type", "User", "Domain", "BusinessUnit=Business Unit", "Status");
                InfoLabel.Text = $"{rows.Count} member(s) in {_teamIds.Count} team(s)";
            }
        }
    }
}
