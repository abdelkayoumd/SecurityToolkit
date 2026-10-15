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
        public ScriptedUi Ui = new ScriptedUi();
        public IChangeUi CreateChangeUi(System.Windows.Forms.IWin32Window owner) { return Ui; }
    }

    internal static class OrgFake
    {
        /// <summary>A fake organization service that serves the records of <paramref name="org"/> the way Dataverse would.</summary>
        public static FakeOrganizationService For(Org org)
        {
            return Serve(org.Graph, org);
        }

        /// <summary>Serves the security records of any graph. Privilege data is only available for the read-side <see cref="Org"/>.</summary>
        public static FakeOrganizationService Serve(SecurityGraph graph, Org org = null)
        {
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
                                ["accessmode"] = new OptionSetValue(u.AccessMode),
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
                                ["ismanaged"] = r.IsManaged,
                                ["businessunitid"] = new EntityReference("businessunit", r.BusinessUnitId ?? Guid.NewGuid()) { Name = r.BusinessUnitName }
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
                            var e = new Entity("team", t.Id) { ["name"] = t.Name, ["teamtype"] = new OptionSetValue(t.TeamType), ["isdefault"] = t.IsDefault };
                            if (t.BusinessUnitId.HasValue)
                            {
                                e["businessunitid"] = new EntityReference("businessunit", t.BusinessUnitId.Value) { Name = t.BusinessUnitName };
                            }

                            result.Entities.Add(e);
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
                    case "privilege" when org != null:
                        // Whatever table and access right is asked for, one privilege exists.
                        result.Entities.Add(new Entity("privilege", Guid.NewGuid()));
                        break;
                    case "roleprivileges" when org != null:
                        // Admin reaches the whole organization, Auditor its business unit, Seller only the user's own records.
                        result.Entities.Add(new Entity("roleprivileges") { ["roleid"] = org.Admin.Id, ["privilegedepthmask"] = 8 });
                        result.Entities.Add(new Entity("roleprivileges") { ["roleid"] = org.Auditor.Id, ["privilegedepthmask"] = 2 });
                        result.Entities.Add(new Entity("roleprivileges") { ["roleid"] = org.SellerRoot.Id, ["privilegedepthmask"] = 1 });
                        break;
                }

                return result;
            };
            fake.OnExecute = request =>
            {
                var response = new Microsoft.Xrm.Sdk.Messages.RetrieveAllEntitiesResponse();
                response.Results["EntityMetadata"] = new Microsoft.Xrm.Sdk.Metadata.EntityMetadata[]
                {
                    new Microsoft.Xrm.Sdk.Metadata.EntityMetadata { LogicalName = "contact" },
                    new Microsoft.Xrm.Sdk.Metadata.EntityMetadata { LogicalName = "account" }
                };
                return response;
            };
            return fake;
        }

        public static TestHost HostFor(Org org)
        {
            return new TestHost { Security = new SecurityService(For(org)) };
        }
    }
}
