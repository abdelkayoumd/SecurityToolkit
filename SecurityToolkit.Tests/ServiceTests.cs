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
    }

    public class FeatureRegistryTests
    {
        private sealed class Stub : IFeature
        {
            public Stub(string id, int order, string title = null) { Id = id; Order = order; Title = title ?? id; }
            public string Id { get; }
            public string Title { get; }
            public string Glyph => "";
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
