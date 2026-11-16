using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SecurityToolkit.Features;
using SecurityToolkit.Models;
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
        public void ShowMessage(BannerKind kind, string message) { Messages.Add(message); }
        public ScriptedUi Ui = new ScriptedUi();
        public IChangeUi CreateChangeUi(System.Windows.Forms.IWin32Window owner) { return Ui; }
        public List<string> Navigated = new List<string>();
        public void Navigate(string featureId) { Navigated.Add(featureId); }
        public List<KeyValuePair<string, Subject>> Opened = new List<KeyValuePair<string, Subject>>();
        public List<string> Messages = new List<string>();
        public void Open(string featureId, Subject subject) { Opened.Add(new KeyValuePair<string, Subject>(featureId, subject)); }
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
                                ["applicationid"] = u.ApplicationId,
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
                    case "fieldsecurityprofile" when org != null:
                        result.Entities.Add(new Entity("fieldsecurityprofile", SalaryReaders) { ["name"] = "Salary readers" });
                        result.Entities.Add(new Entity("fieldsecurityprofile", CreditManagers) { ["name"] = "Credit managers" });
                        result.Entities.Add(new Entity("fieldsecurityprofile", UnusedProfile) { ["name"] = "Unused profile" });
                        break;
                    case "fieldpermission" when org != null:
                        Entity Permission(Guid profile, string table, string column, int read, int update, int create) => new Entity("fieldpermission", Guid.NewGuid())
                        {
                            ["fieldsecurityprofileid"] = new EntityReference("fieldsecurityprofile", profile),
                            ["entityname"] = table,
                            ["attributelogicalname"] = column,
                            ["canread"] = new OptionSetValue(read),
                            ["canupdate"] = new OptionSetValue(update),
                            ["cancreate"] = new OptionSetValue(create)
                        };
                        result.Entities.Add(Permission(SalaryReaders, "contact", "salary", 4, 0, 0));
                        result.Entities.Add(Permission(CreditManagers, "account", "creditlimit", 4, 4, 0));
                        result.Entities.Add(Permission(CreditManagers, "contact", "salary", 0, 4, 0));
                        break;
                    case "systemuserprofiles" when org != null:
                        result.Entities.Add(new Entity("systemuserprofiles") { ["systemuserid"] = org.Ada.Id, ["fieldsecurityprofileid"] = SalaryReaders });
                        result.Entities.Add(new Entity("systemuserprofiles") { ["systemuserid"] = org.Ada.Id, ["fieldsecurityprofileid"] = CreditManagers });
                        break;
                    case "teamprofiles" when org != null:
                        result.Entities.Add(new Entity("teamprofiles") { ["teamid"] = org.Reviewers.Id, ["fieldsecurityprofileid"] = SalaryReaders });
                        break;
                    case "privilege" when org != null && ((QueryExpression)q).Criteria.Conditions.Any(c => c.AttributeName == "name" && c.Operator == ConditionOperator.NotNull):
                        // Privileges with their names: two that are about no table, and one table privilege the outer join finds a table for.
                        foreach (var name in OtherPrivilegeNames)
                        {
                            result.Entities.Add(new Entity("privilege", OtherId(name)) { ["name"] = name });
                        }

                        result.Entities.Add(new Entity("privilege", CatalogId("account", PrivilegeKind.Read))
                        {
                            ["name"] = "prvReadAccount",
                            ["otc.objecttypecode"] = new AliasedValue("privilegeobjecttypecodes", "objecttypecode", "account")
                        });
                        break;
                    case "roleprivileges" when org != null && ((QueryExpression)q).Criteria.Conditions.Any(c => c.AttributeName == "privilegeid" && c.Values.Contains(OtherId("prvExportToExcel"))):
                        // Admin may export across the organization and bulk delete; Auditor may export within its business unit.
                        var others = ((QueryExpression)q).Criteria.Conditions.First(c => c.AttributeName == "privilegeid").Values.Cast<Guid>().ToList();
                        void Grant(RoleInfo role, string name, int mask)
                        {
                            if (others.Contains(OtherId(name)))
                            {
                                result.Entities.Add(new Entity("roleprivileges") { ["roleid"] = role.Id, ["privilegeid"] = OtherId(name), ["privilegedepthmask"] = mask });
                            }
                        }

                        Grant(org.Admin, "prvExportToExcel", 8);
                        Grant(org.Admin, "prvBulkDelete", 8);
                        Grant(org.Auditor, "prvExportToExcel", 2);
                        break;
                    case "privilege" when org != null && ((QueryExpression)q).Criteria.Conditions.Count == 0:
                        // The catalogue: all eight actions on account and contact.
                        foreach (var table in new[] { "account", "contact" })
                        {
                            foreach (PrivilegeKind kind in Enum.GetValues(typeof(PrivilegeKind)))
                            {
                                result.Entities.Add(new Entity("privilege", CatalogId(table, kind))
                                {
                                    ["accessright"] = Privileges.AccessRight(kind),
                                    ["otc.objecttypecode"] = new AliasedValue("privilegeobjecttypecodes", "objecttypecode", table)
                                });
                            }
                        }

                        break;
                    case "privilege" when org != null:
                        // The privilege for the action and table asked for (the same one the grants below refer to).
                        var rightAsked = ((QueryExpression)q).Criteria.Conditions.FirstOrDefault(c => c.AttributeName == "accessright");
                        var tableAsked = ((QueryExpression)q).LinkEntities.SelectMany(l => l.LinkCriteria.Conditions).FirstOrDefault(c => c.AttributeName == "objecttypecode");
                        if (rightAsked != null && tableAsked != null)
                        {
                            var wanted = Privileges.TryKind((int)rightAsked.Values[0], out var kindAsked) ? kindAsked : PrivilegeKind.Read;
                            result.Entities.Add(new Entity("privilege", CatalogId((string)tableAsked.Values[0], wanted)));
                        }
                        else
                        {
                            result.Entities.Add(new Entity("privilege", Guid.NewGuid()));
                        }

                        break;
                    case "roleprivileges" when org != null && ((QueryExpression)q).Criteria.Conditions.Any(c => c.AttributeName == "roleid"):
                        // What the asked-for role records grant: every catalogue privilege, as far as the role reaches.
                        var asked = ((QueryExpression)q).Criteria.Conditions.First(c => c.AttributeName == "roleid").Values.Cast<Guid>().ToList();
                        var reach = new Dictionary<Guid, int> { [org.Admin.Id] = 8, [org.Auditor.Id] = 2, [org.SellerRoot.Id] = 1 };
                        foreach (var roleId in asked.Where(reach.ContainsKey))
                        {
                            foreach (var table in new[] { "account", "contact" })
                            {
                                foreach (PrivilegeKind kind in Enum.GetValues(typeof(PrivilegeKind)))
                                {
                                    result.Entities.Add(new Entity("roleprivileges") { ["roleid"] = roleId, ["privilegeid"] = CatalogId(table, kind), ["privilegedepthmask"] = reach[roleId] });
                                }
                            }
                        }

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

        /// <summary>The column security profiles of the sample organization.</summary>
        internal static readonly Guid SalaryReaders = Guid.NewGuid(), CreditManagers = Guid.NewGuid(), UnusedProfile = Guid.NewGuid();

        /// <summary>The privileges of the sample organization that are not about a table.</summary>
        internal static readonly string[] OtherPrivilegeNames = { "prvExportToExcel", "prvBulkDelete" };

        /// <summary>A stable id for a privilege that is not about a table.</summary>
        internal static Guid OtherId(string name) => CatalogId("other:" + name, PrivilegeKind.Share);

        /// <summary>A stable id for the privilege "kind on table", so grants can refer to it.</summary>
        internal static Guid CatalogId(string table, PrivilegeKind kind)
        {
            var bytes = new byte[16];
            var seed = System.Text.Encoding.UTF8.GetBytes(table + kind);
            for (var i = 0; i < seed.Length; i++)
            {
                bytes[i % 16] ^= seed[i];
            }

            return new Guid(bytes);
        }

        public static TestHost HostFor(Org org)
        {
            return new TestHost { Security = new SecurityService(For(org)) };
        }
    }
}
