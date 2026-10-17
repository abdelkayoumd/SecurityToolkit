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

        [Fact]
        public void SelectingAUnit_ListsItsContents_IncludingChildrenByDefault()
        {
            Run((view, tree, grid) =>
            {
                tree.SelectedNode = Node(tree, "Sales");

                var names = grid.GetVisible<BusinessUnitItem>().Select(i => i.Name).ToList();
                Assert.Contains("Bob", names);
                Assert.Contains("Beta", names); // a child unit's team
            });
        }

        [Fact]
        public void UntickingChildUnits_NarrowsTheList()
        {
            Run((view, tree, grid) =>
            {
                tree.SelectedNode = Node(tree, "Sales");
                var box = view.Commands.OfType<CheckBox>().First(c => c.Text == "Include child business units");

                box.Checked = false;

                Assert.DoesNotContain(grid.GetVisible<BusinessUnitItem>(), i => i.Name == "Beta");
            });
        }

        [Fact]
        public void TheExportCarriesTheColumnsOfTheList()
        {
            Run((view, tree, grid) =>
            {
                tree.SelectedNode = Node(tree, "Root");

                Assert.Equal(new[] { "Type", "Name", "Business Unit", "Detail", "Status" }, grid.GetTable().Key);
            });
        }

        [Fact]
        public void ItIsListedUnderExplore_AndRegistered()
        {
            Assert.Equal(FeatureGroup.Explore, new BusinessUnitTreeFeature().Group);
            Assert.Contains(FeatureRegistry.CreateAll(), f => f is BusinessUnitTreeFeature);
        }
    }
}
