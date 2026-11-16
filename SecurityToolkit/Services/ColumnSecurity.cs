using System;
using System.Collections.Generic;
using System.Linq;
using SecurityToolkit.Models;

namespace SecurityToolkit.Services
{
    /// <summary>A column security profile, with how many members and secured columns it has.</summary>
    internal sealed class ColumnProfile
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int Users { get; set; }
        public int Teams { get; set; }
        public int Columns { get; set; }
    }

    /// <summary>What one profile allows on one secured column.</summary>
    internal sealed class ColumnPermission
    {
        public Guid ProfileId { get; set; }
        public string Table { get; set; }
        public string Column { get; set; }
        public bool CanRead { get; set; }
        public bool CanUpdate { get; set; }
        public bool CanCreate { get; set; }
    }

    /// <summary>One row of a profile's columns, or of a user's columns: a secured column and what is allowed on it.</summary>
    internal sealed class ColumnAccessRow
    {
        public string Table { get; set; }
        public string Column { get; set; }
        public string Read { get; set; }
        public string Update { get; set; }
        public string Create { get; set; }

        /// <summary>For a user: the profiles that give it, and the team when through one.</summary>
        public string Through { get; set; }
    }

    /// <summary>One member of a profile: a user (directly or through a team) or a team.</summary>
    internal sealed class ProfileMemberRow
    {
        public string Member { get; set; }
        public string Type { get; set; }
        public string Via { get; set; }
        public string BusinessUnit { get; set; }
        public string Status { get; set; }

        /// <summary>The user or team, so a row can be opened on another screen.</summary>
        public Guid? UserId { get; set; }
        public Guid? TeamId { get; set; }
    }

    /// <summary>
    /// Column (field) security as read from Dataverse: the profiles, what each allows on which secured columns, and who
    /// holds them, directly or through a team. Answers "who can read or update this secured column?" both ways: per
    /// profile and per user.
    /// </summary>
    internal sealed class ColumnSecurity
    {
        private static readonly StringComparer Names = StringComparer.CurrentCultureIgnoreCase;
        private readonly IReadOnlyList<ColumnPermission> _permissions;
        private readonly ILookup<Guid, Guid> _usersOfProfile;
        private readonly ILookup<Guid, Guid> _teamsOfProfile;

        public ColumnSecurity(IEnumerable<ColumnProfile> profiles, IEnumerable<ColumnPermission> permissions,
            IEnumerable<KeyValuePair<Guid, Guid>> userProfiles, IEnumerable<KeyValuePair<Guid, Guid>> teamProfiles)
        {
            _permissions = permissions.ToList();
            _usersOfProfile = userProfiles.ToLookup(l => l.Value, l => l.Key);
            _teamsOfProfile = teamProfiles.ToLookup(l => l.Value, l => l.Key);
            Profiles = profiles.Select(p => new ColumnProfile
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                Users = _usersOfProfile[p.Id].Distinct().Count(),
                Teams = _teamsOfProfile[p.Id].Distinct().Count(),
                Columns = _permissions.Count(c => c.ProfileId == p.Id)
            }).OrderBy(p => p.Name, Names).ToList();
        }

        public IReadOnlyList<ColumnProfile> Profiles { get; }

        /// <summary>The secured columns a profile covers, and what it allows on each.</summary>
        public IReadOnlyList<ColumnAccessRow> ColumnsOf(Guid profileId)
        {
            return _permissions.Where(p => p.ProfileId == profileId)
                .Select(p => new ColumnAccessRow { Table = p.Table, Column = p.Column, Read = YesNo(p.CanRead), Update = YesNo(p.CanUpdate), Create = YesNo(p.CanCreate) })
                .OrderBy(r => r.Table, Names).ThenBy(r => r.Column, Names).ToList();
        }

        /// <summary>Who holds a profile: its teams, the members of those teams, and the users who hold it directly.</summary>
        public IReadOnlyList<ProfileMemberRow> MembersOf(Guid profileId, SecurityGraph graph)
        {
            var users = graph.Users.ToDictionary(u => u.Id);
            var teams = graph.Teams.ToDictionary(t => t.Id);
            var rows = new List<ProfileMemberRow>();
            foreach (var userId in _usersOfProfile[profileId].Distinct().Where(users.ContainsKey))
            {
                var user = users[userId];
                rows.Add(new ProfileMemberRow { Member = user.FullName, Type = "User", Via = "Direct", BusinessUnit = user.BusinessUnitName, Status = user.Status, UserId = user.Id });
            }

            foreach (var teamId in _teamsOfProfile[profileId].Distinct().Where(teams.ContainsKey))
            {
                var team = teams[teamId];
                rows.Add(new ProfileMemberRow { Member = team.Name, Type = "Team", Via = "Direct", BusinessUnit = team.BusinessUnitName, Status = team.TypeName + " team", TeamId = team.Id });
                foreach (var user in graph.Users.Where(u => graph.TeamsOf(u.Id).Any(t => t.Id == teamId)))
                {
                    rows.Add(new ProfileMemberRow { Member = user.FullName, Type = "User", Via = "Team: " + team.Name, BusinessUnit = user.BusinessUnitName, Status = user.Status, UserId = user.Id });
                }
            }

            return rows.OrderBy(r => r.Type == "Team" ? 0 : 1).ThenBy(r => r.Member, Names).ThenBy(r => r.Via, Names).ToList();
        }

        /// <summary>The profiles a user holds, directly or through their teams: profile id and how ("Direct", "Team: Sales").</summary>
        public IReadOnlyList<KeyValuePair<Guid, string>> ProfilesOf(UserInfo user, SecurityGraph graph)
        {
            var held = new List<KeyValuePair<Guid, string>>();
            foreach (var profile in Profiles)
            {
                if (_usersOfProfile[profile.Id].Contains(user.Id))
                {
                    held.Add(new KeyValuePair<Guid, string>(profile.Id, "Direct"));
                }

                foreach (var team in graph.TeamsOf(user.Id).Where(t => _teamsOfProfile[profile.Id].Contains(t.Id)))
                {
                    held.Add(new KeyValuePair<Guid, string>(profile.Id, "Team: " + team.Name));
                }
            }

            return held;
        }

        /// <summary>
        /// Every secured column a user's profiles cover. A right is allowed when any of their profiles allows it, as
        /// in Dataverse; "Through" names the profiles behind each column.
        /// </summary>
        public IReadOnlyList<ColumnAccessRow> ColumnsOfUser(UserInfo user, SecurityGraph graph)
        {
            var names = Profiles.ToDictionary(p => p.Id, p => p.Name);
            var held = ProfilesOf(user, graph);
            var how = held.GroupBy(h => h.Key).ToDictionary(g => g.Key, g => g.Select(h => h.Value).ToList());
            return _permissions.Where(p => how.ContainsKey(p.ProfileId))
                .GroupBy(p => (Table: p.Table.ToLowerInvariant(), Column: p.Column.ToLowerInvariant()))
                .Select(g => new ColumnAccessRow
                {
                    Table = g.First().Table,
                    Column = g.First().Column,
                    Read = YesNo(g.Any(p => p.CanRead)),
                    Update = YesNo(g.Any(p => p.CanUpdate)),
                    Create = YesNo(g.Any(p => p.CanCreate)),
                    Through = string.Join(", ", g.Select(p => p.ProfileId).Distinct()
                        .Select(id => how[id].Contains("Direct") ? names[id] : $"{names[id]} ({string.Join(", ", how[id].Where(v => v != "Direct"))})"))
                })
                .OrderBy(r => r.Table, Names).ThenBy(r => r.Column, Names).ToList();
        }

        private static string YesNo(bool allowed) => allowed ? "Yes" : "No";
    }
}
