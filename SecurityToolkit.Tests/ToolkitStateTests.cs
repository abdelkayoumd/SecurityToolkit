using System;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using SecurityToolkit.Features;
using SecurityToolkit.Services;
using SecurityToolkit.UI;
using Xunit;

namespace SecurityToolkit.Tests
{
    public class ToolkitStateTests
    {
        private const string Dev = "https://dev.crm.dynamics.com";
        private const string Prod = "https://prod.crm.dynamics.com";

        private static Subject User(string name) => new Subject { Kind = SubjectKind.User, Id = Guid.NewGuid(), Name = name };

        [Fact]
        public void TheLastScreen_IsRememberedPerEnvironment()
        {
            var state = new ToolkitState(null);
            state.SetLastFeature(Dev, "privilege-finder");
            state.SetLastFeature(Prod, "explain-access");

            Assert.Equal("privilege-finder", state.LastFeature(Dev));
            Assert.Equal("explain-access", state.LastFeature(Prod));
            Assert.Null(state.LastFeature("https://other.crm.dynamics.com"));
        }

        [Fact]
        public void WithoutAnEnvironment_NothingIsRemembered()
        {
            var state = new ToolkitState(null);
            state.SetLastFeature(null, "home");
            state.AddRecent(null, User("Bob"));

            Assert.Empty(state.Settings.Environments);
        }

        [Fact]
        public void RecentSubjects_MostRecentFirst_OnceEach_AndCapped()
        {
            var state = new ToolkitState(null);
            var bob = User("Bob");
            state.AddRecent(Dev, bob);
            for (var i = 0; i < 10; i++)
            {
                state.AddRecent(Dev, User("User " + i));
            }

            state.AddRecent(Dev, bob);
            state.AddRecent(Dev, new Subject { Kind = SubjectKind.Table, Name = "Account", LogicalName = "account" });
            state.AddRecent(Dev, new Subject { Kind = SubjectKind.Table, Name = "Account", LogicalName = "account" });

            var recent = state.Recent(Dev);
            Assert.Equal(ToolkitState.MaxRecent, recent.Count);
            Assert.Equal(new[] { "Account", "Bob", "User 9" }, recent.Take(3).Select(s => s.Name));
            Assert.Equal(bob.Id, recent[1].Id);
            Assert.Empty(state.Recent(Prod));
        }

        [Fact]
        public void Options_KeepTheirDefaultUntilChanged_ThenRememberTheChange()
        {
            var state = new ToolkitState(null);
            Assert.Null(state.Option("compare-users", "Differences only"));

            state.SetOption("compare-users", "Differences only", false);
            state.SetOption("team-members", "Include disabled users", true);

            Assert.False(state.Option("compare-users", "Differences only"));
            Assert.True(state.Option("team-members", "Include disabled users"));
        }

        [Fact]
        public void TheOptionsOfCloneAUser_AreNotRemembered_ExceptDisabledUsers()
        {
            var state = new ToolkitState(null);
            state.SetOption("clone-user", "Remove what the source doesn't have", true);
            state.SetOption("clone-user", "Include disabled users", true);

            Assert.Null(state.Option("clone-user", "Remove what the source doesn't have"));
            Assert.True(state.Option("clone-user", "Include disabled users"));
        }

        [Fact]
        public void TheSettings_SurviveASaveAndALoad()
        {
            var state = new ToolkitState(null);
            state.SetLastFeature(Dev, "users-by-role");
            state.AddRecent(Dev, new Subject { Kind = SubjectKind.Role, Id = Guid.NewGuid(), Name = "Salesperson" });
            state.SetOption("users-by-role", "Include disabled users", true);

            var serializer = new XmlSerializer(typeof(ToolkitSettings));
            var writer = new StringWriter();
            serializer.Serialize(writer, state.Settings);
            var loaded = new ToolkitState((ToolkitSettings)serializer.Deserialize(new StringReader(writer.ToString())));

            Assert.Equal("users-by-role", loaded.LastFeature(Dev));
            Assert.Equal(SubjectKind.Role, loaded.Recent(Dev).Single().Kind);
            Assert.True(loaded.Option("users-by-role", "Include disabled users"));
        }

        [Fact]
        public void SettingsWithMissingLists_AreRepaired()
        {
            var state = new ToolkitState(new ToolkitSettings { Environments = null, CheckedOptions = null, UncheckedOptions = null });
            state.SetOption("team-members", "Include disabled users", true);
            Assert.True(state.Option("team-members", "Include disabled users"));
        }
    }

    public class CommandConsistencyTests
    {
        [Fact]
        public void EveryScreenThatExportsItsMainList_CallsItExportToExcel()
        {
            Sta.Run(() =>
            {
                var host = OrgFake.HostFor(new Org());
                foreach (var feature in FeatureRegistry.Everything())
                {
                    var view = feature.CreateView(host);
                    var exports = view.Commands.OfType<CommandButton>().Where(c => c.Glyph == Theme.GlyphDownload && c.Text.StartsWith("Export")).Select(c => c.Text).ToList();
                    if (exports.Count > 0)
                    {
                        Assert.True(exports[0] == "Export to Excel", feature.Id + " starts with \"" + exports[0] + "\"");
                    }

                    view.Dispose();
                }
            });
        }

        [Fact]
        public void PerformClick_RunsTheCommand()
        {
            var clicks = 0;
            var button = new CommandButton { Text = "Refresh" };
            button.Click += (s, e) => clicks++;
            button.PerformClick();
            Assert.Equal(1, clicks);
        }
    }
}
