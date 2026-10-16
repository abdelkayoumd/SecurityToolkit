using System;
using System.Collections.Generic;
using System.Linq;
using SecurityToolkit.Models;

namespace SecurityToolkit.Services
{
    /// <summary>
    /// How many tables each root role can delete records of across the whole organization (Organization-level Delete),
    /// out of all the tables that have a Delete privilege. The measure of how powerful a role is.
    /// </summary>
    internal sealed class OrgDeleteReach
    {
        /// <summary>A role is "broad" when it can delete across the organization on at least this share of the tables.</summary>
        public const double BroadShare = 0.5;

        public OrgDeleteReach(IReadOnlyDictionary<Guid, int> tablesByRole, int totalTables)
        {
            TablesByRole = tablesByRole;
            TotalTables = totalTables;
        }

        /// <summary>Root role id to the number of tables it can delete across the organization.</summary>
        public IReadOnlyDictionary<Guid, int> TablesByRole { get; }

        /// <summary>Every table that has a Delete privilege at all.</summary>
        public int TotalTables { get; }

        public int TablesOf(Guid rootKey) => TablesByRole.TryGetValue(rootKey, out var count) ? count : 0;

        public bool IsBroad(Guid rootKey)
        {
            return TotalTables > 0 && TablesOf(rootKey) >= Math.Ceiling(TotalTables * BroadShare);
        }
    }

    /// <summary>One finding: a user or a role, and why it is listed.</summary>
    internal sealed class PrivilegedRow
    {
        public string Check { get; set; }
        public string Subject { get; set; }
        public string Detail { get; set; }
        public string BusinessUnit { get; set; }
        public string Status { get; set; }
    }

    internal sealed class PrivilegedCheck
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public IReadOnlyList<PrivilegedRow> Rows { get; set; }
        public int Count => Rows.Count;
    }

    /// <summary>
    /// The security-review questions: which roles are powerful, who holds them, and who holds a lot of roles.
    /// Pure logic over the loaded security data. It only reports.
    /// </summary>
    internal static class PrivilegedUsers
    {
        public const int ManyRoles = 8;

        public const string BroadRolesTitle = "Roles that can delete across the organization";
        public const string HoldersTitle = "People holding those roles";
        public const string BuiltInTitle = "System Administrators and Customizers (by role name)";
        public const string DisabledTitle = "Disabled users holding those roles";
        public const string ServiceTitle = "Service accounts holding those roles";
        public const string ManyRolesTitle = "Users with many roles";

        private static readonly StringComparer Names = StringComparer.CurrentCultureIgnoreCase;
        private static readonly string[] BuiltInRoles = { "System Administrator", "System Customizer" };

        private sealed class Route
        {
            public RoleInfo Role;
            public string Source; // "direct" or the team's name
            public bool ViaTeam;
        }

        public static IReadOnlyList<PrivilegedCheck> Run(SecurityGraph graph, OrgDeleteReach reach)
        {
            var routesByUser = graph.Users.ToDictionary(u => u.Id, u => RoutesOf(graph, u));
            var holders = graph.Users
                .Select(u => new { User = u, Broad = routesByUser[u.Id].Where(r => reach.IsBroad(r.Role.RootKey)).ToList() })
                .Where(x => x.Broad.Count > 0).ToList();

            return new[]
            {
                BroadRoles(graph, reach),
                Holders(holders.Where(h => !h.User.IsDisabled && !h.User.IsServiceAccount).Select(h => (h.User, h.Broad)), HoldersTitle,
                    "Active people who hold a role that can delete across the organization, directly or through a team. These are the accounts to protect first."),
                BuiltIn(graph, routesByUser),
                Holders(holders.Where(h => h.User.IsDisabled).Select(h => (h.User, h.Broad)), DisabledTitle,
                    "Disabled users who still hold one of those roles. If one is ever re-enabled by mistake it comes back with full power."),
                Holders(holders.Where(h => h.User.IsServiceAccount && !h.User.IsDisabled).Select(h => (h.User, h.Broad)), ServiceTitle,
                    "Service and application accounts with one of those roles. They are often over-privileged because nobody reviews them."),
                ManyRolesCheck(graph, routesByUser)
            };
        }

        private static List<Route> RoutesOf(SecurityGraph graph, UserInfo user)
        {
            var routes = graph.DirectRolesOf(user.Id).Select(r => new Route { Role = r, Source = "direct" }).ToList();
            foreach (var team in graph.TeamsOf(user.Id))
            {
                routes.AddRange(graph.RolesOfTeam(team.Id).Select(r => new Route { Role = r, Source = team.Name, ViaTeam = true }));
            }

            return routes;
        }

        private static PrivilegedCheck BroadRoles(SecurityGraph graph, OrgDeleteReach reach)
        {
            var rows = graph.Roles.GroupBy(r => r.RootKey)
                .Where(g => reach.IsBroad(g.Key))
                .Select(g => new PrivilegedRow
                {
                    Check = BroadRolesTitle,
                    Subject = g.First().Name,
                    Detail = $"Organization-level Delete on {reach.TablesOf(g.Key)} of {reach.TotalTables} tables",
                    BusinessUnit = string.Empty,
                    Status = string.Empty
                })
                .OrderBy(r => r.Subject, Names).ToList();

            return new PrivilegedCheck
            {
                Title = BroadRolesTitle,
                Description = $"Roles that can delete records, across the whole organization, on at least {(int)(OrgDeleteReach.BroadShare * 100)}% of the {reach.TotalTables} tables that can be deleted from. " +
                              "This finds System Administrator whatever language it is in, and custom roles that are just as powerful.",
                Rows = rows
            };
        }

        private static PrivilegedCheck Holders(IEnumerable<(UserInfo User, List<Route> Broad)> people, string title, string description)
        {
            var rows = people
                .Select(p => new PrivilegedRow
                {
                    Check = title,
                    Subject = p.User.FullName,
                    Detail = Describe(p.Broad),
                    BusinessUnit = p.User.BusinessUnitName,
                    Status = StatusOf(p.User)
                })
                .OrderBy(r => r.Subject, Names).ToList();

            return new PrivilegedCheck { Title = title, Description = description, Rows = rows };
        }

        private static PrivilegedCheck BuiltIn(SecurityGraph graph, Dictionary<Guid, List<Route>> routesByUser)
        {
            var rows = graph.Users
                .Select(u => new { User = u, Hits = routesByUser[u.Id].Where(r => BuiltInRoles.Contains(r.Role.Name, Names)).ToList() })
                .Where(x => x.Hits.Count > 0)
                .Select(x => new PrivilegedRow
                {
                    Check = BuiltInTitle,
                    Subject = x.User.FullName,
                    Detail = Describe(x.Hits),
                    BusinessUnit = x.User.BusinessUnitName,
                    Status = StatusOf(x.User)
                })
                .OrderBy(r => r.Subject, Names).ToList();

            return new PrivilegedCheck
            {
                Title = BuiltInTitle,
                Description = "Everyone holding the built-in System Administrator or System Customizer role, matched by its English name. " +
                              "If your organization uses another language, rely on the checks above, which measure what a role can do.",
                Rows = rows
            };
        }

        private static PrivilegedCheck ManyRolesCheck(SecurityGraph graph, Dictionary<Guid, List<Route>> routesByUser)
        {
            var rows = graph.Users
                .Select(u => new { User = u, Roles = routesByUser[u.Id].Select(r => r.Role.RootKey).Distinct().Count() })
                .Where(x => x.Roles >= ManyRoles)
                .OrderByDescending(x => x.Roles).ThenBy(x => x.User.FullName, Names)
                .Select(x => new PrivilegedRow
                {
                    Check = ManyRolesTitle,
                    Subject = x.User.FullName,
                    Detail = $"{x.Roles} different roles",
                    BusinessUnit = x.User.BusinessUnitName,
                    Status = StatusOf(x.User)
                }).ToList();

            return new PrivilegedCheck
            {
                Title = ManyRolesTitle,
                Description = $"Users who hold {ManyRoles} or more different roles, directly or through teams. Roles pile up as people change jobs; each extra one widens what they can do.",
                Rows = rows
            };
        }

        /// <summary>"System Administrator (direct); Sales Admin (via Team A, Team B)".</summary>
        private static string Describe(IEnumerable<Route> routes)
        {
            return string.Join("; ", routes.GroupBy(r => r.Role.RootKey).OrderBy(g => g.First().Role.Name, Names).Select(g =>
            {
                var parts = new List<string>();
                if (g.Any(r => !r.ViaTeam))
                {
                    parts.Add("direct");
                }

                var teams = g.Where(r => r.ViaTeam).Select(r => r.Source).Distinct().OrderBy(t => t, Names).ToList();
                if (teams.Count > 0)
                {
                    parts.Add("via " + string.Join(", ", teams));
                }

                return $"{g.First().Role.Name} ({string.Join(" + ", parts)})";
            }));
        }

        private static string StatusOf(UserInfo user)
        {
            return user.IsServiceAccount ? user.Status + " (service account)" : user.Status;
        }
    }
}
