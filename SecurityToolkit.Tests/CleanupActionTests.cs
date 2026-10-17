using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using SecurityToolkit.Features;
using SecurityToolkit.Services;
using SecurityToolkit.UI;
using Xunit;

namespace SecurityToolkit.Tests
{
    public class CleanupPlanningTests
    {
        private readonly CleanupOrg _o = new CleanupOrg();

        private IEnumerable<CleanupRow> Rows(string check) => _o.Check(check).Rows;

        [Fact]
        public void TheFindingsKnowWhoAndWhichTeamTheyAreAbout()
        {
            var inTeam = Assert.Single(Rows(CleanupChecks.DisabledInTeams));
            Assert.Equal(_o.Dan.Id, inTeam.UserId);
            Assert.Equal(_o.Quiet.Id, inTeam.TeamId);
            Assert.Equal(_o.Dan.Id, Assert.Single(Rows(CleanupChecks.DisabledWithRoles)).UserId);
        }

        [Fact]
        public void DisabledUsersInTeams_AreTakenOutOfThoseTeams()
        {
            var plan = ChangePlanner.PlanCleanup(_o.Graph, Rows(CleanupChecks.DisabledInTeams));

            var item = Assert.Single(plan.Items);
            Assert.Equal("Remove from team", item.Action);
            Assert.Equal("Dan", item.Subject);
            Assert.Equal("Quiet", item.Target);
            Assert.True(plan.IsReversible);
        }

        [Fact]
        public void RolesOfDisabledUsers_AreRemoved()
        {
            var plan = ChangePlanner.PlanCleanup(_o.Graph, Rows(CleanupChecks.DisabledWithRoles));

            var item = Assert.Single(plan.Items);
            Assert.Equal("Remove role", item.Action);
            Assert.Equal("Dan", item.Subject);
            Assert.StartsWith("Used", item.Target);
            Assert.True(plan.IsReversible);
        }

        [Fact]
        public void BothKindsOfFinding_CanBePlannedTogether()
        {
            var plan = ChangePlanner.PlanCleanup(_o.Graph, Rows(CleanupChecks.DisabledInTeams).Concat(Rows(CleanupChecks.DisabledWithRoles)));

            Assert.Equal(new[] { "Remove from team", "Remove role" }, plan.Items.Select(i => i.Action).OrderBy(a => a));
        }

        [Fact]
        public void OtherFindings_PlanNothing()
        {
            var others = new[] { CleanupChecks.NoRoles, CleanupChecks.UnusedRoles, CleanupChecks.EmptyTeams, CleanupChecks.TeamsWithoutRoles }.SelectMany(Rows);

            Assert.Empty(ChangePlanner.PlanCleanup(_o.Graph, others).Items);
        }

        [Fact]
        public void AFindingForAUserWhoIsNotDisabledAnyMore_IsNotActedOn()
        {
            var stale = new CleanupRow { Check = CleanupChecks.DisabledWithRoles, UserId = _o.Ben.Id };
            var staleTeam = new CleanupRow { Check = CleanupChecks.DisabledInTeams, UserId = _o.Cat.Id, TeamId = _o.Staffed.Id };

            Assert.Empty(ChangePlanner.PlanCleanup(_o.Graph, new[] { stale, staleTeam }).Items);
        }

        [Fact]
        public void ATeamWhoseMembersAreNotManagedByHand_IsLeftAlone_AndSaysSo()
        {
            var row = new CleanupRow { Check = CleanupChecks.DisabledInTeams, UserId = _o.Dan.Id, TeamId = _o.Access.Id };

            var plan = ChangePlanner.PlanCleanup(_o.Graph, new[] { row });

            Assert.Empty(plan.Items);
            Assert.NotEmpty(plan.Notes);
        }

        [Fact]
        public void TheSameFindingTwice_IsPlannedOnce()
        {
            var row = Rows(CleanupChecks.DisabledInTeams).Single();

            Assert.Single(ChangePlanner.PlanCleanup(_o.Graph, new[] { row, row }).Items);
        }
    }

    public class CleanupReportActionTests
    {
        private static void Run(Action<FeatureView, TestHost, FakeOrganizationService, FilterableGrid, CleanupOrg> test)
        {
            Sta.Run(() =>
            {
                var org = new CleanupOrg();
                var service = OrgFake.Serve(org.Graph);
                service.OnExecute = request =>
                {
                    var response = new ExecuteMultipleResponse();
                    response.Results["Responses"] = new ExecuteMultipleResponseItemCollection();
                    return response;
                };
                var host = new TestHost { Security = new SecurityService(service) };
                var view = new CleanupReportFeature().CreateView(host);
                var form = new Form { Size = new Size(1200, 500) };
                form.Controls.Add(view);
                form.Show();
                Application.DoEvents();
                _ = view.Commands;
                view.OnActivated();

                var grids = new List<FilterableGrid>();
                Collect(view, grids);
                var ordered = grids.OrderBy(g => g.PointToScreen(Point.Empty).X).ToArray();
                test(view, host, service, ordered[1], org);
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

        private static void Click(FeatureView view, string text)
        {
            var button = view.Commands.OfType<CommandButton>().First(b => b.Text == text);
            typeof(Control).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(button, new object[] { EventArgs.Empty });
        }

        private static List<OrganizationRequest> Sent(FakeOrganizationService service)
        {
            return service.ExecutedRequests.OfType<ExecuteMultipleRequest>().SelectMany(m => m.Requests).ToList();
        }

        [Fact]
        public void RemovingDisabledUsersFromTeams_ShowsTheReviewFirst_ThenSendsOnlyThat()
        {
            Run((view, host, service, findings, org) =>
            {
                Click(view, "Remove disabled users from teams");

                var plan = Assert.Single(host.Ui.Reviewed);
                var item = Assert.Single(plan.Items);
                Assert.Equal("Remove from team", item.Action);
                Assert.Single(Sent(service)); // approved by the scripted dialog
            });
        }

        [Fact]
        public void NothingIsSent_WhenTheReviewIsRefused()
        {
            Run((view, host, service, findings, org) =>
            {
                host.Ui.Approve = false;

                Click(view, "Remove roles from disabled users");

                Assert.Single(host.Ui.Reviewed);
                Assert.Empty(Sent(service));
            });
        }

        [Fact]
        public void RowsSelectedOnTheRight_NarrowTheAction()
        {
            Run((view, host, service, findings, org) =>
            {
                // Only the "roles" finding is selected, so taking users out of teams has nothing to act on of its own.
                MasterDetailHarness.Select<CleanupRow>(findings, r => r.Check == CleanupChecks.DisabledWithRoles);

                Click(view, "Remove disabled users from teams");

                // A selection of another check's rows does not narrow this one: it falls back to all of its findings.
                Assert.Equal("Remove from team", Assert.Single(Assert.Single(host.Ui.Reviewed).Items).Action);
            });
        }

        [Fact]
        public void WhenThereIsNothingToClean_ItSaysSoInsteadOfOpeningAReview()
        {
            Sta.Run(() =>
            {
                var org = new Org();
                var host = new TestHost { Security = new SecurityService(OrgFake.Serve(org.Graph)) };
                var view = new CleanupReportFeature().CreateView(host);
                var form = new Form { Size = new Size(1200, 500) };
                form.Controls.Add(view);
                form.Show();
                Application.DoEvents();
                _ = view.Commands;
                view.OnActivated();

                Click(view, "Remove roles from disabled users");

                Assert.Empty(host.Ui.Reviewed);
                Assert.NotEmpty(host.Ui.Told);
                form.Dispose();
            });
        }
    }
}
