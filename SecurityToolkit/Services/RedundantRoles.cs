using System;
using System.Collections.Generic;
using System.Linq;
using SecurityToolkit.Models;

namespace SecurityToolkit.Services
{
    /// <summary>
    /// Roles a user holds that add nothing: every privilege the role grants, the user already gets at the same depth or
    /// wider from their other roles. Only roles assigned to the user directly are candidates (removing a team's role
    /// would change the team for everyone), and the count is conservative:
    /// <list type="bullet">
    /// <item>a team's role only covers when the team is in the user's own business unit, and only for grants of
    /// Business Unit depth or wider (a team's user-level grants may reach only the records the team owns);</item>
    /// <item>of two roles that cover each other, only one is reported, so removing every finding never takes access
    /// away.</item>
    /// </list>
    /// </summary>
    internal static class RedundantRoles
    {
        public const string Title = "Roles that add nothing";

        public const string Description = "Roles a user holds directly whose every privilege they already get, at least as far, from their other roles. " +
                                          "Roles through teams only count when the team is in the user's business unit. Disabled users are left out.";

        private static readonly StringComparer Names = StringComparer.CurrentCultureIgnoreCase;

        /// <summary>The role records whose privileges are needed: every role an enabled user holds, directly or through a team.</summary>
        public static IReadOnlyList<Guid> RoleIdsToRead(SecurityGraph graph)
        {
            var ids = new HashSet<Guid>();
            foreach (var user in graph.Users.Where(u => !u.IsDisabled))
            {
                foreach (var role in graph.DirectRolesOf(user.Id))
                {
                    ids.Add(role.Id);
                }

                foreach (var team in graph.TeamsOf(user.Id))
                {
                    foreach (var role in graph.RolesOfTeam(team.Id))
                    {
                        ids.Add(role.Id);
                    }
                }
            }

            return ids.ToList();
        }

        public static CleanupCheck Find(SecurityGraph graph, IReadOnlyDictionary<Guid, IReadOnlyList<RoleGrant>> grants)
        {
            var rows = new List<CleanupRow>();
            foreach (var user in graph.Users.Where(u => !u.IsDisabled).OrderBy(u => u.FullName, Names))
            {
                rows.AddRange(ForUser(graph, user, grants));
            }

            return new CleanupCheck { Title = Title, Description = Description, Rows = rows };
        }

        /// <summary>The roles of one user that add nothing, each with the roles that already give what it grants.</summary>
        internal static IEnumerable<CleanupRow> ForUser(SecurityGraph graph, UserInfo user, IReadOnlyDictionary<Guid, IReadOnlyList<RoleGrant>> grants)
        {
            IReadOnlyList<RoleGrant> Of(RoleInfo role) => grants.TryGetValue(role.Id, out var list) ? list : new RoleGrant[0];

            // Holding two copies of the same role counts once: a role is identified by its root.
            var direct = graph.DirectRolesOf(user.Id).GroupBy(r => r.RootKey).Select(g => g.First()).ToList();
            var throughTeams = graph.TeamsOf(user.Id)
                .Where(t => t.BusinessUnitId.HasValue && t.BusinessUnitId == user.BusinessUnitId)
                .SelectMany(t => graph.RolesOfTeam(t.Id).Select(r => (Role: r, Team: t)))
                .ToList();

            // The smallest roles first, so that of two roles covering each other the smaller one is the one reported.
            var candidates = direct.OrderBy(r => Of(r).Count).ThenBy(r => r.Name, Names).ToList();
            var kept = new List<RoleInfo>(direct);
            foreach (var role in candidates)
            {
                var others = kept.Where(r => r.RootKey != role.RootKey).ToList();
                var cover = new Dictionary<Guid, PrivilegeDepth>();
                void Add(Guid privilege, PrivilegeDepth depth)
                {
                    if (!cover.TryGetValue(privilege, out var existing) || depth > existing)
                    {
                        cover[privilege] = depth;
                    }
                }

                foreach (var other in others)
                {
                    foreach (var grant in Of(other))
                    {
                        Add(grant.PrivilegeId, grant.Depth);
                    }
                }

                foreach (var (teamRole, _) in throughTeams.Where(t => t.Role.RootKey != role.RootKey))
                {
                    foreach (var grant in Of(teamRole).Where(g => g.Depth >= PrivilegeDepth.BusinessUnit))
                    {
                        Add(grant.PrivilegeId, grant.Depth);
                    }
                }

                var own = Of(role).Where(g => g.Depth > PrivilegeDepth.None).ToList();
                if (own.Any(g => !cover.TryGetValue(g.PrivilegeId, out var depth) || depth < g.Depth))
                {
                    continue;
                }

                kept.Remove(role);
                var by = others.Select(r => r.Name)
                    .Concat(throughTeams.Where(t => t.Role.RootKey != role.RootKey && Of(t.Role).Count > 0).Select(t => $"{t.Role.Name} (team {t.Team.Name})"))
                    .Distinct(Names).OrderBy(n => n, Names).ToList();
                yield return new CleanupRow
                {
                    Check = Title,
                    Item = user.FullName,
                    Detail = own.Count == 0
                        ? $"{role.Name}: grants no privilege at all"
                        : $"{role.Name}: all of it already comes from {string.Join(", ", by)}",
                    BusinessUnit = user.BusinessUnitName,
                    UserId = user.Id,
                    RoleId = role.Id
                };
            }
        }
    }
}
