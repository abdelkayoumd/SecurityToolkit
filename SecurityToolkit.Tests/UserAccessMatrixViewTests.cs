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
                Assert.Equal("Organization", account.Read);
                Assert.Equal("Organization", account.Delete);
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
                Assert.Equal("Business Unit", account.Read);   // Seller gives "User", Auditor via the team gives "Business Unit"
                Assert.Equal("None", account.Delete);
                Assert.NotNull(Info(view, "Bob holds 1 role directly and 1 through teams."));
                Assert.NotNull(Info(view, "A blank cell means the privilege does not exist"));
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
        public void TheFiveDepthWordsAreDrawnAsCells_AndAnyOtherTextIsLeftToTheGrid()
        {
            foreach (var word in new[] { "None", "User", "Business Unit", "Parent: Child Business Units", "Organization" })
            {
                Assert.True(DepthCell.Handles(word), word);
            }

            foreach (var other in new[] { "Account", string.Empty, "Org", "BU" })
            {
                Assert.False(DepthCell.Handles(other), other);
            }
        }

        /// <summary>Draws one cell on a white background and reports whether any pixel in the icon area has the given look.</summary>
        private static bool IconHasPixel(string word, Func<Color, bool> look)
        {
            using (var bitmap = new Bitmap(150, 44))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.White);
                Assert.True(DepthCell.Paint(graphics, new Rectangle(0, 0, 150, 44), word, selected: false));
                for (var x = 4; x < 26; x++)
                {
                    for (var y = 0; y < 44; y++)
                    {
                        if (look(bitmap.GetPixel(x, y)))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        [Fact]
        public void EachDepthHasItsOwnIconColour_AsInTheDynamicsSecurityRoleEditor()
        {
            Sta.Run(() =>
            {
                Assert.True(IconHasPixel("None", c => c.R > 140 && c.G < 90 && c.B < 90), "None: red no-entry sign");
                Assert.True(IconHasPixel("Organization", c => c.G > 100 && c.R < 80 && c.B < 80), "Organization: green org chart");
                Assert.True(IconHasPixel("Business Unit", c => c.B > 150 && c.R < 90), "Business Unit: blue people");
                Assert.True(IconHasPixel("Parent: Child Business Units", c => c.R > 150 && c.G < 110 && c.B < 100), "Parent: Child: red people");
                Assert.True(IconHasPixel("User", c => Math.Abs(c.R - c.G) < 12 && Math.Abs(c.G - c.B) < 12 && c.R < 140), "User: grey person");
            });
        }

        [Fact]
        public void ADepthThatIsNotNone_IsNotRed_AndNoneIsNotGreen()
        {
            Sta.Run(() =>
            {
                Assert.False(IconHasPixel("Organization", c => c.R > 140 && c.G < 90 && c.B < 90));
                Assert.False(IconHasPixel("None", c => c.G > 100 && c.R < 80 && c.B < 80));
            });
        }

        [Fact]
        public void TheLongestLabelWrapsInsideTheCell_RatherThanSpillingIntoTheNextOne()
        {
            Sta.Run(() =>
            {
                using (var bitmap = new Bitmap(300, 44))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.White);
                    DepthCell.Paint(graphics, new Rectangle(0, 0, 118, 44), "Parent: Child Business Units", selected: false);

                    // Nothing may be drawn to the right of the 118px cell.
                    for (var x = 118; x < 300; x++)
                    {
                        for (var y = 0; y < 44; y++)
                        {
                            Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(x, y).ToArgb());
                        }
                    }
                }
            });
        }

        [Fact]
        public void TheGridUsesTheRenderer_ForItsBodyCells()
        {
            Sta.Run(() =>
            {
                var grid = new FilterableGrid { BodyCellRenderer = DepthCell.Paint, RowHeight = 44 };
                var form = new Form { Size = new Size(700, 300) };
                form.Controls.Add(grid);
                form.Show();
                grid.SetData(new List<MatrixRow> { new MatrixRow { Table = "Account", Read = "Organization" } }, "Table", "Read");
                Application.DoEvents();

                using (var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.ClientSize));
                    var green = false;
                    for (var x = 0; x < bitmap.Width && !green; x++)
                    {
                        for (var y = 0; y < bitmap.Height && !green; y++)
                        {
                            var c = bitmap.GetPixel(x, y);
                            green = c.G > 100 && c.R < 60 && c.B < 60;
                        }
                    }

                    Assert.True(green, "The Organization cell's green org-chart icon was not drawn.");
                }

                Assert.Equal(44, grid.RowHeight);
                form.Dispose();
            });
        }

        [Fact]
        public void TheMatrixScreen_UsesTheCrmStyleCellsAndTallRows()
        {
            Run((view, users, matrix) =>
            {
                Assert.NotNull(matrix); // the screen opened with its grids
                Assert.Equal(44, matrix.RowHeight);
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
