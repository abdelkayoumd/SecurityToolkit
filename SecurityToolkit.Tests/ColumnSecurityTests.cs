using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Features;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using SecurityToolkit.UI;
using Xunit;

namespace SecurityToolkit.Tests
{
    public class ColumnSecurityTests
    {
        private static (ColumnSecurity Columns, Org Org) Load()
        {
            var org = new Org();
            return (new SecurityService(OrgFake.For(org)).GetColumnSecurity(), org);
        }

        [Fact]
        public void TheProfiles_AreListed_WithTheirMembersAndColumnsCounted()
        {
            var (columns, _) = Load();

            Assert.Equal(new[] { "Credit managers", "Salary readers", "Unused profile" }, columns.Profiles.Select(p => p.Name));
            var salary = columns.Profiles.Single(p => p.Name == "Salary readers");
            Assert.Equal((1, 1, 1), (salary.Users, salary.Teams, salary.Columns));
            Assert.Equal((0, 0, 0), (columns.Profiles.Last().Users, columns.Profiles.Last().Teams, columns.Profiles.Last().Columns));
        }

        [Fact]
        public void AProfilesColumns_SayWhatIsAllowed()
        {
            var (columns, _) = Load();
            var credit = columns.Profiles.Single(p => p.Name == "Credit managers");

            var rows = columns.ColumnsOf(credit.Id);

            Assert.Equal(new[] { "account.creditlimit", "contact.salary" }, rows.Select(r => r.Table + "." + r.Column));
            Assert.Equal(("Yes", "Yes", "No"), (rows[0].Read, rows[0].Update, rows[0].Create));
            Assert.Equal(("No", "Yes"), (rows[1].Read, rows[1].Update));
        }

        [Fact]
        public void AProfilesMembers_AreItsTeams_TheirMembers_AndTheUsersWhoHoldItDirectly()
        {
            var (columns, org) = Load();
            var salary = columns.Profiles.Single(p => p.Name == "Salary readers");

            var members = columns.MembersOf(salary.Id, org.Graph);

            Assert.Equal(new[] { "Reviewers|Team|Direct", "Ada|User|Direct", "Bob|User|Team: Reviewers", "Cy|User|Team: Reviewers" },
                members.Select(m => $"{m.Member}|{m.Type}|{m.Via}"));
        }

        [Fact]
        public void AUsersColumns_AllowWhatAnyOfTheirProfilesAllows_AndSayThrough()
        {
            var (columns, org) = Load();

            var ada = columns.ColumnsOfUser(org.Ada, org.Graph);
            var salary = ada.Single(r => r.Column == "salary");
            Assert.Equal(("Yes", "Yes"), (salary.Read, salary.Update)); // read from one profile, update from the other
            Assert.Equal("Credit managers, Salary readers", string.Join(", ", salary.Through.Split(new[] { ", " }, System.StringSplitOptions.None).OrderBy(s => s)));

            var bob = Assert.Single(columns.ColumnsOfUser(org.Bob, org.Graph));
            Assert.Equal("Salary readers (Team: Reviewers)", bob.Through);
            Assert.Empty(columns.ColumnsOfUser(org.Dee, org.Graph));
        }

        [Fact]
        public void TheScreen_ShowsAProfile_ThenAUser()
        {
            MasterDetailHarness.Run(new ColumnSecurityFeature(), (view, master, detail, org) =>
            {
                MasterDetailHarness.Select<ColumnProfile>(master, p => p.Name == "Salary readers");
                TeamMembersViewTests.Pump(() => detail.GetVisible<ColumnAccessRow>().Any());
                var tabs = (TabStrip)Sta.Find(view, c => c.Name == "ColumnSecurityTabs");
                Assert.Equal(new[] { "Secured columns (1)", "Members (4)" }, tabs.Tabs);

                view.Commands.OfType<ComboBox>().Single(c => c.Name == "ShowBy").SelectedIndex = 1;
                MasterDetailHarness.Select<UserInfo>(master, u => u.FullName == "Bob");
                TeamMembersViewTests.Pump(() => detail.GetVisible<ColumnAccessRow>().Any());

                Assert.Equal("salary", detail.GetVisible<ColumnAccessRow>().Single().Column);
                Assert.False(tabs.Visible);
                Assert.NotNull(Sta.Find(view, c => c is Label l && l.Text.StartsWith("Bob: 1 profile(s), 1 secured column(s)")));
            });
        }

        [Fact]
        public void OpeningAUserHere_SwitchesTheListToUsers()
        {
            MasterDetailHarness.Run(new ColumnSecurityFeature(), (view, master, detail, org) =>
            {
                view.FeatureId = "column-security";
                view.ShowSubject(new Subject { Kind = SubjectKind.User, Id = org.Bob.Id, Name = "Bob" });

                Assert.Equal(new[] { "Bob" }, master.GetSelected<UserInfo>().Select(u => u.FullName));
            });
        }
    }
}
