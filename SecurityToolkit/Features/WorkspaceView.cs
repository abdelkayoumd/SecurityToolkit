using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Services;
using SecurityToolkit.UI;

namespace SecurityToolkit.Features
{
    /// <summary>
    /// Several screens under one menu entry, as tabs. Each tab is a whole screen with its own commands and options; the
    /// command bar follows the tab in front. Switching tabs carries the selected user (or team, role, table) along, so
    /// the next tab answers for the same one.
    /// </summary>
    internal sealed class WorkspaceView : FeatureView
    {
        private readonly QuestionTabs _tabs;
        private readonly FeatureView[] _parts;
        private int _active;
        private bool _shown;

        public WorkspaceView(IFeatureHost host, IReadOnlyList<IFeature> parts, IReadOnlyList<QuestionTab> tabs) : base(host)
        {
            if (parts.Count == 0 || parts.Count != tabs.Count)
            {
                throw new ArgumentException("A workspace needs one tab per part.", nameof(tabs));
            }

            _tabs = new QuestionTabs { Name = "WorkspaceTabs" };
            var body = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            _parts = new FeatureView[parts.Count];
            for (var i = 0; i < parts.Count; i++)
            {
                var view = parts[i].CreateView(host);
                view.FeatureId = parts[i].Id;
                view.Visible = i == 0;
                body.Controls.Add(view);
                _parts[i] = view;
                _tabs.AddTab(tabs[i]);
            }

            // The last control added is laid out first, so the tabs end up above the screens.
            Controls.Add(body);
            Controls.Add(_tabs);
            _tabs.SelectedIndexChanged += (s, e) => Switch(_tabs.SelectedIndex);
        }

        /// <summary>The screen in front.</summary>
        internal FeatureView Active => _parts[_active];

        internal IReadOnlyList<QuestionTab> Tabs => _tabs.Tabs;

        public override IReadOnlyList<Control> Commands => Active.Commands;

        public override IEnumerable<FeatureView> Parts => _parts;

        public override string ScreenId => Active.FeatureId;

        public override Subject CurrentSubject => Active.CurrentSubject;

        public override void ShowPart(string featureId)
        {
            var index = Array.FindIndex(_parts, p => string.Equals(p.FeatureId, featureId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _tabs.SelectedIndex = index;
            }
        }

        public override void ShowSubject(Subject subject)
        {
            Active.ShowSubject(subject);
        }

        public override void OnActivated()
        {
            _shown = true;
            Active.OnActivated();
        }

        public override void OnConnectionChanged()
        {
            foreach (var part in _parts)
            {
                part.OnConnectionChanged();
            }
        }

        private void Switch(int index)
        {
            if (index == _active)
            {
                return;
            }

            var carried = Active.CurrentSubject;
            Active.Visible = false;
            _active = index;
            Active.Visible = true;
            Active.BringToFront();
            _ = Active.Commands; // some options live there (users or teams), and a carried subject may need them
            OnCommandsChanged();
            if (_shown)
            {
                Active.OnActivated();
            }

            if (carried != null && !Same(carried, Active.CurrentSubject))
            {
                Active.ShowSubject(carried);
            }
        }

        private static bool Same(Subject a, Subject b)
        {
            return b != null && a.Kind == b.Kind && a.Id == b.Id && string.Equals(a.LogicalName, b.LogicalName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
