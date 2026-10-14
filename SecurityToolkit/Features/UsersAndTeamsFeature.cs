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
        public int Order => 10;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new UsersAndTeamsView(host);
        }

        private sealed class UsersAndTeamsView : DataView
        {
            private readonly TreeView _tree;
            private readonly Timer _selectionTimer;
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
                    ItemHeight = 24
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
                }

                base.Dispose(disposing);
            }

            protected override IEnumerable<Control> CreateCommands()
            {
                yield return Command(Theme.GlyphDownload, "Load users", () => WithGraph(OnGraphLoaded), strong: true);
                yield return Command(Glyphs.Group, "Show teams", () => WithGraph(() => ShowTeams(promptWhenNothingSelected: true)));
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

                    var answer = MessageBox.Show(this, "No user is selected. Show the teams of all listed users?", "Show teams",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (answer != DialogResult.Yes)
                    {
                        return;
                    }

                    users = Grid.GetVisible<UserInfo>();
                }

                _tree.BeginUpdate();
                _tree.Nodes.Clear();
                foreach (var user in users)
                {
                    var userNode = new TreeNode(user.FullName);
                    foreach (var team in Graph.TeamsOf(user.Id))
                    {
                        var teamNode = new TreeNode(team.Name);
                        foreach (var role in Graph.RolesOfTeam(team.Id))
                        {
                            teamNode.Nodes.Add(new TreeNode(role.Name));
                        }

                        userNode.Nodes.Add(teamNode);
                    }

                    _tree.Nodes.Add(userNode);
                }

                CollapseToUsers();
                _tree.EndUpdate();
                InfoLabel.Text = $"Teams and roles of {users.Count} user(s). Team roles are shown under each team.";
            }

            /// <summary>Users expanded to their teams, teams collapsed.</summary>
            private void CollapseToUsers()
            {
                _tree.ExpandAll();
                foreach (TreeNode user in _tree.Nodes)
                {
                    foreach (TreeNode team in user.Nodes)
                    {
                        team.Collapse();
                    }
                }
            }

            private void ExportTree()
            {
                var rows = new List<IReadOnlyList<string>>();
                foreach (TreeNode user in _tree.Nodes)
                {
                    if (user.Nodes.Count == 0)
                    {
                        rows.Add(new[] { user.Text, string.Empty, string.Empty });
                    }

                    foreach (TreeNode team in user.Nodes)
                    {
                        if (team.Nodes.Count == 0)
                        {
                            rows.Add(new[] { user.Text, team.Text, string.Empty });
                        }

                        foreach (TreeNode role in team.Nodes)
                        {
                            rows.Add(new[] { user.Text, team.Text, role.Text });
                        }
                    }
                }

                ExportUi.Save(this, "UsersTeamsRoles.xlsx", "Users Teams Roles", new[] { "User", "Team", "Role" }, rows);
            }
        }
    }
}
