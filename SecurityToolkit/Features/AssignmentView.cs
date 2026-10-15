using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Services;
using SecurityToolkit.UI;

namespace SecurityToolkit.Features
{
    /// <summary>One entry of the "what" list: a role or a team, and how many of the chosen subjects already have it.</summary>
    internal sealed class PickRow
    {
        public Guid Key { get; set; }
        public string Name { get; set; }
        public string Detail { get; set; }
        public string BusinessUnit { get; set; }
        /// <summary>"2 of 5" for the subjects selected on the left.</summary>
        public string Assigned { get; set; }
    }

    /// <summary>
    /// A write feature built from two lists: pick who on the left (users or teams), what on the right (roles or
    /// teams), then Add or Remove. The right list shows how many of the chosen subjects already have each item.
    /// Every change goes through <see cref="PlanRunner"/>: reviewed first, applied only if approved.
    /// </summary>
    internal abstract class AssignmentView : MasterDetailView
    {
        private readonly string _subjectNoun;
        private readonly string _itemNoun;
        private readonly string _addText;
        private readonly string _removeText;
        private List<PickRow> _rows = new List<PickRow>();

        protected AssignmentView(IFeatureHost host, string leftTitle, string rightTitle, string subjectNoun, string itemNoun, string addText, string removeText)
            : base(host, leftTitle, rightTitle)
        {
            _subjectNoun = subjectNoun;
            _itemNoun = itemNoun;
            _addText = addText;
            _removeText = removeText;
            InfoLabel.Text = "Connect to an environment to get started.";
        }

        /// <summary>Fills the left list.</summary>
        protected abstract void FillSubjects();

        /// <summary>The entries of the right list.</summary>
        protected abstract IEnumerable<PickRow> BuildRows();

        protected abstract string[] RowColumns { get; }

        protected abstract IReadOnlyList<Guid> SelectedSubjectIds();

        protected abstract bool Has(Guid subjectId, PickRow row);

        /// <summary>The plan for adding (or removing) the chosen items to (from) the selected subjects.</summary>
        protected abstract ChangePlan Plan(bool add, IReadOnlyList<PickRow> items);

        /// <summary>Extra commands (a checkbox, say) a feature wants after the common ones.</summary>
        protected virtual IEnumerable<Control> ExtraCommands()
        {
            return Enumerable.Empty<Control>();
        }

        protected override string PromptText =>
            $"Select one or more {_subjectNoun}s on the left and {_itemNoun}s on the right, then choose {_addText} or {_removeText}.";

        protected override IEnumerable<Control> CreateCommands()
        {
            yield return Command(Theme.GlyphRefresh, "Refresh", () => WithGraph(OnGraphLoaded, refresh: true));
            yield return CommandSeparator();
            yield return Command(Glyphs.Add, _addText, () => Apply(add: true), strong: true);
            yield return Command(Glyphs.Remove, _removeText, () => Apply(add: false), strong: true);
            yield return CommandSeparator();
            yield return Command(Theme.GlyphClearSelection, "Clear selection", () => { Master.ClearSelection(); Grid.ClearSelection(); });
            foreach (var command in ExtraCommands())
            {
                yield return command;
            }
        }

        protected override void Reset()
        {
            _rows = new List<PickRow>();
            base.Reset();
        }

        protected override void OnGraphLoaded()
        {
            if (Graph == null)
            {
                return;
            }

            FillSubjects();
            _rows = BuildRows().ToList();
            Grid.SetData(_rows, RowColumns);
            UpdateAssigned();
            InfoLabel.Text = PromptText;
        }

        protected override void OnMasterSelectionChanged()
        {
            UpdateAssigned();
        }

        /// <summary>Counts, per right-hand entry, how many of the selected subjects already have it.</summary>
        private void UpdateAssigned()
        {
            var ids = SelectedSubjectIds();
            foreach (var row in _rows)
            {
                row.Assigned = ids.Count == 0 ? string.Empty : $"{ids.Count(id => Has(id, row))} of {ids.Count}";
            }

            Grid.RefreshCells();
        }

        private void Apply(bool add)
        {
            WithGraph(() =>
            {
                var subjects = SelectedSubjectIds();
                var items = Grid.GetSelected<PickRow>();
                if (subjects.Count == 0 || items.Count == 0)
                {
                    Host.CreateChangeUi(this).Tell($"Select at least one {_subjectNoun} on the left and one {_itemNoun} on the right first.");
                    return;
                }

                var plan = Plan(add, items);
                new PlanRunner(Host, Host.CreateChangeUi(this)).Run(plan, () => WithGraph(OnGraphLoaded, refresh: true));
            });
        }
    }
}
