using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Services;
using SecurityToolkit.UI;

namespace SecurityToolkit.Features
{
    /// <summary>What a feature view may ask of the shell that hosts it.</summary>
    internal interface IFeatureHost
    {
        /// <summary>Null until XrmToolBox has a connection.</summary>
        SecurityService Security { get; }

        /// <summary>Runs <paramref name="work"/> on a background thread behind the busy overlay, then <paramref name="done"/> on the UI thread. Errors are shown to the user.</summary>
        void RunAsync<T>(string message, Func<SecurityService, T> work, Action<T> done);

        /// <summary>Makes sure there is a connection (prompting for one if needed), then runs <paramref name="action"/>.</summary>
        void EnsureConnection(Action action);

        void ShowMessage(BannerKind kind, string message);

        /// <summary>The dialogs a write operation uses to review, report and offer undo. The shell supplies real windows.</summary>
        IChangeUi CreateChangeUi(System.Windows.Forms.IWin32Window owner);
    }

    /// <summary>The heading a feature sits under in the navigation rail.</summary>
    internal enum FeatureGroup
    {
        /// <summary>The landing page, above any heading.</summary>
        Start,

        /// <summary>Read-only features: look things up, export.</summary>
        Explore,

        /// <summary>Features that modify security in the organization.</summary>
        Change
    }

    /// <summary>A section of the toolkit, shown as one item of the left navigation rail.</summary>
    internal interface IFeature
    {
        string Id { get; }
        string Title { get; }
        string Glyph { get; }

        /// <summary>The heading it is listed under.</summary>
        FeatureGroup Group { get; }

        /// <summary>Position within its group; lower comes first.</summary>
        int Order { get; }

        FeatureView CreateView(IFeatureHost host);
    }

    /// <summary>Base of every feature screen. Created lazily the first time its rail item is opened.</summary>
    internal abstract class FeatureView : UserControl
    {
        protected FeatureView(IFeatureHost host)
        {
            Host = host;
            Dock = DockStyle.Fill;
            BackColor = Theme.PageBackground;
            Font = Theme.FontRegular;
        }

        private IReadOnlyList<Control> _commands;

        protected IFeatureHost Host { get; }

        /// <summary>The buttons this feature puts on the shell's command bar (built once, on first use).</summary>
        public IReadOnlyList<Control> Commands => _commands ?? (_commands = CreateCommands().ToList());

        protected virtual IEnumerable<Control> CreateCommands()
        {
            return Enumerable.Empty<Control>();
        }

        protected static Control CommandSeparator()
        {
            return new Panel { Width = 1, Height = 20, BackColor = Theme.Border, Margin = new Padding(4, 10, 4, 10) };
        }

        /// <summary>The connection changed (or was established); cached data is stale.</summary>
        public virtual void OnConnectionChanged()
        {
        }

        /// <summary>The view was brought to the front.</summary>
        public virtual void OnActivated()
        {
        }
    }

    /// <summary>
    /// The list of features. Adding a feature is one class implementing <see cref="IFeature"/> plus one line here;
    /// removing one is deleting that line.
    /// </summary>
    internal static class FeatureRegistry
    {
        public static IReadOnlyList<IFeature> CreateAll()
        {
            return Order(new IFeature[]
            {
                new HomeFeature(),
                new UsersAndTeamsFeature(),
                new TeamMembersFeature(),
                new CompareUsersFeature(),
                new UsersByRoleFeature(),
                new UsersByBusinessUnitFeature(),
                new PrivilegeFinderFeature(),
                new ExplainAccessFeature(),
                new CleanupReportFeature(),
                new UserRolesFeature(),
                new TeamRolesFeature(),
                new TeamMembershipFeature(),
                new CloneUserFeature(),
            });
        }

        internal static IReadOnlyList<IFeature> Order(IEnumerable<IFeature> features)
        {
            var list = features.OrderBy(f => f.Group).ThenBy(f => f.Order).ThenBy(f => f.Title, StringComparer.OrdinalIgnoreCase).ToList();
            var duplicate = list.GroupBy(f => f.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
            {
                throw new InvalidOperationException($"Two features share the id '{duplicate.Key}'.");
            }

            return list;
        }
    }
}
