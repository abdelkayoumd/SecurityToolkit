using System;
using System.Collections.Generic;
using System.Linq;
using SecurityToolkit.Models;

namespace SecurityToolkit.Services
{
    /// <summary>One role in the list to pick from: a role once, however many business-unit copies exist.</summary>
    internal sealed class RoleRow
    {
        public Guid RoleId { get; set; }
        public Guid RootKey { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
    }

    /// <summary>One table and privilege, and how far each of the two roles reaches for it.</summary>
    internal sealed class RoleDiffRow
    {
        public string Table { get; set; }
        public string Privilege { get; set; }
        public string A { get; set; }
        public string B { get; set; }
        public string Result { get; set; }
        public bool IsDifference { get; set; }
    }

    /// <summary>
    /// Compares what two roles grant, privilege by privilege. Pure logic over the privilege catalog and each role's
    /// grants. Only privileges at least one of the roles holds are listed.
    /// </summary>
    internal static class RoleComparer
    {
        private static readonly StringComparer Names = StringComparer.CurrentCultureIgnoreCase;

        private static readonly PrivilegeKind[] Order =
        {
            PrivilegeKind.Create, PrivilegeKind.Read, PrivilegeKind.Write, PrivilegeKind.Delete,
            PrivilegeKind.Append, PrivilegeKind.AppendTo, PrivilegeKind.Assign, PrivilegeKind.Share
        };

        /// <summary>One entry per role, whatever the number of business-unit copies, by name.</summary>
        public static IReadOnlyList<RoleRow> RolesToPick(SecurityGraph graph)
        {
            return graph.Roles.GroupBy(r => r.RootKey)
                .Select(g => g.OrderByDescending(r => r.RootRoleId == null).First())
                .Select(r => new RoleRow { RoleId = r.Id, RootKey = r.RootKey, Name = r.Name, Type = r.IsManaged ? "Managed" : "Custom" })
                .OrderBy(r => r.Name, Names).ToList();
        }

        /// <summary>The names to head the two columns and the results with: the role names, told apart if they are the same.</summary>
        public static Tuple<string, string> Labels(RoleRow a, RoleRow b)
        {
            return string.Equals(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase)
                ? Tuple.Create(a.Name + " (1)", b.Name + " (2)")
                : Tuple.Create(a.Name, b.Name);
        }

        public static IReadOnlyList<RoleDiffRow> Compare(
            RoleRow a, RoleRow b, IEnumerable<TableInfo> tables,
            IReadOnlyDictionary<Guid, PrivilegeTarget> catalog,
            IReadOnlyList<RoleGrant> grantsA, IReadOnlyList<RoleGrant> grantsB)
        {
            var labels = Labels(a, b);
            var depthsA = Widest(grantsA, catalog);
            var depthsB = Widest(grantsB, catalog);
            var display = tables.ToDictionary(t => t.LogicalName, t => t.DisplayName, Names);

            var rows = new List<RoleDiffRow>();
            foreach (var key in depthsA.Keys.Union(depthsB.Keys))
            {
                depthsA.TryGetValue(key, out var depthA);
                depthsB.TryGetValue(key, out var depthB);

                string result;
                if (depthA == depthB) { result = "Same"; }
                else if (depthB == PrivilegeDepth.None) { result = "Only " + labels.Item1; }
                else if (depthA == PrivilegeDepth.None) { result = "Only " + labels.Item2; }
                else { result = (depthA > depthB ? labels.Item1 : labels.Item2) + " reaches further"; }

                rows.Add(new RoleDiffRow
                {
                    Table = display.TryGetValue(key.Item1, out var shown) && !string.IsNullOrEmpty(shown) ? shown : key.Item1,
                    Privilege = Privileges.Label(key.Item2),
                    A = Privileges.Label(depthA),
                    B = Privileges.Label(depthB),
                    Result = result,
                    IsDifference = depthA != depthB
                });
            }

            return rows.OrderBy(r => r.Table, Names).ThenBy(r => Array.FindIndex(Order, k => Privileges.Label(k) == r.Privilege)).ToList();
        }

        /// <summary>The summary sentence over the rows.</summary>
        public static string Summarize(IReadOnlyList<RoleDiffRow> rows, RoleRow a, RoleRow b)
        {
            var labels = Labels(a, b);
            var onlyA = rows.Count(r => r.Result == "Only " + labels.Item1);
            var onlyB = rows.Count(r => r.Result == "Only " + labels.Item2);
            var reach = rows.Count(r => r.IsDifference) - onlyA - onlyB;
            var same = rows.Count(r => !r.IsDifference);
            return $"{labels.Item1} and {labels.Item2}: {onlyA} privilege(s) only {labels.Item1}, {onlyB} only {labels.Item2}, " +
                   $"{reach} with a different reach, {same} the same.";
        }

        private static Dictionary<(string, PrivilegeKind), PrivilegeDepth> Widest(IEnumerable<RoleGrant> grants, IReadOnlyDictionary<Guid, PrivilegeTarget> catalog)
        {
            var widest = new Dictionary<(string, PrivilegeKind), PrivilegeDepth>();
            foreach (var grant in grants)
            {
                if (grant.Depth == PrivilegeDepth.None || !catalog.TryGetValue(grant.PrivilegeId, out var target))
                {
                    continue;
                }

                var key = (target.Table.ToLowerInvariant(), target.Kind);
                if (!widest.TryGetValue(key, out var existing) || grant.Depth > existing)
                {
                    widest[key] = grant.Depth;
                }
            }

            return widest;
        }
    }
}
