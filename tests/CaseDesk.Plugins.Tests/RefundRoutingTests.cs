using System;
using System.Collections.Generic;
using CaseDesk.Plugins.Domain;
using FakeXrmEasy;
using Microsoft.Xrm.Sdk;
using Xunit;
using C = CaseDesk.Plugins.Domain.Schema.Case;
using S = CaseDesk.Plugins.Domain.CaseStatus;

namespace CaseDesk.Plugins.Tests
{
    public class RefundRoutingTests
    {
        private static Entity Run(Entity existing, Entity target, params Entity[] related)
        {
            var fake = new XrmFakedContext();
            var data = new List<Entity>(TestData.AllPolicies()) { existing };
            data.AddRange(related);
            fake.Initialize(data);
            fake.ExecutePluginWith<CasePreUpdatePlugin>(TestData.UpdateContext(fake, target, existing));
            return target;
        }

        private static Entity RefundCase(int status)
        {
            var e = TestData.ExistingCase(status, category: Category.Refund);
            e[C.ResolutionSummary] = "Refund due";
            e[C.RefundAmount] = new Money(50m);
            return e;
        }

        [Fact]
        public void Resolving_a_refund_case_goes_to_awaiting_approval()
        {
            var existing = RefundCase(S.InProgress);
            var target = TestData.StatusChange(existing, S.Resolved);

            Run(existing, target);

            Assert.Equal(S.AwaitingApproval, target.GetAttributeValue<OptionSetValue>(C.StatusCode).Value);
            Assert.Equal(CaseState.Active, target.GetAttributeValue<OptionSetValue>(C.StateCode).Value);
            Assert.False(target.Contains(C.ResolvedOn));
        }

        [Fact]
        public void An_old_approval_does_not_skip_a_new_approval()
        {
            // Reopened after an earlier approval: resolving again must ask again.
            var existing = RefundCase(S.InProgress);
            var target = TestData.StatusChange(existing, S.Resolved);

            Run(existing, target, TestData.Approval(existing.Id, RefundDecision.Approved, DateTime.UtcNow.AddDays(-2)));

            Assert.Equal(S.AwaitingApproval, target.GetAttributeValue<OptionSetValue>(C.StatusCode).Value);
        }

        [Fact]
        public void Refund_with_open_notes_is_blocked_before_approval()
        {
            var existing = RefundCase(S.InProgress);
            var target = TestData.StatusChange(existing, S.Resolved);

            var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
                Run(existing, target, TestData.Note(existing.Id, isOpen: true)));
            Assert.StartsWith("CD004", ex.Message);
        }

        [Fact]
        public void Awaiting_approval_to_resolved_is_allowed_when_latest_approval_is_approved()
        {
            var existing = RefundCase(S.AwaitingApproval);
            var target = TestData.StatusChange(existing, S.Resolved);

            Run(existing, target,
                TestData.Approval(existing.Id, RefundDecision.Rejected, DateTime.UtcNow.AddDays(-1)),
                TestData.Approval(existing.Id, RefundDecision.Approved, DateTime.UtcNow.AddMinutes(-1)));

            Assert.Equal(S.Resolved, target.GetAttributeValue<OptionSetValue>(C.StatusCode).Value);
            Assert.True(target.Contains(C.ResolvedOn));
        }

        [Fact]
        public void Awaiting_approval_to_resolved_is_blocked_when_latest_approval_is_pending()
        {
            var existing = RefundCase(S.AwaitingApproval);
            var target = TestData.StatusChange(existing, S.Resolved);

            var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
                Run(existing, target,
                    TestData.Approval(existing.Id, RefundDecision.Approved, DateTime.UtcNow.AddDays(-3)),
                    TestData.Approval(existing.Id, RefundDecision.Pending, DateTime.UtcNow.AddMinutes(-5))));
            Assert.StartsWith("CD006", ex.Message);
        }

        [Fact]
        public void Awaiting_approval_to_closed_is_blocked()
        {
            var existing = RefundCase(S.AwaitingApproval);
            var target = TestData.StatusChange(existing, S.Closed);

            var ex = Assert.Throws<InvalidPluginExecutionException>(() => Run(existing, target));
            Assert.StartsWith("CD006", ex.Message);
        }

        [Fact]
        public void Rejection_returns_case_to_in_progress()
        {
            var existing = RefundCase(S.AwaitingApproval);
            var target = TestData.StatusChange(existing, S.InProgress);

            Run(existing, target, TestData.Approval(existing.Id, RefundDecision.Rejected, DateTime.UtcNow));

            Assert.Equal(S.InProgress, target.GetAttributeValue<OptionSetValue>(C.StatusCode).Value);
        }

        [Fact]
        public void Category_change_while_awaiting_approval_is_blocked_with_CD007()
        {
            var existing = RefundCase(S.AwaitingApproval);
            var target = new Entity(C.EntityName, existing.Id);
            target[C.Category] = new OptionSetValue(Category.Billing);

            var ex = Assert.Throws<InvalidPluginExecutionException>(() => Run(existing, target));
            Assert.StartsWith("CD007", ex.Message);
        }

        [Fact]
        public void Changing_category_to_refund_on_resolved_case_is_not_retroactive()
        {
            var existing = TestData.ExistingCase(S.Resolved);
            var target = new Entity(C.EntityName, existing.Id);
            target[C.Category] = new OptionSetValue(Category.Refund);

            Run(existing, target);

            Assert.False(target.Contains(C.StatusCode));
        }
    }
}
