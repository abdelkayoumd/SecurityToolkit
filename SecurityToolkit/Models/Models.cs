using System;

namespace SecurityToolkit.Models
{
    internal sealed class UserInfo
    {
        public Guid Id { get; set; }
        public string FullName { get; set; }
        public string DomainName { get; set; }
        public Guid? BusinessUnitId { get; set; }
        public string BusinessUnitName { get; set; }
        public bool IsDisabled { get; set; }
        public string Status => IsDisabled ? "Disabled" : "Active";
    }

    internal sealed class RoleInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public Guid? BusinessUnitId { get; set; }
        public string BusinessUnitName { get; set; }
        /// <summary>Every business unit has its own copy of a role; copies share the same root role.</summary>
        public Guid? RootRoleId { get; set; }
        /// <summary>Identifies "the same role" across business units.</summary>
        public Guid RootKey => RootRoleId ?? Id;
    }

    internal sealed class TeamInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public Guid? BusinessUnitId { get; set; }
        public string BusinessUnitName { get; set; }
        /// <summary>0 owner, 1 access, 2 security group, 3 office group.</summary>
        public int TeamType { get; set; }
        public bool IsDefault { get; set; }

        /// <summary>
        /// Only plain owner teams, other than a business unit's default team, have members that can be added and
        /// removed by hand. Access-team members come from record sharing, group-team members from the Entra group,
        /// and the default team always holds every user of its business unit.
        /// </summary>
        public bool IsManuallyManaged => TeamType == 0 && !IsDefault;

        /// <summary>Access teams have no security roles (they carry record-level access instead).</summary>
        public bool CanHoldRoles => TeamType != 1;

        public string TypeName
        {
            get
            {
                switch (TeamType)
                {
                    case 0: return "Owner";
                    case 1: return "Access";
                    case 2: return "Entra security group";
                    case 3: return "Entra Microsoft 365 group";
                    default: return "Type " + TeamType;
                }
            }
        }
    }

    internal sealed class BusinessUnitInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public Guid? ParentId { get; set; }
        public string ParentName { get; set; }
    }

    internal sealed class UserTeamLink
    {
        public Guid UserId { get; set; }
        public Guid TeamId { get; set; }
    }

    internal sealed class UserRoleLink
    {
        public Guid UserId { get; set; }
        public Guid RoleId { get; set; }
    }

    internal sealed class TeamRoleLink
    {
        public Guid TeamId { get; set; }
        public Guid RoleId { get; set; }
    }

    /// <summary>One entry of the role picker: a role as a whole, however many business-unit copies it has.</summary>
    internal sealed class RoleChoice
    {
        public Guid RootKey { get; set; }
        public string Name { get; set; }
        public int Copies { get; set; }
    }

    internal sealed class TableInfo
    {
        public string LogicalName { get; set; }
        public string DisplayName { get; set; }
    }
}
