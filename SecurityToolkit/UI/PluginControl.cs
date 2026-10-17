using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using SecurityToolkit.Features;
using SecurityToolkit.Services;
using XrmToolBox.Extensibility;
using Label = System.Windows.Forms.Label;

namespace SecurityToolkit.UI
{
    /// <summary>
    /// The shell: a navy title bar, the feature rail on the left and the active feature on the right.
    /// It knows nothing about individual features; they come from <see cref="FeatureRegistry"/>.
    /// </summary>
    public class PluginControl : PluginControlBase, IFeatureHost
    {
        private readonly IReadOnlyList<IFeature> _features = FeatureRegistry.CreateAll();
        private readonly Dictionary<string, FeatureView> _views = new Dictionary<string, FeatureView>(StringComparer.OrdinalIgnoreCase);

        private NavRail _rail;
        private Panel _content;
        private FlowLayoutPanel _commandBar;
        private Banner _banner;
        private Label _lblConnection;
        private FeatureView _current;
        private SecurityService _security;

        public PluginControl()
        {
            ToolName = "Security Toolkit";
            BuildLayout();
            ConnectionUpdated += PluginControl_ConnectionUpdated;
        }

        SecurityService IFeatureHost.Security => _security;

        private void PluginControl_ConnectionUpdated(object sender, ConnectionUpdatedEventArgs e)
        {
            _security = Service == null ? null : new SecurityService(Service);
            _lblConnection.Text = e.ConnectionDetail != null ? $"Connected to {e.ConnectionDetail.OrganizationFriendlyName}" : "Connected";
            foreach (var view in _views.Values)
            {
                view.OnConnectionChanged();
            }
        }

        private void BuildLayout()
        {
            BackColor = Theme.PageBackground;
            Font = Theme.FontRegular;
            Dock = DockStyle.Fill;

            _content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.PageBackground };
            _banner = new Banner { Dock = DockStyle.Top };
            _rail = new NavRail();
            foreach (var feature in _features)
            {
                _rail.AddItem(feature.Id, feature.Glyph, feature.Title, feature.Group, FeatureGuide.Find(feature.Id)?.Summary);
            }

            _rail.ItemSelected += (s, id) => ShowFeature(id);

            // Docking order: the last control added is laid out first.
            Controls.Add(_content);
            Controls.Add(_banner);
            Controls.Add(_rail);
            Controls.Add(BuildCommandBar());
            Controls.Add(BuildHeader());

            if (_features.Count > 0)
            {
                _rail.Select(_features[0].Id);
            }
        }

        private Control BuildCommandBar()
        {
            _commandBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                MinimumSize = new Size(0, 41),
                WrapContents = true,
                BackColor = Theme.PanelBackground,
                Margin = new Padding(0),
                Padding = new Padding(6, 0, 6, 0)
            };
            _commandBar.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border))
                {
                    e.Graphics.DrawLine(pen, 0, _commandBar.Height - 1, _commandBar.Width, _commandBar.Height - 1);
                }
            };
            return _commandBar;
        }

        private Control BuildHeader()
        {
            var bar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                BackColor = Theme.Navy,
                ColumnCount = 3,
                RowCount = 1,
                Height = 40,
                Margin = new Padding(0),
                Padding = new Padding(12, 0, 12, 0)
            };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

            bar.Controls.Add(new Label
            {
                Text = Theme.GlyphWaffle,
                Font = Theme.FontIcon,
                ForeColor = Color.White,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 12, 0)
            }, 0, 0);
            bar.Controls.Add(new Label
            {
                Text = "Security Toolkit",
                Font = Theme.FontAppTitle,
                ForeColor = Color.White,
                AutoSize = true,
                Anchor = AnchorStyles.Left
            }, 1, 0);
            _lblConnection = new Label
            {
                Text = "Not connected",
                Font = Theme.FontSmall,
                ForeColor = Color.FromArgb(200, 210, 225),
                AutoSize = true,
                Anchor = AnchorStyles.Right
            };
            bar.Controls.Add(_lblConnection, 2, 0);
            return bar;
        }

        private void ShowFeature(string id)
        {
            _banner.HideBanner();
            if (!_views.TryGetValue(id, out var view))
            {
                var feature = FindFeature(id);
                view = feature.CreateView(this);
                view.FeatureId = id;
                _views[id] = view;
                _content.Controls.Add(view);
            }

            _commandBar.SuspendLayout();
            _commandBar.Controls.Clear();
            foreach (var command in view.Commands)
            {
                _commandBar.Controls.Add(command);
            }

            _commandBar.ResumeLayout();

            _current?.SendToBack();
            _current = view;
            view.BringToFront();
            view.OnActivated();
        }

        private IFeature FindFeature(string id)
        {
            foreach (var feature in _features)
            {
                if (string.Equals(feature.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return feature;
                }
            }

            throw new ArgumentException($"Unknown feature '{id}'.", nameof(id));
        }

        void IFeatureHost.EnsureConnection(Action action)
        {
            ExecuteMethod(action);
        }

        IChangeUi IFeatureHost.CreateChangeUi(IWin32Window owner)
        {
            return new WinFormsChangeUi(owner);
        }

        void IFeatureHost.Navigate(string featureId)
        {
            _rail.Select(featureId);
        }

        void IFeatureHost.ShowMessage(BannerKind kind, string message)
        {
            _banner.ShowMessage(kind, message);
        }

        void IFeatureHost.RunAsync<T>(string message, Func<SecurityService, T> work, Action<T> done)
        {
            var security = _security;
            WorkAsync(new WorkAsyncInfo
            {
                Message = message,
                Work = (worker, args) => args.Result = work(security),
                PostWorkCallBack = args =>
                {
                    if (args.Error != null)
                    {
                        _banner.ShowMessage(BannerKind.Error, args.Error.Message, toolTip: args.Error.ToString());
                        return;
                    }

                    _banner.HideBanner();
                    done((T)args.Result);
                }
            });
        }
    }
}
