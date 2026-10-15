using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SecurityToolkit.Features;
using SecurityToolkit.Services;
using SecurityToolkit.UI;

namespace SecurityToolkit.Tests
{
    /// <summary>A stand-in for the XrmToolBox shell: runs "background" work immediately and always counts as connected.</summary>
    internal sealed class TestHost : IFeatureHost
    {
        public SecurityService Security { get; set; }
        public void RunAsync<T>(string message, Func<SecurityService, T> work, Action<T> done) { done(work(Security)); }
        public void EnsureConnection(Action action) { action(); }
        public void ShowMessage(BannerKind kind, string message) { }
    }

    internal static class OrgFake
    {
        /// <summary>A fake organization service that serves the records of <paramref name="org"/> the way Dataverse would.</summary>
        public static FakeOrganizationService For(Org org)
        {
            var graph = org.Graph;
            var fake = new FakeOrganizationService();
            fake.OnRetrieveMultiple = q =>
            {
                var result = new EntityCollection();
                switch (((QueryExpression)q).EntityName)
                {
                    case "systemuser":
                        foreach (var u in graph.Users)
                        {
                            result.Entities.Add(new Entity("systemuser", u.Id)
                            {
                                ["fullname"] = u.FullName,
                                ["domainname"] = u.DomainName,
                                ["isdisabled"] = u.IsDisabled,
                                ["businessunitid"] = new EntityReference("businessunit", u.BusinessUnitId.Value) { Name = u.BusinessUnitName }
                            });
                        }

                        break;
                    case "role":
                        foreach (var r in graph.Roles)
                        {
                            var e = new Entity("role", r.Id)
                            {
                                ["name"] = r.Name,
                                ["businessunitid"] = new EntityReference("businessunit", Guid.NewGuid()) { Name = r.BusinessUnitName }
                            };
                            if (r.RootRoleId.HasValue)
                            {
                                e["parentrootroleid"] = new EntityReference("role", r.RootRoleId.Value);
                            }

                            result.Entities.Add(e);
                        }

                        break;
                    case "team":
                        foreach (var t in graph.Teams)
                        {
                            result.Entities.Add(new Entity("team", t.Id) { ["name"] = t.Name, ["teamtype"] = new OptionSetValue(t.TeamType) });
                        }

                        break;
                    case "businessunit":
                        foreach (var b in graph.BusinessUnits)
                        {
                            var e = new Entity("businessunit", b.Id) { ["name"] = b.Name };
                            if (b.ParentId.HasValue)
                            {
                                e["parentbusinessunitid"] = new EntityReference("businessunit", b.ParentId.Value);
                            }

                            result.Entities.Add(e);
                        }

                        break;
                    case "teammembership":
                        foreach (var u in graph.Users)
                        {
                            foreach (var t in graph.TeamsOf(u.Id))
                            {
                                result.Entities.Add(new Entity("teammembership") { ["systemuserid"] = u.Id, ["teamid"] = t.Id });
                            }
                        }

                        break;
                    case "systemuserroles":
                        foreach (var u in graph.Users)
                        {
                            foreach (var r in graph.DirectRolesOf(u.Id))
                            {
                                result.Entities.Add(new Entity("systemuserroles") { ["systemuserid"] = u.Id, ["roleid"] = r.Id });
                            }
                        }

                        break;
                    case "teamroles":
                        foreach (var t in graph.Teams)
                        {
                            foreach (var r in graph.RolesOfTeam(t.Id))
                            {
                                result.Entities.Add(new Entity("teamroles") { ["teamid"] = t.Id, ["roleid"] = r.Id });
                            }
                        }

                        break;
                }

                return result;
            };
            return fake;
        }

        public static TestHost HostFor(Org org)
        {
            return new TestHost { Security = new SecurityService(For(org)) };
        }
    }
}
