using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using SecurityToolkit.UI;

namespace SecurityToolkit.Features
{
    /// <summary>
    /// W-04: copy one user's setup (business unit, security roles, teams) onto other users. Pick the source on the
    /// left, the targets on the right, tick what to copy, and review the exact changes before anything happens.
    /// </summary>
    internal sealed class CloneUserFeature : IFeature
    {
        public string Id => "clone-user";
        public string Title => "Clone a user";
        public string Glyph => Glyphs.Clone;
        public FeatureGroup Group => FeatureGroup.Change;
        public int Order => 40;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new CloneUserView(host);
        }

        private sealed class CloneUserView : MasterDetailView
        {
            private CheckBox _chkBusinessUnit;
            private CheckBox _chkRoles;
            private CheckBox _chkTeams;
            private CheckBox _chkExtras;
            private CheckBox _chkDisabled;

            public CloneUserView(IFeatureHost host)
                : base(host, "Copy from (select one user)", "Copy to (Ctrl/Shift-click to select several)")
            {
                InfoLabel.Text = "Connect to an environment to get started.";
            }

            protected override string PromptText => "Select the user to copy from on the left and the users to copy to on the right, tick what to copy, then Clone profile.";

            protected override IEnumerable<Control> CreateCommands()
            {
                yield return Command(Theme.GlyphRefresh, "Refresh", () => WithGraph(OnGraphLoaded, refresh: true));
                yield return CommandSeparator();
                yield return Command(Glyphs.Clone, "Clone profile", Clone, strong: true);
                yield return Command(Theme.GlyphClearSelection, "Clear selection", () => { Master.ClearSelection(); Grid.ClearSelection(); });
                yield return CommandSeparator();

                // Copying roles and teams is the usual job; moving business unit is deliberate and off by default.
                _chkRoles = CommandCheck("Security roles", null, isChecked: true);
                _chkTeams = CommandCheck("Teams", null, isChecked: true);
                _chkBusinessUnit = CommandCheck("Business unit", null);
                _chkExtras = CommandCheck("Remove what the source doesn't have", null);
                _chkDisabled = CommandCheck("Include disabled users", () => { if (Graph != null) { OnGraphLoaded(); } });
                yield return CommandLabel("Copy:");
                yield return _chkRoles;
                yield return _chkTeams;
                yield return _chkBusinessUnit;
                yield return _chkExtras;
                yield return _chkDisabled;
            }

            protected override void OnGraphLoaded()
            {
                if (Graph == null)
                {
                    return;
                }

                var includeDisabled = _chkDisabled != null && _chkDisabled.Checked;
                var users = Graph.Users.Where(u => includeDisabled || !u.IsDisabled).ToList();
                var columns = new[] { "FullName=Full Name", "DomainName=User Name", "BusinessUnitName=Business Unit", "Status" };
                Master.SetData(users, columns);
                Grid.SetData(users, columns);
                InfoLabel.Text = PromptText;
            }

            protected override void OnMasterSelectionChanged()
            {
                var picked = Master.GetSelected<UserInfo>();
                if (picked.Count == 0)
                {
                    return;
                }

                if (picked.Count > 1)
                {
                    InfoLabel.Text = "Select exactly one user to copy from.";
                    return;
                }

                var source = picked[0];
                var roles = Graph.DirectRolesOf(source.Id);
                var teams = Graph.TeamsOf(source.Id).Where(t => t.IsManuallyManaged).ToList();
                InfoLabel.Text = $"{source.FullName}: business unit {source.BusinessUnitName}; " +
                                 $"{roles.Count} role(s){Names(roles.Select(r => r.Name))}; {teams.Count} team(s){Names(teams.Select(t => t.Name))}";
            }

            private static string Names(IEnumerable<string> names)
            {
                var list = names.ToList();
                return list.Count == 0 ? string.Empty : " (" + string.Join(", ", list.Take(4)) + (list.Count > 4 ? ", …" : string.Empty) + ")";
            }

            private void Clone()
            {
                WithGraph(() =>
                {
                    var sources = Master.GetSelected<UserInfo>();
                    var targets = Grid.GetSelected<UserInfo>();
                    var options = new CloneOptions
                    {
                        Roles = _chkRoles.Checked,
                        Teams = _chkTeams.Checked,
                        BusinessUnit = _chkBusinessUnit.Checked,
                        RemoveExtras = _chkExtras.Checked
                    };

                    var problem = Validate(sources, targets, options);
                    if (problem != null)
                    {
                        Host.CreateChangeUi(this).Tell(problem);
                        return;
                    }

                    var plan = ChangePlanner.PlanClone(Graph, sources[0], targets, options);
                    new PlanRunner(Host, Host.CreateChangeUi(this)).Run(plan, () => WithGraph(OnGraphLoaded, refresh: true));
                });
            }

            /// <summary>What is missing before a clone can be planned, or null when it is ready.</summary>
            internal static string Validate(IReadOnlyList<UserInfo> sources, IReadOnlyList<UserInfo> targets, CloneOptions options)
            {
                if (sources.Count != 1)
                {
                    return "Select exactly one user to copy from, on the left.";
                }

                if (targets.All(t => t.Id == sources[0].Id))
                {
                    return "Select at least one other user to copy to, on the right.";
                }

                if (!options.Roles && !options.Teams && !options.BusinessUnit)
                {
                    return "Tick at least one of Security roles, Teams or Business unit to copy.";
                }

                return null;
            }
        }
    }
}
