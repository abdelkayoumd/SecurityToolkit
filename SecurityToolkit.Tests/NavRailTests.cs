using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using SecurityToolkit.Features;
using SecurityToolkit.UI;
using Xunit;

namespace SecurityToolkit.Tests
{
    public class NavRailTests
    {
        private static NavRail Rail(int height = 600)
        {
            var rail = new NavRail { Dock = DockStyle.None, Height = height };
            rail.AddItem("home", "", "Home");
            rail.AddItem("a", "", "Users and teams", FeatureGroup.Explore);
            rail.AddItem("b", "", "Team members", FeatureGroup.Explore);
            rail.AddItem("c", "", "Users by role", FeatureGroup.Explore);
            rail.AddItem("d", "", "Assign roles to users", FeatureGroup.Change);
            rail.AddItem("e", "", "Clone user profile", FeatureGroup.Change);
            return rail;
        }

        private static void Wheel(NavRail rail, int delta)
        {
            var method = typeof(NavRail).GetMethod("OnMouseWheel", BindingFlags.NonPublic | BindingFlags.Instance);
            method.Invoke(rail, new object[] { new MouseEventArgs(MouseButtons.None, 0, 10, 100, delta) });
        }

        [Fact]
        public void EachGroupGetsItsBoldHeadingAboveItsFirstItem_AndHomeHasNone()
        {
            Sta.Run(() =>
            {
                Assert.Equal(new[] { "Home", "#EXPLORE", "Users and teams", "Team members", "Users by role", "#CHANGE", "Assign roles to users", "Clone user profile" },
                    Rail().RowLabels);
            });
        }

        [Fact]
        public void TheCompleteFeatureSet_ProducesTwoHeadingsInOrder()
        {
            Sta.Run(() =>
            {
                var rail = new NavRail();
                foreach (var f in FeatureRegistry.CreateAll())
                {
                    rail.AddItem(f.Id, f.Glyph, f.Title, f.Group);
                }

                var labels = rail.RowLabels.ToList();
                Assert.Equal(new[] { "#EXPLORE", "#CHANGE" }, labels.Where(l => l.StartsWith("#")));
                Assert.Equal("Home", labels[0]);
                Assert.True(labels.IndexOf("#CHANGE") > labels.IndexOf("Privilege finder"));
                Assert.Equal("Clone user profile", labels.Last());
            });
        }

        [Fact]
        public void HeadingsAreNotSelectable()
        {
            Sta.Run(() =>
            {
                var rail = Rail();
                string chosen = null;
                rail.ItemSelected += (s, id) => chosen = id;

                // Home occupies 40..76 (below the 40px toggle row); the EXPLORE heading sits right under it.
                Assert.Null(rail.ItemIdAt(new Point(20, 40 + 36 + 10)));
                Assert.Equal("a", rail.ItemIdAt(new Point(20, 40 + 36 + 30 + 10)));
                Assert.Null(chosen);
            });
        }

        [Fact]
        public void SelectingAnItem_RaisesTheEventWithItsId()
        {
            Sta.Run(() =>
            {
                var rail = Rail();
                string chosen = null;
                rail.ItemSelected += (s, id) => chosen = id;

                rail.Select("c");

                Assert.Equal("c", chosen);
                Assert.Equal("c", rail.SelectedId);
            });
        }

        [Fact]
        public void WhenEverythingFits_ThereIsNothingToScroll()
        {
            Sta.Run(() => Assert.Equal(0, Rail(height: 600).MaxScroll));
        }

        [Fact]
        public void InAShortWindow_TheListScrollsByTheWheel_AndStopsAtBothEnds()
        {
            Sta.Run(() =>
            {
                var rail = Rail(height: 200);
                Assert.True(rail.MaxScroll > 0);

                Wheel(rail, -120);
                Assert.True(rail.ScrollOffset > 0);

                for (var i = 0; i < 40; i++) { Wheel(rail, -120); }
                Assert.Equal(rail.MaxScroll, rail.ScrollOffset);

                for (var i = 0; i < 40; i++) { Wheel(rail, 120); }
                Assert.Equal(0, rail.ScrollOffset);
            });
        }

        [Fact]
        public void ClickingAfterScrolling_HitsTheItemThatIsActuallyThere()
        {
            Sta.Run(() =>
            {
                var rail = Rail(height: 200);
                Wheel(rail, -120);
                Assert.True(rail.ScrollOffset > 0);

                // One wheel step is 36px: Home (0..36) has scrolled away, the EXPLORE heading (36..66) is now at the
                // top, and the first item starts 30px below the toggle row.
                Assert.Null(rail.ItemIdAt(new Point(20, 40 + 10)));          // the heading
                Assert.Equal("a", rail.ItemIdAt(new Point(20, 40 + 30 + 10))); // Users and teams, not Home
            });
        }

        [Fact]
        public void SelectingAnItemBelowTheFold_ScrollsItIntoView()
        {
            Sta.Run(() =>
            {
                var rail = Rail(height: 200);

                rail.Select("e"); // the last item, well below a 200px window

                Assert.Equal(rail.MaxScroll, rail.ScrollOffset);
                Assert.Equal("e", rail.ItemIdAt(new Point(20, 199 - 10)));
            });
        }

        [Fact]
        public void Collapsing_ShrinksHeadingsToDividers_AndNeverLeavesTheScrollPastTheEnd()
        {
            Sta.Run(() =>
            {
                var rail = Rail(height: 220);
                rail.Select("e");
                var expandedMax = rail.MaxScroll;

                rail.Expanded = false;

                Assert.True(rail.MaxScroll < expandedMax);
                Assert.True(rail.ScrollOffset <= rail.MaxScroll);
            });
        }

        [Fact]
        public void BothModesPaintWithoutError()
        {
            Sta.Run(() =>
            {
                var form = new Form { Size = new Size(400, 500) };
                var rail = new NavRail();
                foreach (var f in FeatureRegistry.CreateAll())
                {
                    rail.AddItem(f.Id, f.Glyph, f.Title, f.Group);
                }

                form.Controls.Add(rail);
                form.Show();
                Application.DoEvents();
                using (var bitmap = new Bitmap(rail.Width, rail.Height)) { rail.DrawToBitmap(bitmap, new Rectangle(Point.Empty, rail.Size)); }

                rail.Expanded = false;
                using (var bitmap = new Bitmap(rail.Width, rail.Height)) { rail.DrawToBitmap(bitmap, new Rectangle(Point.Empty, rail.Size)); }
                form.Dispose();
            });
        }
    }
}
