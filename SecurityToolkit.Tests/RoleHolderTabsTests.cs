using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using SecurityToolkit.Features;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using SecurityToolkit.UI;
using Xunit;

namespace SecurityToolkit.Tests
{
    public class TeamsInRoleTests
    {
        private readonly Org _o = new Org();

        [Fact]
        public void ARoleListsTheTeamsThatHoldIt_WithTheirMemberCounts()
        {
            var row = Assert.Single(_o.Graph.TeamsInRole(_o.Auditor.RootKey));

            Assert.Equal("Reviewers", row.Team);
            Assert.Equal("Auditor", row.Role);
            Assert.Equal(2, row.Members);   // Bob and Cy, the disabled one too
        }

        [Fact]
        public void ARoleNoTeamHolds_HasNoTeams()
        {
            Assert.Empty(_o.Graph.TeamsInRole(_o.Admin.RootKey));
        }

        [Fact]
        public void AnyBusinessUnitCopyOfTheRoleCounts()
        {
            var t = new TreeOrg();

            // Alpha holds Auditor; the role is found by its root, whichever copy the team has.
            Assert.Equal("Alpha", Assert.Single(t.Graph.TeamsInRole(t.O.Auditor.RootKey)).Team);
        }
    }

    public class TabStripTests
    {
        private static TabStrip Strip()
        {
            var strip = new TabStrip { Dock = DockStyle.None, Size = new Size(600, 32) };
            strip.AddTab("Users who have the role (0)");
            strip.AddTab("Teams who have the role (3)");
            return strip;
        }

        [Fact]
        public void TheFirstTabIsSelected_AndChoosingAnotherRaisesTheEvent()
        {
            Sta.Run(() =>
            {
                var strip = Strip();
                var raised = 0;
                strip.SelectedIndexChanged += (s, e) => raised++;
                Assert.Equal(0, strip.SelectedIndex);

                strip.SelectedIndex = 1;
                strip.SelectedIndex = 1; // already there: nothing happens

                Assert.Equal(1, strip.SelectedIndex);
                Assert.Equal(1, raised);
            });
        }

        [Fact]
        public void ClickingATab_SelectsIt()
        {
            Sta.Run(() =>
            {
                var strip = Strip();
                var second = strip.TabAt(new Point(250, 10));
                Assert.Equal(1, second);

                typeof(TabStrip).GetMethod("OnMouseClick", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(strip, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 250, 10, 0) });

                Assert.Equal(1, strip.SelectedIndex);
            });
        }

        [Fact]
        public void OutsideTheTabs_NothingIsHit()
        {
            Sta.Run(() => Assert.Equal(-1, Strip().TabAt(new Point(590, 10))));
        }

        [Fact]
        public void TheTitlesCanChange_ToShowCounts()
        {
            Sta.Run(() =>
            {
                var strip = Strip();

                strip.SetText(1, "Teams who have the role (7)");

                Assert.Equal("Teams who have the role (7)", strip.Tabs[1]);
            });
        }

        [Fact]
        public void ItPaintsWithoutError()
        {
            Sta.Run(() =>
            {
                var strip = Strip();
                using (var bitmap = new Bitmap(600, 32)) { strip.DrawToBitmap(bitmap, new Rectangle(0, 0, 600, 32)); }
            });
        }
    }

    public class RoleHolderViewTabsTests
    {
        private static void Run(System.Action<FeatureView, FilterableGrid, FilterableGrid, FilterableGrid, TabStrip> test)
        {
            MasterDetailHarness.Run(new UsersByRoleFeature(), (view, master, detail, org) =>
            {
                var teams = (FilterableGrid)Sta.Find(view, c => c is FilterableGrid g && g.EmptyMessage == "No team holds the selected roles.");
                var tabs = (TabStrip)Sta.Find(view, c => c is TabStrip);
                test(view, master, detail, teams, tabs);
            });
        }

        [Fact]
        public void TheRightSideHasTwoTabs_UsersFirst()
        {
            Run((view, roles, users, teams, tabs) =>
            {
                Assert.Equal(new[] { "Users who have the role", "Teams who have the role" }, tabs.Tabs);
                Assert.True(users.Visible);
                Assert.False(teams.Visible);
            });
        }

        [Fact]
        public void SelectingARole_ShowsTheCountsInTheTabTitles_AndTheTeamsThatHoldIt()
        {
            Run((view, roles, users, teams, tabs) =>
            {
                MasterDetailHarness.Select<RoleChoice>(roles, r => r.Name == "Auditor");
                TeamMembersViewTests.Pump(() => teams.GetVisible<RoleTeamRow>().Any());

                Assert.Equal("Users who have the role (1)", tabs.Tabs[0]);   // Bob, through the team; Cy is disabled
                Assert.Equal("Teams who have the role (1)", tabs.Tabs[1]);
                var team = Assert.Single(teams.GetVisible<RoleTeamRow>());
                Assert.Equal("Reviewers", team.Team);
                Assert.Equal(2, team.Members);
            });
        }

        [Fact]
        public void ChoosingTheTeamsTab_ShowsTheTeamsList_InsteadOfTheUsers()
        {
            Run((view, roles, users, teams, tabs) =>
            {
                tabs.SelectedIndex = 1;

                Assert.False(users.Visible);
                Assert.True(teams.Visible);
            });
        }

        [Fact]
        public void TheTeamsListHasItsColumns()
        {
            Run((view, roles, users, teams, tabs) =>
            {
                MasterDetailHarness.Select<RoleChoice>(roles, r => r.Name == "Auditor");
                TeamMembersViewTests.Pump(() => teams.GetVisible<RoleTeamRow>().Any());

                Assert.Equal(new[] { "Role", "Team", "Team type", "Business Unit", "Members" }, teams.GetTable().Key);
            });
        }

        [Fact]
        public void ARoleNoTeamHolds_SaysSoInTheTeamsList()
        {
            Run((view, roles, users, teams, tabs) =>
            {
                MasterDetailHarness.Select<RoleChoice>(roles, r => r.Name == "Admin");
                TeamMembersViewTests.Pump(() => tabs.Tabs[0].Contains("(1)"));

                Assert.Equal("Teams who have the role (0)", tabs.Tabs[1]);
                Assert.Empty(teams.GetVisible<RoleTeamRow>());
            });
        }
    }
}
