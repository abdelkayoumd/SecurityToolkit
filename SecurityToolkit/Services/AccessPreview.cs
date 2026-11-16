using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using SecurityToolkit.Models;

namespace SecurityToolkit.Services
{
    /// <summary>One table privilege of one user that a planned change widens, narrows, gives or takes away.</summary>
    internal sealed class AccessChangeRow
    {
        public string User { get; set; }
        public string Table { get; set; }
        public string Privilege { get; set; }
        public string Before { get; set; }
        public string After { get; set; }

        /// <summary>"Gains", "Loses", "Wider" or "Narrower".</summary>
        public string Change { get; set; }

        public bool IsLoss => Change == "Loses" || Change == "Narrower";
    }

    /// <summary>The role records a user holds (directly and through teams) before and after a planned change.</summary>
    internal sealed class RoleSwap
    {
        public UserInfo User { get; set; }
        public IReadOnlyList<Guid> Before { get; set; }
        public IReadOnlyList<Guid> After { get; set; }
    }

    /// <summary>
    /// "What if": plays a change plan on a copy of who holds what (roles of users, roles of teams, team membership)
    /// and compares each affected user's reach per table and privilege before and after. Nothing is sent anywhere.
    /// </summary>
    internal static class AccessPreview
    {
        private static readonly StringComparer Names = StringComparer.CurrentCultureIgnoreCase;

        /// <summary>True when the change can alter what someone may do: a role or a team membership is added or removed.</summary>
        public static bool ChangesAccess(ChangeItem item)
        {
            return Link(item.Request, out _, out _, out _, out _);
        }

        /// <summary>Every user whose roles the plan changes, with their role records before and after.</summary>
        public static IReadOnlyList<RoleSwap> Simulate(SecurityGraph graph, IEnumerable<ChangeItem> items)
        {
            var userRoles = graph.Users.ToDictionary(u => u.Id, u => new HashSet<Guid>(graph.DirectRolesOf(u.Id).Select(r => r.Id)));
            var teamRoles = graph.Teams.ToDictionary(t => t.Id, t => new HashSet<Guid>(graph.RolesOfTeam(t.Id).Select(r => r.Id)));
            var members = graph.Teams.ToDictionary(t => t.Id, t => new HashSet<Guid>());
            foreach (var user in graph.Users)
            {
                foreach (var team in graph.TeamsOf(user.Id))
                {
                    members[team.Id].Add(user.Id);
                }
            }

            HashSet<Guid> Effective(Guid userId)
            {
                var roles = new HashSet<Guid>(userRoles.TryGetValue(userId, out var direct) ? direct : new HashSet<Guid>());
                foreach (var team in members.Where(m => m.Value.Contains(userId)))
                {
                    roles.UnionWith(teamRoles.TryGetValue(team.Key, out var held) ? held : new HashSet<Guid>());
                }

                return roles;
            }

            var touched = new HashSet<Guid>();
            var before = new Dictionary<Guid, HashSet<Guid>>();
            void Touch(Guid userId)
            {
                if (userRoles.ContainsKey(userId) && touched.Add(userId))
                {
                    before[userId] = Effective(userId);
                }
            }

            foreach (var item in items)
            {
                if (!Link(item.Request, out var add, out var relationship, out var target, out var related))
                {
                    continue;
                }

                switch (relationship)
                {
                    case ChangePlanner.UserRoleRelationship:
                        var userId = target.LogicalName == "systemuser" ? target.Id : related.Id;
                        var roleId = target.LogicalName == "systemuser" ? related.Id : target.Id;
                        Touch(userId);
                        if (userRoles.TryGetValue(userId, out var mine))
                        {
                            Apply(mine, roleId, add);
                        }

                        break;
                    case ChangePlanner.TeamRoleRelationship:
                        var teamId = target.LogicalName == "team" ? target.Id : related.Id;
                        var teamRoleId = target.LogicalName == "team" ? related.Id : target.Id;
                        foreach (var member in members.TryGetValue(teamId, out var inTeam) ? inTeam.ToList() : new List<Guid>())
                        {
                            Touch(member);
                        }

                        if (teamRoles.TryGetValue(teamId, out var theirs))
                        {
                            Apply(theirs, teamRoleId, add);
                        }

                        break;
                    case ChangePlanner.TeamMembershipRelationship:
                        var team = target.LogicalName == "team" ? target.Id : related.Id;
                        var person = target.LogicalName == "team" ? related.Id : target.Id;
                        Touch(person);
                        if (members.TryGetValue(team, out var people))
                        {
                            Apply(people, person, add);
                        }

                        break;
                }
            }

            var users = graph.Users.ToDictionary(u => u.Id);
            return touched.Select(id => new RoleSwap { User = users[id], Before = before[id].ToList(), After = Effective(id).ToList() })
                .OrderBy(s => s.User.FullName, Names).ToList();
        }

        /// <summary>The role records whose privileges the comparison needs.</summary>
        public static IReadOnlyList<Guid> RoleIdsNeeded(IEnumerable<RoleSwap> swaps)
        {
            return swaps.SelectMany(s => s.Before.Concat(s.After)).Distinct().ToList();
        }

        /// <summary>For each user, every table privilege whose reach the change alters. Losses come first: they are what to check.</summary>
        public static IReadOnlyList<AccessChangeRow> Compare(
            IEnumerable<RoleSwap> swaps,
            IReadOnlyDictionary<Guid, IReadOnlyList<PrivilegeTarget>> catalog,
            IReadOnlyDictionary<Guid, IReadOnlyList<RoleGrant>> grants,
            IEnumerable<TableInfo> tables)
        {
            var names = tables.GroupBy(t => t.LogicalName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().DisplayName, StringComparer.OrdinalIgnoreCase);
            var rows = new List<AccessChangeRow>();
            foreach (var swap in swaps)
            {
                var was = Reach(swap.Before, catalog, grants);
                var will = Reach(swap.After, catalog, grants);
                foreach (var key in was.Keys.Union(will.Keys))
                {
                    var from = was.TryGetValue(key, out var a) ? a : PrivilegeDepth.None;
                    var to = will.TryGetValue(key, out var b) ? b : PrivilegeDepth.None;
                    if (from == to)
                    {
                        continue;
                    }

                    rows.Add(new AccessChangeRow
                    {
                        User = swap.User.FullName,
                        Table = names.TryGetValue(key.Table, out var display) && !string.IsNullOrEmpty(display) ? display : key.Table,
                        Privilege = Privileges.Label(key.Kind),
                        Before = Privileges.Label(from),
                        After = Privileges.Label(to),
                        Change = from == PrivilegeDepth.None ? "Gains" : to == PrivilegeDepth.None ? "Loses" : to > from ? "Wider" : "Narrower"
                    });
                }
            }

            return rows.OrderBy(r => r.User, Names).ThenBy(r => r.IsLoss ? 0 : 1).ThenBy(r => r.Table, Names)
                .ThenBy(r => Array.IndexOf(Kinds, r.Privilege)).ToList();
        }

        /// <summary>A short line per user for the review: "Bob loses 3 and gains 8 privileges".</summary>
        public static string Summarize(IReadOnlyList<AccessChangeRow> rows, int users)
        {
            if (rows.Count == 0)
            {
                return users == 0 ? string.Empty : $"What {(users == 1 ? "this user" : $"these {users} users")} can do on tables stays the same.";
            }

            var losses = rows.Count(r => r.IsLoss);
            return $"{rows.Select(r => r.User).Distinct().Count()} user(s) affected: {rows.Count - losses} gain(s) or wider reach, {losses} loss(es) or narrower reach.";
        }

        private static readonly string[] Kinds = Enum.GetValues(typeof(PrivilegeKind)).Cast<PrivilegeKind>().Select(Privileges.Label).ToArray();

        private static Dictionary<(string Table, PrivilegeKind Kind), PrivilegeDepth> Reach(
            IEnumerable<Guid> roleIds,
            IReadOnlyDictionary<Guid, IReadOnlyList<PrivilegeTarget>> catalog,
            IReadOnlyDictionary<Guid, IReadOnlyList<RoleGrant>> grants)
        {
            var reach = new Dictionary<(string, PrivilegeKind), PrivilegeDepth>();
            foreach (var roleId in roleIds)
            {
                if (!grants.TryGetValue(roleId, out var held))
                {
                    continue;
                }

                foreach (var grant in held)
                {
                    if (!catalog.TryGetValue(grant.PrivilegeId, out var targets))
                    {
                        continue;
                    }

                    foreach (var target in targets)
                    {
                        var key = (target.Table.ToLowerInvariant(), target.Kind);
                        if (!reach.TryGetValue(key, out var existing) || grant.Depth > existing)
                        {
                            reach[key] = grant.Depth;
                        }
                    }
                }
            }

            return reach.Where(r => r.Value > PrivilegeDepth.None).ToDictionary(r => r.Key, r => r.Value);
        }

        private static void Apply(HashSet<Guid> set, Guid id, bool add)
        {
            if (add)
            {
                set.Add(id);
            }
            else
            {
                set.Remove(id);
            }
        }

        private static bool Link(OrganizationRequest request, out bool add, out string relationship, out EntityReference target, out EntityReference related)
        {
            add = false;
            relationship = null;
            target = null;
            related = null;
            switch (request)
            {
                case AssociateRequest associate when associate.RelatedEntities?.Count == 1:
                    add = true;
                    relationship = associate.Relationship?.SchemaName;
                    target = associate.Target;
                    related = associate.RelatedEntities[0];
                    break;
                case DisassociateRequest disassociate when disassociate.RelatedEntities?.Count == 1:
                    relationship = disassociate.Relationship?.SchemaName;
                    target = disassociate.Target;
                    related = disassociate.RelatedEntities[0];
                    break;
                default:
                    return false;
            }

            return relationship == ChangePlanner.UserRoleRelationship || relationship == ChangePlanner.TeamRoleRelationship || relationship == ChangePlanner.TeamMembershipRelationship;
        }
    }
}
