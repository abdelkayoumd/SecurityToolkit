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
