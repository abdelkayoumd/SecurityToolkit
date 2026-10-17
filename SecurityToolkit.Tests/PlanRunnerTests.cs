using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using SecurityToolkit.Features;
using SecurityToolkit.Services;
using SecurityToolkit.UI;
using Xunit;

namespace SecurityToolkit.Tests
{
    /// <summary>A scripted stand-in for the windows: records what it was shown and answers as told.</summary>
    internal sealed class ScriptedUi : IChangeUi
    {
        public bool Approve = true;
        public bool SaveRestore = false;
        public string RestorePath;
        public bool ApplyWithoutRestore = false;
        public Queue<bool> UndoAnswers = new Queue<bool>();

        public List<ChangePlan> Reviewed = new List<ChangePlan>();
        public List<IReadOnlyList<ChangeResult>> Shown = new List<IReadOnlyList<ChangeResult>>();
        public List<string> Told = new List<string>();

        public bool Review(ChangePlan plan, out bool saveRestoreFile)
        {
            Reviewed.Add(plan);
            saveRestoreFile = SaveRestore;
            return Approve;
        }

        public string AskRestoreFilePath() => RestorePath;
        public bool ConfirmApplyWithoutRestoreFile() => ApplyWithoutRestore;

        public bool ShowResults(IReadOnlyList<ChangeResult> results)
        {
            Shown.Add(results);
            return UndoAnswers.Count > 0 && UndoAnswers.Dequeue();
        }

        public void Tell(string message) { Told.Add(message); }

        public List<string> Asked = new List<string>();
        public bool Answer = true;
        public bool Ask(string question) { Asked.Add(question); return Answer; }
    }

    public class PlanRunnerTests
    {
        private readonly WriteOrg _o = new WriteOrg();
        private readonly ScriptedUi _ui = new ScriptedUi();
        private readonly FakeOrganizationService _service = new FakeOrganizationService();
        private readonly TestHost _host;
        private int _refreshed;

        public PlanRunnerTests()
        {
            _service.OnExecute = request =>
            {
                var response = new ExecuteMultipleResponse();
                response.Results["Responses"] = new ExecuteMultipleResponseItemCollection();
                return response;
            };
            _host = new TestHost { Security = new SecurityService(_service) };
        }

        private ChangePlan TwoChanges()
        {
            return ChangePlanner.PlanUserRoles(_o.Graph, new[] { _o.Dee, _o.Ada }, new[] { _o.Choice("Seller") }, add: true);
        }

        private PlanRunner Runner() => new PlanRunner(_host, _ui);

        [Fact]
        public void NothingIsSent_UntilTheUserApprovesTheReview()
        {
            _ui.Approve = false;

            Runner().Run(TwoChanges(), () => _refreshed++);

            Assert.Single(_ui.Reviewed);
            Assert.Empty(_service.ExecutedRequests);
            Assert.Empty(_ui.Shown);
            Assert.Equal(0, _refreshed);
        }

        [Fact]
        public void AnApprovedPlan_IsApplied_ReportedAndThenRefreshes()
        {
            Runner().Run(TwoChanges(), () => _refreshed++);

            var sent = Assert.Single(_service.ExecutedRequests); // one ExecuteMultiple for both
            Assert.Equal(2, ((ExecuteMultipleRequest)sent).Requests.Count);
            Assert.Equal(2, Assert.Single(_ui.Shown).Count);
            Assert.Equal(1, _refreshed);
        }

        [Fact]
        public void AnEmptyPlan_ShowsWhyInsteadOfAReview()
        {
            var plan = ChangePlanner.PlanUserRoles(_o.Graph, new[] { _o.Bob }, new[] { _o.Choice("Seller") }, add: true); // Bob has it

            Runner().Run(plan, () => _refreshed++);

            Assert.Empty(_ui.Reviewed);
            Assert.Empty(_service.ExecutedRequests);
            Assert.Contains("nothing to change", _ui.Told.Single());
            Assert.Contains("already has it", _ui.Told.Single());
        }

        [Fact]
        public void TheCachedSecurityData_IsDroppedAfterChanges()
        {
            var fake = new FakeOrganizationService { OnExecute = _service.OnExecute };
            fake.OnRetrieveMultiple = _service.OnRetrieveMultiple;
            var security = new SecurityService(fake);
            security.GetUsers();
            var host = new TestHost { Security = security };

            new PlanRunner(host, _ui).Run(TwoChanges(), null);
            security.GetUsers();

            Assert.Equal(2, fake.Queries.Count); // fetched again, not served from the stale cache
        }

        [Fact]
        public void FailedChanges_AreReported_AndOnlyTheSuccessfulOnesCanBeUndone()
        {
            _service.OnExecute = request =>
            {
                var response = new ExecuteMultipleResponse();
                var items = new ExecuteMultipleResponseItemCollection();
                items.Add(new ExecuteMultipleResponseItem { RequestIndex = 0, Fault = new OrganizationServiceFault { Message = "no privilege" } });
                response.Results["Responses"] = items;
                return response;
            };
            _ui.UndoAnswers.Enqueue(true);
            _service.ExecutedRequests.Clear();

            Runner().Run(TwoChanges(), () => _refreshed++);

            Assert.Equal(new[] { false, true }, _ui.Shown[0].Select(r => r.Success));
            // The undo plan reviewed second contains only the one change that succeeded.
            Assert.Equal(2, _ui.Reviewed.Count);
            Assert.Single(_ui.Reviewed[1].Items);
            Assert.Equal("Remove role", _ui.Reviewed[1].Items[0].Action);
        }

        [Fact]
        public void Undo_ReviewsAndAppliesTheOppositeChanges_ThenRefreshesOnce()
        {
            _ui.UndoAnswers.Enqueue(true);

            Runner().Run(TwoChanges(), () => _refreshed++);

            Assert.Equal(2, _ui.Reviewed.Count);
            Assert.All(_ui.Reviewed[0].Items, i => Assert.Equal("Add role", i.Action));
            Assert.All(_ui.Reviewed[1].Items, i => Assert.Equal("Remove role", i.Action));
            Assert.Equal(2, _service.ExecutedRequests.Count);
            Assert.IsType<DisassociateRequest>(((ExecuteMultipleRequest)_service.ExecutedRequests[1]).Requests[0]);
            Assert.Equal(1, _refreshed);
        }

        [Fact]
        public void TheUndoIsReviewedLikeAnyOtherChange_AndCanBeRefused()
        {
            // The user approves the original, asks to undo it, then refuses the undo's review.
            var inner = new ScriptedUi();
            inner.UndoAnswers.Enqueue(true);
            var gate = new RefusingSecondReview(inner);

            new PlanRunner(_host, gate).Run(TwoChanges(), () => _refreshed++);

            Assert.Equal(2, gate.Reviews);
            Assert.Single(_service.ExecutedRequests); // only the original went out
            Assert.Equal(1, _refreshed);              // the original changes were applied, so the view must still reload
        }

        private sealed class RefusingSecondReview : IChangeUi
        {
            private readonly ScriptedUi _inner;
            public RefusingSecondReview(ScriptedUi inner) { _inner = inner; }
            public int Reviews;
            public bool Review(ChangePlan plan, out bool save) { Reviews++; save = false; return Reviews == 1; }
            public string AskRestoreFilePath() => null;
            public bool ConfirmApplyWithoutRestoreFile() => false;
            public bool ShowResults(IReadOnlyList<ChangeResult> results) => _inner.ShowResults(results);
            public void Tell(string message) { }
            public bool Ask(string question) => false;
        }

        [Fact]
        public void RestoreFile_IsWrittenBeforeAnythingIsApplied()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xlsx");
            try
            {
                _ui.SaveRestore = true;
                _ui.RestorePath = path;
                var existedWhenApplied = false;
                _service.OnExecute = request =>
                {
                    existedWhenApplied = File.Exists(path);
                    var response = new ExecuteMultipleResponse();
                    response.Results["Responses"] = new ExecuteMultipleResponseItemCollection();
                    return response;
                };

                Runner().Run(TwoChanges(), null);

                Assert.True(existedWhenApplied);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void DecliningARestoreFile_AsksBeforeApplyingWithoutOne()
        {
            _ui.SaveRestore = true;
            _ui.RestorePath = null;

            _ui.ApplyWithoutRestore = false;
            Runner().Run(TwoChanges(), null);
            Assert.Empty(_service.ExecutedRequests);

            _ui.ApplyWithoutRestore = true;
            Runner().Run(TwoChanges(), null);
            Assert.Single(_service.ExecutedRequests);
        }

        [Fact]
        public void IfTheRestoreFileCannotBeWritten_NothingIsChanged()
        {
            _ui.SaveRestore = true;
            _ui.RestorePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "missing-folder", "restore.xlsx");

            Runner().Run(TwoChanges(), null);

            Assert.Empty(_service.ExecutedRequests);
            Assert.Contains("nothing was changed", _ui.Told.Single());
        }
    }

    public class RestoreFileTests
    {
        private readonly WriteOrg _o = new WriteOrg();

        [Fact]
        public void Rows_ListTheReversingChangesLastChangeFirst()
        {
            var plan = ChangePlanner.PlanClone(_o.Graph, _o.Bob, new[] { _o.Ada }, new CloneOptions { Roles = true, Teams = true, RemoveExtras = true });

            var rows = RestoreFile.Rows(plan);

            Assert.Equal(plan.Items.Count, rows.Count);
            Assert.Equal(plan.Items.Last().Inverse.Action, rows[0][0]);
            Assert.All(rows, r => Assert.Equal("Yes", r[4]));
        }

        [Fact]
        public void Rows_FlagTheChangesThatCannotBeReversed()
        {
            var plan = ChangePlanner.PlanClone(_o.Graph, _o.Bob, new[] { _o.Ada }, new CloneOptions { BusinessUnit = true });

            var row = Assert.Single(RestoreFile.Rows(plan));

            Assert.Equal("(cannot be undone)", row[0]);
            Assert.Equal("No", row[4]);
        }

        [Fact]
        public void Write_ProducesAWorkbookWithTheRows()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xlsx");
            try
            {
                var plan = ChangePlanner.PlanUserRoles(_o.Graph, new[] { _o.Dee }, new[] { _o.Choice("Admin") }, true);

                RestoreFile.Write(path, plan);

                using (var package = Package.Open(path, FileMode.Open, FileAccess.Read))
                {
                    var sheet = XDocument.Load(package.GetPart(new Uri("/xl/worksheets/sheet1.xml", UriKind.Relative)).GetStream());
                    var texts = sheet.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value).ToList();
                    Assert.Contains("Remove role", texts);
                    Assert.Contains("Dee", texts);
                }
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
