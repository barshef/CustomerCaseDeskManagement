using System;
using CaseDesk.Plugins.Domain;
using Microsoft.Xrm.Sdk;
using C = CaseDesk.Plugins.Domain.Schema.Case;

namespace CaseDesk.Plugins
{
    /// <summary>
    /// Step S1 — Create of cd_case, PreOperation, synchronous.
    /// Enforces Title and Customer for every caller (FR-002) and sets SLA policy, Due Date and
    /// Warning On inside the create transaction (FR-003), so a case is never stored without a due date.
    /// </summary>
    public sealed class CasePreCreatePlugin : PluginBase
    {
        protected override void ExecuteCaseDesk(LocalPluginContext context)
        {
            var target = context.Target;
            if (target == null || target.LogicalName != C.EntityName) return;

            if (string.IsNullOrWhiteSpace(target.GetAttributeValue<string>(C.Title)))
                throw CaseDeskErrors.MissingTitle();
            if (target.GetAttributeValue<EntityReference>(C.Account) == null)
                throw CaseDeskErrors.MissingCustomer();

            var priority = target.GetAttributeValue<OptionSetValue>(C.Priority)?.Value ?? Priority.Medium;
            target[C.Priority] = new OptionSetValue(priority);

            // createdon is not yet in the target during PreOperation; "now" is what the platform will stamp.
            var createdOn = DateTime.UtcNow;
            SlaPolicies.Apply(context.SystemService, target, priority, createdOn);

            context.Trace("Due date {0:O} (priority {1})", target[C.DueDate], Priority.Label(priority));
        }
    }
}
