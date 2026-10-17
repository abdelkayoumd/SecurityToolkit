using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Features;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using SecurityToolkit.UI;
using Xunit;

namespace SecurityToolkit.Tests
{
    /// <summary>Root (Ada, team Alpha) -> Sales (Bob, Dee, its default team) -> Emea (Cy disabled, team Beta).</summary>
    internal sealed class TreeOrg
    {
        public Org O = new Org();
        public TeamInfo Alpha, SalesDefault, Beta;
        public SecurityGraph Graph;

        public TreeOrg()
        {
            Alpha = new TeamInfo { Id = Guid.NewGuid(), Name = "Alpha", TeamType = 0, BusinessUnitId = O.Root, BusinessUnitName = "Root" };
            SalesDefault = new TeamInfo { Id = Guid.NewGuid(), Name = "Sales", TeamType = 0, IsDefault = true, BusinessUnitId = O.Sales, BusinessUnitName = "Sales" };
            Beta = new TeamInfo { Id = Guid.NewGuid(), Name = "Beta", TeamType = 0, BusinessUnitId = O.Emea, BusinessUnitName = "Emea" };

            Graph = new SecurityGraph(
                O.Graph.Users, O.Graph.Roles, new[] { Alpha, SalesDefault, Beta }, O.Graph.BusinessUnits,
                new[] { new UserTeamLink { UserId = O.Ada.Id, TeamId = Alpha.Id } },
                new[]
                {
                    new UserRoleLink { UserId = O.Ada.Id, RoleId = O.Admin.Id },
                    new UserRoleLink { UserId = O.Bob.Id, RoleId = O.SellerSales.Id }
                },
                new[] { new TeamRoleLink { TeamId = Alpha.Id, RoleId = O.Auditor.Id } });
        }
    }

    public class BusinessUnitTreeTests
    {
        private readonly TreeOrg _t = new TreeOrg();

        [Fact]
        public void TheHierarchyFollowsTheParents()
        {
            var root = Assert.Single(BusinessUnitTree.Build(_t.Graph));

            Assert.Equal("Root", root.Name);
            var sales = Assert.Single(root.Children);
            Assert.Equal("Sales", sales.Name);
            Assert.Equal("Emea", Assert.Single(sales.Children).Name);
        }

        [Fact]
        public void EachUnitCountsItsActiveUsersAndItsTeams_NotTheDefaultTeam()
        {
            var root = BusinessUnitTree.Build(_t.Graph).Single();
            var sales = root.Children.Single();

            Assert.Equal(1, root.Users);
            Assert.Equal(1, root.Teams);
            Assert.Equal(2, sales.Users);
            Assert.Equal(0, sales.Teams); // its only team is the default one
        }

        [Fact]
        public void TheEmeaUserIsDisabled_SoItIsNotCounted()
        {
            var emea = BusinessUnitTree.Build(_t.Graph).Single().Children.Single().Children.Single();

            Assert.Equal(0, emea.Users);
            Assert.Equal(1, emea.Teams);
        }

        [Fact]
        public void TotalsAddUpEverythingBelow()
        {
            var root = BusinessUnitTree.Build(_t.Graph).Single();

            Assert.Equal(3, root.TotalUsers);
            Assert.Equal(2, root.TotalTeams);
        }

        [Fact]
        public void AnOrphanedUnit_BecomesARoot()
        {
            var lone = new BusinessUnitInfo { Id = Guid.NewGuid(), Name = "Lone", ParentId = Guid.NewGuid() };
            var graph = new SecurityGraph(_t.Graph.Users, _t.Graph.Roles, _t.Graph.Teams, _t.Graph.BusinessUnits.Concat(new[] { lone }),
                new UserTeamLink[0], new UserRoleLink[0], new TeamRoleLink[0]);

            Assert.Equal(new[] { "Lone", "Root" }, BusinessUnitTree.Build(graph).Select(r => r.Name).OrderBy(n => n));
        }

        [Fact]
        public void ContentsOfAUnit_ListUsersThenTeamsThenRolesInUse()
        {
            var items = BusinessUnitTree.Contents(_t.Graph, _t.O.Root, includeChildren: false, includeDisabled: false);

            Assert.Equal(new[] { "User:Ada", "Team:Alpha", "Role in use:Admin", "Role in use:Auditor" }, items.Select(i => i.Type + ":" + i.Name));
        }

        [Fact]
        public void ARoleIsShownWithWhoHoldsIt()
        {
            var items = BusinessUnitTree.Contents(_t.Graph, _t.O.Root, false, false);

            Assert.Equal("Held by 1 user(s) and 0 team(s)", items.Single(i => i.Name == "Admin").Detail);
            Assert.Equal("Held by 0 user(s) and 1 team(s)", items.Single(i => i.Name == "Auditor").Detail);
        }

        [Fact]
        public void ChildUnitsAreIncludedOnlyWhenAsked()
        {
            Assert.DoesNotContain(BusinessUnitTree.Contents(_t.Graph, _t.O.Sales, false, false), i => i.Name == "Beta");
            Assert.Contains(BusinessUnitTree.Contents(_t.Graph, _t.O.Sales, true, false), i => i.Name == "Beta");
        }

        [Fact]
        public void DisabledUsersAreIncludedOnlyWhenAsked()
        {
            Assert.DoesNotContain(BusinessUnitTree.Contents(_t.Graph, _t.O.Emea, false, false), i => i.Name == "Cy");
            var cy = BusinessUnitTree.Contents(_t.Graph, _t.O.Emea, false, true).Single(i => i.Name == "Cy");
            Assert.Equal("Disabled", cy.Status);
        }

        [Fact]
        public void TheDefaultTeamIsNeverListed()
        {
            Assert.DoesNotContain(BusinessUnitTree.Contents(_t.Graph, _t.O.Sales, true, true), i => i.Type == BusinessUnitTree.TeamType && i.Name == "Sales");
        }

        [Fact]
        public void ARoleWithCopiesInSeveralUnits_IsOneRole()
        {
            var seller = BusinessUnitTree.Contents(_t.Graph, _t.O.Sales, false, false).Single(i => i.Name == "Seller");

            Assert.Equal("Held by 1 user(s) and 0 team(s)", seller.Detail);
        }

        [Fact]
        public void ANodeSaysWhatIsInIt()
        {
            var root = BusinessUnitTree.Build(_t.Graph).Single();

            Assert.Contains("1 user, 1 team", BusinessUnitTree.Describe(root));
            Assert.Contains("3 users, 2 teams with children", BusinessUnitTree.Describe(root));
        }
    }

    public class BusinessUnitTreeViewTests
    {
        private static void Run(Action<FeatureView, TreeView, FilterableGrid> test)
        {
            Sta.Run(() =>
            {
                var t = new TreeOrg();
                var host = new TestHost { Security = new SecurityService(OrgFake.Serve(t.Graph)) };
                var view = new BusinessUnitTreeFeature().CreateView(host);
                var form = new Form { Size = new Size(1200, 500) };
                form.Controls.Add(view);
                form.Show();
                Application.DoEvents();
                _ = view.Commands;
                view.OnActivated();

                var tree = (TreeView)Sta.Find(view, c => c is TreeView);
                var grid = (FilterableGrid)Sta.Find(view, c => c is FilterableGrid);
                test(view, tree, grid);
                form.Dispose();
            });
        }

        private static TreeNode Node(TreeView tree, string name)
        {
            TreeNode Search(TreeNodeCollection nodes)
            {
                foreach (TreeNode n in nodes)
                {
                    if (((BusinessUnitNode)n.Tag).Name == name) { return n; }
                    var inner = Search(n.Nodes);
                    if (inner != null) { return inner; }
                }

                return null;
            }

            return Search(tree.Nodes);
        }

        [Fact]
        public void OpeningTheFeature_ShowsTheWholeHierarchyExpanded()
        {
            Run((view, tree, grid) =>
            {
                Assert.Single(tree.Nodes);
                Assert.NotNull(Node(tree, "Emea"));
                Assert.True(Node(tree, "Sales").IsExpanded);
            });
        }

        private static void Show(FeatureView view, string what)
        {
            var combo = view.Commands.OfType<ComboBox>().Single(c => c.Name == "ShowChoice");
            combo.SelectedItem = what;
        }

        [Fact]
        public void ByDefault_AUnitShowsItsUsers_WithTheirTeamsAndRoles_IncludingChildUnits()
        {
            Run((view, tree, grid) =>
            {
                tree.SelectedNode = Node(tree, "Sales");

                var bob = grid.GetVisible<UserSummaryRow>().Single(r => r.User == "Bob");
                Assert.Equal("Seller", bob.DirectRoles);
                Assert.Equal(new[] { "Bob", "Dee" }, grid.GetVisible<UserSummaryRow>().Select(r => r.User).OrderBy(n => n)); // Cy (Emea) is disabled
            });
        }

        [Fact]
        public void ChildUnitsAndDisabledUsers_AreAddedWhenTicked()
        {
            Run((view, tree, grid) =>
            {
                tree.SelectedNode = Node(tree, "Sales");
                view.Commands.OfType<CheckBox>().First(c => c.Text == "Include disabled users").Checked = true;

                Assert.Equal(new[] { "Bob", "Cy", "Dee" }, grid.GetVisible<UserSummaryRow>().Select(r => r.User).OrderBy(n => n));
            });
        }

        [Fact]
        public void UntickingChildUnits_NarrowsTheList()
        {
            Run((view, tree, grid) =>
            {
                tree.SelectedNode = Node(tree, "Root");
                view.Commands.OfType<CheckBox>().First(c => c.Text == "Include disabled users").Checked = true;
                Assert.Contains(grid.GetVisible<UserSummaryRow>(), r => r.User == "Cy");

                view.Commands.OfType<CheckBox>().First(c => c.Text == "Include child business units").Checked = false;

                Assert.Equal(new[] { "Ada" }, grid.GetVisible<UserSummaryRow>().Select(r => r.User));
            });
        }

        [Fact]
        public void ChoosingTeams_ListsTheTeamsOfTheUnit()
        {
            Run((view, tree, grid) =>
            {
                tree.SelectedNode = Node(tree, "Sales");

                Show(view, "Teams");

                Assert.Equal(new[] { "Beta" }, grid.GetVisible<BusinessUnitItem>().Select(i => i.Name)); // the default team is not listed
                Assert.Equal(new[] { "Team", "Business Unit", "Detail" }, grid.GetTable().Key);
            });
        }

        [Fact]
        public void ChoosingRolesInUse_ListsTheRolesHeldInTheUnit()
        {
            Run((view, tree, grid) =>
            {
                tree.SelectedNode = Node(tree, "Sales");

                Show(view, "Roles in use");

                var seller = Assert.Single(grid.GetVisible<BusinessUnitItem>());
                Assert.Equal("Seller", seller.Name);
                Assert.Equal(new[] { "Role", "Detail", "Kind" }, grid.GetTable().Key);
            });
        }

        [Fact]
        public void TheUsersExport_HasTheUsersColumns()
        {
            Run((view, tree, grid) =>
            {
                tree.SelectedNode = Node(tree, "Root");

                Assert.Equal(new[] { "User", "Business Unit", "Teams", "Direct roles", "Roles from teams", "Status" }, grid.GetTable().Key);
            });
        }

        [Fact]
        public void TheSummaryLine_CountsUsersTeamsAndRoles()
        {
            Run((view, tree, grid) =>
            {
                tree.SelectedNode = Node(tree, "Sales");

                var info = ((System.Windows.Forms.Label)Sta.Find(view, c => c is System.Windows.Forms.Label l && l.Text.Contains("role(s) in use"))).Text;
                Assert.Contains("2 user(s), 1 team(s), 1 role(s) in use", info);
            });
        }

        [Fact]
        public void TheOldUsersByBusinessUnitScreen_IsGone_AndItsJobIsHere()
        {
            Assert.DoesNotContain(FeatureRegistry.CreateAll(), f => f.Id == "users-by-business-unit");
            Assert.Contains(view_commandsOfUsers(), t => t == "Export users in detail");
        }

        private static IEnumerable<string> view_commandsOfUsers()
        {
            var view = new BusinessUnitTreeFeature().CreateView(new TestHost());
            return view.Commands.OfType<CommandButton>().Select(b => b.Text);
        }

        [Fact]
        public void ItIsListedUnderItsGroup_AndRegistered()
        {
            Assert.Equal(FeatureGroup.LookUp, new BusinessUnitTreeFeature().Group);
            Assert.Contains(FeatureRegistry.CreateAll(), f => f is BusinessUnitTreeFeature);
        }
    }
}
