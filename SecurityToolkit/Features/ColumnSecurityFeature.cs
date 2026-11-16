using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using SecurityToolkit.UI;
using Label = System.Windows.Forms.Label;

namespace SecurityToolkit.Features
{
    /// <summary>
    /// "Who can read or update secured columns?" Column security profiles, shown two ways: by profile (its secured
    /// columns, and its members: teams, their members, and users who hold it directly) or by user (every secured column
    /// their profiles cover, and which profile gives it).
    /// </summary>
    internal sealed class ColumnSecurityFeature : IFeature
    {
        public string Id => "column-security";
        public string Title => "Column security";
        public string Glyph => Glyphs.Unlock;
        public FeatureGroup Group => FeatureGroup.UnderstandAccess;
        public int Order => 20;

        public FeatureView CreateView(IFeatureHost host)
        {
            return new ColumnSecurityView(host);
        }

        private sealed class Loaded
        {
            public SecurityGraph Graph { get; set; }
            public ColumnSecurity Columns { get; set; }
        }

        private sealed class ColumnSecurityView : MasterDetailView
        {
            private const string ColumnsTab = "Secured columns";
            private const string MembersTab = "Members";
            private const string SystemAdministratorNote = " System Administrators can read and update every secured column whatever their profiles.";

            private readonly TabStrip _tabs;
            private readonly FilterableGrid _members;
            private ComboBox _cmbMode;
            private ColumnSecurity _columns;

            public ColumnSecurityView(IFeatureHost host)
                : base(host, "Profiles (select one)", ColumnsTab)
            {
                Master.MultiSelect = false;
                Grid.BodyCellRenderer = PaintYesNo;

                _tabs = new TabStrip { Name = "ColumnSecurityTabs" };
                _tabs.AddTab(ColumnsTab);
                _tabs.AddTab(MembersTab);
                _members = new FilterableGrid { Visible = false, EmptyMessage = "Nobody holds this profile." };
                EnableOpenIn(_members);

                var side = Grid.Parent;
                side.Controls.OfType<Label>().First().Visible = false; // the tabs replace the heading
                side.Controls.Remove(Grid);
                var results = new Panel { Dock = DockStyle.Fill };
                results.Controls.Add(Grid);
                results.Controls.Add(_members);
                side.Controls.Add(results);
                side.Controls.Add(_tabs);
                _tabs.SelectedIndexChanged += (s, e) =>
                {
                    Grid.Visible = _tabs.SelectedIndex == 0;
                    _members.Visible = _tabs.SelectedIndex == 1;
                };

                InfoLabel.Text = "Connect to an environment to get started.";
            }

            /// <summary>True when the list on the left holds users rather than profiles.</summary>
            private bool ByUser => _cmbMode != null && _cmbMode.SelectedIndex == 1;

            protected override string PromptText => ByUser
                ? "Select a user on the left to see the secured columns they can read, update or create."
                : "Select a profile on the left to see its secured columns and who holds it.";

            protected override bool IsLoaded => Graph != null && _columns != null;

            protected override void LoadData()
            {
                Host.RunAsync("Loading column security...", security => new Loaded { Graph = security.GetGraph(), Columns = security.GetColumnSecurity() }, loaded =>
                {
                    Graph = loaded.Graph;
                    _columns = loaded.Columns;
                    OnGraphLoaded();
                });
            }

            protected override IEnumerable<Control> CreateCommands()
            {
                yield return Command(Theme.GlyphRefresh, "Refresh", () => Host.EnsureConnection(() => { Host.Security.Invalidate(); LoadData(); }), strong: true);
                yield return CommandSeparator();
                yield return Command(Theme.GlyphDownload, "Export to Excel", () => ExportUi.SaveGrid(this, _tabs.SelectedIndex == 1 && !ByUser ? _members : Grid,
                    "ColumnSecurity.xlsx", _tabs.SelectedIndex == 1 && !ByUser ? MembersTab : ColumnsTab));
                yield return new Label { Text = "Show by", AutoSize = true, ForeColor = Theme.TextSecondary, Margin = new Padding(14, 12, 4, 0) };
                _cmbMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90, Margin = new Padding(0, 8, 8, 0), Font = Theme.FontRegular, Name = "ShowBy" };
                _cmbMode.Items.AddRange(new object[] { "profile", "user" });
                _cmbMode.SelectedIndex = 0;
                _cmbMode.SelectedIndexChanged += (s, e) => { if (IsLoaded) { OnGraphLoaded(); } };
                yield return _cmbMode;
            }

            /// <summary>A user opened from another screen: show the users.</summary>
            protected override void PrepareForSubject(Subject subject)
            {
                if (subject.Kind == SubjectKind.User && !ByUser && _cmbMode != null)
                {
                    _cmbMode.SelectedIndex = 1;
                }
            }

            protected override void Reset()
            {
                _columns = null;
                _members.SetData(new object[0]);
                base.Reset();
            }

            protected override void OnGraphLoaded()
            {
                if (!IsLoaded)
                {
                    return;
                }

                Grid.SetData(new object[0]);
                _members.SetData(new object[0]);
                _tabs.SelectedIndex = 0;
                _tabs.Visible = !ByUser;
                Grid.Visible = true;
                _members.Visible = false;
                Master.Parent.Controls.OfType<Label>().First().Text = ByUser ? "Users (select one)" : "Profiles (select one)";
                if (ByUser)
                {
                    Master.SetData(Graph.Users.Where(u => !u.IsDisabled).ToList(), "FullName=Full Name", "BusinessUnitName=Business Unit", "Status");
                }
                else
                {
                    Master.SetData(_columns.Profiles, "Name=Profile", "Users", "Teams", "Columns=Secured columns");
                }

                InfoLabel.Text = _columns.Profiles.Count == 0
                    ? "This environment has no column security profile." + SystemAdministratorNote
                    : $"{_columns.Profiles.Count} profile(s). {PromptText}";
            }

            protected override void OnMasterSelectionChanged()
            {
                if (ByUser)
                {
                    var user = Master.GetSelected<UserInfo>().FirstOrDefault();
                    if (user == null)
                    {
                        return;
                    }

                    var rows = _columns.ColumnsOfUser(user, Graph);
                    Grid.SetData(rows, "Table", "Column", "Read", "Update", "Create", "Through=Through profile");
                    var held = _columns.ProfilesOf(user, Graph).Select(p => p.Key).Distinct().Count();
                    InfoLabel.Text = held == 0
                        ? $"{user.FullName} holds no column security profile: no secured column is open to them." + SystemAdministratorNote
                        : $"{user.FullName}: {held} profile(s), {rows.Count} secured column(s), {rows.Count(r => r.Read == "Yes")} readable, {rows.Count(r => r.Update == "Yes")} updatable." + SystemAdministratorNote;
                    return;
                }

                var profile = Master.GetSelected<ColumnProfile>().FirstOrDefault();
                if (profile == null)
                {
                    return;
                }

                Grid.SetData(_columns.ColumnsOf(profile.Id), "Table", "Column", "Read", "Update", "Create");
                var members = _columns.MembersOf(profile.Id, Graph);
                _members.SetData(members, "Member", "Type", "Via", "BusinessUnit=Business Unit", "Status");
                _tabs.SetText(0, $"{ColumnsTab} ({profile.Columns})");
                _tabs.SetText(1, $"{MembersTab} ({members.Count})");
                var people = members.Where(m => m.UserId.HasValue).Select(m => m.UserId).Distinct().Count();
                InfoLabel.Text = $"{profile.Name}: {profile.Columns} secured column(s), held by {people} user(s) ({profile.Users} directly, the rest through {profile.Teams} team(s)).";
            }

            /// <summary>"Yes" and "No" in green and red.</summary>
            private static bool PaintYesNo(System.Drawing.Graphics g, System.Drawing.Rectangle bounds, string text, bool selected)
            {
                if (text != "Yes" && text != "No")
                {
                    return false;
                }

                TextRenderer.DrawText(g, text, Theme.FontBold, new System.Drawing.Rectangle(bounds.Left + 6, bounds.Top, bounds.Width - 6, bounds.Height),
                    text == "Yes" ? Theme.Success : Theme.Danger, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine);
                return true;
            }
        }
    }
}
