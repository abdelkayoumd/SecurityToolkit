using System;
using System.Drawing;
using System.Windows.Forms;
using SecurityToolkit.UI;
using Label = System.Windows.Forms.Label;

namespace SecurityToolkit.Features
{
    /// <summary>The landing page. For now it also proves the shared data layer by counting the security records.</summary>
    internal sealed class HomeFeature : IFeature
    {
        public string Id => "home";
        public string Title => "Home";
        public string Glyph => Glyphs.Home;
        public int Order => 0;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new HomeView(host);
        }

        private sealed class HomeView : FeatureView
        {
            private readonly Label _summary;

            public HomeView(IFeatureHost host) : base(host)
            {
                Padding = new Padding(24);

                var title = new Label
                {
                    Text = "Security Toolkit",
                    Font = Theme.FontTitle,
                    ForeColor = Theme.TextPrimary,
                    AutoSize = true,
                    Location = new Point(24, 24)
                };
                var intro = new Label
                {
                    Text = "One place for security administration. Pick a feature in the menu on the left.",
                    ForeColor = Theme.TextSecondary,
                    AutoSize = true,
                    Location = new Point(26, 64)
                };
                var refresh = new CommandButton { Glyph = Theme.GlyphRefresh, Text = "Load security data", Strong = true, Location = new Point(18, 100) };
                refresh.Click += (s, e) => Host.EnsureConnection(LoadCounts);
                _summary = new Label
                {
                    Text = "Not loaded yet.",
                    ForeColor = Theme.TextPrimary,
                    AutoSize = true,
                    Location = new Point(26, 152)
                };

                Controls.Add(title);
                Controls.Add(intro);
                Controls.Add(refresh);
                Controls.Add(_summary);
            }

            public override void OnConnectionChanged()
            {
                _summary.Text = "Not loaded yet.";
            }

            private void LoadCounts()
            {
                Host.RunAsync("Loading security data...", security =>
                {
                    security.Invalidate();
                    return new[] { security.GetUsers().Count, security.GetRoles().Count, security.GetTeams().Count, security.GetBusinessUnits().Count };
                }, counts =>
                {
                    _summary.Text = $"{counts[0]} users   |   {counts[1]} security roles   |   {counts[2]} teams   |   {counts[3]} business units";
                });
            }
        }
    }
}
