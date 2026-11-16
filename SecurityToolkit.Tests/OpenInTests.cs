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
    public class SubjectTests
    {
        private readonly Org _o = new Org();

        [Fact]
        public void ModelRows_GiveExactSubjects()
        {
            var user = Subjects.From(_o.Bob, _o.Graph).Single();
            Assert.Equal(SubjectKind.User, user.Kind);
            Assert.Equal(_o.Bob.Id, user.Id);

            var role = Subjects.From(new RoleChoice { RootKey = _o.SellerRoot.Id, Name = "Seller" }, _o.Graph).Single();
            Assert.Equal(SubjectKind.Role, role.Kind);
            Assert.Equal(_o.SellerRoot.Id, role.Id);

            // A business-unit copy of a role stands for the role as a whole.
            Assert.Equal(_o.SellerRoot.Id, Subjects.From(_o.SellerSales, _o.Graph).Single().Id);

            var table = Subjects.From(new TableInfo { LogicalName = "account", DisplayName = "Account" }, null).Single();
            Assert.Equal("account", table.LogicalName);
        }

        [Fact]
        public void ReportRows_AreLookedUpByName_UserTeamRoleThenTable()
        {
            var row = new UserAccessRow { User = "Bob", Team = "Reviewers", Role = "Seller" };
            var subjects = Subjects.From(row, _o.Graph);

            Assert.Equal(new[] { SubjectKind.User, SubjectKind.Team, SubjectKind.Role }, subjects.Select(s => s.Kind));
            Assert.Equal(new[] { _o.Bob.Id, _o.Reviewers.Id, _o.SellerRoot.Id }, subjects.Select(s => s.Id));

            var privilege = Subjects.From(new RolePrivilegeRow { Table = "Account", Role = "Admin" }, _o.Graph);
            Assert.Equal(new[] { SubjectKind.Role, SubjectKind.Table }, privilege.Select(s => s.Kind));
        }

        [Fact]
        public void ANameThatIsNotUnique_IsLeftOut_UnlessTheDomainTellsThemApart()
        {
            var twin = new UserInfo { Id = Guid.NewGuid(), FullName = "Bob", DomainName = "bob2@x", BusinessUnitName = "Root" };
            var graph = new SecurityGraph(new[] { _o.Bob, twin }, new RoleInfo[0], new TeamInfo[0], new BusinessUnitInfo[0],
                new UserTeamLink[0], new UserRoleLink[0], new TeamRoleLink[0]);

            Assert.Empty(Subjects.From(new UserAccessRow { User = "Bob" }, graph));
            Assert.Equal(twin.Id, Subjects.From(new TeamMemberRow { User = "Bob", Domain = "bob2@x" }, graph).Single().Id);
        }

        [Fact]
        public void WithoutTheGraph_OnlyTablesComeFromNames()
        {
            Assert.Equal(new[] { SubjectKind.Table }, Subjects.From(new RolePrivilegeRow { Table = "Account", Role = "Admin" }, null).Select(s => s.Kind));
        }

        [Fact]
        public void Matches_RecognisesTheRowForTheSubject()
        {
            var bob = Subjects.From(_o.Bob, _o.Graph).Single();
            Assert.True(Subjects.Matches(bob, _o.Bob));
            Assert.False(Subjects.Matches(bob, _o.Ada));
            Assert.True(Subjects.Matches(bob, new PrincipalRow { Id = _o.Bob.Id }));
            Assert.False(Subjects.Matches(bob, new PrincipalRow { Id = _o.Bob.Id, IsTeam = true }));

            var byName = new Subject { Kind = SubjectKind.Table, Name = "Account" };
            Assert.True(Subjects.Matches(byName, new TableInfo { LogicalName = "account", DisplayName = "Account" }));
            var byLogicalName = new Subject { Kind = SubjectKind.Table, Name = "Accounts", LogicalName = "account" };
            Assert.True(Subjects.Matches(byLogicalName, new TableInfo { LogicalName = "account", DisplayName = "Account" }));
        }

        [Fact]
        public void EveryAction_OpensAScreenThatExists()
        {
            var ids = FeatureRegistry.CreateAll().Select(f => f.Id).ToList();
            Assert.All(SubjectActions.All, a => Assert.NotNull(FeatureRegistry.Find(a.FeatureId)));
        }

        [Fact]
        public void TheScreenTheMenuIsOpenedFrom_IsLeftOut()
        {
            var actions = SubjectActions.For(SubjectKind.User, "user-access-matrix");
            Assert.DoesNotContain(actions, a => a.FeatureId == "user-access-matrix");
            Assert.Contains(actions, a => a.FeatureId == "explain-access");
        }
    }

    public class OpenInViewTests
    {
        /// <summary>Builds a screen the way the shell does (id set, commands built, activated) and hands it with its host and grids.</summary>
        private static void Run(string featureId, Action<FeatureView, TestHost, FilterableGrid[], Org> test)
        {
            Sta.Run(() =>
            {
                var org = new Org();
                var host = OrgFake.HostFor(org);
                var view = FeatureRegistry.Find(featureId).CreateView(host);
                view.FeatureId = featureId;
                var form = new Form { Size = new Size(1200, 500) };
                form.Controls.Add(view);
                form.Show();
                Application.DoEvents();
                _ = view.Commands;
                view.OnActivated();

                var grids = new List<FilterableGrid>();
                Collect(view, grids);
                test(view, host, grids.OrderBy(g => g.PointToScreen(Point.Empty).X).ToArray(), org);
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

        private static Subject User(UserInfo user) => new Subject { Kind = SubjectKind.User, Id = user.Id, Name = user.FullName };

        [Fact]
        public void ShowSubject_SelectsTheUser_AndTheScreenAnswersForThem()
        {
            Run("user-access-matrix", (view, host, grids, org) =>
            {
                view.ShowSubject(User(org.Bob));

                Assert.Equal(new[] { org.Bob.Id }, grids[0].GetSelected<UserInfo>().Select(u => u.Id));
                TeamMembersViewTests.Pump(() => grids[1].GetVisible<MatrixRow>().Any());
                Assert.Empty(host.Messages);
            });
        }

        [Fact]
        public void ShowSubject_ClearsASearchThatHidTheUser()
        {
            Run("explain-access", (view, host, grids, org) =>
            {
                var search = (TextBox)Sta.Find(grids[0], c => c is TextBox);
                search.Text = "Ada";
                Assert.Single(grids[0].GetVisible<UserInfo>());

                view.ShowSubject(User(org.Bob));

                Assert.Equal(new[] { "Bob" }, grids[0].GetSelected<UserInfo>().Select(u => u.FullName));
                Assert.Equal(string.Empty, search.Text);
            });
        }

        [Fact]
        public void ShowSubject_ForADisabledUser_IncludesDisabledUsers()
        {
            Run("compare-users", (view, host, grids, org) =>
            {
                var box = view.Commands.OfType<CheckBox>().Single(c => c.Text == "Include disabled users");
                Assert.False(box.Checked);

                view.ShowSubject(User(org.Cy));

                Assert.True(box.Checked);
                Assert.Equal(new[] { "Cy" }, grids[0].GetSelected<UserInfo>().Select(u => u.FullName));
            });
        }

        [Fact]
        public void ShowSubject_ForSomethingTheListDoesNotHold_SaysSo()
        {
            Run("assign-team-roles", (view, host, grids, org) =>
            {
                // Access teams cannot hold roles, so they are not listed on this screen.
                view.ShowSubject(new Subject { Kind = SubjectKind.Team, Id = org.Empty.Id, Name = "Empty team" });

                Assert.Empty(grids[0].GetSelected<TeamInfo>());
                Assert.Equal(new[] { "Empty team is not in the list of this screen." }, host.Messages);
            });
        }

        [Fact]
        public void ShowSubject_ForAKindTheScreenDoesNotTake_IsIgnored()
        {
            Run("team-members", (view, host, grids, org) =>
            {
                view.ShowSubject(User(org.Bob));

                Assert.Empty(grids[0].GetSelected<object>());
                Assert.Empty(host.Messages);
            });
        }

        [Fact]
        public void ShowSubject_OnRecordAccess_SwitchesTheListToTeams()
        {
            Run("record-access", (view, host, grids, org) =>
            {
                view.ShowSubject(new Subject { Kind = SubjectKind.Team, Id = org.Reviewers.Id, Name = "Reviewers" });

                var picked = grids[0].GetSelected<PrincipalRow>().Single();
                Assert.True(picked.IsTeam);
                Assert.Equal(org.Reviewers.Id, picked.Id);
            });
        }

        [Fact]
        public void ShowSubject_OnThePrivilegeFinder_SelectsTheTable()
        {
            Run("privilege-finder", (view, host, grids, org) =>
            {
                view.ShowSubject(new Subject { Kind = SubjectKind.Table, Name = "contact" });

                Assert.Equal(new[] { "contact" }, grids[0].GetSelected<TableInfo>().Select(t => t.LogicalName));
            });
        }

        [Fact]
        public void TheRowMenu_OffersTheOtherScreens_AndOpensThemOnTheRow()
        {
            Run("users-by-role", (view, host, grids, org) =>
            {
                MasterDetailHarness.Select<RoleChoice>(grids[0], r => r.Name == "Seller");
                TeamMembersViewTests.Pump(() => grids[1].GetVisible<RoleUserRow>().Any());
                var row = grids[1].GetVisible<RoleUserRow>().First(r => r.User == "Bob");

                // A row about a user and a role: one submenu each, and this screen is not offered for the role.
                var items = ((DataView)view).OpenInItems(row).OfType<ToolStripMenuItem>().ToList();
                Assert.Equal(new[] { "User: Bob", "Role: Seller" }, items.Select(i => i.Text));
                var roleActions = items[1].DropDownItems.OfType<ToolStripMenuItem>().Select(i => (string)i.Tag).ToList();
                Assert.DoesNotContain("users-by-role", roleActions);
                Assert.Contains("compare-roles", roleActions);

                items[0].DropDownItems.OfType<ToolStripMenuItem>().Single(i => (string)i.Tag == "user-access-matrix").PerformClick();
                var opened = host.Opened.Single();
                Assert.Equal("user-access-matrix", opened.Key);
                Assert.Equal(org.Bob.Id, opened.Value.Id);
            });
        }

        [Fact]
        public void TheRowMenu_EndsWithCopy_EvenWithoutAnythingToOpen()
        {
            Run("cleanup-report", (view, host, grids, org) =>
            {
                var menu = grids[0].BuildRowMenu(null, null);
                Assert.Equal(new[] { "Copy \"\"", "Copy row" }, menu.Items.OfType<ToolStripMenuItem>().Select(i => i.Text));
            });
        }

        [Fact]
        public void CopyRows_GivesTabSeparatedTextWithHeaders()
        {
            Run("user-access-matrix", (view, host, grids, org) =>
            {
                MasterDetailHarness.Select<UserInfo>(grids[0], u => u.FullName == "Bob");
                var lines = grids[0].SelectedRowsText().Split(new[] { Environment.NewLine }, StringSplitOptions.None);

                Assert.Equal("Full Name", lines[0].Split('\t')[0]);
                Assert.Equal("Bob", lines[1].Split('\t')[0]);
                Assert.Equal(2, lines.Length);
            });
        }
    }
}
