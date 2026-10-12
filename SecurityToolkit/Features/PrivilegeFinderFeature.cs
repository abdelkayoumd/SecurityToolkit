using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using SecurityToolkit.UI;

namespace SecurityToolkit.Features
{
    /// <summary>R-05: pick a table and a privilege, and list the security roles that grant it at that depth or wider.</summary>
    internal sealed class PrivilegeFinderFeature : IFeature
    {
        public string Id => "privilege-finder";
        public string Title => "Privilege finder";
        public string Glyph => Glyphs.Lock;
        public int Order => 50;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new PrivilegeFinderView(host);
        }

        private sealed class Choice<T>
        {
            public Choice(T value, string text) { Value = value; Text = text; }
            public T Value { get; }
            public string Text { get; }
            public override string ToString() => Text;
        }

        private sealed class PrivilegeFinderView : DataView
        {
            private TableInfo _table;
            private ComboBox _cmbPrivilege;
            private ComboBox _cmbDepth;

            public PrivilegeFinderView(IFeatureHost host) : base(host)
            {
                InfoLabel.Text = "Select a table and a privilege to list the roles that grant it.";
            }

            protected override IEnumerable<Control> CreateCommands()
            {
                yield return Command(Theme.GlyphSearch, "Select table", PickTable, strong: true);

                _cmbPrivilege = Combo(Enum.GetValues(typeof(PrivilegeKind)).Cast<PrivilegeKind>().Select(k => new Choice<PrivilegeKind>(k, Privileges.Label(k))), 110);
                _cmbPrivilege.SelectedIndex = 1; // Read
                _cmbDepth = Combo(new[] { PrivilegeDepth.User, PrivilegeDepth.BusinessUnit, PrivilegeDepth.ParentChildBusinessUnits, PrivilegeDepth.Organization }
                    .Select(d => new Choice<PrivilegeDepth>(d, Privileges.Label(d))), 190);
                _cmbDepth.SelectedIndex = 0; // User or wider = any access

                yield return CommandLabel("Privilege");
                yield return _cmbPrivilege;
                yield return CommandLabel("At least");
                yield return _cmbDepth;
                yield return CommandSeparator();
                yield return Command(Theme.GlyphDownload, "Export to Excel", () => ExportUi.SaveGrid(this, Grid, "RolesWithPrivilege.xlsx", "Roles"));
            }

            private ComboBox Combo<T>(IEnumerable<Choice<T>> items, int width)
            {
                var box = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Width = width,
                    Font = Theme.FontRegular,
                    Margin = new Padding(2, 9, 6, 0)
                };
                box.Items.AddRange(items.Cast<object>().ToArray());
                box.SelectedIndexChanged += (s, e) => Find();
                return box;
            }

            protected override void Reset()
            {
                _table = null;
                base.Reset();
                InfoLabel.Text = "Select a table and a privilege to list the roles that grant it.";
            }

            protected override void OnGraphLoaded()
            {
            }

            private void PickTable()
            {
                Host.EnsureConnection(() => Host.RunAsync("Loading tables...", security => security.GetTables(), tables =>
                {
                    var picked = PickerDialog<TableInfo>.Pick(this, "Select a table", tables, false, "DisplayName=Table", "LogicalName=Logical name");
                    if (picked == null)
                    {
                        return;
                    }

                    _table = picked[0];
                    Find();
                }));
            }

            private void Find()
            {
                if (_table == null || _cmbPrivilege.SelectedItem == null || _cmbDepth.SelectedItem == null)
                {
                    return;
                }

                var table = _table;
                var kind = ((Choice<PrivilegeKind>)_cmbPrivilege.SelectedItem).Value;
                var depth = ((Choice<PrivilegeDepth>)_cmbDepth.SelectedItem).Value;

                Host.RunAsync("Finding roles...", security => security.FindRolesWithPrivilege(table.LogicalName, kind, depth), rows =>
                {
                    Grid.SetData(rows, "Role", "BusinessUnit=Business Unit", "Depth");
                    var wider = depth == PrivilegeDepth.Organization ? string.Empty : " or wider";
                    InfoLabel.Text = $"{rows.Count} role(s) grant {Privileges.Label(kind)} on {table.DisplayName} at {Privileges.Label(depth)} level{wider}";
                });
            }
        }
    }
}
