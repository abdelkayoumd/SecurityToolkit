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

namespace SecurityToolkit.Tests
{
    public class ApplicationUsersTests
    {
        /// <summary>The sample organization with Dee turned into an application user, Ada given the same treatment when asked.</summary>
        private static void Run(Action<FeatureView, FilterableGrid, FilterableGrid, Org> test, bool adaToo = false)
        {
            Sta.Run(() =>
            {
                var org = new Org();
                org.Dee.ApplicationId = Guid.NewGuid();
                if (adaToo)
                {
                    org.Ada.ApplicationId = Guid.NewGuid();
                }

                var host = OrgFake.HostFor(org);
                var view = new ApplicationUsersFeature().CreateView(host);
                view.FeatureId = "application-users";
                var form = new Form { Size = new Size(1200, 600) };
                form.Controls.Add(view);
                form.Show();
                Application.DoEvents();
                _ = view.Commands;
                view.OnActivated();

                var grids = new List<FilterableGrid>();
                void Collect(Control parent) { foreach (Control c in parent.Controls) { if (c is FilterableGrid g) { grids.Add(g); } else { Collect(c); } } }
                Collect(view);
                var ordered = grids.OrderBy(g => g.PointToScreen(Point.Empty).X).ToArray();
                test(view, ordered[0], ordered[1], org);
                form.Dispose();
            });
        }

        [Fact]
        public void AnApplicationUser_IsNotAPerson_AndCountsAsAServiceAccount()
        {
            var app = new UserInfo { ApplicationId = Guid.NewGuid() };
            Assert.True(app.IsApplicationUser);
            Assert.True(app.IsServiceAccount);
            Assert.False(new UserInfo { ApplicationId = Guid.Empty }.IsApplicationUser);
            Assert.False(new UserInfo().IsApplicationUser);
        }

        [Fact]
        public void OnlyApplicationUsersAreListed_WithTheirRoleCount()
        {
            Run((view, apps, roles, org) =>
            {
                var rows = apps.GetVisible<ApplicationUsersFeature.ApplicationRow>();
                Assert.Equal(new[] { "Dee" }, rows.Select(r => r.Name));
                Assert.Equal(0, rows[0].Roles);
                Assert.NotNull(Sta.Find(view, c => c is Label l && l.Text.StartsWith("1 application user(s), apart from the 2 people")));
            });
        }

        [Fact]
        public void SelectingApplicationUsers_ListsTheirRoles_DirectAndThroughTeams()
        {
            Run((view, apps, roles, org) =>
            {
                MasterDetailHarness.Select<ApplicationUsersFeature.ApplicationRow>(apps, r => r.Name == "Ada");
                TeamMembersViewTests.Pump(() => roles.GetVisible<UserAccessRow>().Any());

                var row = roles.GetVisible<UserAccessRow>().Single();
                Assert.Equal("Admin", row.Role);
                Assert.Equal("Direct role", row.Source);
            }, adaToo: true);
        }

        [Fact]
        public void AnApplicationUser_CanBeOpenedOnAnotherScreen()
        {
            Run((view, apps, roles, org) =>
            {
                var row = apps.GetVisible<ApplicationUsersFeature.ApplicationRow>().Single();
                var items = ((DataView)view).OpenInItems(row).OfType<ToolStripMenuItem>().Select(i => (string)i.Tag).ToList();

                Assert.Contains("user-access-matrix", items);
                Assert.True(Subjects.Matches(Subjects.From(row, org.Graph).Single(), row));
            });
        }

        [Fact]
        public void TheUserQuery_ReadsTheApplicationId()
        {
            var org = new Org();
            var id = Guid.NewGuid();
            org.Bob.ApplicationId = id;

            var users = new SecurityService(OrgFake.For(org)).GetUsers();

            Assert.Equal(id, users.Single(u => u.FullName == "Bob").ApplicationId);
            Assert.Null(users.Single(u => u.FullName == "Ada").ApplicationId);
        }
    }
}
