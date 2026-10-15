using System;
using System.Collections.Generic;
using System.Linq;

namespace SecurityToolkit.Services
{
    /// <summary>
    /// The "how to put it back" sheet saved before changes are applied: for every planned change, the change that
    /// reverses it. Changes the toolkit cannot reverse are listed too, marked as such, so nothing is silently missing.
    /// </summary>
    internal static class RestoreFile
    {
        public static readonly string[] Headers = { "To restore", "Who / what", "To", "Undoes the change", "Reversible" };

        public static List<IReadOnlyList<string>> Rows(ChangePlan plan)
        {
            // Last change first: that is the order in which an undo has to run.
            return plan.Items.AsEnumerable().Reverse().Select(item => item.Inverse != null
                ? (IReadOnlyList<string>)new[] { item.Inverse.Action, item.Inverse.Subject, item.Inverse.Target, item.Action, "Yes" }
                : new[] { "(cannot be undone)", item.Subject, item.Target, item.Action, "No" }).ToList();
        }

        public static string SuggestedName(DateTime now)
        {
            return $"SecurityToolkit-restore-{now:yyyyMMdd-HHmmss}.xlsx";
        }

        public static void Write(string path, ChangePlan plan)
        {
            ExcelExporter.Write(path, "Restore", Headers, Rows(plan));
        }
    }
}
