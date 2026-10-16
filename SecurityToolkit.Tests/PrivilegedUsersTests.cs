using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using Xunit;

namespace SecurityToolkit.Tests
{
    public class OrgDeleteReachTests
    {
        private static OrgDeleteReach Reach(int total, params (Guid, int)[] roles)
        {
            return new OrgDeleteReach(roles.ToDictionary(r => r.Item1, r => r.Item2), total);
        }

        [Theory]
        [InlineData(100, 50, true)]
        [InlineData(100, 49, false)]
        [InlineData(100, 100, true)]
        [InlineData(5, 3, true)]    // half of 5 is 2.5, so 3 are needed
        [InlineData(5, 2, false)]
        [InlineData(0, 0, false)]   // no tables to delete from: nothing is broad
        public void ARoleIsBroadFromHalfOfTheTablesUp(int total, int tables, bool broad)
        {
            var role = Guid.NewGuid();

            Assert.Equal(broad, Reach(total, (role, tables)).IsBroad(role));
        }

        [Fact]
        public void ARoleNobodyMeasured_ReachesNothing()
        {
            var reach = Reach(100);

            Assert.Equal(0, reach.TablesOf(Guid.NewGuid()));
            Assert.False(reach.IsBroad(Guid.NewGuid()));
        }
    }

    public class PrivilegedUsersTests
    {
        private readonly Org _o = new Org();

        private OrgDeleteReach AdminIsBroad() => new OrgDeleteReach(new Dictionary<Guid, int> { [_o.Admin.RootKey] = 60, [_o.Auditor.RootKey] = 10 }, 100);

        private OrgDeleteReach AdminAndAuditorAreBroad() => new OrgDeleteReach(new Dictionary<Guid, int> { [_o.Admin.RootKey] = 60, [_o.Auditor.RootKey] = 55 }, 100);

        private static PrivilegedCheck Check(IReadOnlyList<PrivilegedCheck> checks, string title) => checks.Single(c => c.Title == title);

        [Fact]
        public void ThereAreSixChecks_InAFixedOrder()
        {
            Assert.Equal(
                new[] { PrivilegedUsers.BroadRolesTitle, PrivilegedUsers.HoldersTitle, PrivilegedUsers.BuiltInTitle, PrivilegedUsers.DisabledTitle, PrivilegedUsers.ServiceTitle, PrivilegedUsers.ManyRolesTitle },
                PrivilegedUsers.Run(_o.Graph, AdminIsBroad()).Select(c => c.Title));
        }

        [Fact]
        public void TheBroadRolesCheck_ShowsWhichRolesCountAndWhy()
        {
            var row = Assert.Single(Check(PrivilegedUsers.Run(_o.Graph, AdminIsBroad()), PrivilegedUsers.BroadRolesTitle).Rows);

            Assert.Equal("Admin", row.Subject);
            Assert.Equal("Organization-level Delete on 60 of 100 tables", row.Detail);
        }

        [Fact]
        public void TheBroadRolesDescription_StatesTheRule()
        {
            var description = Check(PrivilegedUsers.Run(_o.Graph, AdminIsBroad()), PrivilegedUsers.BroadRolesTitle).Description;

            Assert.Contains("at least 50%", description);
            Assert.Contains("100 tables", description);
        }

        [Fact]
        public void AUserWithABroadRoleAssignedDirectly_IsListed_WithTheRoleAndHowTheyHoldIt()
        {
            var row = Assert.Single(Check(PrivilegedUsers.Run(_o.Graph, AdminIsBroad()), PrivilegedUsers.HoldersTitle).Rows);

            Assert.Equal("Ada", row.Subject);
            Assert.Equal("Admin (direct)", row.Detail);
            Assert.Equal("Root", row.BusinessUnit);
            Assert.Equal("Active", row.Status);
        }

        [Fact]
        public void ABroadRoleHeldThroughATeam_NamesTheTeam_AndOnlyBroadRolesAreShown()
        {
            var holders = Check(PrivilegedUsers.Run(_o.Graph, AdminAndAuditorAreBroad()), PrivilegedUsers.HoldersTitle).Rows;

            var bob = holders.Single(r => r.Subject == "Bob");
            Assert.Equal("Auditor (via Reviewers)", bob.Detail); // Bob's own Seller role is not broad, so it is not mentioned
        }

        [Fact]
        public void ARoleHeldBothWays_SaysSo()
        {
            var graph = new SecurityGraph(_o.Graph.Users, _o.Graph.Roles, _o.Graph.Teams, _o.Graph.BusinessUnits,
                new[] { new UserTeamLink { UserId = _o.Bob.Id, TeamId = _o.Reviewers.Id } },
                new[] { new UserRoleLink { UserId = _o.Bob.Id, RoleId = _o.Auditor.Id } },
                new[] { new TeamRoleLink { TeamId = _o.Reviewers.Id, RoleId = _o.Auditor.Id } });

            var bob = Check(PrivilegedUsers.Run(graph, AdminAndAuditorAreBroad()), PrivilegedUsers.HoldersTitle).Rows.Single(r => r.Subject == "Bob");

            Assert.Equal("Auditor (direct + via Reviewers)", bob.Detail);
        }

        [Fact]
        public void DisabledHolders_AreInTheirOwnCheck_NotInThePeopleCheck()
        {
            var checks = PrivilegedUsers.Run(_o.Graph, AdminAndAuditorAreBroad());

            // Cy is disabled and holds Auditor through Reviewers.
            Assert.DoesNotContain(Check(checks, PrivilegedUsers.HoldersTitle).Rows, r => r.Subject == "Cy");
            var cy = Assert.Single(Check(checks, PrivilegedUsers.DisabledTitle).Rows);
            Assert.Equal("Cy", cy.Subject);
            Assert.Equal("Disabled", cy.Status);
        }

        [Fact]
        public void ServiceAccountsWithABroadRole_AreInTheirOwnCheck_NotInThePeopleCheck()
        {
            var service = new UserInfo { Id = Guid.NewGuid(), FullName = "Integration", AccessMode = 4, BusinessUnitName = "Root" };
            var graph = new SecurityGraph(_o.Graph.Users.Concat(new[] { service }), _o.Graph.Roles, _o.Graph.Teams, _o.Graph.BusinessUnits,
                new UserTeamLink[0], new[] { new UserRoleLink { UserId = service.Id, RoleId = _o.Admin.Id } }, new TeamRoleLink[0]);

            var checks = PrivilegedUsers.Run(graph, AdminIsBroad());

            Assert.DoesNotContain(Check(checks, PrivilegedUsers.HoldersTitle).Rows, r => r.Subject == "Integration");
            var row = Assert.Single(Check(checks, PrivilegedUsers.ServiceTitle).Rows);
            Assert.Equal("Integration", row.Subject);
            Assert.Equal("Active (service account)", row.Status);
        }

        [Fact]
        public void TheBuiltInAdministratorRoles_AreFoundByTheirEnglishNames_WhateverTheCase()
        {
            var admin = new RoleInfo { Id = Guid.NewGuid(), Name = "System Administrator" };
            var customizer = new RoleInfo { Id = Guid.NewGuid(), Name = "system customizer" };
            var graph = new SecurityGraph(_o.Graph.Users, new[] { admin, customizer, _o.Auditor }, _o.Graph.Teams, _o.Graph.BusinessUnits,
                new UserTeamLink[0],
                new[] { new UserRoleLink { UserId = _o.Ada.Id, RoleId = admin.Id }, new UserRoleLink { UserId = _o.Bob.Id, RoleId = customizer.Id }, new UserRoleLink { UserId = _o.Dee.Id, RoleId = _o.Auditor.Id } },
                new TeamRoleLink[0]);

            var rows = Check(PrivilegedUsers.Run(graph, new OrgDeleteReach(new Dictionary<Guid, int>(), 100)), PrivilegedUsers.BuiltInTitle).Rows;

            Assert.Equal(new[] { "Ada", "Bob" }, rows.Select(r => r.Subject));
        }

        [Fact]
        public void UsersWithManyRoles_AreListed_BusiestFirst_CountingEachRoleOnce()
        {
            var roles = Enumerable.Range(1, 9).Select(i => new RoleInfo { Id = Guid.NewGuid(), Name = "Role " + i }).ToList();
            var links = roles.Select(r => new UserRoleLink { UserId = _o.Ada.Id, RoleId = r.Id }).ToList();
            links.AddRange(roles.Take(7).Select(r => new UserRoleLink { UserId = _o.Bob.Id, RoleId = r.Id })); // 7: below the line
            var graph = new SecurityGraph(_o.Graph.Users, roles, _o.Graph.Teams, _o.Graph.BusinessUnits, new UserTeamLink[0], links, new TeamRoleLink[0]);

            var rows = Check(PrivilegedUsers.Run(graph, new OrgDeleteReach(new Dictionary<Guid, int>(), 100)), PrivilegedUsers.ManyRolesTitle).Rows;

            var ada = Assert.Single(rows);
            Assert.Equal("Ada", ada.Subject);
            Assert.Equal("9 different roles", ada.Detail);
        }

        [Fact]
        public void WhenNoRoleIsBroad_NoOneIsFlaggedAsHoldingOne()
        {
            var checks = PrivilegedUsers.Run(_o.Graph, new OrgDeleteReach(new Dictionary<Guid, int>(), 100));

            Assert.Empty(Check(checks, PrivilegedUsers.BroadRolesTitle).Rows);
            Assert.Empty(Check(checks, PrivilegedUsers.HoldersTitle).Rows);
            Assert.Empty(Check(checks, PrivilegedUsers.DisabledTitle).Rows);
            Assert.Empty(Check(checks, PrivilegedUsers.ServiceTitle).Rows);
        }

        [Fact]
        public void EveryFinding_NamesItsCheck_AndEveryCheckHasADescription()
        {
            foreach (var check in PrivilegedUsers.Run(_o.Graph, AdminAndAuditorAreBroad()))
            {
                Assert.Equal(check.Rows.Count, check.Count);
                Assert.All(check.Rows, r => Assert.Equal(check.Title, r.Check));
                Assert.False(string.IsNullOrWhiteSpace(check.Description));
            }
        }
    }

    public class OrganizationDeleteReachQueryTests
    {
        private static Entity DeletePrivilege(Guid id, string table) => new Entity("privilege", id)
        {
            ["accessright"] = 65536,
            ["otc.objecttypecode"] = new AliasedValue("privilegeobjecttypecodes", "objecttypecode", table)
        };

        private static Entity ReadPrivilege(Guid id, string table) => new Entity("privilege", id)
        {
            ["accessright"] = 1,
            ["otc.objecttypecode"] = new AliasedValue("privilegeobjecttypecodes", "objecttypecode", table)
        };

        private static Entity Grant(Guid role, Guid privilege) => new Entity("roleprivileges") { ["roleid"] = role, ["privilegeid"] = privilege };

        [Fact]
        public void ItCountsHowManyTablesEachRoleCanDeleteOnAcrossTheOrganization()
        {
            var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid(); var read = Guid.NewGuid();
            var r1 = Guid.NewGuid(); var r2 = Guid.NewGuid(); var r3 = Guid.NewGuid();
            var fake = new FakeOrganizationService
            {
                OnRetrieveMultiple = q =>
                {
                    switch (((QueryExpression)q).EntityName)
                    {
                        case "privilege":
                            return new EntityCollection(new List<Entity> { DeletePrivilege(a, "account"), DeletePrivilege(b, "contact"), DeletePrivilege(c, "lead"), ReadPrivilege(read, "account") });
                        default:
                            return new EntityCollection(new List<Entity> { Grant(r1, a), Grant(r1, b), Grant(r1, c), Grant(r2, a) });
                    }
                }
            };

            var reach = new SecurityService(fake).GetOrganizationDeleteReach(new[] { r1, r2, r3 });

            Assert.Equal(3, reach.TotalTables);          // account, contact, lead have a Delete privilege; Read does not count
            Assert.Equal(3, reach.TablesOf(r1));
            Assert.Equal(1, reach.TablesOf(r2));
            Assert.Equal(0, reach.TablesOf(r3));
        }

        [Fact]
        public void ItAsksOnlyForDeletePrivileges_AtOrganizationDepthOrWider_OfTheRolesGiven()
        {
            var del = Guid.NewGuid(); var read = Guid.NewGuid(); var role = Guid.NewGuid();
            var fake = new FakeOrganizationService
            {
                OnRetrieveMultiple = q => ((QueryExpression)q).EntityName == "privilege"
                    ? new EntityCollection(new List<Entity> { DeletePrivilege(del, "account"), ReadPrivilege(read, "account") })
                    : new EntityCollection()
            };

            new SecurityService(fake).GetOrganizationDeleteReach(new[] { role });

            var grants = (QueryExpression)fake.Queries.Last();
            Assert.Equal("roleprivileges", grants.EntityName);
            Assert.Equal(new[] { role }, grants.Criteria.Conditions.Single(c => c.AttributeName == "roleid").Values.Cast<Guid>());
            Assert.Equal(new[] { del }, grants.Criteria.Conditions.Single(c => c.AttributeName == "privilegeid").Values.Cast<Guid>());
            var depth = grants.Criteria.Conditions.Single(c => c.AttributeName == "privilegedepthmask");
            Assert.Equal(ConditionOperator.GreaterEqual, depth.Operator);
            Assert.Equal(8, (int)depth.Values[0]);
        }

        [Fact]
        public void ItIsMeasuredOnce_UntilInvalidated()
        {
            var fake = new FakeOrganizationService
            {
                OnRetrieveMultiple = q => ((QueryExpression)q).EntityName == "privilege"
                    ? new EntityCollection(new List<Entity> { DeletePrivilege(Guid.NewGuid(), "account") })
                    : new EntityCollection()
            };
            var service = new SecurityService(fake);

            service.GetOrganizationDeleteReach(new[] { Guid.NewGuid() });
            var queries = fake.Queries.Count;
            service.GetOrganizationDeleteReach(new[] { Guid.NewGuid() });
            Assert.Equal(queries, fake.Queries.Count);

            service.Invalidate();
            service.GetOrganizationDeleteReach(new[] { Guid.NewGuid() });
            Assert.True(fake.Queries.Count > queries);
        }

        [Fact]
        public void ManyRolesAndPrivileges_AreAskedForInChunks()
        {
            var privileges = Enumerable.Range(0, 700).Select(i => DeletePrivilege(Guid.NewGuid(), "table" + i)).ToList();
            var fake = new FakeOrganizationService
            {
                OnRetrieveMultiple = q => ((QueryExpression)q).EntityName == "privilege" ? new EntityCollection(privileges) : new EntityCollection()
            };

            new SecurityService(fake).GetOrganizationDeleteReach(Enumerable.Range(0, 250).Select(i => Guid.NewGuid()));

            // 250 roles in chunks of 100 = 3; 700 privileges in chunks of 500 = 2; plus the one privilege query.
            Assert.Equal(1 + 3 * 2, fake.Queries.Count);
        }
    }
}
