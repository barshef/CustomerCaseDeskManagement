using Microsoft.Xrm.Sdk;

namespace CaseDesk.Plugins.Domain
{
    /// <summary>
    /// User-facing errors. Each message starts with a stable code (contracts/plugin-steps.md)
    /// so tests and support can match on it.
    /// </summary>
    public static class CaseDeskErrors
    {
        public static InvalidPluginExecutionException MissingTitle()
            => Create("CD001", "A case needs a title.");

        public static InvalidPluginExecutionException MissingCustomer()
            => Create("CD002", "A case needs a customer (account).");

        public static InvalidPluginExecutionException MissingSlaPolicy(int priority)
            => Create("CD003", $"No SLA policy exists for priority {Priority.Label(priority)}. Ask a supervisor to create one.");

        public static InvalidPluginExecutionException OpenNotes(int count)
            => Create("CD004", $"This case has {count} open note(s). Close them before resolving.");

        public static InvalidPluginExecutionException MissingResolutionSummary()
            => Create("CD005", "Enter a resolution summary before resolving.");

        public static InvalidPluginExecutionException IllegalTransition(int from, int to)
            => Create("CD006", $"A case cannot move from {CaseStatus.Label(from)} to {CaseStatus.Label(to)}.");

        public static InvalidPluginExecutionException CategoryLockedDuringApproval()
            => Create("CD007", "The category cannot change while a refund approval is pending.");

        public static InvalidPluginExecutionException NoWriteAccess()
            => Create("CD008", "You need edit access to this case to escalate it.");

        public static InvalidPluginExecutionException NotOpenForEscalation()
            => Create("CD009", "Only open cases can be escalated.");

        public static InvalidPluginExecutionException SupervisorTeamMissing(string teamName)
            => Create("CD010", $"The supervisor team '{teamName}' was not found. Ask an administrator to create it.");

        public static InvalidPluginExecutionException Create(string code, string message)
            => new InvalidPluginExecutionException(OperationStatus.Failed, $"{code}: {message}");
    }
}
