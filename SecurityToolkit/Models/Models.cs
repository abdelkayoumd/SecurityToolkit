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
    }

    internal sealed class RoleInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public Guid? BusinessUnitId { get; set; }
        public string BusinessUnitName { get; set; }
        /// <summary>Every business unit has its own copy of a role; copies share the same root role.</summary>
        public Guid? RootRoleId { get; set; }
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
    }

    internal sealed class BusinessUnitInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public Guid? ParentId { get; set; }
        public string ParentName { get; set; }
    }
}
