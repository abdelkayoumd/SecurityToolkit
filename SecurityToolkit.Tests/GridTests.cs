using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using SecurityToolkit.Models;
using SecurityToolkit.UI;
using Xunit;

namespace SecurityToolkit.Tests
{
    internal static class Sta
    {
        /// <summary>WinForms controls need a UI thread; run the test body on one and rethrow what it throws.</summary>
        public static void Run(Action body)
        {
            Exception error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    body();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null)
            {
                throw new Exception(error.Message, error);
            }
        }

        public static Control Find(Control root, Func<Control, bool> match)
        {
            foreach (Control child in root.Controls)
            {
                if (match(child))
                {
                    return child;
                }

                var inner = Find(child, match);
                if (inner != null)
                {
                    return inner;
                }
            }

            return null;
        }
    }

    public class FilterableGridTests
    {
        private static readonly string[] Columns = { "FullName=Full Name", "BusinessUnitName=Business Unit", "Status" };

        private static List<UserInfo> Users()
        {
            return new List<UserInfo>
            {
                new UserInfo { Id = Guid.NewGuid(), FullName = "Carol", BusinessUnitName = "Sales" },
                new UserInfo { Id = Guid.NewGuid(), FullName = "Alice", BusinessUnitName = "Support" },
                new UserInfo { Id = Guid.NewGuid(), FullName = "Bob", BusinessUnitName = "Sales" },
                new UserInfo { Id = Guid.NewGuid(), FullName = "Dave", BusinessUnitName = "Sales", IsDisabled = true }
            };
        }

        /// <summary>The grid is hosted in a shown form, as in the real tool.</summary>
        private static void OnUiThread(Action<FilterableGrid, Form> test)
        {
            Sta.Run(() =>
            {
                var grid = new FilterableGrid();
                var form = new Form { Size = new Size(800, 400) };
                form.Controls.Add(grid);
                form.Show();
                Application.DoEvents();
                test(grid, form);
                form.Dispose();
            });
        }

        private static IEnumerable<string> Names(FilterableGrid grid)
        {
            return grid.GetVisible<UserInfo>().Select(u => u.FullName);
        }

        [Fact]
        public void ValueFilter_KeepsOnlyTheChosenValues()
        {
            OnUiThread((grid, form) =>
            {
                grid.SetData(Users(), Columns);

                grid.SetValueFilter("BusinessUnitName", new[] { "Sales" });

                Assert.Equal(new[] { "Carol", "Bob", "Dave" }, Names(grid));
                Assert.True(grid.HasValueFilter("BusinessUnitName"));
            });
        }

        [Fact]
        public void ValueFilter_AllowsSeveralValuesAtOnce()
        {
            OnUiThread((grid, form) =>
            {
                grid.SetData(Users(), Columns);

                grid.SetValueFilter("FullName", new[] { "Alice", "Dave" });

                Assert.Equal(new[] { "Alice", "Dave" }, Names(grid).OrderBy(n => n));
            });
        }

        [Fact]
        public void ValueFilters_CombineAcrossColumns()
        {
            OnUiThread((grid, form) =>
            {
                grid.SetData(Users(), Columns);

                grid.SetValueFilter("BusinessUnitName", new[] { "Sales" });
                grid.SetValueFilter("Status", new[] { "Disabled" });

                Assert.Equal(new[] { "Dave" }, Names(grid));
            });
        }

        [Fact]
        public void ValueFilter_CombinesWithTheSearchBox()
        {
            OnUiThread((grid, form) =>
            {
                grid.SetData(Users(), Columns);
                var search = (TextBox)Sta.Find(grid, c => c is TextBox t && t.Dock == DockStyle.Top);

                grid.SetValueFilter("BusinessUnitName", new[] { "Sales" });
                search.Text = "o"; // Carol and Bob; Alice (Support) is excluded by the column filter

                Assert.Equal(new[] { "Carol", "Bob" }, Names(grid));
            });
        }

        [Fact]
        public void ValueFilter_EqualToEveryValueIsNoFilterAtAll()
        {
            OnUiThread((grid, form) =>
            {
                grid.SetData(Users(), Columns);

                grid.SetValueFilter("BusinessUnitName", new[] { "Sales", "Support" });

                Assert.False(grid.HasValueFilter("BusinessUnitName"));
                Assert.Equal(4, Names(grid).Count());
            });
        }

        [Fact]
        public void ClearFilters_RemovesValueFiltersAndTheSearch()
        {
            OnUiThread((grid, form) =>
            {
                grid.SetData(Users(), Columns);
                var search = (TextBox)Sta.Find(grid, c => c is TextBox t && t.Dock == DockStyle.Top);
                grid.SetValueFilter("BusinessUnitName", new[] { "Support" });
                search.Text = "ali";

                grid.ClearFilters();

                Assert.Equal(string.Empty, search.Text);
                Assert.False(grid.HasValueFilter("BusinessUnitName"));
                Assert.Equal(4, Names(grid).Count());
            });
        }

        [Fact]
        public void GetColumnValues_ListsDistinctValuesSortedFromAllRows()
        {
            OnUiThread((grid, form) =>
            {
                grid.SetData(Users(), Columns);
                grid.SetValueFilter("BusinessUnitName", new[] { "Support" });

                Assert.Equal(new[] { "Sales", "Support" }, grid.GetColumnValues("BusinessUnitName"));
                Assert.Equal(new[] { "Active", "Disabled" }, grid.GetColumnValues("Status"));
            });
        }

        [Fact]
        public void Sorting_ByColumnWorksAndSurvivesFiltering()
        {
            OnUiThread((grid, form) =>
            {
                grid.SetData(Users(), Columns);

                grid.SortBy("FullName");
                Assert.Equal(new[] { "Alice", "Bob", "Carol", "Dave" }, Names(grid));

                grid.SortBy("FullName", ascending: false);
                Assert.Equal(new[] { "Dave", "Carol", "Bob", "Alice" }, Names(grid));

                grid.SetValueFilter("BusinessUnitName", new[] { "Sales" });
                Assert.Equal(new[] { "Dave", "Carol", "Bob" }, Names(grid));
            });
        }

        [Fact]
        public void Binding_SelectsNothingUntilTheUserChooses()
        {
            OnUiThread((grid, form) =>
            {
                grid.SetData(Users(), Columns);
                Assert.Equal(0, grid.SelectedCount);

                grid.SetValueFilter("BusinessUnitName", new[] { "Sales" });
                Assert.Equal(0, grid.SelectedCount);
            });
        }

        [Fact]
        public void Export_FollowsTheFilterAndSort()
        {
            OnUiThread((grid, form) =>
            {
                grid.SetData(Users(), Columns);
                grid.SetValueFilter("BusinessUnitName", new[] { "Sales" });
                grid.SortBy("FullName");

                var table = grid.GetTable();

                Assert.Equal(new[] { "Full Name", "Business Unit", "Status" }, table.Key);
                Assert.Equal(new[] { "Bob", "Carol", "Dave" }, table.Value.Select(r => r[0]));
            });
        }

        [Fact]
        public void Filters_AreKeptWhenTheSameDataIsReloaded_AndDroppedForANewShape()
        {
            OnUiThread((grid, form) =>
            {
                grid.SetData(Users(), Columns);
                grid.SetValueFilter("BusinessUnitName", new[] { "Sales" });

                grid.SetData(Users(), Columns); // e.g. a Refresh
                Assert.True(grid.HasValueFilter("BusinessUnitName"));
                Assert.Equal(3, Names(grid).Count());

                grid.SetData(new List<TeamInfo> { new TeamInfo { Name = "T" } }, "Name");
                Assert.False(grid.HasValueFilter("BusinessUnitName"));
                Assert.Single(grid.GetVisible<TeamInfo>());
            });
        }

        [Fact]
        public void Binding_BeforeTheGridIsShown_DoesNotBreakTheLaterShow()
        {
            Sta.Run(() =>
            {
                var grid = new FilterableGrid();
                grid.SetData(Users(), Columns);
                var form = new Form { Size = new Size(800, 400) };
                form.Controls.Add(grid);
                form.Show();
                Application.DoEvents();
                Assert.Equal(4, grid.GetVisible<UserInfo>().Count);
                form.Dispose();
            });
        }
    }

    public class ColumnFilterPanelTests
    {
        private static readonly string[] Values = { "", "Sales", "Support", "Marketing" };

        [Fact]
        public void WithoutAFilter_EverythingStartsChecked()
        {
            Sta.Run(() =>
            {
                var panel = new ColumnFilterPanel(Values, null);

                Assert.Equal(Values.OrderBy(v => v), panel.CheckedValues.OrderBy(v => v));
                Assert.Equal(Values, panel.ShownValues);
            });
        }

        [Fact]
        public void WithAFilter_OnlyTheAllowedValuesStartChecked()
        {
            Sta.Run(() =>
            {
                var panel = new ColumnFilterPanel(Values, new HashSet<string> { "Sales" });

                Assert.Equal(new[] { "Sales" }, panel.CheckedValues);
            });
        }

        [Fact]
        public void Search_NarrowsTheListedValues()
        {
            Sta.Run(() =>
            {
                var panel = new ColumnFilterPanel(Values, null);

                panel.SearchText = "su";

                Assert.Equal(new[] { "Support" }, panel.ShownValues);
            });
        }

        [Fact]
        public void BlankValuesAreListedAndSearchableAsBlanks()
        {
            Sta.Run(() =>
            {
                var panel = new ColumnFilterPanel(Values, null);

                panel.SearchText = "blank";

                Assert.Equal(new[] { string.Empty }, panel.ShownValues);
            });
        }

        [Fact]
        public void ClearAllThenSelectAll_OnlyTouchTheValuesShownBySearch()
        {
            Sta.Run(() =>
            {
                var panel = new ColumnFilterPanel(Values, null);

                panel.SearchText = "s"; // "(Blanks)", Sales and Support contain an "s"; Marketing does not
                panel.CheckAllShown(false);
                panel.SearchText = string.Empty;

                Assert.Equal(new[] { "Marketing" }, panel.CheckedValues);

                panel.SearchText = "sales";
                panel.CheckAllShown(true);

                Assert.Equal(new[] { "Marketing", "Sales" }, panel.CheckedValues.OrderBy(v => v));
            });
        }

        [Fact]
        public void Apply_ReportsTheCheckedValues()
        {
            Sta.Run(() =>
            {
                var panel = new ColumnFilterPanel(Values, new HashSet<string> { "Sales", "Support" });
                IReadOnlyCollection<string> applied = null;
                panel.Applied += (s, values) => applied = values;

                var apply = (Button)Sta.Find(panel, c => c is Button b && b.Text == "Apply");
                apply.PerformClick();

                Assert.Equal(new[] { "Sales", "Support" }, applied.OrderBy(v => v));
            });
        }
    }
}
