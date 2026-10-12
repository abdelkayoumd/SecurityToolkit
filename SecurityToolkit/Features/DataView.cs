using System;
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

        /// <summary>The loaded data, or null until a command has loaded it for the current connection.</summary>
        protected SecurityGraph Graph { get; private set; }

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
            box.CheckedChanged += (s, e) => changed();
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
