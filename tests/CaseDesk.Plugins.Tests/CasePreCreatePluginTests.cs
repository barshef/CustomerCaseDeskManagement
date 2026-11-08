using System;
using System.Linq;
using CaseDesk.Plugins.Domain;
using FakeXrmEasy;
using Microsoft.Xrm.Sdk;
using Xunit;
using C = CaseDesk.Plugins.Domain.Schema.Case;

namespace CaseDesk.Plugins.Tests
{
    public class CasePreCreatePluginTests
    {
        private static XrmFakedPluginExecutionContext CreateContext(XrmFakedContext fake, Entity target)
        {
            var ctx = fake.GetDefaultPluginContext();
            ctx.MessageName = "Create";
            ctx.Stage = 20;
            ctx.PrimaryEntityName = C.EntityName;
            ctx.InputParameters = new ParameterCollection { { "Target", target } };
            return ctx;
        }

        [Fact]
        public void Sets_due_date_warning_and_policy_from_matching_priority()
        {
            var fake = new XrmFakedContext();
            var policies = TestData.AllPolicies();
            fake.Initialize(policies);
            var target = TestData.NewCase(Priority.High);
            var before = DateTime.UtcNow;

            fake.ExecutePluginWith<CasePreCreatePlugin>(CreateContext(fake, target));

            var due = target.GetAttributeValue<DateTime>(C.DueDate);
            var warning = target.GetAttributeValue<DateTime>(C.WarningOn);
            Assert.InRange(due, before.AddHours(8).AddSeconds(-1), DateTime.UtcNow.AddHours(8).AddSeconds(1));
            Assert.Equal(due.AddHours(-2), warning);
            var high = policies.Single(p => p.GetAttributeValue<OptionSetValue>(Schema.SlaPolicy.Priority).Value == Priority.High);
            Assert.Equal(high.Id, target.GetAttributeValue<EntityReference>(C.SlaPolicy).Id);
            Assert.False(target.GetAttributeValue<bool>(C.SlaWarningSent));
        }

        [Fact]
        public void Overrides_any_due_date_supplied_by_the_caller()
        {
            var fake = new XrmFakedContext();
            fake.Initialize(TestData.AllPolicies());
            var target = TestData.NewCase(Priority.Critical);
            target[C.DueDate] = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            fake.ExecutePluginWith<CasePreCreatePlugin>(CreateContext(fake, target));

            Assert.True(target.GetAttributeValue<DateTime>(C.DueDate) < DateTime.UtcNow.AddHours(5));
        }

        [Fact]
        public void Defaults_priority_to_medium_when_not_supplied()
        {
            var fake = new XrmFakedContext();
            fake.Initialize(TestData.AllPolicies());
            var target = TestData.NewCase();
            target.Attributes.Remove(C.Priority);

            fake.ExecutePluginWith<CasePreCreatePlugin>(CreateContext(fake, target));

            Assert.Equal(Priority.Medium, target.GetAttributeValue<OptionSetValue>(C.Priority).Value);
            Assert.True(target.GetAttributeValue<DateTime>(C.DueDate) > DateTime.UtcNow.AddHours(23));
        }

        [Fact]
        public void Missing_title_is_rejected_with_CD001()
        {
            var fake = new XrmFakedContext();
            fake.Initialize(TestData.AllPolicies());
            var target = TestData.NewCase(title: "   ");

            var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
                fake.ExecutePluginWith<CasePreCreatePlugin>(CreateContext(fake, target)));
            Assert.StartsWith("CD001", ex.Message);
        }

        [Fact]
        public void Missing_customer_is_rejected_with_CD002()
        {
            var fake = new XrmFakedContext();
            fake.Initialize(TestData.AllPolicies());
            var target = TestData.NewCase(withAccount: false);

            var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
                fake.ExecutePluginWith<CasePreCreatePlugin>(CreateContext(fake, target)));
            Assert.StartsWith("CD002", ex.Message);
        }

        [Fact]
        public void Missing_sla_policy_is_rejected_with_CD003()
        {
            var fake = new XrmFakedContext();
            fake.Initialize(new[] { TestData.SlaPolicy(Priority.Low, 72, 2) });
            var target = TestData.NewCase(Priority.Critical);

            var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
                fake.ExecutePluginWith<CasePreCreatePlugin>(CreateContext(fake, target)));
            Assert.StartsWith("CD003", ex.Message);
            Assert.Contains("Critical", ex.Message);
        }
    }
}
