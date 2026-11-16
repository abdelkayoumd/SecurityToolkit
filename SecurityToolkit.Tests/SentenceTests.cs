using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using SecurityToolkit.Features;
using SecurityToolkit.Models;
using SecurityToolkit.UI;
using Xunit;
using Label = System.Windows.Forms.Label;

namespace SecurityToolkit.Tests
{
    public class SentenceBarTests
    {
        [Fact]
        public void TheAnswer_StaysAtTheEnd_AndIsHiddenUntilThereIsOne()
        {
            Sta.Run(() =>
            {
                var bar = new SentenceBar();
                bar.Words("Why can");
                var box = bar.Choice(new ComboBox());
                bar.Words("?");

                var last = bar.Controls[bar.Controls.Count - 1];
                Assert.Equal("SentenceAnswer", last.Name);
                Assert.Equal(string.Empty, bar.AnswerText);

                bar.Answer("Yes, at Organization level", SentenceTone.Yes);
                Assert.Equal("Yes, at Organization level", bar.AnswerText);
                Assert.Equal(Theme.Success, last.ForeColor);

                bar.Answer(null);
                Assert.Equal(string.Empty, bar.AnswerText);
            });
        }

        [Fact]
        public void PlainText_ReadsTheSentenceWithItsChoices()
        {
            Sta.Run(() =>
            {
                var bar = new SentenceBar();
                bar.Words("Why can Ada");
                bar.Choice(SentenceChoices.Privilege());
                bar.Words("on Account");
                bar.Words("?");
                bar.Answer("No: no role grants it", SentenceTone.No);

                Assert.Equal("Why can Ada Read on Account? No: no role grants it", bar.PlainText());
            });
        }
    }

    public class PickBoxTests
    {
        private static PickBox Tables()
        {
            var box = new PickBox();
            box.SetItems(new[]
            {
                new TableInfo { LogicalName = "account", DisplayName = "Account" },
                new TableInfo { LogicalName = "contact", DisplayName = "Contact" }
            }, SentenceChoices.TableText);
            return box;
        }

        [Fact]
        public void Enter_PicksTheTypedName_OrLogicalName()
        {
            Sta.Run(() =>
            {
                var box = Tables();
                var picked = 0;
                box.Picked += (s, e) => picked++;
                var keyDown = typeof(Control).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance);

                box.Text = "contact";
                keyDown.Invoke(box, new object[] { new KeyEventArgs(Keys.Enter) });
                Assert.Equal("contact", box.Selected<TableInfo>().LogicalName);

                box.Text = "Account";
                keyDown.Invoke(box, new object[] { new KeyEventArgs(Keys.Enter) });
                Assert.Equal("account", box.Selected<TableInfo>().LogicalName);
                Assert.Equal(2, picked);
            });
        }

        [Fact]
        public void Show_SetsTheItemWithoutRaisingPicked_AndRefillingKeepsIt()
        {
            Sta.Run(() =>
            {
                var box = Tables();
                var picked = 0;
                box.Picked += (s, e) => picked++;

                Assert.True(box.Show(t => ((TableInfo)t).LogicalName == "contact"));
                Assert.False(box.Show(t => ((TableInfo)t).LogicalName == "nope"));
                Assert.Equal(0, picked);

                var contact = box.Selected<TableInfo>();
                box.SetItems(new[] { new TableInfo { LogicalName = "x", DisplayName = "X" }, contact }, SentenceChoices.TableText);
                Assert.Same(contact, box.Selected<TableInfo>());
                Assert.Equal(0, picked);
            });
        }
    }

    public class SentenceScreenTests
    {
        private static string Answer(Control view) => ((Label)Sta.Find(view, c => c.Name == "SentenceAnswer")).Text;

        private static void Pick(PickBox box, string startsWith)
        {
            box.SelectedIndex = Enumerable.Range(0, box.Items.Count).First(i => box.Items[i].ToString().StartsWith(startsWith, StringComparison.Ordinal));
        }

        private static void PickContaining(PickBox box, string part)
        {
            box.SelectedIndex = Enumerable.Range(0, box.Items.Count).First(i => box.Items[i].ToString().Contains(part));
        }

        [Fact]
        public void ExplainAccess_AnswersAtTheEndOfTheQuestion_NamingTheUser()
        {
            MasterDetailHarness.Run(new ExplainAccessFeature(), (view, users, routes, org) =>
            {
                MasterDetailHarness.Select<UserInfo>(users, u => u.FullName == "Ada");
                TeamMembersViewTests.Pump(() => Sta.Find(view, c => c is Label l && l.Text == "Ada") != null);

                PickContaining((PickBox)Sta.Find(view, c => c.Name == "Table"), "(account)");
                TeamMembersViewTests.Pump(() => Answer(view).Length > 0);

                Assert.StartsWith("Yes, at ", Answer(view));
            });
        }

        [Fact]
        public void ExplainAccess_ForSeveralUsers_SaysHowManyCan()
        {
            MasterDetailHarness.Run(new ExplainAccessFeature(), (view, users, routes, org) =>
            {
                MasterDetailHarness.Select<UserInfo>(users, u => u.FullName == "Ada" || u.FullName == "Bob" || u.FullName == "Dee");
                TeamMembersViewTests.Pump(() => Sta.Find(view, c => c is Label l && l.Text == "these 3 users") != null);

                PickContaining((PickBox)Sta.Find(view, c => c.Name == "Table"), "(account)");
                TeamMembersViewTests.Pump(() => Answer(view).Length > 0);

                Assert.Equal("2 of 3 can", Answer(view));
            });
        }

        [Fact]
        public void CompareUsers_PickingBothInTheSentence_SelectsThemOnTheLeft_AndCompares()
        {
            MasterDetailHarness.Run(new CompareUsersFeature(), (view, users, result, org) =>
            {
                Pick((PickBox)Sta.Find(view, c => c.Name == "FirstUser"), "Ada");
                Pick((PickBox)Sta.Find(view, c => c.Name == "SecondUser"), "Bob");

                Assert.Equal(new[] { "Ada", "Bob" }, users.GetSelected<UserInfo>().Select(u => u.FullName).OrderBy(n => n));
                Assert.EndsWith("differences", Answer(view));
            });
        }

        [Fact]
        public void CompareUsers_CtrlClickingTwo_FillsTheSentence()
        {
            MasterDetailHarness.Run(new CompareUsersFeature(), (view, users, result, org) =>
            {
                MasterDetailHarness.Select<UserInfo>(users, u => u.FullName == "Ada" || u.FullName == "Bob");
                TeamMembersViewTests.Pump(() => Answer(view).Length > 0);

                var first = ((PickBox)Sta.Find(view, c => c.Name == "FirstUser")).Selected<UserInfo>();
                var second = ((PickBox)Sta.Find(view, c => c.Name == "SecondUser")).Selected<UserInfo>();
                Assert.Equal(new[] { "Ada", "Bob" }, new[] { first.FullName, second.FullName }.OrderBy(n => n));
            });
        }

        [Fact]
        public void CompareUsers_TheSameUserTwice_AsksForTwoDifferentOnes()
        {
            MasterDetailHarness.Run(new CompareUsersFeature(), (view, users, result, org) =>
            {
                Pick((PickBox)Sta.Find(view, c => c.Name == "FirstUser"), "Ada");
                Pick((PickBox)Sta.Find(view, c => c.Name == "SecondUser"), "Ada");

                Assert.NotNull(Sta.Find(view, c => c is Label l && l.Text == "Pick two different users to compare."));
                Assert.Equal(string.Empty, Answer(view));
            });
        }
    }
}
