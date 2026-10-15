using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Features;
using SecurityToolkit.Services;
using SecurityToolkit.UI;
using Xunit;

namespace SecurityToolkit.Tests
{
    public class CleanupReportViewTests
    {
        private static void Run(Action<FeatureView, FilterableGrid, FilterableGrid> test)
        {
            Sta.Run(() =>
            {
                var org = new CleanupOrg();
                var host = new TestHost { Security = new SecurityService(OrgFake.Serve(org.Graph)) };
                var view = new CleanupReportFeature().CreateView(host);
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
            return ((Label)Sta.Find(view, c => c is Label l && l.Text.Contains(containing)))?.Text;
        }

        [Fact]
        public void OpeningTheFeature_ListsEveryCheckWithItsCount()
        {
            Run((view, checks, findings) =>
            {
                var counts = checks.GetVisible<CleanupCheck>().ToDictionary(c => c.Title, c => c.Count);

                Assert.Equal(6, counts.Count);
                Assert.Equal(1, counts[CleanupChecks.NoRoles]);
                Assert.Equal(2, counts[CleanupChecks.UnusedRoles]);
                Assert.Equal(2, counts[CleanupChecks.TeamsWithoutRoles]);
            });
        }

        [Fact]
        public void WithNothingSelected_EveryFindingIsShown()
        {
            Run((view, checks, findings) =>
            {
                Assert.Equal(8, findings.GetVisible<CleanupRow>().Count());
                Assert.NotNull(Info(view, "8 finding(s) across 6 checks"));
            });
        }

        [Fact]
        public void SelectingACheck_NarrowsTheFindings_AndExplainsIt()
        {
            Run((view, checks, findings) =>
            {
                MasterDetailHarness.Select<CleanupCheck>(checks, c => c.Title == CleanupChecks.UnusedRoles);
                TeamMembersViewTests.Pump(() => findings.GetVisible<CleanupRow>().Count() == 2);

                Assert.Equal(new[] { "Spare", "Vendor" }, findings.GetVisible<CleanupRow>().Select(r => r.Item));
                Assert.NotNull(Info(view, "not assigned to any user or team"));
            });
        }

        [Fact]
        public void SelectingSeveralChecks_ShowsTheirFindingsTogether()
        {
            Run((view, checks, findings) =>
            {
                MasterDetailHarness.Select<CleanupCheck>(checks, c => c.Title == CleanupChecks.NoRoles || c.Title == CleanupChecks.EmptyTeams);
                TeamMembersViewTests.Pump(() => findings.GetVisible<CleanupRow>().Count() == 2);

                Assert.Equal(new[] { "Ann", "Empty" }, findings.GetVisible<CleanupRow>().Select(r => r.Item).OrderBy(n => n));
                Assert.NotNull(Info(view, "2 finding(s) in 2 selected checks"));
            });
        }

        [Fact]
        public void ClearingTheSelection_ShowsEverythingAgain()
        {
            Run((view, checks, findings) =>
            {
                MasterDetailHarness.Select<CleanupCheck>(checks, c => c.Title == CleanupChecks.NoRoles);
                TeamMembersViewTests.Pump(() => findings.GetVisible<CleanupRow>().Count() == 1);

                checks.ClearSelection();
                TeamMembersViewTests.Pump(() => findings.GetVisible<CleanupRow>().Count() == 8);
            });
        }

        [Fact]
        public void TheExportCarriesTheCheckNameOnEveryRow()
        {
            Run((view, checks, findings) =>
            {
                var table = findings.GetTable();

                Assert.Equal(new[] { "Check", "Item", "Detail", "Business Unit" }, table.Key);
                Assert.Equal(8, table.Value.Count);
                Assert.All(table.Value, row => Assert.False(string.IsNullOrEmpty(row[0])));
            });
        }

        [Fact]
        public void ItIsListedUnderExplore()
        {
            Assert.Equal(FeatureGroup.Explore, new CleanupReportFeature().Group);
        }
    }
}
