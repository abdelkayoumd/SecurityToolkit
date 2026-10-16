using System;
using System.Collections.Generic;
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
    public class ExplainAccessViewTests
    {
        private static void Run(Action<FeatureView, FilterableGrid, FilterableGrid, ComboBox, ComboBox> test)
        {
            MasterDetailHarness.Run(new ExplainAccessFeature(), (view, users, routes, org) =>
            {
                var combos = new List<ComboBox>();
                Collect(view, combos);
                var ordered = combos.OrderBy(c => c.PointToScreen(Point.Empty).X).ToArray();
                test(view, users, routes, ordered[0], ordered[1]);
            });
        }

        private static void Collect(Control root, List<ComboBox> found)
        {
            foreach (Control child in root.Controls)
            {
                if (child is ComboBox box) { found.Add(box); } else { Collect(child, found); }
            }
        }

        private static void PickTable(ComboBox table, string logicalName)
        {
            var index = Enumerable.Range(0, table.Items.Count).First(i => table.Items[i].ToString().Contains("(" + logicalName + ")"));
            table.SelectedIndex = index;
        }

        private static string Info(FeatureView view, string containing)
        {
            return ((Label)Sta.Find(view, c => c is Label l && l.Text.Contains(containing)))?.Text;
        }

        [Fact]
        public void OpeningTheScreen_ListsTheUsers_AndFillsTheTableBox()
        {
            Run((view, users, routes, privilege, table) =>
            {
                Assert.Equal(new[] { "Ada", "Bob", "Dee" }, users.GetVisible<UserInfo>().Select(u => u.FullName)); // Cy is disabled
                Assert.Equal(2, table.Items.Count);
                Assert.Equal("Read", privilege.Text);
                Assert.NotNull(Info(view, "Select users on the left, then choose a privilege and a table"));
            });
        }

        [Fact]
        public void ADirectRole_IsShownAsDirect_WithItsDepthAndMarkedStrongest()
        {
            Run((view, users, routes, privilege, table) =>
            {
                MasterDetailHarness.Select<UserInfo>(users, u => u.FullName == "Ada");
                PickTable(table, "account");
                TeamMembersViewTests.Pump(() => routes.GetVisible<AccessReasonRow>().Any());

                var row = Assert.Single(routes.GetVisible<AccessReasonRow>());
                Assert.Equal("Admin", row.Role);
                Assert.Equal("Direct", row.Via);
                Assert.Equal("Organization", row.Depth);
                Assert.Equal("✓", row.Strongest);
                Assert.NotNull(Info(view, "Ada can Read"));
                Assert.NotNull(Info(view, "at Organization level"));
            });
        }

        [Fact]
        public void ARoleThroughATeam_NamesTheTeam_AndTheSummaryWarnsAboutTeamRecords()
        {
            Run((view, users, routes, privilege, table) =>
            {
                MasterDetailHarness.Select<UserInfo>(users, u => u.FullName == "Bob");
                PickTable(table, "account");
                TeamMembersViewTests.Pump(() => routes.GetVisible<AccessReasonRow>().Any());

                var row = Assert.Single(routes.GetVisible<AccessReasonRow>());
                Assert.Equal("Team: Reviewers", row.Via);
                Assert.NotNull(Info(view, "may apply only to records the team owns"));
            });
        }

        [Fact]
        public void AUserWithNoGrantingRole_IsShownAsHavingNone_NotLeftOut()
        {
            Run((view, users, routes, privilege, table) =>
            {
                MasterDetailHarness.Select<UserInfo>(users, u => u.FullName == "Dee");
                PickTable(table, "account");
                TeamMembersViewTests.Pump(() => routes.GetVisible<AccessReasonRow>().Any());

                var row = Assert.Single(routes.GetVisible<AccessReasonRow>());
                Assert.Equal("Dee", row.User);
                Assert.Equal("(no role grants this)", row.Role);
                Assert.NotNull(Info(view, "Dee has no role that grants Read on"));
            });
        }

        [Fact]
        public void SeveralUsers_ShowEachOnesRoutes_AndASummaryCount()
        {
            Run((view, users, routes, privilege, table) =>
            {
                MasterDetailHarness.Select<UserInfo>(users, u => true);
                PickTable(table, "account");
                TeamMembersViewTests.Pump(() => routes.GetVisible<AccessReasonRow>().Count() == 3);

                Assert.Equal(new[] { "Ada", "Bob", "Dee" }, routes.GetVisible<AccessReasonRow>().Select(r => r.User).OrderBy(n => n));
                Assert.NotNull(Info(view, "2 of 3 selected users have a role that grants Read on"));
            });
        }

        [Fact]
        public void ChangingThePrivilege_ExplainsAgainForTheNewOne()
        {
            Run((view, users, routes, privilege, table) =>
            {
                MasterDetailHarness.Select<UserInfo>(users, u => u.FullName == "Ada");
                PickTable(table, "account");
                TeamMembersViewTests.Pump(() => Info(view, "Ada can Read") != null);

                privilege.SelectedIndex = Enumerable.Range(0, privilege.Items.Count).First(i => privilege.Items[i].ToString() == "Delete");

                Assert.NotNull(Info(view, "Ada can Delete"));
            });
        }

        [Fact]
        public void TypingATableNameAndPressingEnter_PicksThatTable()
        {
            Run((view, users, routes, privilege, table) =>
            {
                MasterDetailHarness.Select<UserInfo>(users, u => u.FullName == "Ada");
                table.Text = "contact";

                var keyDown = typeof(Control).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance);
                keyDown.Invoke(table, new object[] { new KeyEventArgs(Keys.Enter) });
                TeamMembersViewTests.Pump(() => routes.GetVisible<AccessReasonRow>().Any());

                Assert.Contains("(contact)", table.SelectedItem.ToString());
                Assert.NotNull(Info(view, "Ada can Read contact"));
            });
        }

        [Fact]
        public void WithoutATable_NothingIsExplained_AndThePromptStays()
        {
            Run((view, users, routes, privilege, table) =>
            {
                MasterDetailHarness.Select<UserInfo>(users, u => u.FullName == "Ada");
                TeamMembersViewTests.Pump(() => true);
                System.Threading.Thread.Sleep(400);
                Application.DoEvents();

                Assert.Empty(routes.GetVisible<AccessReasonRow>());
                Assert.NotNull(Info(view, "choose a privilege and a table"));
            });
        }

        [Fact]
        public void ItIsListedUnderExplore_BesideThePrivilegeFinder()
        {
            var ids = FeatureRegistry.CreateAll().Select(f => f.Id).ToList();

            Assert.Equal(FeatureGroup.Explore, new ExplainAccessFeature().Group);
            Assert.Equal(ids.IndexOf("privilege-finder") + 1, ids.IndexOf("explain-access"));
        }
    }
}
