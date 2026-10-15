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
    /// <summary>Shared plumbing for tests of features laid out as a list on the left and results on the right.</summary>
    internal static class MasterDetailHarness
    {
        public static void Run(IFeature feature, Action<FeatureView, FilterableGrid, FilterableGrid, Org> test, bool connected = true)
        {
            Sta.Run(() =>
            {
                var org = new Org();
                var host = connected ? OrgFake.HostFor(org) : new TestHost { Security = null };
                var view = feature.CreateView(host);
                var form = new Form { Size = new Size(1200, 500) };
                form.Controls.Add(view);
                form.Show();
                Application.DoEvents();
                _ = view.Commands; // the shell builds a feature's command bar before showing it
                view.OnActivated();

                var grids = new List<FilterableGrid>();
                Collect(view, grids);
                var ordered = grids.OrderBy(g => g.PointToScreen(Point.Empty).X).ToArray();
                test(view, ordered[0], ordered[1], org);
                form.Dispose();
            });
        }

        private static void Collect(Control root, List<FilterableGrid> found)
        {
            foreach (Control child in root.Controls)
            {
                if (child is FilterableGrid grid)
                {
                    found.Add(grid);
                }
                else
                {
                    Collect(child, found);
                }
            }
        }

        public static void Select<T>(FilterableGrid grid, Func<T, bool> pick)
        {
            var data = (DataGridView)Sta.Find(grid, c => c is DataGridView);
            foreach (DataGridViewRow row in data.Rows)
            {
                row.Selected = pick((T)row.DataBoundItem);
            }
        }

        public static CheckBox Check(FeatureView view, string text)
        {
            return view.Commands.OfType<CheckBox>().First(c => c.Text == text);
        }
    }

    public class UsersByRoleViewTests
    {
        private static void Run(Action<FeatureView, FilterableGrid, FilterableGrid, Org> test, bool connected = true)
        {
            MasterDetailHarness.Run(new UsersByRoleFeature(), test, connected);
        }

        [Fact]
        public void OpeningTheFeature_ListsTheRolesOnceEach()
        {
            Run((view, master, detail, org) =>
            {
                Assert.Equal(new[] { "Admin", "Auditor", "Seller" }, master.GetVisible<RoleChoice>().Select(r => r.Name));
            });
        }

        [Fact]
        public void SelectingARole_ShowsWhoHasItAndHow()
        {
            Run((view, master, detail, org) =>
            {
                MasterDetailHarness.Select<RoleChoice>(master, r => r.Name == "Auditor");
                TeamMembersViewTests.Pump(() => detail.GetVisible<RoleUserRow>().Any());

                var row = Assert.Single(detail.GetVisible<RoleUserRow>()); // Cy is disabled
                Assert.Equal("Bob", row.User);
                Assert.Equal("Via team", row.Assignment);
                Assert.Equal("Reviewers", row.Team);
                Assert.Equal("Auditor", row.Role);
            });
        }

        [Fact]
        public void SelectingSeveralRoles_CombinesTheirHolders_EachRowNamingItsRole()
        {
            Run((view, master, detail, org) =>
            {
                MasterDetailHarness.Select<RoleChoice>(master, r => r.Name == "Admin" || r.Name == "Seller");
                TeamMembersViewTests.Pump(() => detail.GetVisible<RoleUserRow>().Count() == 2);

                Assert.Equal(new[] { "Admin/Ada", "Seller/Bob" },
                    detail.GetVisible<RoleUserRow>().Select(r => r.Role + "/" + r.User).OrderBy(s => s));
            });
        }

        [Fact]
        public void IncludeDisabledUsers_AddsThemToTheCurrentSelection()
        {
            Run((view, master, detail, org) =>
            {
                MasterDetailHarness.Select<RoleChoice>(master, r => r.Name == "Auditor");
                TeamMembersViewTests.Pump(() => detail.GetVisible<RoleUserRow>().Any());

                MasterDetailHarness.Check(view, "Include disabled users").Checked = true;

                Assert.Equal(new[] { "Bob", "Cy" }, detail.GetVisible<RoleUserRow>().Select(r => r.User).OrderBy(n => n));
            });
        }

        [Fact]
        public void WhenNotConnected_NothingIsLoaded()
        {
            Run((view, master, detail, org) => Assert.Empty(master.GetVisible<RoleChoice>()), connected: false);
        }
    }

    public class UsersByBusinessUnitViewTests
    {
        private static void Run(Action<FeatureView, FilterableGrid, FilterableGrid, Org> test)
        {
            MasterDetailHarness.Run(new UsersByBusinessUnitFeature(), test);
        }

        [Fact]
        public void OpeningTheFeature_ListsBusinessUnitsByPathSoChildrenFollowTheirParent()
        {
            Run((view, master, detail, org) =>
            {
                Assert.Equal(new[] { "Root", "Root / Sales", "Root / Sales / Emea" }, master.GetVisible<BusinessUnitRow>().Select(b => b.Path));
            });
        }

        [Fact]
        public void SelectingABusinessUnit_ShowsItsUsersWithDirectAndInheritedRoles()
        {
            Run((view, master, detail, org) =>
            {
                MasterDetailHarness.Select<BusinessUnitRow>(master, b => b.Name == "Sales");
                TeamMembersViewTests.Pump(() => detail.GetVisible<UserSummaryRow>().Any());

                var bob = detail.GetVisible<UserSummaryRow>().Single(r => r.User == "Bob");
                Assert.Equal("Seller", bob.DirectRoles);
                Assert.Equal("Auditor (Reviewers)", bob.TeamRoles);
                Assert.Equal(new[] { "Bob", "Dee" }, detail.GetVisible<UserSummaryRow>().Select(r => r.User).OrderBy(n => n));
            });
        }

        [Fact]
        public void IncludeChildBusinessUnits_AddsTheirUsers()
        {
            Run((view, master, detail, org) =>
            {
                MasterDetailHarness.Select<BusinessUnitRow>(master, b => b.Name == "Sales");
                TeamMembersViewTests.Pump(() => detail.GetVisible<UserSummaryRow>().Any());

                MasterDetailHarness.Check(view, "Include child business units").Checked = true;
                MasterDetailHarness.Check(view, "Include disabled users").Checked = true; // Cy (Emea) is disabled

                Assert.Equal(new[] { "Bob", "Cy", "Dee" }, detail.GetVisible<UserSummaryRow>().Select(r => r.User).OrderBy(n => n));
            });
        }

        [Fact]
        public void SelectingSeveralBusinessUnits_CombinesTheirUsers()
        {
            Run((view, master, detail, org) =>
            {
                MasterDetailHarness.Select<BusinessUnitRow>(master, b => b.Name == "Root" || b.Name == "Sales");
                TeamMembersViewTests.Pump(() => detail.GetVisible<UserSummaryRow>().Count() == 3);

                Assert.Equal(new[] { "Ada", "Bob", "Dee" }, detail.GetVisible<UserSummaryRow>().Select(r => r.User).OrderBy(n => n));
            });
        }
    }

    public class PrivilegeFinderViewTests
    {
        private static void Run(Action<FeatureView, FilterableGrid, FilterableGrid, Org> test, bool connected = true)
        {
            MasterDetailHarness.Run(new PrivilegeFinderFeature(), test, connected);
        }

        /// <summary>The privilege (0) and depth (1) dropdowns, which sit inside the sentence above the lists.</summary>
        private static ComboBox Combo(FeatureView view, int index)
        {
            var found = new List<ComboBox>();
            CollectCombos(view, found);
            return found.OrderBy(c => c.PointToScreen(Point.Empty).X).ElementAt(index);
        }

        private static void CollectCombos(Control root, List<ComboBox> found)
        {
            foreach (Control child in root.Controls)
            {
                if (child is ComboBox box)
                {
                    found.Add(box);
                }
                else
                {
                    CollectCombos(child, found);
                }
            }
        }

        /// <summary>The words of the sentence, joined: labels and the current choice of each dropdown, left to right.</summary>
        private static string Sentence(FeatureView view)
        {
            var panel = (FlowLayoutPanel)Sta.Find(view, c => c is FlowLayoutPanel p && p.Dock == DockStyle.Top && p.Controls.OfType<ComboBox>().Any());
            return string.Join(" ", panel.Controls.Cast<Control>().Select(c => c is ComboBox b ? "[" + b.Text + "]" : c.Text));
        }

        [Fact]
        public void TheDropdownsAreInTheSentence_NotOnTheCommandBar()
        {
            Run((view, master, detail, org) =>
            {
                Assert.Empty(view.Commands.OfType<ComboBox>());
                Assert.Equal(2, new Func<int>(() => { var f = new List<ComboBox>(); CollectCombos(view, f); return f.Count; })());
                Assert.True(Sta.Find(view, c => c is FlowLayoutPanel p && p.Controls.OfType<ComboBox>().Count() == 2).Visible);
            });
        }

        [Fact]
        public void BeforeATableIsPicked_TheSentenceReadsAsAQuestion()
        {
            Run((view, master, detail, org) =>
            {
                Assert.Equal("Roles that grant [Read] on the selected table(s) at [User] level or wider", Sentence(view));
            });
        }

        [Fact]
        public void AfterPickingATable_TheSentenceStatesTheResult()
        {
            Run((view, master, detail, org) =>
            {
                MasterDetailHarness.Select<TableInfo>(master, t => t.LogicalName == "account");
                TeamMembersViewTests.Pump(() => detail.GetVisible<RolePrivilegeRow>().Any());

                Assert.Equal("3 role(s) grant [Read] on 1 table(s) at [User] level or wider", Sentence(view));
            });
        }

        [Fact]
        public void ChangingTheDropdowns_UpdatesTheSentenceAndTheResults()
        {
            Run((view, master, detail, org) =>
            {
                MasterDetailHarness.Select<TableInfo>(master, t => t.LogicalName == "account");
                TeamMembersViewTests.Pump(() => detail.GetVisible<RolePrivilegeRow>().Any());

                Combo(view, 0).SelectedIndex = 3; // Delete
                Combo(view, 1).SelectedIndex = 3; // Organization

                Assert.Equal("1 role(s) grant [Delete] on 1 table(s) at [Organization] level", Sentence(view));
                Assert.Equal(new[] { "Admin" }, detail.GetVisible<RolePrivilegeRow>().Select(r => r.Role));
            });
        }

        [Fact]
        public void OpeningTheFeature_ListsTheTables()
        {
            Run((view, master, detail, org) =>
            {
                Assert.Equal(new[] { "account", "contact" }, master.GetVisible<TableInfo>().Select(t => t.LogicalName));
            });
        }

        [Fact]
        public void SelectingATable_ListsEveryRoleThatGrantsTheDefaultPrivilege()
        {
            Run((view, master, detail, org) =>
            {
                MasterDetailHarness.Select<TableInfo>(master, t => t.LogicalName == "account");
                TeamMembersViewTests.Pump(() => detail.GetVisible<RolePrivilegeRow>().Any());

                var rows = detail.GetVisible<RolePrivilegeRow>();
                Assert.Equal(new[] { "Admin", "Auditor", "Seller" }, rows.Select(r => r.Role).OrderBy(n => n));
                Assert.All(rows, r => Assert.Equal("account", r.Table));
            });
        }

        [Fact]
        public void RaisingTheMinimumDepth_DropsTheNarrowerRoles()
        {
            Run((view, master, detail, org) =>
            {
                MasterDetailHarness.Select<TableInfo>(master, t => t.LogicalName == "account");
                TeamMembersViewTests.Pump(() => detail.GetVisible<RolePrivilegeRow>().Any());

                Combo(view, 1).SelectedIndex = 1; // Business Unit or wider

                Assert.Equal(new[] { "Admin", "Auditor" }, detail.GetVisible<RolePrivilegeRow>().Select(r => r.Role).OrderBy(n => n));

                Combo(view, 1).SelectedIndex = 3; // Organization

                Assert.Equal(new[] { "Admin" }, detail.GetVisible<RolePrivilegeRow>().Select(r => r.Role));
            });
        }

        [Fact]
        public void SelectingSeveralTables_ListsEachTablesRoles()
        {
            Run((view, master, detail, org) =>
            {
                MasterDetailHarness.Select<TableInfo>(master, t => true);
                TeamMembersViewTests.Pump(() => detail.GetVisible<RolePrivilegeRow>().Count() == 6);

                Assert.Equal(new[] { "account", "contact" }, detail.GetVisible<RolePrivilegeRow>().Select(r => r.Table).Distinct().OrderBy(n => n));
            });
        }

        [Fact]
        public void WhenNotConnected_NothingIsLoaded()
        {
            Run((view, master, detail, org) => Assert.Empty(master.GetVisible<TableInfo>()), connected: false);
        }
    }
}
