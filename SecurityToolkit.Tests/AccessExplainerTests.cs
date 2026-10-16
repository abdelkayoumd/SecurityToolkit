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
    public class AccessExplainerTests
    {
        private readonly Org _o = new Org();

        /// <summary>Admin reaches the organization, Auditor its business unit, and only the Root copy of Seller the user's own records.</summary>
        private IReadOnlyDictionary<Guid, PrivilegeDepth> Grants()
        {
            return new Dictionary<Guid, PrivilegeDepth>
            {
                [_o.Admin.Id] = PrivilegeDepth.Organization,
                [_o.Auditor.Id] = PrivilegeDepth.BusinessUnit,
                [_o.SellerRoot.Id] = PrivilegeDepth.User
            };
        }

        [Fact]
        public void ARoleAssignedDirectly_IsARouteViaDirect()
        {
            var answer = AccessExplainer.Explain(_o.Graph, _o.Ada, Grants());

            var route = Assert.Single(answer.Routes);
            Assert.Equal("Admin", route.Role);
            Assert.Equal("Direct", route.Via);
            Assert.Equal("Organization", route.Depth);
            Assert.False(route.ViaTeam);
        }

        [Fact]
        public void ARoleReachedThroughATeam_NamesTheTeam()
        {
            var answer = AccessExplainer.Explain(_o.Graph, _o.Bob, Grants());

            var route = Assert.Single(answer.Routes);
            Assert.Equal("Auditor", route.Role);
            Assert.Equal("Team: Reviewers", route.Via);
            Assert.True(route.ViaTeam);
        }

        [Fact]
        public void OnlyTheRoleRecordsTheUserActuallyHolds_Count_NotOtherBusinessUnitsCopies()
        {
            // Bob holds the Sales copy of Seller; the grant is on the Root copy, which he does not hold.
            var answer = AccessExplainer.Explain(_o.Graph, _o.Bob, Grants());

            Assert.DoesNotContain(answer.Routes, r => r.Role == "Seller");
        }

        [Fact]
        public void AUserWithNoGrantingRole_HasNoRoutes()
        {
            var answer = AccessExplainer.Explain(_o.Graph, _o.Dee, Grants());

            Assert.False(answer.HasAccess);
            Assert.Equal(PrivilegeDepth.None, answer.Best);
            Assert.Empty(answer.Routes);
        }

        [Fact]
        public void ARoleWithADepthOfNone_IsNotARoute()
        {
            var grants = new Dictionary<Guid, PrivilegeDepth> { [_o.Admin.Id] = PrivilegeDepth.None };

            Assert.False(AccessExplainer.Explain(_o.Graph, _o.Ada, grants).HasAccess);
        }

        [Fact]
        public void SeveralRoutes_AreOrderedWidestFirst_AndTheWidestIsMarked()
        {
            // Give Bob a second, direct route that reaches further than the one through his team.
            var graph = new SecurityGraph(_o.Graph.Users, _o.Graph.Roles, _o.Graph.Teams, _o.Graph.BusinessUnits,
                new[] { new UserTeamLink { UserId = _o.Bob.Id, TeamId = _o.Reviewers.Id } },
                new[] { new UserRoleLink { UserId = _o.Bob.Id, RoleId = _o.Admin.Id } },
                new[] { new TeamRoleLink { TeamId = _o.Reviewers.Id, RoleId = _o.Auditor.Id } });

            var answer = AccessExplainer.Explain(graph, _o.Bob, Grants());

            Assert.Equal(new[] { "Admin", "Auditor" }, answer.Routes.Select(r => r.Role));
            Assert.Equal(new[] { "✓", string.Empty }, answer.Routes.Select(r => r.Strongest));
            Assert.Equal(PrivilegeDepth.Organization, answer.Best);
            Assert.Equal(1, answer.DirectCount);
            Assert.Equal(1, answer.TeamCount);
        }

        [Fact]
        public void TwoRoutesOfTheSameReach_AreBothMarkedStrongest()
        {
            var graph = new SecurityGraph(_o.Graph.Users, _o.Graph.Roles, _o.Graph.Teams, _o.Graph.BusinessUnits,
                new[] { new UserTeamLink { UserId = _o.Bob.Id, TeamId = _o.Reviewers.Id } },
                new[] { new UserRoleLink { UserId = _o.Bob.Id, RoleId = _o.Auditor.Id } },
                new[] { new TeamRoleLink { TeamId = _o.Reviewers.Id, RoleId = _o.Auditor.Id } });

            var answer = AccessExplainer.Explain(graph, _o.Bob, Grants());

            Assert.Equal(2, answer.Routes.Count);
            Assert.All(answer.Routes, r => Assert.Equal("✓", r.Strongest));
        }

        [Fact]
        public void TheSummary_SaysWhatTheUserCanDo_AndHowFar()
        {
            var summary = AccessExplainer.Summarize(AccessExplainer.Explain(_o.Graph, _o.Ada, Grants()), PrivilegeKind.Delete, "Account");

            Assert.Equal("Ada can Delete Account at Organization level, through 1 route (1 direct, 0 through teams).", summary);
        }

        [Fact]
        public void TheSummary_SaysWhenThereIsNoAccess()
        {
            var summary = AccessExplainer.Summarize(AccessExplainer.Explain(_o.Graph, _o.Dee, Grants()), PrivilegeKind.AppendTo, "Contact");

            Assert.Equal("Dee has no role that grants Append To on Contact.", summary);
        }

        [Fact]
        public void TheTeamCaveat_AppliesOnlyWhenTheStrongestRouteIsEntirelyThroughTeams()
        {
            Assert.True(AccessExplainer.NeedsTeamCaveat(AccessExplainer.Explain(_o.Graph, _o.Bob, Grants())));
            Assert.False(AccessExplainer.NeedsTeamCaveat(AccessExplainer.Explain(_o.Graph, _o.Ada, Grants())));
            Assert.False(AccessExplainer.NeedsTeamCaveat(AccessExplainer.Explain(_o.Graph, _o.Dee, Grants())));
        }
    }

    public class RoleGrantsTests
    {
        [Fact]
        public void GetRoleGrants_ReturnsTheDepthPerRoleRecord_TheWidestWhenAPrivilegeIsRepeated()
        {
            var roleA = Guid.NewGuid();
            var roleB = Guid.NewGuid();
            var fake = new FakeOrganizationService();
            fake.OnRetrieveMultiple = q =>
            {
                switch (((QueryExpression)q).EntityName)
                {
                    case "privilege":
                        return new EntityCollection(new List<Entity> { new Entity("privilege", Guid.NewGuid()), new Entity("privilege", Guid.NewGuid()) });
                    case "roleprivileges":
                        return new EntityCollection(new List<Entity>
                        {
                            new Entity("roleprivileges") { ["roleid"] = roleA, ["privilegedepthmask"] = 1 },
                            new Entity("roleprivileges") { ["roleid"] = roleA, ["privilegedepthmask"] = 4 },
                            new Entity("roleprivileges") { ["roleid"] = roleB, ["privilegedepthmask"] = 8 }
                        });
                    default:
                        return new EntityCollection();
                }
            };

            var grants = new SecurityService(fake).GetRoleGrants("account", PrivilegeKind.Delete);

            Assert.Equal(PrivilegeDepth.ParentChildBusinessUnits, grants[roleA]);
            Assert.Equal(PrivilegeDepth.Organization, grants[roleB]);
            Assert.Equal(2, grants.Count);
        }

        [Fact]
        public void GetRoleGrants_IsEmptyWhenThePrivilegeDoesNotExist()
        {
            var fake = new FakeOrganizationService();

            var grants = new SecurityService(fake).GetRoleGrants("account", PrivilegeKind.Read);

            Assert.Empty(grants);
            Assert.Single(fake.Queries); // no point asking for grants of a privilege that is not there
        }
    }
}
