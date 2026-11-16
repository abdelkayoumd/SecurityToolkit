using System;
using System.Collections.Generic;
using System.Linq;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using Xunit;

namespace SecurityToolkit.Tests
{
    public class RedundantRolesTests
    {
        private static readonly Guid Bu = Guid.NewGuid(), OtherBu = Guid.NewGuid();
        private static readonly Guid P1 = Guid.NewGuid(), P2 = Guid.NewGuid();

        /// <summary>One user, the given direct roles and team roles, and what each role grants.</summary>
        private sealed class Scene
        {
            public readonly UserInfo User = new UserInfo { Id = Guid.NewGuid(), FullName = "Pat", BusinessUnitId = Bu, BusinessUnitName = "Sales" };
            public readonly List<RoleInfo> Roles = new List<RoleInfo>();
            public readonly List<TeamInfo> Teams = new List<TeamInfo>();
            public readonly List<UserRoleLink> Direct = new List<UserRoleLink>();
            public readonly List<UserTeamLink> Members = new List<UserTeamLink>();
            public readonly List<TeamRoleLink> TeamRoles = new List<TeamRoleLink>();
            public readonly Dictionary<Guid, IReadOnlyList<RoleGrant>> Grants = new Dictionary<Guid, IReadOnlyList<RoleGrant>>();

            public RoleInfo Role(string name, params (Guid Privilege, PrivilegeDepth Depth)[] grants)
            {
                var role = new RoleInfo { Id = Guid.NewGuid(), Name = name, BusinessUnitId = Bu, BusinessUnitName = "Sales" };
                Roles.Add(role);
                Grants[role.Id] = grants.Select(g => new RoleGrant { PrivilegeId = g.Privilege, Depth = g.Depth }).ToList();
                return role;
            }

            public void Hold(RoleInfo role) => Direct.Add(new UserRoleLink { UserId = User.Id, RoleId = role.Id });

            public void ThroughTeam(RoleInfo role, Guid businessUnit)
            {
                var team = new TeamInfo { Id = Guid.NewGuid(), Name = "Team " + role.Name, BusinessUnitId = businessUnit, TeamType = 0 };
                Teams.Add(team);
                Members.Add(new UserTeamLink { UserId = User.Id, TeamId = team.Id });
                TeamRoles.Add(new TeamRoleLink { TeamId = team.Id, RoleId = role.Id });
            }

            public List<CleanupRow> Find()
            {
                var graph = new SecurityGraph(new[] { User }, Roles, Teams,
                    new[] { new BusinessUnitInfo { Id = Bu, Name = "Sales" }, new BusinessUnitInfo { Id = OtherBu, Name = "Other" } },
                    Members, Direct, TeamRoles);
                return RedundantRoles.Find(graph, Grants).Rows.ToList();
            }
        }

        [Fact]
        public void ARoleWhoseEveryPrivilegeComesFromAnother_AddsNothing()
        {
            var scene = new Scene();
            var big = scene.Role("Big", (P1, PrivilegeDepth.BusinessUnit), (P2, PrivilegeDepth.Organization));
            var small = scene.Role("Small", (P1, PrivilegeDepth.User));
            scene.Hold(big);
            scene.Hold(small);

            var row = Assert.Single(scene.Find());
            Assert.Equal(small.Id, row.RoleId);
            Assert.Equal("Pat", row.Item);
            Assert.Equal("Small: all of it already comes from Big", row.Detail);
        }

        [Fact]
        public void ARoleThatReachesFurther_IsNeeded()
        {
            var scene = new Scene();
            scene.Hold(scene.Role("A", (P1, PrivilegeDepth.User)));
            scene.Hold(scene.Role("B", (P1, PrivilegeDepth.Organization)));

            Assert.Equal(new[] { "A" }, scene.Find().Select(r => r.Detail.Split(':')[0]));
        }

        [Fact]
        public void OfTwoRolesThatCoverEachOther_OnlyOneIsReported()
        {
            var scene = new Scene();
            scene.Hold(scene.Role("Twin 1", (P1, PrivilegeDepth.BusinessUnit)));
            scene.Hold(scene.Role("Twin 2", (P1, PrivilegeDepth.BusinessUnit)));

            Assert.Single(scene.Find());
        }

        [Fact]
        public void TheOnlyRole_IsNeeded_UnlessItGrantsNothing()
        {
            var scene = new Scene();
            scene.Hold(scene.Role("Only", (P1, PrivilegeDepth.User)));
            Assert.Empty(scene.Find());

            var empty = new Scene();
            empty.Hold(empty.Role("Empty"));
            Assert.Equal("Empty: grants no privilege at all", Assert.Single(empty.Find()).Detail);
        }

        [Fact]
        public void ATeamRole_CoversOnlyInTheUsersBusinessUnit_AndOnlyFromBusinessUnitDepth()
        {
            var same = new Scene();
            same.Hold(same.Role("Mine", (P1, PrivilegeDepth.BusinessUnit)));
            same.ThroughTeam(same.Role("Team role", (P1, PrivilegeDepth.Organization)), Bu);
            Assert.Equal("Mine: all of it already comes from Team role (team Team Team role)", Assert.Single(same.Find()).Detail);

            var elsewhere = new Scene();
            elsewhere.Hold(elsewhere.Role("Mine", (P1, PrivilegeDepth.BusinessUnit)));
            elsewhere.ThroughTeam(elsewhere.Role("Team role", (P1, PrivilegeDepth.Organization)), OtherBu);
            Assert.Empty(elsewhere.Find());

            var userLevel = new Scene();
            userLevel.Hold(userLevel.Role("Mine", (P1, PrivilegeDepth.User)));
            userLevel.ThroughTeam(userLevel.Role("Team role", (P1, PrivilegeDepth.User)), Bu);
            Assert.Empty(userLevel.Find());
        }

        [Fact]
        public void DisabledUsers_AreLeftOut()
        {
            var scene = new Scene();
            scene.User.IsDisabled = true;
            scene.Hold(scene.Role("Big", (P1, PrivilegeDepth.Organization)));
            scene.Hold(scene.Role("Small", (P1, PrivilegeDepth.User)));

            Assert.Empty(scene.Find());
        }

        [Fact]
        public void RemovingTheFindings_PlansTheRemovalOfJustThoseRoles()
        {
            var scene = new Scene();
            var big = scene.Role("Big", (P1, PrivilegeDepth.Organization));
            var small = scene.Role("Small", (P1, PrivilegeDepth.User));
            scene.Hold(big);
            scene.Hold(small);
            var graph = new SecurityGraph(new[] { scene.User }, scene.Roles, scene.Teams, new[] { new BusinessUnitInfo { Id = Bu, Name = "Sales" } },
                scene.Members, scene.Direct, scene.TeamRoles);

            var plan = ChangePlanner.PlanCleanup(graph, RedundantRoles.Find(graph, scene.Grants).Rows);

            var item = Assert.Single(plan.Items);
            Assert.Equal("Remove role", item.Action);
            Assert.Contains("Small", item.Target);
        }

        [Fact]
        public void TheRolesToRead_AreThoseEnabledUsersHold()
        {
            var org = new Org();
            var ids = RedundantRoles.RoleIdsToRead(org.Graph);

            Assert.Contains(org.Admin.Id, ids);       // Ada, directly
            Assert.Contains(org.SellerSales.Id, ids); // Bob, directly
            Assert.Contains(org.Auditor.Id, ids);     // Bob, through Reviewers
        }
    }
}
