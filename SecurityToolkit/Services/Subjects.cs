using System;
using System.Collections.Generic;
using System.Linq;
using SecurityToolkit.Features;
using SecurityToolkit.Models;

namespace SecurityToolkit.Services
{
    /// <summary>The kinds of thing a screen can be opened on.</summary>
    internal enum SubjectKind
    {
        User,
        Team,
        Role,
        Table
    }

    /// <summary>
    /// A user, team, role or table picked on one screen and carried to another. A role is identified by its root
    /// (all its business-unit copies at once); a table by its logical name when known, otherwise by its display name.
    /// </summary>
    internal sealed class Subject
    {
        public SubjectKind Kind { get; set; }

        /// <summary>The user or team id, or the role's root key. Empty for a table.</summary>
        public Guid Id { get; set; }

        /// <summary>What the user reads: the user's full name, the team, role or table display name.</summary>
        public string Name { get; set; }

        /// <summary>A table's logical name, when the row it came from knew it.</summary>
        public string LogicalName { get; set; }

        public string KindText => Kind.ToString().ToLowerInvariant();

        public override string ToString() => Kind + ": " + Name;
    }

    /// <summary>Finds the subjects a grid row is about, and recognises the row that stands for a subject.</summary>
    internal static class Subjects
    {
        private static readonly StringComparer Names = StringComparer.CurrentCultureIgnoreCase;

        /// <summary>
        /// The users, teams, roles and tables a row is about, in that order. Rows of the model types are exact.
        /// Report rows that only carry names ("User", "Team", "Role", "Table") are looked up in
        /// <paramref name="graph"/>, and a name that is not unique there is left out rather than guessed.
        /// </summary>
        public static IReadOnlyList<Subject> From(object row, SecurityGraph graph)
        {
            var found = new List<Subject>();
            switch (row)
            {
                case null:
                    return found;
                case UserInfo user:
                    found.Add(User(user));
                    return found;
                case TeamInfo team:
                    found.Add(Team(team));
                    return found;
                case RoleChoice choice:
                    found.Add(new Subject { Kind = SubjectKind.Role, Id = choice.RootKey, Name = choice.Name });
                    return found;
                case RoleRow role:
                    found.Add(new Subject { Kind = SubjectKind.Role, Id = role.RootKey, Name = role.Name });
                    return found;
                case RoleInfo info:
                    found.Add(new Subject { Kind = SubjectKind.Role, Id = info.RootKey, Name = info.Name });
                    return found;
                case TableInfo table:
                    found.Add(new Subject { Kind = SubjectKind.Table, Name = table.DisplayName, LogicalName = table.LogicalName });
                    return found;
                case PrincipalRow principal:
                    found.Add(new Subject { Kind = principal.IsTeam ? SubjectKind.Team : SubjectKind.User, Id = principal.Id, Name = principal.Name });
                    return found;
                case ShareRow share when share.PrincipalId != Guid.Empty && (share.Type == "User" || share.Type == "Team"):
                    found.Add(new Subject { Kind = share.Type == "Team" ? SubjectKind.Team : SubjectKind.User, Id = share.PrincipalId, Name = share.Principal });
                    return found;
            }

            var userName = Text(row, "User");
            if (userName != null && graph != null)
            {
                var domain = Text(row, "Domain");
                var users = graph.Users.Where(u => Names.Equals(u.FullName, userName) && (domain == null || Names.Equals(u.DomainName ?? string.Empty, domain))).ToList();
                if (users.Count == 1)
                {
                    found.Add(User(users[0]));
                }
            }

            var teamName = Text(row, "Team");
            if (teamName != null && graph != null)
            {
                var teams = graph.Teams.Where(t => Names.Equals(t.Name, teamName)).ToList();
                if (teams.Count == 1)
                {
                    found.Add(Team(teams[0]));
                }
            }

            var roleName = Text(row, "Role");
            if (roleName != null && graph != null)
            {
                var roles = graph.RoleChoices().Where(r => Names.Equals(r.Name, roleName)).ToList();
                if (roles.Count == 1)
                {
                    found.Add(new Subject { Kind = SubjectKind.Role, Id = roles[0].RootKey, Name = roles[0].Name });
                }
            }

            var tableName = Text(row, "Table");
            if (tableName != null)
            {
                found.Add(new Subject { Kind = SubjectKind.Table, Name = tableName });
            }

            return found;
        }

        /// <summary>True when <paramref name="row"/> (an item of a screen's pick list) stands for <paramref name="subject"/>.</summary>
        public static bool Matches(Subject subject, object row)
        {
            switch (subject.Kind)
            {
                case SubjectKind.User:
                    return (row is UserInfo user && user.Id == subject.Id) || (row is PrincipalRow p && !p.IsTeam && p.Id == subject.Id);
                case SubjectKind.Team:
                    return (row is TeamInfo team && team.Id == subject.Id) || (row is PrincipalRow t && t.IsTeam && t.Id == subject.Id);
                case SubjectKind.Role:
                    return (row is RoleChoice choice && choice.RootKey == subject.Id) || (row is RoleRow role && role.RootKey == subject.Id);
                case SubjectKind.Table:
                    return row is TableInfo table && (subject.LogicalName != null
                        ? string.Equals(table.LogicalName, subject.LogicalName, StringComparison.OrdinalIgnoreCase)
                        : Names.Equals(table.DisplayName, subject.Name));
                default:
                    return false;
            }
        }

        private static Subject User(UserInfo user) => new Subject { Kind = SubjectKind.User, Id = user.Id, Name = user.FullName };

        private static Subject Team(TeamInfo team) => new Subject { Kind = SubjectKind.Team, Id = team.Id, Name = team.Name };

        /// <summary>A non-empty string property of the row, or null.</summary>
        private static string Text(object row, string property)
        {
            var info = row.GetType().GetProperty(property);
            var value = info != null && info.PropertyType == typeof(string) ? (string)info.GetValue(row) : null;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
