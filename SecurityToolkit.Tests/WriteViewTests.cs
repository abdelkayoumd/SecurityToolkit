using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using SecurityToolkit.Features;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using SecurityToolkit.UI;
using Xunit;

namespace SecurityToolkit.Tests
{
    /// <summary>Runs a write feature against the write-test organization, with scripted dialogs and a recording service.</summary>
    internal static class WriteHarness
    {
        internal sealed class Context
        {
            public FeatureView View;
            public FilterableGrid Left, Right;
            public WriteOrg Org;
            public TestHost Host;
            public FakeOrganizationService Service;

            public ScriptedUi Ui => Host.Ui;

            public void Click(string commandText)
            {
                var button = View.Commands.OfType<CommandButton>().First(b => b.Text == commandText);
                typeof(Control).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(button, new object[] { EventArgs.Empty });
            }

            public void SelectLeft<T>(Func<T, bool> pick) { Select(Left, pick); }
            public void SelectRight(Func<PickRow, bool> pick) { Select(Right, pick); }

            public CheckBox Check(string text) => View.Commands.OfType<CheckBox>().First(c => c.Text == text);

            /// <summary>Every request that actually went to Dataverse (unpacked from the ExecuteMultiple batches).</summary>
            public List<OrganizationRequest> Sent => Service.ExecutedRequests.OfType<ExecuteMultipleRequest>().SelectMany(m => m.Requests).ToList();

            private static void Select<T>(FilterableGrid grid, Func<T, bool> pick)
            {
                var data = (DataGridView)Sta.Find(grid, c => c is DataGridView);
                foreach (DataGridViewRow row in data.Rows)
                {
                    row.Selected = pick((T)row.DataBoundItem);
                }
            }
        }

        public static void Run(IFeature feature, Action<Context> test)
        {
            Sta.Run(() =>
            {
                var org = new WriteOrg();
                var service = OrgFake.Serve(org.Graph);
                service.OnExecute = request =>
                {
                    var response = new ExecuteMultipleResponse();
                    response.Results["Responses"] = new ExecuteMultipleResponseItemCollection();
                    return response;
                };
                var host = new TestHost { Security = new SecurityService(service) };
                var view = feature.CreateView(host);
                var form = new Form { Size = new Size(1300, 520) };
                form.Controls.Add(view);
                form.Show();
                Application.DoEvents();
                _ = view.Commands;
                view.OnActivated();

                var grids = new List<FilterableGrid>();
                Collect(view, grids);
                var ordered = grids.OrderBy(g => g.PointToScreen(Point.Empty).X).ToArray();
                test(new Context { View = view, Left = ordered[0], Right = ordered[1], Org = org, Host = host, Service = service });
                form.Dispose();
            });
        }

        private static void Collect(Control root, List<FilterableGrid> found)
        {
            foreach (Control child in root.Controls)
            {
                if (child is FilterableGrid grid) { found.Add(grid); } else { Collect(child, found); }
            }
        }
    }

    public class UserRolesViewTests
    {
        private static void Run(Action<WriteHarness.Context> test) { WriteHarness.Run(new UserRolesFeature(), test); }

        [Fact]
        public void ListsActiveUsersOnTheLeftAndEachRoleOnceOnTheRight()
        {
            Run(c =>
            {
                Assert.Equal(new[] { "Ada", "Bob", "Cy", "Dee" }, c.Left.GetVisible<UserInfo>().Select(u => u.FullName));
                Assert.Equal(new[] { "Admin", "Auditor", "Seller" }, c.Right.GetVisible<PickRow>().Select(r => r.Name));
            });
        }

        [Fact]
        public void TheRightListShowsHowManyOfTheSelectedUsersHaveEachRole()
        {
            Run(c =>
            {
                c.SelectLeft<UserInfo>(u => u.FullName == "Ada" || u.FullName == "Bob");
                TeamMembersViewTests.Pump(() => c.Right.GetVisible<PickRow>().Any(r => r.Assigned.Length > 0));

                var assigned = c.Right.GetVisible<PickRow>().ToDictionary(r => r.Name, r => r.Assigned);
                Assert.Equal("1 of 2", assigned["Admin"]);
                Assert.Equal("1 of 2", assigned["Seller"]);
                Assert.Equal("0 of 2", assigned["Auditor"]);
            });
        }

        [Fact]
        public void AddingARole_ShowsTheReview_ThenSendsTheAssociation()
        {
            Run(c =>
            {
                c.SelectLeft<UserInfo>(u => u.FullName == "Dee");
                c.SelectRight(r => r.Name == "Auditor");

                c.Click("Add roles");

                var item = Assert.Single(Assert.Single(c.Ui.Reviewed).Items);
                Assert.Equal("Add role", item.Action);
                var sent = Assert.IsType<AssociateRequest>(Assert.Single(c.Sent));
                Assert.Equal(c.Org.Dee.Id, sent.Target.Id);
                Assert.Equal(c.Org.AuditorRoot.Id, sent.RelatedEntities[0].Id);
                Assert.Equal("systemuserroles_association", sent.Relationship.SchemaName);
            });
        }

        [Fact]
        public void RemovingARole_SendsTheDisassociationOfTheCopyTheUserHolds()
        {
            Run(c =>
            {
                c.SelectLeft<UserInfo>(u => u.FullName == "Bob");
                c.SelectRight(r => r.Name == "Seller");

                c.Click("Remove roles");

                var sent = Assert.IsType<DisassociateRequest>(Assert.Single(c.Sent));
                Assert.Equal(c.Org.SellerSales.Id, sent.RelatedEntities[0].Id);
            });
        }

        [Fact]
        public void NothingIsSent_WhenTheUserDoesNotApproveTheReview()
        {
            Run(c =>
            {
                c.Ui.Approve = false;
                c.SelectLeft<UserInfo>(u => u.FullName == "Dee");
                c.SelectRight(r => r.Name == "Auditor");

                c.Click("Add roles");

                Assert.Single(c.Ui.Reviewed);
                Assert.Empty(c.Sent);
            });
        }

        [Fact]
        public void WithoutASelectionOnBothSides_ItAsksForOneAndSendsNothing()
        {
            Run(c =>
            {
                c.SelectLeft<UserInfo>(u => u.FullName == "Dee"); // no role chosen

                c.Click("Add roles");

                Assert.Contains("Select at least one user", c.Ui.Told.Single());
                Assert.Empty(c.Ui.Reviewed);
                Assert.Empty(c.Sent);
            });
        }

        [Fact]
        public void ARoleThatTheUsersBusinessUnitLacks_IsReportedNotSent()
        {
            Run(c =>
            {
                c.SelectLeft<UserInfo>(u => u.FullName == "Cy"); // Sales has no Auditor copy
                c.SelectRight(r => r.Name == "Auditor");

                c.Click("Add roles");

                Assert.Contains("nothing to change", c.Ui.Told.Single());
                Assert.Contains("no copy in business unit \"Sales\"", c.Ui.Told.Single());
                Assert.Empty(c.Sent);
            });
        }

        [Fact]
        public void AfterApplying_TheListsReloadAndTheUndoRestoresTheState()
        {
            Run(c =>
            {
                c.Ui.UndoAnswers.Enqueue(true);
                c.SelectLeft<UserInfo>(u => u.FullName == "Dee");
                c.SelectRight(r => r.Name == "Admin");

                c.Click("Add roles");

                Assert.Equal(2, c.Ui.Reviewed.Count);
                Assert.IsType<AssociateRequest>(c.Sent[0]);
                Assert.IsType<DisassociateRequest>(c.Sent[1]);
            });
        }
    }

    public class TeamRolesViewTests
    {
        private static void Run(Action<WriteHarness.Context> test) { WriteHarness.Run(new TeamRolesFeature(), test); }

        [Fact]
        public void AccessTeams_AreNotOfferedBecauseTheyCannotHoldRoles()
        {
            Run(c =>
            {
                Assert.Equal(new[] { "Entra group", "Reviewers", "Root" }, c.Left.GetVisible<TeamInfo>().Select(t => t.Name).OrderBy(n => n));
            });
        }

        [Fact]
        public void AddingARoleToATeam_SendsTheTeamRoleAssociation()
        {
            Run(c =>
            {
                c.SelectLeft<TeamInfo>(t => t.Name == "Reviewers");
                c.SelectRight(r => r.Name == "Admin");

                c.Click("Add roles");

                var sent = Assert.IsType<AssociateRequest>(Assert.Single(c.Sent));
                Assert.Equal("team", sent.Target.LogicalName);
                Assert.Equal("teamroles_association", sent.Relationship.SchemaName);
                Assert.Equal(c.Org.AdminRoot.Id, sent.RelatedEntities[0].Id);
            });
        }
    }

    public class TeamMembershipViewTests
    {
        private static void Run(Action<WriteHarness.Context> test) { WriteHarness.Run(new TeamMembershipFeature(), test); }

        [Fact]
        public void OnlyTeamsWithHandManagedMembersAreOffered()
        {
            Run(c =>
            {
                Assert.Equal(new[] { "Reviewers" }, c.Right.GetVisible<PickRow>().Select(r => r.Name));
            });
        }

        [Fact]
        public void TheRightListShowsHowManySelectedUsersAreMembers()
        {
            Run(c =>
            {
                c.SelectLeft<UserInfo>(u => u.FullName == "Bob" || u.FullName == "Dee");
                TeamMembersViewTests.Pump(() => c.Right.GetVisible<PickRow>().Any(r => r.Assigned.Length > 0));

                Assert.Equal("1 of 2", c.Right.GetVisible<PickRow>().Single().Assigned);
            });
        }

        [Fact]
        public void AddingUsersToATeam_SkipsThoseAlreadyInIt()
        {
            Run(c =>
            {
                c.SelectLeft<UserInfo>(u => u.FullName == "Bob" || u.FullName == "Dee");
                c.SelectRight(r => r.Name == "Reviewers");

                c.Click("Add to teams");

                var sent = Assert.IsType<AssociateRequest>(Assert.Single(c.Sent));
                Assert.Equal("teammembership_association", sent.Relationship.SchemaName);
                Assert.Equal(c.Org.Dee.Id, sent.RelatedEntities[0].Id);
            });
        }

        [Fact]
        public void RemovingUsersFromATeam_DisassociatesTheMembers()
        {
            Run(c =>
            {
                c.SelectLeft<UserInfo>(u => u.FullName == "Bob" || u.FullName == "Cy");
                c.SelectRight(r => r.Name == "Reviewers");

                c.Click("Remove from teams");

                Assert.Equal(2, c.Sent.OfType<DisassociateRequest>().Count());
            });
        }
    }

    public class CloneUserViewTests
    {
        private static void Run(Action<WriteHarness.Context> test) { WriteHarness.Run(new CloneUserFeature(), test); }

        [Fact]
        public void CloningRolesAndTeams_ReviewsThenSendsOneChangePerMissingItem()
        {
            Run(c =>
            {
                c.SelectLeft<UserInfo>(u => u.FullName == "Bob");
                SelectTarget(c, "Dee");

                c.Click("Clone profile");

                var plan = Assert.Single(c.Ui.Reviewed);
                Assert.Equal(new[] { "Add role:Seller (Root)", "Add to team:Reviewers" }, plan.Items.Select(i => i.Action + ":" + i.Target));
                Assert.Equal(2, c.Sent.Count);
            });
        }

        private static void SelectTarget(WriteHarness.Context c, string name)
        {
            var data = (DataGridView)Sta.Find(c.Right, x => x is DataGridView);
            foreach (DataGridViewRow row in data.Rows)
            {
                row.Selected = ((UserInfo)row.DataBoundItem).FullName == name;
            }
        }

        [Fact]
        public void TheBusinessUnitIsOnlyCopiedWhenTicked_AndTheReviewWarnsThatItCannotBeUndone()
        {
            Run(c =>
            {
                c.SelectLeft<UserInfo>(u => u.FullName == "Bob");
                SelectTarget(c, "Ada");

                c.Click("Clone profile");
                Assert.DoesNotContain(c.Ui.Reviewed[0].Items, i => i.Action == "Change business unit");

                // Applying reloaded the lists, which clears the selection; choose again.
                c.Ui.Reviewed.Clear();
                c.SelectLeft<UserInfo>(u => u.FullName == "Bob");
                SelectTarget(c, "Ada");
                c.Check("Business unit").Checked = true;
                c.Ui.Approve = false; // look at the review, but do not apply it

                c.Click("Clone profile");

                var plan = Assert.Single(c.Ui.Reviewed);
                Assert.Equal("Change business unit", plan.Items[0].Action);
                Assert.False(plan.IsReversible);
                Assert.Contains(plan.Notes, n => n.Contains("cannot be undone"));
            });
        }

        [Fact]
        public void WithoutExactlyOneSource_ItSaysSoAndChangesNothing()
        {
            Run(c =>
            {
                SelectTarget(c, "Dee"); // no source selected

                c.Click("Clone profile");

                Assert.Contains("exactly one user", c.Ui.Told.Single());
                Assert.Empty(c.Ui.Reviewed);
            });
        }

        [Fact]
        public void WithNothingTickedToCopy_ItSaysSo()
        {
            Run(c =>
            {
                c.SelectLeft<UserInfo>(u => u.FullName == "Bob");
                SelectTarget(c, "Dee");
                c.Check("Security roles").Checked = false;
                c.Check("Teams").Checked = false;

                c.Click("Clone profile");

                Assert.Contains("Tick at least one", c.Ui.Told.Single());
                Assert.Empty(c.Sent);
            });
        }

        [Fact]
        public void TheSourceUserOnItsOwn_IsNotATarget()
        {
            Run(c =>
            {
                c.SelectLeft<UserInfo>(u => u.FullName == "Bob");
                SelectTarget(c, "Bob");

                c.Click("Clone profile");

                Assert.Contains("at least one other user", c.Ui.Told.Single());
            });
        }
    }
}
