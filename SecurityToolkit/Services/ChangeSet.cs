using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;

namespace SecurityToolkit.Services
{
    /// <summary>One planned write: a human-readable description plus the request that performs it.</summary>
    internal sealed class ChangeItem
    {
        public ChangeItem(string action, string subject, string target, OrganizationRequest request)
        {
            Action = action;
            Subject = subject;
            Target = target;
            Request = request ?? throw new ArgumentNullException(nameof(request));
        }

        /// <summary>For example "Add role" / "Remove from team".</summary>
        public string Action { get; }
        public string Subject { get; }
        public string Target { get; }
        public OrganizationRequest Request { get; }
        public string Description => $"{Action}: {Subject} → {Target}";

        /// <summary>The change that puts things back as they were, or null when this one cannot be undone.</summary>
        public ChangeItem Inverse { get; set; }
    }

    /// <summary>
    /// What a write feature is about to do: the changes, plus notes on what was left out and why. It is shown to the
    /// user for approval before anything is sent to Dataverse.
    /// </summary>
    internal sealed class ChangePlan
    {
        public List<ChangeItem> Items { get; } = new List<ChangeItem>();

        /// <summary>Things that were skipped or need attention ("no copy of role X in business unit Y").</summary>
        public List<string> Notes { get; } = new List<string>();

        /// <summary>True when every change in the plan can be undone.</summary>
        public bool IsReversible => Items.Count > 0 && Items.All(i => i.Inverse != null);

        /// <summary>
        /// The plan that undoes <paramref name="done"/> (the changes that succeeded): their inverses, last change first.
        /// Changes without an inverse are reported in the notes.
        /// </summary>
        public static ChangePlan UndoOf(IEnumerable<ChangeItem> done)
        {
            var plan = new ChangePlan();
            foreach (var item in done.Reverse())
            {
                if (item.Inverse != null)
                {
                    plan.Items.Add(item.Inverse);
                }
                else
                {
                    plan.Notes.Add($"Cannot be undone: {item.Description}");
                }
            }

            return plan;
        }
    }

    internal sealed class ChangeResult
    {
        public ChangeResult(ChangeItem item, bool success, string error)
        {
            Item = item;
            Success = success;
            Error = error;
        }

        public ChangeItem Item { get; }
        public bool Success { get; }
        public string Error { get; }
    }

    /// <summary>Applies a list of <see cref="ChangeItem"/> through ExecuteMultiple and reports each outcome.</summary>
    internal static class ChangeExecutor
    {
        public const int BatchSize = 100;

        public static List<ChangeResult> Apply(IOrganizationService service, IReadOnlyList<ChangeItem> items, Action<int, int> progress = null)
        {
            var results = new List<ChangeResult>(items.Count);
            for (var offset = 0; offset < items.Count; offset += BatchSize)
            {
                var batch = items.Skip(offset).Take(BatchSize).ToList();
                var multiple = new ExecuteMultipleRequest
                {
                    Settings = new ExecuteMultipleSettings { ContinueOnError = true, ReturnResponses = true },
                    Requests = new OrganizationRequestCollection()
                };
                multiple.Requests.AddRange(batch.Select(b => b.Request));

                var response = (ExecuteMultipleResponse)service.Execute(multiple);
                var faults = response.Responses.Where(r => r.Fault != null).ToDictionary(r => r.RequestIndex, r => r.Fault.Message);
                for (var i = 0; i < batch.Count; i++)
                {
                    results.Add(faults.TryGetValue(i, out var error)
                        ? new ChangeResult(batch[i], false, error)
                        : new ChangeResult(batch[i], true, null));
                }

                progress?.Invoke(Math.Min(offset + batch.Count, items.Count), items.Count);
            }

            return results;
        }

        /// <summary>An AssociateRequest for the many-to-many links security uses (user-role, team-role, team-member).</summary>
        public static OrganizationRequest Associate(string entity, Guid id, string relationship, string relatedEntity, Guid relatedId)
        {
            return new AssociateRequest
            {
                Target = new EntityReference(entity, id),
                Relationship = new Relationship(relationship),
                RelatedEntities = new EntityReferenceCollection { new EntityReference(relatedEntity, relatedId) }
            };
        }

        public static OrganizationRequest Disassociate(string entity, Guid id, string relationship, string relatedEntity, Guid relatedId)
        {
            return new DisassociateRequest
            {
                Target = new EntityReference(entity, id),
                Relationship = new Relationship(relationship),
                RelatedEntities = new EntityReferenceCollection { new EntityReference(relatedEntity, relatedId) }
            };
        }
    }
}
