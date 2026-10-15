using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using SecurityToolkit.UI;

namespace SecurityToolkit.Features
{
    /// <summary>R-03: pick one or more security roles on the left; everyone who has them, directly or through a team, appears on the right.</summary>
    internal sealed class UsersByRoleFeature : IFeature
    {
        public string Id => "users-by-role";
        public string Title => "Users by role";
        public string Glyph => Glyphs.Permissions;
        public int Order => 30;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new UsersByRoleView(host);
        }

        private sealed class UsersByRoleView : MasterDetailView
        {
            private IReadOnlyList<RoleChoice> _roles = new RoleChoice[0];
            private CheckBox _chkDisabled;

            public UsersByRoleView(IFeatureHost host)
                : base(host, "Security roles (Ctrl/Shift-click to select several)", "Users who have the role")
            {
                InfoLabel.Text = "Connect to an environment to get started.";
            }

            protected override string PromptText => "Select one or more security roles on the left to see who has them.";

            protected override IEnumerable<Control> CreateCommands()
            {
                yield return Command(Theme.GlyphRefresh, "Refresh", () => WithGraph(OnGraphLoaded, refresh: true), strong: true);
                yield return CommandSeparator();
                yield return Command(Theme.GlyphSelectAll, "Select all roles", Master.SelectAll);
                yield return Command(Theme.GlyphClearSelection, "Clear selection", Master.ClearSelection);
                yield return CommandSeparator();
                yield return Command(Theme.GlyphDownload, "Export to Excel", () => ExportUi.SaveGrid(this, Grid, "UsersByRole.xlsx", "Users by role"));
                _chkDisabled = CommandCheck("Include disabled users", ShowUsers);
                yield return _chkDisabled;
            }

            protected override void Reset()
            {
                _roles = new RoleChoice[0];
                base.Reset();
            }

            protected override void OnGraphLoaded()
            {
                if (Graph == null)
                {
                    return;
                }

                Master.SetData(Graph.RoleChoices(), "Name=Role", "Copies=Business unit copies");
                if (_roles.Count == 0)
                {
                    InfoLabel.Text = PromptText;
                }
                else
                {
                    ShowUsers();
                }
            }

            protected override void OnMasterSelectionChanged()
            {
                var picked = Master.GetSelected<RoleChoice>();
                if (picked.Count == 0)
                {
                    // Only cleared (by a filter, say): keep showing the last choice.
                    return;
                }

                _roles = picked;
                ShowUsers();
            }

            private void ShowUsers()
            {
                if (Graph == null || _roles.Count == 0)
                {
                    return;
                }

                var includeDisabled = _chkDisabled != null && _chkDisabled.Checked;
                var rows = _roles.SelectMany(role => Graph.UsersInRole(role.RootKey, includeDisabled)).ToList();
                Grid.SetData(rows, "Role", "User", "Domain", "BusinessUnit=Business Unit", "Assignment", "Team", "Status");

                var users = rows.Select(r => r.User + "|" + r.Domain).Distinct().Count();
                var direct = rows.Count(r => r.Assignment == "Direct");
                InfoLabel.Text = $"{_roles.Count} role(s): {users} user(s), {direct} direct assignment(s), {rows.Count - direct} through a team";
            }
        }
    }
}
