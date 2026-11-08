using CaseDesk.Plugins.Domain;
using Xunit;
using S = CaseDesk.Plugins.Domain.CaseStatus;

namespace CaseDesk.Plugins.Tests
{
    public class CaseStateMachineTests
    {
        [Theory]
        [InlineData(S.New, S.Assigned)]
        [InlineData(S.New, S.InProgress)]
        [InlineData(S.New, S.AwaitingCustomer)]
        [InlineData(S.New, S.Resolved)]
        [InlineData(S.New, S.Cancelled)]
        [InlineData(S.Assigned, S.InProgress)]
        [InlineData(S.Assigned, S.AwaitingCustomer)]
        [InlineData(S.Assigned, S.Resolved)]
        [InlineData(S.Assigned, S.Cancelled)]
        [InlineData(S.InProgress, S.Assigned)]
        [InlineData(S.InProgress, S.AwaitingCustomer)]
        [InlineData(S.InProgress, S.Resolved)]
        [InlineData(S.InProgress, S.Cancelled)]
        [InlineData(S.AwaitingCustomer, S.InProgress)]
        [InlineData(S.AwaitingCustomer, S.Resolved)]
        [InlineData(S.AwaitingCustomer, S.Cancelled)]
        [InlineData(S.AwaitingApproval, S.InProgress)]
        [InlineData(S.AwaitingApproval, S.Resolved)]
        [InlineData(S.Resolved, S.InProgress)]
        [InlineData(S.Resolved, S.Closed)]
        public void Allowed_transitions(int from, int to)
        {
            Assert.True(CaseStateMachine.CanMove(from, to));
        }

        [Theory]
        [InlineData(S.New, S.Closed)]
        [InlineData(S.InProgress, S.Closed)]
        [InlineData(S.New, S.AwaitingApproval)]
        [InlineData(S.InProgress, S.AwaitingApproval)]
        [InlineData(S.AwaitingCustomer, S.Assigned)]
        [InlineData(S.AwaitingApproval, S.Closed)]
        [InlineData(S.AwaitingApproval, S.Cancelled)]
        [InlineData(S.Resolved, S.Cancelled)]
        [InlineData(S.Resolved, S.New)]
        [InlineData(S.Closed, S.InProgress)]
        [InlineData(S.Closed, S.Cancelled)]
        [InlineData(S.Cancelled, S.InProgress)]
        [InlineData(S.Cancelled, S.Closed)]
        public void Rejected_transitions(int from, int to)
        {
            Assert.False(CaseStateMachine.CanMove(from, to));
        }

        [Fact]
        public void Same_status_is_always_allowed_for_idempotent_retries()
        {
            Assert.True(CaseStateMachine.CanMove(S.Closed, S.Closed));
        }

        [Fact]
        public void Refund_without_approval_resolves_to_awaiting_approval()
        {
            Assert.Equal(S.AwaitingApproval, CaseStateMachine.ResolveTarget(Category.Refund, hasApprovedRefund: false));
            Assert.Equal(S.Resolved, CaseStateMachine.ResolveTarget(Category.Refund, hasApprovedRefund: true));
            Assert.Equal(S.Resolved, CaseStateMachine.ResolveTarget(Category.Billing, hasApprovedRefund: false));
        }

        [Theory]
        [InlineData(S.Closed, CaseState.Inactive)]
        [InlineData(S.Cancelled, CaseState.Inactive)]
        [InlineData(S.Resolved, CaseState.Active)]
        [InlineData(S.New, CaseState.Active)]
        public void State_of_status(int status, int state)
        {
            Assert.Equal(state, S.StateOf(status));
        }
    }
}
