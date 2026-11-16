using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Features;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using SecurityToolkit.UI;
using Xunit;
using Label = System.Windows.Forms.Label;

namespace SecurityToolkit.Tests
{
    public class EnvironmentComparerTests
    {
        /// <summary>One environment: the sample organization (with ids of its own), what its roles grant, and a privilege catalogue.</summary>
        private static EnvironmentSecurity Side(string name, Org org, Dictionary<RoleInfo, (string Table, PrivilegeKind Kind, PrivilegeDepth Depth)[]> grants)
        {
            var catalog = new Dictionary<Guid, IReadOnlyList<PrivilegeTarget>>();
            var byKey = new Dictionary<string, Guid>();
            Guid Privilege(string table, PrivilegeKind kind)
            {
                var key = table + kind;
                if (!byKey.TryGetValue(key, out var id))
                {
                    byKey[key] = id = Guid.NewGuid(); // each environment has its own privilege ids
                    catalog[id] = new[] { new PrivilegeTarget { Table = table, Kind = kind } };
                }

                return id;
            }

            return new EnvironmentSecurity
            {
                Name = name,
                Graph = org.Graph,
                Catalog = catalog,
                OtherPrivileges = new OtherPrivilege[0],
                Grants = grants.ToDictionary(g => g.Key.Id, g => (IReadOnlyList<RoleGrant>)g.Value.Select(x => new RoleGrant { PrivilegeId = Privilege(x.Table, x.Kind), Depth = x.Depth }).ToList())
            };
        }

        private static IReadOnlyList<EnvironmentCheck> Compare(Action<Org> changeProd, Func<Org, Dictionary<RoleInfo, (string, PrivilegeKind, PrivilegeDepth)[]>> prodGrants = null)
        {
            var dev = new Org();
            var prod = new Org();
            changeProd?.Invoke(prod);
            var devGrants = new Dictionary<RoleInfo, (string, PrivilegeKind, PrivilegeDepth)[]>
            {
                [dev.Admin] = new[] { ("account", PrivilegeKind.Delete, PrivilegeDepth.Organization), ("contact", PrivilegeKind.Read, PrivilegeDepth.Organization) }
            };
            var other = prodGrants?.Invoke(prod) ?? new Dictionary<RoleInfo, (string, PrivilegeKind, PrivilegeDepth)[]>
            {
                [prod.Admin] = new[] { ("account", PrivilegeKind.Delete, PrivilegeDepth.Organization), ("contact", PrivilegeKind.Read, PrivilegeDepth.Organization) }
            };

            return EnvironmentComparer.Compare(Side("Dev", dev, devGrants), Side("Prod", prod, other));
        }

        private static EnvironmentCheck Check(IReadOnlyList<EnvironmentCheck> checks, string title) => checks.Single(c => c.Title == title);

        [Fact]
        public void TwoIdenticalEnvironments_HaveNoDifference_ThoughEveryIdDiffers()
        {
            var checks = Compare(null);

            Assert.Equal(new[] { "Roles only in Dev", "Roles only in Prod", EnvironmentComparer.Privileges, EnvironmentComparer.TeamRoles }, checks.Select(c => c.Title));
            Assert.All(checks, c => Assert.Equal(0, c.Count));
        }

        [Fact]
        public void ARoleOnOneSideOnly_IsListedUnderThatSide()
        {
            var checks = Compare(prod => prod.Auditor.Name = "Reader");

            Assert.Equal(new[] { "Auditor" }, Check(checks, "Roles only in Dev").Rows.Select(r => r.Item));
            Assert.Equal(new[] { "Reader" }, Check(checks, "Roles only in Prod").Rows.Select(r => r.Item));
            Assert.Equal(("Yes", "No"), (Check(checks, "Roles only in Dev").Rows[0].Here, Check(checks, "Roles only in Dev").Rows[0].There));
        }

        [Fact]
        public void APrivilegeThatReachesDifferently_OrExistsOnOneSide_IsADifference()
        {
            var checks = Compare(null, prod => new Dictionary<RoleInfo, (string, PrivilegeKind, PrivilegeDepth)[]>
            {
                [prod.Admin] = new[] { ("account", PrivilegeKind.Delete, PrivilegeDepth.BusinessUnit), ("account", PrivilegeKind.Create, PrivilegeDepth.User) }
            });

            var rows = Check(checks, EnvironmentComparer.Privileges).Rows;
            Assert.Equal(new[] { "Create on account|None|User", "Delete on account|Organization|Business Unit", "Read on contact|Organization|None" },
                rows.Select(r => $"{r.Detail}|{r.Here}|{r.There}"));
            Assert.All(rows, r => Assert.Equal("Admin", r.Item));
        }

        [Fact]
        public void ATeamWithDifferentRoles_IsADifference()
        {
            var checks = Compare(prod => prod.Auditor.Name = "Reader");

            var rows = Check(checks, EnvironmentComparer.TeamRoles).Rows;
            Assert.Equal(new[] { "Reviewers|Auditor|Yes|No", "Reviewers|Reader|No|Yes" }, rows.Select(r => $"{r.Item}|{r.Detail}|{r.Here}|{r.There}"));
        }

        [Fact]
        public void OtherPrivileges_AreComparedByName()
        {
            var dev = new Org();
            var prod = new Org();
            EnvironmentSecurity WithExport(string name, Org org, PrivilegeDepth depth)
            {
                var id = Guid.NewGuid();
                return new EnvironmentSecurity
                {
                    Name = name,
                    Graph = org.Graph,
                    Catalog = new Dictionary<Guid, IReadOnlyList<PrivilegeTarget>>(),
                    OtherPrivileges = new[] { new OtherPrivilege { Id = id, Name = "prvExportToExcel", Label = "Export to Excel" } },
                    Grants = new Dictionary<Guid, IReadOnlyList<RoleGrant>> { [org.Admin.Id] = new[] { new RoleGrant { PrivilegeId = id, Depth = depth } } }
                };
            }

            var checks = EnvironmentComparer.Compare(WithExport("Dev", dev, PrivilegeDepth.Organization), WithExport("Prod", prod, PrivilegeDepth.BusinessUnit));

            var row = Check(checks, EnvironmentComparer.Privileges).Rows.Single();
            Assert.Equal(("Export to Excel", "Organization", "Business Unit"), (row.Detail, row.Here, row.There));
        }
    }

    public class CompareEnvironmentsViewTests
    {
        private static void Run(Action<FeatureView, TestHost, FilterableGrid, FilterableGrid> test)
        {
            Sta.Run(() =>
            {
                var host = OrgFake.HostFor(new Org());
                var view = new CompareEnvironmentsFeature().CreateView(host);
                view.FeatureId = "compare-environments";
                var form = new Form { Size = new Size(1300, 600) };
                form.Controls.Add(view);
                form.Show();
                Application.DoEvents();
                _ = view.Commands;
                view.OnActivated();

                var grids = new List<FilterableGrid>();
                void Collect(Control parent) { foreach (Control c in parent.Controls) { if (c is FilterableGrid g) { grids.Add(g); } else { Collect(c); } } }
                Collect(view);
                var ordered = grids.OrderBy(g => g.PointToScreen(Point.Empty).X).ToArray();
                test(view, host, ordered[0], ordered[1]);
                form.Dispose();
            });
        }

        private static void Click(FeatureView view, string text) => view.Commands.OfType<CommandButton>().Single(c => c.Text == text).PerformClick();

        [Fact]
        public void WithoutASecondEnvironment_ItAsksForOne()
        {
            Run((view, host, checks, differences) =>
            {
                Assert.Empty(checks.GetVisible<EnvironmentCheck>());
                Assert.NotNull(Sta.Find(view, c => c is Label l && l.Text.StartsWith("Choose the other environment")));
                Assert.NotNull(Sta.Find(view, c => c is Label l && l.Text == "(choose the other environment)"));

                Click(view, "Choose the other environment");
                Assert.Equal(1, host.OtherRequests);
            });
        }

        [Fact]
        public void ChoosingTheOtherEnvironment_ComparesBoth_AndNamesThemInTheSentenceAndColumns()
        {
            Run((view, host, checks, differences) =>
            {
                var prod = new Org();
                prod.Auditor.Name = "Reader";
                host.OtherToChoose = () => new KeyValuePair<string, SecurityService>("Prod", new SecurityService(OrgFake.For(prod)));

                Click(view, "Choose the other environment");

                var counts = checks.GetVisible<EnvironmentCheck>().ToDictionary(c => c.Title, c => c.Count);
                Assert.Equal(1, counts["Roles only in Dev"]);
                Assert.Equal(1, counts["Roles only in Prod"]);
                Assert.Equal(2, counts[EnvironmentComparer.TeamRoles]);
                Assert.Equal(new[] { "Check", "Item", "Detail", "Dev", "Prod" }, differences.GetTable().Key.ToArray());
                Assert.NotNull(Sta.Find(view, c => c is Label l && l.Text == "Prod"));
                Assert.EndsWith("differences", ((Label)Sta.Find(view, c => c.Name == "SentenceAnswer")).Text);
            });
        }
    }
}
