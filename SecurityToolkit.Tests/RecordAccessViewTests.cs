using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using SecurityToolkit.Features;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using SecurityToolkit.UI;
using Xunit;
using Label = System.Windows.Forms.Label;

namespace SecurityToolkit.Tests
{
    public class RecordAccessViewTests
    {
        private const int Everything = 1 | 2 | 4 | 16 | 65536 | 262144 | 524288;

        private sealed class Scene
        {
            public Org Org;
            public Guid Record = Guid.NewGuid();
            public FakeOrganizationService Fake;
            public Dictionary<Guid, int> PlatformMasks = new Dictionary<Guid, int>();
            public List<Entity> Accounts = new List<Entity>();
            public List<PrincipalAccess> Shares = new List<PrincipalAccess>();
        }

        private static void Set(object target, string property, object value)
        {
            target.GetType().GetProperty(property).GetSetMethod(true).Invoke(target, new[] { value });
        }

        private static Scene BuildScene()
        {
            var scene = new Scene { Org = new Org() };
            var org = scene.Org;
            var fake = OrgFake.Serve(org.Graph, org);
            var served = fake.OnRetrieveMultiple;
            var serveRest = fake.OnExecute;
            scene.Fake = fake;

            // A user-owned "account" table whose record "Contoso" is owned by Dee, in the Sales business unit.
            scene.Accounts.Add(new Entity("account", scene.Record) { ["name"] = "Contoso" });
            fake.OnRetrieveMultiple = q => ((QueryExpression)q).EntityName == "account" ? new EntityCollection(scene.Accounts) : served(q);
            fake.OnRetrieve = (table, id, columns) => new Entity(table, id)
            {
                ["name"] = "Contoso",
                ["ownerid"] = new EntityReference("systemuser", org.Dee.Id) { Name = "Dee" },
                ["owningbusinessunit"] = new EntityReference("businessunit", org.Sales) { Name = "Sales" }
            };
            fake.OnExecute = request =>
            {
                switch (request)
                {
                    case RetrieveEntityRequest _:
                        var meta = new EntityMetadata { LogicalName = "account" };
                        Set(meta, "OwnershipType", (OwnershipTypes?)OwnershipTypes.UserOwned);
                        Set(meta, "PrimaryNameAttribute", "name");
                        var entity = new RetrieveEntityResponse();
                        entity.Results["EntityMetadata"] = meta;
                        return entity;
                    case RetrievePrincipalAccessRequest access:
                        var response = new RetrievePrincipalAccessResponse();
                        response.Results["AccessRights"] = (AccessRights)(scene.PlatformMasks.TryGetValue(access.Principal.Id, out var mask) ? mask : 0);
                        return response;
                    case RetrieveSharedPrincipalsAndAccessRequest _:
                        var shared = new RetrieveSharedPrincipalsAndAccessResponse();
                        shared.Results["PrincipalAccesses"] = scene.Shares.ToArray();
                        return shared;
                    default:
                        return serveRest(request);
                }
            };
            return scene;
        }

        private static void Run(Action<Scene, FeatureView, FilterableGrid, FilterableGrid, TestHost> test)
        {
            Sta.Run(() =>
            {
                var scene = BuildScene();
                var host = new TestHost { Security = new SecurityService(scene.Fake) };
                var view = new RecordAccessFeature().CreateView(host);
                var form = new Form { Size = new Size(1400, 600) };
                form.Controls.Add(view);
                form.Show();
                Application.DoEvents();
                _ = view.Commands;
                view.OnActivated();

                var grids = new List<FilterableGrid>();
                Collect(view, grids);
                var master = grids.OrderBy(g => g.PointToScreen(Point.Empty).X).First();
                var access = grids.First(g => g.EmptyMessageProvider != null && !ReferenceEquals(g, master));
                test(scene, view, master, access, host);
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

        private static T Named<T>(FeatureView view, string name) where T : Control => (T)Sta.Find(view, c => c.Name == name);

        private static ComboBox Table(FeatureView view) => Named<ComboBox>(view, "RecordTable");

        private static void PickTable(FeatureView view, string logical)
        {
            var combo = Table(view);
            combo.SelectedIndex = Enumerable.Range(0, combo.Items.Count).First(i => combo.Items[i].ToString().Contains("(" + logical + ")"));
        }

        private static void Find(FeatureView view, string text)
        {
            Named<TextBox>(view, "RecordText").Text = text;
            Named<Button>(view, "FindRecord").PerformClick();
        }

        private static string Info(FeatureView view, string containing)
        {
            return ((Label)Sta.Find(view, c => c is Label l && l.Text.Contains(containing)))?.Text;
        }

        private static string Link(Scene scene) => $"https://org.crm.dynamics.com/main.aspx?pagetype=entityrecord&etn=account&id={scene.Record}";

        [Fact]
        public void PastingARecordLink_SelectsItsTable_AndReadsTheRecord()
        {
            Run((scene, view, users, access, host) =>
            {
                Find(view, Link(scene));
                TeamMembersViewTests.Pump(() => Info(view, "Contoso (account)") != null);

                Assert.Contains("(account)", Table(view).SelectedItem.ToString());
                var info = Info(view, "Contoso");
                Assert.Contains("Owner: Dee (user)", info);
                Assert.Contains("business unit Sales", info);
            });
        }

        [Fact]
        public void SelectingAUser_ShowsTheirAnswerForEveryRight_WithTheReasons()
        {
            Run((scene, view, users, access, host) =>
            {
                scene.PlatformMasks[scene.Org.Ada.Id] = Everything;
                Find(view, Link(scene));
                TeamMembersViewTests.Pump(() => Info(view, "Contoso (account)") != null);

                MasterDetailHarness.Select<PrincipalRow>(users, u => u.Name == "Ada");
                TeamMembersViewTests.Pump(() => access.GetVisible<RecordAccessRow>().Count == 7);

                var rows = access.GetVisible<RecordAccessRow>();
                Assert.All(rows, r => Assert.Equal("Yes", r.Result));
                Assert.Contains("Admin", rows[0].Why);
                Assert.NotNull(Info(view, "Ada has 7 of 7 rights"));
            });
        }

        [Fact]
        public void AUserTheRecordIsOutOfReachFor_GetsNoWithTheGap()
        {
            Run((scene, view, users, access, host) =>
            {
                scene.PlatformMasks[scene.Org.Dee.Id] = 0;
                Find(view, Link(scene));
                TeamMembersViewTests.Pump(() => Info(view, "Contoso (account)") != null);

                MasterDetailHarness.Select<PrincipalRow>(users, u => u.Name == "Dee");
                TeamMembersViewTests.Pump(() => access.GetVisible<RecordAccessRow>().Count == 7);

                Assert.All(access.GetVisible<RecordAccessRow>(), r => Assert.Equal("No", r.Result));
                Assert.Contains("No role Dee holds grants Read", access.GetVisible<RecordAccessRow>()[0].Why);
            });
        }

        [Fact]
        public void TheSharingTab_ListsWhoTheRecordIsSharedWith_AndCountsThem()
        {
            Run((scene, view, users, access, host) =>
            {
                scene.Shares.Add(new PrincipalAccess { Principal = new EntityReference("team", scene.Org.Reviewers.Id) { Name = "Reviewers" }, AccessMask = AccessRights.ReadAccess | AccessRights.WriteAccess });
                Find(view, Link(scene));
                TeamMembersViewTests.Pump(() => Info(view, "Contoso (account)") != null);

                var tabs = Named<TabStrip>(view, "RecordAccessTabs");
                Assert.Equal("Shared with (1)", tabs.Tabs[1]);
                var shares = (FilterableGrid)Sta.Find(view, c => c is FilterableGrid g && g.EmptyMessage != null && g.EmptyMessage.Contains("sharing"));
                var row = Assert.Single(shares.GetVisible<ShareRow>());
                Assert.Equal("Reviewers", row.Principal);
                Assert.Equal("Read, Write", row.Rights);
            });
        }

        [Fact]
        public void ASharedRecord_ExplainsAUsersAccessThroughTheirTeam()
        {
            Run((scene, view, users, access, host) =>
            {
                scene.Shares.Add(new PrincipalAccess { Principal = new EntityReference("team", scene.Org.Reviewers.Id) { Name = "Reviewers" }, AccessMask = AccessRights.DeleteAccess });
                scene.PlatformMasks[scene.Org.Bob.Id] = 65536;
                Find(view, Link(scene));
                TeamMembersViewTests.Pump(() => Info(view, "Contoso (account)") != null);

                MasterDetailHarness.Select<PrincipalRow>(users, u => u.Name == "Bob");
                TeamMembersViewTests.Pump(() => access.GetVisible<RecordAccessRow>().Count == 7);

                var delete = access.GetVisible<RecordAccessRow>().Single(r => r.Right == "Delete");
                Assert.Equal("Yes", delete.Result);
                Assert.Contains("Reviewers", delete.Why);
            });
        }

        [Fact]
        public void TypingANameSearchesTheTable_AndOneMatchIsPickedForYou()
        {
            Run((scene, view, users, access, host) =>
            {
                PickTable(view, "account");

                Find(view, "Conto");
                TeamMembersViewTests.Pump(() => Info(view, "Contoso (account)") != null);

                var matches = Named<ComboBox>(view, "RecordMatches");
                Assert.True(matches.Visible);
                Assert.Equal("Contoso", matches.SelectedItem.ToString());
            });
        }

        [Fact]
        public void SeveralMatches_AreListedToPickFrom()
        {
            Run((scene, view, users, access, host) =>
            {
                scene.Accounts.Add(new Entity("account", Guid.NewGuid()) { ["name"] = "Contoso Ltd" });
                PickTable(view, "account");

                Find(view, "Conto");
                TeamMembersViewTests.Pump(() => Info(view, "2 records match") != null);

                Assert.Equal(2, Named<ComboBox>(view, "RecordMatches").Items.Count);
            });
        }

        [Fact]
        public void ANameWithoutATable_AsksForTheTable()
        {
            Run((scene, view, users, access, host) =>
            {
                Find(view, "Contoso");

                Assert.NotNull(Info(view, "Choose the table to search in first"));
            });
        }

        [Fact]
        public void ABareIdWithoutATable_AsksForTheTableOrTheWholeLink()
        {
            Run((scene, view, users, access, host) =>
            {
                Find(view, scene.Record.ToString());

                Assert.NotNull(Info(view, "does not say which table"));
            });
        }

        [Fact]
        public void ANameNothingMatches_SaysSo()
        {
            Run((scene, view, users, access, host) =>
            {
                scene.Accounts.Clear();
                PickTable(view, "account");

                Find(view, "zzz");

                Assert.NotNull(Info(view, "has a name containing \"zzz\""));
            });
        }

        [Fact]
        public void CheckingATeam_ListsTheTeams_AndAnswersForTheSelectedOne()
        {
            Run((scene, view, users, access, host) =>
            {
                Find(view, Link(scene));
                TeamMembersViewTests.Pump(() => Info(view, "Contoso (account)") != null);

                view.Commands.OfType<ComboBox>().Single(c => c.Name == "PrincipalType").SelectedIndex = 1;
                Assert.Contains(users.GetVisible<PrincipalRow>(), t => t.Name == "Reviewers" && t.IsTeam);

                MasterDetailHarness.Select<PrincipalRow>(users, t => t.Name == "Reviewers");
                TeamMembersViewTests.Pump(() => access.GetVisible<RecordAccessRow>().Count == 7);

                var read = access.GetVisible<RecordAccessRow>()[0];
                Assert.NotNull(read.Why);
                Assert.NotNull(Info(view, "Reviewers has"));
            });
        }

        [Fact]
        public void TheFullReason_ShowsWhenTheMouseRestsOnIt()
        {
            Run((scene, view, users, access, host) =>
            {
                Find(view, Link(scene));
                TeamMembersViewTests.Pump(() => Info(view, "Contoso (account)") != null);
                MasterDetailHarness.Select<PrincipalRow>(users, u => u.Name == "Dee");
                TeamMembersViewTests.Pump(() => access.GetVisible<RecordAccessRow>().Count == 7);

                var data = (DataGridView)Sta.Find(access, c => c is DataGridView);
                var column = data.Columns.Cast<DataGridViewColumn>().First(c => c.DataPropertyName == "Why");
                var args = (DataGridViewCellToolTipTextNeededEventArgs)Activator.CreateInstance(typeof(DataGridViewCellToolTipTextNeededEventArgs),
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, new object[] { column.Index, 0, string.Empty }, null);
                typeof(DataGridView).GetMethod("OnCellToolTipTextNeeded", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null,
                    new[] { typeof(DataGridViewCellToolTipTextNeededEventArgs) }, null).Invoke(data, new object[] { args });

                Assert.Equal(access.GetVisible<RecordAccessRow>()[0].Why, args.ToolTipText);
            });
        }

        [Fact]
        public void OnlyOneUserOrTeamCanBeSelected()
        {
            Run((scene, view, users, access, host) =>
            {
                Assert.False(users.MultiSelect);
            });
        }

        [Fact]
        public void BeforeARecordIsChosen_TheResultsAreaSaysHowToChooseOne()
        {
            Run((scene, view, users, access, host) =>
            {
                var message = (Label)Sta.Find(access, c => c is Label l && l.Visible && l.Parent is DataGridView);

                Assert.Contains("Pick a record above", message.Text);
            });
        }

        [Fact]
        public void ThePlatformCannotAnswer_TheScreenSaysSoAndFallsBackToTheRoles()
        {
            Run((scene, view, users, access, host) =>
            {
                var previous = scene.Fake.OnExecute;
                scene.Fake.OnExecute = request =>
                {
                    if (request is RetrievePrincipalAccessRequest) { throw new InvalidOperationException("no permission"); }
                    return previous(request);
                };
                Find(view, Link(scene));
                TeamMembersViewTests.Pump(() => Info(view, "Contoso (account)") != null);

                MasterDetailHarness.Select<PrincipalRow>(users, u => u.Name == "Ada");
                TeamMembersViewTests.Pump(() => access.GetVisible<RecordAccessRow>().Count == 7);

                Assert.NotNull(Info(view, "could not be read (no permission)"));
                Assert.All(access.GetVisible<RecordAccessRow>(), r => Assert.Equal("Yes", r.Result)); // Admin reaches everything
            });
        }

        [Fact]
        public void ItIsListedUnderUnderstandAccess_BetweenExplainAccessAndThePrivilegeFinder()
        {
            var ids = FeatureRegistry.CreateAll().Select(f => f.Id).ToList();

            Assert.Equal(FeatureGroup.UnderstandAccess, new RecordAccessFeature().Group);
            Assert.Equal(ids.IndexOf("explain-access") + 1, ids.IndexOf("record-access"));
        }
    }
}
