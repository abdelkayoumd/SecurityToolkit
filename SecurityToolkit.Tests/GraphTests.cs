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
    /// <summary>
    /// A small organization:  Root BU -> Sales BU -> Emea BU.
    /// Ada (Root) holds Admin directly. Bob (Sales) is in team Reviewers, which holds Auditor, and holds Seller directly.
    /// Cy (Emea, disabled) is in Reviewers. Dee (Sales) has nothing.
    /// "Seller" exists as two business-unit copies sharing one root role.
    /// </summary>
    internal sealed class Org
    {
        public Guid Root = Guid.NewGuid(), Sales = Guid.NewGuid(), Emea = Guid.NewGuid();
        public UserInfo Ada, Bob, Cy, Dee;
        public RoleInfo Admin, Auditor, SellerRoot, SellerSales;
        public TeamInfo Reviewers, Empty;
        public SecurityGraph Graph;

        public Org()
        {
            Ada = new UserInfo { Id = Guid.NewGuid(), FullName = "Ada", DomainName = "ada@x", BusinessUnitId = Root, BusinessUnitName = "Root" };
            Bob = new UserInfo { Id = Guid.NewGuid(), FullName = "Bob", DomainName = "bob@x", BusinessUnitId = Sales, BusinessUnitName = "Sales" };
            Cy = new UserInfo { Id = Guid.NewGuid(), FullName = "Cy", DomainName = "cy@x", BusinessUnitId = Emea, BusinessUnitName = "Emea", IsDisabled = true };
            Dee = new UserInfo { Id = Guid.NewGuid(), FullName = "Dee", DomainName = "dee@x", BusinessUnitId = Sales, BusinessUnitName = "Sales" };

            Admin = new RoleInfo { Id = Guid.NewGuid(), Name = "Admin", BusinessUnitName = "Root" };
            Auditor = new RoleInfo { Id = Guid.NewGuid(), Name = "Auditor", BusinessUnitName = "Root" };
            SellerRoot = new RoleInfo { Id = Guid.NewGuid(), Name = "Seller", BusinessUnitName = "Root" };
            SellerSales = new RoleInfo { Id = Guid.NewGuid(), Name = "Seller", BusinessUnitName = "Sales", RootRoleId = SellerRoot.Id };

            Reviewers = new TeamInfo { Id = Guid.NewGuid(), Name = "Reviewers", TeamType = 0 };
            Empty = new TeamInfo { Id = Guid.NewGuid(), Name = "Empty team", TeamType = 1 };

            Graph = new SecurityGraph(
                new[] { Dee, Cy, Bob, Ada },
                new[] { Admin, Auditor, SellerRoot, SellerSales },
                new[] { Reviewers, Empty },
                new[]
                {
                    new BusinessUnitInfo { Id = Root, Name = "Root" },
                    new BusinessUnitInfo { Id = Sales, Name = "Sales", ParentId = Root },
                    new BusinessUnitInfo { Id = Emea, Name = "Emea", ParentId = Sales }
                },
                new[]
                {
                    new UserTeamLink { UserId = Bob.Id, TeamId = Reviewers.Id },
                    new UserTeamLink { UserId = Cy.Id, TeamId = Reviewers.Id },
                    new UserTeamLink { UserId = Bob.Id, TeamId = Empty.Id }
                },
                new[]
                {
                    new UserRoleLink { UserId = Ada.Id, RoleId = Admin.Id },
                    new UserRoleLink { UserId = Bob.Id, RoleId = SellerSales.Id }
                },
                new[] { new TeamRoleLink { TeamId = Reviewers.Id, RoleId = Auditor.Id } });
        }
    }

    public class SecurityGraphTests
    {
        private readonly Org _o = new Org();

        [Fact]
        public void UsersInRole_ReturnsDirectHoldersAndTeamMembers()
        {
            var rows = _o.Graph.UsersInRole(_o.Auditor.RootKey, includeDisabled: true);

            Assert.Equal(new[] { "Bob", "Cy" }, rows.Select(r => r.User));
            Assert.All(rows, r => Assert.Equal("Via team", r.Assignment));
            Assert.All(rows, r => Assert.Equal("Reviewers", r.Team));
        }

        [Fact]
        public void UsersInRole_HidesDisabledUsersUnlessAsked()
        {
            Assert.Equal(new[] { "Bob" }, _o.Graph.UsersInRole(_o.Auditor.RootKey, false).Select(r => r.User));
        }

        [Fact]
        public void UsersInRole_TreatsBusinessUnitCopiesAsTheSameRole()
        {
            // Bob holds the Sales copy; asking for the root role's key must still find him.
            var rows = _o.Graph.UsersInRole(_o.SellerRoot.RootKey, false);

            Assert.Equal("Bob", Assert.Single(rows).User);
            Assert.Equal("Direct", rows[0].Assignment);
        }

        [Fact]
        public void RoleChoices_ListsEachRoleOnceWithItsCopyCount()
        {
            var choices = _o.Graph.RoleChoices();

            Assert.Equal(new[] { "Admin", "Auditor", "Seller" }, choices.Select(c => c.Name));
            Assert.Equal(2, choices.Single(c => c.Name == "Seller").Copies);
        }

        [Fact]
        public void TeamMembers_ListsMembersPerTeam()
        {
            var rows = _o.Graph.TeamMembers(new[] { _o.Reviewers.Id, _o.Empty.Id }, includeDisabled: false);

            Assert.Equal(new[] { "Empty team/Bob", "Reviewers/Bob" }, rows.Select(r => r.Team + "/" + r.User));
            Assert.Equal("Owner", rows.Single(r => r.Team == "Reviewers").TeamType);
        }

        [Fact]
        public void BusinessUnitScope_IncludesDescendantsOnlyWhenAsked()
        {
            Assert.Equal(new[] { _o.Sales }, _o.Graph.BusinessUnitScope(_o.Sales, false));
            Assert.Equal(new[] { _o.Sales, _o.Emea }.OrderBy(g => g), _o.Graph.BusinessUnitScope(_o.Sales, true).OrderBy(g => g));
            Assert.Equal(3, _o.Graph.BusinessUnitScope(_o.Root, true).Count);
        }

        [Fact]
        public void BuildUserAccess_SeparatesDirectRolesFromRolesInheritedFromTeams()
        {
            var scope = _o.Graph.BusinessUnitScope(_o.Sales, includeChildren: false);
            _o.Graph.BuildUserAccess(_o.Graph.UsersInBusinessUnits(scope, includeDisabled: false), out var summary, out var detail);

            var bob = summary.Single(s => s.User == "Bob");
            Assert.Equal("Seller", bob.DirectRoles);
            Assert.Equal("Auditor (Reviewers)", bob.TeamRoles);
            Assert.Equal("Empty team; Reviewers", bob.Teams);

            Assert.Contains(detail, d => d.User == "Bob" && d.Role == "Seller" && d.Source == "Direct role");
            Assert.Contains(detail, d => d.User == "Bob" && d.Team == "Reviewers" && d.Role == "Auditor" && d.Source == "Inherited from team");
            Assert.Contains(detail, d => d.User == "Bob" && d.Team == "Empty team" && d.Source == "Team member (no roles)");
        }

        [Fact]
        public void BuildUserAccess_KeepsUsersWithNothingVisible()
        {
            _o.Graph.BuildUserAccess(new[] { _o.Dee }, out var summary, out var detail);

            Assert.Equal("Dee", Assert.Single(summary).User);
            Assert.Equal("No team and no role", Assert.Single(detail).Source);
        }

        [Fact]
        public void UsersInBusinessUnits_FiltersByScopeAndStatus()
        {
            var scope = _o.Graph.BusinessUnitScope(_o.Root, includeChildren: true);

            Assert.Equal(new[] { "Ada", "Bob", "Dee" }, _o.Graph.UsersInBusinessUnits(scope, false).Select(u => u.FullName));
            Assert.Equal(4, _o.Graph.UsersInBusinessUnits(scope, true).Count);
        }
    }

    public class PrivilegeTests
    {
        [Theory]
        [InlineData(1, (int)PrivilegeDepth.User)]
        [InlineData(2, (int)PrivilegeDepth.BusinessUnit)]
        [InlineData(4, (int)PrivilegeDepth.ParentChildBusinessUnits)]
        [InlineData(8, (int)PrivilegeDepth.Organization)]
        [InlineData(3, (int)PrivilegeDepth.BusinessUnit)]
        [InlineData(15, (int)PrivilegeDepth.Organization)]
        [InlineData(0, (int)PrivilegeDepth.None)]
        public void DepthFromMask_UsesTheWidestBit(int mask, int expected)
        {
            Assert.Equal((PrivilegeDepth)expected, Privileges.DepthFromMask(mask));
        }

        [Theory]
        [InlineData((int)PrivilegeKind.Read, 1)]
        [InlineData((int)PrivilegeKind.Write, 2)]
        [InlineData((int)PrivilegeKind.Append, 4)]
        [InlineData((int)PrivilegeKind.AppendTo, 16)]
        [InlineData((int)PrivilegeKind.Create, 32)]
        [InlineData((int)PrivilegeKind.Delete, 65536)]
        [InlineData((int)PrivilegeKind.Share, 262144)]
        [InlineData((int)PrivilegeKind.Assign, 524288)]
        public void AccessRight_MatchesDataverseValues(int kind, int expected)
        {
            Assert.Equal(expected, Privileges.AccessRight((PrivilegeKind)kind));
        }

        private static Entity Role(Guid id, string name, string bu, Guid? root = null)
        {
            var e = new Entity("role", id) { ["name"] = name, ["businessunitid"] = new EntityReference("businessunit", Guid.NewGuid()) { Name = bu } };
            if (root.HasValue)
            {
                e["parentrootroleid"] = new EntityReference("role", root.Value);
            }

            return e;
        }

        private static Entity Grant(Guid roleId, int mask)
        {
            return new Entity("roleprivileges") { ["roleid"] = roleId, ["privilegedepthmask"] = mask };
        }

        [Fact]
        public void FindRolesWithPrivilege_ReturnsRolesAtTheRequestedDepthOrWider()
        {
            var user = Guid.NewGuid(); var bu = Guid.NewGuid(); var org = Guid.NewGuid();
            var fake = new FakeOrganizationService();
            fake.OnRetrieveMultiple = q =>
            {
                var query = (QueryExpression)q;
                switch (query.EntityName)
                {
                    case "privilege":
                        return new EntityCollection(new List<Entity> { new Entity("privilege", Guid.NewGuid()) });
                    case "roleprivileges":
                        return new EntityCollection(new List<Entity> { Grant(user, 1), Grant(bu, 2), Grant(org, 8) });
                    case "role":
                        return new EntityCollection(new List<Entity> { Role(user, "Basic role", "Root"), Role(bu, "Mid role", "Root"), Role(org, "Org role", "Root") });
                    default:
                        return new EntityCollection();
                }
            };

            var rows = new SecurityService(fake).FindRolesWithPrivilege("account", PrivilegeKind.Create, PrivilegeDepth.BusinessUnit);

            Assert.Equal(new[] { "Mid role", "Org role" }, rows.Select(r => r.Role));
            Assert.Equal(new[] { "Business Unit", "Organization" }, rows.Select(r => r.Depth));

            var privilegeQuery = (QueryExpression)fake.Queries.First();
            Assert.Contains(privilegeQuery.Criteria.Conditions, c => c.AttributeName == "accessright" && (int)c.Values[0] == 32);
            Assert.Contains(privilegeQuery.LinkEntities[0].LinkCriteria.Conditions, c => c.AttributeName == "objecttypecode" && (string)c.Values[0] == "account");
        }

        [Fact]
        public void FindRolesWithPrivilege_ReturnsNothingWhenThePrivilegeDoesNotExist()
        {
            var fake = new FakeOrganizationService();
            var rows = new SecurityService(fake).FindRolesWithPrivilege("account", PrivilegeKind.Read, PrivilegeDepth.User);

            Assert.Empty(rows);
            Assert.Single(fake.Queries);
        }

        private static EntityMetadata Intersect(string logicalName)
        {
            var metadata = new EntityMetadata { LogicalName = logicalName };
            typeof(EntityMetadata).GetProperty("IsIntersect").GetSetMethod(true).Invoke(metadata, new object[] { (bool?)true });
            return metadata;
        }

        [Fact]
        public void GetTables_SkipsRelationshipTablesAndSortsByDisplayName()
        {
            var fake = new FakeOrganizationService();
            fake.OnExecute = r =>
            {
                var response = new RetrieveAllEntitiesResponse();
                response.Results["EntityMetadata"] = new EntityMetadata[]
                {
                    new EntityMetadata { LogicalName = "zeta" },
                    new EntityMetadata { LogicalName = "contact", DisplayName = new Label(new LocalizedLabel("Contact", 1033), new LocalizedLabel[0]) },
                    Intersect("link")
                };
                return response;
            };

            var tables = new SecurityService(fake).GetTables();

            Assert.Equal(new[] { "contact", "zeta" }, tables.Select(t => t.LogicalName));
            Assert.Equal("Contact", tables[0].DisplayName);
        }
    }
}
