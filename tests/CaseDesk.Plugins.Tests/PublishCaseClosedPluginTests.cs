using System;
using System.Collections.Generic;
using CaseDesk.Plugins.Domain;
using FakeItEasy;
using FakeXrmEasy;
using Microsoft.Xrm.Sdk;
using Xunit;
using C = CaseDesk.Plugins.Domain.Schema.Case;
using S = CaseDesk.Plugins.Domain.CaseStatus;

namespace CaseDesk.Plugins.Tests
{
    public class PublishCaseClosedPluginTests
    {
        private static readonly DateTime ClosedOn = new DateTime(2026, 11, 20, 10, 15, 0, DateTimeKind.Utc);

        private readonly XrmFakedContext _fake = new XrmFakedContext();
        private readonly Entity _sbEndpoint = Endpoint("CaseDesk CaseClosed ServiceBus");
        private readonly Entity _webhookEndpoint = Endpoint("CaseDesk CaseClosed Webhook");

        private static Entity Endpoint(string name) =>
            new Entity("serviceendpoint", Guid.NewGuid()) { ["name"] = name };

        private XrmFakedPluginExecutionContext ClosingContext(Entity pre, int newStatus = S.Closed)
        {
            var post = new Entity(C.EntityName, pre.Id);
            foreach (var a in pre.Attributes) post[a.Key] = a.Value;
            post[C.StatusCode] = new OptionSetValue(newStatus);
            post[C.StateCode] = new OptionSetValue(S.StateOf(newStatus));
            post[C.ClosedOn] = ClosedOn;
            var target = TestData.StatusChange(pre, newStatus);
            var ctx = TestData.UpdateContext(_fake, target, pre, post, stage: 40);
            ctx.Mode = 1; // async
            return ctx;
        }

        private void Init(params Entity[] extra)
        {
            var data = new List<Entity> { _sbEndpoint, _webhookEndpoint };
            data.AddRange(extra);
            _fake.Initialize(data);
        }

        [Fact]
        public void Defaults_to_service_bus_endpoint()
        {
            Init();
            var notifier = _fake.GetFakedServiceEndpointNotificationService();
            var ctx = ClosingContext(TestData.ExistingCase(S.Resolved));

            _fake.ExecutePluginWith<PublishCaseClosedPlugin>(ctx);

            A.CallTo(() => notifier.Execute(
                    A<EntityReference>.That.Matches(r => r.Id == _sbEndpoint.Id),
                    A<IExecutionContext>._))
                .MustHaveHappenedOnceExactly();
        }

        [Fact]
        public void Uses_webhook_endpoint_when_transport_is_webhook()
        {
            var values = new List<Entity>();
            var def = TestData.EnvVar(EnvironmentVariables.CaseClosedTransport, "ServiceBus", "Webhook", values);
            var all = new List<Entity> { def };
            all.AddRange(values);
            Init(all.ToArray());
            var notifier = _fake.GetFakedServiceEndpointNotificationService();

            _fake.ExecutePluginWith<PublishCaseClosedPlugin>(ClosingContext(TestData.ExistingCase(S.Resolved)));

            A.CallTo(() => notifier.Execute(
                    A<EntityReference>.That.Matches(r => r.Id == _webhookEndpoint.Id),
                    A<IExecutionContext>._))
                .MustHaveHappenedOnceExactly();
        }

        [Fact]
        public void Transport_none_publishes_nothing()
        {
            Init(TestData.EnvVar(EnvironmentVariables.CaseClosedTransport, "None"));
            var notifier = _fake.GetFakedServiceEndpointNotificationService();

            _fake.ExecutePluginWith<PublishCaseClosedPlugin>(ClosingContext(TestData.ExistingCase(S.Resolved)));

            A.CallTo(() => notifier.Execute(A<EntityReference>._, A<IExecutionContext>._)).MustNotHaveHappened();
        }

        [Fact]
        public void Cancelled_case_is_not_published()
        {
            Init();
            var notifier = _fake.GetFakedServiceEndpointNotificationService();

            _fake.ExecutePluginWith<PublishCaseClosedPlugin>(ClosingContext(TestData.ExistingCase(S.InProgress), S.Cancelled));

            A.CallTo(() => notifier.Execute(A<EntityReference>._, A<IExecutionContext>._)).MustNotHaveHappened();
        }

        [Fact]
        public void Already_closed_case_is_not_published_again()
        {
            Init();
            var notifier = _fake.GetFakedServiceEndpointNotificationService();

            _fake.ExecutePluginWith<PublishCaseClosedPlugin>(ClosingContext(TestData.ExistingCase(S.Closed)));

            A.CallTo(() => notifier.Execute(A<EntityReference>._, A<IExecutionContext>._)).MustNotHaveHappened();
        }

        [Fact]
        public void Missing_endpoint_fails_the_job_with_a_clear_message()
        {
            _fake.Initialize(new List<Entity>());

            var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
                _fake.ExecutePluginWith<PublishCaseClosedPlugin>(ClosingContext(TestData.ExistingCase(S.Resolved))));
            Assert.Contains("CaseDesk CaseClosed ServiceBus", ex.Message);
        }

        [Fact]
        public void Message_id_and_transport_are_shared_with_the_listener()
        {
            Init();
            var pre = TestData.ExistingCase(S.Resolved);
            var ctx = ClosingContext(pre);

            _fake.ExecutePluginWith<PublishCaseClosedPlugin>(ctx);

            Assert.Equal(pre.Id + ":" + ClosedOn.ToString("O"), ctx.SharedVariables["CaseDesk.MessageId"]);
            Assert.Equal("ServiceBus", ctx.SharedVariables["CaseDesk.Transport"]);
        }

        [Fact]
        public void Message_id_is_stable_across_retries()
        {
            var id = Guid.NewGuid();
            Assert.Equal(PublishCaseClosedPlugin.MessageId(id, ClosedOn), PublishCaseClosedPlugin.MessageId(id, ClosedOn));
        }
    }
}
