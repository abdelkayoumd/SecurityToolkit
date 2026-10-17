using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.UI;
using Label = System.Windows.Forms.Label;

namespace SecurityToolkit.Features
{
    /// <summary>
    /// The landing page: the questions an administrator asks, each opening the screen that answers it, grouped by
    /// what they are for. It also counts the security records, which proves the connection works.
    /// </summary>
    internal sealed class HomeFeature : IFeature
    {
        public string Id => "home";
        public string Title => "Home";
        public string Glyph => Glyphs.Home;
        public FeatureGroup Group => FeatureGroup.Start;
        public int Order => 0;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new HomeView(host);
        }

        /// <summary>One question, drawn as a card. Clicking it (or pressing Enter) opens its screen.</summary>
        private sealed class GuideCard : Control
        {
            private const int CardWidth = 300;
            private const int CardHeight = 104;
            private readonly string _question;
            private readonly string _screen;
            private readonly string _summary;
            private static readonly Font IconFont = new Font("Segoe MDL2 Assets", 16F);
            private readonly string _glyph;
            private bool _hot;

            public GuideCard(GuideEntry entry, string screenTitle, string glyph)
            {
                _glyph = glyph;
                _question = entry.Question;
                _screen = screenTitle;
                _summary = entry.Summary;
                Name = "Card:" + entry.FeatureId;
                Tag = entry.FeatureId;
                Size = new Size(CardWidth, CardHeight);
                Margin = new Padding(0, 0, 12, 12);
                Cursor = Cursors.Hand;
                TabStop = true;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            public string Question => _question;

            protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

            protected override bool IsInputKey(Keys keyData)
            {
                return keyData == Keys.Enter || keyData == Keys.Space || base.IsInputKey(keyData);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
                {
                    OnClick(EventArgs.Empty);
                    e.Handled = true;
                }

                base.OnKeyDown(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
                using (var fill = new SolidBrush(_hot ? Theme.AccentSelected : Theme.PanelBackground))
                using (var border = new Pen(_hot || Focused ? Theme.Accent : Theme.Border))
                {
                    g.FillRectangle(fill, bounds);
                    g.DrawRectangle(border, bounds);
                }

                var flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis;
                // The screen's own icon, as in the menu, so a card and its menu item are recognisably the same thing.
                TextRenderer.DrawText(g, _glyph, IconFont, new Rectangle(12, 12, 28, 28), Theme.Accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, _question, Theme.FontBold, new Rectangle(48, 10, Width - 60, 40), Theme.TextPrimary, flags);
                TextRenderer.DrawText(g, _screen + "  ›", Theme.FontSmall, new Rectangle(48, 34, Width - 60, 16), Theme.Accent, flags);
                TextRenderer.DrawText(g, _summary, Theme.FontSmall, new Rectangle(48, 54, Width - 60, 44), Theme.TextSecondary, flags);
            }
        }

        private sealed class HomeView : FeatureView
        {
            private readonly Label _summary;

            public HomeView(IFeatureHost host) : base(host)
            {
                var header = new Panel { Dock = DockStyle.Top, Height = 132 };
                header.Controls.Add(new Label
                {
                    Text = "Security Toolkit",
                    Font = Theme.FontTitle,
                    ForeColor = Theme.TextPrimary,
                    AutoSize = true,
                    Location = new Point(24, 20)
                });
                header.Controls.Add(new Label
                {
                    Text = "Pick the question you want answered, or choose a screen from the menu on the left. " +
                           "Every screen except those under CHANGE only reads. Nothing is changed unless you use a Change screen, and every change is reviewed first.",
                    ForeColor = Theme.TextSecondary,
                    AutoSize = false,
                    Size = new Size(900, 40),
                    Location = new Point(26, 58)
                });
                _summary = new Label
                {
                    Name = "HomeSummary",
                    Text = "Not loaded yet.",
                    ForeColor = Theme.TextPrimary,
                    AutoSize = true,
                    Location = new Point(26, 104)
                };
                header.Controls.Add(_summary);

                // Sections stacked top to bottom; each is a heading over a row of cards that wraps to the width.
                var cards = new FlowLayoutPanel
                {
                    Name = "HomeCards",
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    Padding = new Padding(24, 0, 12, 12)
                };
                var rows = new List<FlowLayoutPanel>();

                var known = FeatureRegistry.CreateAll().ToDictionary(f => f.Id);
                foreach (var section in Enum.GetValues(typeof(GuideSection)).Cast<GuideSection>())
                {
                    var entries = FeatureGuide.All.Where(e => e.Section == section && known.ContainsKey(e.FeatureId)).ToList();
                    if (entries.Count == 0)
                    {
                        continue;
                    }

                    var heading = new Label
                    {
                        Text = FeatureGuide.Heading(section).ToUpperInvariant(),
                        Font = Theme.FontBold,
                        ForeColor = Theme.TextSecondary,
                        AutoSize = true,
                        Margin = new Padding(0, 10, 0, 8)
                    };
                    cards.Controls.Add(heading);

                    var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0), WrapContents = true };
                    foreach (var entry in entries)
                    {
                        var card = new GuideCard(entry, known[entry.FeatureId].Title, known[entry.FeatureId].Glyph);
                        var target = entry.FeatureId;
                        card.Click += (s, e) => Host.Navigate(target);
                        row.Controls.Add(card);
                    }

                    rows.Add(row);
                    cards.Controls.Add(row);
                }

                void FitRows()
                {
                    var width = Math.Max(340, cards.ClientSize.Width - cards.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
                    foreach (var row in rows)
                    {
                        row.MaximumSize = new Size(width, 0);
                    }
                }

                cards.Resize += (s, e) => FitRows();
                FitRows();

                // The last control added is laid out first, so the header ends up above the cards.
                Controls.Add(cards);
                Controls.Add(header);
            }

            protected override IEnumerable<Control> CreateCommands()
            {
                var load = new CommandButton { Glyph = Theme.GlyphRefresh, Text = "Load security data", Strong = true };
                load.Click += (s, e) => Host.EnsureConnection(LoadCounts);
                yield return load;
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
