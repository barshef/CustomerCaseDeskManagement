using System;
using System.Collections.Generic;
using System.Linq;
using CaseDesk.Plugins.Domain;
using FakeXrmEasy;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;
using C = CaseDesk.Plugins.Domain.Schema.Case;
using N = CaseDesk.Plugins.Domain.Schema.CaseNote;
using S = CaseDesk.Plugins.Domain.CaseStatus;

namespace CaseDesk.Plugins.Tests
{
    public class EscalateCasePluginTests
    {
        private readonly XrmFakedContext _fake = new XrmFakedContext();
        private readonly Entity _team;
        private readonly Entity _agent;

        public EscalateCasePluginTests()
        {
            _team = new Entity("team", Guid.NewGuid()) { ["name"] = "CaseDesk Supervisors" };
            _agent = new Entity("systemuser", Guid.NewGuid()) { ["fullname"] = "Ada Agent" };
        }

        private XrmFakedPluginExecutionContext Context(Entity existing, string reason, AccessRights rights = AccessRights.ReadAccess | AccessRights.WriteAccess)
        {
            _fake.AddFakeMessageExecutor<RetrievePrincipalAccessRequest>(new FakeAccessExecutor(rights));
            var ctx = _fake.GetDefaultPluginContext();
            ctx.MessageName = "cd_EscalateCase";
            ctx.Stage = 30;
            ctx.PrimaryEntityName = C.EntityName;
            ctx.PrimaryEntityId = existing.Id;
            ctx.UserId = _agent.Id;
            ctx.InitiatingUserId = _agent.Id;
            ctx.InputParameters = new ParameterCollection { { "Target", existing.ToEntityReference() } };
            if (reason != null) ctx.InputParameters["Reason"] = reason;
            ctx.OutputParameters = new ParameterCollection();
            return ctx;
        }

        private void Init(Entity existing, bool withTeam = true)
        {
            var data = new List<Entity>(TestData.AllPolicies()) { existing, _agent };
            if (withTeam) data.Add(_team);
            _fake.Initialize(data);
        }

        private Entity Reload(Guid id) =>
            _fake.CreateQuery(C.EntityName).Single(e => e.Id == id);

        private List<Entity> Notes(Guid caseId) =>
            _fake.CreateQuery(N.EntityName)
                .Where(n => ((EntityReference)n[N.Case]).Id == caseId).ToList();

        [Fact]
        public void Escalation_sets_critical_assigns_team_and_logs_note()
        {
            var existing = TestData.ExistingCase(S.InProgress, Priority.Medium);
            existing[C.OwnerId] = _agent.ToEntityReference();
            Init(existing);
            var ctx = Context(existing, "Customer threatened to cancel");

            _fake.ExecutePluginWith<EscalateCasePlugin>(ctx);

            var updated = Reload(existing.Id);
            Assert.Equal(Priority.Critical, updated.GetAttributeValue<OptionSetValue>(C.Priority).Value);
            Assert.Equal(_team.Id, updated.GetAttributeValue<EntityReference>(C.OwnerId).Id);
            Assert.Equal("team", updated.GetAttributeValue<EntityReference>(C.OwnerId).LogicalName);
            var note = Assert.Single(Notes(existing.Id));
            Assert.Equal("Escalated by Ada Agent: Customer threatened to cancel", note.GetAttributeValue<string>(N.Text));
            Assert.Equal(NoteType.Internal, note.GetAttributeValue<OptionSetValue>(N.NoteType).Value);
            Assert.False(note.GetAttributeValue<bool>(N.IsOpen));
            Assert.True((bool)ctx.OutputParameters["Escalated"]);
            Assert.Contains("CD-00042", (string)ctx.OutputParameters["Message"]);
        }

        [Fact]
        public void Missing_reason_uses_default_text()
        {
            var existing = TestData.ExistingCase(S.New, Priority.Low);
            Init(existing);

            _fake.ExecutePluginWith<EscalateCasePlugin>(Context(existing, null));

            Assert.Equal("Escalated by Ada Agent: No reason given", Notes(existing.Id).Single().GetAttributeValue<string>(N.Text));
        }

        [Fact]
        public void Already_escalated_case_is_left_unchanged()
        {
            var existing = TestData.ExistingCase(S.InProgress, Priority.Critical);
            existing[C.OwnerId] = _team.ToEntityReference();
            Init(existing);
            var ctx = Context(existing, "again");

            _fake.ExecutePluginWith<EscalateCasePlugin>(ctx);

            Assert.False((bool)ctx.OutputParameters["Escalated"]);
            Assert.Contains("already escalated", (string)ctx.OutputParameters["Message"]);
            Assert.Empty(Notes(existing.Id));
        }

        [Theory]
        [InlineData(S.Resolved)]
        [InlineData(S.Closed)]
        [InlineData(S.Cancelled)]
        public void Only_open_cases_can_be_escalated(int status)
        {
            var existing = TestData.ExistingCase(status);
            Init(existing);

            var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
                _fake.ExecutePluginWith<EscalateCasePlugin>(Context(existing, "x")));
            Assert.StartsWith("CD009", ex.Message);
        }

        [Fact]
        public void Missing_team_is_reported()
        {
            var existing = TestData.ExistingCase(S.InProgress);
            Init(existing, withTeam: false);

            var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
                _fake.ExecutePluginWith<EscalateCasePlugin>(Context(existing, "x")));
            Assert.StartsWith("CD010", ex.Message);
            Assert.Contains("CaseDesk Supervisors", ex.Message);
        }

        [Fact]
        public void Caller_without_write_access_is_rejected()
        {
            var existing = TestData.ExistingCase(S.InProgress);
            Init(existing);

            var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
                _fake.ExecutePluginWith<EscalateCasePlugin>(Context(existing, "x", AccessRights.ReadAccess)));
            Assert.StartsWith("CD008", ex.Message);
            Assert.Empty(Notes(existing.Id));
        }

        [Fact]
        public void Team_name_comes_from_environment_variable_when_set()
        {
            var existing = TestData.ExistingCase(S.InProgress);
            var other = new Entity("team", Guid.NewGuid()) { ["name"] = "Tier 2" };
            var extra = new List<Entity>();
            var def = TestData.EnvVar(EnvironmentVariables.SupervisorTeamName, "CaseDesk Supervisors", "Tier 2", extra);
            var data = new List<Entity>(TestData.AllPolicies()) { existing, _agent, _team, other, def };
            data.AddRange(extra);
            _fake.Initialize(data);

            _fake.ExecutePluginWith<EscalateCasePlugin>(Context(existing, "x"));

            Assert.Equal(other.Id, Reload(existing.Id).GetAttributeValue<EntityReference>(C.OwnerId).Id);
        }
    }
}
