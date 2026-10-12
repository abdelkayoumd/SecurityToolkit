using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using SecurityToolkit.Models;

namespace SecurityToolkit.Services
{
    /// <summary>
    /// Read access to the security entities (users, roles, teams, business units). Each list is fetched once,
    /// fully paged, then served from memory until <see cref="Invalidate"/> is called.
    /// </summary>
    internal sealed class SecurityService
    {
        private const int PageSize = 5000;

        private readonly IOrganizationService _service;
        private List<UserInfo> _users;
        private List<RoleInfo> _roles;
        private List<TeamInfo> _teams;
        private List<BusinessUnitInfo> _businessUnits;
        private SecurityGraph _graph;
        private IReadOnlyList<TableInfo> _tables;

        public SecurityService(IOrganizationService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public IOrganizationService Organization => _service;

        public void Invalidate()
        {
            _users = null;
            _roles = null;
            _teams = null;
            _businessUnits = null;
            _graph = null;
            _tables = null;
        }

        /// <summary>Everything the read-only features need, loaded once (paged) and cached until <see cref="Invalidate"/>.</summary>
        public SecurityGraph GetGraph()
        {
            return _graph ?? (_graph = new SecurityGraph(
                GetUsers(), GetRoles(), GetTeams(), GetBusinessUnits(),
                Fetch(new QueryExpression("teammembership") { ColumnSet = new ColumnSet("systemuserid", "teamid") },
                    e => new UserTeamLink { UserId = e.GetAttributeValue<Guid>("systemuserid"), TeamId = e.GetAttributeValue<Guid>("teamid") }),
                Fetch(new QueryExpression("systemuserroles") { ColumnSet = new ColumnSet("systemuserid", "roleid") },
                    e => new UserRoleLink { UserId = e.GetAttributeValue<Guid>("systemuserid"), RoleId = e.GetAttributeValue<Guid>("roleid") }),
                Fetch(new QueryExpression("teamroles") { ColumnSet = new ColumnSet("teamid", "roleid") },
                    e => new TeamRoleLink { TeamId = e.GetAttributeValue<Guid>("teamid"), RoleId = e.GetAttributeValue<Guid>("roleid") })));
        }

        /// <summary>The tables a privilege can be about (no relationship tables), by display name.</summary>
        public IReadOnlyList<TableInfo> GetTables()
        {
            if (_tables != null)
            {
                return _tables;
            }

            var response = (RetrieveAllEntitiesResponse)_service.Execute(
                new RetrieveAllEntitiesRequest { EntityFilters = EntityFilters.Entity, RetrieveAsIfPublished = false });
            return _tables = response.EntityMetadata
                .Where(m => m.IsIntersect != true && m.IsPrivate != true)
                .Select(m => new TableInfo
                {
                    LogicalName = m.LogicalName,
                    DisplayName = m.DisplayName?.UserLocalizedLabel?.Label ?? m.LogicalName
                })
                .OrderBy(t => t.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Every role (in every business unit) that grants <paramref name="kind"/> on <paramref name="table"/> at
        /// <paramref name="minimum"/> depth or wider.
        /// </summary>
        public IReadOnlyList<RolePrivilegeRow> FindRolesWithPrivilege(string table, PrivilegeKind kind, PrivilegeDepth minimum)
        {
            var privilegeQuery = new QueryExpression("privilege")
            {
                ColumnSet = new ColumnSet("privilegeid"),
                Criteria = { Conditions = { new ConditionExpression("accessright", ConditionOperator.Equal, Privileges.AccessRight(kind)) } },
                LinkEntities =
                {
                    new LinkEntity("privilege", "privilegeobjecttypecodes", "privilegeid", "privilegeid", JoinOperator.Inner)
                    {
                        LinkCriteria = { Conditions = { new ConditionExpression("objecttypecode", ConditionOperator.Equal, table) } }
                    }
                }
            };
            var privilegeIds = RetrieveAll(privilegeQuery).Select(e => e.Id).Distinct().Cast<object>().ToArray();
            if (privilegeIds.Length == 0)
            {
                return new List<RolePrivilegeRow>();
            }

            var grantQuery = new QueryExpression("roleprivileges")
            {
                ColumnSet = new ColumnSet("roleid", "privilegedepthmask"),
                Criteria = { Conditions = { new ConditionExpression("privilegeid", ConditionOperator.In, privilegeIds) } }
            };

            var roles = GetRoles().ToDictionary(r => r.Id);
            var rows = new List<RolePrivilegeRow>();
            foreach (var grant in RetrieveAll(grantQuery))
            {
                if (!roles.TryGetValue(grant.GetAttributeValue<Guid>("roleid"), out var role))
                {
                    continue;
                }

                var depth = Privileges.DepthFromMask(grant.GetAttributeValue<int>("privilegedepthmask"));
                rows.Add(new RolePrivilegeRow
                {
                    Role = role.Name,
                    BusinessUnit = role.BusinessUnitName,
                    Depth = Privileges.Label(depth),
                    DepthLevel = (int)depth
                });
            }

            return Privileges.AtLeast(rows, minimum)
                .OrderBy(r => r.Role, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.BusinessUnit, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public IReadOnlyList<UserInfo> GetUsers()
        {
            return _users ?? (_users = Fetch(new QueryExpression("systemuser")
            {
                ColumnSet = new ColumnSet("fullname", "domainname", "businessunitid", "isdisabled"),
                // Excludes the "# ..." system placeholders, as the former teams_listing tool did.
                Criteria = { Conditions = { new ConditionExpression("fullname", ConditionOperator.DoesNotBeginWith, "#") } },
                Orders = { new OrderExpression("fullname", OrderType.Ascending) }
            }, e => new UserInfo
            {
                Id = e.Id,
                FullName = e.GetAttributeValue<string>("fullname"),
                DomainName = e.GetAttributeValue<string>("domainname"),
                BusinessUnitId = e.GetAttributeValue<EntityReference>("businessunitid")?.Id,
                BusinessUnitName = e.GetAttributeValue<EntityReference>("businessunitid")?.Name,
                IsDisabled = e.GetAttributeValue<bool>("isdisabled")
            }));
        }

        public IReadOnlyList<RoleInfo> GetRoles()
        {
            return _roles ?? (_roles = Fetch(new QueryExpression("role")
            {
                ColumnSet = new ColumnSet("name", "businessunitid", "parentrootroleid"),
                Orders = { new OrderExpression("name", OrderType.Ascending) }
            }, e => new RoleInfo
            {
                Id = e.Id,
                Name = e.GetAttributeValue<string>("name"),
                BusinessUnitId = e.GetAttributeValue<EntityReference>("businessunitid")?.Id,
                BusinessUnitName = e.GetAttributeValue<EntityReference>("businessunitid")?.Name,
                RootRoleId = e.GetAttributeValue<EntityReference>("parentrootroleid")?.Id
            }));
        }

        public IReadOnlyList<TeamInfo> GetTeams()
        {
            return _teams ?? (_teams = Fetch(new QueryExpression("team")
            {
                ColumnSet = new ColumnSet("name", "businessunitid", "teamtype", "isdefault"),
                Orders = { new OrderExpression("name", OrderType.Ascending) }
            }, e => new TeamInfo
            {
                Id = e.Id,
                Name = e.GetAttributeValue<string>("name"),
                BusinessUnitId = e.GetAttributeValue<EntityReference>("businessunitid")?.Id,
                BusinessUnitName = e.GetAttributeValue<EntityReference>("businessunitid")?.Name,
                TeamType = e.GetAttributeValue<OptionSetValue>("teamtype")?.Value ?? 0,
                IsDefault = e.GetAttributeValue<bool>("isdefault")
            }));
        }

        public IReadOnlyList<BusinessUnitInfo> GetBusinessUnits()
        {
            return _businessUnits ?? (_businessUnits = Fetch(new QueryExpression("businessunit")
            {
                ColumnSet = new ColumnSet("name", "parentbusinessunitid"),
                Criteria = { Conditions = { new ConditionExpression("isdisabled", ConditionOperator.Equal, false) } },
                Orders = { new OrderExpression("name", OrderType.Ascending) }
            }, e => new BusinessUnitInfo
            {
                Id = e.Id,
                Name = e.GetAttributeValue<string>("name"),
                ParentId = e.GetAttributeValue<EntityReference>("parentbusinessunitid")?.Id,
                ParentName = e.GetAttributeValue<EntityReference>("parentbusinessunitid")?.Name
            }));
        }

        /// <summary>Runs a query to the end, following the paging cookie.</summary>
        public IEnumerable<Entity> RetrieveAll(QueryExpression query)
        {
            query.PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1 };
            while (true)
            {
                var page = _service.RetrieveMultiple(query);
                foreach (var entity in page.Entities)
                {
                    yield return entity;
                }

                if (!page.MoreRecords)
                {
                    yield break;
                }

                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }
        }

        private List<T> Fetch<T>(QueryExpression query, Func<Entity, T> map)
        {
            return RetrieveAll(query).Select(map).ToList();
        }
    }
}
