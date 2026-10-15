using System;
using System.Linq;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using Xunit;

namespace SecurityToolkit.Tests
{
    public class CompareUsersTests
    {
        private readonly Org _o = new Org();

        [Fact]
        public void BusinessUnitComesFirst_ThenRolesThenTeams_EachByName()
        {
            var rows = _o.Graph.CompareUsers(_o.Ada, _o.Bob);

            Assert.Equal("Business unit", rows[0].Category);
            Assert.Equal(new[] { "Security role", "Security role", "Security role" }, rows.Skip(1).Take(3).Select(r => r.Category));
            Assert.Equal(new[] { "Admin", "Auditor", "Seller" }, rows.Where(r => r.Category == "Security role").Select(r => r.Item));
            Assert.Equal(new[] { "Empty team", "Reviewers" }, rows.Where(r => r.Category == "Team").Select(r => r.Item));
            Assert.Equal("Team", rows.Last().Category);
        }

        [Fact]
        public void DifferentBusinessUnits_AreReportedAsDiffers()
        {
            var row = _o.Graph.CompareUsers(_o.Ada, _o.Bob).First();

            Assert.Equal("Root", row.A);
            Assert.Equal("Sales", row.B);
            Assert.Equal("Differs", row.Result);
            Assert.True(row.IsDifference);
        }

        [Fact]
        public void SameBusinessUnit_IsReportedAsSame()
        {
            var row = _o.Graph.CompareUsers(_o.Bob, _o.Dee).First(); // both in Sales

            Assert.Equal("Same", row.Result);
            Assert.False(row.IsDifference);
        }

        [Fact]
        public void ARoleOnlyOneUserHas_IsMarkedOnlyThatUser_WithItsSource()
        {
            var admin = _o.Graph.CompareUsers(_o.Ada, _o.Bob).Single(r => r.Item == "Admin");

            Assert.Equal("Direct", admin.A);
            Assert.Equal(string.Empty, admin.B);
            Assert.Equal("Only Ada", admin.Result);
            Assert.True(admin.IsDifference);
        }

        [Fact]
        public void ARoleReachedThroughATeam_ShowsWhichTeam_AndCountsLikeADirectOne()
        {
            var auditor = _o.Graph.CompareUsers(_o.Ada, _o.Bob).Single(r => r.Item == "Auditor");

            Assert.Equal("via Reviewers", auditor.B);
            Assert.Equal("Only Bob", auditor.Result);
        }

        [Fact]
        public void TheSameRoleHeldTwoWays_IsInBoth_AndEachSideShowsItsOwnSource()
        {
            // Bob has Auditor through Reviewers; give Ada the same role directly.
            var graph = new SecurityGraph(
                _o.Graph.Users, _o.Graph.Roles, _o.Graph.Teams, _o.Graph.BusinessUnits,
                new[] { new UserTeamLink { UserId = _o.Bob.Id, TeamId = _o.Reviewers.Id } },
                new[] { new UserRoleLink { UserId = _o.Ada.Id, RoleId = _o.Auditor.Id } },
                new[] { new TeamRoleLink { TeamId = _o.Reviewers.Id, RoleId = _o.Auditor.Id } });

            var auditor = graph.CompareUsers(_o.Ada, _o.Bob).Single(r => r.Item == "Auditor");

            Assert.Equal("Direct", auditor.A);
            Assert.Equal("via Reviewers", auditor.B);
            Assert.Equal("Both", auditor.Result);
            Assert.False(auditor.IsDifference);
        }

        [Fact]
        public void ARoleHeldBothDirectlyAndThroughATeam_SaysSo()
        {
            var graph = new SecurityGraph(
                _o.Graph.Users, _o.Graph.Roles, _o.Graph.Teams, _o.Graph.BusinessUnits,
                new[] { new UserTeamLink { UserId = _o.Bob.Id, TeamId = _o.Reviewers.Id } },
                new[] { new UserRoleLink { UserId = _o.Bob.Id, RoleId = _o.Auditor.Id } },
                new[] { new TeamRoleLink { TeamId = _o.Reviewers.Id, RoleId = _o.Auditor.Id } });

            var auditor = graph.CompareUsers(_o.Bob, _o.Dee).Single(r => r.Item == "Auditor");

            Assert.Equal("Direct + via Reviewers", auditor.A);
        }

        [Fact]
        public void BusinessUnitCopiesOfARole_AreTheSameRole()
        {
            var w = new WriteOrg(); // Seller exists as a Root copy and a Sales copy
            var graph = new SecurityGraph(
                w.Graph.Users, w.Graph.Roles, w.Graph.Teams, w.Graph.BusinessUnits, new UserTeamLink[0],
                new[]
                {
                    new UserRoleLink { UserId = w.Dee.Id, RoleId = w.SellerRoot.Id },  // Dee (Root) has the Root copy
                    new UserRoleLink { UserId = w.Bob.Id, RoleId = w.SellerSales.Id }  // Bob (Sales) has the Sales copy
                },
                new TeamRoleLink[0]);

            var seller = graph.CompareUsers(w.Dee, w.Bob).Single(r => r.Item == "Seller");

            Assert.Equal("Both", seller.Result);
        }

        [Fact]
        public void TeamsBothBelongTo_AreInBoth_AndOthersAreOnlyOneSide()
        {
            var teams = _o.Graph.CompareUsers(_o.Bob, _o.Cy).Where(r => r.Category == "Team").ToDictionary(r => r.Item, r => r.Result);

            Assert.Equal("Both", teams["Reviewers"]);
            Assert.Equal("Only Bob", teams["Empty team"]);
        }

        [Fact]
        public void TwoUsersWithTheSameName_AreToldApartByTheirUserNames()
        {
            var one = new UserInfo { Id = Guid.NewGuid(), FullName = "Sam Lee", DomainName = "sam1@x", BusinessUnitId = _o.Root, BusinessUnitName = "Root" };
            var two = new UserInfo { Id = Guid.NewGuid(), FullName = "Sam Lee", DomainName = "sam2@x", BusinessUnitId = _o.Root, BusinessUnitName = "Root" };
            var graph = new SecurityGraph(
                new[] { one, two }, _o.Graph.Roles, _o.Graph.Teams, _o.Graph.BusinessUnits,
                new UserTeamLink[0], new[] { new UserRoleLink { UserId = one.Id, RoleId = _o.Admin.Id } }, new TeamRoleLink[0]);

            var admin = graph.CompareUsers(one, two).Single(r => r.Item == "Admin");

            Assert.Equal("Only Sam Lee (sam1@x)", admin.Result);
        }

        [Fact]
        public void UsersWithNothingInCommonOrAtAll_CompareWithoutError()
        {
            var rows = _o.Graph.CompareUsers(_o.Dee, _o.Dee);

            Assert.Single(rows); // just the business unit line
            Assert.Equal("Same", rows[0].Result);
        }
    }
}
