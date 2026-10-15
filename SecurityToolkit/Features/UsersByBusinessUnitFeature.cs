using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Services;
using SecurityToolkit.UI;

namespace SecurityToolkit.Features
{
    /// <summary>
    /// R-04: pick one or more business units on the left; their users appear on the right with their teams,
    /// directly assigned roles and the roles they inherit from those teams.
    /// </summary>
    internal sealed class UsersByBusinessUnitFeature : IFeature
    {
        public string Id => "users-by-business-unit";
        public string Title => "Users by business unit";
        public string Glyph => Glyphs.Business;
        public FeatureGroup Group => FeatureGroup.Explore;
        public int Order => 40;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new UsersByBusinessUnitView(host);
        }

        private sealed class UsersByBusinessUnitView : MasterDetailView
        {
            private IReadOnlyList<Guid> _businessUnitIds = new Guid[0];
            private List<UserAccessRow> _detail = new List<UserAccessRow>();
            private CheckBox _chkChildren;
            private CheckBox _chkDisabled;

            public UsersByBusinessUnitView(IFeatureHost host)
                : base(host, "Business units (Ctrl/Shift-click to select several)", "Users, teams and security roles")
            {
                InfoLabel.Text = "Connect to an environment to get started.";
            }

            protected override string PromptText => "Select one or more business units on the left to see their users, teams and roles.";

            protected override IEnumerable<Control> CreateCommands()
            {
                yield return Command(Theme.GlyphRefresh, "Refresh", () => WithGraph(OnGraphLoaded, refresh: true), strong: true);
                yield return CommandSeparator();
                yield return Command(Theme.GlyphSelectAll, "Select all", Master.SelectAll);
                yield return Command(Theme.GlyphClearSelection, "Clear selection", Master.ClearSelection);
                yield return CommandSeparator();
                yield return Command(Theme.GlyphDownload, "Export summary", () => ExportUi.SaveGrid(this, Grid, "UsersByBusinessUnit.xlsx", "Users"));
                yield return Command(Theme.GlyphDownload, "Export detailed", ExportDetailed);
                _chkChildren = CommandCheck("Include child business units", ShowUsers);
                _chkDisabled = CommandCheck("Include disabled users", ShowUsers);
                yield return _chkChildren;
                yield return _chkDisabled;
            }

            protected override void Reset()
            {
                _businessUnitIds = new Guid[0];
                _detail = new List<UserAccessRow>();
                base.Reset();
            }

            protected override void OnGraphLoaded()
            {
                if (Graph == null)
                {
                    return;
                }

                // Sorted by path, so each business unit sits directly under its parent.
                Master.SetData(Graph.BusinessUnitRows(), "Path=Business unit", "Parent");
                Master.SortBy("Path");
                if (_businessUnitIds.Count == 0)
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
                var picked = Master.GetSelected<BusinessUnitRow>();
                if (picked.Count == 0)
                {
                    // Only cleared (by a filter, say): keep showing the last choice.
                    return;
                }

                _businessUnitIds = picked.Select(b => b.Id).ToList();
                ShowUsers();
            }

            private void ShowUsers()
            {
                if (Graph == null || _businessUnitIds.Count == 0)
                {
                    return;
                }

                var children = _chkChildren != null && _chkChildren.Checked;
                var scope = new HashSet<Guid>(_businessUnitIds.SelectMany(id => Graph.BusinessUnitScope(id, children)));
                var users = Graph.UsersInBusinessUnits(scope, _chkDisabled != null && _chkDisabled.Checked);
                Graph.BuildUserAccess(users, out var summary, out var detail);
                _detail = detail;

                Grid.SetData(summary, "User", "BusinessUnit=Business Unit", "Teams", "DirectRoles=Direct roles", "TeamRoles=Roles from teams", "Status");
                InfoLabel.Text = $"{summary.Count} user(s) in {scope.Count} business unit(s)";
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
