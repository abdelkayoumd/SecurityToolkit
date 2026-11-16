using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SecurityToolkit.Features;
using SecurityToolkit.Services;
using SecurityToolkit.UI;
using Xunit;

namespace SecurityToolkit.Tests
{
    public class OtherPrivilegesTests
    {
        [Theory]
        [InlineData("prvExportToExcel", "Export to Excel")]
        [InlineData("prvActOnBehalfOfAnotherUser", "Act on Behalf of Another User")]
        [InlineData("prvBulkDelete", "Bulk Delete")]
        [InlineData("prvUseCRM", "Use CRM")]
        [InlineData("prvGoMobile", "Go Mobile")]
        [InlineData("prvPublishXMLCustomizations", "Publish XML Customizations")]
        [InlineData("", "")]
        public void TheNameReadsInWords(string name, string label)
        {
            Assert.Equal(label, OtherPrivilege.LabelOf(name));
        }

        [Fact]
        public void OnlyThePrivilegesWithoutATable_AreListed_InAlphabeticalOrder()
        {
            var service = new SecurityService(OrgFake.For(new Org()));

            var others = service.GetOtherPrivileges();

            Assert.Equal(new[] { "Bulk Delete", "Export to Excel" }, others.Select(p => p.Label));
            Assert.Equal(new[] { "prvBulkDelete", "prvExportToExcel" }, others.Select(p => p.Name));
        }

        [Fact]
        public void TheRolesThatGrantThem_AreFound_WithTheirDepth_AndTheMinimumDepthApplies()
        {
            var service = new SecurityService(OrgFake.For(new Org()));
            var export = service.GetOtherPrivileges().Where(p => p.Label == "Export to Excel").ToList();

            var any = service.FindRolesWithOtherPrivileges(export, PrivilegeDepth.User);
            Assert.Equal(new[] { "Admin", "Auditor" }, any.Select(r => r.Role));
            Assert.All(any, r => Assert.Equal("Export to Excel", r.Table));
            Assert.Equal("Organization", any.Single(r => r.Role == "Admin").Depth);

            var wide = service.FindRolesWithOtherPrivileges(export, PrivilegeDepth.Organization);
            Assert.Equal(new[] { "Admin" }, wide.Select(r => r.Role));
            Assert.Empty(service.FindRolesWithOtherPrivileges(new OtherPrivilege[0], PrivilegeDepth.User));
        }

        [Fact]
        public void ThePrivilegeFinder_SwitchesToOtherPrivileges_AndStatesTheResultInItsSentence()
        {
            MasterDetailHarness.Run(new PrivilegeFinderFeature(), (view, master, detail, org) =>
            {
                var mode = view.Commands.OfType<ComboBox>().Single(c => c.Name == "FinderMode");
                mode.SelectedIndex = 1;

                Assert.Equal(new[] { "Bulk Delete", "Export to Excel" }, master.GetVisible<OtherPrivilege>().Select(p => p.Label));
                Assert.False(Sta.Find(view, c => c is CheckedDropDown).Visible); // the eight table actions do not apply

                MasterDetailHarness.Select<OtherPrivilege>(master, p => p.Label == "Export to Excel");
                TeamMembersViewTests.Pump(() => detail.GetVisible<RolePrivilegeRow>().Count == 2);

                Assert.Equal(new[] { "Privilege", "Role", "Business Unit", "Depth" }, detail.GetTable().Key.ToArray());
                Assert.NotNull(Sta.Find(view, c => c is Label l && l.Text == "2 role(s) grant"));
                Assert.NotNull(Sta.Find(view, c => c is Label l && l.Text == "Export to Excel at"));

                mode.SelectedIndex = 0;
                Assert.NotEmpty(master.GetVisible<SecurityToolkit.Models.TableInfo>());
                Assert.True(Sta.Find(view, c => c is CheckedDropDown).Visible);
            });
        }

        [Fact]
        public void OpeningATableOnThePrivilegeFinder_ShowsTheTablesAgain()
        {
            MasterDetailHarness.Run(new PrivilegeFinderFeature(), (view, master, detail, org) =>
            {
                view.FeatureId = "privilege-finder";
                view.Commands.OfType<ComboBox>().Single(c => c.Name == "FinderMode").SelectedIndex = 1;

                view.ShowSubject(new Subject { Kind = SubjectKind.Table, Name = "contact" });

                Assert.Equal(new[] { "contact" }, master.GetSelected<SecurityToolkit.Models.TableInfo>().Select(t => t.LogicalName));
            });
        }
    }
}
