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
    public class WorkspaceRegistryTests
    {
        private static readonly string[] Tabs = { "user-access-matrix", "explain-access", "record-access" };

        [Fact]
        public void TheMenu_HasOneEntryForAUsersAccess_InsteadOfThree()
        {
            var menu = FeatureRegistry.CreateAll().Select(f => f.Id).ToList();

            Assert.Contains("user-access", menu);
            Assert.All(Tabs, id => Assert.DoesNotContain(id, menu));
        }

        [Fact]
        public void TheOldScreens_AreTheTabs_InTheOrderWhatWhyThisRecord()
        {
            var workspace = (IWorkspaceFeature)FeatureRegistry.Find("user-access");

            Assert.Equal(Tabs, workspace.Parts.Select(p => p.Id));
            Assert.Equal(new[] { "What can they do?", "Why do they have access?", "Can they open this record?" }, UserAccessFeature.Tabs.Select(t => t.Question));
            Assert.All(UserAccessFeature.Tabs, t => Assert.False(string.IsNullOrWhiteSpace(t.Hint)));
        }

        [Fact]
        public void EveryOldId_StillFindsItsScreen_AndLeadsToTheWorkspace()
        {
            foreach (var id in Tabs)
            {
                Assert.Equal(id, FeatureRegistry.Find(id).Id);
                Assert.Equal("user-access", FeatureRegistry.MenuIdOf(id));
            }

            Assert.Equal("privilege-finder", FeatureRegistry.MenuIdOf("privilege-finder"));
            Assert.Null(FeatureRegistry.Find("nope"));
        }
    }

    public class WorkspaceViewTests
    {
        private static void Run(Action<WorkspaceView, TestHost, Org> test)
        {
            Sta.Run(() =>
            {
                var org = new Org();
                var host = OrgFake.HostFor(org);
                var view = (WorkspaceView)FeatureRegistry.Find("user-access").CreateView(host);
                view.FeatureId = "user-access";
                var form = new Form { Size = new Size(1200, 600) };
                form.Controls.Add(view);
                form.Show();
                Application.DoEvents();
                _ = view.Commands;
                view.OnActivated();
                test(view, host, org);
                form.Dispose();
            });
        }

        private static FilterableGrid[] Grids(Control root)
        {
            var found = new List<FilterableGrid>();
            void Collect(Control parent)
            {
                foreach (Control child in parent.Controls)
                {
                    if (child is FilterableGrid grid) { found.Add(grid); } else { Collect(child); }
                }
            }

            Collect(root);
            return found.OrderBy(g => g.PointToScreen(Point.Empty).X).ToArray();
        }

        [Fact]
        public void ItOpensOnWhatTheyCanDo_WithThreeTabs()
        {
            Run((view, host, org) =>
            {
                Assert.Equal(3, view.Tabs.Count);
                Assert.Equal("user-access-matrix", view.ScreenId);
                Assert.Equal(new[] { "user-access-matrix", "explain-access", "record-access" }, view.Parts.Select(p => p.FeatureId));
            });
        }

        [Fact]
        public void TheCommandBar_FollowsTheTabInFront()
        {
            Run((view, host, org) =>
            {
                var changes = 0;
                view.CommandsChanged += (s, e) => changes++;
                Assert.Contains(view.Commands.OfType<CheckBox>(), c => c.Text == "Include tables with no access");

                view.ShowPart("record-access");

                Assert.Equal(1, changes);
                Assert.Equal("record-access", view.ScreenId);
                Assert.Contains(view.Commands.OfType<ComboBox>(), c => c.Name == "PrincipalType");
                Assert.DoesNotContain(view.Commands.OfType<CheckBox>(), c => c.Text == "Include tables with no access");
            });
        }

        [Fact]
        public void ShowPart_ForAScreenThatIsNotATab_ChangesNothing()
        {
            Run((view, host, org) =>
            {
                view.ShowPart("privilege-finder");
                Assert.Equal("user-access-matrix", view.ScreenId);
            });
        }

        [Fact]
        public void TheSelectedUser_FollowsToTheNextTab()
        {
            Run((view, host, org) =>
            {
                MasterDetailHarness.Select<UserInfo>(Grids(view.Active)[0], u => u.FullName == "Bob");
                Assert.Equal(org.Bob.Id, view.CurrentSubject.Id);

                view.ShowPart("explain-access");

                Assert.Equal(new[] { "Bob" }, Grids(view.Active)[0].GetSelected<UserInfo>().Select(u => u.FullName));
                Assert.Empty(host.Messages);
            });
        }

        [Fact]
        public void ATeam_DoesNotFollowToATabThatOnlyListsUsers()
        {
            Run((view, host, org) =>
            {
                view.ShowPart("record-access");
                view.ShowSubject(new Subject { Kind = SubjectKind.Team, Id = org.Reviewers.Id, Name = "Reviewers" });
                Assert.Equal(org.Reviewers.Id, view.CurrentSubject.Id);

                view.ShowPart("user-access-matrix");

                Assert.Empty(Grids(view.Active)[0].GetSelected<object>());
                Assert.Empty(host.Messages);
            });
        }

        [Fact]
        public void ShowSubject_GoesToTheTabInFront()
        {
            Run((view, host, org) =>
            {
                view.ShowPart("explain-access");
                view.ShowSubject(new Subject { Kind = SubjectKind.User, Id = org.Bob.Id, Name = "Bob" });

                Assert.Equal(new[] { "Bob" }, Grids(view.Active)[0].GetSelected<UserInfo>().Select(u => u.FullName));
            });
        }

        [Fact]
        public void TheRowMenu_OfferTheOtherTabs_ButNotTheOneInFront()
        {
            Run((view, host, org) =>
            {
                var matrix = (DataView)view.Active;
                var items = matrix.OpenInItems(org.Bob).OfType<ToolStripMenuItem>().Select(i => (string)i.Tag).ToList();

                Assert.DoesNotContain("user-access-matrix", items);
                Assert.Contains("explain-access", items);
                Assert.Contains("record-access", items);
            });
        }

        [Fact]
        public void ClickingAQuestionCard_OpensItsTab()
        {
            Run((view, host, org) =>
            {
                var cards = (QuestionTabs)Sta.Find(view, c => c is QuestionTabs);
                var third = cards.CardBounds(2);

                Assert.Equal(2, cards.TabAt(new Point(third.Left + third.Width / 2, third.Top + third.Height / 2)));
                Assert.Equal(-1, cards.TabAt(new Point(cards.CardBounds(1).Right + 3, third.Top + 5))); // the gap between cards
                cards.SelectedIndex = 2;

                Assert.Equal("record-access", view.ScreenId);
            });
        }
    }

    public class QuestionTabsTests
    {
        [Fact]
        public void TheCards_ShareTheWidth_AndPaint_EvenWhenNarrow()
        {
            Sta.Run(() =>
            {
                var tabs = new QuestionTabs { Width = 900 };
                foreach (var tab in UserAccessFeature.Tabs)
                {
                    tabs.AddTab(tab);
                }

                Assert.Equal(tabs.CardBounds(0).Width, tabs.CardBounds(2).Width);
                Assert.True(tabs.CardBounds(2).Right <= tabs.Width);

                foreach (var width in new[] { 900, 240, 30 })
                {
                    tabs.Width = width;
                    using (var bitmap = new Bitmap(Math.Max(1, tabs.Width), tabs.Height))
                    {
                        tabs.DrawToBitmap(bitmap, new Rectangle(Point.Empty, tabs.Size));
                    }
                }
            });
        }
    }
}

namespace SecurityToolkit.Tests
{
    public class CompareWorkspaceTests
    {
        [Fact]
        public void TheMenu_HasOneCompareEntry_WithTwoUsersAndTwoRolesAsTabs()
        {
            var menu = FeatureRegistry.CreateAll().Select(f => f.Id).ToList();
            var compare = (IWorkspaceFeature)FeatureRegistry.Find("compare");

            Assert.Contains("compare", menu);
            Assert.DoesNotContain("compare-users", menu);
            Assert.DoesNotContain("compare-roles", menu);
            Assert.Equal(new[] { "compare-users", "compare-roles" }, compare.Parts.Select(p => p.Id));
            Assert.Equal(new[] { "How do two users differ?", "How do two roles differ?" }, CompareFeature.Tabs.Select(t => t.Question));
            Assert.Equal("compare", FeatureRegistry.MenuIdOf("compare-users"));
            Assert.Equal("compare", FeatureRegistry.MenuIdOf("compare-roles"));
        }

        [Fact]
        public void ARole_OpensOnTheTwoRolesTab_AndAUserDoesNotFollowThere()
        {
            Sta.Run(() =>
            {
                var org = new Org();
                var host = OrgFake.HostFor(org);
                var view = (WorkspaceView)FeatureRegistry.Find("compare").CreateView(host);
                view.FeatureId = "compare";
                var form = new Form { Size = new Size(1200, 600) };
                form.Controls.Add(view);
                form.Show();
                Application.DoEvents();
                _ = view.Commands;
                view.OnActivated();

                Assert.Equal("compare-users", view.ScreenId);
                view.ShowSubject(new Subject { Kind = SubjectKind.User, Id = org.Bob.Id, Name = "Bob" });

                view.ShowPart("compare-roles");

                Assert.Equal("compare-roles", view.ScreenId);
                Assert.Empty(host.Messages); // a user is not offered to the roles list, and nothing complains
                Assert.Contains(view.Commands.OfType<CheckBox>(), c => c.Text == "Differences only");
                form.Dispose();
            });
        }
    }
}

namespace SecurityToolkit.Tests
{
    public class AssignWorkspaceTests
    {
        private static readonly string[] Tabs = { "assign-user-roles", "assign-team-roles", "assign-team-membership" };

        [Fact]
        public void TheMenu_HasOneAssignEntry_WithTheThreeChangeScreensAsTabs()
        {
            var menu = FeatureRegistry.CreateAll().Select(f => f.Id).ToList();
            var assign = (IWorkspaceFeature)FeatureRegistry.Find("assign");

            Assert.Contains("assign", menu);
            Assert.All(Tabs, id => Assert.DoesNotContain(id, menu));
            Assert.Equal(Tabs, assign.Parts.Select(p => p.Id));
            Assert.Equal(FeatureGroup.Change, assign.Group);
            Assert.All(assign.Parts, p => Assert.Equal(FeatureGroup.Change, p.Group));
            Assert.All(Tabs, id => Assert.Equal("assign", FeatureRegistry.MenuIdOf(id)));
            Assert.Equal(3, AssignFeature.Tabs.Length);
            Assert.Contains("clone-user", menu); // cloning stays a screen of its own
        }

        [Fact]
        public void ATeam_OpensOnRolesForTeams_WithItsOwnCommands()
        {
            Sta.Run(() =>
            {
                var org = new Org();
                var host = OrgFake.HostFor(org);
                var view = (WorkspaceView)FeatureRegistry.Find("assign").CreateView(host);
                view.FeatureId = "assign";
                var form = new Form { Size = new Size(1200, 600) };
                form.Controls.Add(view);
                form.Show();
                Application.DoEvents();
                _ = view.Commands;
                view.OnActivated();

                view.ShowPart("assign-team-roles");
                view.ShowSubject(new Subject { Kind = SubjectKind.Team, Id = org.Reviewers.Id, Name = "Reviewers" });

                Assert.Equal("assign-team-roles", view.ScreenId);
                Assert.Equal(org.Reviewers.Id, view.CurrentSubject.Id);
                Assert.Contains(view.Commands.OfType<CommandButton>(), c => c.Text == "Add roles");
                Assert.Empty(host.Messages);
                form.Dispose();
            });
        }
    }
}
