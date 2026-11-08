using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using P = CaseDesk.Plugins.Domain.Schema.SlaPolicy;

namespace CaseDesk.Plugins.Domain
{
    /// <summary>Looks up the SLA policy for a priority and applies it to a case target.</summary>
    public static class SlaPolicies
    {
        public static Entity ForPriority(IOrganizationService service, int priority)
        {
            var query = new QueryExpression(P.EntityName)
            {
                ColumnSet = new ColumnSet(P.ResolutionHours, P.WarningHours),
                TopCount = 1,
                NoLock = true,
                Criteria = { Conditions = { new ConditionExpression(P.Priority, ConditionOperator.Equal, priority) } },
            };
            return service.RetrieveMultiple(query).Entities.FirstOrDefault();
        }

        /// <summary>Sets SLA policy, Due Date, Warning On and resets the warning flag on the target.</summary>
        public static void Apply(IOrganizationService service, Entity target, int priority, DateTime createdOnUtc)
        {
            var policy = ForPriority(service, priority) ?? throw CaseDeskErrors.MissingSlaPolicy(priority);
            var dates = SlaCalculator.Calculate(
                createdOnUtc,
                policy.GetAttributeValue<int>(P.ResolutionHours),
                policy.GetAttributeValue<int>(P.WarningHours));

            target[Schema.Case.SlaPolicy] = policy.ToEntityReference();
            target[Schema.Case.DueDate] = dates.DueDate;
            target[Schema.Case.WarningOn] = dates.WarningOn;
            target[Schema.Case.SlaWarningSent] = false;
        }
    }
}
