using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using SecurityToolkit.UI;

namespace SecurityToolkit.Features
{
    /// <summary>
    /// The drop-downs that sentences share, so a privilege, a depth or a table is picked the same way on every screen.
    /// </summary>
    internal static class SentenceChoices
    {
        private sealed class Choice<T>
        {
            public Choice(T value, string text) { Value = value; Text = text; }
            public T Value { get; }
            public string Text { get; }
            public override string ToString() => Text;
        }

        /// <summary>The eight privileges, in the order the drop-downs list them.</summary>
        public static readonly PrivilegeKind[] Kinds = Enum.GetValues(typeof(PrivilegeKind)).Cast<PrivilegeKind>().ToArray();

        /// <summary>One privilege (Create, Read, ...), Read to start with.</summary>
        public static ComboBox Privilege()
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Name = "Privilege" };
            box.Items.AddRange(Kinds.Select(k => (object)new Choice<PrivilegeKind>(k, Privileges.Label(k))).ToArray());
            box.SelectedIndex = Array.IndexOf(Kinds, PrivilegeKind.Read);
            return box;
        }

        public static PrivilegeKind PrivilegeOf(ComboBox box) => ((Choice<PrivilegeKind>)box.SelectedItem).Value;

        /// <summary>Several privileges at once (one, several or all), Read to start with.</summary>
        public static CheckedDropDown PrivilegeList()
        {
            var box = new CheckedDropDown(Kinds.Select(Privileges.Label), "privileges") { Width = 150, Name = "Privileges" };
            box.SetChecked(new[] { Array.IndexOf(Kinds, PrivilegeKind.Read) });
            return box;
        }

        public static IReadOnlyList<PrivilegeKind> PrivilegesOf(CheckedDropDown box) => box.CheckedIndices.Select(i => Kinds[i]).ToList();

        /// <summary>The smallest reach that counts: User (any access) up to Organization.</summary>
        public static ComboBox Depth()
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, Name = "Depth" };
            box.Items.AddRange(new[] { PrivilegeDepth.User, PrivilegeDepth.BusinessUnit, PrivilegeDepth.ParentChildBusinessUnits, PrivilegeDepth.Organization }
                .Select(d => (object)new Choice<PrivilegeDepth>(d, Privileges.Label(d))).ToArray());
            box.SelectedIndex = 0;
            return box;
        }

        public static PrivilegeDepth DepthOf(ComboBox box) => ((Choice<PrivilegeDepth>)box.SelectedItem).Value;

        /// <summary>How a table reads in a pick box: "Account (account)".</summary>
        public static string TableText(TableInfo table) => $"{table.DisplayName} ({table.LogicalName})";
    }
}
