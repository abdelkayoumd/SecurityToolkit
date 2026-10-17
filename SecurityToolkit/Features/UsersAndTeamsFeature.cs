using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Models;
using SecurityToolkit.UI;

namespace SecurityToolkit.Features
{
    /// <summary>
    /// R-01: the former teams_listing tool. Users on the left; for the selected ones, a User → Team → Role tree
    /// on the right. Everything comes from the cached graph, so showing teams needs no further queries.
    /// </summary>
    internal sealed class UsersAndTeamsFeature : IFeature
    {
        public string Id => "users-and-teams";
        public string Title => "Users and teams";
        public string Glyph => Glyphs.People;
        public FeatureGroup Group => FeatureGroup.Explore;
        public int Order => 10;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new UsersAndTeamsView(host);
        }

        private sealed class UsersAndTeamsView : DataView
        {
            private readonly ImageList _icons = TreeIcons.Create();
            private readonly TreeView _tree;
            private readonly Timer _selectionTimer;
            private IReadOnlyList<UserInfo> _shownUsers = new UserInfo[0];
            private CheckBox _chkDisabled;

            public UsersAndTeamsView(IFeatureHost host) : base(host)
            {
                _tree = new TreeView
                {
                    Dock = DockStyle.Fill,
                    BorderStyle = BorderStyle.None,
                    BackColor = Theme.PanelBackground,
                    ForeColor = Theme.TextPrimary,
                    Font = Theme.FontRegular,
                    ShowLines = false,
                    ItemHeight = 24,
                    ShowNodeToolTips = true,
                    ImageList = _icons
                };

                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

                Controls.Remove(Grid);
                Grid.Dock = DockStyle.Fill;
                layout.Controls.Add(Grid, 0, 0);
                layout.Controls.Add(_tree, 1, 0);
                Controls.Add(layout);
                Controls.SetChildIndex(layout, 0);

                InfoLabel.Text = "Load the users, then select some to see their teams and roles.";

                // Selecting rows shows their teams. The short delay lets a drag or Shift-click settle first.
                _selectionTimer = new Timer { Interval = 200 };
                _selectionTimer.Tick += (s, e) =>
                {
                    _selectionTimer.Stop();
                    if (Graph != null)
                    {
                        ShowTeams(promptWhenNothingSelected: false);
                    }
                };
                Grid.SelectionChanged += (s, e) =>
                {
                    _selectionTimer.Stop();
                    _selectionTimer.Start();
                };
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _selectionTimer?.Dispose();
                    _icons.Dispose();
                }

                base.Dispose(disposing);
            }

            protected override IEnumerable<Control> CreateCommands()
            {
                yield return Command(Theme.GlyphDownload, "Load users", () => WithGraph(OnGraphLoaded), strong: true);
                yield return Command(Glyphs.Group, "Show teams", ShowTeamsClicked);
                yield return Command(Theme.GlyphRefresh, "Refresh", () => WithGraph(OnGraphLoaded, refresh: true));
                yield return CommandSeparator();
                yield return Command(Theme.GlyphExpand, "Expand all", () => _tree.ExpandAll());
                yield return Command(Theme.GlyphCollapse, "Collapse all", CollapseToUsers);
                yield return CommandSeparator();
                yield return Command(Theme.GlyphDownload, "Export users", () => ExportUi.SaveGrid(this, Grid, "Users.xlsx", "Users"));
                yield return Command(Theme.GlyphDownload, "Export teams and roles", ExportTree);
                _chkDisabled = CommandCheck("Include disabled users", OnGraphLoaded);
                yield return _chkDisabled;
            }

            protected override void Reset()
            {
                _tree.Nodes.Clear();
                _shownUsers = new UserInfo[0];
                base.Reset();
                InfoLabel.Text = "Load the users, select some, then show their teams and roles.";
            }

            protected override void OnGraphLoaded()
            {
                if (Graph == null)
                {
                    return;
                }

                var includeDisabled = _chkDisabled != null && _chkDisabled.Checked;
                var users = Graph.Users.Where(u => includeDisabled || !u.IsDisabled).ToList();
                Grid.SetData(users, "FullName=Full Name", "DomainName=User Name", "BusinessUnitName=Business Unit", "Status");
                InfoLabel.Text = $"{users.Count} user(s). Select users (Ctrl/Shift for several) to see their teams and roles.";
            }

            /// <summary>
            /// With no users listed yet there is nothing to show teams for, so offer to load them first instead of
            /// asking about "all listed users" that do not exist.
            /// </summary>
            private void ShowTeamsClicked()
            {
                Host.EnsureConnection(() =>
                {
                    if (Graph == null || Grid.GetVisible<UserInfo>().Count == 0)
                    {
                        if (Host.CreateChangeUi(this).Ask("The users are not loaded yet. Load them now? You can then select users and show their teams."))
                        {
                            WithGraph(OnGraphLoaded);
                        }

                        return;
                    }

                    ShowTeams(promptWhenNothingSelected: true);
                });
            }

            private void ShowTeams(bool promptWhenNothingSelected)
            {
                var users = Grid.GetSelected<UserInfo>();
                if (users.Count == 0)
                {
                    if (!promptWhenNothingSelected)
                    {
                        // Selection was only cleared (by a filter, say): keep showing what was last chosen.
                        return;
                    }

                    if (!Host.CreateChangeUi(this).Ask("No user is selected. Show the teams of all listed users?"))
                    {
                        return;
                    }

                    users = Grid.GetVisible<UserInfo>();
                }

                _shownUsers = users;
                _tree.BeginUpdate();
                _tree.Nodes.Clear();
                foreach (var user in users)
                {
                    _tree.Nodes.Add(BuildUserNode(user));
                }

                CollapseToUsers();
                _tree.EndUpdate();
                InfoLabel.Text = $"{users.Count} user(s): roles assigned directly, and teams with the roles each team gives.";
            }

            /// <summary>
            /// A user with two headings: the roles assigned to them directly, and their teams (each team's roles are
            /// the ones the user inherits from it).
            /// </summary>
            private TreeNode BuildUserNode(UserInfo user)
            {
                var userNode = Node(user.FullName, TreeIcons.User, "User");

                var directRoles = Graph.DirectRolesOf(user.Id);
                var directNode = Node($"Direct roles ({directRoles.Count})", TreeIcons.DirectRolesGroup, "Security roles assigned to the user themselves");
                foreach (var role in directRoles)
                {
                    directNode.Nodes.Add(Node(role.Name, TreeIcons.DirectRole, "Assigned directly to the user"));
                }

                var teams = Graph.TeamsOf(user.Id);
                var teamsNode = Node($"Teams ({teams.Count})", TreeIcons.TeamsGroup, "Teams the user belongs to; a team's roles apply to all its members");
                foreach (var team in teams)
                {
                    var teamNode = Node(team.Name, TreeIcons.Team, team.TypeName + " team");
                    foreach (var role in Graph.RolesOfTeam(team.Id))
                    {
                        teamNode.Nodes.Add(Node(role.Name, TreeIcons.InheritedRole, $"Inherited from team {team.Name}"));
                    }

                    teamsNode.Nodes.Add(teamNode);
                }

                userNode.Nodes.Add(directNode);
                userNode.Nodes.Add(teamsNode);
                return userNode;
            }

            private static TreeNode Node(string text, int icon, string toolTip)
            {
                return new TreeNode(text) { ImageIndex = icon, SelectedImageIndex = icon, ToolTipText = toolTip };
            }

            /// <summary>
            /// Users and their headings open; teams stay closed once several users are shown (so a long list stays
            /// readable), but are opened when there are only a few users.
            /// </summary>
            private void CollapseToUsers()
            {
                _tree.ExpandAll();
                if (_tree.Nodes.Count <= 3)
                {
                    return;
                }

                foreach (TreeNode user in _tree.Nodes)
                {
                    foreach (TreeNode heading in user.Nodes)
                    {
                        foreach (TreeNode team in heading.Nodes)
                        {
                            if (team.ImageIndex == TreeIcons.Team)
                            {
                                team.Collapse();
                            }
                        }
                    }
                }
            }

            /// <summary>One row per role or team of each shown user, with where each role comes from.</summary>
            private void ExportTree()
            {
                var rows = new List<IReadOnlyList<string>>();
                if (Graph != null)
                {
                    Graph.BuildUserAccess(_shownUsers, out _, out var detail);
                    foreach (var row in detail)
                    {
                        rows.Add(new[] { row.User, row.BusinessUnit, row.Team, row.Role, row.Source });
                    }
                }

                ExportUi.Save(this, "UsersTeamsRoles.xlsx", "Users Teams Roles",
                    new[] { "User", "Business Unit", "Team", "Role", "Source" }, rows);
            }
        }
    }
}
