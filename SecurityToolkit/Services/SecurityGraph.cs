using System;
using System.Collections.Generic;
using System.Linq;
using SecurityToolkit.Models;

namespace SecurityToolkit.Services
{
    internal sealed class RoleUserRow
    {
        public string User { get; set; }
        public string Domain { get; set; }
        public string BusinessUnit { get; set; }
        /// <summary>"Direct" or "Via team".</summary>
        public string Assignment { get; set; }
        public string Team { get; set; }
        public string Status { get; set; }
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
        }

        public IReadOnlyList<UserInfo> Users { get; }
        public IReadOnlyList<RoleInfo> Roles { get; }
        public IReadOnlyList<TeamInfo> Teams { get; }
        public IReadOnlyList<BusinessUnitInfo> BusinessUnits { get; }

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
            var rows = new List<RoleUserRow>();

            foreach (var user in Users.Where(u => includeDisabled || !u.IsDisabled))
            {
                if (_roleIdsByUser[user.Id].Any(roleIds.Contains))
                {
                    rows.Add(ToRoleUserRow(user, "Direct", null));
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
                    rows.Add(ToRoleUserRow(member, "Via team", team.Name));
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

        private IReadOnlyList<RoleInfo> Resolve(IEnumerable<Guid> roleIds)
        {
            return roleIds.Select(id => _roles.TryGetValue(id, out var r) ? r : null).Where(r => r != null)
                .Distinct().OrderBy(r => r.Name, Names).ToList();
        }

        private static RoleUserRow ToRoleUserRow(UserInfo user, string assignment, string team)
        {
            return new RoleUserRow
            {
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
