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
    public class RoleGrantsForTests
    {
        private readonly Guid _createAccount = Guid.NewGuid();
        private readonly Guid _createAccountAgain = Guid.NewGuid();
        private readonly Guid _readAccount = Guid.NewGuid();
        private readonly Guid _createContact = Guid.NewGuid();
        private readonly Guid _roleA = Guid.NewGuid();
        private readonly Guid _roleB = Guid.NewGuid();
        private readonly Guid _roleC = Guid.NewGuid();

        private static Entity Privilege(Guid id, int right, string table) => new Entity("privilege", id)
        {
            ["accessright"] = right,
            ["otc.objecttypecode"] = new AliasedValue("privilegeobjecttypecodes", "objecttypecode", table)
        };

        private static Entity Grant(Guid role, Guid privilege, int mask) => new Entity("roleprivileges") { ["roleid"] = role, ["privilegeid"] = privilege, ["privilegedepthmask"] = mask };

        private FakeOrganizationService Fake()
        {
            return new FakeOrganizationService
            {
                OnRetrieveMultiple = q =>
                {
                    if (((QueryExpression)q).EntityName == "privilege")
                    {
                        return new EntityCollection(new List<Entity>
                        {
                            Privilege(_createAccount, 32, "account"), Privilege(_createAccountAgain, 32, "account"),
                            Privilege(_readAccount, 1, "account"), Privilege(_createContact, 32, "contact")
                        });
                    }

                    return new EntityCollection(new List<Entity>
                    {
                        Grant(_roleA, _createAccount, 1),
                        Grant(_roleA, _createAccountAgain, 4),   // widest of the two wins
                        Grant(_roleB, _readAccount, 8),          // another privilege
                        Grant(_roleB, _createContact, 8),        // another table
                        Grant(_roleC, _createAccount, 0)         // a grant of nothing
                    });
                }
            };
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
            var asked = fake.Queries.Count;
            service.GetRoleGrantsFor(new[] { _roleA }, "account", PrivilegeKind.Read);

            Assert.Equal(asked, fake.Queries.Count);
        }
    }
}

namespace SecurityToolkit.Tests
{
    public class GrantCoverageTests
    {
        private static Entity Privilege(Guid id, int right, string table) => new Entity("privilege", id)
        {
            ["accessright"] = right,
            ["otc.objecttypecode"] = new Microsoft.Xrm.Sdk.AliasedValue("privilegeobjecttypecodes", "objecttypecode", table)
        };

        private static (FakeOrganizationService, Guid, Guid) Fake()
        {
            var create = Guid.NewGuid();
            var withRows = Guid.NewGuid();
            var withoutRows = Guid.NewGuid();
            var fake = new FakeOrganizationService
            {
                OnRetrieveMultiple = q => ((QueryExpression)q).EntityName == "privilege"
                    ? new EntityCollection(new System.Collections.Generic.List<Entity> { Privilege(create, 32, "account"), Privilege(Guid.NewGuid(), 1, "account") })
                    : new EntityCollection(new System.Collections.Generic.List<Entity>
                    {
                        new Entity("roleprivileges") { ["roleid"] = withRows, ["privilegeid"] = create, ["privilegedepthmask"] = 4 }
                    })
            };
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
            Assert.Equal(2, coverage.CatalogSize);
        }

        [Fact]
        public void ATableWithoutThatPrivilege_HasNoCatalogMatch()
        {
            var (fake, withRows, _) = Fake();

            Assert.Equal(0, new SecurityService(fake).GetGrantCoverage(new[] { withRows }, "lead", PrivilegeKind.Create).CatalogMatches);
        }

        [Fact]
        public void TheDescription_SaysWhatTheAnswerRestsOn()
        {
            var text = AccessExplainer.DescribeCoverage(
                new GrantCoverage { RolesChecked = 15, RolesWithPrivileges = 14, PrivilegeRows = 9000, CatalogMatches = 1, CatalogSize = 5600 }, PrivilegeKind.Create, "Account");

            Assert.Contains("Checked 15 role record(s): 14 came back with privileges (9000 rows)", text);
            Assert.Contains("1 Create privilege record(s) for Account", text);
        }

        [Fact]
        public void WhenTheCatalogHasNoSuchPrivilege_TheDescriptionSaysSo()
        {
            var text = AccessExplainer.DescribeCoverage(
                new GrantCoverage { RolesChecked = 3, RolesWithPrivileges = 3, PrivilegeRows = 100, CatalogMatches = 0, CatalogSize = 5600 }, PrivilegeKind.Create, "Account");

            Assert.Contains("No Create privilege on Account was found in the privilege catalog (5600 privileges read)", text);
        }

        [Fact]
        public void ForAUserWithoutRoles_ItSaysThereIsNothingToCheck()
        {
            Assert.Equal("The user holds no role to check.", AccessExplainer.DescribeCoverage(new GrantCoverage(), PrivilegeKind.Read, "Account"));
        }
    }
}
