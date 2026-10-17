using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using SecurityToolkit.Features;
using SecurityToolkit.Models;
using SecurityToolkit.UI;
using Xunit;

namespace SecurityToolkit.Tests
{
    public class UserListTooltipTests
    {
        private static string TipOf(FilterableGrid grid, string property, int row)
        {
            var data = (DataGridView)Sta.Find(grid, c => c is DataGridView);
            var column = data.Columns.Cast<DataGridViewColumn>().First(c => c.DataPropertyName == property);
            var args = (DataGridViewCellToolTipTextNeededEventArgs)System.Activator.CreateInstance(typeof(DataGridViewCellToolTipTextNeededEventArgs), BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { column.Index, row, string.Empty }, null);
            typeof(DataGridView).GetMethod("OnCellToolTipTextNeeded", BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[] { typeof(DataGridViewCellToolTipTextNeededEventArgs) }, null).Invoke(data, new object[] { args });
            return args.ToolTipText;
        }

        [Fact]
        public void TheUserListOfAUsersAccess_HasNoUserNameColumn()
        {
            MasterDetailHarness.Run(new UserAccessMatrixFeature(), (view, master, detail, org) =>
            {
                Assert.Equal(new[] { "Full Name", "Business Unit", "Status" }, master.GetTable().Key);
            });
        }

        [Fact]
        public void HoveringTheFullName_ShowsTheUserName()
        {
            MasterDetailHarness.Run(new UserAccessMatrixFeature(), (view, master, detail, org) =>
            {
                var users = master.GetVisible<UserInfo>().ToList();

                Assert.Equal("ada@x", TipOf(master, "FullName", users.FindIndex(u => u.FullName == "Ada")));
                Assert.Equal("bob@x", TipOf(master, "FullName", users.FindIndex(u => u.FullName == "Bob")));
            });
        }

        [Fact]
        public void OtherColumnsHaveNoTip()
        {
            MasterDetailHarness.Run(new UserAccessMatrixFeature(), (view, master, detail, org) =>
            {
                Assert.Equal(string.Empty, TipOf(master, "Status", 0));
            });
        }
    }
}
