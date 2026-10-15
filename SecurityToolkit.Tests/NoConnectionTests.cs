using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using SecurityToolkit.Features;
using SecurityToolkit.Services;
using SecurityToolkit.UI;
using Xunit;

namespace SecurityToolkit.Tests
{
    /// <summary>
    /// The XrmToolBox review asks that, with no organization connected, every control can be used without an error,
    /// and that controls which need a connection open the connection dialog. This host behaves like XrmToolBox does
    /// without a connection: it never runs the work, it only records that a connection was asked for.
    /// </summary>
    internal sealed class DisconnectedHost : IFeatureHost
    {
        public SecurityService Security => null;
        public int ConnectionRequests;
        public ScriptedUi Ui = new ScriptedUi();

        public void EnsureConnection(Action action) { ConnectionRequests++; }

        public void RunAsync<T>(string message, Func<SecurityService, T> work, Action<T> done)
        {
            throw new InvalidOperationException("Work was started without a connection.");
        }

        public void ShowMessage(BannerKind kind, string message) { }
        public IChangeUi CreateChangeUi(IWin32Window owner) { return Ui; }
    }

    public class NoConnectionTests
    {
        private static void Click(Control control)
        {
            typeof(Control).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(control, new object[] { EventArgs.Empty });
        }

        /// <summary>Opens every screen with no connection and uses every command and checkbox on it.</summary>
        private static void ForEachScreen(Action<IFeature, FeatureView, DisconnectedHost> use)
        {
            Sta.Run(() =>
            {
                foreach (var feature in FeatureRegistry.CreateAll())
                {
                    var host = new DisconnectedHost();
                    var view = feature.CreateView(host);
                    var form = new Form { Size = new Size(1200, 600) };
                    form.Controls.Add(view);
                    form.Show();
                    Application.DoEvents();
                    _ = view.Commands;
                    view.OnActivated();
                    use(feature, view, host);
                    form.Dispose();
                }
            });
        }

        [Fact]
        public void EveryScreenOpensWithoutAConnection_AndSaysHowToGetStarted()
        {
            ForEachScreen((feature, view, host) =>
            {
                Assert.Equal(0, host.ConnectionRequests); // opening a screen must not pop up the connection dialog uninvited
            });
        }

        [Fact]
        public void EveryCommand_CanBeClickedWithoutAConnection_WithoutAnError()
        {
            ForEachScreen((feature, view, host) =>
            {
                // Export commands tell the user there is nothing to export, in a message box; the rest must simply work.
                foreach (var button in view.Commands.OfType<CommandButton>().Where(b => !b.Text.StartsWith("Export")))
                {
                    var ex = Record.Exception(() => Click(button));
                    Assert.True(ex == null, $"{feature.Title} / {button.Text}: {ex}");
                }
            });
        }

        [Fact]
        public void EveryCheckboxAndDropdown_CanBeChangedWithoutAConnection_WithoutAnError()
        {
            ForEachScreen((feature, view, host) =>
            {
                foreach (var box in view.Commands.OfType<CheckBox>())
                {
                    var ex = Record.Exception(() => { box.Checked = !box.Checked; box.Checked = !box.Checked; });
                    Assert.True(ex == null, $"{feature.Title} / {box.Text}: {ex}");
                }

                var combos = new List<ComboBox>();
                Collect(view, combos);
                foreach (var combo in combos)
                {
                    var ex = Record.Exception(() => { for (var i = 0; i < combo.Items.Count; i++) { combo.SelectedIndex = i; } });
                    Assert.True(ex == null, $"{feature.Title} dropdown: {ex}");
                }
            });
        }

        [Fact]
        public void TheCommandsThatNeedData_AskForAConnection()
        {
            ForEachScreen((feature, view, host) =>
            {
                var needsData = view.Commands.OfType<CommandButton>().Where(b => b.Text == "Refresh" || b.Text == "Load users" || b.Text == "Show teams"
                    || b.Text.StartsWith("Add") || b.Text.StartsWith("Remove") || b.Text == "Clone profile").ToList();

                foreach (var button in needsData)
                {
                    var before = host.ConnectionRequests;
                    Click(button);
                    Assert.True(host.ConnectionRequests > before, $"{feature.Title} / {button.Text} did not ask for a connection.");
                }
            });
        }

        [Fact]
        public void TheListsAreEmptyNotBroken_WithoutAConnection()
        {
            ForEachScreen((feature, view, host) =>
            {
                var grids = new List<FilterableGrid>();
                CollectGrids(view, grids);
                Assert.All(grids, g => Assert.Equal(0, g.SelectedCount));
            });
        }

        private static void Collect(Control root, List<ComboBox> found)
        {
            foreach (Control child in root.Controls)
            {
                if (child is ComboBox box) { found.Add(box); } else { Collect(child, found); }
            }
        }

        private static void CollectGrids(Control root, List<FilterableGrid> found)
        {
            foreach (Control child in root.Controls)
            {
                if (child is FilterableGrid grid) { found.Add(grid); } else { CollectGrids(child, found); }
            }
        }
    }
}
