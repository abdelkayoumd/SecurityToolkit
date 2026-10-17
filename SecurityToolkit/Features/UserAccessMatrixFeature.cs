using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using SecurityToolkit.UI;

namespace SecurityToolkit.Features
{
    /// <summary>
    /// "What can this user do?" Pick a user on the left; the right is a grid of every table against the eight
    /// privileges, each cell showing how far the user's roles (direct and through teams) reach, shaded by reach.
    /// The reverse of Explain access, which starts from one privilege and lists the routes to it.
    /// </summary>
    internal sealed class UserAccessMatrixFeature : IFeature
    {
        public string Id => "user-access-matrix";
        public string Title => "A user's access";
        public string Glyph => Glyphs.Grid;
        public FeatureGroup Group => FeatureGroup.UnderstandAccess;
        public int Order => 10;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new UserAccessMatrixView(host);
        }

        private sealed class Loaded
        {
            public IReadOnlyList<TableInfo> Tables { get; set; }
            public IReadOnlyDictionary<Guid, PrivilegeTarget> Catalog { get; set; }
            public IReadOnlyDictionary<Guid, IReadOnlyList<RoleGrant>> Grants { get; set; }
        }

        private sealed class UserAccessMatrixView : MasterDetailView
        {
            private UserInfo _user;
            private CheckBox _chkEmpty;
            private CheckBox _chkDisabled;

            public UserAccessMatrixView(IFeatureHost host)
                : base(host, "Users (select one)", "What the user can do: the widest reach across all their roles")
            {
                // The look of the Dynamics 365 security role editor: an icon and the depth in words in every cell.
                Grid.BodyCellRenderer = DepthCell.Paint;
                Grid.RowHeight = 36;
                InfoLabel.Text = "Connect to an environment to get started.";
            }

            protected override string PromptText => "Select a user on the left to see what they can do.";

            // Nine columns of results need the room more than a list of names does.
            protected override int MasterWidthPercent => 18;

            protected override IEnumerable<Control> CreateCommands()
            {
                yield return Command(Theme.GlyphRefresh, "Refresh", Reload, strong: true);
                yield return CommandSeparator();
                yield return Command(Theme.GlyphClearSelection, "Clear selection", Master.ClearSelection);
                yield return Command(Theme.GlyphDownload, "Export to Excel", () => ExportUi.SaveGrid(this, Grid, "WhatCanTheUserDo.xlsx", "Access"));
                _chkEmpty = CommandCheck("Include tables with no access", ShowMatrix);
                _chkDisabled = CommandCheck("Include disabled users", () => { if (Graph != null) { OnGraphLoaded(); } });
                yield return _chkEmpty;
                yield return _chkDisabled;
            }

            protected override void Reset()
            {
                _user = null;
                base.Reset();
            }

            private void Reload()
            {
                // Roles may have changed in Dataverse, so forget what is known about them as well.
                Host.EnsureConnection(() =>
                {
                    Host.Security.Invalidate();
                    LoadData();
                });
            }

            protected override void OnGraphLoaded()
            {
                if (Graph == null)
                {
                    return;
                }

                var includeDisabled = _chkDisabled != null && _chkDisabled.Checked;
                Master.SetData(Graph.Users.Where(u => includeDisabled || !u.IsDisabled).ToList(),
                    "FullName=Full Name", "DomainName=User Name", "BusinessUnitName=Business Unit", "Status");
                if (_user == null)
                {
                    InfoLabel.Text = PromptText;
                }
                else
                {
                    ShowMatrix();
                }
            }

            protected override void OnMasterSelectionChanged()
            {
                var picked = Master.GetSelected<UserInfo>();
                if (picked.Count == 0)
                {
                    // Only cleared (by a filter, say): keep showing the last user.
                    return;
                }

                if (picked.Count > 1)
                {
                    InfoLabel.Text = "Select one user to see what they can do.";
                    return;
                }

                _user = picked[0];
                ShowMatrix();
            }

            private void ShowMatrix()
            {
                if (Graph == null || _user == null)
                {
                    return;
                }

                var user = _user;
                var graph = Graph;
                var roleIds = AccessMatrixBuilder.RoleIdsOf(graph, user);
                var includeEmpty = _chkEmpty != null && _chkEmpty.Checked;

                Host.RunAsync("Reading what the user can do...", security => new Loaded
                {
                    Tables = security.GetTables(),
                    Catalog = security.GetPrivilegeCatalog(),
                    Grants = security.GetRolePrivileges(roleIds)
                }, loaded =>
                {
                    var rows = AccessMatrixBuilder.Build(graph, user, loaded.Tables, loaded.Catalog, loaded.Grants, includeEmpty);
                    Grid.SetData(rows, "Table", "Create", "Read", "Write", "Delete", "Append", "AppendTo=Append To", "Assign", "Share");
                    Grid.SetColumnWeight("Table", 140);
                    Grid.SetColumnMinimumWidth("Table", 120);
                    foreach (var privilege in new[] { "Create", "Read", "Write", "Delete", "Append", "AppendTo", "Assign", "Share" })
                    {
                        // Wide enough for an icon and "Business Unit". Longer labels are clipped at the cell edge, as in Dynamics 365.
                        Grid.SetColumnMinimumWidth(privilege, 112);
                    }

                    InfoLabel.Text = $"{AccessMatrixBuilder.DescribeRoles(graph, user)} {rows.Count} table(s) shown. A blank cell means the privilege does not exist for that table.";
                });
            }
        }
    }
}
