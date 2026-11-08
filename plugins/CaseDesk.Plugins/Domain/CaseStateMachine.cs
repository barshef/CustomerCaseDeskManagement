using System.Collections.Generic;

namespace CaseDesk.Plugins.Domain
{
    /// <summary>
    /// Allowed status transitions for cd_case (data-model.md "Allowed transitions").
    /// Pure logic: rules that need data (open notes, approvals) are applied by the plugin.
    /// </summary>
    public static class CaseStateMachine
    {
        private static readonly Dictionary<int, HashSet<int>> Allowed = new Dictionary<int, HashSet<int>>
        {
            [CaseStatus.New] = new HashSet<int> { CaseStatus.Assigned, CaseStatus.InProgress, CaseStatus.AwaitingCustomer, CaseStatus.Resolved, CaseStatus.Cancelled },
            [CaseStatus.Assigned] = new HashSet<int> { CaseStatus.InProgress, CaseStatus.AwaitingCustomer, CaseStatus.Resolved, CaseStatus.Cancelled },
            [CaseStatus.InProgress] = new HashSet<int> { CaseStatus.Assigned, CaseStatus.AwaitingCustomer, CaseStatus.Resolved, CaseStatus.Cancelled },
            [CaseStatus.AwaitingCustomer] = new HashSet<int> { CaseStatus.InProgress, CaseStatus.Resolved, CaseStatus.Cancelled },
            // Leaving Awaiting Approval is done by the approval flow; Resolved needs an Approved approval.
            [CaseStatus.AwaitingApproval] = new HashSet<int> { CaseStatus.InProgress, CaseStatus.Resolved },
            [CaseStatus.Resolved] = new HashSet<int> { CaseStatus.InProgress, CaseStatus.Closed },
            [CaseStatus.Closed] = new HashSet<int>(),
            [CaseStatus.Cancelled] = new HashSet<int>(),
        };

        public static bool CanMove(int from, int to)
        {
            if (from == to) return true; // no-op updates are always fine (idempotent retries)
            return Allowed.TryGetValue(from, out var targets) && targets.Contains(to);
        }

        /// <summary>
        /// Status an agent's "Resolve" lands on: Refund cases without a current approval go
        /// to Awaiting Approval instead (FR-005).
        /// </summary>
        public static int ResolveTarget(int category, bool hasApprovedRefund)
        {
            return category == Category.Refund && !hasApprovedRefund
                ? CaseStatus.AwaitingApproval
                : CaseStatus.Resolved;
        }
    }
}
