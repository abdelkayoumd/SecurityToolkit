using System;
using System.Collections.Generic;
using System.Linq;
using SecurityToolkit.Models;

namespace SecurityToolkit.Services
{
    /// <summary>What a privilege record is about: one action on one table.</summary>
    internal sealed class PrivilegeTarget
    {
        public string Table { get; set; }
        public PrivilegeKind Kind { get; set; }
    }

    /// <summary>One privilege a role holds, and how far it reaches.</summary>
    internal sealed class RoleGrant
    {
        public Guid PrivilegeId { get; set; }
        public PrivilegeDepth Depth { get; set; }
    }

    /// <summary>One table and, for each privilege, how far the user's roles reach (empty when they do not).</summary>
    internal sealed class MatrixRow
    {
        public string Table { get; set; }
        public string TableName { get; set; }
        public string Create { get; set; }
        public string Read { get; set; }
        public string Write { get; set; }
        public string Delete { get; set; }
        public string Append { get; set; }
        public string AppendTo { get; set; }
        public string Assign { get; set; }
        public string Share { get; set; }

        /// <summary>True when the user has at least one of the eight privileges on this table.</summary>
        public bool HasAny => new[] { Create, Read, Write, Delete, Append, AppendTo, Assign, Share }
            .Any(c => !string.IsNullOrEmpty(c) && c != AccessMatrixBuilder.None);
    }

    /// <summary>
    /// Builds the "what can this user do?" grid: for every table and privilege, the widest reach among all the roles
    /// the user holds, directly or through teams. Pure logic over loaded data.
    /// </summary>
    internal static class AccessMatrixBuilder
    {
        private static readonly StringComparer Names = StringComparer.CurrentCultureIgnoreCase;

        /// <summary>
        /// A cell where the privilege exists for the table but the user has no role granting it. (A cell that is blank
        /// means the privilege does not exist for that table at all, for instance Assign on a table nobody owns.)
        /// </summary>
        public const string None = "None";

        /// <summary>The ids of every role record the user holds, directly or through a team.</summary>
        public static IReadOnlyList<Guid> RoleIdsOf(SecurityGraph graph, UserInfo user)
        {
            return graph.DirectRolesOf(user.Id)
                .Concat(graph.TeamsOf(user.Id).SelectMany(t => graph.RolesOfTeam(t.Id)))
                .Select(r => r.Id).Distinct().ToList();
        }

        public static IReadOnlyList<MatrixRow> Build(
            SecurityGraph graph, UserInfo user, IEnumerable<TableInfo> tables,
            IReadOnlyDictionary<Guid, PrivilegeTarget> catalog,
            IReadOnlyDictionary<Guid, IReadOnlyList<RoleGrant>> grantsByRole, bool includeTablesWithoutAccess)
        {
            var best = new Dictionary<string, Dictionary<PrivilegeKind, PrivilegeDepth>>(Names);
            foreach (var roleId in RoleIdsOf(graph, user))
            {
                if (!grantsByRole.TryGetValue(roleId, out var grants))
                {
                    continue;
                }

                foreach (var grant in grants)
                {
                    if (grant.Depth == PrivilegeDepth.None || !catalog.TryGetValue(grant.PrivilegeId, out var target))
                    {
                        continue;
                    }

                    if (!best.TryGetValue(target.Table, out var byKind))
                    {
                        best[target.Table] = byKind = new Dictionary<PrivilegeKind, PrivilegeDepth>();
                    }

                    if (!byKind.TryGetValue(target.Kind, out var existing) || grant.Depth > existing)
                    {
                        byKind[target.Kind] = grant.Depth;
                    }
                }
            }

            // Which privileges exist at all for each table, so "not granted" can be told from "does not exist".
            var knownPrivileges = new HashSet<(string, PrivilegeKind)>(catalog.Values.Select(t => (t.Table.ToLowerInvariant(), t.Kind)));

            var rows = new List<MatrixRow>();
            foreach (var table in tables.OrderBy(t => t.DisplayName, Names))
            {
                best.TryGetValue(table.LogicalName, out var kinds);
                string Cell(PrivilegeKind kind)
                {
                    if (kinds != null && kinds.TryGetValue(kind, out var depth))
                    {
                        return Privileges.Label(depth);
                    }

                    return knownPrivileges.Contains((table.LogicalName.ToLowerInvariant(), kind)) ? None : string.Empty;
                }

                var row = new MatrixRow
                {
                    Table = table.DisplayName,
                    TableName = table.LogicalName,
                    Create = Cell(PrivilegeKind.Create),
                    Read = Cell(PrivilegeKind.Read),
                    Write = Cell(PrivilegeKind.Write),
                    Delete = Cell(PrivilegeKind.Delete),
                    Append = Cell(PrivilegeKind.Append),
                    AppendTo = Cell(PrivilegeKind.AppendTo),
                    Assign = Cell(PrivilegeKind.Assign),
                    Share = Cell(PrivilegeKind.Share)
                };

                if (includeTablesWithoutAccess || row.HasAny)
                {
                    rows.Add(row);
                }
            }

            return rows;
        }

        /// <summary>One line about the user's roles, for the summary.</summary>
        public static string DescribeRoles(SecurityGraph graph, UserInfo user)
        {
            var direct = graph.DirectRolesOf(user.Id).Count;
            var viaTeams = graph.TeamsOf(user.Id).SelectMany(t => graph.RolesOfTeam(t.Id)).Select(r => r.Id).Distinct().Count();
            if (direct + viaTeams == 0)
            {
                return $"{user.FullName} holds no security roles, so has no access.";
            }

            return $"{user.FullName} holds {direct} role{(direct == 1 ? string.Empty : "s")} directly and {viaTeams} through teams.";
        }
    }
}
