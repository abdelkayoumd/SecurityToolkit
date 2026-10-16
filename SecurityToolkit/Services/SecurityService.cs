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
        private const int RoleChunk = 100;
        private readonly Dictionary<Guid, IReadOnlyList<RoleGrant>> _roleGrants = new Dictionary<Guid, IReadOnlyList<RoleGrant>>();
        private IReadOnlyDictionary<Guid, PrivilegeTarget> _catalog;
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
            _catalog = null;
            _roleGrants.Clear();
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
            var grants = GetRoleGrants(table, kind);
            if (grants.Count == 0)
            {
                return new List<RolePrivilegeRow>();
            }

            var roles = GetRoles().ToDictionary(r => r.Id);
            var rows = new List<RolePrivilegeRow>();
            foreach (var grant in grants)
            {
                if (!roles.TryGetValue(grant.Key, out var role))
                {
                    continue;
                }

                rows.Add(new RolePrivilegeRow
                {
                    Role = role.Name,
                    BusinessUnit = role.BusinessUnitName,
                    Depth = Privileges.Label(grant.Value),
                    DepthLevel = (int)grant.Value
                });
            }

            return Privileges.AtLeast(rows, minimum)
                .OrderBy(r => r.Role, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.BusinessUnit, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>
        /// Which action on which table each privilege record stands for. Privileges that are not one of the eight
        /// table actions (or not about a table) are left out.
        /// </summary>
        public IReadOnlyDictionary<Guid, PrivilegeTarget> GetPrivilegeCatalog()
        {
            if (_catalog != null)
            {
                return _catalog;
            }

            var query = new QueryExpression("privilege")
            {
                ColumnSet = new ColumnSet("privilegeid", "accessright"),
                LinkEntities =
                {
                    new LinkEntity("privilege", "privilegeobjecttypecodes", "privilegeid", "privilegeid", JoinOperator.Inner)
                    {
                        Columns = new ColumnSet("objecttypecode"),
                        EntityAlias = "otc"
                    }
                }
            };

            var catalog = new Dictionary<Guid, PrivilegeTarget>();
            foreach (var entity in RetrieveAll(query))
            {
                var table = (entity.GetAttributeValue<AliasedValue>("otc.objecttypecode")?.Value) as string;
                if (string.IsNullOrEmpty(table) || !Privileges.TryKind(entity.GetAttributeValue<int>("accessright"), out var kind))
                {
                    continue;
                }

                catalog[entity.Id] = new PrivilegeTarget { Table = table, Kind = kind };
            }

            return _catalog = catalog;
        }

        /// <summary>
        /// Every privilege each of the given role records holds, and how far it reaches. Roles already fetched are
        /// served from memory, so moving between users who share roles costs nothing.
        /// </summary>
        public IReadOnlyDictionary<Guid, IReadOnlyList<RoleGrant>> GetRolePrivileges(IEnumerable<Guid> roleIds)
        {
            var wanted = roleIds.Distinct().ToList();
            var missing = wanted.Where(id => !_roleGrants.ContainsKey(id)).ToList();

            for (var offset = 0; offset < missing.Count; offset += RoleChunk)
            {
                var chunk = missing.Skip(offset).Take(RoleChunk).ToList();
                var query = new QueryExpression("roleprivileges")
                {
                    ColumnSet = new ColumnSet("roleid", "privilegeid", "privilegedepthmask"),
                    Criteria = { Conditions = { new ConditionExpression("roleid", ConditionOperator.In, chunk.Cast<object>().ToArray()) } }
                };

                var found = new Dictionary<Guid, List<RoleGrant>>();
                foreach (var row in RetrieveAll(query))
                {
                    var roleId = row.GetAttributeValue<Guid>("roleid");
                    if (!found.TryGetValue(roleId, out var list))
                    {
                        found[roleId] = list = new List<RoleGrant>();
                    }

                    list.Add(new RoleGrant
                    {
                        PrivilegeId = row.GetAttributeValue<Guid>("privilegeid"),
                        Depth = Privileges.DepthFromMask(row.GetAttributeValue<int>("privilegedepthmask"))
                    });
                }

                // A role with no privileges at all is remembered too, so it is not asked for again.
                foreach (var id in chunk)
                {
                    _roleGrants[id] = found.TryGetValue(id, out var grants) ? (IReadOnlyList<RoleGrant>)grants : new RoleGrant[0];
                }
            }

            return wanted.ToDictionary(id => id, id => _roleGrants[id]);
        }

        /// <summary>
        /// For each role record that grants <paramref name="kind"/> on <paramref name="table"/>, how far it reaches.
        /// A role record is one business unit's copy of a role, which is what a user or team actually holds.
        /// </summary>
        public IReadOnlyDictionary<Guid, PrivilegeDepth> GetRoleGrants(string table, PrivilegeKind kind)
        {
            var grants = new Dictionary<Guid, PrivilegeDepth>();
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
                return grants;
            }

            var grantQuery = new QueryExpression("roleprivileges")
            {
                ColumnSet = new ColumnSet("roleid", "privilegedepthmask"),
                Criteria = { Conditions = { new ConditionExpression("privilegeid", ConditionOperator.In, privilegeIds) } }
            };

            foreach (var grant in RetrieveAll(grantQuery))
            {
                var roleId = grant.GetAttributeValue<Guid>("roleid");
                var depth = Privileges.DepthFromMask(grant.GetAttributeValue<int>("privilegedepthmask"));

                // A role can reach the table through more than one privilege record; the widest one counts.
                if (!grants.TryGetValue(roleId, out var existing) || depth > existing)
                {
                    grants[roleId] = depth;
                }
            }

            return grants;
        }

        public IReadOnlyList<UserInfo> GetUsers()
        {
            return _users ?? (_users = Fetch(new QueryExpression("systemuser")
            {
                ColumnSet = new ColumnSet("fullname", "domainname", "businessunitid", "isdisabled", "accessmode"),
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
                IsDisabled = e.GetAttributeValue<bool>("isdisabled"),
                AccessMode = e.GetAttributeValue<OptionSetValue>("accessmode")?.Value ?? 0
            }));
        }

        public IReadOnlyList<RoleInfo> GetRoles()
        {
            return _roles ?? (_roles = Fetch(new QueryExpression("role")
            {
                ColumnSet = new ColumnSet("name", "businessunitid", "parentrootroleid", "ismanaged"),
                Orders = { new OrderExpression("name", OrderType.Ascending) }
            }, e => new RoleInfo
            {
                Id = e.Id,
                Name = e.GetAttributeValue<string>("name"),
                BusinessUnitId = e.GetAttributeValue<EntityReference>("businessunitid")?.Id,
                BusinessUnitName = e.GetAttributeValue<EntityReference>("businessunitid")?.Name,
                RootRoleId = e.GetAttributeValue<EntityReference>("parentrootroleid")?.Id,
                IsManaged = e.GetAttributeValue<bool>("ismanaged")
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
