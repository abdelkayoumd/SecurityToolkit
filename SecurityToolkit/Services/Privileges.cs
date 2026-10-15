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

    internal sealed class RolePrivilegeRow
    {
        public string Table { get; set; }
        public string Role { get; set; }
        public string BusinessUnit { get; set; }
        public string Depth { get; set; }
        public int DepthLevel { get; set; }
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
