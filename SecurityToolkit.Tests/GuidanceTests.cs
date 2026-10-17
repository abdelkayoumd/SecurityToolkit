using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Features;
using SecurityToolkit.Models;
using SecurityToolkit.UI;
using Xunit;
using Label = System.Windows.Forms.Label;

namespace SecurityToolkit.Tests
{
    public class FeatureGuideTests
    {
        [Fact]
        public void EveryScreen_ExceptHome_HasGuideText()
        {
            var missing = FeatureRegistry.CreateAll().Where(f => f.Id != "home" && FeatureGuide.Find(f.Id) == null).Select(f => f.Id).ToList();

            Assert.True(missing.Count == 0, "No guide text for: " + string.Join(", ", missing));
        }

        [Fact]
        public void EveryGuideEntry_BelongsToARealScreen()
        {
            var ids = new HashSet<string>(FeatureRegistry.CreateAll().Select(f => f.Id));

            Assert.All(FeatureGuide.All, e => Assert.Contains(e.FeatureId, ids));
        }

        [Fact]
        public void EveryEntry_HasAQuestionASummaryAndSteps_AndTheQuestionsAreDistinct()
        {
            Assert.All(FeatureGuide.All, e =>
            {
                Assert.False(string.IsNullOrWhiteSpace(e.Question));
                Assert.False(string.IsNullOrWhiteSpace(e.Summary));
                Assert.NotEmpty(e.Steps);
                Assert.All(e.Steps, s => Assert.False(string.IsNullOrWhiteSpace(s)));
            });
            Assert.Equal(FeatureGuide.All.Count, FeatureGuide.All.Select(e => e.Question).Distinct().Count());
            Assert.Equal(FeatureGuide.All.Count, FeatureGuide.All.Select(e => e.FeatureId).Distinct().Count());
        }

        [Fact]
        public void TheScreensThatChangeSecurity_AreListedUnderChange_AndSayEveryChangeIsReviewed()
        {
            var changing = FeatureRegistry.CreateAll().Where(f => f.Group == FeatureGroup.Change).Select(f => f.Id).ToList();

            foreach (var id in changing)
            {
                var entry = FeatureGuide.Find(id);
                Assert.Equal(GuideSection.Change, entry.Section);
                Assert.Contains(entry.Steps, s => s.Contains("review"));
            }
        }

        [Fact]
        public void AnUnknownScreen_HasNoGuide()
        {
            Assert.Null(FeatureGuide.Find("nope"));
            Assert.Null(FeatureGuide.Find(null));
        }
    }

    public class HelpStripTests
    {
        private static void WithView(string featureId, Action<FeatureView> test)
        {
            Sta.Run(() =>
            {
                var feature = FeatureRegistry.CreateAll().Single(f => f.Id == featureId);
                var view = feature.CreateView(new TestHost());
                var form = new Form { Size = new Size(1100, 500) };
                form.Controls.Add(view);
                form.Show();
                Application.DoEvents();
                test(view);
                form.Dispose();
            });
        }

        private static Control Named(FeatureView view, string name) => Sta.Find(view, c => c.Name == name);

        [Fact]
        public void AScreenShowsWhatItDoes_InOneLine_UnderTheCommandBar()
        {
            WithView("compare-users", view =>
            {
                view.FeatureId = "compare-users";

                Assert.Equal(FeatureGuide.Find("compare-users").Summary, Named(view, "HelpSummary").Text);
            });
        }

        [Fact]
        public void TheStepsAreHidden_UntilHowItWorksIsClicked_ThenCanBeHiddenAgain()
        {
            WithView("compare-users", view =>
            {
                view.FeatureId = "compare-users";
                var details = Named(view, "HelpDetails");
                var toggle = (LinkLabel)Named(view, "HelpToggle");
                Assert.False(details.Visible);

                typeof(LinkLabel).GetMethod("OnLinkClicked", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .Invoke(toggle, new object[] { new LinkLabelLinkClickedEventArgs(new LinkLabel.Link()) });
                Assert.True(details.Visible);
                Assert.StartsWith("1.", details.Text);
                Assert.Equal("Hide", toggle.Text);

                typeof(LinkLabel).GetMethod("OnLinkClicked", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .Invoke(toggle, new object[] { new LinkLabelLinkClickedEventArgs(new LinkLabel.Link()) });
                Assert.False(details.Visible);
            });
        }

        [Fact]
        public void TheStepsAreNumbered_OneLineEach()
        {
            WithView("explain-access", view =>
            {
                view.FeatureId = "explain-access";

                var lines = Named(view, "HelpDetails").Text.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                Assert.Equal(FeatureGuide.Find("explain-access").Steps.Length, lines.Length);
                Assert.StartsWith("1.", lines[0]);
            });
        }

        [Fact]
        public void WithoutAFeatureId_ThereIsNoHelpStrip()
        {
            WithView("compare-users", view =>
            {
                Assert.Null(Named(view, "HelpBar"));
            });
        }

        [Fact]
        public void SettingTheFeatureIdTwice_DoesNotAddASecondStrip()
        {
            WithView("compare-users", view =>
            {
                view.FeatureId = "compare-users";
                view.FeatureId = "compare-users";

                Assert.Single(view.Controls.Cast<Control>().Where(c => c.Name == "HelpBar"));
            });
        }
    }

    public class HomeCardsTests
    {
        private static void WithHome(Action<FeatureView, TestHost> test)
        {
            Sta.Run(() =>
            {
                var host = new TestHost();
                var view = new HomeFeature().CreateView(host);
                var form = new Form { Size = new Size(1200, 700) };
                form.Controls.Add(view);
                form.Show();
                Application.DoEvents();
                test(view, host);
                form.Dispose();
            });
        }

        private static List<Control> Cards(Control root)
        {
            var found = new List<Control>();
            void Walk(Control c) { foreach (Control child in c.Controls) { if (child.Name.StartsWith("Card:")) { found.Add(child); } else { Walk(child); } } }
            Walk(root);
            return found;
        }

        [Fact]
        public void EveryScreenWithGuideText_GetsACard()
        {
            WithHome((view, host) =>
            {
                var ids = Cards(view).Select(c => (string)c.Tag).OrderBy(i => i).ToList();

                Assert.Equal(FeatureGuide.All.Select(e => e.FeatureId).OrderBy(i => i), ids);
            });
        }

        [Fact]
        public void ClickingACard_OpensItsScreen()
        {
            WithHome((view, host) =>
            {
                var card = Cards(view).Single(c => (string)c.Tag == "explain-access");

                typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(card, new object[] { EventArgs.Empty });

                Assert.Equal(new[] { "explain-access" }, host.Navigated);
            });
        }

        [Fact]
        public void PressingEnterOnACard_OpensItsScreen()
        {
            WithHome((view, host) =>
            {
                var card = Cards(view).Single(c => (string)c.Tag == "compare-roles");

                typeof(Control).GetMethod("OnKeyDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .Invoke(card, new object[] { new KeyEventArgs(Keys.Enter) });

                Assert.Equal(new[] { "compare-roles" }, host.Navigated);
            });
        }

        [Fact]
        public void TheCardsAreGroupedUnderHeadings_AndTheChangeGroupSaysItIsReviewed()
        {
            WithHome((view, host) =>
            {
                var headings = new List<string>();
                var flow = (FlowLayoutPanel)Sta.Find(view, c => c.Name == "HomeCards");
                headings.AddRange(flow.Controls.OfType<Label>().Select(l => l.Text));

                Assert.Contains("LOOK UP", headings);
                Assert.Contains(headings, h => h.StartsWith("CHANGE") && h.Contains("REVIEWED"));
            });
        }
    }

    public class EmptyMessageTests
    {
        private static string Message(FilterableGrid grid)
        {
            var label = (Label)Sta.Find(grid, c => c is Label l && l.Visible && l.Parent is DataGridView);
            return label?.Text;
        }

        private static FilterableGrid Shown(Form form)
        {
            var grid = new FilterableGrid();
            form.Controls.Add(grid);
            form.Show();
            Application.DoEvents();
            return grid;
        }

        [Fact]
        public void AnEmptyGrid_SaysWhatToDo()
        {
            Sta.Run(() =>
            {
                var form = new Form { Size = new Size(600, 400) };
                var grid = Shown(form);
                grid.EmptyMessage = "Select a user first.";

                grid.SetData(new List<UserInfo>(), "FullName");

                Assert.Equal("Select a user first.", Message(grid));
                form.Dispose();
            });
        }

        [Fact]
        public void TheMessageGoesAwayWhenThereAreRows()
        {
            Sta.Run(() =>
            {
                var form = new Form { Size = new Size(600, 400) };
                var grid = Shown(form);
                grid.EmptyMessage = "Select a user first.";

                grid.SetData(new List<UserInfo> { new UserInfo { FullName = "Ada" } }, "FullName");

                Assert.Null(Message(grid));
                form.Dispose();
            });
        }

        [Fact]
        public void WhenASearchMatchesNothing_ItSaysSoInsteadOfRepeatingTheInstruction()
        {
            Sta.Run(() =>
            {
                var form = new Form { Size = new Size(600, 400) };
                var grid = Shown(form);
                grid.EmptyMessage = "Select a user first.";
                grid.SetData(new List<UserInfo> { new UserInfo { FullName = "Ada" } }, "FullName");

                var box = (TextBox)Sta.Find(grid, c => c is TextBox);
                box.Text = "zzz";

                Assert.Contains("No records match", Message(grid));
                form.Dispose();
            });
        }

        [Fact]
        public void TheProviderIsAskedEachTime_SoTheMessageFollowsTheState()
        {
            Sta.Run(() =>
            {
                var form = new Form { Size = new Size(600, 400) };
                var grid = Shown(form);
                var text = "first";
                grid.EmptyMessageProvider = () => text;
                grid.SetData(new List<UserInfo>(), "FullName");
                Assert.Equal("first", Message(grid));

                text = "second";
                grid.RefreshEmptyMessage();

                Assert.Equal("second", Message(grid));
                form.Dispose();
            });
        }
    }

    public class ResultsAreaMessageTests
    {
        [Fact]
        public void BeforeAnythingIsSelected_TheResultsAreaSaysWhatToSelect()
        {
            MasterDetailHarness.Run(new CompareUsersFeature(), (view, master, detail, org) =>
            {
                var label = (Label)Sta.Find(detail, c => c is Label l && l.Visible && l.Parent is DataGridView);

                Assert.Contains("Select two users", label.Text);
            });
        }

        [Fact]
        public void WithNoConnection_ItAsksToConnect()
        {
            Sta.Run(() =>
            {
                var view = new CompareUsersFeature().CreateView(new DisconnectedHost());
                var form = new Form { Size = new Size(1100, 500) };
                form.Controls.Add(view);
                form.Show();
                Application.DoEvents();
                _ = view.Commands;
                view.OnActivated();

                var label = (Label)Sta.Find(view, c => c is Label l && l.Visible && l.Parent is DataGridView && l.Text.Contains("Connect"));

                Assert.NotNull(label);
                form.Dispose();
            });
        }
    }
}
