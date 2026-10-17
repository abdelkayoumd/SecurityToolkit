using System;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using SecurityToolkit.Services;
using SecurityToolkit.UI;
using Label = System.Windows.Forms.Label;

namespace SecurityToolkit.Features
{
    /// <summary>
    /// The common shape of a read-only feature: a one-line description of what is shown, and a filterable grid
    /// below it. Subclasses add their commands and decide what goes in the grid.
    /// </summary>
    internal abstract class DataView : FeatureView
    {
        protected DataView(IFeatureHost host) : base(host)
        {
            Grid = new FilterableGrid();
            InfoLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                Padding = new Padding(10, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.TextSecondary,
                Font = Theme.FontBold
            };

            // The last control added is laid out first, so the label ends up above the grid.
            Controls.Add(Grid);
            Controls.Add(InfoLabel);
        }

        protected FilterableGrid Grid { get; }
        protected Label InfoLabel { get; }

        private Panel _helpBar;

        /// <summary>
        /// A line under the command bar saying what this screen does, with a "How it works" link that opens the steps.
        /// Built once the shell has told the view which feature it is; a feature without guide text shows nothing.
        /// </summary>
        protected override void OnFeatureIdChanged()
        {
            var entry = FeatureGuide.Find(FeatureId);
            if (entry == null || _helpBar != null)
            {
                return;
            }

            var steps = string.Join(Environment.NewLine, entry.Steps.Select((s, i) => $"{i + 1}.  {s}"));
            var details = new Label
            {
                Name = "HelpDetails",
                Dock = DockStyle.Top,
                Visible = false,
                Text = steps,
                ForeColor = Theme.TextPrimary,
                BackColor = Theme.PanelBackground,
                Padding = new Padding(12, 8, 12, 8),
                Height = 24 + 20 * entry.Steps.Sum(s => 1 + s.Length / 150)
            };

            var toggle = new LinkLabel
            {
                Name = "HelpToggle",
                Text = "? How it works",
                Dock = DockStyle.Right,
                Width = 120,
                TextAlign = ContentAlignment.MiddleCenter,
                LinkColor = Theme.Accent,
                Font = Theme.FontSmall
            };
            toggle.LinkClicked += (s, e) =>
            {
                details.Visible = !details.Visible;
                toggle.Text = details.Visible ? "Hide" : "? How it works";
            };

            var summary = new Label
            {
                Name = "HelpSummary",
                Dock = DockStyle.Fill,
                Text = entry.Summary,
                ForeColor = Theme.TextSecondary,
                Font = Theme.FontSmall,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0),
                AutoEllipsis = true
            };

            _helpBar = new Panel { Name = "HelpBar", Dock = DockStyle.Top, Height = 28, BackColor = Theme.PageBackground };
            _helpBar.Controls.Add(summary);
            _helpBar.Controls.Add(toggle);

            // The last control added is laid out first, so the bar ends up above the description and the lists.
            Controls.Add(details);
            Controls.Add(_helpBar);
        }

        /// <summary>The loaded data, or null until a command has loaded it for the current connection.</summary>
        protected SecurityGraph Graph { get; set; }

        /// <summary>Called when data (re)loaded; subclasses redraw the grid from <see cref="Graph"/>.</summary>
        protected abstract void OnGraphLoaded();

        /// <summary>The connection changed; drop everything that was shown.</summary>
        protected virtual void Reset()
        {
            Graph = null;
            InfoLabel.Text = string.Empty;
            Grid.SetData(new object[0]);
        }

        public override void OnConnectionChanged()
        {
            Reset();
        }

        /// <summary>Loads (or reloads, with <paramref name="refresh"/>) the security data, then runs <paramref name="then"/>.</summary>
        protected void WithGraph(Action then, bool refresh = false)
        {
            Host.EnsureConnection(() => Host.RunAsync("Loading security data...", security =>
            {
                if (refresh)
                {
                    security.Invalidate();
                }

                return security.GetGraph();
            }, graph =>
            {
                Graph = graph;
                then();
            }));
        }

        protected CommandButton Command(string glyph, string text, Action click, bool strong = false)
        {
            var button = new CommandButton { Glyph = glyph, Text = text, Strong = strong };
            button.Click += (s, e) => click();
            return button;
        }

        protected static CheckBox CommandCheck(string text, Action changed, bool isChecked = false)
        {
            var box = new CheckBox
            {
                Text = text,
                AutoSize = true,
                Checked = isChecked,
                Font = Theme.FontRegular,
                ForeColor = Theme.TextPrimary,
                Margin = new Padding(10, 11, 6, 0)
            };
            box.CheckedChanged += (s, e) => changed?.Invoke();
            return box;
        }

        protected static Label CommandLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = Theme.FontRegular,
                ForeColor = Theme.TextSecondary,
                Margin = new Padding(10, 13, 2, 0)
            };
        }
    }
}
