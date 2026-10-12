using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Models;
using SecurityToolkit.UI;

namespace SecurityToolkit.Features
{
    /// <summary>R-03: pick a security role and list every user who has it, directly or through a team.</summary>
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

        private sealed class UsersByRoleView : DataView
        {
            private RoleChoice _role;
            private CheckBox _chkDisabled;

            public UsersByRoleView(IFeatureHost host) : base(host)
            {
                InfoLabel.Text = "Select a security role to list the users who have it.";
            }

            protected override IEnumerable<Control> CreateCommands()
            {
                yield return Command(Theme.GlyphSearch, "Select role", () => WithGraph(PickRole), strong: true);
                yield return Command(Theme.GlyphRefresh, "Refresh", () => WithGraph(OnGraphLoaded, refresh: true));
                yield return CommandSeparator();
                yield return Command(Theme.GlyphDownload, "Export to Excel", () => ExportUi.SaveGrid(this, Grid, "UsersByRole.xlsx", "Users by role"));
                _chkDisabled = CommandCheck("Include disabled users", OnGraphLoaded);
                yield return _chkDisabled;
            }

            protected override void Reset()
            {
                _role = null;
                base.Reset();
                InfoLabel.Text = "Select a security role to list the users who have it.";
            }

            private void PickRole()
            {
                var picked = PickerDialog<RoleChoice>.Pick(this, "Select a security role", Graph.RoleChoices(), false,
                    "Name=Role", "Copies=Business unit copies");
                if (picked == null)
                {
                    return;
                }

                _role = picked[0];
                OnGraphLoaded();
            }

            protected override void OnGraphLoaded()
            {
                if (Graph == null || _role == null)
                {
                    return;
                }

                var rows = Graph.UsersInRole(_role.RootKey, _chkDisabled != null && _chkDisabled.Checked);
                Grid.SetData(rows, "User", "Domain", "BusinessUnit=Business Unit", "Assignment", "Team", "Status");

                var users = rows.Select(r => r.User + "|" + r.Domain).Distinct().Count();
                var direct = rows.Count(r => r.Assignment == "Direct");
                InfoLabel.Text = $"{_role.Name}: {users} user(s), {direct} with the role directly, {rows.Count - direct} through a team";
            }
        }
    }
}
