using System;
using System.Collections.Generic;
using System.Linq;
using SecurityToolkit.Services;
using SecurityToolkit.UI;

namespace SecurityToolkit.Features
{
    /// <summary>
    /// The one path every write feature takes: review the plan, optionally save a restore file, apply, show what
    /// happened, and offer to undo. Nothing is sent to Dataverse unless the user approves the review.
    /// </summary>
    internal sealed class PlanRunner
    {
        private readonly IFeatureHost _host;
        private readonly IChangeUi _ui;

        public PlanRunner(IFeatureHost host, IChangeUi ui)
        {
            _host = host;
            _ui = ui;
        }

        /// <param name="plan">What to do.</param>
        /// <param name="refresh">Called once everything is finished, so the feature can reload what it shows.</param>
        public void Run(ChangePlan plan, Action refresh)
        {
            Run(plan, refresh, refreshIfNothingApplied: false);
        }

        /// <param name="refreshIfNothingApplied">
        /// True for an undo that follows changes which were already applied: if the user then backs out of the undo,
        /// the view still has to reload, because it is showing a state that no longer exists.
        /// </param>
        private void Run(ChangePlan plan, Action refresh, bool refreshIfNothingApplied)
        {
            void NothingApplied()
            {
                if (refreshIfNothingApplied)
                {
                    refresh?.Invoke();
                }
            }

            if (plan.Items.Count == 0)
            {
                _ui.Tell(NothingToDo(plan));
                NothingApplied();
                return;
            }

            if (!_ui.Review(plan, out var saveRestoreFile))
            {
                NothingApplied();
                return;
            }

            if (saveRestoreFile && !TrySaveRestoreFile(plan))
            {
                NothingApplied();
                return;
            }

            var items = plan.Items.ToList();
            _host.RunAsync($"Applying {items.Count} change(s)...", security => ChangeExecutor.Apply(security.Organization, items), results =>
            {
                // What Dataverse holds has changed; anything cached about it is stale.
                _host.Security?.Invalidate();

                var undo = _ui.ShowResults(results);
                if (undo)
                {
                    var done = results.Where(r => r.Success).Select(r => r.Item);
                    Run(ChangePlan.UndoOf(done), refresh, refreshIfNothingApplied: true);
                }
                else
                {
                    refresh?.Invoke();
                }
            });
        }

        /// <summary>False when the user backed out, which cancels the whole run.</summary>
        private bool TrySaveRestoreFile(ChangePlan plan)
        {
            var path = _ui.AskRestoreFilePath();
            if (path == null)
            {
                return _ui.ConfirmApplyWithoutRestoreFile();
            }

            try
            {
                RestoreFile.Write(path, plan);
                return true;
            }
            catch (Exception ex)
            {
                _ui.Tell("The restore file could not be saved, so nothing was changed: " + ex.Message);
                return false;
            }
        }

        private static string NothingToDo(ChangePlan plan)
        {
            var message = "There is nothing to change.";
            if (plan.Notes.Count > 0)
            {
                message += Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, plan.Notes.Take(12).Select(n => "• " + n));
                if (plan.Notes.Count > 12)
                {
                    message += Environment.NewLine + $"… and {plan.Notes.Count - 12} more.";
                }
            }

            return message;
        }
    }
}
