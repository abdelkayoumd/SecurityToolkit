using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Features;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using SecurityToolkit.UI;
using Xunit;

namespace SecurityToolkit.Tests
{
    public class AccessPreviewTests
    {
        /// <summary>A plan that gives (or takes) one role record to (or from) one user, as the planners build it.</summary>
        private static ChangePlan UserRole(UserInfo user, RoleInfo role, bool add)
        {
            var plan = new ChangePlan();
            var request = add
                ? ChangeExecutor.Associate("systemuser", user.Id, ChangePlanner.UserRoleRelationship, "role", role.Id)
                : ChangeExecutor.Disassociate("systemuser", user.Id, ChangePlanner.UserRoleRelationship, "role", role.Id);
            plan.Items.Add(new ChangeItem(add ? "Add role" : "Remove role", user.FullName, role.Name, request));
            return plan;
        }

        private static (System.Collections.Generic.IReadOnlyList<RoleSwap> Swaps, System.Collections.Generic.IReadOnlyList<AccessChangeRow> Rows) Preview(Org org, ChangePlan plan)
        {
            var security = new SecurityService(OrgFake.For(org));
            var swaps = AccessPreview.Simulate(org.Graph, plan.Items);
            return (swaps, AccessPreview.Compare(swaps, security.GetPrivilegeCatalog(), security.GetRolePrivileges(AccessPreview.RoleIdsNeeded(swaps)), security.GetTables()));
        }

        [Fact]
        public void GivingARole_ShowsEveryPrivilegeTheUserGains()
        {
            var org = new Org();
            var plan = UserRole(org.Dee, org.SellerSales, true);

            var (swaps, rows) = Preview(org, plan);

            var dee = Assert.Single(swaps);
            Assert.Equal("Dee", dee.User.FullName);
            Assert.Empty(dee.Before);
            Assert.Equal(16, rows.Count); // eight privileges on account and contact
            Assert.All(rows, r => Assert.Equal("Gains", r.Change));
            Assert.All(rows, r => Assert.Equal("None", r.Before));
            Assert.All(rows, r => Assert.Equal("User", r.After));
        }

        [Fact]
        public void TakingARoleAway_ShowsTheLosses()
        {
            var org = new Org();
            var plan = UserRole(org.Ada, org.Admin, false);

            var (_, rows) = Preview(org, plan);

            Assert.Equal(16, rows.Count);
            Assert.All(rows, r => Assert.True(r.IsLoss));
            Assert.All(rows, r => Assert.Equal("Organization", r.Before));
        }

        [Fact]
        public void JoiningATeam_GivesTheTeamsRoles()
        {
            var org = new Org();
            var plan = ChangePlanner.PlanMembership(org.Graph, new[] { org.Dee }, new[] { org.Reviewers }, true);

            var (_, rows) = Preview(org, plan);

            Assert.Equal(16, rows.Count);
            Assert.All(rows, r => Assert.Equal("Business Unit", r.After));
        }

        [Fact]
        public void ARoleTakenFromATeam_ChangesEveryMember_AndAWiderReachThatShrinksIsNarrower()
        {
            var org = new Org();
            var plan = ChangePlanner.PlanTeamRoles(org.Graph, new[] { org.Reviewers }, new[] { new RoleChoice { RootKey = org.Auditor.Id, Name = "Auditor", Copies = 1 } }, false);

            var (swaps, rows) = Preview(org, plan);

            Assert.Equal(new[] { "Bob", "Cy" }, swaps.Select(s => s.User.FullName));
            Assert.All(rows.Where(r => r.User == "Bob"), r => Assert.Equal("Narrower", r.Change)); // Bob keeps Seller, at User level
            Assert.All(rows.Where(r => r.User == "Cy"), r => Assert.Equal("Loses", r.Change));
            Assert.Equal("Bob", rows[0].User);
        }

        [Fact]
        public void AChangeThatAltersNothing_SaysSo()
        {
            Assert.Equal("What this user can do on tables stays the same.", AccessPreview.Summarize(new AccessChangeRow[0], 1));
            Assert.Equal(string.Empty, AccessPreview.Summarize(new AccessChangeRow[0], 0));
        }

        [Fact]
        public void TheReview_ReceivesThePreview_BeforeAnythingIsApplied()
        {
            var org = new Org();
            var host = OrgFake.HostFor(org);
            host.Security.GetGraph(); // as the screen that planned the change has
            host.Ui.Approve = false;
            var plan = UserRole(org.Dee, org.SellerSales, true);

            new PlanRunner(host, host.Ui).Run(plan, null);

            var reviewed = Assert.Single(host.Ui.Reviewed);
            Assert.Equal(16, reviewed.Impact.Count);
            Assert.StartsWith("1 user(s) affected: 16 gain(s)", reviewed.ImpactSummary);
        }

        [Fact]
        public void TheReviewDialog_HasATabForWhatChangesForEachUser()
        {
            Sta.Run(() =>
            {
                var org = new Org();
                var plan = UserRole(org.Ada, org.Admin, false);
                plan.Impact = Preview(org, plan).Rows;
                plan.ImpactSummary = AccessPreview.Summarize(plan.Impact, 1);

                using (var dialog = new ReviewDialog(plan))
                {
                    var tabs = (TabStrip)Sta.Find(dialog, c => c.Name == "ReviewTabs");
                    Assert.Equal(new[] { "Changes (1)", "What changes for each user (16)" }, tabs.Tabs);
                    var impact = (ListView)Sta.Find(dialog, c => c.Name == "Impact");
                    Assert.Equal(16, impact.Items.Count);
                    Assert.Equal(Theme.Danger, impact.Items[0].ForeColor);
                }

                var plain = new ChangePlan();
                plain.Items.Add(plan.Items[0]);
                using (var dialog = new ReviewDialog(plain))
                {
                    Assert.Null(Sta.Find(dialog, c => c.Name == "ReviewTabs"));
                }
            });
        }
    }
}
