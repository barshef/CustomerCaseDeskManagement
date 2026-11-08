using System;
using System.Linq;
using CaseDesk.Plugins.Domain;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using C = CaseDesk.Plugins.Domain.Schema.Case;
using N = CaseDesk.Plugins.Domain.Schema.CaseNote;
using R = CaseDesk.Plugins.Domain.Schema.RefundApproval;

namespace CaseDesk.Plugins
{
    /// <summary>
    /// Step S2 — Update of cd_case, PreOperation, synchronous; filtering attributes
    /// statecode, statuscode, cd_priority, cd_category; pre-image "PreImage".
    /// Enforces the lifecycle (FR-004a), the open-note guard (FR-004), resolution data (FR-004b),
    /// refund routing (FR-005) and due-date recalculation on priority change (FR-003).
    /// </summary>
    public sealed class CasePreUpdatePlugin : PluginBase
    {
        protected override void ExecuteCaseDesk(LocalPluginContext context)
        {
            var target = context.Target;
            var pre = context.PreImage();
            if (target == null || pre == null || target.LogicalName != C.EntityName) return;

            var service = context.SystemService; // guards must see every note/approval, not only the caller's
            var from = pre.GetAttributeValue<OptionSetValue>(C.StatusCode)?.Value ?? CaseStatus.New;
            var requested = target.GetAttributeValue<OptionSetValue>(C.StatusCode)?.Value ?? from;
            var category = Value(target, pre, C.Category) ?? Category.Other;
            var preCategory = pre.GetAttributeValue<OptionSetValue>(C.Category)?.Value;

            if (from == CaseStatus.AwaitingApproval && target.Contains(C.Category) && category != preCategory)
                throw CaseDeskErrors.CategoryLockedDuringApproval();

            var finalStatus = requested == from ? from : ApplyTransition(context, service, target, pre, from, requested, category);

            RecalculateSlaIfPriorityChanged(context, service, target, pre, finalStatus);
        }

        private static int ApplyTransition(LocalPluginContext context, IOrganizationService service,
            Entity target, Entity pre, int from, int requested, int category)
        {
            if (!CaseStateMachine.CanMove(from, requested))
                throw CaseDeskErrors.IllegalTransition(from, requested);

            var final = requested;
            if (requested == CaseStatus.Resolved)
            {
                if (from == CaseStatus.AwaitingApproval)
                {
                    // Only the approval flow should get here, after recording an Approved decision.
                    if (!LatestApprovalIsApproved(service, pre.Id))
                        throw CaseDeskErrors.IllegalTransition(from, requested);
                }
                else
                {
                    var summary = target.Contains(C.ResolutionSummary)
                        ? target.GetAttributeValue<string>(C.ResolutionSummary)
                        : pre.GetAttributeValue<string>(C.ResolutionSummary);
                    if (string.IsNullOrWhiteSpace(summary))
                        throw CaseDeskErrors.MissingResolutionSummary();

                    var openNotes = CountOpenNotes(service, pre.Id);
                    if (openNotes > 0)
                        throw CaseDeskErrors.OpenNotes(openNotes);

                    // From a working state a refund always needs a fresh approval.
                    final = CaseStateMachine.ResolveTarget(category, hasApprovedRefund: false);
                }
            }

            target[C.StatusCode] = new OptionSetValue(final);
            target[C.StateCode] = new OptionSetValue(CaseStatus.StateOf(final));

            var now = DateTime.UtcNow;
            if (final == CaseStatus.Resolved)
            {
                target[C.ResolvedOn] = now;
            }
            else if (from == CaseStatus.Resolved && final == CaseStatus.InProgress)
            {
                target[C.ResolvedOn] = null;
                target[C.CustomerReplyPending] = false;
            }
            else if (final == CaseStatus.Closed || final == CaseStatus.Cancelled)
            {
                target[C.ClosedOn] = now;
            }

            context.Trace("Status {0} -> {1} (requested {2})",
                CaseStatus.Label(from), CaseStatus.Label(final), CaseStatus.Label(requested));
            return final;
        }

        private static void RecalculateSlaIfPriorityChanged(LocalPluginContext context, IOrganizationService service,
            Entity target, Entity pre, int finalStatus)
        {
            if (!target.Contains(C.Priority)) return;
            var newPriority = target.GetAttributeValue<OptionSetValue>(C.Priority)?.Value;
            var oldPriority = pre.GetAttributeValue<OptionSetValue>(C.Priority)?.Value;
            if (newPriority == null || newPriority == oldPriority) return;
            if (!CaseStatus.IsOpen(finalStatus)) return; // finished cases keep their SLA history

            var createdOn = pre.Contains(C.CreatedOn) ? pre.GetAttributeValue<DateTime>(C.CreatedOn) : DateTime.UtcNow;
            SlaPolicies.Apply(service, target, newPriority.Value, createdOn);
            context.Trace("Priority {0} -> {1}: due date now {2:O}",
                Priority.Label(oldPriority ?? 0), Priority.Label(newPriority.Value), target[C.DueDate]);
        }

        private static int CountOpenNotes(IOrganizationService service, Guid caseId)
        {
            var query = new QueryExpression(N.EntityName)
            {
                ColumnSet = new ColumnSet(false),
                NoLock = true,
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression(N.Case, ConditionOperator.Equal, caseId),
                        new ConditionExpression(N.IsOpen, ConditionOperator.Equal, true),
                    },
                },
            };
            return service.RetrieveMultiple(query).Entities.Count;
        }

        private static bool LatestApprovalIsApproved(IOrganizationService service, Guid caseId)
        {
            var query = new QueryExpression(R.EntityName)
            {
                ColumnSet = new ColumnSet(R.Decision, R.CreatedOn),
                Criteria = { Conditions = { new ConditionExpression(R.Case, ConditionOperator.Equal, caseId) } },
            };
            query.AddOrder(R.CreatedOn, OrderType.Descending);
            // Sort in memory too: the in-memory test service and Dataverse must agree on "latest".
            var latest = service.RetrieveMultiple(query).Entities
                .OrderByDescending(e => e.GetAttributeValue<DateTime>(R.CreatedOn))
                .FirstOrDefault();
            return latest?.GetAttributeValue<OptionSetValue>(R.Decision)?.Value == RefundDecision.Approved;
        }

        private static int? Value(Entity target, Entity pre, string attribute) =>
            target.Contains(attribute)
                ? target.GetAttributeValue<OptionSetValue>(attribute)?.Value
                : pre.GetAttributeValue<OptionSetValue>(attribute)?.Value;
    }
}
