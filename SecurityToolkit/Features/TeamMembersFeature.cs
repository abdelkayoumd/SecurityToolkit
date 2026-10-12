using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Models;
using SecurityToolkit.UI;

namespace SecurityToolkit.Features
{
    /// <summary>R-02: pick one or more teams and list their members.</summary>
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

        private sealed class TeamMembersView : DataView
        {
            private IReadOnlyList<Guid> _teamIds = new Guid[0];
            private CheckBox _chkDisabled;

            public TeamMembersView(IFeatureHost host) : base(host)
            {
                InfoLabel.Text = "Select one or more teams to list their members.";
            }

            protected override IEnumerable<Control> CreateCommands()
            {
                yield return Command(Theme.GlyphSearch, "Select teams", () => WithGraph(PickTeams), strong: true);
                yield return Command(Theme.GlyphRefresh, "Refresh", () => WithGraph(OnGraphLoaded, refresh: true));
                yield return CommandSeparator();
                yield return Command(Theme.GlyphDownload, "Export to Excel", () => ExportUi.SaveGrid(this, Grid, "TeamMembers.xlsx", "Team members"));
                _chkDisabled = CommandCheck("Include disabled users", OnGraphLoaded);
                yield return _chkDisabled;
            }

            protected override void Reset()
            {
                _teamIds = new Guid[0];
                base.Reset();
                InfoLabel.Text = "Select one or more teams to list their members.";
            }

            private void PickTeams()
            {
                var picked = PickerDialog<TeamInfo>.Pick(this, "Select teams", Graph.Teams, true,
                    "Name=Team", "TypeName=Type", "BusinessUnitName=Business Unit");
                if (picked == null)
                {
                    return;
                }

                _teamIds = picked.Select(t => t.Id).ToList();
                OnGraphLoaded();
            }

            protected override void OnGraphLoaded()
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
