namespace CaseDesk.Seeder;

/// <summary>
/// Choice values, names and seed markers used by the seeder. Values mirror
/// specs/001-casedesk-case-management/data-model.md (publisher option value prefix 41720).
/// This is the single place the seeder hard-codes Dataverse values.
/// </summary>
public static class Constants
{
    /// <summary>Global choice <c>cd_casecategory</c>.</summary>
    public static class Category
    {
        public const int Billing = 417200000;
        public const int Delivery = 417200001;
        public const int Product = 417200002;
        public const int Refund = 417200003;
        public const int Other = 417200004;

        public static string Label(int value) => value switch
        {
            Billing => "Billing",
            Delivery => "Delivery",
            Product => "Product",
            Refund => "Refund",
            Other => "Other",
            _ => value.ToString(),
        };
    }

    /// <summary>Global choice <c>cd_casepriority</c>.</summary>
    public static class Priority
    {
        public const int Low = 417200000;
        public const int Medium = 417200001;
        public const int High = 417200002;
        public const int Critical = 417200003;

        public static string Label(int value) => value switch
        {
            Low => "Low",
            Medium => "Medium",
            High => "High",
            Critical => "Critical",
            _ => value.ToString(),
        };
    }

    /// <summary>Case <c>statecode</c> / <c>statuscode</c> values.</summary>
    public static class CaseStatus
    {
        public const int StateActive = 0;
        public const int StateInactive = 1;

        public const int New = 1;
        public const int Assigned = 417200001;
        public const int InProgress = 417200002;
        public const int AwaitingCustomer = 417200003;
        public const int AwaitingApproval = 417200004;
        public const int Resolved = 417200005;
        public const int Closed = 2;
        public const int Cancelled = 417200006;
    }

    /// <summary>Table logical names.</summary>
    public static class Tables
    {
        public const string Case = "cd_case";
        public const string SlaPolicy = "cd_slapolicy";
        public const string Account = "account";
        public const string Contact = "contact";
        public const string BusinessUnit = "businessunit";
        public const string Team = "team";
        public const string Role = "role";
        public const string TeamRoles = "teamroles";
    }

    /// <summary>Alternate key on cd_slapolicy (one policy per priority).</summary>
    public const string SlaPolicyPriorityKey = "cd_slapolicy_priority_key";

    public const string BusinessUnitA = "CaseDesk BU A";
    public const string BusinessUnitB = "CaseDesk BU B";
    public const string SupervisorTeamName = "CaseDesk Supervisors";
    public const string SupervisorRoleName = "CaseDesk Supervisor";
    public const string TeamRolesRelationship = "teamroles_association";

    /// <summary>team.teamtype: 0 = Owner team.</summary>
    public const int TeamTypeOwner = 0;

    /// <summary>Title prefix that marks a case as seeded demo data.</summary>
    public const string SeedCasePrefix = "[Seed]";

    /// <summary>Environment variable holding the Dataverse connection string.</summary>
    public const string ConnectionEnvVar = "CASEDESK_DATAVERSE_CONNECTION";
}
