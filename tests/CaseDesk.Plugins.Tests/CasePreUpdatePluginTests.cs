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
    public class CasePreUpdatePluginTests
    {
        private static Entity Run(XrmFakedContext fake, Entity existing, Entity target, params Entity[] related)
        {
            var data = new List<Entity>(TestData.AllPolicies()) { existing };
            data.AddRange(related);
            fake.Initialize(data);
            fake.ExecutePluginWith<CasePreUpdatePlugin>(TestData.UpdateContext(fake, target, existing));
            return target;
        }

        [Fact]
        public void Resolve_with_an_open_note_is_blocked_with_CD004()
        {
            var fake = new XrmFakedContext();
            var existing = TestData.ExistingCase(S.InProgress);
            existing[C.ResolutionSummary] = "Fixed";
            var target = TestData.StatusChange(existing, S.Resolved);

            var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
                Run(fake, existing, target, TestData.Note(existing.Id, isOpen: true), TestData.Note(existing.Id, isOpen: false)));

            Assert.StartsWith("CD004", ex.Message);
            Assert.Contains("1 open note", ex.Message);
        }

        [Fact]
        public void Resolve_with_closed_notes_succeeds_and_sets_resolved_on()
        {
            var fake = new XrmFakedContext();
            var existing = TestData.ExistingCase(S.InProgress);
            var target = TestData.StatusChange(existing, S.Resolved);
            target[C.ResolutionSummary] = "Credit note issued";

            Run(fake, existing, target, TestData.Note(existing.Id, isOpen: false));

            Assert.Equal(S.Resolved, target.GetAttributeValue<OptionSetValue>(C.StatusCode).Value);
            Assert.Equal(CaseState.Active, target.GetAttributeValue<OptionSetValue>(C.StateCode).Value);
            Assert.True(target.Contains(C.ResolvedOn));
        }

        [Fact]
        public void Open_notes_on_another_case_do_not_block()
        {
            var fake = new XrmFakedContext();
            var existing = TestData.ExistingCase(S.InProgress);
            existing[C.ResolutionSummary] = "Fixed";
            var target = TestData.StatusChange(existing, S.Resolved);

            Run(fake, existing, target, TestData.Note(Guid.NewGuid(), isOpen: true));

            Assert.Equal(S.Resolved, target.GetAttributeValue<OptionSetValue>(C.StatusCode).Value);
        }

        [Fact]
        public void Resolve_without_summary_is_blocked_with_CD005()
        {
            var fake = new XrmFakedContext();
            var existing = TestData.ExistingCase(S.InProgress);
            var target = TestData.StatusChange(existing, S.Resolved);

            var ex = Assert.Throws<InvalidPluginExecutionException>(() => Run(fake, existing, target));
            Assert.StartsWith("CD005", ex.Message);
        }

        [Fact]
        public void Illegal_transition_is_blocked_with_CD006()
        {
            var fake = new XrmFakedContext();
            var existing = TestData.ExistingCase(S.InProgress);
            var target = TestData.StatusChange(existing, S.Closed);

            var ex = Assert.Throws<InvalidPluginExecutionException>(() => Run(fake, existing, target));
            Assert.StartsWith("CD006", ex.Message);
            Assert.Contains("In Progress", ex.Message);
            Assert.Contains("Closed", ex.Message);
        }

        [Fact]
        public void Cancel_from_resolved_is_blocked()
        {
            var fake = new XrmFakedContext();
            var existing = TestData.ExistingCase(S.Resolved);
            var target = TestData.StatusChange(existing, S.Cancelled);

            var ex = Assert.Throws<InvalidPluginExecutionException>(() => Run(fake, existing, target));
            Assert.StartsWith("CD006", ex.Message);
        }

        [Fact]
        public void Cancel_sets_closed_on_and_inactive_state()
        {
            var fake = new XrmFakedContext();
            var existing = TestData.ExistingCase(S.AwaitingCustomer);
            var target = new Entity(C.EntityName, existing.Id);
            target[C.StatusCode] = new OptionSetValue(S.Cancelled); // API caller sends status only

            Run(fake, existing, target);

            Assert.True(target.Contains(C.ClosedOn));
            Assert.Equal(CaseState.Inactive, target.GetAttributeValue<OptionSetValue>(C.StateCode).Value);
        }

        [Fact]
        public void Close_from_resolved_sets_closed_on()
        {
            var fake = new XrmFakedContext();
            var existing = TestData.ExistingCase(S.Resolved);
            existing[C.ResolvedOn] = DateTime.UtcNow.AddDays(-6);
            var target = TestData.StatusChange(existing, S.Closed);

            Run(fake, existing, target);

            Assert.True(target.Contains(C.ClosedOn));
        }

        [Fact]
        public void Reopen_clears_resolved_on_and_reply_flag()
        {
            var fake = new XrmFakedContext();
            var existing = TestData.ExistingCase(S.Resolved);
            existing[C.ResolvedOn] = DateTime.UtcNow.AddDays(-1);
            existing[C.CustomerReplyPending] = true;
            var target = TestData.StatusChange(existing, S.InProgress);

            Run(fake, existing, target);

            Assert.True(target.Contains(C.ResolvedOn));
            Assert.Null(target[C.ResolvedOn]);
            Assert.False(target.GetAttributeValue<bool>(C.CustomerReplyPending));
        }

        [Fact]
        public void Same_status_update_is_a_no_op()
        {
            var fake = new XrmFakedContext();
            var existing = TestData.ExistingCase(S.Closed);
            var target = TestData.StatusChange(existing, S.Closed);

            Run(fake, existing, target);

            Assert.False(target.Contains(C.ClosedOn));
        }

        [Fact]
        public void Priority_change_recalculates_dates_from_original_created_on()
        {
            var fake = new XrmFakedContext();
            var created = new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);
            var existing = TestData.ExistingCase(S.InProgress, Priority.Low, createdOn: created);
            existing[C.SlaWarningSent] = true;
            var target = new Entity(C.EntityName, existing.Id);
            target[C.Priority] = new OptionSetValue(Priority.Critical);

            Run(fake, existing, target);

            Assert.Equal(created.AddHours(4), target.GetAttributeValue<DateTime>(C.DueDate));
            Assert.Equal(created.AddHours(3), target.GetAttributeValue<DateTime>(C.WarningOn));
            Assert.False(target.GetAttributeValue<bool>(C.SlaWarningSent));
            Assert.True(target.Contains(C.SlaPolicy));
        }

        [Fact]
        public void Priority_change_on_resolved_case_keeps_dates()
        {
            var fake = new XrmFakedContext();
            var existing = TestData.ExistingCase(S.Resolved, Priority.Low);
            var target = new Entity(C.EntityName, existing.Id);
            target[C.Priority] = new OptionSetValue(Priority.Critical);

            Run(fake, existing, target);

            Assert.False(target.Contains(C.DueDate));
        }

        [Fact]
        public void Unrelated_update_does_nothing()
        {
            var fake = new XrmFakedContext();
            var existing = TestData.ExistingCase(S.InProgress);
            var target = new Entity(C.EntityName, existing.Id);
            target[C.Category] = new OptionSetValue(Category.Product);

            Run(fake, existing, target);

            Assert.Single(target.Attributes);
        }
    }
}
