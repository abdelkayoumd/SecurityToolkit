using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Services;
using SecurityToolkit.UI;

namespace SecurityToolkit.Features
{
    /// <summary>
    /// The business-unit hierarchy as a tree, each unit with how many users and teams it holds. Select a unit and the
    /// right lists what is in it, as you choose: its users (with their teams and roles), its teams, or the security
    /// roles in use there. Read-only.
    /// </summary>
    internal sealed class BusinessUnitTreeFeature : IFeature
    {
        public string Id => "business-unit-tree";
        public string Title => "Business units";
        public string Glyph => Glyphs.Business;
        public FeatureGroup Group => FeatureGroup.LookUp;
        public int Order => 40;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new BusinessUnitTreeView(host);
        }

        private sealed class BusinessUnitTreeView : DataView
        {
            private readonly ImageList _icons = TreeIcons.Create();
            private readonly TreeView _tree;
            private bool _activated;
            private CheckBox _chkChildren;
            private CheckBox _chkDisabled;
            private ComboBox _cmbShow;
            private List<UserAccessRow> _detail = new List<UserAccessRow>();

            public BusinessUnitTreeView(IFeatureHost host) : base(host)
            {
                _tree = new TreeView
                {
                    Dock = DockStyle.Fill,
                    BorderStyle = BorderStyle.None,
                    BackColor = Theme.PanelBackground,
                    ForeColor = Theme.TextPrimary,
                    Font = Theme.FontRegular,
                    ShowLines = false,
                    ItemHeight = 24,
                    ShowNodeToolTips = true,
                    HideSelection = false,
                    ImageList = _icons
                };
                _tree.AfterSelect += (s, e) => ShowContents();
                Grid.EmptyMessageProvider = () => Host.Security == null ? "Connect to an environment to get started." : "Select a business unit in the tree to see its users, teams and roles.";

                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

                Controls.Remove(Grid);
                Grid.Dock = DockStyle.Fill;
                layout.Controls.Add(_tree, 0, 0);
                layout.Controls.Add(Grid, 1, 0);
                Controls.Add(layout);
                Controls.SetChildIndex(layout, 0);

                InfoLabel.Text = "Connect to an environment to get started.";
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _icons.Dispose();
                }

                base.Dispose(disposing);
            }

            protected override IEnumerable<Control> CreateCommands()
            {
                yield return Command(Theme.GlyphRefresh, "Refresh", () => WithGraph(OnGraphLoaded, refresh: true), strong: true);
                yield return CommandSeparator();
                yield return Command(Theme.GlyphExpand, "Expand all", () => _tree.ExpandAll());
                yield return Command(Theme.GlyphCollapse, "Collapse all", () => _tree.CollapseAll());
                yield return CommandSeparator();
                yield return Command(Theme.GlyphDownload, "Export to Excel", () => ExportUi.SaveGrid(this, Grid, "BusinessUnit.xlsx", "Business unit"));
                yield return Command(Theme.GlyphDownload, "Export users in detail", ExportDetailed);
                yield return new Label { Text = "Show", AutoSize = true, ForeColor = Theme.TextSecondary, Margin = new Padding(14, 12, 4, 0) };
                _cmbShow = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130, Margin = new Padding(0, 8, 8, 0), Font = Theme.FontRegular, Name = "ShowChoice" };
                _cmbShow.Items.AddRange(new object[] { "Users", "Teams", "Roles in use" });
                _cmbShow.SelectedIndex = 0;
                _cmbShow.SelectedIndexChanged += (s, e) => ShowContents();
                yield return _cmbShow;
                _chkChildren = CommandCheck("Include child business units", ShowContents, isChecked: true);
                _chkDisabled = CommandCheck("Include disabled users", ShowContents);
                yield return _chkChildren;
                yield return _chkDisabled;
            }

            public override void OnActivated()
            {
                _activated = true;
                Grid.RefreshEmptyMessage();
                if (Graph == null)
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
                _tree.Nodes.Clear();
                base.Reset();
                InfoLabel.Text = Host.Security == null ? "Connect to an environment to get started." : PromptText;
            }

            private static string PromptText => "Select a business unit to see its users, teams and the security roles in use there.";

            private void AutoLoad()
            {
                if (Host.Security == null)
                {
                    InfoLabel.Text = "Connect to an environment to get started.";
                    return;
                }

                Host.RunAsync("Loading security data...", security => security.GetGraph(), graph =>
                {
                    Graph = graph;
                    OnGraphLoaded();
                });
            }

            protected override void OnGraphLoaded()
            {
                if (Graph == null)
                {
                    return;
                }

                var selected = _tree.SelectedNode?.Tag as BusinessUnitNode;
                _tree.BeginUpdate();
                _tree.Nodes.Clear();
                foreach (var root in BusinessUnitTree.Build(Graph))
                {
                    _tree.Nodes.Add(ToTreeNode(root));
                }

                _tree.ExpandAll();
                _tree.EndUpdate();

                var again = selected == null ? null : Find(_tree.Nodes, selected.Id);
                if (again != null)
                {
                    _tree.SelectedNode = again;
                }
                else
                {
                    Grid.SetData(new object[0]);
                    InfoLabel.Text = PromptText;
                }
            }

            private static TreeNode ToTreeNode(BusinessUnitNode unit)
            {
                var node = new TreeNode(BusinessUnitTree.Describe(unit))
                {
                    Tag = unit,
                    ImageIndex = TreeIcons.BusinessUnit,
                    SelectedImageIndex = TreeIcons.BusinessUnit,
                    ToolTipText = unit.Name
                };
                foreach (var child in unit.Children)
                {
                    node.Nodes.Add(ToTreeNode(child));
                }

                return node;
            }

            private static TreeNode Find(TreeNodeCollection nodes, Guid id)
            {
                foreach (TreeNode node in nodes)
                {
                    if (((BusinessUnitNode)node.Tag).Id == id)
                    {
                        return node;
                    }

                    var inChild = Find(node.Nodes, id);
                    if (inChild != null)
                    {
                        return inChild;
                    }
                }

                return null;
            }

            private void ExportDetailed()
            {
                var rows = new List<IReadOnlyList<string>>();
                foreach (var row in _detail)
                {
                    rows.Add(new[] { row.User, row.BusinessUnit, row.Team, row.Role, row.Source });
                }

                ExportUi.Save(this, "BusinessUnitUsersDetailed.xlsx", "Users detailed", new[] { "User", "Business Unit", "Team", "Role", "Source" }, rows);
            }

            private void ShowContents()
            {
                var unit = _tree.SelectedNode?.Tag as BusinessUnitNode;
                if (Graph == null || unit == null)
                {
                    return;
                }

                var children = _chkChildren != null && _chkChildren.Checked;
                var disabled = _chkDisabled != null && _chkDisabled.Checked;
                var items = BusinessUnitTree.Contents(Graph, unit.Id, children, disabled);
                var teams = items.Where(i => i.Type == BusinessUnitTree.TeamType).ToList();
                var roles = items.Where(i => i.Type == BusinessUnitTree.RoleType).ToList();
                var users = Graph.UsersInBusinessUnits(Graph.BusinessUnitScope(unit.Id, children), disabled);
                Graph.BuildUserAccess(users, out var summary, out var detail);
                _detail = detail;

                switch (_cmbShow == null ? 0 : _cmbShow.SelectedIndex)
                {
                    case 1:
                        Grid.SetData(teams, "Name=Team", "BusinessUnit=Business Unit", "Detail");
                        break;
                    case 2:
                        Grid.SetData(roles, "Name=Role", "Detail", "Status=Kind");
                        break;
                    default:
                        Grid.SetData(summary, "User", "BusinessUnit=Business Unit", "Teams", "DirectRoles=Direct roles", "TeamRoles=Roles from teams", "Status");
                        break;
                }

                InfoLabel.Text = $"{unit.Name}{(children ? " and its child business units" : string.Empty)}: " +
                                 $"{users.Count} user(s), {teams.Count} team(s), {roles.Count} role(s) in use. The default team of each business unit is not listed.";
            }
        }
    }
}
