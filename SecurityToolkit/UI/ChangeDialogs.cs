using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Services;
using Label = System.Windows.Forms.Label;

namespace SecurityToolkit.UI
{
    /// <summary>
    /// Everything the user is asked and told while a write operation runs. A single seam, so the logic that
    /// sequences it can be tested without showing any windows.
    /// </summary>
    internal interface IChangeUi
    {
        /// <summary>Shows the plan; true when the user approves it. <paramref name="saveRestoreFile"/> is their choice about a restore file.</summary>
        bool Review(ChangePlan plan, out bool saveRestoreFile);

        /// <summary>Asks where to save the restore file; null when the user declines to pick a place.</summary>
        string AskRestoreFilePath();

        /// <summary>The user chose not to save the restore file: should the changes be applied anyway?</summary>
        bool ConfirmApplyWithoutRestoreFile();

        /// <summary>Shows what happened; true when the user asked to undo the successful changes.</summary>
        bool ShowResults(IReadOnlyList<ChangeResult> results);

        void Tell(string message);
    }

    /// <summary>The real windows.</summary>
    internal sealed class WinFormsChangeUi : IChangeUi
    {
        private readonly IWin32Window _owner;

        public WinFormsChangeUi(IWin32Window owner)
        {
            _owner = owner;
        }

        public bool Review(ChangePlan plan, out bool saveRestoreFile)
        {
            using (var dialog = new ReviewDialog(plan))
            {
                var approved = dialog.ShowDialog(_owner) == DialogResult.OK;
                saveRestoreFile = dialog.SaveRestoreFile;
                return approved;
            }
        }

        public string AskRestoreFilePath()
        {
            using (var dialog = new SaveFileDialog
            {
                Title = "Save the restore file",
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                FileName = RestoreFile.SuggestedName(DateTime.Now)
            })
            {
                return dialog.ShowDialog(_owner) == DialogResult.OK ? dialog.FileName : null;
            }
        }

        public bool ConfirmApplyWithoutRestoreFile()
        {
            return MessageBox.Show(_owner, "No restore file was saved. Apply the changes anyway?", "Security Toolkit",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        public bool ShowResults(IReadOnlyList<ChangeResult> results)
        {
            using (var dialog = new ResultDialog(results))
            {
                dialog.ShowDialog(_owner);
                return dialog.UndoRequested;
            }
        }

        public void Tell(string message)
        {
            MessageBox.Show(_owner, message, "Security Toolkit", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    /// <summary>What is about to happen: every change, anything skipped and why, and a warning for what cannot be undone.</summary>
    internal sealed class ReviewDialog : ToolkitDialog
    {
        private readonly CheckBox _restore;

        public ReviewDialog(ChangePlan plan)
            : base($"Review {plan.Items.Count} change(s) before applying", new Size(860, 600))
        {
            var buttons = CreateButtonRow(out var apply, $"Apply {plan.Items.Count} change(s)", out var cancel, "Cancel");
            apply.AutoSize = true;

            _restore = new CheckBox
            {
                Text = "Save a restore file first (how to put everything back)",
                Checked = true,
                AutoSize = true,
                ForeColor = Theme.TextPrimary,
                Margin = new Padding(8, 8, 0, 0)
            };

            var list = ChangeList(plan.Items.Select(i => new[] { i.Action, i.Subject, i.Target }), new[] { "Action", "Who / what", "To" });

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 0, BackColor = Theme.PageBackground };
            var bottomHeight = 0;

            if (plan.Items.Any(i => i.Inverse == null))
            {
                var warning = new Label
                {
                    Text = "Some of these changes cannot be undone by the toolkit.",
                    Dock = DockStyle.Top,
                    Height = 28,
                    Padding = new Padding(10, 0, 0, 0),
                    TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = Theme.Danger,
                    BackColor = Theme.DangerBackground,
                    Font = Theme.FontBold
                };
                bottom.Controls.Add(warning);
                bottomHeight += 28;
            }

            if (plan.Notes.Count > 0)
            {
                var notes = new TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    Dock = DockStyle.Top,
                    Height = 96,
                    BorderStyle = BorderStyle.None,
                    BackColor = Theme.WarningBackground,
                    ForeColor = Theme.Warning,
                    Text = $"Notes ({plan.Notes.Count}):{Environment.NewLine}" + string.Join(Environment.NewLine, plan.Notes.Select(n => "• " + n))
                };
                bottom.Controls.Add(notes);
                bottomHeight += 96;
            }

            bottom.Height = bottomHeight;
            var restoreRow = new Panel { Dock = DockStyle.Bottom, Height = 32, BackColor = Theme.PanelBackground };
            restoreRow.Controls.Add(_restore);

            // The last control added is laid out first: heading on top, buttons at the very bottom.
            Controls.Add(list);
            Controls.Add(bottom);
            Controls.Add(restoreRow);
            Controls.Add(buttons);
            Controls.Add(CreateHeading(Text));
            AcceptButton = null; // approving a write must be a deliberate click, never a stray Enter
            CancelButton = cancel;
        }

        public bool SaveRestoreFile => _restore.Checked;

        internal static ListView ChangeList(IEnumerable<string[]> rows, string[] headers)
        {
            var list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                BorderStyle = BorderStyle.None,
                Font = Theme.FontRegular
            };
            for (var i = 0; i < headers.Length; i++)
            {
                list.Columns.Add(headers[i], i == headers.Length - 1 ? 400 : 200);
            }

            foreach (var row in rows)
            {
                list.Items.Add(new ListViewItem(row));
            }

            return list;
        }
    }

    /// <summary>What happened to each change, with the option to undo what succeeded.</summary>
    internal sealed class ResultDialog : ToolkitDialog
    {
        public ResultDialog(IReadOnlyList<ChangeResult> results)
            : base(Heading(results), new Size(860, 540))
        {
            var succeeded = results.Where(r => r.Success).ToList();
            var undoable = succeeded.Count(r => r.Item.Inverse != null);

            var buttons = CreateButtonRow(out var close, "Close", out var undo, undoable > 0 ? $"Undo {undoable} change(s)" : "Undo");
            undo.AutoSize = true;
            undo.Visible = undoable > 0;
            undo.DialogResult = DialogResult.Retry;
            close.DialogResult = DialogResult.OK;

            var list = ReviewDialog.ChangeList(
                results.Select(r => new[] { r.Item.Action, r.Item.Subject + " → " + r.Item.Target, r.Success ? "Done" : "Failed: " + r.Error }),
                new[] { "Action", "Change", "Result" });
            for (var i = 0; i < results.Count; i++)
            {
                if (!results[i].Success)
                {
                    list.Items[i].ForeColor = Theme.Danger;
                }
            }

            Controls.Add(list);
            Controls.Add(buttons);
            Controls.Add(CreateHeading(Text));
            AcceptButton = close;
        }

        /// <summary>The user clicked Undo.</summary>
        public bool UndoRequested => DialogResult == DialogResult.Retry;

        private static string Heading(IReadOnlyList<ChangeResult> results)
        {
            var failed = results.Count(r => !r.Success);
            return failed == 0 ? $"All {results.Count} change(s) succeeded" : $"{results.Count - failed} succeeded, {failed} failed";
        }
    }
}
