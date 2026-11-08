using System.Linq;
using CaseDesk.Plugins.Domain;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using C = CaseDesk.Plugins.Domain.Schema.Case;
using N = CaseDesk.Plugins.Domain.Schema.CaseNote;

namespace CaseDesk.Plugins
{
    /// <summary>
    /// Main operation of Custom API cd_EscalateCase (bound to cd_case), FR-009.
    /// Atomic: runs inside the Custom API transaction, so priority, owner and note are all
    /// committed or none are. Idempotent: an already escalated case is returned unchanged.
    /// </summary>
    public sealed class EscalateCasePlugin : PluginBase
    {
        public const string DefaultTeamName = "CaseDesk Supervisors";
        private const int MaxReasonLength = 500;

        protected override void ExecuteCaseDesk(LocalPluginContext context)
        {
            var ctx = context.ExecutionContext;
            var caseRef = ctx.InputParameters["Target"] as EntityReference;
            if (caseRef == null || caseRef.LogicalName != C.EntityName)
                throw new InvalidPluginExecutionException("cd_EscalateCase must be called on a cd_case record.");

            var system = context.SystemService;

            // Agents have no Assign privilege (FR-013): check edit access as the caller, then act as SYSTEM.
            if (!CanWrite(system, ctx.InitiatingUserId, caseRef))
                throw CaseDeskErrors.NoWriteAccess();

            var current = system.Retrieve(C.EntityName, caseRef.Id,
                new ColumnSet(C.CaseNumber, C.Priority, C.StatusCode, C.StateCode, C.OwnerId));
            var status = current.GetAttributeValue<OptionSetValue>(C.StatusCode)?.Value ?? CaseStatus.New;
            if (!CaseStatus.IsOpen(status))
                throw CaseDeskErrors.NotOpenForEscalation();

            var teamName = EnvironmentVariables.Get(system, EnvironmentVariables.SupervisorTeamName, DefaultTeamName);
            var team = FindTeam(system, teamName) ?? throw CaseDeskErrors.SupervisorTeamMissing(teamName);
            var caseNumber = current.GetAttributeValue<string>(C.CaseNumber);

            var owner = current.GetAttributeValue<EntityReference>(C.OwnerId);
            var isCritical = current.GetAttributeValue<OptionSetValue>(C.Priority)?.Value == Priority.Critical;
            if (isCritical && owner != null && owner.Id == team.Id)
            {
                Respond(ctx, false, $"Case {caseNumber} is already escalated.");
                return;
            }

            var reason = Reason(ctx);
            var caller = system.Retrieve("systemuser", ctx.InitiatingUserId, new ColumnSet("fullname"))
                .GetAttributeValue<string>("fullname");

            // Priority change runs CasePreUpdatePlugin, which recalculates Due Date / Warning On.
            system.Update(new Entity(C.EntityName, caseRef.Id) { [C.Priority] = new OptionSetValue(Priority.Critical) });
            system.Update(new Entity(C.EntityName, caseRef.Id) { [C.OwnerId] = team.ToEntityReference() });
            system.Create(new Entity(N.EntityName)
            {
                [N.Case] = caseRef,
                [N.Title] = $"Escalated by {caller}",
                [N.Text] = $"Escalated by {caller}: {reason}",
                [N.NoteType] = new OptionSetValue(NoteType.Internal),
                [N.IsOpen] = false,
            });

            context.Trace("Escalated {0} to team {1} for {2}", caseNumber, teamName, caller);
            Respond(ctx, true, $"Case {caseNumber} escalated to {teamName}.");
        }

        private static bool CanWrite(IOrganizationService service, System.Guid userId, EntityReference record)
        {
            var response = (RetrievePrincipalAccessResponse)service.Execute(new RetrievePrincipalAccessRequest
            {
                Principal = new EntityReference("systemuser", userId),
                Target = record,
            });
            return (response.AccessRights & AccessRights.WriteAccess) == AccessRights.WriteAccess;
        }

        private static Entity FindTeam(IOrganizationService service, string name)
        {
            var query = new QueryExpression("team")
            {
                ColumnSet = new ColumnSet("name"),
                TopCount = 1,
                Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.Equal, name) } },
            };
            return service.RetrieveMultiple(query).Entities.FirstOrDefault();
        }

        private static string Reason(IPluginExecutionContext ctx)
        {
            var reason = ctx.InputParameters.Contains("Reason") ? (ctx.InputParameters["Reason"] as string)?.Trim() : null;
            if (string.IsNullOrEmpty(reason)) return "No reason given";
            return reason.Length > MaxReasonLength ? reason.Substring(0, MaxReasonLength) : reason;
        }

        private static void Respond(IPluginExecutionContext ctx, bool escalated, string message)
        {
            ctx.OutputParameters["Escalated"] = escalated;
            ctx.OutputParameters["Message"] = message;
        }
    }
}
