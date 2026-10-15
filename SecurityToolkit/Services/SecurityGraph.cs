using System;
using System.Collections.Generic;
using System.Linq;
using SecurityToolkit.Models;

namespace SecurityToolkit.Services
{
    internal sealed class RoleUserRow
    {
        public string Role { get; set; }
        public string User { get; set; }
        public string Domain { get; set; }
        public string BusinessUnit { get; set; }
        /// <summary>"Direct" or "Via team".</summary>
        public string Assignment { get; set; }
        public string Team { get; set; }
        public string Status { get; set; }
    }

    /// <summary>One line of a side-by-side comparison of two users.</summary>
    internal sealed class CompareRow
    {
        /// <summary>"Business unit", "Security role" or "Team".</summary>
        public string Category { get; set; }
        public string Item { get; set; }
        /// <summary>What the first user has for this item: a check mark, where a role comes from, or a business unit name; empty when they lack it.</summary>
        public string A { get; set; }
        public string B { get; set; }
        /// <summary>"Both", "Only {first user}", "Only {second user}", or for business units "Same" / "Differs".</summary>
        public string Result { get; set; }
        /// <summary>True when the two users differ on this item.</summary>
        public bool IsDifference { get; set; }
    }

    internal sealed class BusinessUnitRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Parent { get; set; }
        /// <summary>The chain from the root, for example "Root / Sales / Emea".</summary>
        public string Path { get; set; }
    }

    internal sealed class TeamMemberRow
    {
        public string Team { get; set; }
        public string TeamType { get; set; }
        public string User { get; set; }
        public string Domain { get; set; }
        public string BusinessUnit { get; set; }
        public string Status { get; set; }
    }

    internal sealed class UserSummaryRow
    {
        public string User { get; set; }
        public string BusinessUnit { get; set; }
        public string Teams { get; set; }
        public string DirectRoles { get; set; }
        /// <summary>Roles inherited from teams, as "Role (Team)".</summary>
        public string TeamRoles { get; set; }
        public string Status { get; set; }
    }

    internal sealed class UserAccessRow
    {
        public string User { get; set; }
        public string BusinessUnit { get; set; }
        public string Team { get; set; }
        public string Role { get; set; }
        /// <summary>"Direct role", "Inherited from team" or "Team member (no roles)".</summary>
        public string Source { get; set; }
    }

    /// <summary>
    /// All the security records of an organization, linked together in memory. Every analysis the features show
    /// (who holds a role, who is in a team, what a business unit's users inherit) is a query on this graph.
    /// </summary>
    internal sealed class SecurityGraph
    {
        private static readonly StringComparer Names = StringComparer.CurrentCultureIgnoreCase;

        private readonly Dictionary<Guid, UserInfo> _users;
        private readonly Dictionary<Guid, RoleInfo> _roles;
        private readonly Dictionary<Guid, TeamInfo> _teams;
        private readonly ILookup<Guid, Guid> _teamIdsByUser;
        private readonly ILookup<Guid, Guid> _userIdsByTeam;
        private readonly ILookup<Guid, Guid> _roleIdsByUser;
        private readonly ILookup<Guid, Guid> _roleIdsByTeam;
        private readonly HashSet<Guid> _assignedRoleIds;

        public SecurityGraph(
            IEnumerable<UserInfo> users, IEnumerable<RoleInfo> roles, IEnumerable<TeamInfo> teams,
            IEnumerable<BusinessUnitInfo> businessUnits, IEnumerable<UserTeamLink> memberships,
            IEnumerable<UserRoleLink> userRoles, IEnumerable<TeamRoleLink> teamRoles)
        {
            Users = users.OrderBy(u => u.FullName, Names).ToList();
            Roles = roles.OrderBy(r => r.Name, Names).ToList();
            Teams = teams.OrderBy(t => t.Name, Names).ToList();
            BusinessUnits = businessUnits.OrderBy(b => b.Name, Names).ToList();

            _users = Users.ToDictionary(u => u.Id);
            _roles = Roles.ToDictionary(r => r.Id);
            _teams = Teams.ToDictionary(t => t.Id);

            var memberList = memberships.ToList();
            _teamIdsByUser = memberList.ToLookup(m => m.UserId, m => m.TeamId);
            _userIdsByTeam = memberList.ToLookup(m => m.TeamId, m => m.UserId);
            _roleIdsByUser = userRoles.ToLookup(r => r.UserId, r => r.RoleId);
            _roleIdsByTeam = teamRoles.ToLookup(r => r.TeamId, r => r.RoleId);
            _assignedRoleIds = new HashSet<Guid>(_roleIdsByUser.SelectMany(g => g).Concat(_roleIdsByTeam.SelectMany(g => g)));
        }

        public IReadOnlyList<UserInfo> Users { get; }
        public IReadOnlyList<RoleInfo> Roles { get; }
        public IReadOnlyList<TeamInfo> Teams { get; }
        public IReadOnlyList<BusinessUnitInfo> BusinessUnits { get; }

        /// <summary>True when this copy of a role is assigned to at least one user or team.</summary>
        public bool RoleIsAssigned(Guid roleId)
        {
            return _assignedRoleIds.Contains(roleId);
        }

        public IReadOnlyList<TeamInfo> TeamsOf(Guid userId)
        {
            return _teamIdsByUser[userId].Select(id => _teams.TryGetValue(id, out var t) ? t : null).Where(t => t != null)
                .Distinct().OrderBy(t => t.Name, Names).ToList();
        }

        public IReadOnlyList<RoleInfo> DirectRolesOf(Guid userId)
        {
            return Resolve(_roleIdsByUser[userId]);
        }

        public IReadOnlyList<RoleInfo> RolesOfTeam(Guid teamId)
        {
            return Resolve(_roleIdsByTeam[teamId]);
        }

        public IReadOnlyList<UserInfo> MembersOf(Guid teamId)
        {
            return _userIdsByTeam[teamId].Select(id => _users.TryGetValue(id, out var u) ? u : null).Where(u => u != null)
                .Distinct().OrderBy(u => u.FullName, Names).ToList();
        }

        /// <summary>Roles as a user would pick them: one entry per role, however many business-unit copies exist.</summary>
        public IReadOnlyList<RoleChoice> RoleChoices()
        {
            return Roles.GroupBy(r => r.RootKey)
                .Select(g => new RoleChoice { RootKey = g.Key, Name = g.First().Name, Copies = g.Count() })
                .OrderBy(c => c.Name, Names).ToList();
        }

        /// <summary>Everyone who holds the role, directly or through a team.</summary>
        public IReadOnlyList<RoleUserRow> UsersInRole(Guid roleRootKey, bool includeDisabled)
        {
            var roleIds = new HashSet<Guid>(Roles.Where(r => r.RootKey == roleRootKey).Select(r => r.Id));
            var roleName = Roles.FirstOrDefault(r => r.RootKey == roleRootKey)?.Name ?? string.Empty;
            var rows = new List<RoleUserRow>();

            foreach (var user in Users.Where(u => includeDisabled || !u.IsDisabled))
            {
                if (_roleIdsByUser[user.Id].Any(roleIds.Contains))
                {
                    rows.Add(ToRoleUserRow(roleName, user, "Direct", null));
                }
            }

            foreach (var team in Teams)
            {
                if (!_roleIdsByTeam[team.Id].Any(roleIds.Contains))
                {
                    continue;
                }

                foreach (var member in MembersOf(team.Id).Where(u => includeDisabled || !u.IsDisabled))
                {
                    rows.Add(ToRoleUserRow(roleName, member, "Via team", team.Name));
                }
            }

            return rows.OrderBy(r => r.User, Names).ThenBy(r => r.Assignment).ThenBy(r => r.Team, Names).ToList();
        }

        public IReadOnlyList<TeamMemberRow> TeamMembers(IEnumerable<Guid> teamIds, bool includeDisabled)
        {
            var rows = new List<TeamMemberRow>();
            foreach (var teamId in teamIds)
            {
                if (!_teams.TryGetValue(teamId, out var team))
                {
                    continue;
                }

                foreach (var user in MembersOf(teamId).Where(u => includeDisabled || !u.IsDisabled))
                {
                    rows.Add(new TeamMemberRow
                    {
                        Team = team.Name,
                        TeamType = team.TypeName,
                        User = user.FullName,
                        Domain = user.DomainName,
                        BusinessUnit = user.BusinessUnitName,
                        Status = user.Status
                    });
                }
            }

            return rows.OrderBy(r => r.Team, Names).ThenBy(r => r.User, Names).ToList();
        }

        /// <summary>
        /// Every business unit as a list row, ordered by path ("Root / Sales / Emea") so that sorting by path keeps
        /// parents directly above their children.
        /// </summary>
        public IReadOnlyList<BusinessUnitRow> BusinessUnitRows()
        {
            var byId = BusinessUnits.ToDictionary(b => b.Id);
            return BusinessUnits.Select(unit =>
                {
                    var names = new List<string> { unit.Name };
                    var seen = new HashSet<Guid> { unit.Id };
                    var parent = unit.ParentId;
                    while (parent.HasValue && seen.Add(parent.Value) && byId.TryGetValue(parent.Value, out var up))
                    {
                        names.Insert(0, up.Name);
                        parent = up.ParentId;
                    }

                    return new BusinessUnitRow { Id = unit.Id, Name = unit.Name, Parent = unit.ParentName ?? string.Empty, Path = string.Join(" / ", names) };
                })
                .OrderBy(r => r.Path, Names).ToList();
        }

        /// <summary>The business unit's id, plus every descendant's when <paramref name="includeChildren"/> is set.</summary>
        public ISet<Guid> BusinessUnitScope(Guid businessUnitId, bool includeChildren)
        {
            var scope = new HashSet<Guid> { businessUnitId };
            if (!includeChildren)
            {
                return scope;
            }

            var queue = new Queue<Guid>();
            queue.Enqueue(businessUnitId);
            while (queue.Count > 0)
            {
                var parent = queue.Dequeue();
                foreach (var child in BusinessUnits.Where(b => b.ParentId == parent && scope.Add(b.Id)))
                {
                    queue.Enqueue(child.Id);
                }
            }

            return scope;
        }

        public IReadOnlyList<UserInfo> UsersInBusinessUnits(ISet<Guid> businessUnitIds, bool includeDisabled)
        {
            return Users.Where(u => u.BusinessUnitId.HasValue && businessUnitIds.Contains(u.BusinessUnitId.Value) && (includeDisabled || !u.IsDisabled)).ToList();
        }

        /// <summary>
        /// For each user: a one-line summary (teams, direct roles, roles inherited from teams) and the detailed
        /// rows behind it, one per role or team, saying where each role comes from.
        /// </summary>
        public void BuildUserAccess(IEnumerable<UserInfo> users, out List<UserSummaryRow> summary, out List<UserAccessRow> detail)
        {
            summary = new List<UserSummaryRow>();
            detail = new List<UserAccessRow>();

            foreach (var user in users.OrderBy(u => u.FullName, Names))
            {
                var teams = TeamsOf(user.Id);
                var direct = DirectRolesOf(user.Id);
                var inherited = new List<string>();
                var rows = new List<UserAccessRow>();

                foreach (var role in direct)
                {
                    rows.Add(new UserAccessRow { Team = string.Empty, Role = role.Name, Source = "Direct role" });
                }

                foreach (var team in teams)
                {
                    var teamRoles = RolesOfTeam(team.Id);
                    if (teamRoles.Count == 0)
                    {
                        rows.Add(new UserAccessRow { Team = team.Name, Role = string.Empty, Source = "Team member (no roles)" });
                        continue;
                    }

                    foreach (var role in teamRoles)
                    {
                        inherited.Add($"{role.Name} ({team.Name})");
                        rows.Add(new UserAccessRow { Team = team.Name, Role = role.Name, Source = "Inherited from team" });
                    }
                }

                if (rows.Count == 0)
                {
                    rows.Add(new UserAccessRow { Team = string.Empty, Role = string.Empty, Source = "No team and no role" });
                }

                foreach (var row in rows)
                {
                    row.User = user.FullName;
                    row.BusinessUnit = user.BusinessUnitName;
                }

                detail.AddRange(rows);
                summary.Add(new UserSummaryRow
                {
                    User = user.FullName,
                    BusinessUnit = user.BusinessUnitName,
                    Teams = string.Join("; ", teams.Select(t => t.Name)),
                    DirectRoles = string.Join("; ", direct.Select(r => r.Name)),
                    TeamRoles = string.Join("; ", inherited),
                    Status = user.Status
                });
            }
        }

        /// <summary>
        /// Compares two users on business unit, security roles and teams. Roles are compared as each user effectively
        /// has them: directly or through a team, with copies in different business units counting as the same role.
        /// </summary>
        public IReadOnlyList<CompareRow> CompareUsers(UserInfo first, UserInfo second)
        {
            var (nameA, nameB) = Labels(first, second);
            var rows = new List<CompareRow>
            {
                new CompareRow
                {
                    Category = "Business unit",
                    Item = "Business unit",
                    A = first.BusinessUnitName ?? string.Empty,
                    B = second.BusinessUnitName ?? string.Empty,
                    Result = first.BusinessUnitId == second.BusinessUnitId ? "Same" : "Differs",
                    IsDifference = first.BusinessUnitId != second.BusinessUnitId
                }
            };

            var rolesA = EffectiveRoles(first);
            var rolesB = EffectiveRoles(second);
            foreach (var key in rolesA.Keys.Union(rolesB.Keys).OrderBy(k => (rolesA.TryGetValue(k, out var x) ? x : rolesB[k]).Name, Names))
            {
                var inA = rolesA.TryGetValue(key, out var a);
                var inB = rolesB.TryGetValue(key, out var b);
                rows.Add(Row("Security role", (inA ? a : b).Name, inA ? a.Source : string.Empty, inB ? b.Source : string.Empty, inA, inB, nameA, nameB));
            }

            var teamsA = TeamsOf(first.Id).ToDictionary(t => t.Id);
            var teamsB = TeamsOf(second.Id).ToDictionary(t => t.Id);
            foreach (var id in teamsA.Keys.Union(teamsB.Keys).OrderBy(i => (teamsA.TryGetValue(i, out var x) ? x : teamsB[i]).Name, Names))
            {
                var inA = teamsA.ContainsKey(id);
                var inB = teamsB.ContainsKey(id);
                rows.Add(Row("Team", (inA ? teamsA[id] : teamsB[id]).Name, inA ? "✓" : string.Empty, inB ? "✓" : string.Empty, inA, inB, nameA, nameB));
            }

            return rows;
        }

        private static CompareRow Row(string category, string item, string a, string b, bool inA, bool inB, string nameA, string nameB)
        {
            return new CompareRow
            {
                Category = category,
                Item = item,
                A = a,
                B = b,
                Result = inA && inB ? "Both" : inA ? "Only " + nameA : "Only " + nameB,
                IsDifference = !(inA && inB)
            };
        }

        /// <summary>The names to show for the two sides; two users with the same name are told apart by their user names.</summary>
        internal static (string, string) Labels(UserInfo first, UserInfo second)
        {
            if (!string.Equals(first.FullName, second.FullName, StringComparison.CurrentCultureIgnoreCase))
            {
                return (first.FullName, second.FullName);
            }

            return ($"{first.FullName} ({first.DomainName})", $"{second.FullName} ({second.DomainName})");
        }

        private sealed class EffectiveRole
        {
            public string Name;
            public string Source;
        }

        /// <summary>Each role the user has (by root role), and where it comes from: "Direct", "via Team" or both.</summary>
        private Dictionary<Guid, EffectiveRole> EffectiveRoles(UserInfo user)
        {
            var direct = new HashSet<Guid>(DirectRolesOf(user.Id).Select(r => r.RootKey));
            var viaTeams = new Dictionary<Guid, List<string>>();
            var names = new Dictionary<Guid, string>();

            foreach (var role in DirectRolesOf(user.Id))
            {
                names[role.RootKey] = role.Name;
            }

            foreach (var team in TeamsOf(user.Id))
            {
                foreach (var role in RolesOfTeam(team.Id))
                {
                    names[role.RootKey] = role.Name;
                    if (!viaTeams.TryGetValue(role.RootKey, out var list))
                    {
                        viaTeams[role.RootKey] = list = new List<string>();
                    }

                    list.Add(team.Name);
                }
            }

            return names.ToDictionary(n => n.Key, n =>
            {
                var parts = new List<string>();
                if (direct.Contains(n.Key))
                {
                    parts.Add("Direct");
                }

                if (viaTeams.TryGetValue(n.Key, out var teamNames))
                {
                    parts.Add("via " + string.Join(", ", teamNames.Distinct().OrderBy(t => t, Names)));
                }

                return new EffectiveRole { Name = n.Value, Source = string.Join(" + ", parts) };
            });
        }

        private IReadOnlyList<RoleInfo> Resolve(IEnumerable<Guid> roleIds)
        {
            return roleIds.Select(id => _roles.TryGetValue(id, out var r) ? r : null).Where(r => r != null)
                .Distinct().OrderBy(r => r.Name, Names).ToList();
        }

        private static RoleUserRow ToRoleUserRow(string role, UserInfo user, string assignment, string team)
        {
            return new RoleUserRow
            {
                Role = role,
                User = user.FullName,
                Domain = user.DomainName,
                BusinessUnit = user.BusinessUnitName,
                Assignment = assignment,
                Team = team ?? string.Empty,
                Status = user.Status
            };
        }
    }
}
