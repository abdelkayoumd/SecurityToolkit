using System;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Features;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using Xunit;

namespace SecurityToolkit.Tests
{
    public class CompareUsersViewTests
    {
        private static void Run(Action<FeatureView, FilterableGridPair, Org> test)
        {
            MasterDetailHarness.Run(new CompareUsersFeature(), (view, master, detail, org) => test(view, new FilterableGridPair(master, detail), org));
        }

        private sealed class FilterableGridPair
        {
            public FilterableGridPair(SecurityToolkit.UI.FilterableGrid left, SecurityToolkit.UI.FilterableGrid right) { Left = left; Right = right; }
            public SecurityToolkit.UI.FilterableGrid Left { get; }
            public SecurityToolkit.UI.FilterableGrid Right { get; }
        }

        private static string Info(FeatureView view, string containing)
        {
            var label = (Label)Sta.Find(view, c => c is Label l && l.Text.Contains(containing));
            return label?.Text;
        }

        [Fact]
        public void OpeningTheFeature_ListsTheUsers_AndAsksForTwo()
        {
            Run((view, grids, org) =>
            {
                Assert.Equal(new[] { "Ada", "Bob", "Dee" }, grids.Left.GetVisible<UserInfo>().Select(u => u.FullName)); // Cy is disabled
                Assert.NotNull(Info(view, "Select two users"));
            });
        }

        [Fact]
        public void SelectingTwoUsers_ShowsTheirDifferences_WithColumnsNamedAfterThem()
        {
            Run((view, grids, org) =>
            {
                MasterDetailHarness.Select<UserInfo>(grids.Left, u => u.FullName == "Ada" || u.FullName == "Bob");
                TeamMembersViewTests.Pump(() => grids.Right.GetVisible<CompareRow>().Any());

                Assert.Equal(new[] { "Category", "Item", "Ada", "Bob", "Result" }, grids.Right.GetTable().Key);
                var admin = grids.Right.GetVisible<CompareRow>().Single(r => r.Item == "Admin");
                Assert.Equal("Only Ada", admin.Result);
                Assert.NotNull(Info(view, "different business units (Root / Sales)"));
            });
        }

        [Fact]
        public void DifferencesOnly_HidesWhatBothHave_UntilTheBoxIsUnticked()
        {
            Run((view, grids, org) =>
            {
                MasterDetailHarness.Select<UserInfo>(grids.Left, u => u.FullName == "Bob" || u.FullName == "Dee");
                // Bob and Dee are both in Sales. That line is no difference, but it is always shown; everything else is a difference.
                TeamMembersViewTests.Pump(() => grids.Right.GetVisible<CompareRow>().Any());

                Assert.Contains(grids.Right.GetVisible<CompareRow>(), r => r.Category == "Business unit" && r.Result == "Same");
                Assert.All(grids.Right.GetVisible<CompareRow>().Where(r => r.Category != "Business unit"), r => Assert.True(r.IsDifference));

                MasterDetailHarness.Check(view, "Differences only").Checked = false;

                Assert.Contains(grids.Right.GetVisible<CompareRow>(), r => r.Category == "Business unit" && r.Result == "Same");
            });
        }

        [Fact]
        public void TwoUsersWithNothingToCompare_StillShowTheirBusinessUnit()
        {
            Sta.Run(() =>
            {
                var org = new CleanupOrg();
                var host = new TestHost { Security = new SecurityService(OrgFake.Serve(org.Graph)) };
                var view = new CompareUsersFeature().CreateView(host);
                var form = new Form { Size = new System.Drawing.Size(1200, 500) };
                form.Controls.Add(view);
                form.Show();
                Application.DoEvents();
                _ = view.Commands;
                view.OnActivated();
                var grids = new System.Collections.Generic.List<SecurityToolkit.UI.FilterableGrid>();
                CollectGrids(view, grids);
                var ordered = grids.OrderBy(g => g.PointToScreen(System.Drawing.Point.Empty).X).ToArray();

                // Ann and Svc have no roles and the same teams: nothing differs, yet the grid is not left empty.
                MasterDetailHarness.Select<UserInfo>(ordered[0], u => u.FullName == "Ann" || u.FullName == "Svc");
                TeamMembersViewTests.Pump(() => ordered[1].GetVisible<CompareRow>().Any());

                var row = Assert.Single(ordered[1].GetVisible<CompareRow>());
                Assert.Equal("Business unit", row.Category);
                Assert.Equal("Same", row.Result);
                form.Dispose();
            });
        }

        private static void CollectGrids(Control root, System.Collections.Generic.List<SecurityToolkit.UI.FilterableGrid> found)
        {
            foreach (Control child in root.Controls)
            {
                if (child is SecurityToolkit.UI.FilterableGrid grid) { found.Add(grid); } else { CollectGrids(child, found); }
            }
        }

        [Fact]
        public void TheSummaryLine_CountsWhatEachSideHasAndWhatTheyShare()
        {
            Run((view, grids, org) =>
            {
                MasterDetailHarness.Select<UserInfo>(grids.Left, u => u.FullName == "Ada" || u.FullName == "Bob");
                TeamMembersViewTests.Pump(() => grids.Right.GetVisible<CompareRow>().Any());

                // Ada: Admin. Bob: Auditor (team), Seller, Empty team, Reviewers.
                Assert.NotNull(Info(view, "1 only Ada, 4 only Bob, 0 in both"));
            });
        }

        [Fact]
        public void SelectingOneUser_PromptsForASecond_AndKeepsTheLastComparison()
        {
            Run((view, grids, org) =>
            {
                MasterDetailHarness.Select<UserInfo>(grids.Left, u => u.FullName == "Ada" || u.FullName == "Bob");
                TeamMembersViewTests.Pump(() => grids.Right.GetVisible<CompareRow>().Any());
                var shown = grids.Right.GetVisible<CompareRow>().Count();

                MasterDetailHarness.Select<UserInfo>(grids.Left, u => u.FullName == "Dee");
                TeamMembersViewTests.Pump(() => Info(view, "Ctrl-click a second user") != null);

                Assert.Equal(shown, grids.Right.GetVisible<CompareRow>().Count());
            });
        }

        [Fact]
        public void SelectingThree_SaysToSelectExactlyTwo()
        {
            Run((view, grids, org) =>
            {
                MasterDetailHarness.Select<UserInfo>(grids.Left, u => true);
                TeamMembersViewTests.Pump(() => Info(view, "exactly two") != null);
            });
        }
    }
}
