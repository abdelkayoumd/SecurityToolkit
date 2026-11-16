using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using SecurityToolkit.UI;

namespace SecurityToolkit.Features
{
    /// <summary>
    /// Application users (app registrations acting in Dataverse) listed apart from people: pick some on the left and
    /// see every role they hold, directly or through a team. They often hold powerful roles and are easy to forget.
    /// </summary>
    internal sealed class ApplicationUsersFeature : IFeature
    {
        public string Id => "application-users";
        public string Title => "Application users";
        public string Glyph => Glyphs.AllApps;
        public FeatureGroup Group => FeatureGroup.LookUp;
        public int Order => 50;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new ApplicationUsersView(host);
        }

        /// <summary>One application user in the list on the left.</summary>
        internal sealed class ApplicationRow
        {
            public ApplicationRow(UserInfo user, int roles)
            {
                User = user;
                Roles = roles;
            }

            public UserInfo User { get; }
            public string Name => User.FullName;
            public string ApplicationId => User.ApplicationId?.ToString();
            public string BusinessUnit => User.BusinessUnitName;
            public string Status => User.Status;
            public int Roles { get; }
        }

        private sealed class ApplicationUsersView : MasterDetailView
        {
            private CheckBox _chkDisabled;

            public ApplicationUsersView(IFeatureHost host)
                : base(host, "Application users (Ctrl/Shift-click to select several)", "Their roles, direct and through teams")
            {
                InfoLabel.Text = "Connect to an environment to get started.";
            }

            protected override string PromptText => "Select application users on the left to see their roles.";

            protected override IEnumerable<Control> CreateCommands()
            {
                yield return Command(Theme.GlyphRefresh, "Refresh", () => WithGraph(OnGraphLoaded, refresh: true), strong: true);
                yield return CommandSeparator();
                yield return Command(Theme.GlyphClearSelection, "Clear selection", Master.ClearSelection);
                yield return Command(Theme.GlyphDownload, "Export to Excel", () => ExportUi.SaveGrid(this, Grid, "ApplicationUsers.xlsx", "Roles"));
                _chkDisabled = CommandCheck("Include disabled users", () => { if (Graph != null) { OnGraphLoaded(); } });
                yield return _chkDisabled;
            }

            protected override void OnGraphLoaded()
            {
                if (Graph == null)
                {
                    return;
                }

                var includeDisabled = _chkDisabled != null && _chkDisabled.Checked;
                var apps = Graph.Users.Where(u => u.IsApplicationUser && (includeDisabled || !u.IsDisabled))
                    .Select(u => new ApplicationRow(u, Graph.DirectRolesOf(u.Id).Count + Graph.TeamsOf(u.Id).Sum(t => Graph.RolesOfTeam(t.Id).Count)))
                    .ToList();
                Master.SetData(apps, "Name", "ApplicationId=Application ID", "BusinessUnit=Business Unit", "Roles", "Status");
                var people = Graph.Users.Count(u => !u.IsApplicationUser && (includeDisabled || !u.IsDisabled));
                InfoLabel.Text = apps.Count == 0
                    ? "This environment has no application user" + (includeDisabled ? "." : " that is enabled.")
                    : $"{apps.Count} application user(s), apart from the {people} people. {(Master.SelectedCount == 0 ? PromptText : string.Empty)}".Trim();
                if (Master.SelectedCount > 0)
                {
                    OnMasterSelectionChanged();
                }
            }

            protected override void OnMasterSelectionChanged()
            {
                var picked = Master.GetSelected<ApplicationRow>();
                if (picked.Count == 0)
                {
                    return;
                }

                Graph.BuildUserAccess(picked.Select(p => p.User), out _, out var detail);
                Grid.SetData(detail, "User=Application user", "Team", "Role", "Source");
                var noRole = picked.Where(p => p.Roles == 0).Select(p => p.Name).ToList();
                InfoLabel.Text = $"{picked.Count} application user(s): {detail.Count(r => !string.IsNullOrEmpty(r.Role))} role(s)." +
                                 (noRole.Count == 0 ? string.Empty : $" No role: {string.Join(", ", noRole)}.");
            }
        }
    }
}
