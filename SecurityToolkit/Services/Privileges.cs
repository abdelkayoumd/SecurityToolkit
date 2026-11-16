using System;
using System.Collections.Generic;
using System.Linq;

namespace SecurityToolkit.Services
{
    /// <summary>The eight table privileges of a security role.</summary>
    internal enum PrivilegeKind
    {
        Create,
        Read,
        Write,
        Delete,
        Append,
        AppendTo,
        Assign,
        Share
    }

    /// <summary>How far a privilege reaches. The numbers are the bits of a role privilege's depth mask.</summary>
    internal enum PrivilegeDepth
    {
        None = 0,
        User = 1,
        BusinessUnit = 2,
        ParentChildBusinessUnits = 4,
        Organization = 8
    }

    /// <summary>A privilege that is not about a table (Export to Excel, Bulk Delete...): a "miscellaneous privilege".</summary>
    internal sealed class OtherPrivilege
    {
        private static readonly HashSet<string> SmallWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "and", "as", "at", "by", "for", "from", "in", "into", "of", "on", "or", "the", "to", "with"
        };

        public Guid Id { get; set; }

        /// <summary>The privilege's own name, "prvExportToExcel".</summary>
        public string Name { get; set; }

        /// <summary>The name in words, "Export to Excel".</summary>
        public string Label { get; set; }

        /// <summary>"prvActOnBehalfOfAnotherUser" reads "Act on Behalf of Another User"; acronyms stay together ("prvUseCRM" reads "Use CRM").</summary>
        public static string LabelOf(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var text = name.StartsWith("prv", StringComparison.Ordinal) ? name.Substring(3) : name;
            var words = new List<string>();
            var current = new System.Text.StringBuilder();
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                var startsWord = i > 0 && char.IsUpper(c)
                    && (char.IsLower(text[i - 1]) || char.IsDigit(text[i - 1]) || (i + 1 < text.Length && char.IsLower(text[i + 1]) && char.IsUpper(text[i - 1])));
                if ((startsWord || c == '_' || c == ' ') && current.Length > 0)
                {
                    words.Add(current.ToString());
                    current.Clear();
                }

                if (c != '_' && c != ' ')
                {
                    current.Append(c);
                }
            }

            if (current.Length > 0)
            {
                words.Add(current.ToString());
            }

            return string.Join(" ", words.Select((w, i) => i > 0 && SmallWords.Contains(w) ? w.ToLowerInvariant() : w));
        }

        public override string ToString() => Label;
    }

    internal sealed class RolePrivilegeRow
    {
        public string Table { get; set; }
        public string Role { get; set; }
        public string BusinessUnit { get; set; }

        /// <summary>The widest depth among the privileges that were asked for.</summary>
        public string Depth { get; set; }
        public int DepthLevel { get; set; }

        // One depth per privilege, in words; filled for the privileges that were asked for.
        public string Create { get; set; }
        public string Read { get; set; }
        public string Write { get; set; }
        public string Delete { get; set; }
        public string Append { get; set; }
        public string AppendTo { get; set; }
        public string Assign { get; set; }
        public string Share { get; set; }

        public string Get(PrivilegeKind kind)
        {
            switch (kind)
            {
                case PrivilegeKind.Create: return Create;
                case PrivilegeKind.Read: return Read;
                case PrivilegeKind.Write: return Write;
                case PrivilegeKind.Delete: return Delete;
                case PrivilegeKind.Append: return Append;
                case PrivilegeKind.AppendTo: return AppendTo;
                case PrivilegeKind.Assign: return Assign;
                case PrivilegeKind.Share: return Share;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public void Set(PrivilegeKind kind, string depth)
        {
            switch (kind)
            {
                case PrivilegeKind.Create: Create = depth; break;
                case PrivilegeKind.Read: Read = depth; break;
                case PrivilegeKind.Write: Write = depth; break;
                case PrivilegeKind.Delete: Delete = depth; break;
                case PrivilegeKind.Append: Append = depth; break;
                case PrivilegeKind.AppendTo: AppendTo = depth; break;
                case PrivilegeKind.Assign: Assign = depth; break;
                case PrivilegeKind.Share: Share = depth; break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }

    internal static class Privileges
    {
        /// <summary>The "accessright" value Dataverse stores on a privilege record.</summary>
        public static int AccessRight(PrivilegeKind kind)
        {
            switch (kind)
            {
                case PrivilegeKind.Read: return 1;
                case PrivilegeKind.Write: return 2;
                case PrivilegeKind.Append: return 4;
                case PrivilegeKind.AppendTo: return 16;
                case PrivilegeKind.Create: return 32;
                case PrivilegeKind.Delete: return 65536;
                case PrivilegeKind.Share: return 262144;
                case PrivilegeKind.Assign: return 524288;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        /// <summary>The privilege kind for a privilege record's "accessright" value; false for rights that are not one of the eight.</summary>
        public static bool TryKind(int accessRight, out PrivilegeKind kind)
        {
            foreach (PrivilegeKind candidate in Enum.GetValues(typeof(PrivilegeKind)))
            {
                if (AccessRight(candidate) == accessRight)
                {
                    kind = candidate;
                    return true;
                }
            }

            kind = default(PrivilegeKind);
            return false;
        }

        public static string Label(PrivilegeKind kind)
        {
            return kind == PrivilegeKind.AppendTo ? "Append To" : kind.ToString();
        }

        public static string Label(PrivilegeDepth depth)
        {
            switch (depth)
            {
                case PrivilegeDepth.User: return "User";
                case PrivilegeDepth.BusinessUnit: return "Business Unit";
                case PrivilegeDepth.ParentChildBusinessUnits: return "Parent: Child Business Units";
                case PrivilegeDepth.Organization: return "Organization";
                default: return "None";
            }
        }

        /// <summary>A depth mask can carry several bits; the widest one is the depth that applies.</summary>
        public static PrivilegeDepth DepthFromMask(int mask)
        {
            if ((mask & 8) != 0) { return PrivilegeDepth.Organization; }
            if ((mask & 4) != 0) { return PrivilegeDepth.ParentChildBusinessUnits; }
            if ((mask & 2) != 0) { return PrivilegeDepth.BusinessUnit; }
            if ((mask & 1) != 0) { return PrivilegeDepth.User; }
            return PrivilegeDepth.None;
        }

        /// <summary>"Same level or more": keeps the rows whose depth is at least <paramref name="minimum"/>.</summary>
        public static IEnumerable<RolePrivilegeRow> AtLeast(IEnumerable<RolePrivilegeRow> rows, PrivilegeDepth minimum)
        {
            return rows.Where(r => r.DepthLevel >= (int)minimum);
        }
    }
}
