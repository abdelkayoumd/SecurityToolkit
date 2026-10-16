using System;
using System.Collections.Generic;
using System.Linq;
using SecurityToolkit.Models;

namespace SecurityToolkit.Services
{
    /// <summary>One route by which a user gets a privilege: a role they hold, and how they hold it.</summary>
    internal sealed class AccessReasonRow
    {
        public string User { get; set; }
        public string Role { get; set; }
        public string RoleBusinessUnit { get; set; }
        /// <summary>"Direct" when the role is assigned to the user, or "Team: {name}" when a team they belong to holds it.</summary>
        public string Via { get; set; }
        public string Depth { get; set; }
        public int DepthLevel { get; set; }
        /// <summary>A check mark on the route(s) that reach furthest, which is what the user effectively has.</summary>
        public string Strongest { get; set; }
        public bool ViaTeam { get; set; }
    }

    /// <summary>Everything that was found for one user.</summary>
    internal sealed class AccessAnswer
    {
        public UserInfo User { get; set; }
        public IReadOnlyList<AccessReasonRow> Routes { get; set; }
        public bool HasAccess => Routes.Count > 0;
        public PrivilegeDepth Best => HasAccess ? (PrivilegeDepth)Routes.Max(r => r.DepthLevel) : PrivilegeDepth.None;
        public int DirectCount => Routes.Count(r => !r.ViaTeam);
        public int TeamCount => Routes.Count(r => r.ViaTeam);
    }

    /// <summary>
    /// Answers "why does this user have this privilege on this table?": which of the roles they hold, directly or
    /// through a team, grant it, and how far each reaches. Pure logic over the loaded security data.
    /// </summary>
    internal static class AccessExplainer
    {
        private static readonly StringComparer Names = StringComparer.CurrentCultureIgnoreCase;

        /// <param name="grants">For each role record, how far it grants the privilege (see <see cref="SecurityService.GetRoleGrants"/>).</param>
        public static AccessAnswer Explain(SecurityGraph graph, UserInfo user, IReadOnlyDictionary<Guid, PrivilegeDepth> grants)
        {
            var routes = new List<AccessReasonRow>();

            foreach (var role in graph.DirectRolesOf(user.Id))
            {
                AddRoute(routes, user, role, "Direct", viaTeam: false, grants);
            }

            foreach (var team in graph.TeamsOf(user.Id))
            {
                foreach (var role in graph.RolesOfTeam(team.Id))
                {
                    AddRoute(routes, user, role, "Team: " + team.Name, viaTeam: true, grants);
                }
            }

            var ordered = routes.OrderByDescending(r => r.DepthLevel).ThenBy(r => r.Role, Names).ThenBy(r => r.Via, Names).ToList();
            var best = ordered.Count == 0 ? 0 : ordered[0].DepthLevel;
            foreach (var route in ordered.Where(r => r.DepthLevel == best))
            {
                route.Strongest = "✓";
            }

            return new AccessAnswer { User = user, Routes = ordered };
        }

        private static void AddRoute(List<AccessReasonRow> routes, UserInfo user, RoleInfo role, string via, bool viaTeam, IReadOnlyDictionary<Guid, PrivilegeDepth> grants)
        {
            if (!grants.TryGetValue(role.Id, out var depth) || depth == PrivilegeDepth.None)
            {
                return;
            }

            routes.Add(new AccessReasonRow
            {
                User = user.FullName,
                Role = role.Name,
                RoleBusinessUnit = role.BusinessUnitName,
                Via = via,
                Depth = Privileges.Label(depth),
                DepthLevel = (int)depth,
                Strongest = string.Empty,
                ViaTeam = viaTeam
            });
        }

        /// <summary>One sentence for the user's result.</summary>
        public static string Summarize(AccessAnswer answer, PrivilegeKind kind, string table)
        {
            var action = Privileges.Label(kind);
            if (!answer.HasAccess)
            {
                return $"{answer.User.FullName} has no role that grants {action} on {table}.";
            }

            // A role can be a route twice (assigned directly and also held by a team), so these are routes, not roles.
            var routes = answer.Routes.Count;
            return $"{answer.User.FullName} can {action} {table} at {Privileges.Label(answer.Best)} level, " +
                   $"through {routes} route{(routes == 1 ? string.Empty : "s")} ({answer.DirectCount} direct, {answer.TeamCount} through teams).";
        }

        /// <summary>
        /// A caveat worth showing when the strongest route is only through a team. A team's roles always apply to the
        /// records the team owns; whether members also get them on their own records depends on the team's member
        /// privilege inheritance setting, which the toolkit does not read.
        /// </summary>
        public static bool NeedsTeamCaveat(AccessAnswer answer)
        {
            return answer.HasAccess && answer.Routes.Where(r => r.Strongest.Length > 0).All(r => r.ViaTeam);
        }
    }
}
