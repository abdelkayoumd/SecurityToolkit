using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using SecurityToolkit.Models;
using SecurityToolkit.Services;
using Xunit;
using PrivilegeDepth = SecurityToolkit.Services.PrivilegeDepth;

namespace SecurityToolkit.Tests
{
    public class RecordLinkTests
    {
        private const string Id = "6f1d1b8e-3c52-4a31-9d52-0a8d2f4c7b10";

        [Fact]
        public void AModelDrivenAppLink_GivesTheTableAndTheId()
        {
            var ok = RecordLinks.TryParse($"https://org.crm4.dynamics.com/main.aspx?appid=1&pagetype=entityrecord&etn=Account&id={Id}", out var table, out var id);

            Assert.True(ok);
            Assert.Equal("account", table);
            Assert.Equal(Guid.Parse(Id), id);
        }

        [Fact]
        public void TheIdMayComeBeforeTheTable()
        {
            Assert.True(RecordLinks.TryParse($"https://x/main.aspx?id={Id}&etn=contact&pagetype=entityrecord", out var table, out var id));
            Assert.Equal("contact", table);
            Assert.Equal(Guid.Parse(Id), id);
        }

        [Fact]
        public void ABareId_GivesNoTable()
        {
            Assert.True(RecordLinks.TryParse(Id, out var table, out var id));
            Assert.Null(table);
            Assert.Equal(Guid.Parse(Id), id);
        }

        [Theory]
        [InlineData("{6f1d1b8e-3c52-4a31-9d52-0a8d2f4c7b10}")]
        [InlineData("  6F1D1B8E-3C52-4A31-9D52-0A8D2F4C7B10  ")]
        public void AnIdInBracesOrCapitals_IsRead(string text)
        {
            Assert.True(RecordLinks.TryParse(text, out _, out var id));
            Assert.Equal(Guid.Parse(Id), id);
        }

        [Fact]
        public void AnEncodedBraceInALink_IsRead()
        {
            Assert.True(RecordLinks.TryParse($"https://x/main.aspx?etn=account&id=%7B{Id}%7D", out var table, out var id));
            Assert.Equal("account", table);
            Assert.Equal(Guid.Parse(Id), id);
        }

        [Theory]
        [InlineData("Contoso")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("https://x/main.aspx?pagetype=entityrecord&etn=account")]
        public void ANameOrALinkWithoutAnId_IsNotARecordReference(string text)
        {
            Assert.False(RecordLinks.TryParse(text, out var table, out var id));
            Assert.Null(table);
            Assert.Equal(Guid.Empty, id);
        }

        [Fact]
        public void ALongTextThatMerelyContainsAnId_IsTreatedAsAName()
        {
            Assert.False(RecordLinks.TryParse($"the account we discussed last week had the id {Id} in the email", out _, out _));
        }
    }

    public class RecordAccessAnalyzerTests
    {
        private readonly Org _o = new Org();

        private RecordFacts Record(TableOwnership ownership, Guid? owner = null, string ownerName = null, string ownerType = "systemuser", Guid? unit = null, string unitName = null)
        {
            return new RecordFacts
            {
                Table = "account", TableDisplayName = "Account", Id = Guid.NewGuid(), Name = "Contoso", Ownership = ownership,
                OwnerId = owner, OwnerName = ownerName, OwnerType = ownerType, BusinessUnitId = unit, BusinessUnitName = unitName
            };
        }

        private AccessPrincipal Bob => new AccessPrincipal { Id = _o.Bob.Id, Name = "Bob", BusinessUnitId = _o.Sales, BusinessUnitName = "Sales" };
        private AccessPrincipal Ada => new AccessPrincipal { Id = _o.Ada.Id, Name = "Ada", BusinessUnitId = _o.Root, BusinessUnitName = "Root" };
        private AccessPrincipal Dee => new AccessPrincipal { Id = _o.Dee.Id, Name = "Dee", BusinessUnitId = _o.Sales, BusinessUnitName = "Sales" };

        /// <summary>Every right granted by these role records, each at the depth given.</summary>
        private static IReadOnlyDictionary<PrivilegeKind, IReadOnlyDictionary<Guid, PrivilegeDepth>> Grants(params (Guid, PrivilegeDepth)[] roles)
        {
            return RecordAccessAnalyzer.Rights.ToDictionary(k => k, k => (IReadOnlyDictionary<Guid, PrivilegeDepth>)roles.ToDictionary(r => r.Item1, r => r.Item2));
        }

        private static readonly ShareRow[] NoShares = new ShareRow[0];

        private static RecordAccessRow Read(IReadOnlyList<RecordAccessRow> rows) => rows.Single(r => r.Kind == PrivilegeKind.Read);

        [Fact]
        public void ThereIsOneRowPerRecordRight_InOrder_WithoutCreate()
        {
            var rows = RecordAccessAnalyzer.Analyze(_o.Graph, Ada, Record(TableOwnership.UserOrTeam, _o.Bob.Id, "Bob", unit: _o.Sales, unitName: "Sales"), Grants(), NoShares, null);

            Assert.Equal(new[] { "Read", "Write", "Delete", "Append", "Append To", "Assign", "Share" }, rows.Select(r => r.Right));
        }

        [Fact]
        public void OrganizationDepth_ReachesEveryRecord()
        {
            var rows = RecordAccessAnalyzer.Analyze(_o.Graph, Ada, Record(TableOwnership.UserOrTeam, _o.Bob.Id, "Bob", unit: _o.Sales, unitName: "Sales"),
                Grants((_o.Admin.Id, PrivilegeDepth.Organization)), NoShares, null);

            var read = Read(rows);
            Assert.Equal("Yes", read.Result);
            Assert.Contains("Admin", read.Why);
            Assert.Contains("Organization", read.Why);
        }

        [Fact]
        public void UserDepth_ReachesOnlyRecordsThePrincipalOwns()
        {
            var seller = Grants((_o.SellerSales.Id, PrivilegeDepth.User));

            var own = RecordAccessAnalyzer.Analyze(_o.Graph, Bob, Record(TableOwnership.UserOrTeam, _o.Bob.Id, "Bob", unit: _o.Sales, unitName: "Sales"), seller, NoShares, null);
            var someoneElses = RecordAccessAnalyzer.Analyze(_o.Graph, Bob, Record(TableOwnership.UserOrTeam, _o.Dee.Id, "Dee", unit: _o.Sales, unitName: "Sales"), seller, NoShares, null);

            Assert.Equal("Yes", Read(own).Result);
            Assert.Equal("No", Read(someoneElses).Result);
            Assert.StartsWith("Bob does not own the record (owner: Dee)", Read(someoneElses).Why);   // the gap comes first
            Assert.Contains("best role: \"Seller\"", Read(someoneElses).Why);
        }

        [Fact]
        public void BusinessUnitDepth_NeedsTheSameUnit_NotAChildOfIt()
        {
            var auditor = Grants((_o.Auditor.Id, PrivilegeDepth.BusinessUnit)); // Bob holds Auditor through Reviewers

            var same = RecordAccessAnalyzer.Analyze(_o.Graph, Bob, Record(TableOwnership.UserOrTeam, _o.Dee.Id, "Dee", unit: _o.Sales, unitName: "Sales"), auditor, NoShares, null);
            var child = RecordAccessAnalyzer.Analyze(_o.Graph, Bob, Record(TableOwnership.UserOrTeam, _o.Cy.Id, "Cy", unit: _o.Emea, unitName: "Emea"), auditor, NoShares, null);

            Assert.Equal("Yes", Read(same).Result);
            Assert.Equal("No", Read(child).Result);
            Assert.Contains("in Emea, not in Sales", Read(child).Why);
        }

        [Fact]
        public void ParentChildDepth_ReachesTheUnitAndEveryUnitBelow_ButNotAboveIt()
        {
            var wide = Grants((_o.Auditor.Id, PrivilegeDepth.ParentChildBusinessUnits));

            var below = RecordAccessAnalyzer.Analyze(_o.Graph, Bob, Record(TableOwnership.UserOrTeam, _o.Cy.Id, "Cy", unit: _o.Emea, unitName: "Emea"), wide, NoShares, null);
            var above = RecordAccessAnalyzer.Analyze(_o.Graph, Bob, Record(TableOwnership.UserOrTeam, _o.Ada.Id, "Ada", unit: _o.Root, unitName: "Root"), wide, NoShares, null);

            Assert.Equal("Yes", Read(below).Result);
            Assert.Equal("No", Read(above).Result);
            Assert.Contains("neither Sales nor below it", Read(above).Why);
        }

        [Fact]
        public void ATableOwnedByTheOrganization_NeedsOnlyTheRole()
        {
            var rows = RecordAccessAnalyzer.Analyze(_o.Graph, Bob, Record(TableOwnership.Organization), Grants((_o.SellerSales.Id, PrivilegeDepth.User)), NoShares, null);

            var read = Read(rows);
            Assert.Equal("Yes", read.Result);
            Assert.Contains("belong to the organization", read.Why);
        }

        [Fact]
        public void ARecordOwnedByATeamTheUserBelongsTo_CountsAsTheirOwnAtUserDepth()
        {
            var record = Record(TableOwnership.UserOrTeam, _o.Reviewers.Id, "Reviewers", "team", _o.Sales, "Sales");

            var rows = RecordAccessAnalyzer.Analyze(_o.Graph, Bob, record, Grants((_o.SellerSales.Id, PrivilegeDepth.User)), NoShares, null);

            Assert.Equal("Yes", Read(rows).Result);
            Assert.Contains("owned by a team", Read(rows).Why);
        }

        [Fact]
        public void AShareGivesTheRightWithoutAnyRole_AndSaysWho()
        {
            var shares = new[] { new ShareRow { PrincipalId = _o.Dee.Id, Principal = "Dee", Type = "User", Mask = 2, Rights = "Write" } };

            var rows = RecordAccessAnalyzer.Analyze(_o.Graph, Dee, Record(TableOwnership.UserOrTeam, _o.Ada.Id, "Ada", unit: _o.Root, unitName: "Root"), Grants(), shares, null);

            var write = rows.Single(r => r.Kind == PrivilegeKind.Write);
            Assert.Equal("Yes", write.Result);
            Assert.Equal("Shared with Dee (Write).", write.Why);
            Assert.Equal("No", Read(rows).Result);   // the share covers Write only
        }

        [Fact]
        public void AShareWithATeamTheUserBelongsTo_Counts()
        {
            var shares = new[] { new ShareRow { PrincipalId = _o.Reviewers.Id, Principal = "Reviewers", Type = "Team", Mask = 1, Rights = "Read" } };

            var rows = RecordAccessAnalyzer.Analyze(_o.Graph, Bob, Record(TableOwnership.UserOrTeam, _o.Ada.Id, "Ada", unit: _o.Root, unitName: "Root"), Grants(), shares, null);

            Assert.Equal("Yes", Read(rows).Result);
            Assert.Contains("Reviewers", Read(rows).Why);
        }

        [Fact]
        public void WithNoRoleAndNoShare_EveryRightIsNo_AndSaysWhy()
        {
            var rows = RecordAccessAnalyzer.Analyze(_o.Graph, Dee, Record(TableOwnership.UserOrTeam, _o.Ada.Id, "Ada", unit: _o.Root, unitName: "Root"), Grants(), NoShares, null);

            Assert.All(rows, r => Assert.Equal("No", r.Result));
            Assert.Contains("No role Dee holds grants Read on Account", Read(rows).Why);
        }

        [Fact]
        public void ThePlatformsAnswerDecidesTheResult()
        {
            var owner = Record(TableOwnership.UserOrTeam, _o.Bob.Id, "Bob", unit: _o.Sales, unitName: "Sales");
            var seller = Grants((_o.SellerSales.Id, PrivilegeDepth.User));

            var rows = RecordAccessAnalyzer.Analyze(_o.Graph, Bob, owner, seller, NoShares, platformMask: 1); // the platform grants Read only

            Assert.Equal("Yes", Read(rows).Result);
            Assert.False(Read(rows).Differs);
            Assert.Equal("No", rows.Single(r => r.Kind == PrivilegeKind.Write).Result);
            Assert.True(rows.Single(r => r.Kind == PrivilegeKind.Write).Differs);   // my reading expected Write
            Assert.Contains("the platform says no", rows.Single(r => r.Kind == PrivilegeKind.Write).Why);
        }

        [Fact]
        public void WhenThePlatformGrantsWhatNoRouteExplains_ItSaysSo()
        {
            var rows = RecordAccessAnalyzer.Analyze(_o.Graph, Dee, Record(TableOwnership.UserOrTeam, _o.Ada.Id, "Ada", unit: _o.Root, unitName: "Root"), Grants(), NoShares, platformMask: 1);

            var read = Read(rows);
            Assert.Equal("Yes", read.Result);
            Assert.True(read.Differs);
            Assert.Contains("could not find the route", read.Why);
        }

        [Fact]
        public void ATeamIsCheckedByTheRolesItHolds()
        {
            var team = new AccessPrincipal { Id = _o.Reviewers.Id, Name = "Reviewers", IsTeam = true, BusinessUnitId = _o.Sales, BusinessUnitName = "Sales" };
            var rows = RecordAccessAnalyzer.Analyze(_o.Graph, team, Record(TableOwnership.UserOrTeam, _o.Dee.Id, "Dee", unit: _o.Sales, unitName: "Sales"),
                Grants((_o.Auditor.Id, PrivilegeDepth.BusinessUnit)), NoShares, null);

            var read = Read(rows);
            Assert.Equal("Yes", read.Result);
            Assert.Contains("held by the team", read.Why);
        }

        [Fact]
        public void ATeamOwnsItsOwnRecords_ButAMembersTeamsDoNotCountForATeam()
        {
            var team = new AccessPrincipal { Id = _o.Reviewers.Id, Name = "Reviewers", IsTeam = true, BusinessUnitId = _o.Sales, BusinessUnitName = "Sales" };
            var owned = Record(TableOwnership.UserOrTeam, _o.Reviewers.Id, "Reviewers", "team", _o.Sales, "Sales");

            var rows = RecordAccessAnalyzer.Analyze(_o.Graph, team, owned, Grants((_o.Auditor.Id, PrivilegeDepth.User)), NoShares, null);

            Assert.Equal("Yes", Read(rows).Result);
        }

        [Fact]
        public void WhenSeveralRoutesReach_TheStrongestIsNamed_AndTheRestCounted()
        {
            var rows = RecordAccessAnalyzer.Analyze(_o.Graph, Bob, Record(TableOwnership.UserOrTeam, _o.Bob.Id, "Bob", unit: _o.Sales, unitName: "Sales"),
                Grants((_o.SellerSales.Id, PrivilegeDepth.User), (_o.Auditor.Id, PrivilegeDepth.BusinessUnit)), NoShares, null);

            Assert.Contains("Role \"Auditor\"", Read(rows).Why);
            Assert.Contains("and 1 more route", Read(rows).Why);
        }

        [Theory]
        [InlineData(1, "Read")]
        [InlineData(3, "Read, Write")]
        [InlineData(0, "no rights")]
        [InlineData(65536 | 262144, "Delete, Share")]
        public void ARightsMask_IsPutInWords(int mask, string expected)
        {
            Assert.Equal(expected, RecordAccessAnalyzer.RightsText(mask));
        }

        [Fact]
        public void TheRelationBetweenThePrincipalAndTheRecord_IsSaidInWords()
        {
            Assert.Contains("owns the record", RecordAccessAnalyzer.Relation(_o.Graph, Bob, Record(TableOwnership.UserOrTeam, _o.Bob.Id, "Bob", unit: _o.Sales, unitName: "Sales")));
            Assert.Contains("same business unit", RecordAccessAnalyzer.Relation(_o.Graph, Bob, Record(TableOwnership.UserOrTeam, _o.Dee.Id, "Dee", unit: _o.Sales, unitName: "Sales")));
            Assert.Contains("below Bob's", RecordAccessAnalyzer.Relation(_o.Graph, Bob, Record(TableOwnership.UserOrTeam, _o.Cy.Id, "Cy", unit: _o.Emea, unitName: "Emea")));
            Assert.Contains("parent business unit", RecordAccessAnalyzer.Relation(_o.Graph, Bob, Record(TableOwnership.UserOrTeam, _o.Ada.Id, "Ada", unit: _o.Root, unitName: "Root")));
            Assert.Contains("belongs to the organization", RecordAccessAnalyzer.Relation(_o.Graph, Bob, Record(TableOwnership.Organization)));
        }
    }

    public class RecordServiceTests
    {
        private static void Set(object target, string property, object value)
        {
            target.GetType().GetProperty(property).GetSetMethod(true).Invoke(target, new[] { value });
        }

        private static EntityMetadata Meta(OwnershipTypes ownership, string primaryName = "name")
        {
            var meta = new EntityMetadata { LogicalName = "account", DisplayName = new Label("Account", 1033) };
            Set(meta, "OwnershipType", (OwnershipTypes?)ownership);
            Set(meta, "PrimaryNameAttribute", primaryName);
            return meta;
        }

        private static FakeOrganizationService Fake(OwnershipTypes ownership = OwnershipTypes.UserOwned)
        {
            var fake = new FakeOrganizationService();
            fake.OnExecute = request =>
            {
                switch (request)
                {
                    case RetrieveEntityRequest _:
                        var entity = new RetrieveEntityResponse();
                        entity.Results["EntityMetadata"] = Meta(ownership);
                        return entity;
                    case RetrievePrincipalAccessRequest _:
                        var access = new RetrievePrincipalAccessResponse();
                        access.Results["AccessRights"] = AccessRights.ReadAccess | AccessRights.WriteAccess;
                        return access;
                    case RetrieveSharedPrincipalsAndAccessRequest _:
                        var shared = new RetrieveSharedPrincipalsAndAccessResponse();
                        shared.Results["PrincipalAccesses"] = new[]
                        {
                            new PrincipalAccess { Principal = new EntityReference("team", Guid.NewGuid()) { Name = "Sales" }, AccessMask = AccessRights.ReadAccess },
                            new PrincipalAccess { Principal = new EntityReference("systemuser", Guid.NewGuid()), AccessMask = AccessRights.ReadAccess | AccessRights.ShareAccess }
                        };
                        return shared;
                    default:
                        return new OrganizationResponse();
                }
            };
            return fake;
        }

        [Fact]
        public void TheTableDetails_SayWhoOwnsItsRecords_AndWhatItsNameColumnIs()
        {
            var service = new SecurityService(Fake(OwnershipTypes.UserOwned));

            var details = service.GetTableDetails("account");

            Assert.Equal(TableOwnership.UserOrTeam, details.Ownership);
            Assert.Equal("name", details.PrimaryNameAttribute);
            Assert.Equal("Account", details.DisplayName);
        }

        [Theory]
        [InlineData(OwnershipTypes.TeamOwned, 0)]
        [InlineData(OwnershipTypes.BusinessOwned, 1)]
        [InlineData(OwnershipTypes.OrganizationOwned, 2)]
        public void TheOwnershipOfATable_IsMapped(OwnershipTypes type, int expected)
        {
            Assert.Equal((TableOwnership)expected, new SecurityService(Fake(type)).GetTableDetails("account").Ownership);
        }

        [Fact]
        public void TheTableDetails_AreAskedForOnce()
        {
            var fake = Fake();
            var service = new SecurityService(fake);

            service.GetTableDetails("account");
            service.GetTableDetails("ACCOUNT");

            Assert.Single(fake.ExecutedRequests.OfType<RetrieveEntityRequest>());
        }

        [Fact]
        public void ARecordOfAUserOwnedTable_IsReadWithItsOwnerAndUnit()
        {
            var fake = Fake();
            var owner = Guid.NewGuid();
            var unit = Guid.NewGuid();
            ColumnSet asked = null;
            fake.OnRetrieve = (table, id, columns) =>
            {
                asked = columns;
                return new Entity(table, id)
                {
                    ["name"] = "Contoso",
                    ["ownerid"] = new EntityReference("team", owner) { Name = "Sales" },
                    ["owningbusinessunit"] = new EntityReference("businessunit", unit) { Name = "Contoso EMEA" }
                };
            };

            var facts = new SecurityService(fake).GetRecordFacts("account", Guid.NewGuid());

            Assert.Equal(new[] { "name", "ownerid", "owningbusinessunit" }, asked.Columns);
            Assert.Equal("Contoso", facts.Name);
            Assert.Equal(owner, facts.OwnerId);
            Assert.True(facts.OwnedByTeam);
            Assert.Equal("Contoso EMEA", facts.BusinessUnitName);
            Assert.Equal(TableOwnership.UserOrTeam, facts.Ownership);
        }

        [Fact]
        public void ARecordOfAnOrganizationOwnedTable_IsReadWithoutOwnerColumns()
        {
            var fake = Fake(OwnershipTypes.OrganizationOwned);
            ColumnSet asked = null;
            fake.OnRetrieve = (table, id, columns) => { asked = columns; return new Entity(table, id) { ["name"] = "Setting" }; };

            var facts = new SecurityService(fake).GetRecordFacts("account", Guid.NewGuid());

            Assert.Equal(new[] { "name" }, asked.Columns);
            Assert.Null(facts.OwnerId);
        }

        [Fact]
        public void SearchingByName_AsksForRecordsContainingTheText_AndEscapesPatternCharacters()
        {
            var fake = Fake();
            var found = Guid.NewGuid();
            fake.OnRetrieveMultiple = q => new EntityCollection(new List<Entity> { new Entity("account", found) { ["name"] = "100% Contoso" } });

            var matches = new SecurityService(fake).SearchRecords("account", "100%_[x");

            var query = (QueryExpression)fake.Queries.Single();
            var condition = query.Criteria.Conditions.Single();
            Assert.Equal("name", condition.AttributeName);
            Assert.Equal(ConditionOperator.Like, condition.Operator);
            Assert.Equal("%100[%][_][[]x%", condition.Values[0]);
            Assert.Equal(25, query.TopCount);
            Assert.Equal("100% Contoso", Assert.Single(matches).Name);
            Assert.Equal(found, matches[0].Id);
        }

        [Fact]
        public void SearchingForNothing_FindsNothing_WithoutAskingDataverse()
        {
            var fake = Fake();

            Assert.Empty(new SecurityService(fake).SearchRecords("account", "   "));
            Assert.Empty(fake.Queries);
        }

        [Fact]
        public void ThePlatformsAccess_IsTheRightsMask()
        {
            var principal = new AccessPrincipal { Id = Guid.NewGuid(), Name = "Ada", IsTeam = false };
            var fake = Fake();

            var mask = new SecurityService(fake).GetPrincipalAccess(principal, "account", Guid.NewGuid());

            Assert.Equal(3, mask);
            var request = (RetrievePrincipalAccessRequest)fake.ExecutedRequests.Last();
            Assert.Equal("systemuser", request.Principal.LogicalName);
            Assert.Equal("account", request.Target.LogicalName);
        }

        [Fact]
        public void ATeamIsAskedAboutAsATeam()
        {
            var fake = Fake();

            new SecurityService(fake).GetPrincipalAccess(new AccessPrincipal { Id = Guid.NewGuid(), IsTeam = true }, "account", Guid.NewGuid());

            Assert.Equal("team", ((RetrievePrincipalAccessRequest)fake.ExecutedRequests.Last()).Principal.LogicalName);
        }

        [Fact]
        public void TheShares_NameWhoHasWhat()
        {
            var shares = new SecurityService(Fake()).GetShares("account", Guid.NewGuid());

            Assert.Equal(2, shares.Count);
            Assert.Equal("Team", shares[0].Type);
            Assert.Equal("Sales", shares[0].Principal);
            Assert.Equal("Read", shares[0].Rights);
            Assert.Equal("User", shares[1].Type);
            Assert.Equal("Read, Share", shares[1].Rights);
        }
    }
}
