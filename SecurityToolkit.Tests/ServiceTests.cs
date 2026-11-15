using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using SecurityToolkit.Features;
using SecurityToolkit.Services;
using Xunit;

namespace SecurityToolkit.Tests
{
    public class SecurityServiceTests
    {
        private static Entity User(string name, Guid? buId = null)
        {
            var e = new Entity("systemuser", Guid.NewGuid());
            e["fullname"] = name;
            e["businessunitid"] = new EntityReference("businessunit", buId ?? Guid.NewGuid()) { Name = "Root" };
            return e;
        }

        [Fact]
        public void GetUsers_FollowsPagingUntilNoMoreRecords()
        {
            var fake = new FakeOrganizationService();
            fake.OnRetrieveMultiple = q =>
            {
                var page = ((QueryExpression)q).PageInfo.PageNumber;
                var result = new EntityCollection();
                result.Entities.Add(User("User " + page));
                result.MoreRecords = page < 3;
                result.PagingCookie = "cookie" + page;
                return result;
            };

            var users = new SecurityService(fake).GetUsers();

            Assert.Equal(new[] { "User 1", "User 2", "User 3" }, users.Select(u => u.FullName));
            Assert.Equal(3, fake.Queries.Count);
            Assert.Equal("cookie2", ((QueryExpression)fake.Queries[2]).PageInfo.PagingCookie);
        }

        [Fact]
        public void GetUsers_IsCachedUntilInvalidated()
        {
            var fake = new FakeOrganizationService();
            fake.OnRetrieveMultiple = q => new EntityCollection(new List<Entity> { User("A") });
            var service = new SecurityService(fake);

            service.GetUsers();
            service.GetUsers();
            Assert.Single(fake.Queries);

            service.Invalidate();
            service.GetUsers();
            Assert.Equal(2, fake.Queries.Count);
        }

        [Fact]
        public void GetUsers_MapsBusinessUnitAndHidesSystemPlaceholders()
        {
            var fake = new FakeOrganizationService();
            fake.OnRetrieveMultiple = q => new EntityCollection(new List<Entity> { User("Ada") });

            var user = new SecurityService(fake).GetUsers().Single();
            var query = (QueryExpression)fake.Queries.Single();

            Assert.Equal("Root", user.BusinessUnitName);
            Assert.Contains(query.Criteria.Conditions, c => c.AttributeName == "fullname" && c.Operator == ConditionOperator.DoesNotBeginWith);
        }
    }

    public class ChangeExecutorTests
    {
        private static ChangeItem Item(string subject)
        {
            return new ChangeItem("Add role", subject, "Salesperson",
                ChangeExecutor.Associate("systemuser", Guid.NewGuid(), "systemuserroles_association", "role", Guid.NewGuid()));
        }

        [Fact]
        public void Apply_ReportsPerItemFaultsFromExecuteMultiple()
        {
            var fake = new FakeOrganizationService();
            fake.OnExecute = r =>
            {
                var multiple = (ExecuteMultipleRequest)r;
                var response = new ExecuteMultipleResponse();
                var items = new ExecuteMultipleResponseItemCollection();
                items.Add(new ExecuteMultipleResponseItem { RequestIndex = 1, Fault = new OrganizationServiceFault { Message = "boom" } });
                response.Results["Responses"] = items;
                Assert.True(multiple.Settings.ContinueOnError);
                return response;
            };

            var results = ChangeExecutor.Apply(fake, new[] { Item("A"), Item("B"), Item("C") });

            Assert.Equal(new[] { true, false, true }, results.Select(x => x.Success));
            Assert.Equal("boom", results[1].Error);
        }

        [Fact]
        public void Apply_SplitsLargeChangeSetsIntoBatches()
        {
            var fake = new FakeOrganizationService();
            fake.OnExecute = r =>
            {
                var response = new ExecuteMultipleResponse();
                response.Results["Responses"] = new ExecuteMultipleResponseItemCollection();
                return response;
            };
            var items = Enumerable.Range(0, ChangeExecutor.BatchSize * 2 + 1).Select(i => Item("U" + i)).ToList();
            var progress = new List<int>();

            var results = ChangeExecutor.Apply(fake, items, (done, total) => progress.Add(done));

            Assert.Equal(3, fake.ExecutedRequests.Count);
            Assert.Equal(items.Count, results.Count);
            Assert.All(results, r => Assert.True(r.Success));
            Assert.Equal(new[] { 100, 200, 201 }, progress);
        }
    }

    public class ExcelExporterTests
    {
        [Fact]
        public void Write_ProducesWorkbookWithHeaderAndRows()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xlsx");
            try
            {
                ExcelExporter.Write(path, "Users: <all>", new[] { "Name", "Role" },
                    new IReadOnlyList<string>[] { new[] { "Ada & Co", "Admin" }, new[] { "Bob", null } });

                using (var package = Package.Open(path, FileMode.Open, FileAccess.Read))
                {
                    var sheet = XDocument.Load(package.GetPart(new Uri("/xl/worksheets/sheet1.xml", UriKind.Relative)).GetStream());
                    var texts = sheet.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value).ToList();
                    Assert.Equal(new[] { "Name", "Role", "Ada & Co", "Admin", "Bob", "" }, texts);

                    var workbook = XDocument.Load(package.GetPart(new Uri("/xl/workbook.xml", UriKind.Relative)).GetStream());
                    Assert.Equal("Users <all>", workbook.Descendants().Single(e => e.Name.LocalName == "sheet").Attribute("name").Value);
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Theory]
        [InlineData(0, "A")]
        [InlineData(25, "Z")]
        [InlineData(26, "AA")]
        [InlineData(701, "ZZ")]
        [InlineData(702, "AAA")]
        public void ColumnLetter_FollowsExcelNaming(int index, string expected)
        {
            Assert.Equal(expected, ExcelExporter.ColumnLetter(index));
        }

        [Fact]
        public void SafeSheetName_TruncatesTo31Characters()
        {
            Assert.Equal(31, ExcelExporter.SafeSheetName(new string('x', 50)).Length);
            Assert.Equal("Sheet1", ExcelExporter.SafeSheetName("???"));
        }

        private static void Export(ExportInfo info, Action<XDocument, XDocument, Package> check)
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xlsx");
            try
            {
                ExcelExporter.Write(path, "Roles", new[] { "Role", "Users", "Read", "Code" },
                    new IReadOnlyList<string>[] { new[] { "Admin", "12", "Organization", "007" }, new[] { "Seller", "3", "None", "42" }, new[] { "It's", "", "User", "1" } }, info);

                using (var package = Package.Open(path, FileMode.Open, FileAccess.Read))
                {
                    var sheet = XDocument.Load(package.GetPart(new Uri("/xl/worksheets/sheet1.xml", UriKind.Relative)).GetStream());
                    var workbook = XDocument.Load(package.GetPart(new Uri("/xl/workbook.xml", UriKind.Relative)).GetStream());
                    check(sheet, workbook, package);
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static XElement Cell(XDocument sheet, string reference)
        {
            return sheet.Descendants().Single(e => e.Name.LocalName == "c" && e.Attribute("r").Value == reference);
        }

        [Fact]
        public void Write_FreezesTheHeader_FiltersEveryColumn_AndFitsThePageWidth()
        {
            Export(null, (sheet, workbook, package) =>
            {
                var pane = sheet.Descendants().Single(e => e.Name.LocalName == "pane");
                Assert.Equal("frozen", pane.Attribute("state").Value);
                Assert.Equal("1", pane.Attribute("ySplit").Value);
                Assert.Equal("A1:D4", sheet.Descendants().Single(e => e.Name.LocalName == "autoFilter").Attribute("ref").Value);
                Assert.Equal("landscape", sheet.Descendants().Single(e => e.Name.LocalName == "pageSetup").Attribute("orientation").Value);
                Assert.Equal("1", Cell(sheet, "A1").Attribute("s").Value);

                var names = workbook.Descendants().Where(e => e.Name.LocalName == "definedName").ToDictionary(e => e.Attribute("name").Value, e => e.Value);
                Assert.Equal("'Roles'!$A$1:$D$4", names["_xlnm._FilterDatabase"]);
                Assert.Equal("'Roles'!$1:$1", names["_xlnm.Print_Titles"]);
            });
        }

        [Fact]
        public void Write_StoresWholeNumberColumnsAsNumbers_ButKeepsCodesAsText()
        {
            Export(null, (sheet, workbook, package) =>
            {
                // "Users" holds 12, 3 and a blank: numbers. "Code" holds 007: text, or the zeros would be lost.
                Assert.Null(Cell(sheet, "B2").Attribute("t"));
                Assert.Equal("12", Cell(sheet, "B2").Value);
                Assert.Equal("inlineStr", Cell(sheet, "B4").Attribute("t").Value);
                Assert.Equal("inlineStr", Cell(sheet, "D2").Attribute("t").Value);
                Assert.Equal("007", Cell(sheet, "D2").Value);
            });
        }

        [Theory]
        [InlineData(true, "12", "3", "")]
        [InlineData(true, "-4", "0")]
        [InlineData(false, "007", "42")]
        [InlineData(false, "12", "n/a")]
        [InlineData(false, "", "")]
        [InlineData(false, "1.5")]
        public void IsNumberColumn_OnlyForWholeNumbers(bool expected, params string[] values)
        {
            Assert.Equal(expected, ExcelExporter.IsNumberColumn(values));
        }

        [Fact]
        public void Write_ColoursDepthsOnlyWhenAsked()
        {
            Export(null, (sheet, workbook, package) => Assert.Equal("2", Cell(sheet, "C2").Attribute("s").Value));
            Export(new ExportInfo { ColourDepths = true }, (sheet, workbook, package) =>
            {
                Assert.Equal("8", Cell(sheet, "C2").Attribute("s").Value); // Organization
                Assert.Equal("4", Cell(sheet, "C3").Attribute("s").Value); // None
                Assert.Equal("5", Cell(sheet, "C4").Attribute("s").Value); // User
                Assert.Equal("2", Cell(sheet, "A2").Attribute("s").Value); // any other text
            });
        }

        [Fact]
        public void Write_WithInfo_AddsAnAboutSheetSayingWhatWasExported()
        {
            var info = new ExportInfo
            {
                Title = "Privilege finder: Roles", Description = "3 role(s) grant Read on 1 table(s) at User level or wider",
                Environment = "Contoso Dev", ExportedOn = new DateTime(2026, 11, 15, 9, 30, 0)
            };
            Export(info, (sheet, workbook, package) =>
            {
                Assert.Equal(new[] { "Roles", "About" }, workbook.Descendants().Where(e => e.Name.LocalName == "sheet").Select(e => e.Attribute("name").Value));

                var about = XDocument.Load(package.GetPart(new Uri("/xl/worksheets/sheet2.xml", UriKind.Relative)).GetStream());
                var texts = about.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value).ToList();
                Assert.Equal("Privilege finder: Roles", texts[0]);
                Assert.Contains("3 role(s) grant Read on 1 table(s) at User level or wider", texts);
                Assert.Contains("Contoso Dev", texts);
                Assert.Contains("2026-11-15 09:30", texts);
                Assert.Equal("3", texts[texts.IndexOf("Rows") + 1]);
            });

            Export(null, (sheet, workbook, package) => Assert.False(package.PartExists(new Uri("/xl/worksheets/sheet2.xml", UriKind.Relative))));
        }

        [Fact]
        public void EveryStyleACellUses_ExistsInTheStylesheet()
        {
            Export(new ExportInfo { Title = "T", ColourDepths = true }, (sheet, workbook, package) =>
            {
                var styles = XDocument.Load(package.GetPart(new Uri("/xl/styles.xml", UriKind.Relative)).GetStream());
                var cellXfs = styles.Descendants().Single(e => e.Name.LocalName == "cellXfs");
                var fonts = styles.Descendants().Single(e => e.Name.LocalName == "fonts").Elements().Count();
                var fills = styles.Descendants().Single(e => e.Name.LocalName == "fills").Elements().Count();
                var borders = styles.Descendants().Single(e => e.Name.LocalName == "borders").Elements().Count();

                Assert.Equal(cellXfs.Elements().Count().ToString(), cellXfs.Attribute("count").Value);
                Assert.All(cellXfs.Elements(), xf =>
                {
                    Assert.InRange(int.Parse(xf.Attribute("fontId").Value), 0, fonts - 1);
                    Assert.InRange(int.Parse(xf.Attribute("fillId").Value), 0, fills - 1);
                    Assert.InRange(int.Parse(xf.Attribute("borderId").Value), 0, borders - 1);
                });

                var about = XDocument.Load(package.GetPart(new Uri("/xl/worksheets/sheet2.xml", UriKind.Relative)).GetStream());
                var used = sheet.Descendants().Concat(about.Descendants()).Where(e => e.Name.LocalName == "c").Select(e => int.Parse(e.Attribute("s").Value));
                Assert.All(used, s => Assert.InRange(s, 0, cellXfs.Elements().Count() - 1));
            });
        }
    }

    public class FeatureRegistryTests
    {
        private sealed class Stub : IFeature
        {
            public Stub(string id, int order, string title = null, FeatureGroup group = FeatureGroup.LookUp) { Id = id; Order = order; Title = title ?? id; Group = group; }
            public string Id { get; }
            public string Title { get; }
            public string Glyph => "";
            public FeatureGroup Group { get; }
            public int Order { get; }
            public FeatureView CreateView(IFeatureHost host) => throw new NotSupportedException();
        }

        [Fact]
        public void Order_SortsByOrderThenTitle()
        {
            var ordered = FeatureRegistry.Order(new IFeature[] { new Stub("c", 2), new Stub("b", 1, "Zeta"), new Stub("a", 1, "Alpha") });
            Assert.Equal(new[] { "a", "b", "c" }, ordered.Select(f => f.Id));
        }

        [Fact]
        public void Order_GroupsComeFirst_ThenOrderWithinTheGroup()
        {
            var ordered = FeatureRegistry.Order(new IFeature[]
            {
                new Stub("change1", 1, group: FeatureGroup.Change),
                new Stub("explore9", 9, group: FeatureGroup.LookUp),
                new Stub("home", 50, group: FeatureGroup.Start),
                new Stub("explore2", 2, group: FeatureGroup.LookUp)
            });
            Assert.Equal(new[] { "home", "explore2", "explore9", "change1" }, ordered.Select(f => f.Id));
        }

        [Fact]
        public void EveryFeatureThatChangesSecurity_IsUnderChange_AndEveryReadOnlyOneUnderItsOwnGroup()
        {
            var all = FeatureRegistry.CreateAll().ToDictionary(f => f.Id, f => f.Group);

            Assert.Equal(FeatureGroup.Start, all["home"]);
            foreach (var id in new[] { "assign-user-roles", "assign-team-roles", "assign-team-membership", "clone-user" })
            {
                Assert.Equal(FeatureGroup.Change, all[id]);
            }

            var expected = new Dictionary<string, FeatureGroup>
            {
                ["users-and-teams"] = FeatureGroup.LookUp, ["team-members"] = FeatureGroup.LookUp, ["users-by-role"] = FeatureGroup.LookUp, ["business-unit-tree"] = FeatureGroup.LookUp,
                ["user-access-matrix"] = FeatureGroup.UnderstandAccess, ["explain-access"] = FeatureGroup.UnderstandAccess, ["privilege-finder"] = FeatureGroup.UnderstandAccess,
                ["compare-users"] = FeatureGroup.Compare, ["compare-roles"] = FeatureGroup.Compare,
                ["privileged-users"] = FeatureGroup.Review, ["cleanup-report"] = FeatureGroup.Review
            };
            foreach (var pair in expected)
            {
                Assert.Equal(pair.Value, all[pair.Key]);
            }

            Assert.DoesNotContain("users-by-business-unit", all.Keys);
        }

        [Fact]
        public void Order_RejectsDuplicateIds()
        {
            Assert.Throws<InvalidOperationException>(() => FeatureRegistry.Order(new IFeature[] { new Stub("x", 1), new Stub("X", 2) }));
        }

        [Fact]
        public void RegisteredFeatures_HaveUniqueIdsAndHomeFirst()
        {
            var all = FeatureRegistry.CreateAll();
            Assert.Equal("home", all[0].Id);
        }
    }
}
