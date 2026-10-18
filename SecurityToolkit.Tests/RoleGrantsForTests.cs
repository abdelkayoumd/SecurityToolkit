using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using Xunit;

namespace SecurityToolkit.Tests
{
    /// <summary>A fake that answers the privilege queries the way Dataverse does: by action and by table.</summary>
    internal static class PrivilegeFake
    {
        public static Entity Privilege(Guid id, int right, object table) => new Entity("privilege", id)
        {
            ["accessright"] = right,
            ["otc.objecttypecode"] = new AliasedValue("privilegeobjecttypecodes", "objecttypecode", table)
        };

        /// <summary>A privilege that stands for one action on one table.</summary>
        public static IReadOnlyList<PrivilegeTarget> Target(string table, PrivilegeKind kind) => new[] { new PrivilegeTarget { Table = table, Kind = kind } };

        public static Entity Grant(Guid role, Guid privilege, int mask) => new Entity("roleprivileges") { ["roleid"] = role, ["privilegeid"] = privilege, ["privilegedepthmask"] = mask };

        /// <summary>
        /// Serves the given privileges (filtered by "accessright" and the linked table when asked), the given grants
        /// (filtered by the "roleid" list when asked) and the given roles.
        /// </summary>
        public static FakeOrganizationService Serve(IEnumerable<Entity> privileges, IEnumerable<Entity> grants, IEnumerable<Entity> roles = null)
        {
            var allPrivileges = privileges.ToList();
            var allGrants = grants.ToList();
            var allRoles = (roles ?? new Entity[0]).ToList();
            return new FakeOrganizationService
            {
                OnRetrieveMultiple = q =>
                {
                    var query = (QueryExpression)q;
                    switch (query.EntityName)
                    {
                        case "privilege":
                            var right = query.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == "accessright");
                            var table = query.LinkEntities.SelectMany(l => l.LinkCriteria.Conditions).FirstOrDefault(c => c.AttributeName == "objecttypecode");
                            return new EntityCollection(allPrivileges
                                .Where(p => right == null || (int)right.Values[0] == p.GetAttributeValue<int>("accessright"))
                                .Where(p => table == null || string.Equals((string)table.Values[0], p.GetAttributeValue<AliasedValue>("otc.objecttypecode").Value as string, StringComparison.OrdinalIgnoreCase))
                                .ToList());
                        case "roleprivileges":
                            var asked = query.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == "roleid");
                            var roleIds = asked == null ? null : new HashSet<Guid>(asked.Values.Cast<Guid>());
                            return new EntityCollection(allGrants.Where(g => roleIds == null || roleIds.Contains(g.GetAttributeValue<Guid>("roleid"))).ToList());
                        case "role":
                            return new EntityCollection(allRoles);
                        default:
                            return new EntityCollection();
                    }
                }
            };
        }

        public static Entity Role(Guid id, string name, Guid? root = null)
        {
            var role = new Entity("role", id) { ["name"] = name, ["businessunitid"] = new EntityReference("businessunit", Guid.NewGuid()) { Name = "BU" } };
            if (root.HasValue)
            {
                role["parentrootroleid"] = new EntityReference("role", root.Value);
            }

            return role;
        }
    }

    public class RoleGrantsForTests
    {
        private readonly Guid _createAccount = Guid.NewGuid();
        private readonly Guid _createAccountAgain = Guid.NewGuid();
        private readonly Guid _readAccount = Guid.NewGuid();
        private readonly Guid _createContact = Guid.NewGuid();
        private readonly Guid _roleA = Guid.NewGuid();
        private readonly Guid _roleB = Guid.NewGuid();
        private readonly Guid _roleC = Guid.NewGuid();

        private FakeOrganizationService Fake()
        {
            return PrivilegeFake.Serve(
                new[]
                {
                    PrivilegeFake.Privilege(_createAccount, 32, "account"), PrivilegeFake.Privilege(_createAccountAgain, 32, "account"),
                    PrivilegeFake.Privilege(_readAccount, 1, "account"), PrivilegeFake.Privilege(_createContact, 32, "contact")
                },
                new[]
                {
                    PrivilegeFake.Grant(_roleA, _createAccount, 1),
                    PrivilegeFake.Grant(_roleA, _createAccountAgain, 4),   // widest of the two wins
                    PrivilegeFake.Grant(_roleB, _readAccount, 8),          // another privilege
                    PrivilegeFake.Grant(_roleB, _createContact, 8),        // another table
                    PrivilegeFake.Grant(_roleC, _createAccount, 0)         // a grant of nothing
                });
        }

        [Fact]
        public void OnlyRolesThatGrantThatPrivilegeOnThatTable_AreReturned_WithTheirWidestReach()
        {
            var grants = new SecurityService(Fake()).GetRoleGrantsFor(new[] { _roleA, _roleB, _roleC }, "account", PrivilegeKind.Create);

            var only = Assert.Single(grants);
            Assert.Equal(_roleA, only.Key);
            Assert.Equal(PrivilegeDepth.ParentChildBusinessUnits, only.Value);
        }

        [Fact]
        public void TheTableNameIsMatchedWhateverTheCase()
        {
            var grants = new SecurityService(Fake()).GetRoleGrantsFor(new[] { _roleB }, "Contact", PrivilegeKind.Create);

            Assert.Equal(PrivilegeDepth.Organization, grants[_roleB]);
        }

        [Fact]
        public void ATableWithoutThatPrivilege_GivesNothing()
        {
            Assert.Empty(new SecurityService(Fake()).GetRoleGrantsFor(new[] { _roleA }, "lead", PrivilegeKind.Create));
        }

        [Fact]
        public void OnlyTheGrantsOfTheGivenRolesAreAskedFor_NeverEveryGrantInTheOrganization()
        {
            var fake = Fake();

            new SecurityService(fake).GetRoleGrantsFor(new[] { _roleA }, "account", PrivilegeKind.Create);

            var grantQueries = fake.Queries.OfType<QueryExpression>().Where(q => q.EntityName == "roleprivileges").ToList();
            Assert.NotEmpty(grantQueries);
            Assert.All(grantQueries, q => Assert.Contains(q.Criteria.Conditions, c => c.AttributeName == "roleid"));
        }

        [Fact]
        public void TheSameRolesAreNotReadTwice()
        {
            var fake = Fake();
            var service = new SecurityService(fake);

            service.GetRoleGrantsFor(new[] { _roleA }, "account", PrivilegeKind.Create);
            service.GetRoleGrantsFor(new[] { _roleA }, "account", PrivilegeKind.Read);

            Assert.Single(fake.Queries.OfType<QueryExpression>(), q => q.EntityName == "roleprivileges");
        }

        [Fact]
        public void ThePrivilegeOfAnActionOnATable_IsLookedUpByActionAndTable_AndRemembered()
        {
            var fake = Fake();
            var service = new SecurityService(fake);

            var first = service.FindPrivilegeIds("account", PrivilegeKind.Create);
            var queries = fake.Queries.Count;
            var second = service.FindPrivilegeIds("ACCOUNT", PrivilegeKind.Create);

            Assert.Equal(new[] { _createAccount, _createAccountAgain }.OrderBy(i => i), first.OrderBy(i => i));
            Assert.Equal(queries, fake.Queries.Count);
            Assert.Equal(first, second);
        }
    }

    /// <summary>Dataverse keeps a role's privileges on its root role; the copies in other business units have none of their own.</summary>
    public class RootRolePrivilegeTests
    {
        private readonly Guid _read = Guid.NewGuid();
        private readonly Guid _root = Guid.NewGuid();
        private readonly Guid _copy = Guid.NewGuid();
        private readonly Guid _otherRoot = Guid.NewGuid();
        private readonly Guid _otherCopy = Guid.NewGuid();

        private FakeOrganizationService Fake()
        {
            return PrivilegeFake.Serve(
                new[] { PrivilegeFake.Privilege(_read, 1, "account") },
                new[] { PrivilegeFake.Grant(_root, _read, 4) }, // rows exist for the root only
                new[]
                {
                    PrivilegeFake.Role(_root, "Admin"), PrivilegeFake.Role(_copy, "Admin", _root),
                    PrivilegeFake.Role(_otherRoot, "Trader"), PrivilegeFake.Role(_otherCopy, "Trader", _otherRoot)
                });
        }

        [Fact]
        public void ACopyGetsThePrivilegesOfItsRootRole()
        {
            var grants = new SecurityService(Fake()).GetRolePrivileges(new[] { _copy });

            var grant = Assert.Single(grants[_copy]);
            Assert.Equal(_read, grant.PrivilegeId);
            Assert.Equal(PrivilegeDepth.ParentChildBusinessUnits, grant.Depth);
        }

        [Fact]
        public void TheRootsAreAskedFor_NotTheCopies()
        {
            var fake = Fake();

            new SecurityService(fake).GetRolePrivileges(new[] { _copy, _otherCopy });

            var query = fake.Queries.OfType<QueryExpression>().Single(q => q.EntityName == "roleprivileges");
            var asked = query.Criteria.Conditions.Single(c => c.AttributeName == "roleid").Values.Cast<Guid>().OrderBy(i => i).ToList();
            Assert.Equal(new[] { _root, _otherRoot }.OrderBy(i => i), asked);
        }

        [Fact]
        public void ACopyAndItsRoot_AreReadOnce()
        {
            var fake = Fake();

            var grants = new SecurityService(fake).GetRolePrivileges(new[] { _root, _copy });

            Assert.Single(grants[_root]);
            Assert.Single(grants[_copy]);
            var query = fake.Queries.OfType<QueryExpression>().Single(q => q.EntityName == "roleprivileges");
            Assert.Single(query.Criteria.Conditions.Single(c => c.AttributeName == "roleid").Values);
        }

        [Fact]
        public void ARoleThatIsNotKnown_IsAskedForAsItIs()
        {
            var stranger = Guid.NewGuid();

            var grants = new SecurityService(Fake()).GetRolePrivileges(new[] { stranger });

            Assert.Empty(grants[stranger]);
        }

        [Fact]
        public void TheGrantOfAPrivilegeReachesTheCopyToo()
        {
            var grants = new SecurityService(Fake()).GetRoleGrantsFor(new[] { _copy, _otherCopy }, "account", PrivilegeKind.Read);

            var only = Assert.Single(grants);
            Assert.Equal(_copy, only.Key);
            Assert.Equal(PrivilegeDepth.ParentChildBusinessUnits, only.Value);
        }
    }

    public class PrivilegeCatalogReadingTests
    {
        private static FakeOrganizationService Fake(object tableValue, int objectTypeCode = 1)
        {
            var fake = PrivilegeFake.Serve(new[] { PrivilegeFake.Privilege(Guid.NewGuid(), 1, tableValue) }, new Entity[0]);
            fake.OnExecute = request =>
            {
                var response = new RetrieveAllEntitiesResponse();
                var meta = new EntityMetadata { LogicalName = "account" };
                // The code is read-only in the SDK; Dataverse fills it in.
                typeof(EntityMetadata).GetProperty("ObjectTypeCode").GetSetMethod(true).Invoke(meta, new object[] { (int?)objectTypeCode });
                response.Results["EntityMetadata"] = new[] { meta };
                return response;
            };
            return fake;
        }

        [Fact]
        public void ATableGivenAsItsLogicalName_IsRead()
        {
            var catalog = new SecurityService(Fake("account")).GetPrivilegeCatalog();

            Assert.Equal("account", Assert.Single(Assert.Single(catalog).Value).Table);
        }

        [Fact]
        public void ATableGivenAsAnObjectTypeCode_IsLookedUpByItsMetadata()
        {
            var catalog = new SecurityService(Fake(1)).GetPrivilegeCatalog();

            Assert.Equal("account", Assert.Single(Assert.Single(catalog).Value).Table);
        }

        [Fact]
        public void ATableGivenAsAnOptionSetValue_IsLookedUpByItsMetadata()
        {
            var catalog = new SecurityService(Fake(new OptionSetValue(1))).GetPrivilegeCatalog();

            Assert.Equal("account", Assert.Single(Assert.Single(catalog).Value).Table);
        }

        [Fact]
        public void APrivilegeThatStandsForSeveralTables_KeepsEveryOne()
        {
            var shared = Guid.NewGuid();
            var fake = PrivilegeFake.Serve(
                new[] { PrivilegeFake.Privilege(shared, 1, "account"), PrivilegeFake.Privilege(shared, 1, "customeraddress"), PrivilegeFake.Privilege(shared, 1, "account") },
                new Entity[0]);

            var targets = new SecurityService(fake).GetPrivilegeCatalog()[shared];

            Assert.Equal(new[] { "account", "customeraddress" }, targets.Select(t => t.Table).OrderBy(t => t));
        }

        [Fact]
        public void ACodeThatNoTableHas_IsLeftOut()
        {
            Assert.Empty(new SecurityService(Fake(99)).GetPrivilegeCatalog());
        }
    }

    public class GrantCoverageTests
    {
        private static (FakeOrganizationService, Guid, Guid) Fake()
        {
            var create = Guid.NewGuid();
            var withRows = Guid.NewGuid();
            var withoutRows = Guid.NewGuid();
            var fake = PrivilegeFake.Serve(
                new[] { PrivilegeFake.Privilege(create, 32, "account"), PrivilegeFake.Privilege(Guid.NewGuid(), 1, "account") },
                new[] { PrivilegeFake.Grant(withRows, create, 4) });
            return (fake, withRows, withoutRows);
        }

        [Fact]
        public void TheCoverageSaysHowManyRolesWereCheckedAndHowManyCameBackWithPrivileges()
        {
            var (fake, withRows, withoutRows) = Fake();

            var coverage = new SecurityService(fake).GetGrantCoverage(new[] { withRows, withoutRows }, "account", PrivilegeKind.Create);

            Assert.Equal(2, coverage.RolesChecked);
            Assert.Equal(1, coverage.RolesWithPrivileges);
            Assert.Equal(1, coverage.PrivilegeRows);
            Assert.Equal(1, coverage.CatalogMatches);
        }

        [Fact]
        public void ATableWithoutThatPrivilege_HasNoMatch()
        {
            var (fake, withRows, _) = Fake();

            Assert.Equal(0, new SecurityService(fake).GetGrantCoverage(new[] { withRows }, "lead", PrivilegeKind.Create).CatalogMatches);
        }

        [Fact]
        public void TheDescription_SaysWhatTheAnswerRestsOn()
        {
            var text = AccessExplainer.DescribeCoverage(
                new GrantCoverage { RolesChecked = 15, RolesWithPrivileges = 14, PrivilegeRows = 9000, CatalogMatches = 1 }, PrivilegeKind.Create, "Account");

            Assert.Contains("Checked 15 role record(s): 14 came back with privileges (9000 rows)", text);
            Assert.Contains("Found 1 Create privilege record(s) for Account", text);
        }

        [Fact]
        public void WhenNoSuchPrivilegeExists_TheDescriptionSaysSo()
        {
            var text = AccessExplainer.DescribeCoverage(
                new GrantCoverage { RolesChecked = 3, RolesWithPrivileges = 3, PrivilegeRows = 100, CatalogMatches = 0 }, PrivilegeKind.Create, "Account");

            Assert.Contains("No Create privilege on Account was found.", text);
        }

        [Fact]
        public void ForAUserWithoutRoles_ItSaysThereIsNothingToCheck()
        {
            Assert.Equal("The user holds no role to check.", AccessExplainer.DescribeCoverage(new GrantCoverage(), PrivilegeKind.Read, "Account"));
        }
    }
}
