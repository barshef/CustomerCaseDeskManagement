namespace CaseDesk.Plugins.Domain
{
    /// <summary>statecode values for cd_case.</summary>
    public static class CaseState
    {
        public const int Active = 0;
        public const int Inactive = 1;
    }

    /// <summary>statuscode (status reason) values for cd_case.</summary>
    public static class CaseStatus
    {
        public const int New = 1;
        public const int Assigned = 417200001;
        public const int InProgress = 417200002;
        public const int AwaitingCustomer = 417200003;
        public const int AwaitingApproval = 417200004;
        public const int Resolved = 417200005;
        public const int Closed = 2;
        public const int Cancelled = 417200006;

        public static int StateOf(int status)
        {
            return status == Closed || status == Cancelled ? CaseState.Inactive : CaseState.Active;
        }

        /// <summary>Open = still being worked (counts toward SLA, can be escalated).</summary>
        public static bool IsOpen(int status)
        {
            return StateOf(status) == CaseState.Active && status != Resolved;
        }

        public static string Label(int status)
        {
            switch (status)
            {
                case New: return "New";
                case Assigned: return "Assigned";
                case InProgress: return "In Progress";
                case AwaitingCustomer: return "Awaiting Customer";
                case AwaitingApproval: return "Awaiting Approval";
                case Resolved: return "Resolved";
                case Closed: return "Closed";
                case Cancelled: return "Cancelled";
                default: return status.ToString();
            }
        }
    }
}
