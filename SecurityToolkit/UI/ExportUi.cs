using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Features;
using SecurityToolkit.Services;

namespace SecurityToolkit.UI
{
    /// <summary>The save-dialog-then-write flow shared by every "Export to Excel" command.</summary>
    internal static class ExportUi
    {
        /// <summary>The environment the toolkit is connected to, written to the "About" sheet of every export. Set by the shell.</summary>
        public static string EnvironmentName { get; set; }

        public static void Save(IWin32Window owner, string defaultFileName, string sheetName,
            IReadOnlyList<string> headers, IReadOnlyCollection<IReadOnlyList<string>> rows, string description = null, bool colourDepths = false)
        {
            if (rows.Count == 0)
            {
                MessageBox.Show(owner, "There is nothing to export yet.", "Export to Excel", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dialog = new SaveFileDialog { Filter = "Excel Workbook (*.xlsx)|*.xlsx", FileName = defaultFileName })
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    ExcelExporter.Write(dialog.FileName, sheetName, headers, rows, Describe(owner, sheetName, description, colourDepths));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(owner, "Could not export: " + ex.Message, "Export to Excel", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var open = MessageBox.Show(owner, $"Exported {rows.Count} row(s) to {Path.GetFileName(dialog.FileName)}.\n\nOpen it now?", "Export to Excel",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2);
                if (open == DialogResult.Yes)
                {
                    try
                    {
                        Process.Start(dialog.FileName);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(owner, "The file was saved, but it could not be opened: " + ex.Message, "Export to Excel", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
        }

        /// <summary>
        /// Exports what the grid currently shows: visible columns, current filter and sort. Privilege depths keep
        /// their colours when the grid draws them. <paramref name="description"/> says in words what was asked.
        /// </summary>
        public static void SaveGrid(IWin32Window owner, FilterableGrid grid, string defaultFileName, string sheetName, string description = null)
        {
            var table = grid.GetTable();
            Save(owner, defaultFileName, sheetName, table.Key, table.Value, description, grid.BodyCellRenderer != null);
        }

        /// <summary>What the "About" sheet says: the screen the export came from, what was asked, and the environment.</summary>
        internal static ExportInfo Describe(IWin32Window owner, string sheetName, string description, bool colourDepths)
        {
            var featureId = (owner as FeatureView)?.FeatureId;
            var title = featureId == null ? null : FeatureRegistry.CreateAll().FirstOrDefault(f => f.Id == featureId)?.Title;
            return new ExportInfo
            {
                Title = string.IsNullOrEmpty(title) || string.Equals(title, sheetName, StringComparison.CurrentCultureIgnoreCase) ? sheetName : title + ": " + sheetName,
                Description = description,
                Environment = EnvironmentName,
                ColourDepths = colourDepths
            };
        }
    }
}
