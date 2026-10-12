using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using SecurityToolkit.Services;

namespace SecurityToolkit.UI
{
    /// <summary>The save-dialog-then-write flow shared by every "Export to Excel" command.</summary>
    internal static class ExportUi
    {
        public static void Save(IWin32Window owner, string defaultFileName, string sheetName,
            IReadOnlyList<string> headers, IReadOnlyCollection<IReadOnlyList<string>> rows)
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
                    ExcelExporter.Write(dialog.FileName, sheetName, headers, rows);
                    MessageBox.Show(owner, $"Exported {rows.Count} row(s) to {Path.GetFileName(dialog.FileName)}.", "Export to Excel", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(owner, "Could not export: " + ex.Message, "Export to Excel", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>Exports what the grid currently shows: visible columns, current filter and sort.</summary>
        public static void SaveGrid(IWin32Window owner, FilterableGrid grid, string defaultFileName, string sheetName)
        {
            var table = grid.GetTable();
            Save(owner, defaultFileName, sheetName, table.Key, table.Value);
        }
    }
}
