using System;
using System.Collections.Generic;
using System.Linq;
using SecurityToolkit.Models;

namespace SecurityToolkit.Services
{
    /// <summary>One finding of a cleanup check.</summary>
    internal sealed class CleanupRow
    {
        public string Check { get; set; }
        public string Item { get; set; }
        public string Detail { get; set; }
        public string BusinessUnit { get; set; }

        /// <summary>The user or team the finding is about; not shown, used when acting on it.</summary>
        public Guid? UserId { get; set; }
        public Guid? TeamId { get; set; }
    }

    /// <summary>One cleanup check and what it found.</summary>
    internal sealed class CleanupCheck
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public IReadOnlyList<CleanupRow> Rows { get; set; }
        public int Count => Rows.Count;
    }

    /// <summary>
    /// Tidy-up checks over the security setup: things that look forgotten or pointless. They only report; nothing
    /// is changed. Each one is conservative about what it counts, and says so in its description.
    /// </summary>
    internal static class CleanupChecks
    {
        public const string NoRoles = "Active users with no security role";
        public const string DisabledInTeams = "Disabled users still in teams";
        public const string DisabledWithRoles = "Disabled users that still have roles";
        public const string UnusedRoles = "Security roles nobody holds";
        public const string EmptyTeams = "Empty teams";
        public const string TeamsWithoutRoles = "Teams without any role";

        private static readonly StringComparer Names = StringComparer.CurrentCultureIgnoreCase;

        public static IReadOnlyList<CleanupCheck> Run(SecurityGraph graph)
        {
            return new[]
            {
                UsersWithNoRoles(graph),
                DisabledUsersInTeams(graph),
                DisabledUsersWithRoles(graph),
                RolesNobodyHolds(graph),
                TeamsWithNoMembers(graph),
                TeamsWithNoRoles(graph)
            };
        }

        private static bool HasAnyRole(SecurityGraph graph, UserInfo user)
        {
            return graph.DirectRolesOf(user.Id).Count > 0 || graph.TeamsOf(user.Id).Any(t => graph.RolesOfTeam(t.Id).Count > 0);
        }

        private static CleanupCheck UsersWithNoRoles(SecurityGraph graph)
        {
            var rows = graph.Users
                .Where(u => !u.IsDisabled && !u.IsServiceAccount && !HasAnyRole(graph, u))
                .Select(u =>
                {
                    var teams = graph.TeamsOf(u.Id).Count(t => !t.IsDefault);
                    return new CleanupRow { Check = NoRoles, Item = u.FullName, Detail = $"Member of {teams} team(s)", BusinessUnit = u.BusinessUnitName };
                })
                .OrderBy(r => r.Item, Names).ToList();

            return new CleanupCheck
            {
                Title = NoRoles,
                Description = "Active users who have no security role, neither assigned to them nor through any team. They can sign in but see nothing. Service and application accounts are skipped.",
                Rows = rows
            };
        }

        private static CleanupCheck DisabledUsersInTeams(SecurityGraph graph)
        {
            var rows = graph.Users.Where(u => u.IsDisabled)
                .SelectMany(u => graph.TeamsOf(u.Id).Where(t => !t.IsDefault)
                    .Select(t => new CleanupRow { Check = DisabledInTeams, Item = u.FullName, Detail = $"{t.Name} ({t.TypeName.ToLowerInvariant()} team)", BusinessUnit = u.BusinessUnitName, UserId = u.Id, TeamId = t.Id }))
                .OrderBy(r => r.Item, Names).ThenBy(r => r.Detail, Names).ToList();

            return new CleanupCheck
            {
                Title = DisabledInTeams,
                Description = "Disabled users who are still members of a team other than their business unit's default team. They no longer count toward anything, and can be taken out.",
                Rows = rows
            };
        }

        private static CleanupCheck DisabledUsersWithRoles(SecurityGraph graph)
        {
            var rows = graph.Users.Where(u => u.IsDisabled)
                .Select(u => new { User = u, Roles = graph.DirectRolesOf(u.Id) })
                .Where(x => x.Roles.Count > 0)
                .Select(x => new CleanupRow
                {
                    Check = DisabledWithRoles,
                    Item = x.User.FullName,
                    Detail = string.Join("; ", x.Roles.Select(r => r.Name)),
                    BusinessUnit = x.User.BusinessUnitName,
                    UserId = x.User.Id
                })
                .OrderBy(r => r.Item, Names).ToList();

            return new CleanupCheck
            {
                Title = DisabledWithRoles,
                Description = "Disabled users who still have security roles assigned to them directly. Removing the roles tidies the user up if they are ever re-enabled by mistake.",
                Rows = rows
            };
        }

        private static CleanupCheck RolesNobodyHolds(SecurityGraph graph)
        {
            // A role exists as one copy per business unit; it is unused only if no copy is assigned to anyone.
            var rows = graph.Roles.GroupBy(r => r.RootKey)
                .Where(g => !g.Any(copy => graph.RoleIsAssigned(copy.Id)))
                .Select(g => new CleanupRow
                {
                    Check = UnusedRoles,
                    Item = g.First().Name,
                    Detail = g.Any(r => r.IsManaged) ? "Managed (comes from a solution)" : "Custom",
                    BusinessUnit = string.Empty
                })
                .OrderBy(r => r.Item, Names).ToList();

            return new CleanupCheck
            {
                Title = UnusedRoles,
                Description = "Security roles that are not assigned to any user or team in any business unit. Custom ones can usually be deleted; managed ones belong to a solution and cannot.",
                Rows = rows
            };
        }

        /// <summary>Teams worth checking: not the default team of a business unit, and not access teams (empty and role-less is normal for those).</summary>
        private static IEnumerable<TeamInfo> OrdinaryTeams(SecurityGraph graph)
        {
            return graph.Teams.Where(t => !t.IsDefault && t.TeamType != 1);
        }

        private static CleanupCheck TeamsWithNoMembers(SecurityGraph graph)
        {
            var rows = OrdinaryTeams(graph).Where(t => graph.MembersOf(t.Id).Count == 0)
                .Select(t => new CleanupRow { Check = EmptyTeams, Item = t.Name, Detail = t.TypeName + " team", BusinessUnit = t.BusinessUnitName })
                .OrderBy(r => r.Item, Names).ToList();

            return new CleanupCheck
            {
                Title = EmptyTeams,
                Description = "Owner and group teams with no members. Access teams and the default team of each business unit are not counted.",
                Rows = rows
            };
        }

        private static CleanupCheck TeamsWithNoRoles(SecurityGraph graph)
        {
            var rows = OrdinaryTeams(graph).Where(t => graph.RolesOfTeam(t.Id).Count == 0)
                .Select(t => new CleanupRow { Check = TeamsWithoutRoles, Item = t.Name, Detail = $"{graph.MembersOf(t.Id).Count} member(s)", BusinessUnit = t.BusinessUnitName })
                .OrderBy(r => r.Item, Names).ToList();

            return new CleanupCheck
            {
                Title = TeamsWithoutRoles,
                Description = "Owner and group teams that carry no security role, so being a member gives nothing (apart from owning records). Access teams and default teams are not counted.",
                Rows = rows
            };
        }
    }
}
