using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SecurityToolkit.Features;
using SecurityToolkit.Services;
using SecurityToolkit.UI;
using Xunit;

namespace SecurityToolkit.Tests
{
    public class PrivilegedUsersViewTests
    {
        private static void Run(Action<FeatureView, FilterableGrid, FilterableGrid> test)
        {
            Sta.Run(() =>
            {
                var org = new Org();
                var fake = OrgFake.Serve(org.Graph);
                var served = fake.OnRetrieveMultiple;
                var tables = new[] { "account", "contact" };
                var privileges = tables.Select(t => new Entity("privilege", Guid.NewGuid())
                {
                    ["accessright"] = 65536,
                    ["otc.objecttypecode"] = new AliasedValue("privilegeobjecttypecodes", "objecttypecode", t)
                }).ToList();
                fake.OnRetrieveMultiple = q =>
                {
                    switch (((QueryExpression)q).EntityName)
                    {
                        case "privilege":
                            return new EntityCollection(privileges);
                        case "roleprivileges":
                            // Admin can delete everywhere in the organization; nobody else can.
                            return new EntityCollection(privileges.Select(p => new Entity("roleprivileges") { ["roleid"] = org.Admin.Id, ["privilegeid"] = p.Id }).ToList());
                        default:
                            return served(q);
                    }
                };

                var host = new TestHost { Security = new SecurityService(fake) };
                var view = new PrivilegedUsersFeature().CreateView(host);
                var form = new Form { Size = new Size(1200, 500) };
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
        public void OpeningTheFeature_ListsTheSixChecksWithTheirCounts()
        {
            Run((view, checks, findings) =>
            {
                TeamMembersViewTests.Pump(() => checks.GetVisible<PrivilegedCheck>().Any());
                var counts = checks.GetVisible<PrivilegedCheck>().ToDictionary(c => c.Title, c => c.Count);

                Assert.Equal(6, counts.Count);
                Assert.Equal(1, counts[PrivilegedUsers.BroadRolesTitle]);
                Assert.Equal(1, counts[PrivilegedUsers.HoldersTitle]);
            });
        }

        [Fact]
        public void WithNothingSelected_EveryFindingIsShown()
        {
            Run((view, checks, findings) =>
            {
                TeamMembersViewTests.Pump(() => findings.GetVisible<PrivilegedRow>().Any());

                var total = checks.GetVisible<PrivilegedCheck>().Sum(c => c.Count);
                Assert.Equal(total, findings.GetVisible<PrivilegedRow>().Count());
                Assert.NotNull(Info(view, "across 6 checks"));
            });
        }

        [Fact]
        public void SelectingACheck_NarrowsTheFindings_AndExplainsIt()
        {
            Run((view, checks, findings) =>
            {
                TeamMembersViewTests.Pump(() => checks.GetVisible<PrivilegedCheck>().Any());
                MasterDetailHarness.Select<PrivilegedCheck>(checks, c => c.Title == PrivilegedUsers.BroadRolesTitle);
                TeamMembersViewTests.Pump(() => findings.GetVisible<PrivilegedRow>().Count() == 1);

                Assert.Equal("Admin", findings.GetVisible<PrivilegedRow>().Single().Subject);
                Assert.NotNull(Info(view, "across the whole organization"));
            });
        }

        [Fact]
        public void TheExportCarriesTheCheckNameOnEveryRow()
        {
            Run((view, checks, findings) =>
            {
                TeamMembersViewTests.Pump(() => findings.GetVisible<PrivilegedRow>().Any());
                var table = findings.GetTable();

                Assert.Equal(new[] { "Check", "User or role", "Roles", "Business Unit", "Status" }, table.Key);
                Assert.All(table.Value, row => Assert.False(string.IsNullOrEmpty(row[0])));
            });
        }

        [Fact]
        public void ItIsListedUnderItsGroup()
        {
            Assert.Equal(FeatureGroup.Review, new PrivilegedUsersFeature().Group);
            Assert.Contains(FeatureRegistry.CreateAll(), f => f is PrivilegedUsersFeature);
        }
    }
}
