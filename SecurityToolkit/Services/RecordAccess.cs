using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SecurityToolkit.Models;

namespace SecurityToolkit.Services
{
    /// <summary>Who owns the records of a table, which decides how a privilege's depth applies to them.</summary>
    internal enum TableOwnership
    {
        /// <summary>Records belong to a user or a team (and sit in that owner's business unit).</summary>
        UserOrTeam,

        /// <summary>Records belong to a business unit only.</summary>
        BusinessUnit,

        /// <summary>Records belong to the organization: roles decide, depth does not apply.</summary>
        Organization
    }

    /// <summary>What is needed to read a record of a table: its name column and who owns its records.</summary>
    internal sealed class TableDetails
    {
        public string LogicalName { get; set; }
        public string DisplayName { get; set; }
        public string PrimaryNameAttribute { get; set; }
        public TableOwnership Ownership { get; set; }
    }

    /// <summary>One record found by name, to pick from.</summary>
    internal sealed class RecordMatch
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Id.ToString() : Name;
    }

    /// <summary>A record, and the facts about it that decide who can reach it.</summary>
    internal sealed class RecordFacts
    {
        public string Table { get; set; }
        public string TableDisplayName { get; set; }
        public Guid Id { get; set; }
        public string Name { get; set; }
        public TableOwnership Ownership { get; set; }

        /// <summary>The owning user or team, when the table has owners.</summary>
        public Guid? OwnerId { get; set; }
        public string OwnerName { get; set; }

        /// <summary>"systemuser" or "team".</summary>
        public string OwnerType { get; set; }

        public Guid? BusinessUnitId { get; set; }
        public string BusinessUnitName { get; set; }

        public bool OwnedByTeam => OwnerType == "team";
    }

    /// <summary>A principal the record is shared with, and which rights it was given.</summary>
    internal sealed class ShareRow
    {
        public Guid PrincipalId { get; set; }
        public string Principal { get; set; }

        /// <summary>"User" or "Team".</summary>
        public string Type { get; set; }
        public int Mask { get; set; }
        public string Rights { get; set; }
    }

    /// <summary>One right on the record: whether the principal has it, and why.</summary>
    internal sealed class RecordAccessRow
    {
        public string Right { get; set; }
        public string Result { get; set; }
        public string Why { get; set; }

        /// <summary>True when the platform's answer and my own reading of the roles and shares disagree.</summary>
        public bool Differs { get; set; }
        public PrivilegeKind Kind { get; set; }
    }

    /// <summary>The user or the team access is being checked for.</summary>
    internal sealed class AccessPrincipal
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public bool IsTeam { get; set; }
        public Guid? BusinessUnitId { get; set; }
        public string BusinessUnitName { get; set; }
        public string Type => IsTeam ? "Team" : "User";
        public string EntityName => IsTeam ? "team" : "systemuser";
    }

    /// <summary>A record shown as a link or an id: the pieces that can be read out of what was pasted.</summary>
    internal static class RecordLinks
    {
        private static readonly Regex Table = new Regex(@"[?&#]etn=([A-Za-z0-9_]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex Id = new Regex(@"(?:[?&#]id=)?\{?([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\}?", RegexOptions.Compiled);

        /// <summary>
        /// Reads a model-driven app record link (with etn= and id=) or a bare id. The table is null when only an id was
        /// given. Returns false for anything else, which is then treated as a name to search for.
        /// </summary>
        public static bool TryParse(string text, out string table, out Guid id)
        {
            table = null;
            id = Guid.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var trimmed = text.Trim();
            var tableMatch = Table.Match(trimmed);
            if (tableMatch.Success)
            {
                table = tableMatch.Groups[1].Value.ToLowerInvariant();
            }

            // With a link, the id is the one after "id="; a bare id may be wrapped in braces.
            var idPart = Regex.Match(trimmed, @"[?&#]id=\{?(?:%7[Bb])?([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
            var match = idPart.Success ? idPart : (trimmed.Length <= 40 ? Id.Match(trimmed) : Match.Empty);
            if (!match.Success || !Guid.TryParse(match.Groups[1].Value, out id))
            {
                table = null;
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Works out, for one principal and one record, whether each right is held and why: which role reaches the record,
    /// whether it was shared, or what falls short. The platform's own answer (when given) decides the result; this
    /// reading explains it, and says so when the two differ. Pure logic over the loaded security data.
    /// </summary>
    internal static class RecordAccessAnalyzer
    {
        private static readonly StringComparer Names = StringComparer.CurrentCultureIgnoreCase;

        /// <summary>The rights that apply to a record, in the order they are shown. (Create is about a table, not a record.)</summary>
        public static readonly PrivilegeKind[] Rights =
        {
            PrivilegeKind.Read, PrivilegeKind.Write, PrivilegeKind.Delete, PrivilegeKind.Append, PrivilegeKind.AppendTo, PrivilegeKind.Assign, PrivilegeKind.Share
        };

        private sealed class Route
        {
            public RoleInfo Role;
            public string Via;
            public PrivilegeDepth Depth;
        }

        /// <param name="grants">For each right, how far each role record grants it on the record's table (see <see cref="SecurityService.GetRoleGrantsFor"/>).</param>
        /// <param name="shares">Who the record is shared with.</param>
        /// <param name="platformMask">What the platform says the principal may do (an AccessRights mask), or null when not asked.</param>
        public static IReadOnlyList<RecordAccessRow> Analyze(
            SecurityGraph graph, AccessPrincipal principal, RecordFacts record,
            IReadOnlyDictionary<PrivilegeKind, IReadOnlyDictionary<Guid, PrivilegeDepth>> grants,
            IReadOnlyList<ShareRow> shares, int? platformMask)
        {
            var rows = new List<RecordAccessRow>();
            foreach (var kind in Rights)
            {
                grants.TryGetValue(kind, out var forKind);
                var routes = RoutesOf(graph, principal, forKind ?? new Dictionary<Guid, PrivilegeDepth>());
                var reaching = routes.Where(r => Reaches(graph, principal, record, r.Depth)).OrderByDescending(r => r.Depth).ThenBy(r => r.Role.Name, Names).ToList();
                var share = ShareFor(graph, principal, shares, kind);

                var mine = reaching.Count > 0 || share != null;
                string why;
                if (reaching.Count > 0)
                {
                    why = WhyByRole(principal, record, reaching[0], reaching.Count);
                }
                else if (share != null)
                {
                    why = $"Shared with {share.Principal} ({share.Rights}).";
                }
                else
                {
                    why = WhyNot(graph, principal, record, kind, routes);
                }

                var granted = platformMask.HasValue ? (platformMask.Value & Privileges.AccessRight(kind)) != 0 : mine;
                var differs = platformMask.HasValue && granted != mine;
                if (differs)
                {
                    why = granted
                        ? "Granted by the platform, but I could not find the route in the roles or shares; hierarchy security or another mechanism may apply."
                        : $"I expected access ({Lower(why)}) but the platform says no; check that the user is enabled and the record is active.";
                }

                rows.Add(new RecordAccessRow { Kind = kind, Right = Privileges.Label(kind), Result = granted ? "Yes" : "No", Why = why, Differs = differs });
            }

            return rows;
        }

        /// <summary>How the principal relates to the record, in words: "owns it", "same business unit", and so on.</summary>
        public static string Relation(SecurityGraph graph, AccessPrincipal principal, RecordFacts record)
        {
            if (OwnsRecord(graph, principal, record, out var viaTeam))
            {
                return viaTeam != null ? $"the record is owned by {viaTeam}, a team {principal.Name} belongs to" : $"{principal.Name} owns the record";
            }

            if (record.Ownership == TableOwnership.Organization)
            {
                return "the record belongs to the organization";
            }

            if (!record.BusinessUnitId.HasValue || !principal.BusinessUnitId.HasValue)
            {
                return "the record's business unit is not known";
            }

            if (record.BusinessUnitId == principal.BusinessUnitId)
            {
                return "the record is in the same business unit";
            }

            if (graph.BusinessUnitScope(principal.BusinessUnitId.Value, true).Contains(record.BusinessUnitId.Value))
            {
                return "the record is in a business unit below " + principal.Name + "'s";
            }

            return graph.BusinessUnitScope(record.BusinessUnitId.Value, true).Contains(principal.BusinessUnitId.Value)
                ? "the record is in a parent business unit"
                : "the record is in an unrelated business unit";
        }

        private static List<Route> RoutesOf(SecurityGraph graph, AccessPrincipal principal, IReadOnlyDictionary<Guid, PrivilegeDepth> grants)
        {
            var routes = new List<Route>();
            void Add(RoleInfo role, string via)
            {
                if (grants.TryGetValue(role.Id, out var depth) && depth != PrivilegeDepth.None)
                {
                    routes.Add(new Route { Role = role, Via = via, Depth = depth });
                }
            }

            if (principal.IsTeam)
            {
                foreach (var role in graph.RolesOfTeam(principal.Id))
                {
                    Add(role, "held by the team");
                }
            }
            else
            {
                foreach (var role in graph.DirectRolesOf(principal.Id))
                {
                    Add(role, "assigned directly");
                }

                foreach (var team in graph.TeamsOf(principal.Id))
                {
                    foreach (var role in graph.RolesOfTeam(team.Id))
                    {
                        Add(role, "through team " + team.Name);
                    }
                }
            }

            return routes;
        }

        /// <summary>True when the principal owns the record: as the owner, or (for a user) as a member of the owning team.</summary>
        private static bool OwnsRecord(SecurityGraph graph, AccessPrincipal principal, RecordFacts record, out string viaTeam)
        {
            viaTeam = null;
            if (record.Ownership != TableOwnership.UserOrTeam || !record.OwnerId.HasValue)
            {
                return false;
            }

            if (record.OwnerId == principal.Id)
            {
                return true;
            }

            if (!principal.IsTeam && record.OwnedByTeam && graph.TeamsOf(principal.Id).Any(t => t.Id == record.OwnerId))
            {
                viaTeam = record.OwnerName;
                return true;
            }

            return false;
        }

        /// <summary>Whether a role's depth reaches this record.</summary>
        private static bool Reaches(SecurityGraph graph, AccessPrincipal principal, RecordFacts record, PrivilegeDepth depth)
        {
            if (record.Ownership == TableOwnership.Organization || depth == PrivilegeDepth.Organization)
            {
                return depth != PrivilegeDepth.None;
            }

            switch (depth)
            {
                case PrivilegeDepth.User:
                    return OwnsRecord(graph, principal, record, out _);
                case PrivilegeDepth.BusinessUnit:
                    return record.BusinessUnitId.HasValue && record.BusinessUnitId == principal.BusinessUnitId;
                case PrivilegeDepth.ParentChildBusinessUnits:
                    return record.BusinessUnitId.HasValue && principal.BusinessUnitId.HasValue
                           && graph.BusinessUnitScope(principal.BusinessUnitId.Value, true).Contains(record.BusinessUnitId.Value);
                default:
                    return false;
            }
        }

        /// <summary>The share that gives this right to the principal itself, or to a team it belongs to.</summary>
        private static ShareRow ShareFor(SecurityGraph graph, AccessPrincipal principal, IReadOnlyList<ShareRow> shares, PrivilegeKind kind)
        {
            var bit = Privileges.AccessRight(kind);
            var teams = principal.IsTeam ? new HashSet<Guid>() : new HashSet<Guid>(graph.TeamsOf(principal.Id).Select(t => t.Id));
            return shares.FirstOrDefault(s => (s.Mask & bit) != 0 && (s.PrincipalId == principal.Id || teams.Contains(s.PrincipalId)));
        }

        private static string WhyByRole(AccessPrincipal principal, RecordFacts record, Route route, int count)
        {
            var more = count > 1 ? $" (and {count - 1} more route{(count == 2 ? string.Empty : "s")})" : string.Empty;
            var reach = record.Ownership == TableOwnership.Organization
                ? "records of this table belong to the organization, so the role is enough"
                : DescribeReach(principal, record, route.Depth);
            return $"Role \"{route.Role.Name}\" ({route.Via}) grants it at {Privileges.Label(route.Depth)} depth; {reach}.{more}";
        }

        private static string DescribeReach(AccessPrincipal principal, RecordFacts record, PrivilegeDepth depth)
        {
            switch (depth)
            {
                case PrivilegeDepth.Organization: return "that covers every record";
                case PrivilegeDepth.ParentChildBusinessUnits: return "the record's business unit is " + principal.BusinessUnitName + " or below it";
                case PrivilegeDepth.BusinessUnit: return "the record is in " + principal.BusinessUnitName;
                default: return record.OwnedByTeam ? "the record is owned by a team the principal belongs to" : "the principal owns the record";
            }
        }

        private static string WhyNot(SecurityGraph graph, AccessPrincipal principal, RecordFacts record, PrivilegeKind kind, IReadOnlyList<Route> routes)
        {
            var action = Privileges.Label(kind);
            if (routes.Count == 0)
            {
                return $"No role {principal.Name} holds grants {action} on {record.TableDisplayName}, and the record is not shared with {principal.Name}.";
            }

            var best = routes.OrderByDescending(r => r.Depth).First();
            var owner = record.OwnerName != null ? $" (owner: {record.OwnerName})" : string.Empty;
            string gap;
            switch (best.Depth)
            {
                case PrivilegeDepth.User:
                    gap = $"{principal.Name} does not own the record{owner}";
                    break;
                case PrivilegeDepth.BusinessUnit:
                    gap = $"the record is in {record.BusinessUnitName ?? "another business unit"}, not in {principal.BusinessUnitName}";
                    break;
                default:
                    gap = $"the record is in {record.BusinessUnitName ?? "another business unit"}, which is neither {principal.BusinessUnitName} nor below it";
                    break;
            }

            // The gap first: it is the useful part, and the cell may be clipped.
            return $"{char.ToUpperInvariant(gap[0])}{gap.Substring(1)} (best role: \"{best.Role.Name}\", {best.Via}, reaches {Privileges.Label(best.Depth)}).";
        }

        private static string Lower(string text)
        {
            return string.IsNullOrEmpty(text) ? text : char.ToLowerInvariant(text[0]) + text.Substring(1).TrimEnd('.');
        }

        /// <summary>A rights mask as words: "Read, Write".</summary>
        public static string RightsText(int mask)
        {
            var names = Rights.Concat(new[] { PrivilegeKind.Create }).Where(k => (mask & Privileges.AccessRight(k)) != 0).Select(Privileges.Label).ToList();
            return names.Count == 0 ? "no rights" : string.Join(", ", names);
        }
    }
}
