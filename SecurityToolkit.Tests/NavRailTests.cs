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
            rail.AddItem("a", "", "Users and teams", FeatureGroup.LookUp);
            rail.AddItem("b", "", "Teams and members", FeatureGroup.LookUp);
            rail.AddItem("c", "", "Role holders", FeatureGroup.LookUp);
            rail.AddItem("x", "", "Two users", FeatureGroup.Compare);
            rail.AddItem("d", "", "Roles for users", FeatureGroup.Change);
            rail.AddItem("e", "", "Clone a user", FeatureGroup.Change);
            return rail;
        }

        private static void Wheel(NavRail rail, int delta)
        {
            var method = typeof(NavRail).GetMethod("OnMouseWheel", BindingFlags.NonPublic | BindingFlags.Instance);
            method.Invoke(rail, new object[] { new MouseEventArgs(MouseButtons.None, 0, 10, 100, delta) });
        }

        private static void Click(NavRail rail, Point point)
        {
            typeof(NavRail).GetMethod("OnMouseClick", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(rail, new object[] { new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0) });
        }

        /// <summary>The y of the middle of the row with this label ("#LOOK UP" for a heading), given what is currently shown.</summary>
        private static Point Middle(NavRail rail, string label, int heightOfItem = 36, int heightOfHeading = 30)
        {
            var y = rail.ContentTop - rail.ScrollOffset;
            foreach (var row in rail.VisibleRowLabels)
            {
                var height = row.StartsWith("#") ? heightOfHeading : heightOfItem;
                if (row == label)
                {
                    return new Point(20, y + height / 2 + (row.StartsWith("#") ? 4 : 0));
                }

                y += height;
            }

            throw new InvalidOperationException("Not shown: " + label);
        }

        [Fact]
        public void EachGroupGetsItsBoldHeadingAboveItsFirstItem_AndHomeHasNone()
        {
            Sta.Run(() =>
            {
                Assert.Equal(new[] { "Home", "#LOOK UP", "Users and teams", "Teams and members", "Role holders", "#COMPARE", "Two users", "#CHANGE", "Roles for users", "Clone a user" },
                    Rail().RowLabels);
            });
        }

        [Fact]
        public void TheCompleteFeatureSet_ProducesTheFiveHeadingsInOrder()
        {
            Sta.Run(() =>
            {
                var rail = new NavRail();
                foreach (var f in FeatureRegistry.CreateAll())
                {
                    rail.AddItem(f.Id, f.Glyph, f.Title, f.Group);
                }

                var labels = rail.RowLabels.ToList();
                Assert.Equal(new[] { "#LOOK UP", "#UNDERSTAND ACCESS", "#COMPARE", "#REVIEW", "#CHANGE" }, labels.Where(l => l.StartsWith("#")));
                Assert.Equal("Home", labels[0]);
                Assert.Equal("Clone a user", labels.Last());
            });
        }

        [Fact]
        public void TheMenuIsTheApprovedOne_ItemsInOrderUnderTheirHeadings()
        {
            Sta.Run(() =>
            {
                var rail = new NavRail();
                foreach (var f in FeatureRegistry.CreateAll())
                {
                    rail.AddItem(f.Id, f.Glyph, f.Title, f.Group);
                }

                Assert.Equal(new[]
                {
                    "Home",
                    "#LOOK UP", "Users and teams", "Teams and members", "Role holders", "Business units",
                    "#UNDERSTAND ACCESS", "A user's access", "Explain access", "Privilege finder",
                    "#COMPARE", "Two users", "Two roles",
                    "#REVIEW", "Privileged users", "Cleanup report",
                    "#CHANGE", "Roles for users", "Roles for teams", "Team membership", "Clone a user"
                }, rail.RowLabels);
            });
        }

        [Fact]
        public void EveryScreenHasItsOwnIcon()
        {
            var glyphs = FeatureRegistry.CreateAll().Select(f => f.Glyph).ToList();

            Assert.Equal(glyphs.Count, glyphs.Distinct().Count());
        }

        [Fact]
        public void HeadingsAreNotSelectable()
        {
            Sta.Run(() =>
            {
                var rail = Rail();
                string chosen = null;
                rail.ItemSelected += (s, id) => chosen = id;
                var top = rail.ContentTop;

                // Home occupies the first 36px; the LOOK UP heading sits right under it.
                Assert.Null(rail.ItemIdAt(new Point(20, top + 36 + 10)));
                Assert.Equal("a", rail.ItemIdAt(new Point(20, top + 36 + 30 + 10)));
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

                // One wheel step is 36px: Home has scrolled away, the LOOK UP heading is now at the top, and the first
                // item starts 30px below it.
                var top = rail.ContentTop;
                Assert.Null(rail.ItemIdAt(new Point(20, top + 10)));            // the heading
                Assert.Equal("a", rail.ItemIdAt(new Point(20, top + 30 + 10))); // Users and teams, not Home
            });
        }

        [Fact]
        public void SelectingAnItemBelowTheFold_ScrollsItIntoView()
        {
            Sta.Run(() =>
            {
                var rail = Rail(height: 240);

                rail.Select("e"); // the last item, well below the window

                Assert.Equal(rail.MaxScroll, rail.ScrollOffset);
                Assert.Equal("e", rail.ItemIdAt(new Point(20, 239 - 10)));
            });
        }

        [Fact]
        public void Collapsing_ShrinksHeadingsToDividers_AndNeverLeavesTheScrollPastTheEnd()
        {
            Sta.Run(() =>
            {
                var rail = Rail(height: 260);
                rail.Select("e");
                var expandedMax = rail.MaxScroll;

                rail.Expanded = false;

                Assert.True(rail.MaxScroll < expandedMax);
                Assert.True(rail.ScrollOffset <= rail.MaxScroll);
            });
        }

        [Fact]
        public void TheSearchBoxIsOnlyThereWhileTheRailIsExpanded()
        {
            Sta.Run(() =>
            {
                var rail = Rail();
                var box = rail.Controls.OfType<TextBox>().Single();
                Assert.True(box.Visible);
                var expandedTop = rail.ContentTop;

                rail.Expanded = false;

                Assert.False(box.Visible);
                Assert.True(rail.ContentTop < expandedTop);
            });
        }

        [Fact]
        public void ClickingAHeading_FoldsItsGroup_AndAgainUnfoldsIt()
        {
            Sta.Run(() =>
            {
                var rail = Rail();

                Click(rail, Middle(rail, "#LOOK UP"));
                Assert.True(rail.IsCollapsed(FeatureGroup.LookUp));
                Assert.DoesNotContain("Users and teams", rail.VisibleRowLabels);
                Assert.Contains("Two users", rail.VisibleRowLabels);        // other groups are untouched
                Assert.Contains("#LOOK UP", rail.VisibleRowLabels);         // the heading stays, to unfold it again

                Click(rail, Middle(rail, "#LOOK UP"));
                Assert.False(rail.IsCollapsed(FeatureGroup.LookUp));
                Assert.Contains("Users and teams", rail.VisibleRowLabels);
            });
        }

        [Fact]
        public void AFoldedGroupGivesItsRoomBack()
        {
            Sta.Run(() =>
            {
                var rail = Rail(height: 200);
                var before = rail.MaxScroll;

                rail.ToggleGroup(FeatureGroup.LookUp);
                rail.ToggleGroup(FeatureGroup.Change);

                Assert.True(rail.MaxScroll < before);
            });
        }

        [Fact]
        public void OpeningAScreenInAFoldedGroup_UnfoldsTheGroup()
        {
            Sta.Run(() =>
            {
                var rail = Rail();
                rail.ToggleGroup(FeatureGroup.Change);
                Assert.True(rail.IsCollapsed(FeatureGroup.Change));

                rail.Select("d"); // as when a Home card opens it

                Assert.False(rail.IsCollapsed(FeatureGroup.Change));
                Assert.Contains("Roles for users", rail.VisibleRowLabels);
            });
        }

        [Fact]
        public void FoldingIsIgnoredInIconOnlyMode_SoNoIconIsHidden()
        {
            Sta.Run(() =>
            {
                var rail = Rail();
                rail.ToggleGroup(FeatureGroup.LookUp);

                rail.Expanded = false;

                Assert.False(rail.IsCollapsed(FeatureGroup.LookUp));
                Assert.Contains("Users and teams", rail.VisibleRowLabels);
            });
        }

        [Fact]
        public void Searching_ShowsOnlyTheMatchingScreens_WithTheirHeadings()
        {
            Sta.Run(() =>
            {
                var rail = Rail();

                rail.SearchText = "role";

                Assert.Equal(new[] { "#LOOK UP", "Role holders", "#CHANGE", "Roles for users" }, rail.VisibleRowLabels);
            });
        }

        [Fact]
        public void SearchingForAGroupName_ShowsEverythingInIt()
        {
            Sta.Run(() =>
            {
                var rail = Rail();

                rail.SearchText = "compare";

                Assert.Equal(new[] { "#COMPARE", "Two users" }, rail.VisibleRowLabels);
            });
        }

        [Fact]
        public void Searching_LooksInsideFoldedGroups_AndLeavesTheirFoldingAlone()
        {
            Sta.Run(() =>
            {
                var rail = Rail();
                rail.ToggleGroup(FeatureGroup.LookUp);

                rail.SearchText = "holders";
                Assert.Contains("Role holders", rail.VisibleRowLabels);

                rail.SearchText = string.Empty;
                Assert.DoesNotContain("Role holders", rail.VisibleRowLabels);
            });
        }

        [Fact]
        public void ASearchThatMatchesNothing_ShowsNothing()
        {
            Sta.Run(() =>
            {
                var rail = Rail();

                rail.SearchText = "zzz";

                Assert.Empty(rail.VisibleRowLabels);
            });
        }

        [Fact]
        public void TheSearchIsNotCaseSensitive()
        {
            Sta.Run(() =>
            {
                var rail = Rail();

                rail.SearchText = "CLONE";

                Assert.Contains("Clone a user", rail.VisibleRowLabels);
            });
        }

        [Fact]
        public void OpeningAScreenThatTheSearchHides_ClearsTheSearch()
        {
            Sta.Run(() =>
            {
                var rail = Rail();
                rail.SearchText = "clone";

                rail.Select("a");

                Assert.Equal(string.Empty, rail.SearchText);
                Assert.Equal("a", rail.SelectedId);
            });
        }

        [Fact]
        public void BothModesPaintWithoutError()
        {
            Sta.Run(() =>
            {
                var form = new Form { Size = new Size(400, 600) };
                var rail = new NavRail();
                foreach (var f in FeatureRegistry.CreateAll())
                {
                    rail.AddItem(f.Id, f.Glyph, f.Title, f.Group);
                }

                form.Controls.Add(rail);
                form.Show();
                Application.DoEvents();
                using (var bitmap = new Bitmap(rail.Width, rail.Height)) { rail.DrawToBitmap(bitmap, new Rectangle(Point.Empty, rail.Size)); }

                rail.ToggleGroup(FeatureGroup.Review);
                rail.SearchText = "user";
                using (var bitmap = new Bitmap(rail.Width, rail.Height)) { rail.DrawToBitmap(bitmap, new Rectangle(Point.Empty, rail.Size)); }

                rail.Expanded = false;
                using (var bitmap = new Bitmap(rail.Width, rail.Height)) { rail.DrawToBitmap(bitmap, new Rectangle(Point.Empty, rail.Size)); }
                form.Dispose();
            });
        }
    }
}
