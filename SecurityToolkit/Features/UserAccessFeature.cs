using System.Collections.Generic;
using SecurityToolkit.UI;

namespace SecurityToolkit.Features
{
    /// <summary>
    /// One place for a user's access, as three tabs: what they can do (every table against the eight privileges), why
    /// they have a given privilege (the routes to it), and their access to one record. Replaces three menu entries;
    /// each tab is still reachable by its old id.
    /// </summary>
    internal sealed class UserAccessFeature : IWorkspaceFeature
    {
        public string Id => "user-access";
        public string Title => "A user's access";
        public string Glyph => Glyphs.Grid;
        public FeatureGroup Group => FeatureGroup.UnderstandAccess;
        public int Order => 10;

        public IReadOnlyList<IFeature> Parts { get; } = new IFeature[]
        {
            new UserAccessMatrixFeature(),
            new ExplainAccessFeature(),
            new RecordAccessFeature()
        };

        /// <summary>Each tab says the question it answers, in the order of <see cref="Parts"/>.</summary>
        internal static readonly QuestionTab[] Tabs =
        {
            new QuestionTab(Glyphs.Grid, "What can they do?", "Every table against the eight privileges"),
            new QuestionTab(Glyphs.Help, "Why do they have access?", "Every route to one privilege on one table"),
            new QuestionTab(Glyphs.RecordAccess, "Can they open this record?", "One record, every right, and who it is shared with")
        };

        public FeatureView CreateView(IFeatureHost host)
        {
            return new WorkspaceView(host, Parts, Tabs);
        }
    }
}
