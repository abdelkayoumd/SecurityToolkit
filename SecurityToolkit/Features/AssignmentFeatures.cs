using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using SecurityToolkit.UI;

namespace SecurityToolkit.Features
{
    /// <summary>W-01: add security roles to, or remove them from, the selected users.</summary>
    internal sealed class UserRolesFeature : IFeature
    {
        public string Id => "assign-user-roles";
        public string Title => "Assign roles to users";
        public string Glyph => Glyphs.AddPerson;
        public FeatureGroup Group => FeatureGroup.Change;
        public int Order => 60;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new UserRolesView(host);
        }

        private sealed class UserRolesView : AssignmentView
        {
            private CheckBox _chkDisabled;

            public UserRolesView(IFeatureHost host)
                : base(host, "Users (Ctrl/Shift-click to select several)", "Security roles to add or remove", "user", "role", "Add roles", "Remove roles")
            {
            }

            protected override string[] RowColumns => new[] { "Name=Role", "Assigned=Users who have it" };

            protected override IEnumerable<Control> ExtraCommands()
            {
                _chkDisabled = CommandCheck("Include disabled users", () => { if (Graph != null) { OnGraphLoaded(); } });
                yield return _chkDisabled;
            }

            protected override void FillSubjects()
            {
                var includeDisabled = _chkDisabled != null && _chkDisabled.Checked;
                Master.SetData(Graph.Users.Where(u => includeDisabled || !u.IsDisabled).ToList(),
                    "FullName=Full Name", "DomainName=User Name", "BusinessUnitName=Business Unit", "Status");
            }

            protected override IEnumerable<PickRow> BuildRows()
            {
                return Graph.RoleChoices().Select(c => new PickRow { Key = c.RootKey, Name = c.Name });
            }

            protected override IReadOnlyList<Guid> SelectedSubjectIds()
            {
                return Master.GetSelected<UserInfo>().Select(u => u.Id).ToList();
            }

            protected override bool Has(Guid subjectId, PickRow row)
            {
                return Graph.DirectRolesOf(subjectId).Any(r => r.RootKey == row.Key);
            }

            protected override ChangePlan Plan(bool add, IReadOnlyList<PickRow> items)
            {
                var keys = new HashSet<Guid>(items.Select(i => i.Key));
                return ChangePlanner.PlanUserRoles(Graph, Master.GetSelected<UserInfo>(), Graph.RoleChoices().Where(c => keys.Contains(c.RootKey)), add);
            }
        }
    }

    /// <summary>W-02: add security roles to, or remove them from, the selected teams.</summary>
    internal sealed class TeamRolesFeature : IFeature
    {
        public string Id => "assign-team-roles";
        public string Title => "Assign roles to teams";
        public string Glyph => Glyphs.Security;
        public FeatureGroup Group => FeatureGroup.Change;
        public int Order => 70;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new TeamRolesView(host);
        }

        private sealed class TeamRolesView : AssignmentView
        {
            public TeamRolesView(IFeatureHost host)
                : base(host, "Teams (Ctrl/Shift-click to select several)", "Security roles to add or remove", "team", "role", "Add roles", "Remove roles")
            {
            }

            protected override string[] RowColumns => new[] { "Name=Role", "Assigned=Teams that have it" };

            protected override void FillSubjects()
            {
                // Access teams carry record-level access, not security roles.
                Master.SetData(Graph.Teams.Where(t => t.CanHoldRoles).ToList(), "Name=Team", "TypeName=Type", "BusinessUnitName=Business Unit");
            }

            protected override IEnumerable<PickRow> BuildRows()
            {
                return Graph.RoleChoices().Select(c => new PickRow { Key = c.RootKey, Name = c.Name });
            }

            protected override IReadOnlyList<Guid> SelectedSubjectIds()
            {
                return Master.GetSelected<TeamInfo>().Select(t => t.Id).ToList();
            }

            protected override bool Has(Guid subjectId, PickRow row)
            {
                return Graph.RolesOfTeam(subjectId).Any(r => r.RootKey == row.Key);
            }

            protected override ChangePlan Plan(bool add, IReadOnlyList<PickRow> items)
            {
                var keys = new HashSet<Guid>(items.Select(i => i.Key));
                return ChangePlanner.PlanTeamRoles(Graph, Master.GetSelected<TeamInfo>(), Graph.RoleChoices().Where(c => keys.Contains(c.RootKey)), add);
            }
        }
    }

    /// <summary>W-03: add the selected users to, or remove them from, the selected teams.</summary>
    internal sealed class TeamMembershipFeature : IFeature
    {
        public string Id => "assign-team-membership";
        public string Title => "Manage team membership";
        public string Glyph => Glyphs.Swap;
        public FeatureGroup Group => FeatureGroup.Change;
        public int Order => 80;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new TeamMembershipView(host);
        }

        private sealed class TeamMembershipView : AssignmentView
        {
            private CheckBox _chkDisabled;
            private Dictionary<Guid, HashSet<Guid>> _members = new Dictionary<Guid, HashSet<Guid>>();

            public TeamMembershipView(IFeatureHost host)
                : base(host, "Users (Ctrl/Shift-click to select several)", "Teams to add them to or remove them from (owner teams only)", "user", "team", "Add to teams", "Remove from teams")
            {
            }

            protected override string[] RowColumns => new[] { "Name=Team", "BusinessUnit=Business Unit", "Assigned=Selected users who are members" };

            protected override IEnumerable<Control> ExtraCommands()
            {
                _chkDisabled = CommandCheck("Include disabled users", () => { if (Graph != null) { OnGraphLoaded(); } });
                yield return _chkDisabled;
            }

            protected override void FillSubjects()
            {
                var includeDisabled = _chkDisabled != null && _chkDisabled.Checked;
                Master.SetData(Graph.Users.Where(u => includeDisabled || !u.IsDisabled).ToList(),
                    "FullName=Full Name", "DomainName=User Name", "BusinessUnitName=Business Unit", "Status");
            }

            protected override IEnumerable<PickRow> BuildRows()
            {
                // Only teams whose members are managed by hand can be changed here.
                var teams = Graph.Teams.Where(t => t.IsManuallyManaged).ToList();
                _members = teams.ToDictionary(t => t.Id, t => new HashSet<Guid>(Graph.MembersOf(t.Id).Select(u => u.Id)));
                return teams.Select(t => new PickRow { Key = t.Id, Name = t.Name, BusinessUnit = t.BusinessUnitName });
            }

            protected override IReadOnlyList<Guid> SelectedSubjectIds()
            {
                return Master.GetSelected<UserInfo>().Select(u => u.Id).ToList();
            }

            protected override bool Has(Guid subjectId, PickRow row)
            {
                return _members.TryGetValue(row.Key, out var members) && members.Contains(subjectId);
            }

            protected override ChangePlan Plan(bool add, IReadOnlyList<PickRow> items)
            {
                var ids = new HashSet<Guid>(items.Select(i => i.Key));
                return ChangePlanner.PlanMembership(Graph, Master.GetSelected<UserInfo>(), Graph.Teams.Where(t => ids.Contains(t.Id)), add);
            }
        }
    }
}
