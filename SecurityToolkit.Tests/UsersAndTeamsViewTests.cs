using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using SecurityToolkit.Features;
using SecurityToolkit.UI;
using Xunit;

namespace SecurityToolkit.Tests
{
    public class UsersAndTeamsViewTests
    {
        private static void Click(CommandButton button)
        {
            typeof(Control).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(button, new object[] { EventArgs.Empty });
        }

        private static CommandButton Button(FeatureView view, string text)
        {
            return view.Commands.OfType<CommandButton>().First(b => b.Text == text);
        }

        /// <summary>Opens the feature on the sample organization, loads the users and selects the named ones.</summary>
        private static void WithSelection(string[] selected, Action<FeatureView, TreeView> test)
        {
            Sta.Run(() =>
            {
                var org = new Org();
                var view = new UsersAndTeamsFeature().CreateView(OrgFake.HostFor(org));
                var form = new Form { Size = new Size(1000, 500) };
                form.Controls.Add(view);
                form.Show();
                Application.DoEvents();

                Click(Button(view, "Load users"));
                var grid = (DataGridView)Sta.Find(view, c => c is DataGridView);
                foreach (DataGridViewRow row in grid.Rows)
                {
                    row.Selected = selected.Contains(((SecurityToolkit.Models.UserInfo)row.DataBoundItem).FullName);
                }

                Click(Button(view, "Show teams"));
                test(view, (TreeView)Sta.Find(view, c => c is TreeView));
                form.Dispose();
            });
        }

        private static string[] Texts(TreeNodeCollection nodes)
        {
            return nodes.Cast<TreeNode>().Select(n => n.Text).ToArray();
        }

        [Fact]
        public void Tree_ShowsDirectRolesAndTeamsUnderEachUser()
        {
            WithSelection(new[] { "Bob" }, (view, tree) =>
            {
                var bob = Assert.Single(tree.Nodes.Cast<TreeNode>());
                Assert.Equal("Bob", bob.Text);
                Assert.Equal(new[] { "Direct roles (1)", "Teams (2)" }, Texts(bob.Nodes));

                var direct = bob.Nodes[0];
                Assert.Equal(new[] { "Seller" }, Texts(direct.Nodes));

                var teams = bob.Nodes[1];
                Assert.Equal(new[] { "Empty team", "Reviewers" }, Texts(teams.Nodes));
                Assert.Equal(new[] { "Auditor" }, Texts(teams.Nodes[1].Nodes));
            });
        }

        [Fact]
        public void Tree_GivesEachKindOfNodeItsOwnIcon()
        {
            WithSelection(new[] { "Bob" }, (view, tree) =>
            {
                var bob = tree.Nodes[0];
                Assert.Equal(TreeIcons.User, bob.ImageIndex);
                Assert.Equal(TreeIcons.DirectRolesGroup, bob.Nodes[0].ImageIndex);
                Assert.Equal(TreeIcons.DirectRole, bob.Nodes[0].Nodes[0].ImageIndex);
                Assert.Equal(TreeIcons.TeamsGroup, bob.Nodes[1].ImageIndex);
                Assert.Equal(TreeIcons.Team, bob.Nodes[1].Nodes[1].ImageIndex);
                Assert.Equal(TreeIcons.InheritedRole, bob.Nodes[1].Nodes[1].Nodes[0].ImageIndex);
            });
        }

        [Fact]
        public void Tree_SaysWhereEachRoleComesFrom()
        {
            WithSelection(new[] { "Bob" }, (view, tree) =>
            {
                var bob = tree.Nodes[0];
                Assert.Equal("Assigned directly to the user", bob.Nodes[0].Nodes[0].ToolTipText);
                Assert.Equal("Inherited from team Reviewers", bob.Nodes[1].Nodes[1].Nodes[0].ToolTipText);
            });
        }

        [Fact]
        public void Tree_ShowsEmptyHeadingsForAUserWithNothing()
        {
            WithSelection(new[] { "Dee" }, (view, tree) =>
            {
                var dee = tree.Nodes[0];
                Assert.Equal(new[] { "Direct roles (0)", "Teams (0)" }, Texts(dee.Nodes));
                Assert.Empty(dee.Nodes[0].Nodes.Cast<TreeNode>());
            });
        }

        [Fact]
        public void Tree_ShowsSeveralSelectedUsersAsSeparateBranches()
        {
            WithSelection(new[] { "Ada", "Bob" }, (view, tree) =>
            {
                Assert.Equal(new[] { "Ada", "Bob" }, Texts(tree.Nodes));
                Assert.Equal(new[] { "Admin" }, Texts(tree.Nodes[0].Nodes[0].Nodes));
            });
        }

        [Fact]
        public void Tree_IsFullyExpandedForAFewUsers()
        {
            WithSelection(new[] { "Bob" }, (view, tree) =>
            {
                var reviewers = tree.Nodes[0].Nodes[1].Nodes[1];
                Assert.True(reviewers.IsExpanded);
            });
        }

        [Fact]
        public void IconSet_HasAnImageForEveryNodeKind()
        {
            Sta.Run(() =>
            {
                using (var icons = TreeIcons.Create())
                {
                    Assert.True(icons.Images.Count > TreeIcons.TeamsGroup);
                    Assert.True(icons.Images.Count > TreeIcons.InheritedRole);
                }
            });
        }
    }
}
