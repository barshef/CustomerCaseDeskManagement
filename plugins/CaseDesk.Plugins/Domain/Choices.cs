namespace CaseDesk.Plugins.Domain
{
    /// <summary>Option values for the global choices (see specs/.../data-model.md).</summary>
    public static class Category
    {
        public const int Billing = 417200000;
        public const int Delivery = 417200001;
        public const int Product = 417200002;
        public const int Refund = 417200003;
        public const int Other = 417200004;
    }

    public static class Priority
    {
        public const int Low = 417200000;
        public const int Medium = 417200001;
        public const int High = 417200002;
        public const int Critical = 417200003;

        public static string Label(int value)
        {
            switch (value)
            {
                case Low: return "Low";
                case Medium: return "Medium";
                case High: return "High";
                case Critical: return "Critical";
                default: return value.ToString();
            }
        }
    }

    public static class NoteType
    {
        public const int Internal = 417200000;
        public const int CustomerFacing = 417200001;
    }

    public static class RefundDecision
    {
        public const int Pending = 417200000;
        public const int Approved = 417200001;
        public const int Rejected = 417200002;
    }

    /// <summary>Logical names used by the plugins.</summary>
    public static class Schema
    {
        public static class Case
        {
            public const string EntityName = "cd_case";
            public const string Id = "cd_caseid";
            public const string CaseNumber = "cd_casenumber";
            public const string Title = "cd_name";
            public const string Category = "cd_category";
            public const string Priority = "cd_priority";
            public const string Account = "cd_accountid";
            public const string Contact = "cd_contactid";
            public const string SlaPolicy = "cd_slapolicyid";
            public const string DueDate = "cd_duedate";
            public const string WarningOn = "cd_warningon";
            public const string SlaWarningSent = "cd_slawarningsent";
            public const string ResolvedOn = "cd_resolvedon";
            public const string ClosedOn = "cd_closedon";
            public const string ResolutionSummary = "cd_resolutionsummary";
            public const string RefundAmount = "cd_refundamount";
            public const string CustomerReplyPending = "cd_customerreplypending";
            public const string StateCode = "statecode";
            public const string StatusCode = "statuscode";
            public const string CreatedOn = "createdon";
            public const string OwnerId = "ownerid";
        }

        public static class CaseNote
        {
            public const string EntityName = "cd_casenote";
            public const string Title = "cd_name";
            public const string Text = "cd_notetext";
            public const string NoteType = "cd_notetype";
            public const string IsOpen = "cd_isopen";
            public const string Case = "cd_caseid";
        }

        public static class SlaPolicy
        {
            public const string EntityName = "cd_slapolicy";
            public const string Id = "cd_slapolicyid";
            public const string Priority = "cd_priority";
            public const string ResolutionHours = "cd_resolutionhours";
            public const string WarningHours = "cd_warninghours";
        }

        public static class RefundApproval
        {
            public const string EntityName = "cd_refundapproval";
            public const string Case = "cd_caseid";
            public const string Decision = "cd_decision";
            public const string CreatedOn = "createdon";
        }
    }
}
