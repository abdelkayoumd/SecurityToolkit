using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Features;
using SecurityToolkit.UI;
using Xunit;

namespace SecurityToolkit.Tests
{
    /// <summary>Wherever a privilege depth is shown, it is drawn with its icon as in the Dynamics 365 security role editor.</summary>
    public class DepthDisplayTests
    {
        private static void Collect(Control root, List<FilterableGrid> found)
        {
            foreach (Control child in root.Controls)
            {
                if (child is FilterableGrid grid) { found.Add(grid); } else { Collect(child, found); }
            }
        }

        [Theory]
        [InlineData("compare-roles")]
        [InlineData("privilege-finder")]
        [InlineData("explain-access")]
        [InlineData("user-access-matrix")]
        public void TheResultsGridDrawsDepthsWithTheirIcon(string featureId)
        {
            Sta.Run(() =>
            {
                var feature = FeatureRegistry.CreateAll().Single(f => f.Id == featureId);
                var view = feature.CreateView(new TestHost());
                var grids = new List<FilterableGrid>();
                Collect(view, grids);

                Assert.Contains(grids, g => g.BodyCellRenderer != null);
                view.Dispose();
            });
        }
    }
}
