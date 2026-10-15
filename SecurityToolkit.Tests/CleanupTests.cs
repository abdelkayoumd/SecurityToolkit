using System;
using System.Collections.Generic;
using System.Linq;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using Xunit;

namespace SecurityToolkit.Tests
{
    /// <summary>
    /// Ann: active, no roles, no teams.            Ben: active, a direct role.
    /// Cat: active, only a role through a team.     Dan: disabled, in team Quiet, has a direct role.
    /// Eve: disabled, only in the default team.     Svc: service account, no roles.
    /// Teams: Quiet (no role, member Dan), Staffed (has a role, member Cat), Empty (no members, no role),
    /// a default team (everyone), an access team (no members, no roles).
    /// Roles: Used (Ben), TeamRole (Staffed), Spare (custom, nobody), Vendor (managed, nobody).
    /// </summary>
    internal sealed class CleanupOrg
    {
        public Guid Bu = Guid.NewGuid();
        public UserInfo Ann, Ben, Cat, Dan, Eve, Svc;
        public RoleInfo Used, TeamRole, Spare, Vendor;
        public TeamInfo Quiet, Staffed, Empty, DefaultTeam, Access;
        public SecurityGraph Graph;

        public CleanupOrg()
        {
            UserInfo User(string n, bool disabled = false, int mode = 0) => new UserInfo { Id = Guid.NewGuid(), FullName = n, BusinessUnitId = Bu, BusinessUnitName = "Root", IsDisabled = disabled, AccessMode = mode };
            Ann = User("Ann"); Ben = User("Ben"); Cat = User("Cat"); Dan = User("Dan", true); Eve = User("Eve", true); Svc = User("Svc", mode: 4);

            RoleInfo Role(string n, bool managed = false) => new RoleInfo { Id = Guid.NewGuid(), Name = n, BusinessUnitId = Bu, BusinessUnitName = "Root", IsManaged = managed };
            Used = Role("Used"); TeamRole = Role("TeamRole"); Spare = Role("Spare"); Vendor = Role("Vendor", managed: true);

            TeamInfo Team(string n, int type = 0, bool isDefault = false) => new TeamInfo { Id = Guid.NewGuid(), Name = n, TeamType = type, IsDefault = isDefault, BusinessUnitId = Bu, BusinessUnitName = "Root" };
            Quiet = Team("Quiet"); Staffed = Team("Staffed"); Empty = Team("Empty"); DefaultTeam = Team("Root", isDefault: true); Access = Team("Shared", type: 1);

            var everyone = new[] { Ann, Ben, Cat, Dan, Eve, Svc };
            Graph = new SecurityGraph(
                everyone,
                new[] { Used, TeamRole, Spare, Vendor },
                new[] { Quiet, Staffed, Empty, DefaultTeam, Access },
                new[] { new BusinessUnitInfo { Id = Bu, Name = "Root" } },
                everyone.Select(u => new UserTeamLink { UserId = u.Id, TeamId = DefaultTeam.Id })
                    .Concat(new[]
                    {
                        new UserTeamLink { UserId = Dan.Id, TeamId = Quiet.Id },
                        new UserTeamLink { UserId = Cat.Id, TeamId = Staffed.Id }
                    }),
                new[]
                {
                    new UserRoleLink { UserId = Ben.Id, RoleId = Used.Id },
                    new UserRoleLink { UserId = Dan.Id, RoleId = Used.Id }
                },
                new[] { new TeamRoleLink { TeamId = Staffed.Id, RoleId = TeamRole.Id } });
        }

        public CleanupCheck Check(string title) => CleanupChecks.Run(Graph).Single(c => c.Title == title);
    }

    public class CleanupFieldMappingTests
    {
        [Fact]
        public void AUsersAccessMode_IsReadAndNonInteractiveMeansServiceAccount()
        {
            var fake = new FakeOrganizationService();
            fake.OnRetrieveMultiple = q => new Microsoft.Xrm.Sdk.EntityCollection(new List<Microsoft.Xrm.Sdk.Entity>
            {
                new Microsoft.Xrm.Sdk.Entity("systemuser", Guid.NewGuid()) { ["fullname"] = "App", ["accessmode"] = new Microsoft.Xrm.Sdk.OptionSetValue(4) },
                new Microsoft.Xrm.Sdk.Entity("systemuser", Guid.NewGuid()) { ["fullname"] = "Person", ["accessmode"] = new Microsoft.Xrm.Sdk.OptionSetValue(0) },
                new Microsoft.Xrm.Sdk.Entity("systemuser", Guid.NewGuid()) { ["fullname"] = "Unspecified" }
            });

            var users = new SecurityService(fake).GetUsers().ToDictionary(u => u.FullName);

            Assert.True(users["App"].IsServiceAccount);
            Assert.False(users["Person"].IsServiceAccount);
            Assert.False(users["Unspecified"].IsServiceAccount);
        }

        [Fact]
        public void ARolesManagedFlag_IsRead()
        {
            var fake = new FakeOrganizationService();
            fake.OnRetrieveMultiple = q => new Microsoft.Xrm.Sdk.EntityCollection(new List<Microsoft.Xrm.Sdk.Entity>
            {
                new Microsoft.Xrm.Sdk.Entity("role", Guid.NewGuid()) { ["name"] = "Vendor", ["ismanaged"] = true },
                new Microsoft.Xrm.Sdk.Entity("role", Guid.NewGuid()) { ["name"] = "Custom" }
            });

            var roles = new SecurityService(fake).GetRoles().ToDictionary(r => r.Name);

            Assert.True(roles["Vendor"].IsManaged);
            Assert.False(roles["Custom"].IsManaged);
        }

        [Fact]
        public void TheQueriesAskForTheNewFields()
        {
            var fake = new FakeOrganizationService();
            var service = new SecurityService(fake);

            service.GetUsers();
            service.GetRoles();

            var userColumns = ((Microsoft.Xrm.Sdk.Query.QueryExpression)fake.Queries[0]).ColumnSet.Columns;
            var roleColumns = ((Microsoft.Xrm.Sdk.Query.QueryExpression)fake.Queries[1]).ColumnSet.Columns;
            Assert.Contains("accessmode", userColumns);
            Assert.Contains("ismanaged", roleColumns);
        }
    }

    public class CleanupChecksTests
    {
        private readonly CleanupOrg _o = new CleanupOrg();

        [Fact]
        public void AllSixChecksAreRun_InAFixedOrder()
        {
            Assert.Equal(
                new[] { CleanupChecks.NoRoles, CleanupChecks.DisabledInTeams, CleanupChecks.DisabledWithRoles, CleanupChecks.UnusedRoles, CleanupChecks.EmptyTeams, CleanupChecks.TeamsWithoutRoles },
                CleanupChecks.Run(_o.Graph).Select(c => c.Title));
        }

        [Fact]
        public void UsersWithNoRoles_AreActiveUsersWithNeitherADirectNorATeamRole()
        {
            var rows = _o.Check(CleanupChecks.NoRoles).Rows;

            Assert.Equal(new[] { "Ann" }, rows.Select(r => r.Item)); // Ben has one, Cat gets one via Staffed
        }

        [Fact]
        public void ServiceAccountsAndDisabledUsers_AreNotReportedAsHavingNoRoles()
        {
            var items = _o.Check(CleanupChecks.NoRoles).Rows.Select(r => r.Item).ToList();

            Assert.DoesNotContain("Svc", items);
            Assert.DoesNotContain("Eve", items);
        }

        [Fact]
        public void ANoRolesFinding_SaysHowManyTeamsTheUserIsIn_NotCountingTheDefaultTeam()
        {
            Assert.Equal("Member of 0 team(s)", _o.Check(CleanupChecks.NoRoles).Rows.Single().Detail);
        }

        [Fact]
        public void DisabledUsersInTeams_ListsEachNonDefaultMembership()
        {
            var row = Assert.Single(_o.Check(CleanupChecks.DisabledInTeams).Rows);

            Assert.Equal("Dan", row.Item);
            Assert.Contains("Quiet", row.Detail);
            Assert.Contains("owner team", row.Detail);
        }

        [Fact]
        public void ADisabledUserInOnlyTheDefaultTeam_IsFine()
        {
            Assert.DoesNotContain(_o.Check(CleanupChecks.DisabledInTeams).Rows, r => r.Item == "Eve");
        }

        [Fact]
        public void DisabledUsersWithRoles_ListsTheirDirectRoles()
        {
            var row = Assert.Single(_o.Check(CleanupChecks.DisabledWithRoles).Rows);

            Assert.Equal("Dan", row.Item);
            Assert.Equal("Used", row.Detail);
        }

        [Fact]
        public void UnusedRoles_AreThoseNoUserOrTeamHolds_AndSayWhetherTheyAreManaged()
        {
            var rows = _o.Check(CleanupChecks.UnusedRoles).Rows;

            Assert.Equal(new[] { "Spare", "Vendor" }, rows.Select(r => r.Item));
            Assert.Equal("Custom", rows.Single(r => r.Item == "Spare").Detail);
            Assert.Contains("Managed", rows.Single(r => r.Item == "Vendor").Detail);
        }

        [Fact]
        public void ARoleHeldOnlyByATeam_IsNotUnused()
        {
            Assert.DoesNotContain(_o.Check(CleanupChecks.UnusedRoles).Rows, r => r.Item == "TeamRole");
        }

        [Fact]
        public void ARoleIsUnusedOnlyIfNoBusinessUnitCopyIsAssigned()
        {
            var root = new RoleInfo { Id = Guid.NewGuid(), Name = "Seller", BusinessUnitId = _o.Bu, BusinessUnitName = "Root" };
            var copy = new RoleInfo { Id = Guid.NewGuid(), Name = "Seller", BusinessUnitId = Guid.NewGuid(), BusinessUnitName = "Sales", RootRoleId = root.Id };
            var graph = new SecurityGraph(_o.Graph.Users, new[] { root, copy }, _o.Graph.Teams, _o.Graph.BusinessUnits, new UserTeamLink[0],
                new[] { new UserRoleLink { UserId = _o.Ben.Id, RoleId = copy.Id } }, new TeamRoleLink[0]); // only the Sales copy is held

            Assert.Empty(CleanupChecks.Run(graph).Single(c => c.Title == CleanupChecks.UnusedRoles).Rows);
        }

        [Fact]
        public void EmptyTeams_AreOrdinaryTeamsWithNoMembers_NotTheDefaultOrAccessTeam()
        {
            Assert.Equal(new[] { "Empty" }, _o.Check(CleanupChecks.EmptyTeams).Rows.Select(r => r.Item));
        }

        [Fact]
        public void TeamsWithoutRoles_AreOrdinaryTeamsCarryingNoRole_WithTheirMemberCount()
        {
            var rows = _o.Check(CleanupChecks.TeamsWithoutRoles).Rows;

            Assert.Equal(new[] { "Empty", "Quiet" }, rows.Select(r => r.Item));
            Assert.Equal("1 member(s)", rows.Single(r => r.Item == "Quiet").Detail);
            Assert.Equal("0 member(s)", rows.Single(r => r.Item == "Empty").Detail);
        }

        [Fact]
        public void EveryFinding_NamesItsCheck_AndEachCheckKnowsItsCount()
        {
            foreach (var check in CleanupChecks.Run(_o.Graph))
            {
                Assert.Equal(check.Rows.Count, check.Count);
                Assert.All(check.Rows, r => Assert.Equal(check.Title, r.Check));
                Assert.False(string.IsNullOrWhiteSpace(check.Description));
            }
        }

        [Fact]
        public void AnOrganizationWithNothingToClean_ReportsNothing()
        {
            var graph = new SecurityGraph(
                new[] { _o.Ben }, new[] { _o.Used }, new TeamInfo[0], _o.Graph.BusinessUnits,
                new UserTeamLink[0], new[] { new UserRoleLink { UserId = _o.Ben.Id, RoleId = _o.Used.Id } }, new TeamRoleLink[0]);

            Assert.All(CleanupChecks.Run(graph), c => Assert.Empty(c.Rows));
        }
    }
}
