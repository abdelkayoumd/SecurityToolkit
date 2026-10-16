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
    public class AccessMatrixBuilderTests
    {
        private readonly Org _o = new Org();
        private readonly Guid _accountRead = Guid.NewGuid(), _accountDelete = Guid.NewGuid(), _contactRead = Guid.NewGuid(), _unknown = Guid.NewGuid();

        private IReadOnlyDictionary<Guid, PrivilegeTarget> Catalog()
        {
            return new Dictionary<Guid, PrivilegeTarget>
            {
                [_accountRead] = new PrivilegeTarget { Table = "account", Kind = PrivilegeKind.Read },
                [_accountDelete] = new PrivilegeTarget { Table = "account", Kind = PrivilegeKind.Delete },
                [_contactRead] = new PrivilegeTarget { Table = "contact", Kind = PrivilegeKind.Read }
            };
        }

        private static RoleGrant Grant(Guid privilege, PrivilegeDepth depth) => new RoleGrant { PrivilegeId = privilege, Depth = depth };

        /// <summary>Admin reaches the organization on everything; Seller only the user's own accounts; Auditor its business unit.</summary>
        private IReadOnlyDictionary<Guid, IReadOnlyList<RoleGrant>> Grants()
        {
            return new Dictionary<Guid, IReadOnlyList<RoleGrant>>
            {
                [_o.Admin.Id] = new[] { Grant(_accountRead, PrivilegeDepth.Organization), Grant(_accountDelete, PrivilegeDepth.Organization), Grant(_contactRead, PrivilegeDepth.Organization) },
                [_o.SellerSales.Id] = new[] { Grant(_accountRead, PrivilegeDepth.User) },
                [_o.Auditor.Id] = new[] { Grant(_accountRead, PrivilegeDepth.BusinessUnit), Grant(_contactRead, PrivilegeDepth.BusinessUnit) }
            };
        }

        private static readonly TableInfo[] Tables =
        {
            new TableInfo { LogicalName = "lead", DisplayName = "Lead" },
            new TableInfo { LogicalName = "contact", DisplayName = "Contact" },
            new TableInfo { LogicalName = "account", DisplayName = "Account" }
        };

        private IReadOnlyList<MatrixRow> Build(UserInfo user, bool all = false, IReadOnlyDictionary<Guid, IReadOnlyList<RoleGrant>> grants = null)
        {
            return AccessMatrixBuilder.Build(_o.Graph, user, Tables, Catalog(), grants ?? Grants(), all);
        }

        [Fact]
        public void ADirectRole_FillsTheCellsItGrants_WithTheOrganizationReach()
        {
            var rows = Build(_o.Ada);

            var account = rows.Single(r => r.Table == "Account");
            Assert.Equal("Organization", account.Read);
            Assert.Equal("Organization", account.Delete);
            Assert.Equal(string.Empty, account.Write);
            Assert.Equal("Organization", rows.Single(r => r.Table == "Contact").Read);
        }

        [Fact]
        public void WhenSeveralRolesGrantTheSameThing_TheWidestReachWins()
        {
            // Bob: Seller (own records) directly, and Auditor (business unit) through the Reviewers team.
            var account = Build(_o.Bob).Single(r => r.Table == "Account");

            Assert.Equal("Business Unit", account.Read);
            Assert.Equal("None", account.Delete); // the privilege exists for Account, Bob just has no role granting it
        }

        [Fact]
        public void ARoleThroughATeam_CountsLikeADirectOne()
        {
            Assert.Equal("Business Unit", Build(_o.Bob).Single(r => r.Table == "Contact").Read);
        }

        [Fact]
        public void ByDefault_OnlyTablesTheUserCanTouchAreListed_InNameOrder()
        {
            Assert.Equal(new[] { "Account", "Contact" }, Build(_o.Ada).Select(r => r.Table)); // Lead has nothing
        }

        [Fact]
        public void OnRequest_TablesWithNoAccessAreListedToo_WithEmptyCells()
        {
            var lead = Build(_o.Ada, all: true).Single(r => r.Table == "Lead");

            Assert.False(lead.HasAny);
            Assert.Equal(new[] { "Account", "Contact", "Lead" }, Build(_o.Ada, all: true).Select(r => r.Table));
        }

        [Fact]
        public void APrivilegeNotInTheCatalog_OrWithNoDepth_IsIgnored()
        {
            var grants = new Dictionary<Guid, IReadOnlyList<RoleGrant>>
            {
                [_o.Admin.Id] = new[] { Grant(_unknown, PrivilegeDepth.Organization), Grant(_accountRead, PrivilegeDepth.None) }
            };

            Assert.Empty(Build(_o.Ada, grants: grants));
        }

        [Fact]
        public void ARoleWithNoRecordedPrivileges_AddsNothing()
        {
            Assert.Empty(Build(_o.Ada, grants: new Dictionary<Guid, IReadOnlyList<RoleGrant>>()));
        }

        [Fact]
        public void AUserWithNoRoles_HasNoAccessAtAll()
        {
            Assert.Empty(Build(_o.Dee));
            Assert.Equal("Dee holds no security roles, so has no access.", AccessMatrixBuilder.DescribeRoles(_o.Graph, _o.Dee));
        }

        [Fact]
        public void TheSummary_CountsDirectRolesAndRolesThroughTeams()
        {
            Assert.Equal("Bob holds 1 role directly and 1 through teams.", AccessMatrixBuilder.DescribeRoles(_o.Graph, _o.Bob));
            Assert.Equal("Ada holds 1 role directly and 0 through teams.", AccessMatrixBuilder.DescribeRoles(_o.Graph, _o.Ada));
        }

        [Fact]
        public void ARoleHeldTwoWays_IsReadOnce()
        {
            var graph = new SecurityGraph(_o.Graph.Users, _o.Graph.Roles, _o.Graph.Teams, _o.Graph.BusinessUnits,
                new[] { new UserTeamLink { UserId = _o.Bob.Id, TeamId = _o.Reviewers.Id } },
                new[] { new UserRoleLink { UserId = _o.Bob.Id, RoleId = _o.Auditor.Id } },
                new[] { new TeamRoleLink { TeamId = _o.Reviewers.Id, RoleId = _o.Auditor.Id } });

            Assert.Single(AccessMatrixBuilder.RoleIdsOf(graph, _o.Bob));
        }

        [Fact]
        public void TableNamesMatchWithoutRegardToCase()
        {
            var catalog = new Dictionary<Guid, PrivilegeTarget> { [_accountRead] = new PrivilegeTarget { Table = "ACCOUNT", Kind = PrivilegeKind.Read } };

            var rows = AccessMatrixBuilder.Build(_o.Graph, _o.Ada, Tables, catalog, Grants(), false);

            Assert.Equal("Organization", Assert.Single(rows).Read);
        }

        [Theory]
        [InlineData((int)PrivilegeDepth.User, "User")]
        [InlineData((int)PrivilegeDepth.BusinessUnit, "Business Unit")]
        [InlineData((int)PrivilegeDepth.ParentChildBusinessUnits, "Parent: Child Business Units")]
        [InlineData((int)PrivilegeDepth.Organization, "Organization")]
        public void TheCellsUseTheWordsTheSecurityRoleEditorUses(int depth, string expected)
        {
            var catalog = new Dictionary<Guid, PrivilegeTarget> { [_accountRead] = new PrivilegeTarget { Table = "account", Kind = PrivilegeKind.Read } };
            var grants = new Dictionary<Guid, IReadOnlyList<RoleGrant>> { [_o.Admin.Id] = new[] { Grant(_accountRead, (PrivilegeDepth)depth) } };

            var row = AccessMatrixBuilder.Build(_o.Graph, _o.Ada, Tables, catalog, grants, false).Single();

            Assert.Equal(expected, row.Read);
        }

        [Fact]
        public void APrivilegeThatExistsButIsNotGranted_ShowsNone_WhileOneThatDoesNotExistIsBlank()
        {
            // In the test catalog Account has Read and Delete but no Write. Ada's role grants only Read.
            var catalog = Catalog();
            var grants = new Dictionary<Guid, IReadOnlyList<RoleGrant>> { [_o.Admin.Id] = new[] { Grant(_accountRead, PrivilegeDepth.Organization) } };

            var account = AccessMatrixBuilder.Build(_o.Graph, _o.Ada, Tables, catalog, grants, false).Single(r => r.Table == "Account");

            Assert.Equal("Organization", account.Read);
            Assert.Equal("None", account.Delete);          // exists for Account, not granted
            Assert.Equal(string.Empty, account.Write);     // does not exist for Account
        }

        [Fact]
        public void ATableWhereEveryCellIsNone_CountsAsNoAccess()
        {
            // Ada holds Admin but its grants are on other tables only; Contact has privileges that she lacks.
            var grants = new Dictionary<Guid, IReadOnlyList<RoleGrant>> { [_o.Admin.Id] = new[] { Grant(_accountRead, PrivilegeDepth.Organization) } };

            var rows = AccessMatrixBuilder.Build(_o.Graph, _o.Ada, Tables, Catalog(), grants, includeTablesWithoutAccess: true);

            var contact = rows.Single(r => r.Table == "Contact");
            Assert.Equal("None", contact.Read);
            Assert.False(contact.HasAny);
            Assert.DoesNotContain(AccessMatrixBuilder.Build(_o.Graph, _o.Ada, Tables, Catalog(), grants, false), r => r.Table == "Contact");
        }

        [Fact]
        public void EveryOneOfTheEightPrivilegesHasItsOwnColumn()
        {
            var catalog = new Dictionary<Guid, PrivilegeTarget>();
            var grants = new List<RoleGrant>();
            foreach (PrivilegeKind kind in Enum.GetValues(typeof(PrivilegeKind)))
            {
                var id = Guid.NewGuid();
                catalog[id] = new PrivilegeTarget { Table = "account", Kind = kind };
                grants.Add(Grant(id, PrivilegeDepth.BusinessUnit));
            }

            var row = AccessMatrixBuilder.Build(_o.Graph, _o.Ada, Tables, catalog,
                new Dictionary<Guid, IReadOnlyList<RoleGrant>> { [_o.Admin.Id] = grants }, false).Single();

            Assert.All(new[] { row.Create, row.Read, row.Write, row.Delete, row.Append, row.AppendTo, row.Assign, row.Share }, c => Assert.Equal("Business Unit", c));
        }
    }

    public class PrivilegeQueriesTests
    {
        private static Entity PrivilegeRow(Guid id, int accessRight, string table)
        {
            var e = new Entity("privilege", id) { ["accessright"] = accessRight };
            if (table != null)
            {
                e["otc.objecttypecode"] = new AliasedValue("privilegeobjecttypecodes", "objecttypecode", table);
            }

            return e;
        }

        [Fact]
        public void TheCatalog_MapsEachPrivilegeToItsTableAndAction()
        {
            var read = Guid.NewGuid(); var delete = Guid.NewGuid();
            var fake = new FakeOrganizationService
            {
                OnRetrieveMultiple = q => new EntityCollection(new List<Entity> { PrivilegeRow(read, 1, "account"), PrivilegeRow(delete, 65536, "contact") })
            };

            var catalog = new SecurityService(fake).GetPrivilegeCatalog();

            Assert.Equal(PrivilegeKind.Read, catalog[read].Kind);
            Assert.Equal("account", catalog[read].Table);
            Assert.Equal(PrivilegeKind.Delete, catalog[delete].Kind);
            Assert.Equal("contact", catalog[delete].Table);
        }

        [Fact]
        public void TheCatalog_LeavesOutPrivilegesThatAreNotAboutATableOrNotOneOfTheEightActions()
        {
            var fake = new FakeOrganizationService
            {
                OnRetrieveMultiple = q => new EntityCollection(new List<Entity>
                {
                    PrivilegeRow(Guid.NewGuid(), 1, null),       // no table
                    PrivilegeRow(Guid.NewGuid(), 8, "account"),   // not one of the eight
                    PrivilegeRow(Guid.NewGuid(), 2, "account")    // Write: kept
                })
            };

            Assert.Single(new SecurityService(fake).GetPrivilegeCatalog());
        }

        [Fact]
        public void TheCatalog_IsFetchedOnce_UntilInvalidated()
        {
            var fake = new FakeOrganizationService { OnRetrieveMultiple = q => new EntityCollection(new List<Entity> { PrivilegeRow(Guid.NewGuid(), 1, "account") }) };
            var service = new SecurityService(fake);

            service.GetPrivilegeCatalog();
            service.GetPrivilegeCatalog();
            Assert.Single(fake.Queries);

            service.Invalidate();
            service.GetPrivilegeCatalog();
            Assert.Equal(2, fake.Queries.Count);
        }

        [Fact]
        public void RolePrivileges_AreReturnedPerRole_AndARoleWithNoneGetsAnEmptyList()
        {
            var withGrants = Guid.NewGuid(); var empty = Guid.NewGuid(); var privilege = Guid.NewGuid();
            var fake = new FakeOrganizationService
            {
                OnRetrieveMultiple = q => new EntityCollection(new List<Entity>
                {
                    new Entity("roleprivileges") { ["roleid"] = withGrants, ["privilegeid"] = privilege, ["privilegedepthmask"] = 8 }
                })
            };

            var result = new SecurityService(fake).GetRolePrivileges(new[] { withGrants, empty });

            var grant = Assert.Single(result[withGrants]);
            Assert.Equal(privilege, grant.PrivilegeId);
            Assert.Equal(PrivilegeDepth.Organization, grant.Depth);
            Assert.Empty(result[empty]);
        }

        [Fact]
        public void RolePrivileges_AreFetchedOnlyForRolesNotAlreadyKnown()
        {
            var a = Guid.NewGuid(); var b = Guid.NewGuid();
            var fake = new FakeOrganizationService { OnRetrieveMultiple = q => new EntityCollection() };
            var service = new SecurityService(fake);

            service.GetRolePrivileges(new[] { a });
            service.GetRolePrivileges(new[] { a });            // nothing new
            Assert.Single(fake.Queries);

            service.GetRolePrivileges(new[] { a, b });         // only b is new
            Assert.Equal(2, fake.Queries.Count);
            var asked = ((QueryExpression)fake.Queries[1]).Criteria.Conditions.Single().Values.Cast<Guid>().ToList();
            Assert.Equal(new[] { b }, asked);
        }

        [Fact]
        public void ManyRoles_AreAskedForInChunks()
        {
            var ids = Enumerable.Range(0, 250).Select(i => Guid.NewGuid()).ToList();
            var fake = new FakeOrganizationService { OnRetrieveMultiple = q => new EntityCollection() };

            new SecurityService(fake).GetRolePrivileges(ids);

            Assert.Equal(3, fake.Queries.Count); // 100 + 100 + 50
        }

        [Theory]
        [InlineData(1, true, PrivilegeKind.Read)]
        [InlineData(2, true, PrivilegeKind.Write)]
        [InlineData(4, true, PrivilegeKind.Append)]
        [InlineData(16, true, PrivilegeKind.AppendTo)]
        [InlineData(32, true, PrivilegeKind.Create)]
        [InlineData(65536, true, PrivilegeKind.Delete)]
        [InlineData(262144, true, PrivilegeKind.Share)]
        [InlineData(524288, true, PrivilegeKind.Assign)]
        public void AccessRightNumbers_MapBackToTheirKind(int accessRight, bool known, object expected)
        {
            Assert.Equal(known, Privileges.TryKind(accessRight, out var kind));
            Assert.Equal((PrivilegeKind)expected, kind);
        }

        [Fact]
        public void AnUnknownAccessRight_IsNotAKind()
        {
            Assert.False(Privileges.TryKind(8, out _));
        }
    }
}
