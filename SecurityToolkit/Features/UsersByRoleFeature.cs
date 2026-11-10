using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using SecurityToolkit.UI;

namespace SecurityToolkit.Features
{
    /// <summary>R-03: pick one or more security roles on the left; everyone who has them, directly or through a team, appears on the right.</summary>
    internal sealed class UsersByRoleFeature : IFeature
    {
        public string Id => "users-by-role";
        public string Title => "Role holders";
        public string Glyph => Glyphs.Permissions;
        public FeatureGroup Group => FeatureGroup.LookUp;
        public int Order => 30;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new UsersByRoleView(host);
        }

        private sealed class UsersByRoleView : MasterDetailView
        {
            private IReadOnlyList<RoleChoice> _roles = new RoleChoice[0];
            private CheckBox _chkDisabled;
            private readonly TabStrip _tabs;
            private readonly FilterableGrid _teamsGrid;

            public UsersByRoleView(IFeatureHost host)
                : base(host, "Security roles (Ctrl/Shift-click to select several)", "Users who have the role")
            {
                // Two answers about a role: which users have it, and which teams hold it. Tabs, with the counts in their titles.
                _tabs = new TabStrip { Name = "RoleHolderTabs" };
                _tabs.AddTab(UsersTab);
                _tabs.AddTab(TeamsTab);
                _teamsGrid = new FilterableGrid { Visible = false, EmptyMessage = "No team holds the selected roles." };

                var side = Grid.Parent;
                var heading = side.Controls.OfType<Label>().First();
                heading.Visible = false; // the tabs replace it
                side.Controls.Remove(Grid);
                var results = new Panel { Dock = DockStyle.Fill };
                results.Controls.Add(Grid);
                results.Controls.Add(_teamsGrid);
                side.Controls.Add(results);
                side.Controls.Add(_tabs); // the last control added is laid out first, so the tabs sit above the lists
                _tabs.SelectedIndexChanged += (s, e) =>
                {
                    Grid.Visible = _tabs.SelectedIndex == 0;
                    _teamsGrid.Visible = _tabs.SelectedIndex == 1;
                };
                InfoLabel.Text = "Connect to an environment to get started.";
            }

            protected override string PromptText => "Select one or more security roles on the left to see who has them.";

            private const string UsersTab = "Users who have the role";
            private const string TeamsTab = "Teams who have the role";

            protected override IEnumerable<Control> CreateCommands()
            {
                yield return Command(Theme.GlyphRefresh, "Refresh", () => WithGraph(OnGraphLoaded, refresh: true), strong: true);
                yield return CommandSeparator();
                yield return Command(Theme.GlyphSelectAll, "Select all roles", Master.SelectAll);
                yield return Command(Theme.GlyphClearSelection, "Clear selection", Master.ClearSelection);
                yield return CommandSeparator();
                yield return Command(Theme.GlyphDownload, "Export to Excel", () => ExportUi.SaveGrid(this, _tabs.SelectedIndex == 0 ? Grid : _teamsGrid, _tabs.SelectedIndex == 0 ? "UsersByRole.xlsx" : "TeamsByRole.xlsx", _tabs.SelectedIndex == 0 ? "Users by role" : "Teams by role"));
                _chkDisabled = CommandCheck("Include disabled users", ShowUsers);
                yield return _chkDisabled;
            }

            protected override void Reset()
            {
                _roles = new RoleChoice[0];
                _tabs.SetText(0, UsersTab);
                _tabs.SetText(1, TeamsTab);
                _teamsGrid.SetData(new object[0]);
                base.Reset();
            }

            protected override void OnGraphLoaded()
            {
                if (Graph == null)
                {
                    return;
                }

                Master.SetData(Graph.RoleChoices(), "Name=Role");
                if (_roles.Count == 0)
                {
                    InfoLabel.Text = PromptText;
                }
                else
                {
                    ShowUsers();
                }
            }

            protected override void OnMasterSelectionChanged()
            {
                var picked = Master.GetSelected<RoleChoice>();
                if (picked.Count == 0)
                {
                    // Only cleared (by a filter, say): keep showing the last choice.
                    return;
                }

                _roles = picked;
                ShowUsers();
            }

            private void ShowUsers()
            {
                if (Graph == null || _roles.Count == 0)
                {
                    return;
                }

                var includeDisabled = _chkDisabled != null && _chkDisabled.Checked;
                var rows = _roles.SelectMany(role => Graph.UsersInRole(role.RootKey, includeDisabled)).ToList();
                Grid.SetData(rows, "Role", "User", "Domain", "BusinessUnit=Business Unit", "Assignment", "Team", "Status");

                var teamRows = _roles.SelectMany(role => Graph.TeamsInRole(role.RootKey)).ToList();
                _teamsGrid.SetData(teamRows, "Role", "Team", "TeamType=Team type", "BusinessUnit=Business Unit", "Members");

                var users = rows.Select(r => r.User + "|" + r.Domain).Distinct().Count();
                var direct = rows.Count(r => r.Assignment == "Direct");
                InfoLabel.Text = $"{_roles.Count} role(s): {users} user(s), {direct} direct assignment(s), {rows.Count - direct} through a team; held by {teamRows.Count} team(s)";
                _tabs.SetText(0, $"{UsersTab} ({users})");
                _tabs.SetText(1, $"{TeamsTab} ({teamRows.Count})");
            }
        }
    }
}
