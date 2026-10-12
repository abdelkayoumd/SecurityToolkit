using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
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
