using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using SecurityToolkit.Features;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using SecurityToolkit.UI;
using Xunit;

namespace SecurityToolkit.Tests
{
    /// <summary>A small, entirely fictional organization ("Contoso") used for the README screenshots.</summary>
    internal sealed class DemoOrg
    {
        public readonly Dictionary<string, UserInfo> Users = new Dictionary<string, UserInfo>();
        public readonly Dictionary<string, Dictionary<string, RoleInfo>> Roles = new Dictionary<string, Dictionary<string, RoleInfo>>();
        public readonly Dictionary<string, TeamInfo> Teams = new Dictionary<string, TeamInfo>();
        public readonly Dictionary<string, BusinessUnitInfo> BusinessUnits = new Dictionary<string, BusinessUnitInfo>();
        public SecurityGraph Graph;

        private readonly List<UserTeamLink> _memberships = new List<UserTeamLink>();
        private readonly List<UserRoleLink> _userRoles = new List<UserRoleLink>();
        private readonly List<TeamRoleLink> _teamRoles = new List<TeamRoleLink>();

        public DemoOrg()
        {
            var root = BusinessUnit("Contoso", null);
            var emea = BusinessUnit("Contoso EMEA", root);
            var americas = BusinessUnit("Contoso Americas", root);
            BusinessUnit("Contoso EMEA - France", emea);

            foreach (var role in new[] { "Salesperson", "Sales Manager", "Customer Service Representative", "Customer Service Manager", "Marketing Professional",
                                         "System Administrator", "Field Service Technician", "Reports Viewer", "Legacy Survey Access" })
            {
                Role(role);
            }

            Role("Sales Insights Admin", managed: true);

            User("Alex Morgan", "Contoso"); User("Priya Nair", "Contoso Americas"); User("Jordan Lee", "Contoso Americas");
            User("Sofia Martin", "Contoso EMEA - France"); User("Liam O'Brien", "Contoso EMEA"); User("Chen Wei", "Contoso EMEA");
            User("Amara Okafor", "Contoso EMEA"); User("Noah Becker", "Contoso EMEA - France"); User("Emma Rossi", "Contoso EMEA");
            User("Mateo Silva", "Contoso Americas"); User("Hana Sato", "Contoso"); User("Zoe Laurent", "Contoso EMEA - France");
            User("Oliver Grant", "Contoso Americas", disabled: true); User("Daniel Kim", "Contoso EMEA", disabled: true);
            User("Contoso Integration", "Contoso", accessMode: 4);

            Team("Sales - EMEA", "Contoso EMEA"); Team("Sales - Americas", "Contoso Americas"); Team("Customer Service Tier 1", "Contoso EMEA - France");
            Team("Customer Service Tier 2", "Contoso EMEA"); Team("Marketing Campaigns", "Contoso"); Team("Entra - Sales Managers", "Contoso EMEA", type: 2);
            Team("Shared with Finance", "Contoso", type: 1); Team("Archive 2019", "Contoso");

            Direct("Alex Morgan", "System Administrator"); Direct("Priya Nair", "Sales Manager"); Direct("Jordan Lee", "Salesperson");
            Direct("Liam O'Brien", "Salesperson"); Direct("Chen Wei", "Sales Manager"); Direct("Noah Becker", "Marketing Professional");
            Direct("Mateo Silva", "Field Service Technician"); Direct("Hana Sato", "Reports Viewer"); Direct("Oliver Grant", "Salesperson");

            TeamRole("Sales - EMEA", "Salesperson"); TeamRole("Sales - Americas", "Salesperson"); TeamRole("Customer Service Tier 1", "Customer Service Representative");
            TeamRole("Customer Service Tier 2", "Customer Service Manager"); TeamRole("Marketing Campaigns", "Marketing Professional");
            TeamRole("Entra - Sales Managers", "Sales Manager");

            Member("Priya Nair", "Sales - Americas"); Member("Jordan Lee", "Sales - Americas"); Member("Oliver Grant", "Sales - Americas");
            Member("Liam O'Brien", "Sales - EMEA"); Member("Chen Wei", "Sales - EMEA"); Member("Daniel Kim", "Sales - EMEA");
            Member("Chen Wei", "Entra - Sales Managers"); Member("Sofia Martin", "Customer Service Tier 1"); Member("Zoe Laurent", "Customer Service Tier 1");
            Member("Amara Okafor", "Customer Service Tier 2"); Member("Noah Becker", "Marketing Campaigns"); Member("Hana Sato", "Marketing Campaigns");

            Graph = new SecurityGraph(Users.Values, Roles.Values.SelectMany(r => r.Values), Teams.Values, BusinessUnits.Values, _memberships, _userRoles, _teamRoles);
        }

        private BusinessUnitInfo BusinessUnit(string name, BusinessUnitInfo parent)
        {
            var unit = new BusinessUnitInfo { Id = Guid.NewGuid(), Name = name, ParentId = parent?.Id, ParentName = parent?.Name };
            BusinessUnits[name] = unit;
            return unit;
        }

        private void Role(string name, bool managed = false)
        {
            var copies = new Dictionary<string, RoleInfo>();
            RoleInfo root = null;
            foreach (var unit in BusinessUnits.Values)
            {
                var copy = new RoleInfo { Id = Guid.NewGuid(), Name = name, BusinessUnitId = unit.Id, BusinessUnitName = unit.Name, IsManaged = managed, RootRoleId = root?.Id };
                root = root ?? copy;
                copies[unit.Name] = copy;
            }

            Roles[name] = copies;
        }

        private void User(string name, string unit, bool disabled = false, int accessMode = 0)
        {
            Users[name] = new UserInfo
            {
                Id = Guid.NewGuid(), FullName = name, DomainName = name.ToLowerInvariant().Replace("'", string.Empty).Replace(' ', '.') + "@contoso.com",
                BusinessUnitId = BusinessUnits[unit].Id, BusinessUnitName = unit, IsDisabled = disabled, AccessMode = accessMode
            };
        }

        private void Team(string name, string unit, int type = 0)
        {
            Teams[name] = new TeamInfo { Id = Guid.NewGuid(), Name = name, TeamType = type, BusinessUnitId = BusinessUnits[unit].Id, BusinessUnitName = unit };
        }

        /// <summary>Assigns the role copy that lives in the user's own business unit, as Dataverse requires.</summary>
        private void Direct(string user, string role)
        {
            _userRoles.Add(new UserRoleLink { UserId = Users[user].Id, RoleId = Roles[role][Users[user].BusinessUnitName].Id });
        }

        private void TeamRole(string team, string role)
        {
            _teamRoles.Add(new TeamRoleLink { TeamId = Teams[team].Id, RoleId = Roles[role][Teams[team].BusinessUnitName].Id });
        }

        private void Member(string user, string team)
        {
            _memberships.Add(new UserTeamLink { UserId = Users[user].Id, TeamId = Teams[team].Id });
        }

        private static readonly string[] DemoTables = { "account", "appointment", "contact", "incident", "lead", "opportunity", "quote", "task" };
        private static readonly string[] SalesTables = { "account", "contact", "lead", "opportunity", "quote" };
        private static readonly string[] ServiceTables = { "account", "contact", "incident" };
        private Dictionary<(string, PrivilegeKind), Guid> _catalog;

        /// <summary>One privilege record per table and action, with fixed ids.</summary>
        private Dictionary<(string, PrivilegeKind), Guid> PrivilegeCatalog()
        {
            if (_catalog == null)
            {
                _catalog = new Dictionary<(string, PrivilegeKind), Guid>();
                foreach (var table in DemoTables)
                {
                    foreach (PrivilegeKind kind in Enum.GetValues(typeof(PrivilegeKind)))
                    {
                        _catalog[(table, kind)] = Guid.NewGuid();
                    }
                }
            }

            return _catalog;
        }

        /// <summary>How far a demo role reaches for a table and action: 1 own records, 2 business unit, 4 parent and child units, 8 organization; 0 for none.</summary>
        private static int Reach(string role, string table, PrivilegeKind kind)
        {
            var everyday = kind == PrivilegeKind.Create || kind == PrivilegeKind.Read || kind == PrivilegeKind.Write
                           || kind == PrivilegeKind.Append || kind == PrivilegeKind.AppendTo;
            switch (role)
            {
                case "System Administrator":
                    return 8;
                case "Sales Manager":
                    if (SalesTables.Contains(table)) { return kind == PrivilegeKind.Delete ? 2 : 4; }
                    return kind == PrivilegeKind.Read ? 2 : 0;
                case "Salesperson":
                    if (SalesTables.Contains(table)) { return everyday ? 1 : 0; }
                    return kind == PrivilegeKind.Read ? 1 : 0;
                case "Customer Service Representative":
                    if (ServiceTables.Contains(table)) { return everyday ? (table == "incident" ? 2 : 1) : 0; }
                    return kind == PrivilegeKind.Read ? 1 : 0;
                case "Customer Service Manager":
                    return ServiceTables.Contains(table) ? 2 : (kind == PrivilegeKind.Read ? 1 : 0);
                case "Marketing Professional":
                    return table == "lead" || table == "contact" ? (kind == PrivilegeKind.Assign || kind == PrivilegeKind.Share ? 0 : 2) : (kind == PrivilegeKind.Read ? 1 : 0);
                default:
                    return 0;
            }
        }

        /// <summary>A fake service that serves this organization, including tables and privilege grants for the privilege finder.</summary>
        public FakeOrganizationService Service()
        {
            var service = OrgFake.Serve(Graph);
            var graph = Roles;
            var serveGraph = service.OnRetrieveMultiple;
            service.OnRetrieveMultiple = query =>
            {
                var q = (QueryExpression)query;
                switch (q.EntityName)
                {
                    case "privilege" when q.Criteria.Conditions.Count == 0:
                        // The catalogue of which privilege is which action on which table.
                        return new EntityCollection(PrivilegeCatalog().Select(p =>
                        {
                            var e = new Entity("privilege", p.Value) { ["accessright"] = Privileges.AccessRight(p.Key.Item2) };
                            e["otc.objecttypecode"] = new AliasedValue("privilegeobjecttypecodes", "objecttypecode", p.Key.Item1);
                            return e;
                        }).ToList());
                    case "privilege":
                        return new EntityCollection(new List<Entity> { new Entity("privilege", Guid.NewGuid()) });
                    case "roleprivileges" when q.Criteria.Conditions.Any(c => c.AttributeName == "roleid"):
                        // What each requested role record grants, table by table.
                        var asked = new HashSet<Guid>(q.Criteria.Conditions.First(c => c.AttributeName == "roleid").Values.Cast<Guid>());
                        var roleNames = graph.SelectMany(r => r.Value.Values.Select(copy => new { copy.Id, Name = r.Key })).ToDictionary(x => x.Id, x => x.Name);
                        var grants = new EntityCollection();
                        foreach (var roleId in asked.Where(roleNames.ContainsKey))
                        {
                            foreach (var privilege in PrivilegeCatalog())
                            {
                                var mask = Reach(roleNames[roleId], privilege.Key.Item1, privilege.Key.Item2);
                                if (mask > 0)
                                {
                                    grants.Entities.Add(new Entity("roleprivileges") { ["roleid"] = roleId, ["privilegeid"] = privilege.Value, ["privilegedepthmask"] = mask });
                                }
                            }
                        }

                        return grants;
                    case "roleprivileges":
                        // How far each role reaches: 1 own records, 2 business unit, 4 parent and child units, 8 organization.
                        var depths = new Dictionary<string, int>
                        {
                            ["System Administrator"] = 8, ["Sales Manager"] = 4, ["Customer Service Manager"] = 2,
                            ["Salesperson"] = 1, ["Customer Service Representative"] = 1, ["Marketing Professional"] = 2
                        };
                        var result = new EntityCollection();
                        foreach (var pair in depths)
                        {
                            foreach (var copy in graph[pair.Key].Values)
                            {
                                result.Entities.Add(new Entity("roleprivileges") { ["roleid"] = copy.Id, ["privilegedepthmask"] = pair.Value });
                            }
                        }

                        return result;
                    default:
                        return serveGraph(query);
                }
            };

            service.OnExecute = request =>
            {
                var response = new Microsoft.Xrm.Sdk.Messages.RetrieveAllEntitiesResponse();
                EntityMetadata Table(string logical, string display) => new EntityMetadata
                {
                    LogicalName = logical,
                    DisplayName = new Microsoft.Xrm.Sdk.Label(new LocalizedLabel(display, 1033), new LocalizedLabel[0])
                };
                response.Results["EntityMetadata"] = new[]
                {
                    Table("account", "Account"), Table("appointment", "Appointment"), Table("contact", "Contact"), Table("incident", "Case"),
                    Table("lead", "Lead"), Table("opportunity", "Opportunity"), Table("quote", "Quote"), Table("task", "Task")
                };
                return response;
            };
            return service;
        }
    }

    /// <summary>
    /// Produces the README screenshots from the real shell running against <see cref="DemoOrg"/>. It does nothing
    /// unless SECURITYTOOLKIT_SCREENSHOTS is set, so it never slows or disturbs the normal test run.
    /// </summary>
    public class ScreenshotGenerator
    {
        private static readonly Size WindowSize = new Size(1280, 720);

        [Fact]
        public void GenerateReadmeScreenshots()
        {
            if (Environment.GetEnvironmentVariable("SECURITYTOOLKIT_SCREENSHOTS") != "1")
            {
                return;
            }

            var directory = FindRepositoryRoot();
            var output = Path.Combine(directory, "docs", "screenshots");
            Directory.CreateDirectory(output);

            Sta.Run(() =>
            {
                var demo = new DemoOrg();
                var shell = new PluginControl { Size = WindowSize };
                var form = new Form { ClientSize = WindowSize, FormBorderStyle = FormBorderStyle.FixedToolWindow };
                form.Controls.Add(shell);
                form.Show();
                Wait(200);
                shell.UpdateConnection(demo.Service(), null, string.Empty, null);
                Wait(300);

                var rail = (NavRail)Field(shell, "_rail");
                var views = (Dictionary<string, FeatureView>)Field(shell, "_views");

                void Open(string id) { rail.Select(id); Wait(300); }
                FilterableGrid[] Grids(string id) => FindGrids(views[id]);
                void Capture(string name) { Wait(400); Save(shell, views, Path.Combine(output, name + ".png")); }
                void Until(Func<bool> condition)
                {
                    var clock = Stopwatch.StartNew();
                    while (!condition() && clock.ElapsedMilliseconds < 10000) { Application.DoEvents(); Thread.Sleep(15); }
                    Assert.True(condition(), "Timed out waiting for the screen to load.");
                }

                // Nothing may be selected just because a list was loaded; only the user's own choice selects.
                void LoadedWithoutSelection(string id)
                {
                    var left = Grids(id)[0];
                    Until(() => left.GetVisible<object>().Any());
                    Wait(400);
                    Assert.Equal(0, left.SelectedCount);
                }

                // Users and teams: the user list and, for a selected user, the tree of direct and inherited roles.
                Open("users-and-teams");
                Click(views["users-and-teams"], "Load users");
                LoadedWithoutSelection("users-and-teams");
                MasterDetailHarness.Select<UserInfo>(Grids("users-and-teams")[0], u => u.FullName == "Chen Wei");
                var tree = FindControls<TreeView>(views["users-and-teams"]).Single();
                Until(() => tree.Nodes.Count == 1 && tree.Nodes[0].Text == "Chen Wei");
                Capture("users-and-teams");

                // Compare two users.
                Open("compare-users");
                LoadedWithoutSelection("compare-users");
                MasterDetailHarness.Select<UserInfo>(Grids("compare-users")[0], u => u.FullName == "Chen Wei" || u.FullName == "Liam O'Brien");
                Until(() => Grids("compare-users")[1].GetVisible<CompareRow>().Any());
                Capture("compare-users");

                // Privilege finder.
                Open("privilege-finder");
                LoadedWithoutSelection("privilege-finder");
                var combos = FindControls<ComboBox>(views["privilege-finder"]).OrderBy(c => c.PointToScreen(Point.Empty).X).ToArray();
                combos[0].SelectedIndex = 3; // Delete
                combos[1].SelectedIndex = 1; // Business unit or wider
                MasterDetailHarness.Select<TableInfo>(Grids("privilege-finder")[0], t => t.LogicalName == "account");
                Until(() => Grids("privilege-finder")[1].GetVisible<RolePrivilegeRow>().Any());
                Capture("privilege-finder");

                // Why does a user have access? Chen Wei reaches Account two ways: directly and through two teams.
                Open("explain-access");
                LoadedWithoutSelection("explain-access");
                var explainCombos = FindControls<ComboBox>(views["explain-access"]).OrderBy(c => c.PointToScreen(Point.Empty).X).ToArray();
                explainCombos[0].SelectedIndex = 3; // Delete
                explainCombos[1].SelectedIndex = Enumerable.Range(0, explainCombos[1].Items.Count).First(i => explainCombos[1].Items[i].ToString().StartsWith("Account"));
                MasterDetailHarness.Select<UserInfo>(Grids("explain-access")[0], u => u.FullName == "Chen Wei");
                Until(() => Grids("explain-access")[1].GetVisible<AccessReasonRow>().Any());
                Capture("explain-access");

                // What can a user do? Chen Wei is a sales manager who also sits in a sales team.
                Open("user-access-matrix");
                LoadedWithoutSelection("user-access-matrix");
                MasterDetailHarness.Select<UserInfo>(Grids("user-access-matrix")[0], u => u.FullName == "Chen Wei");
                Until(() => Grids("user-access-matrix")[1].GetVisible<MatrixRow>().Any());
                Capture("user-access-matrix");

                // Cleanup report.
                Open("cleanup-report");
                Until(() => Grids("cleanup-report")[1].GetVisible<CleanupRow>().Any());
                Capture("cleanup-report");

                // Assign roles: the right-hand list shows how many of the selected users already have each role.
                Open("assign-user-roles");
                LoadedWithoutSelection("assign-user-roles");
                Until(() => Grids("assign-user-roles")[1].GetVisible<PickRow>().Any());
                MasterDetailHarness.Select<UserInfo>(Grids("assign-user-roles")[0], u => u.FullName == "Emma Rossi" || u.FullName == "Zoe Laurent");
                Until(() => Grids("assign-user-roles")[1].GetVisible<PickRow>().Any(r => r.Assigned.Length > 0));
                MasterDetailHarness.Select<PickRow>(Grids("assign-user-roles")[1], r => r.Name == "Customer Service Representative");
                Wait(300);
                Capture("assign-roles");

                form.Dispose();

                // The review every change goes through, shown for cloning Liam's setup onto Emma.
                var plan = ChangePlanner.PlanClone(demo.Graph, demo.Users["Liam O'Brien"], new[] { demo.Users["Emma Rossi"] },
                    new CloneOptions { Roles = true, Teams = true });
                plan.Notes.Add("Emma Rossi: nothing else to copy.");
                var dialog = new ReviewDialog(plan) { StartPosition = FormStartPosition.Manual, Location = new Point(40, 40), Size = new Size(820, 420) };
                dialog.Show();
                Wait(400);
                using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                {
                    dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
                    bitmap.Save(Path.Combine(output, "review-changes.png"));
                }

                dialog.Dispose();
            });
        }

        private static void Click(FeatureView view, string text)
        {
            var button = view.Commands.OfType<CommandButton>().First(b => b.Text == text);
            typeof(Control).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(button, new object[] { EventArgs.Empty });
        }

        private static void Wait(int milliseconds)
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < milliseconds)
            {
                Application.DoEvents();
                Thread.Sleep(15);
            }
        }

        private static object Field(object target, string name)
        {
            return target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
        }

        /// <summary>DrawToBitmap paints overlapping siblings bottom-last, so hide every view except the one on top first.</summary>
        private static void Save(PluginControl shell, Dictionary<string, FeatureView> views, string path)
        {
            var content = (Panel)Field(shell, "_content");
            var top = content.Controls.Cast<Control>().OrderBy(c => content.Controls.GetChildIndex(c)).First();
            var hidden = content.Controls.Cast<Control>().Where(c => c != top && c.Visible).ToList();
            foreach (var control in hidden) { control.Visible = false; }
            Wait(300);
            using (var bitmap = new Bitmap(shell.Width, shell.Height))
            {
                shell.DrawToBitmap(bitmap, new Rectangle(Point.Empty, shell.Size));
                bitmap.Save(path);
            }

            foreach (var control in hidden) { control.Visible = true; }
        }

        private static FilterableGrid[] FindGrids(Control root)
        {
            return FindControls<FilterableGrid>(root).OrderBy(g => g.PointToScreen(Point.Empty).X).ToArray();
        }

        private static IEnumerable<T> FindControls<T>(Control root) where T : Control
        {
            foreach (Control child in root.Controls)
            {
                if (child is T match) { yield return match; }
                if (!(child is FilterableGrid)) { foreach (var inner in FindControls<T>(child)) { yield return inner; } }
            }
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SecurityToolkit.slnx")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("Could not find the repository root.");
        }
    }
}
