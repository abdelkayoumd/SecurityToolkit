using System;
using System.Collections.Generic;
using System.Windows.Forms;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using SecurityToolkit.UI;

namespace SecurityToolkit.Features
{
    /// <summary>
    /// R-04: pick a business unit and list its users with their teams, directly assigned roles and the roles
    /// they inherit from those teams.
    /// </summary>
    internal sealed class UsersByBusinessUnitFeature : IFeature
    {
        public string Id => "users-by-business-unit";
        public string Title => "Users by business unit";
        public string Glyph => Glyphs.Contact;
        public int Order => 40;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new UsersByBusinessUnitView(host);
        }

        private sealed class UsersByBusinessUnitView : DataView
        {
            private BusinessUnitInfo _businessUnit;
            private List<UserAccessRow> _detail = new List<UserAccessRow>();
            private CheckBox _chkChildren;
            private CheckBox _chkDisabled;

            public UsersByBusinessUnitView(IFeatureHost host) : base(host)
            {
                InfoLabel.Text = "Select a business unit to list its users, teams and security roles.";
            }

            protected override IEnumerable<Control> CreateCommands()
            {
                yield return Command(Theme.GlyphSearch, "Select business unit", () => WithGraph(PickBusinessUnit), strong: true);
                yield return Command(Theme.GlyphRefresh, "Refresh", () => WithGraph(OnGraphLoaded, refresh: true));
                yield return CommandSeparator();
                yield return Command(Theme.GlyphDownload, "Export summary", () => ExportUi.SaveGrid(this, Grid, "UsersByBusinessUnit.xlsx", "Users"));
                yield return Command(Theme.GlyphDownload, "Export detailed", ExportDetailed);
                _chkChildren = CommandCheck("Include child business units", OnGraphLoaded);
                _chkDisabled = CommandCheck("Include disabled users", OnGraphLoaded);
                yield return _chkChildren;
                yield return _chkDisabled;
            }

            protected override void Reset()
            {
                _businessUnit = null;
                _detail = new List<UserAccessRow>();
                base.Reset();
                InfoLabel.Text = "Select a business unit to list its users, teams and security roles.";
            }

            private void PickBusinessUnit()
            {
                var picked = PickerDialog<BusinessUnitInfo>.Pick(this, "Select a business unit", Graph.BusinessUnits, false,
                    "Name=Business Unit", "ParentName=Parent");
                if (picked == null)
                {
                    return;
                }

                _businessUnit = picked[0];
                OnGraphLoaded();
            }

            protected override void OnGraphLoaded()
            {
                if (Graph == null || _businessUnit == null)
                {
                    return;
                }

                var scope = Graph.BusinessUnitScope(_businessUnit.Id, _chkChildren != null && _chkChildren.Checked);
                var users = Graph.UsersInBusinessUnits(scope, _chkDisabled != null && _chkDisabled.Checked);
                Graph.BuildUserAccess(users, out var summary, out var detail);
                _detail = detail;

                Grid.SetData(summary, "User", "BusinessUnit=Business Unit", "Teams", "DirectRoles=Direct roles", "TeamRoles=Roles from teams", "Status");
                InfoLabel.Text = $"{_businessUnit.Name}: {summary.Count} user(s) in {scope.Count} business unit(s)";
            }

            private void ExportDetailed()
            {
                var rows = new List<IReadOnlyList<string>>();
                foreach (var row in _detail)
                {
                    rows.Add(new[] { row.User, row.BusinessUnit, row.Team, row.Role, row.Source });
                }

                ExportUi.Save(this, "UsersByBusinessUnitDetailed.xlsx", "Users detailed",
                    new[] { "User", "Business Unit", "Team", "Role", "Source" }, rows);
            }
        }
    }
}
