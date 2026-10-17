using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SecurityToolkit.Features;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using SecurityToolkit.UI;
using Xunit;

namespace SecurityToolkit.Tests
{
    public class RoleComparerTests
    {
        private readonly Org _o = new Org();
        private readonly Guid _readAccount = Guid.NewGuid();
        private readonly Guid _deleteAccount = Guid.NewGuid();
        private readonly Guid _writeAccount = Guid.NewGuid();
        private readonly Guid _readContact = Guid.NewGuid();

        private IReadOnlyDictionary<Guid, PrivilegeTarget> Catalog => new Dictionary<Guid, PrivilegeTarget>
        {
            [_readAccount] = new PrivilegeTarget { Table = "account", Kind = PrivilegeKind.Read },
            [_deleteAccount] = new PrivilegeTarget { Table = "account", Kind = PrivilegeKind.Delete },
            [_writeAccount] = new PrivilegeTarget { Table = "account", Kind = PrivilegeKind.Write },
            [_readContact] = new PrivilegeTarget { Table = "contact", Kind = PrivilegeKind.Read }
        };

        private static readonly TableInfo[] Tables =
        {
            new TableInfo { LogicalName = "account", DisplayName = "Account" },
            new TableInfo { LogicalName = "contact", DisplayName = "Contact" }
        };

        private static RoleGrant Grant(Guid privilege, PrivilegeDepth depth) => new RoleGrant { PrivilegeId = privilege, Depth = depth };

        private RoleRow A => RoleComparer.RolesToPick(_o.Graph).Single(r => r.Name == "Admin");
        private RoleRow B => RoleComparer.RolesToPick(_o.Graph).Single(r => r.Name == "Auditor");

        private IReadOnlyList<RoleDiffRow> Compare(RoleGrant[] a, RoleGrant[] b) => RoleComparer.Compare(A, B, Tables, Catalog, a, b);

        [Fact]
        public void ARoleWithBusinessUnitCopies_IsListedOnce()
        {
            var roles = RoleComparer.RolesToPick(_o.Graph);

            Assert.Equal(new[] { "Admin", "Auditor", "Seller" }, roles.Select(r => r.Name));
        }

        [Fact]
        public void TheRoleOfARoleWithCopies_IsTheRootCopy()
        {
            var seller = RoleComparer.RolesToPick(_o.Graph).Single(r => r.Name == "Seller");

            Assert.Equal(_o.SellerRoot.Id, seller.RoleId);
            Assert.Equal(_o.SellerRoot.RootKey, seller.RootKey);
        }

        [Fact]
        public void APrivilegeOnlyOneRoleHolds_IsReportedAsOnlyThatRole()
        {
            var rows = Compare(new[] { Grant(_deleteAccount, PrivilegeDepth.Organization) }, new RoleGrant[0]);

            var row = Assert.Single(rows);
            Assert.Equal("Account", row.Table);
            Assert.Equal("Delete", row.Privilege);
            Assert.Equal("Organization", row.A);
            Assert.Equal("None", row.B);
            Assert.Equal("Only Admin", row.Result);
            Assert.True(row.IsDifference);
        }

        [Fact]
        public void ADifferentReach_NamesTheRoleThatReachesFurther()
        {
            var rows = Compare(new[] { Grant(_readAccount, PrivilegeDepth.BusinessUnit) }, new[] { Grant(_readAccount, PrivilegeDepth.Organization) });

            var row = Assert.Single(rows);
            Assert.Equal("Auditor reaches further", row.Result);
            Assert.True(row.IsDifference);
        }

        [Fact]
        public void TheSameReach_IsListedAsSame_AndIsNotADifference()
        {
            var row = Assert.Single(Compare(new[] { Grant(_readAccount, PrivilegeDepth.User) }, new[] { Grant(_readAccount, PrivilegeDepth.User) }));

            Assert.Equal("Same", row.Result);
            Assert.False(row.IsDifference);
        }

        [Fact]
        public void APrivilegeNeitherRoleGrants_IsNotListed()
        {
            var rows = Compare(new[] { Grant(_readAccount, PrivilegeDepth.None) }, new[] { Grant(_writeAccount, PrivilegeDepth.None) });

            Assert.Empty(rows);
        }

        [Fact]
        public void AGrantOfAnUnknownPrivilege_IsIgnored()
        {
            Assert.Empty(Compare(new[] { Grant(Guid.NewGuid(), PrivilegeDepth.Organization) }, new RoleGrant[0]));
        }

        [Fact]
        public void TheWidestGrantWins_WhenAPrivilegeAppearsTwice()
        {
            var rows = Compare(new[] { Grant(_readAccount, PrivilegeDepth.User), Grant(_readAccount, PrivilegeDepth.Organization) },
                new[] { Grant(_readAccount, PrivilegeDepth.Organization) });

            Assert.Equal("Same", Assert.Single(rows).Result);
        }

        [Fact]
        public void Rows_AreByTable_ThenInTheOrderOfTheEightPrivileges()
        {
            var rows = Compare(
                new[] { Grant(_deleteAccount, PrivilegeDepth.User), Grant(_readContact, PrivilegeDepth.User), Grant(_writeAccount, PrivilegeDepth.User), Grant(_readAccount, PrivilegeDepth.User) },
                new RoleGrant[0]);

            Assert.Equal(new[] { "Account Read", "Account Write", "Account Delete", "Contact Read" }, rows.Select(r => r.Table + " " + r.Privilege));
        }

        [Fact]
        public void ATableWithoutADisplayName_IsShownByItsLogicalName()
        {
            var rows = RoleComparer.Compare(A, B, new TableInfo[0], Catalog, new[] { Grant(_readContact, PrivilegeDepth.User) }, new RoleGrant[0]);

            Assert.Equal("contact", Assert.Single(rows).Table);
        }

        [Fact]
        public void TwoRolesWithTheSameName_AreToldApart()
        {
            var labels = RoleComparer.Labels(new RoleRow { Name = "Sales" }, new RoleRow { Name = "sales" });

            Assert.Equal("Sales (1)", labels.Item1);
            Assert.Equal("sales (2)", labels.Item2);
        }

        [Fact]
        public void TheSummary_CountsEachKindOfDifference()
        {
            var rows = Compare(
                new[] { Grant(_deleteAccount, PrivilegeDepth.Organization), Grant(_readAccount, PrivilegeDepth.User), Grant(_writeAccount, PrivilegeDepth.User) },
                new[] { Grant(_readAccount, PrivilegeDepth.Organization), Grant(_writeAccount, PrivilegeDepth.User), Grant(_readContact, PrivilegeDepth.User) });

            Assert.Equal("Admin and Auditor: 1 privilege(s) only Admin, 1 only Auditor, 1 with a different reach, 1 the same.", RoleComparer.Summarize(rows, A, B));
        }
    }

    public class CompareRolesViewTests
    {
        private static void Run(Action<FeatureView, FilterableGrid, FilterableGrid, Org, TestHost> test)
        {
            Sta.Run(() =>
            {
                var org = new Org();
                var fake = OrgFake.Serve(org.Graph);
                var served = fake.OnRetrieveMultiple;
                var read = Guid.NewGuid();
                var delete = Guid.NewGuid();
                Entity Privilege(Guid id, int right, string table) => new Entity("privilege", id)
                {
                    ["accessright"] = right,
                    ["otc.objecttypecode"] = new AliasedValue("privilegeobjecttypecodes", "objecttypecode", table)
                };

                fake.OnRetrieveMultiple = q =>
                {
                    switch (((QueryExpression)q).EntityName)
                    {
                        case "privilege":
                            return new EntityCollection(new List<Entity> { Privilege(read, 1, "account"), Privilege(delete, 65536, "account") });
                        case "roleprivileges":
                            // Admin reads and deletes across the organization; Auditor only reads, in its business unit.
                            return new EntityCollection(new List<Entity>
                            {
                                new Entity("roleprivileges") { ["roleid"] = org.Admin.Id, ["privilegeid"] = read, ["privilegedepthmask"] = 8 },
                                new Entity("roleprivileges") { ["roleid"] = org.Admin.Id, ["privilegeid"] = delete, ["privilegedepthmask"] = 8 },
                                new Entity("roleprivileges") { ["roleid"] = org.Auditor.Id, ["privilegeid"] = read, ["privilegedepthmask"] = 2 }
                            });
                        default:
                            return served(q);
                    }
                };

                var host = new TestHost { Security = new SecurityService(fake) };
                var view = new CompareRolesFeature().CreateView(host);
                var form = new Form { Size = new Size(1200, 500) };
                form.Controls.Add(view);
                form.Show();
                Application.DoEvents();
                _ = view.Commands;
                view.OnActivated();

                var grids = new List<FilterableGrid>();
                Collect(view, grids);
                var ordered = grids.OrderBy(g => g.PointToScreen(Point.Empty).X).ToArray();
                test(view, ordered[0], ordered[1], org, host);
                form.Dispose();
            });
        }

        private static void Collect(Control root, List<FilterableGrid> found)
        {
            foreach (Control child in root.Controls)
            {
                if (child is FilterableGrid grid) { found.Add(grid); } else { Collect(child, found); }
            }
        }

        private static string Info(FeatureView view, string containing)
        {
            return ((System.Windows.Forms.Label)Sta.Find(view, c => c is System.Windows.Forms.Label l && l.Text.Contains(containing)))?.Text;
        }

        [Fact]
        public void TheLeftListHasEachRoleOnce()
        {
            Run((view, roles, results, org, host) =>
            {
                Assert.Equal(new[] { "Admin", "Auditor", "Seller" }, roles.GetVisible<RoleRow>().Select(r => r.Name));
            });
        }

        [Fact]
        public void SelectingOneRole_AsksForASecond()
        {
            Run((view, roles, results, org, host) =>
            {
                MasterDetailHarness.Select<RoleRow>(roles, r => r.Name == "Admin");

                TeamMembersViewTests.Pump(() => Info(view, "Ctrl-click a second role") != null);

                Assert.NotNull(Info(view, "Ctrl-click a second role"));
            });
        }

        [Fact]
        public void SelectingTwoRoles_ShowsWhereTheyDiffer()
        {
            Run((view, roles, results, org, host) =>
            {
                MasterDetailHarness.Select<RoleRow>(roles, r => r.Name == "Admin" || r.Name == "Auditor");
                TeamMembersViewTests.Pump(() => results.GetVisible<RoleDiffRow>().Any());

                var rows = results.GetVisible<RoleDiffRow>().ToList();
                Assert.Equal(new[] { "Read", "Delete" }, rows.Select(r => r.Privilege));
                Assert.Equal("Only Admin", rows.Single(r => r.Privilege == "Delete").Result);
                Assert.Equal("Admin reaches further", rows.Single(r => r.Privilege == "Read").Result);
                Assert.NotNull(Info(view, "1 privilege(s) only Admin"));
            });
        }

        [Fact]
        public void TheExportHeadsTheRoleColumnsWithTheRoleNames()
        {
            Run((view, roles, results, org, host) =>
            {
                MasterDetailHarness.Select<RoleRow>(roles, r => r.Name == "Admin" || r.Name == "Auditor");
                TeamMembersViewTests.Pump(() => results.GetVisible<RoleDiffRow>().Any());

                var table = results.GetTable();

                Assert.Equal(new[] { "Table", "Privilege", "Admin", "Auditor", "Result" }, table.Key);
            });
        }

        [Fact]
        public void ItIsListedUnderItsGroup_AndRegistered()
        {
            Assert.Equal(FeatureGroup.Compare, new CompareRolesFeature().Group);
            Assert.Contains(FeatureRegistry.CreateAll(), f => f is CompareRolesFeature);
        }
    }
}
