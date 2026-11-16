using System.Collections.Generic;
using SecurityToolkit.UI;

namespace SecurityToolkit.Features
{
    /// <summary>
    /// One place to give or take away access, as three tabs: roles for users, roles for teams, and team membership.
    /// Each tab keeps its own review, restore file and undo. Replaces three menu entries; each tab is still
    /// reachable by its old id.
    /// </summary>
    internal sealed class AssignFeature : IWorkspaceFeature
    {
        public string Id => "assign";
        public string Title => "Assign";
        public string Glyph => Glyphs.AddPerson;
        public FeatureGroup Group => FeatureGroup.Change;
        public int Order => 10;

        public IReadOnlyList<IFeature> Parts { get; } = new IFeature[]
        {
            new UserRolesFeature(),
            new TeamRolesFeature(),
            new TeamMembershipFeature()
        };

        /// <summary>Each tab says what it changes, in the order of <see cref="Parts"/>.</summary>
        internal static readonly QuestionTab[] Tabs =
        {
            new QuestionTab(Glyphs.AddPerson, "Which roles should these users have?", "Add or remove security roles for users"),
            new QuestionTab(Glyphs.TeamShield, "Which roles should these teams have?", "Add or remove security roles for teams"),
            new QuestionTab(Glyphs.Swap, "Which teams should these users be in?", "Add users to owner teams, or take them out")
        };

        public FeatureView CreateView(IFeatureHost host)
        {
            return new WorkspaceView(host, Parts, Tabs);
        }
    }
}
