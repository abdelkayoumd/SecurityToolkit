using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using SecurityToolkit.Features;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using SecurityToolkit.UI;
using Xunit;

namespace SecurityToolkit.Tests
{
    public class UserAccessMatrixViewTests
    {
        /// <summary>
        /// Privileges: account Read/Delete, contact Read. Admin reaches the organization on all three, the Sales copy of
        /// Seller only the user's own accounts, and Auditor (held by Bob through a team) its business unit.
        /// </summary>
        private static TestHost HostFor(Org org)
        {
            var accountRead = Guid.NewGuid(); var accountDelete = Guid.NewGuid(); var contactRead = Guid.NewGuid();
            var service = OrgFake.Serve(org.Graph);
            var graph = service.OnRetrieveMultiple;

            Entity Privilege(Guid id, int right, string table) => new Entity("privilege", id)
            {
                ["accessright"] = right,
                ["otc.objecttypecode"] = new AliasedValue("privilegeobjecttypecodes", "objecttypecode", table)
            };

            Entity Grant(Guid role, Guid privilege, int mask) => new Entity("roleprivileges") { ["roleid"] = role, ["privilegeid"] = privilege, ["privilegedepthmask"] = mask };

            var grants = new List<Entity>
            {
                Grant(org.Admin.Id, accountRead, 8), Grant(org.Admin.Id, accountDelete, 8), Grant(org.Admin.Id, contactRead, 8),
                Grant(org.SellerSales.Id, accountRead, 1),
                Grant(org.Auditor.Id, accountRead, 2), Grant(org.Auditor.Id, contactRead, 2)
            };

            service.OnRetrieveMultiple = query =>
            {
                var q = (QueryExpression)query;
                switch (q.EntityName)
                {
                    case "privilege":
                        return new EntityCollection(new List<Entity> { Privilege(accountRead, 1, "account"), Privilege(accountDelete, 65536, "account"), Privilege(contactRead, 1, "contact") });
                    case "roleprivileges":
                        var asked = new HashSet<Guid>(q.Criteria.Conditions.Single(c => c.AttributeName == "roleid").Values.Cast<Guid>());
                        return new EntityCollection(grants.Where(g => asked.Contains(g.GetAttributeValue<Guid>("roleid"))).ToList());
                    default:
                        return graph(query);
                }
            };

            service.OnExecute = request =>
            {
                var response = new RetrieveAllEntitiesResponse();
                EntityMetadata Table(string logical, string display) => new EntityMetadata
                {
                    LogicalName = logical,
                    DisplayName = new Microsoft.Xrm.Sdk.Label(new LocalizedLabel(display, 1033), new LocalizedLabel[0])
                };
                response.Results["EntityMetadata"] = new[] { Table("account", "Account"), Table("contact", "Contact"), Table("lead", "Lead") };
                return response;
            };

            return new TestHost { Security = new SecurityService(service) };
        }

        private static void Run(Action<FeatureView, FilterableGrid, FilterableGrid> test)
        {
            Sta.Run(() =>
            {
                var org = new Org();
                var view = new UserAccessMatrixFeature().CreateView(HostFor(org));
                var form = new Form { Size = new Size(1300, 500) };
                form.Controls.Add(view);
                form.Show();
                Application.DoEvents();
                _ = view.Commands;
                view.OnActivated();

                var grids = new List<FilterableGrid>();
                Collect(view, grids);
                var ordered = grids.OrderBy(g => g.PointToScreen(Point.Empty).X).ToArray();
                test(view, ordered[0], ordered[1]);
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
        public void OpeningTheScreen_ListsTheUsers_AndAsksForOne()
        {
            Run((view, users, matrix) =>
            {
                Assert.Equal(new[] { "Ada", "Bob", "Dee" }, users.GetVisible<UserInfo>().Select(u => u.FullName));
                Assert.NotNull(Info(view, "Select a user on the left"));
            });
        }

        [Fact]
        public void SelectingAUser_ShowsTheirReachPerTableAndPrivilege()
        {
            Run((view, users, matrix) =>
            {
                MasterDetailHarness.Select<UserInfo>(users, u => u.FullName == "Ada");
                TeamMembersViewTests.Pump(() => matrix.GetVisible<MatrixRow>().Any());

                var rows = matrix.GetVisible<MatrixRow>();
                Assert.Equal(new[] { "Account", "Contact" }, rows.Select(r => r.Table)); // Lead has nothing
                var account = rows.Single(r => r.Table == "Account");
                Assert.Equal("Org", account.Read);
                Assert.Equal("Org", account.Delete);
                Assert.Equal(string.Empty, account.Write);
            });
        }

        [Fact]
        public void TheColumnsAreTheTableAndTheEightPrivileges()
        {
            Run((view, users, matrix) =>
            {
                MasterDetailHarness.Select<UserInfo>(users, u => u.FullName == "Ada");
                TeamMembersViewTests.Pump(() => matrix.GetVisible<MatrixRow>().Any());

                Assert.Equal(new[] { "Table", "Create", "Read", "Write", "Delete", "Append", "Append To", "Assign", "Share" }, matrix.GetTable().Key);
            });
        }

        [Fact]
        public void ARoleThroughATeam_AndADirectOne_CombineToTheWidestReach()
        {
            Run((view, users, matrix) =>
            {
                MasterDetailHarness.Select<UserInfo>(users, u => u.FullName == "Bob");
                TeamMembersViewTests.Pump(() => matrix.GetVisible<MatrixRow>().Any());

                var account = matrix.GetVisible<MatrixRow>().Single(r => r.Table == "Account");
                Assert.Equal("BU", account.Read);   // Seller gives "User", Auditor via the team gives "BU"
                Assert.Equal(string.Empty, account.Delete);
                Assert.NotNull(Info(view, "Bob holds 1 role directly and 1 through teams."));
                Assert.NotNull(Info(view, "User = own records"));
            });
        }

        [Fact]
        public void AUserWithNoRoles_ShowsNothing_AndSaysWhy()
        {
            Run((view, users, matrix) =>
            {
                MasterDetailHarness.Select<UserInfo>(users, u => u.FullName == "Dee");
                TeamMembersViewTests.Pump(() => Info(view, "Dee holds no security roles") != null);

                Assert.Empty(matrix.GetVisible<MatrixRow>());
            });
        }

        [Fact]
        public void IncludeTablesWithNoAccess_AddsThemWithEmptyCells()
        {
            Run((view, users, matrix) =>
            {
                MasterDetailHarness.Select<UserInfo>(users, u => u.FullName == "Ada");
                TeamMembersViewTests.Pump(() => matrix.GetVisible<MatrixRow>().Any());

                MasterDetailHarness.Check(view, "Include tables with no access").Checked = true;
                TeamMembersViewTests.Pump(() => matrix.GetVisible<MatrixRow>().Count() == 3);

                Assert.False(matrix.GetVisible<MatrixRow>().Single(r => r.Table == "Lead").HasAny);
            });
        }

        [Fact]
        public void SelectingSeveralUsers_AsksForOne_AndKeepsTheLastMatrix()
        {
            Run((view, users, matrix) =>
            {
                MasterDetailHarness.Select<UserInfo>(users, u => u.FullName == "Ada");
                TeamMembersViewTests.Pump(() => matrix.GetVisible<MatrixRow>().Any());
                var shown = matrix.GetVisible<MatrixRow>().Count();

                MasterDetailHarness.Select<UserInfo>(users, u => true);
                TeamMembersViewTests.Pump(() => Info(view, "Select one user") != null);

                Assert.Equal(shown, matrix.GetVisible<MatrixRow>().Count());
            });
        }

        [Fact]
        public void TheCellsAreShadedByReach_WiderIsDarker_AndEmptyCellsStayPlain()
        {
            var org = UserAccessMatrixFeature.ShadeOf("Org").Value;
            var parentChild = UserAccessMatrixFeature.ShadeOf("P-C BU").Value;
            var unit = UserAccessMatrixFeature.ShadeOf("BU").Value;
            var user = UserAccessMatrixFeature.ShadeOf("User").Value;

            Assert.True(Brightness(org) < Brightness(parentChild));
            Assert.True(Brightness(parentChild) < Brightness(unit));
            Assert.True(Brightness(unit) < Brightness(user));
            Assert.Null(UserAccessMatrixFeature.ShadeOf(string.Empty));
            Assert.Null(UserAccessMatrixFeature.ShadeOf("Delete"));
        }

        private static float Brightness(Color color) => color.GetBrightness();

        [Fact]
        public void TheGridAppliesTheShade_ToTheCellsItFormats()
        {
            Sta.Run(() =>
            {
                var grid = new FilterableGrid { CellColor = UserAccessMatrixFeature.ShadeOf };
                var form = new Form { Size = new Size(600, 300) };
                form.Controls.Add(grid);
                form.Show();
                grid.SetData(new List<MatrixRow> { new MatrixRow { Table = "Account", Read = "Org", Write = string.Empty } }, "Table", "Read", "Write");
                var data = (DataGridView)Sta.Find(grid, c => c is DataGridView);
                var format = typeof(DataGridView).GetMethod("OnCellFormatting", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(DataGridViewCellFormattingEventArgs) }, null);

                DataGridViewCellStyle Format(string property, object value)
                {
                    var column = data.Columns.Cast<DataGridViewColumn>().First(c => c.DataPropertyName == property);
                    var style = new DataGridViewCellStyle();
                    format.Invoke(data, new object[] { new DataGridViewCellFormattingEventArgs(column.Index, 0, value, typeof(string), style) });
                    return style;
                }

                var shaded = Format("Read", "Org");
                Assert.Equal(UserAccessMatrixFeature.ShadeOf("Org").Value, shaded.BackColor);
                Assert.Equal(DataGridViewContentAlignment.MiddleCenter, shaded.Alignment);
                Assert.True(Format("Write", string.Empty).BackColor.IsEmpty);
                Assert.True(Format("Table", "Account").BackColor.IsEmpty);
                form.Dispose();
            });
        }

        [Fact]
        public void ItIsListedUnderExplore_AfterExplainAccess()
        {
            var ids = FeatureRegistry.CreateAll().Select(f => f.Id).ToList();

            Assert.Equal(FeatureGroup.Explore, new UserAccessMatrixFeature().Group);
            Assert.Equal(ids.IndexOf("explain-access") + 1, ids.IndexOf("user-access-matrix"));
        }
    }
}
