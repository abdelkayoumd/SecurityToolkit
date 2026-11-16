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

        /// <summary>Opens another screen of the toolkit by its feature id.</summary>
        void Navigate(string featureId);

        /// <summary>Opens another screen of the toolkit with <paramref name="subject"/> already selected on it.</summary>
        void Open(string featureId, Subject subject);

        /// <summary>The environment the toolkit is connected to, as people call it ("Contoso Dev"); null when not connected.</summary>
        string EnvironmentName { get; }

        /// <summary>A second environment to compare with; null until one is chosen.</summary>
        SecurityService OtherSecurity { get; }

        string OtherEnvironmentName { get; }

        /// <summary>Asks XrmToolBox for a second connection (its own connection picker).</summary>
        void ChooseOtherEnvironment();

        /// <summary>The second environment was chosen, changed or dropped.</summary>
        event EventHandler OtherEnvironmentChanged;
    }

    /// <summary>The heading a feature sits under in the navigation rail.</summary>
    internal enum FeatureGroup
    {
        /// <summary>The landing page, above any heading.</summary>
        Start,

        /// <summary>Read-only: find out who and what is in the organization.</summary>
        LookUp,

        /// <summary>Read-only: why a user has access, and what they can do.</summary>
        UnderstandAccess,

        /// <summary>Read-only: two users or two roles side by side.</summary>
        Compare,

        /// <summary>Read-only: security health checks.</summary>
        Review,

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

    /// <summary>
    /// One menu entry that holds several screens as tabs. Each part keeps its own id, so links to it (the row menu,
    /// Home, saved settings) still open the right tab.
    /// </summary>
    internal interface IWorkspaceFeature : IFeature
    {
        IReadOnlyList<IFeature> Parts { get; }
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

        private string _featureId;
        private IReadOnlyList<Control> _commands;

        protected IFeatureHost Host { get; }

        /// <summary>Which feature this view is, set by the shell when it creates the view. Lets a screen show its own help.</summary>
        public string FeatureId
        {
            get => _featureId;
            set
            {
                _featureId = value;
                OnFeatureIdChanged();
            }
        }

        protected virtual void OnFeatureIdChanged()
        {
        }

        /// <summary>The buttons this feature puts on the shell's command bar (built once, on first use).</summary>
        public virtual IReadOnlyList<Control> Commands => _commands ?? (_commands = CreateCommands().ToList());

        /// <summary>Raised when <see cref="Commands"/> or <see cref="ScreenId"/> changed (a workspace switched tabs).</summary>
        public event EventHandler CommandsChanged;

        protected void OnCommandsChanged()
        {
            CommandsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>The screens whose options this view shows, each with its own feature id. Just itself, except for a workspace.</summary>
        public virtual IEnumerable<FeatureView> Parts
        {
            get { yield return this; }
        }

        /// <summary>The id of the screen in front: the feature itself, or a workspace's current tab. Remembered between sessions.</summary>
        public virtual string ScreenId => FeatureId;

        /// <summary>Brings the tab with this feature id to the front. Only a workspace has tabs.</summary>
        public virtual void ShowPart(string featureId)
        {
        }

        /// <summary>The user, team, role or table selected on this screen, so another tab can start from it; null when none.</summary>
        public virtual Subject CurrentSubject => null;

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

        /// <summary>Selects <paramref name="subject"/> on this screen (now, or as soon as its list has loaded). Screens that cannot take one ignore it.</summary>
        public virtual void ShowSubject(Subject subject)
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
                new CompareFeature(),
                new UsersByRoleFeature(),
                new BusinessUnitTreeFeature(),
                new ApplicationUsersFeature(),
                new PrivilegeFinderFeature(),
                new UserAccessFeature(),
                new ColumnSecurityFeature(),
                new PrivilegedUsersFeature(),
                new CleanupReportFeature(),
                new AssignFeature(),
                new CloneUserFeature(),
            });
        }

        /// <summary>Any screen by id: a menu entry, or a tab of a workspace. Null when there is none.</summary>
        public static IFeature Find(string id)
        {
            return Everything().FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The menu entry that shows this screen: the workspace for one of its tabs, the screen itself otherwise.</summary>
        public static string MenuIdOf(string id)
        {
            var workspace = CreateAll().OfType<IWorkspaceFeature>()
                .FirstOrDefault(w => w.Parts.Any(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)));
            return workspace?.Id ?? id;
        }

        /// <summary>
        /// How a screen is named for the user: its menu entry, and for a tab the question it answers after it
        /// ("A user's access › Why do they have access?"). Null for an unknown id.
        /// </summary>
        public static string ScreenName(string id)
        {
            var screen = Find(id);
            if (screen == null)
            {
                return null;
            }

            var menuId = MenuIdOf(id);
            return string.Equals(menuId, screen.Id, StringComparison.OrdinalIgnoreCase) ? screen.Title : Find(menuId).Title + " › " + screen.Title;
        }

        /// <summary>Every menu entry and every tab of a workspace.</summary>
        public static IEnumerable<IFeature> Everything()
        {
            foreach (var feature in CreateAll())
            {
                yield return feature;
                if (feature is IWorkspaceFeature workspace)
                {
                    foreach (var part in workspace.Parts)
                    {
                        yield return part;
                    }
                }
            }
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
