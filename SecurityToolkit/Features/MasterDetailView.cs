using System;
using System.Drawing;
using System.Windows.Forms;
using SecurityToolkit.UI;
using Label = System.Windows.Forms.Label;

namespace SecurityToolkit.Features
{
    /// <summary>
    /// A feature where you pick things in a list on the left (teams, roles, business units...) and the results
    /// for the picked ones appear on the right. No popups: the list fills when the feature opens, and the right
    /// side follows the selection a moment after it settles.
    /// </summary>
    internal abstract class MasterDetailView : DataView
    {
        private readonly Timer _selectionTimer;
        private bool _activated;

        protected MasterDetailView(IFeatureHost host, string masterTitle, string detailTitle) : base(host)
        {
            Master = new FilterableGrid();

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            Controls.Remove(Grid);
            layout.Controls.Add(Side(masterTitle, Master), 0, 0);
            layout.Controls.Add(Side(detailTitle, Grid), 1, 0);
            Controls.Add(layout);
            Controls.SetChildIndex(layout, 0);

            // The short delay lets a drag or Shift-click settle before the right side is rebuilt.
            _selectionTimer = new Timer { Interval = 200 };
            _selectionTimer.Tick += (s, e) =>
            {
                _selectionTimer.Stop();
                if (IsLoaded)
                {
                    OnMasterSelectionChanged();
                }
            };
            Master.SelectionChanged += (s, e) =>
            {
                _selectionTimer.Stop();
                _selectionTimer.Start();
            };
        }

        /// <summary>The list you pick from.</summary>
        protected FilterableGrid Master { get; }

        /// <summary>The selection in <see cref="Master"/> changed (and settled). Not called for an empty selection.</summary>
        protected abstract void OnMasterSelectionChanged();

        /// <summary>What to tell the user while nothing is picked yet.</summary>
        protected abstract string PromptText { get; }

        public override void OnActivated()
        {
            _activated = true;
            if (!IsLoaded)
            {
                AutoLoad();
            }
        }

        public override void OnConnectionChanged()
        {
            base.OnConnectionChanged();
            if (_activated)
            {
                AutoLoad();
            }
        }

        protected override void Reset()
        {
            Master.SetData(new object[0]);
            base.Reset();
            InfoLabel.Text = Host.Security == null ? "Connect to an environment to get started." : PromptText;
        }

        /// <summary>True once the left list has its data. By default that is the security graph; features that list something else override it.</summary>
        protected virtual bool IsLoaded => Graph != null;

        /// <summary>Fetches what the left list shows (on a background thread) and fills it. By default: the security graph.</summary>
        protected virtual void LoadData()
        {
            Host.RunAsync("Loading security data...", security => security.GetGraph(), graph =>
            {
                Graph = graph;
                OnGraphLoaded();
            });
        }

        /// <summary>Fills the left list when there is already a connection; otherwise waits for the user to connect.</summary>
        private void AutoLoad()
        {
            if (Host.Security == null)
            {
                InfoLabel.Text = "Connect to an environment to get started.";
                return;
            }

            LoadData();
        }

        private static Control Side(string title, Control content)
        {
            var side = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            content.Dock = DockStyle.Fill;
            var heading = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 28,
                Padding = new Padding(10, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = Theme.FontBold,
                ForeColor = Theme.TextPrimary,
                BackColor = Theme.PageBackground
            };

            // The last control added is laid out first, so the heading ends up above the list.
            side.Controls.Add(content);
            side.Controls.Add(heading);
            return side;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _selectionTimer.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
