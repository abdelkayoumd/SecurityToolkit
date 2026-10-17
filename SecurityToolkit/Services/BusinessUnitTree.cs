using System;
using System.Collections.Generic;
using System.Linq;
using SecurityToolkit.Models;

namespace SecurityToolkit.Services
{
    /// <summary>One business unit in the hierarchy, with what sits in it directly and in everything below it.</summary>
    internal sealed class BusinessUnitNode
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public List<BusinessUnitNode> Children { get; } = new List<BusinessUnitNode>();

        /// <summary>Active users whose business unit is this one.</summary>
        public int Users { get; set; }

        /// <summary>Owner, access and group teams of this business unit, not counting its automatic default team.</summary>
        public int Teams { get; set; }

        /// <summary>The same two counts including every business unit below.</summary>
        public int TotalUsers { get; set; }
        public int TotalTeams { get; set; }
    }

    /// <summary>One thing in a business unit: a user, a team, or a security role in use there.</summary>
    internal sealed class BusinessUnitItem
    {
        public string Type { get; set; }
        public string Name { get; set; }
        public string BusinessUnit { get; set; }
        public string Detail { get; set; }
        public string Status { get; set; }
    }

    /// <summary>
    /// The business-unit hierarchy and what is in each unit. Pure logic over the loaded security data.
    /// </summary>
    internal static class BusinessUnitTree
    {
        private static readonly StringComparer Names = StringComparer.CurrentCultureIgnoreCase;

        public const string UserType = "User";
        public const string TeamType = "Team";
        public const string RoleType = "Role in use";

        /// <summary>The root business unit(s), each with its children, sorted by name.</summary>
        public static IReadOnlyList<BusinessUnitNode> Build(SecurityGraph graph)
        {
            var nodes = graph.BusinessUnits.ToDictionary(b => b.Id, b => new BusinessUnitNode
            {
                Id = b.Id,
                Name = b.Name,
                Users = graph.Users.Count(u => u.BusinessUnitId == b.Id && !u.IsDisabled),
                Teams = graph.Teams.Count(t => t.BusinessUnitId == b.Id && !t.IsDefault)
            });

            var roots = new List<BusinessUnitNode>();
            foreach (var unit in graph.BusinessUnits.OrderBy(b => b.Name, Names))
            {
                var node = nodes[unit.Id];
                if (unit.ParentId.HasValue && nodes.TryGetValue(unit.ParentId.Value, out var parent))
                {
                    parent.Children.Add(node);
                }
                else
                {
                    roots.Add(node);
                }
            }

            foreach (var root in roots)
            {
                Total(root);
            }

            return roots;
        }

        private static void Total(BusinessUnitNode node)
        {
            node.TotalUsers = node.Users;
            node.TotalTeams = node.Teams;
            foreach (var child in node.Children)
            {
                Total(child);
                node.TotalUsers += child.TotalUsers;
                node.TotalTeams += child.TotalTeams;
            }
        }

        /// <summary>Everything in one business unit (and, if asked, everything below it): users, teams and the roles they hold.</summary>
        public static IReadOnlyList<BusinessUnitItem> Contents(SecurityGraph graph, Guid businessUnitId, bool includeChildren, bool includeDisabled)
        {
            var scope = graph.BusinessUnitScope(businessUnitId, includeChildren);
            var items = new List<BusinessUnitItem>();

            var users = graph.UsersInBusinessUnits(scope, includeDisabled).OrderBy(u => u.FullName, Names).ToList();
            foreach (var user in users)
            {
                var direct = graph.DirectRolesOf(user.Id).Count;
                items.Add(new BusinessUnitItem
                {
                    Type = UserType,
                    Name = user.FullName,
                    BusinessUnit = user.BusinessUnitName,
                    Detail = $"{direct} role{(direct == 1 ? string.Empty : "s")} directly, {graph.TeamsOf(user.Id).Count(t => !t.IsDefault)} team(s)",
                    Status = user.Status
                });
            }

            var teams = graph.Teams.Where(t => t.BusinessUnitId.HasValue && scope.Contains(t.BusinessUnitId.Value) && !t.IsDefault)
                .OrderBy(t => t.Name, Names).ToList();
            foreach (var team in teams)
            {
                items.Add(new BusinessUnitItem
                {
                    Type = TeamType,
                    Name = team.Name,
                    BusinessUnit = team.BusinessUnitName,
                    Detail = $"{team.TypeName} team, {graph.MembersOf(team.Id).Count} member(s), {graph.RolesOfTeam(team.Id).Count} role(s)",
                    Status = string.Empty
                });
            }

            // Every role exists as a copy in every business unit, so list the roles actually held here.
            var heldByUsers = users.SelectMany(u => graph.DirectRolesOf(u.Id).Select(r => new { r.RootKey, r.Name, r.IsManaged }))
                .GroupBy(x => x.RootKey).ToDictionary(g => g.Key, g => g.ToList());
            var heldByTeams = teams.SelectMany(t => graph.RolesOfTeam(t.Id).Select(r => new { r.RootKey, r.Name, r.IsManaged }))
                .GroupBy(x => x.RootKey).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var key in heldByUsers.Keys.Union(heldByTeams.Keys))
            {
                var byUsers = heldByUsers.TryGetValue(key, out var u) ? u : null;
                var byTeams = heldByTeams.TryGetValue(key, out var t) ? t : null;
                var sample = (byUsers ?? byTeams)[0];
                items.Add(new BusinessUnitItem
                {
                    Type = RoleType,
                    Name = sample.Name,
                    BusinessUnit = string.Empty,
                    Detail = $"Held by {byUsers?.Count ?? 0} user(s) and {byTeams?.Count ?? 0} team(s)",
                    Status = sample.IsManaged ? "Managed" : "Custom"
                });
            }

            return items.OrderBy(i => Array.IndexOf(new[] { UserType, TeamType, RoleType }, i.Type)).ThenBy(i => i.Name, Names).ToList();
        }

        /// <summary>The text of a node: the name and what is in it.</summary>
        public static string Describe(BusinessUnitNode node)
        {
            var below = node.Children.Count > 0 && (node.TotalUsers != node.Users || node.TotalTeams != node.Teams)
                ? $"  |  {node.TotalUsers} user{(node.TotalUsers == 1 ? string.Empty : "s")}, {node.TotalTeams} team{(node.TotalTeams == 1 ? string.Empty : "s")} with children"
                : string.Empty;
            return $"{node.Name}   {node.Users} user{(node.Users == 1 ? string.Empty : "s")}, {node.Teams} team{(node.Teams == 1 ? string.Empty : "s")}{below}";
        }
    }
}
