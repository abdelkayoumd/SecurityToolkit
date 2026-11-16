using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using SecurityToolkit.UI;
using Xunit;

namespace SecurityToolkit.Tests
{
    public class GlobalSearchTests
    {
        private readonly Org _o = new Org();

        private static readonly IReadOnlyList<TableInfo> Tables = new[]
        {
            new TableInfo { LogicalName = "account", DisplayName = "Account" },
            new TableInfo { LogicalName = "salesorder", DisplayName = "Order" }
        };

        [Fact]
        public void FindsUsersTeamsRolesAndTables()
        {
            Assert.Equal(new[] { SubjectKind.User }, GlobalSearch.Find("bob", _o.Graph, Tables).Select(h => h.Subject.Kind));
            Assert.Equal(new[] { SubjectKind.Team }, GlobalSearch.Find("review", _o.Graph, Tables).Select(h => h.Subject.Kind));
            Assert.Equal(new[] { SubjectKind.Role }, GlobalSearch.Find("audit", _o.Graph, Tables).Select(h => h.Subject.Kind));
            Assert.Equal(new[] { SubjectKind.Table }, GlobalSearch.Find("acc", _o.Graph, Tables).Select(h => h.Subject.Kind));
        }

        [Fact]
        public void ARoleIsFoundOnce_HoweverManyBusinessUnitsItIsIn()
        {
            var hit = GlobalSearch.Find("seller", _o.Graph, Tables).Single();
            Assert.Equal(_o.SellerRoot.Id, hit.Subject.Id);
            Assert.Equal("Security role · 2 business units", hit.Detail);
        }

        [Fact]
        public void LoginsAndLogicalNamesMatchToo()
        {
            Assert.Equal("Bob", GlobalSearch.Find("bob@x", _o.Graph, Tables).Single().Subject.Name);
            Assert.Equal("Order", GlobalSearch.Find("salesorder", _o.Graph, Tables).Single().Subject.Name);
        }

        [Fact]
        public void BestMatchesComeFirst_ExactThenStartThenWordThenAnywhere()
        {
            Assert.Equal(0, GlobalSearch.Best("sales", "Sales"));
            Assert.Equal(1, GlobalSearch.Best("sales", "Salesperson"));
            Assert.Equal(2, GlobalSearch.Best("sales", "EMEA Sales Manager"));
            Assert.Equal(3, GlobalSearch.Best("sales", "Presales"));
            Assert.Equal(-1, GlobalSearch.Best("sales", "Marketing"));
            Assert.Equal(1, GlobalSearch.Best("sales", "Presales", "sales@x"));
        }

        [Fact]
        public void CaseAndAccentsAreIgnored()
        {
            Assert.Equal(0, GlobalSearch.Best("helene", "Hélène"));
            Assert.Equal(1, GlobalSearch.Best("ZOË", "zoe smith"));
        }

        [Fact]
        public void DisabledUsersComeAfterActiveOnes()
        {
            var active = new UserInfo { Id = Guid.NewGuid(), FullName = "Sam B", DomainName = "b@x" };
            var disabled = new UserInfo { Id = Guid.NewGuid(), FullName = "Sam A", DomainName = "a@x", IsDisabled = true };
            var graph = new SecurityGraph(new[] { disabled, active }, new RoleInfo[0], new TeamInfo[0], new BusinessUnitInfo[0],
                new UserTeamLink[0], new UserRoleLink[0], new TeamRoleLink[0]);

            var hits = GlobalSearch.Find("sam", graph, null);
            Assert.Equal(new[] { "Sam B", "Sam A" }, hits.Select(h => h.Subject.Name));
            Assert.EndsWith("disabled", hits[1].Detail);
        }

        [Fact]
        public void NothingTyped_NothingFound()
        {
            Assert.Empty(GlobalSearch.Find("  ", _o.Graph, Tables));
            Assert.Empty(GlobalSearch.Find(null, _o.Graph, Tables));
        }

        [Fact]
        public void ResultsAreCapped()
        {
            var users = Enumerable.Range(0, 50).Select(i => new UserInfo { Id = Guid.NewGuid(), FullName = "User " + i }).ToList();
            var graph = new SecurityGraph(users, new RoleInfo[0], new TeamInfo[0], new BusinessUnitInfo[0], new UserTeamLink[0], new UserRoleLink[0], new TeamRoleLink[0]);
            Assert.Equal(30, GlobalSearch.Find("user", graph, null).Count);
        }
    }

    public class GlobalSearchBoxTests
    {
        private static void Run(Action<GlobalSearchBox, Org, List<KeyValuePair<string, Subject>>> test, bool loaded = true)
        {
            Sta.Run(() =>
            {
                var org = new Org();
                var opened = new List<KeyValuePair<string, Subject>>();
                var form = new Form { Size = new Size(900, 500) };
                var box = new GlobalSearchBox
                {
                    Location = new Point(200, 10),
                    Source = () => Tuple.Create(loaded ? org.Graph : null, (IReadOnlyList<TableInfo>)new TableInfo[0]),
                    Open = (id, subject) => opened.Add(new KeyValuePair<string, Subject>(id, subject))
                };
                box.OverlayHost = form;
                form.Controls.Add(box);
                form.Show();
                Application.DoEvents();
                test(box, org, opened);
                form.Dispose();
            });
        }

        [Fact]
        public void Typing_ListsTheMatchesUnderTheBox()
        {
            Run((box, org, opened) =>
            {
                box.Query = "bob";
                box.Search();

                Assert.True(box.ListShown);
                Assert.Equal(new[] { "Bob" }, box.Hits.Select(h => h.Subject.Name));
            });
        }

        [Fact]
        public void AMatchOffersEveryScreenForItsKind_AndOpensTheChosenOne()
        {
            Run((box, org, opened) =>
            {
                box.Query = "bob";
                box.Search();
                var menu = box.BuildMenu(box.Hits[0]);

                var items = menu.Items.OfType<ToolStripMenuItem>().ToList();
                Assert.Equal(SecurityToolkit.Features.SubjectActions.For(SubjectKind.User, null).Select(a => a.Text), items.Select(i => i.Text));

                items.Single(i => (string)i.Tag == "explain-access").PerformClick();
                Assert.Equal("explain-access", opened.Single().Key);
                Assert.Equal(org.Bob.Id, opened.Single().Value.Id);
                Assert.False(box.ListShown);
            });
        }

        [Fact]
        public void BeforeTheDataIsLoaded_TheFirstSearchAsksForIt_ThenShowsTheMatches()
        {
            var loaded = false;
            Sta.Run(() =>
            {
                var org = new Org();
                var asked = 0;
                var form = new Form();
                var box = new GlobalSearchBox
                {
                    Source = () => Tuple.Create(loaded ? org.Graph : null, (IReadOnlyList<TableInfo>)null)
                };
                box.RequestData = done =>
                {
                    asked++;
                    loaded = true;
                    done();
                };
                box.OverlayHost = form;
                form.Controls.Add(box);
                form.Show();

                box.Query = "ada";
                box.Search();
                box.Search();

                Assert.Equal(1, asked);
                Assert.Equal(new[] { "Ada" }, box.Hits.Select(h => h.Subject.Name));
                form.Dispose();
            });
        }

        [Fact]
        public void ClearingTheText_ClosesTheList()
        {
            Run((box, org, opened) =>
            {
                box.Query = "a";
                box.Search();
                Assert.True(box.ListShown);

                box.Query = string.Empty;
                box.Search();
                Assert.False(box.ListShown);
            });
        }
    }
}
