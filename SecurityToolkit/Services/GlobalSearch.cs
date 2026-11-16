using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SecurityToolkit.Models;

namespace SecurityToolkit.Services
{
    /// <summary>One result of the search box: a user, team, role or table, and a line saying which one it is.</summary>
    internal sealed class SearchHit
    {
        public Subject Subject { get; set; }

        /// <summary>What tells two hits apart: a user's login and business unit, a team's type, a table's logical name...</summary>
        public string Detail { get; set; }

        /// <summary>How well the name matched: 0 exact, 1 starts with the text, 2 a word starts with it, 3 contains it.</summary>
        public int Rank { get; set; }

        public override string ToString() => Subject.Name;
    }

    /// <summary>The search box behind "find a user, team, role or table from anywhere".</summary>
    internal static class GlobalSearch
    {
        private static readonly CompareInfo Compare = CultureInfo.CurrentCulture.CompareInfo;
        private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

        /// <summary>
        /// Everything whose name (or login, or logical name) contains <paramref name="text"/>, best matches first:
        /// exact names, then names starting with the text, then a word starting with it, then the rest. Case and
        /// accents are ignored. Disabled users come after active ones of the same rank.
        /// </summary>
        public static IReadOnlyList<SearchHit> Find(string text, SecurityGraph graph, IReadOnlyList<TableInfo> tables, int max = 30)
        {
            var query = (text ?? string.Empty).Trim();
            if (query.Length == 0)
            {
                return new SearchHit[0];
            }

            var hits = new List<Candidate>();
            if (graph != null)
            {
                foreach (var user in graph.Users)
                {
                    var rank = Best(query, user.FullName, user.DomainName);
                    if (rank >= 0)
                    {
                        var detail = string.Join(" · ", new[] { user.DomainName, user.BusinessUnitName, user.IsDisabled ? "disabled" : null }.Where(s => !string.IsNullOrEmpty(s)));
                        hits.Add(new Candidate(new Subject { Kind = SubjectKind.User, Id = user.Id, Name = user.FullName }, detail, rank, user.IsDisabled));
                    }
                }

                foreach (var team in graph.Teams)
                {
                    var rank = Best(query, team.Name);
                    if (rank >= 0)
                    {
                        hits.Add(new Candidate(new Subject { Kind = SubjectKind.Team, Id = team.Id, Name = team.Name }, team.TypeName + " team · " + team.BusinessUnitName, rank, false));
                    }
                }

                foreach (var role in graph.RoleChoices())
                {
                    var rank = Best(query, role.Name);
                    if (rank >= 0)
                    {
                        var copies = role.Copies == 1 ? "1 business unit" : role.Copies + " business units";
                        hits.Add(new Candidate(new Subject { Kind = SubjectKind.Role, Id = role.RootKey, Name = role.Name }, "Security role · " + copies, rank, false));
                    }
                }
            }

            foreach (var table in tables ?? new TableInfo[0])
            {
                var rank = Best(query, table.DisplayName, table.LogicalName);
                if (rank >= 0)
                {
                    hits.Add(new Candidate(new Subject { Kind = SubjectKind.Table, Name = table.DisplayName, LogicalName = table.LogicalName }, "Table · " + table.LogicalName, rank, false));
                }
            }

            return hits
                .OrderBy(h => h.Rank).ThenBy(h => h.Muted).ThenBy(h => h.Subject.Kind).ThenBy(h => h.Subject.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(max)
                .Select(h => new SearchHit { Subject = h.Subject, Detail = h.Detail, Rank = h.Rank })
                .ToList();
        }

        /// <summary>The best rank of <paramref name="query"/> among the names, or -1 when none contains it.</summary>
        internal static int Best(string query, params string[] names)
        {
            var best = -1;
            foreach (var name in names.Where(n => !string.IsNullOrEmpty(n)))
            {
                var rank = RankOf(query, name);
                if (rank >= 0 && (best < 0 || rank < best))
                {
                    best = rank;
                }
            }

            return best;
        }

        private static int RankOf(string query, string name)
        {
            if (Compare.Compare(name, query, Loose) == 0)
            {
                return 0;
            }

            var at = Compare.IndexOf(name, query, Loose);
            if (at < 0)
            {
                return -1;
            }

            if (at == 0)
            {
                return 1;
            }

            // A word starts with the text somewhere: "sales" in "EMEA Sales Manager", "smith" in "john.smith@x".
            for (var i = at; i >= 0 && i < name.Length; i = Compare.IndexOf(name, query, i + 1, Loose))
            {
                if (!char.IsLetterOrDigit(name[i - 1]))
                {
                    return 2;
                }

                if (i + 1 >= name.Length)
                {
                    break;
                }
            }

            return 3;
        }

        private sealed class Candidate
        {
            public Candidate(Subject subject, string detail, int rank, bool muted)
            {
                Subject = subject;
                Detail = detail;
                Rank = rank;
                Muted = muted;
            }

            public Subject Subject { get; }
            public string Detail { get; }
            public int Rank { get; }
            public bool Muted { get; }
        }
    }
}
