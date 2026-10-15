using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using SecurityToolkit.Features;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using SecurityToolkit.UI;
using Xunit;

namespace SecurityToolkit.Tests
{
    public class TeamMembersViewTests
    {
        /// <summary>Lets timers and queued UI work run until <paramref name="done"/> holds (or fails after 3 seconds).</summary>
        internal static void Pump(Func<bool> done)
        {
            var clock = Stopwatch.StartNew();
            while (!done() && clock.ElapsedMilliseconds < 3000)
            {
                Application.DoEvents();
                Thread.Sleep(15);
            }

            Assert.True(done(), "Timed out waiting for the view to update.");
        }

        private static void Click(CommandButton button)
        {
            typeof(Control).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(button, new object[] { EventArgs.Empty });
        }

        private static FilterableGrid[] Grids(Control view)
        {
            // Master (left) first, detail (right) second.
            var all = new System.Collections.Generic.List<FilterableGrid>();
            Collect(view, all);
            return all.OrderBy(g => g.PointToScreen(Point.Empty).X).ToArray();
        }

        private static void Collect(Control root, System.Collections.Generic.List<FilterableGrid> found)
        {
            foreach (Control child in root.Controls)
            {
                if (child is FilterableGrid grid)
                {
                    found.Add(grid);
                }
                else
                {
                    Collect(child, found);
                }
            }
        }

        private static void SelectRows(FilterableGrid grid, Func<TeamInfo, bool> pick)
        {
            var data = (DataGridView)Sta.Find(grid, c => c is DataGridView);
            foreach (DataGridViewRow row in data.Rows)
            {
                row.Selected = pick((TeamInfo)row.DataBoundItem);
            }
        }

        private static FeatureView Open(TestHost host, Form form)
        {
            var view = new TeamMembersFeature().CreateView(host);
            form.Controls.Add(view);
            form.Show();
            Application.DoEvents();
            return view;
        }

        [Fact]
        public void OpeningTheFeature_ListsTheTeamsWithoutAnyPopup()
        {
            Sta.Run(() =>
            {
                var form = new Form { Size = new Size(1100, 500) };
                var view = Open(OrgFake.HostFor(new Org()), form);

                view.OnActivated();

                var master = Grids(view)[0];
                Assert.Equal(new[] { "Empty team", "Reviewers" }, master.GetVisible<TeamInfo>().Select(t => t.Name));
                form.Dispose();
            });
        }

        [Fact]
        public void SelectingATeam_ShowsItsActiveMembers()
        {
            Sta.Run(() =>
            {
                var form = new Form { Size = new Size(1100, 500) };
                var view = Open(OrgFake.HostFor(new Org()), form);
                view.OnActivated();
                var grids = Grids(view);

                SelectRows(grids[0], t => t.Name == "Reviewers");
                Pump(() => grids[1].GetVisible<TeamMemberRow>().Any());

                Assert.Equal(new[] { "Bob" }, grids[1].GetVisible<TeamMemberRow>().Select(r => r.User)); // Cy is disabled
                form.Dispose();
            });
        }

        [Fact]
        public void SelectingSeveralTeams_ShowsTheMembersOfAllOfThem()
        {
            Sta.Run(() =>
            {
                var form = new Form { Size = new Size(1100, 500) };
                var view = Open(OrgFake.HostFor(new Org()), form);
                view.OnActivated();
                var grids = Grids(view);

                SelectRows(grids[0], t => true);
                Pump(() => grids[1].GetVisible<TeamMemberRow>().Any());

                Assert.Equal(new[] { "Empty team/Bob", "Reviewers/Bob" },
                    grids[1].GetVisible<TeamMemberRow>().Select(r => r.Team + "/" + r.User).OrderBy(s => s));
                form.Dispose();
            });
        }

        [Fact]
        public void IncludeDisabledUsers_AddsThemToTheCurrentSelection()
        {
            Sta.Run(() =>
            {
                var form = new Form { Size = new Size(1100, 500) };
                var view = Open(OrgFake.HostFor(new Org()), form);
                view.OnActivated();
                var grids = Grids(view);
                SelectRows(grids[0], t => t.Name == "Reviewers");
                Pump(() => grids[1].GetVisible<TeamMemberRow>().Any());

                var box = (CheckBox)view.Commands.First(c => c is CheckBox);
                box.Checked = true;

                Assert.Equal(new[] { "Bob", "Cy" }, grids[1].GetVisible<TeamMemberRow>().Select(r => r.User).OrderBy(n => n));
                form.Dispose();
            });
        }

        [Fact]
        public void ClearingTheSelection_KeepsShowingTheLastChoice()
        {
            Sta.Run(() =>
            {
                var form = new Form { Size = new Size(1100, 500) };
                var view = Open(OrgFake.HostFor(new Org()), form);
                view.OnActivated();
                var grids = Grids(view);
                SelectRows(grids[0], t => t.Name == "Reviewers");
                Pump(() => grids[1].GetVisible<TeamMemberRow>().Any());

                grids[0].ClearSelection();
                Pump(() => true);
                Thread.Sleep(300);
                Application.DoEvents();

                Assert.Single(grids[1].GetVisible<TeamMemberRow>());
                form.Dispose();
            });
        }

        [Fact]
        public void WhenNotConnected_ItAsksToConnectAndLoadsNothing()
        {
            Sta.Run(() =>
            {
                var form = new Form { Size = new Size(1100, 500) };
                var host = new TestHost { Security = null };
                var view = Open(host, form);

                view.OnActivated();

                var info = (Label)Sta.Find(view, c => c is Label l && l.Text.StartsWith("Connect"));
                Assert.NotNull(info);
                Assert.Empty(Grids(view)[0].GetVisible<TeamInfo>());
                form.Dispose();
            });
        }

        [Fact]
        public void ConnectingLater_FillsTheListAutomatically()
        {
            Sta.Run(() =>
            {
                var form = new Form { Size = new Size(1100, 500) };
                var host = new TestHost { Security = null };
                var view = Open(host, form);
                view.OnActivated();

                host.Security = OrgFake.HostFor(new Org()).Security;
                view.OnConnectionChanged();

                Assert.Equal(2, Grids(view)[0].GetVisible<TeamInfo>().Count);
                form.Dispose();
            });
        }
    }
}
