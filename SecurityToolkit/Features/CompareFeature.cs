using System.Collections.Generic;
using SecurityToolkit.UI;

namespace SecurityToolkit.Features
{
    /// <summary>
    /// One place to compare, as two tabs: two users (business unit, roles and teams) and two roles (privilege by
    /// privilege). Replaces the "Two users" and "Two roles" menu entries; each tab is still reachable by its old id.
    /// </summary>
    internal sealed class CompareFeature : IWorkspaceFeature
    {
        public string Id => "compare";
        public string Title => "Compare";
        public string Glyph => Glyphs.Compare;
        public FeatureGroup Group => FeatureGroup.Compare;
        public int Order => 10;

        public IReadOnlyList<IFeature> Parts { get; } = new IFeature[]
        {
            new CompareUsersFeature(),
            new CompareRolesFeature()
        };

        /// <summary>Each tab says the question it answers, in the order of <see cref="Parts"/>.</summary>
        internal static readonly QuestionTab[] Tabs =
        {
            new QuestionTab(Glyphs.Compare, "How do two users differ?", "Business unit, roles and teams side by side"),
            new QuestionTab(Glyphs.CompareRoles, "How do two roles differ?", "Table by table, privilege by privilege")
        };

        public FeatureView CreateView(IFeatureHost host)
        {
            return new WorkspaceView(host, Parts, Tabs);
        }
    }
}
