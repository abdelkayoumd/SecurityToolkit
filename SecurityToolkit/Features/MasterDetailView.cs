using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Services;
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
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, MasterWidthPercent));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100 - MasterWidthPercent));
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

            Grid.EmptyMessageProvider = () => Host.Security == null ? "Connect to an environment to get started." : EmptyText;
            EnableOpenIn(Master);
            Master.DataBound += (s, e) => ApplyPendingSubject();
        }

        private Subject _pending;
        private bool _applying;

        /// <summary>
        /// Selects <paramref name="subject"/> in the list on the left: at once when the list is filled, otherwise as
        /// soon as it is. Filters that hide it are cleared, and disabled users are included when that is what hid it.
        /// </summary>
        public override void ShowSubject(Subject subject)
        {
            if (subject == null || !Accepts(subject.Kind))
            {
                return;
            }

            _pending = subject;
            PrepareForSubject(subject);
            ApplyPendingSubject();
        }

        /// <summary>True when the list on the left can hold this kind of subject.</summary>
        protected virtual bool Accepts(SubjectKind kind)
        {
            return SubjectActions.All.Any(a => a.Kind == kind && string.Equals(a.FeatureId, FeatureId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>A screen whose list can show different things (users or teams) switches it to the subject's kind here.</summary>
        protected virtual void PrepareForSubject(Subject subject)
        {
        }

        private void ApplyPendingSubject()
        {
            var subject = _pending;
            if (subject == null || _applying || !IsLoaded)
            {
                return;
            }

            _applying = true;
            try
            {
                Master.ClearFilters();
                if (Master.SelectWhere(row => Subjects.Matches(subject, row)) == 0 && subject.Kind == SubjectKind.User && RevealDisabledUsers())
                {
                    Master.SelectWhere(row => Subjects.Matches(subject, row));
                }

                if (!Master.GetSelected<object>().Any(row => Subjects.Matches(subject, row)))
                {
                    Host.ShowMessage(BannerKind.Warning, $"{subject.Name} is not in the list of this screen.");
                }
            }
            finally
            {
                _pending = null;
                _applying = false;
            }
        }

        /// <summary>Ticks "Include disabled users" when the screen has that option and it is not ticked yet; true when it did.</summary>
        private bool RevealDisabledUsers()
        {
            var box = Commands.OfType<CheckBox>().FirstOrDefault(c => c.Text == "Include disabled users");
            if (box == null || box.Checked)
            {
                return false;
            }

            box.Checked = true; // refills the list
            return true;
        }

        /// <summary>How much of the width the list on the left takes, in percent. A screen whose results need room can narrow it.</summary>
        protected virtual int MasterWidthPercent => 36;

        /// <summary>The list you pick from.</summary>
        protected FilterableGrid Master { get; }

        /// <summary>The selection in <see cref="Master"/> changed (and settled). Not called for an empty selection.</summary>
        protected abstract void OnMasterSelectionChanged();

        /// <summary>What to tell the user while nothing is picked yet.</summary>
        protected abstract string PromptText { get; }

        /// <summary>What the results area says while it has no rows: what to do when nothing is picked, or that nothing was found.</summary>
        protected virtual string EmptyText => Master.SelectedCount == 0 ? PromptText : "Nothing found for this selection.";

        public override void OnActivated()
        {
            _activated = true;
            Grid.RefreshEmptyMessage();
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
