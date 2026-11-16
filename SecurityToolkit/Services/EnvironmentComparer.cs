using System;
using System.Collections.Generic;
using System.Linq;
using SecurityToolkit.Models;

namespace SecurityToolkit.Services
{
    /// <summary>What one environment holds for the comparison: its roles, teams, what each role grants, and privilege names.</summary>
    internal sealed class EnvironmentSecurity
    {
        public string Name { get; set; }
        public SecurityGraph Graph { get; set; }
        public IReadOnlyDictionary<Guid, IReadOnlyList<PrivilegeTarget>> Catalog { get; set; }
        public IReadOnlyList<OtherPrivilege> OtherPrivileges { get; set; }

        /// <summary>What each root role grants, by role id.</summary>
        public IReadOnlyDictionary<Guid, IReadOnlyList<RoleGrant>> Grants { get; set; }

        /// <summary>The roles as people see them: one per root role (business-unit copies mirror it).</summary>
        public IReadOnlyList<RoleInfo> RootRoles => Graph.Roles.Where(r => !r.RootRoleId.HasValue || r.RootRoleId == r.Id).ToList();

        /// <summary>The role ids whose privileges the comparison needs.</summary>
        public static IReadOnlyList<Guid> RolesToRead(SecurityGraph graph) => graph.Roles.Where(r => !r.RootRoleId.HasValue || r.RootRoleId == r.Id).Select(r => r.Id).ToList();
    }

    /// <summary>One difference between two environments.</summary>
    internal sealed class EnvironmentDiffRow
    {
        public string Check { get; set; }
        public string Item { get; set; }
        public string Detail { get; set; }
        public string Here { get; set; }
        public string There { get; set; }
    }

    /// <summary>
    /// Dev against Prod: which security roles exist on one side only, how the privileges of the roles both have
    /// differ, and how the roles of the teams both have differ. Roles, teams, tables and privileges are matched by
    /// name, not by id: ids can differ from one environment to the next.
    /// </summary>
    internal static class EnvironmentComparer
    {
        public const string Privileges = "Privileges that differ";
        public const string TeamRoles = "Team roles that differ";

        private static readonly StringComparer Names = StringComparer.CurrentCultureIgnoreCase;

        public static string OnlyIn(string environment) => $"Roles only in {environment}";

        public static IReadOnlyList<EnvironmentCheck> Compare(EnvironmentSecurity here, EnvironmentSecurity there)
        {
            var hereRoles = UniqueByName(here.RootRoles);
            var thereRoles = UniqueByName(there.RootRoles);

            var onlyHere = hereRoles.Keys.Where(n => !thereRoles.ContainsKey(n)).OrderBy(n => n, Names)
                .Select(n => new EnvironmentDiffRow { Check = OnlyIn(here.Name), Item = n, Detail = "Security role", Here = "Yes", There = "No" }).ToList();
            var onlyThere = thereRoles.Keys.Where(n => !hereRoles.ContainsKey(n)).OrderBy(n => n, Names)
                .Select(n => new EnvironmentDiffRow { Check = OnlyIn(there.Name), Item = n, Detail = "Security role", Here = "No", There = "Yes" }).ToList();

            var privileges = new List<EnvironmentDiffRow>();
            foreach (var name in hereRoles.Keys.Where(thereRoles.ContainsKey).OrderBy(n => n, Names))
            {
                var a = Reach(here, hereRoles[name]);
                var b = Reach(there, thereRoles[name]);
                foreach (var key in a.Keys.Union(b.Keys, Names).OrderBy(k => k, Names))
                {
                    var depthA = a.TryGetValue(key, out var x) ? x : PrivilegeDepth.None;
                    var depthB = b.TryGetValue(key, out var y) ? y : PrivilegeDepth.None;
                    if (depthA != depthB)
                    {
                        privileges.Add(new EnvironmentDiffRow
                        {
                            Check = Privileges, Item = name, Detail = key,
                            Here = Services.Privileges.Label(depthA), There = Services.Privileges.Label(depthB)
                        });
                    }
                }
            }

            var teamRoles = new List<EnvironmentDiffRow>();
            var hereTeams = UniqueTeams(here.Graph);
            var thereTeams = UniqueTeams(there.Graph);
            foreach (var name in hereTeams.Keys.Where(thereTeams.ContainsKey).OrderBy(n => n, Names))
            {
                var a = new HashSet<string>(here.Graph.RolesOfTeam(hereTeams[name].Id).Select(r => r.Name), Names);
                var b = new HashSet<string>(there.Graph.RolesOfTeam(thereTeams[name].Id).Select(r => r.Name), Names);
                foreach (var role in a.Union(b, Names).Where(r => a.Contains(r) != b.Contains(r)).OrderBy(r => r, Names))
                {
                    teamRoles.Add(new EnvironmentDiffRow { Check = TeamRoles, Item = name, Detail = role, Here = a.Contains(role) ? "Yes" : "No", There = b.Contains(role) ? "Yes" : "No" });
                }
            }

            return new[]
            {
                new EnvironmentCheck(OnlyIn(here.Name), $"Security roles {here.Name} has and {there.Name} does not (matched by name).", onlyHere),
                new EnvironmentCheck(OnlyIn(there.Name), $"Security roles {there.Name} has and {here.Name} does not (matched by name).", onlyThere),
                new EnvironmentCheck(Privileges, "For the roles both have, every table privilege and other privilege that reaches differently (or exists on one side only).", privileges),
                new EnvironmentCheck(TeamRoles, "For the teams both have (matched by name), the roles one side gives the team and the other does not.", teamRoles)
            };
        }

        /// <summary>How far a role reaches per privilege, named so both sides match: "Delete on account", "Export to Excel".</summary>
        private static Dictionary<string, PrivilegeDepth> Reach(EnvironmentSecurity side, RoleInfo role)
        {
            var reach = new Dictionary<string, PrivilegeDepth>(Names);
            var others = side.OtherPrivileges?.ToDictionary(p => p.Id, p => p.Label) ?? new Dictionary<Guid, string>();
            if (!side.Grants.TryGetValue(role.Id, out var grants))
            {
                return reach;
            }

            void Keep(string key, PrivilegeDepth depth)
            {
                if (depth > PrivilegeDepth.None && (!reach.TryGetValue(key, out var existing) || depth > existing))
                {
                    reach[key] = depth;
                }
            }

            foreach (var grant in grants)
            {
                if (side.Catalog.TryGetValue(grant.PrivilegeId, out var targets))
                {
                    foreach (var target in targets)
                    {
                        Keep($"{Services.Privileges.Label(target.Kind)} on {target.Table.ToLowerInvariant()}", grant.Depth);
                    }
                }
                else if (others.TryGetValue(grant.PrivilegeId, out var label))
                {
                    Keep(label, grant.Depth);
                }
            }

            return reach;
        }

        private static Dictionary<string, RoleInfo> UniqueByName(IEnumerable<RoleInfo> roles)
        {
            // Two root roles with one name cannot be told apart by name: the first one stands for both.
            return roles.Where(r => !string.IsNullOrWhiteSpace(r.Name)).GroupBy(r => r.Name, Names).ToDictionary(g => g.Key, g => g.First(), Names);
        }

        private static Dictionary<string, TeamInfo> UniqueTeams(SecurityGraph graph)
        {
            // Default (business unit) teams are named after their business unit; they are compared like any other team.
            return graph.Teams.Where(t => !string.IsNullOrWhiteSpace(t.Name)).GroupBy(t => t.Name, Names).Where(g => g.Count() == 1)
                .ToDictionary(g => g.Key, g => g.First(), Names);
        }
    }

    /// <summary>A named group of differences, shown like a check of the Cleanup report.</summary>
    internal sealed class EnvironmentCheck
    {
        public EnvironmentCheck(string title, string description, IReadOnlyList<EnvironmentDiffRow> rows)
        {
            Title = title;
            Description = description;
            Rows = rows;
        }

        public string Title { get; }
        public string Description { get; }
        public IReadOnlyList<EnvironmentDiffRow> Rows { get; }
        public int Count => Rows.Count;
    }
}
