using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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
    public class PluginControl : MultipleConnectionsPluginControlBase, IFeatureHost
    {
        private readonly IReadOnlyList<IFeature> _features = FeatureRegistry.CreateAll();
        private readonly Dictionary<string, FeatureView> _views = new Dictionary<string, FeatureView>(StringComparer.OrdinalIgnoreCase);

        private NavRail _rail;
        private Panel _content;
        private FlowLayoutPanel _commandBar;
        private Banner _banner;
        private Label _lblConnection;
        private GlobalSearchBox _search;
        private Label _lblLoaded;
        private FeatureView _current;
        private SecurityService _security;
        private readonly ToolkitState _state = new ToolkitState(LoadSettings());
        private string _environment;

        public PluginControl()
        {
            ToolName = "Security Toolkit";
            BuildLayout();
            ConnectionUpdated += PluginControl_ConnectionUpdated;
        }

        SecurityService IFeatureHost.Security => _security;

        // ----- a second environment, to compare with -----

        private SecurityService _other;
        private string _otherName;
        private EventHandler _otherChanged;

        string IFeatureHost.EnvironmentName => ConnectionDetail?.OrganizationFriendlyName;

        SecurityService IFeatureHost.OtherSecurity => _other;

        string IFeatureHost.OtherEnvironmentName => _otherName;

        event EventHandler IFeatureHost.OtherEnvironmentChanged
        {
            add => _otherChanged += value;
            remove => _otherChanged -= value;
        }

        void IFeatureHost.ChooseOtherEnvironment()
        {
            // XrmToolBox's own connection picker; the choice arrives in ConnectionDetailsUpdated.
            AddAdditionalOrganization();
        }

        /// <summary>
        /// XrmToolBox added or removed a second connection. Only one other environment is compared at a time: the
        /// latest one chosen.
        /// </summary>
        protected override void ConnectionDetailsUpdated(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            var detail = AdditionalConnectionDetails.LastOrDefault();
            _other = detail == null ? null : new SecurityService(detail.GetCrmServiceClient());
            _otherName = detail?.OrganizationFriendlyName ?? detail?.ConnectionName;
            _otherChanged?.Invoke(this, EventArgs.Empty);
        }

        private void PluginControl_ConnectionUpdated(object sender, ConnectionUpdatedEventArgs e)
        {
            _security = Service == null ? null : new SecurityService(Service);
            _lblConnection.Text = e.ConnectionDetail != null ? $"Connected to {e.ConnectionDetail.OrganizationFriendlyName}" : "Connected";
            ExportUi.EnvironmentName = e.ConnectionDetail?.OrganizationFriendlyName;
            _search.Reset();
            foreach (var view in _views.Values)
            {
                view.OnConnectionChanged();
            }

            // Back to the screen used last time in this environment, unless another screen was opened already.
            _environment = e.ConnectionDetail?.WebApplicationUrl ?? e.ConnectionDetail?.OrganizationFriendlyName;
            var last = _state.LastFeature(_environment);
            if (last != null && _current?.FeatureId == "home" && FeatureRegistry.Find(last) != null)
            {
                ((IFeatureHost)this).Navigate(last);
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
                _rail.AddItem(feature.Id, feature.Glyph, feature.Title, feature.Group);
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
            _lblLoaded = new Label
            {
                AutoSize = true,
                Font = Theme.FontSmall,
                ForeColor = Theme.TextSecondary,
                Margin = new Padding(16, 14, 6, 0),
                Visible = false,
                Name = "LoadedAt"
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
                ColumnCount = 4,
                RowCount = 1,
                Height = 40,
                Margin = new Padding(0),
                Padding = new Padding(12, 0, 12, 0)
            };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
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
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 24, 0)
            }, 1, 0);

            _search = new GlobalSearchBox
            {
                Anchor = AnchorStyles.Left,
                OverlayHost = this,
                Source = () => Tuple.Create(_security?.CachedGraph, _security?.CachedTables),
                RequestData = done =>
                {
                    if (_security == null)
                    {
                        _banner.ShowMessage(BannerKind.Warning, "Connect to an environment to search it.");
                        return;
                    }

                    ((IFeatureHost)this).RunAsync("Loading security data...", security => Tuple.Create(security.GetGraph(), security.GetTables()), loaded => done());
                },
                Open = (featureId, subject) => ((IFeatureHost)this).Open(featureId, subject),
                Recent = () => _state.Recent(_environment)
            };
            bar.Controls.Add(_search, 2, 0);

            _lblConnection = new Label
            {
                Text = "Not connected",
                Font = Theme.FontSmall,
                ForeColor = Color.FromArgb(200, 210, 225),
                AutoSize = true,
                Anchor = AnchorStyles.Right
            };
            bar.Controls.Add(_lblConnection, 3, 0);
            return bar;
        }

        /// <summary>
        /// Keyboard shortcuts, the same on every screen: Ctrl+K puts the cursor in the search box, F5 refreshes,
        /// Ctrl+E exports to Excel (each only where the screen has that command).
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.K:
                    _search.FocusBox();
                    return true;
                case Keys.F5:
                    return ClickCommand("Refresh");
                case Keys.Control | Keys.E:
                    return ClickCommand("Export to Excel");
                default:
                    return base.ProcessCmdKey(ref msg, keyData);
            }
        }

        /// <summary>Runs the current screen's command with this text; false when the screen has none (or it is disabled).</summary>
        private bool ClickCommand(string text)
        {
            var command = _current?.Commands.OfType<CommandButton>().FirstOrDefault(c => c.Text == text && c.Enabled && c.Visible);
            if (command == null)
            {
                return false;
            }

            command.PerformClick();
            return true;
        }

        /// <summary>"Data read at 10:42" at the end of the command bar, so it is clear how fresh the lists are.</summary>
        private void ShowLoadedTime()
        {
            var at = _security?.LoadedAt;
            _lblLoaded.Text = at.HasValue ? $"Data read at {at.Value:HH:mm}  ·  F5 to refresh" : string.Empty;
            _lblLoaded.Visible = at.HasValue;
        }

        /// <summary>
        /// Ticks the command-bar options the way they were left last time, and remembers every later change. Each tab of
        /// a workspace keeps its options under its own id.
        /// </summary>
        private void RestoreOptions(FeatureView view)
        {
            foreach (var part in view.Parts)
            {
                RestoreOptions(part.FeatureId, part);
            }
        }

        private void RestoreOptions(string featureId, FeatureView view)
        {
            foreach (var box in view.Commands.OfType<CheckBox>())
            {
                var saved = _state.Option(featureId, box.Text);
                if (saved.HasValue && box.Checked != saved.Value)
                {
                    box.Checked = saved.Value;
                }

                var option = box;
                box.CheckedChanged += (s, e) =>
                {
                    _state.SetOption(featureId, option.Text, option.Checked);
                    SaveSettings();
                };
            }
        }

        private static ToolkitSettings LoadSettings()
        {
            try
            {
                return SettingsManager.Instance.TryLoad(typeof(PluginControl), out ToolkitSettings settings, "Default") ? settings : new ToolkitSettings();
            }
            catch (Exception)
            {
                // Unreadable settings (an older format, a damaged file): start from the defaults.
                return new ToolkitSettings();
            }
        }

        private void SaveSettings()
        {
            try
            {
                SettingsManager.Instance.Save(typeof(PluginControl), _state.Settings, "Default");
            }
            catch (Exception)
            {
                // Remembering is a convenience: a failure to save must never interrupt the work.
            }
        }

        public override void ClosingPlugin(PluginCloseInfo info)
        {
            SaveSettings();
            base.ClosingPlugin(info);
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
                RestoreOptions(view);
                var created = view;
                created.CommandsChanged += (s, e) =>
                {
                    if (_current == created)
                    {
                        _banner.HideBanner();
                        ShowCommands(created);
                        RememberLocation(created);
                    }
                };
            }

            ShowCommands(view);

            _current?.SendToBack();
            _current = view;
            view.BringToFront();
            view.OnActivated();
            RememberLocation(view);
        }

        private void ShowCommands(FeatureView view)
        {
            _commandBar.SuspendLayout();
            _commandBar.Controls.Clear();
            foreach (var command in view.Commands)
            {
                _commandBar.Controls.Add(command);
            }

            _commandBar.Controls.Add(_lblLoaded);
            ShowLoadedTime();
            _commandBar.ResumeLayout();
        }

        /// <summary>The screen (or tab) in front is where the tool reopens next time in this environment.</summary>
        private void RememberLocation(FeatureView view)
        {
            _state.SetLastFeature(_environment, view.ScreenId);
            SaveSettings();
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
            ShowScreen(featureId);
        }

        void IFeatureHost.Open(string featureId, Subject subject)
        {
            _state.AddRecent(_environment, subject);
            ShowScreen(featureId)?.ShowSubject(subject);
        }

        /// <summary>Opens a screen by id: its menu entry, and the right tab when it is part of a workspace.</summary>
        private FeatureView ShowScreen(string featureId)
        {
            var menuId = FeatureRegistry.MenuIdOf(featureId);
            _rail.Select(menuId);
            if (!_views.TryGetValue(menuId, out var view))
            {
                return null;
            }

            if (!string.Equals(menuId, featureId, StringComparison.OrdinalIgnoreCase))
            {
                view.ShowPart(featureId);
            }

            return view;
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
                    ShowLoadedTime();
                }
            });
        }
    }
}
