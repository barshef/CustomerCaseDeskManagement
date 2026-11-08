using static CaseDesk.Seeder.Constants;

namespace CaseDesk.Seeder;

public sealed record SlaPolicySeed(int Priority, string Name, int ResolutionHours, int WarningHours);

public sealed record AccountSeed(string AccountNumber, string Name, string Email);

public sealed record ContactSeed(string Email, string FirstName, string LastName, string AccountNumber);

public sealed record CaseSeed(
    string Title,
    string Description,
    int Category,
    int Priority,
    string AccountNumber,
    string? ContactEmail,
    decimal? RefundAmount = null);

/// <summary>Demo data. All e-mail addresses use the reserved example.com domain.</summary>
public static class SeedData
{
    public static readonly IReadOnlyList<SlaPolicySeed> SlaPolicies =
    [
        new(Priority.Critical, "Critical - 4h", 4, 1),
        new(Priority.High, "High - 8h", 8, 2),
        new(Priority.Medium, "Medium - 24h", 24, 2),
        new(Priority.Low, "Low - 72h", 72, 2),
    ];

    public static readonly IReadOnlyList<AccountSeed> Accounts =
    [
        new("CD-ACC-001", "Northwind Traders (demo)", "info@northwind.example.com"),
        new("CD-ACC-002", "Fabrikam Logistics (demo)", "info@fabrikam.example.com"),
        new("CD-ACC-003", "Tailspin Toys (demo)", "info@tailspin.example.com"),
        new("CD-ACC-004", "Adventure Works Cycles (demo)", "info@adventureworks.example.com"),
        new("CD-ACC-005", "Woodgrove Retail (demo)", "info@woodgrove.example.com"),
    ];

    public static readonly IReadOnlyList<ContactSeed> Contacts =
    [
        new("alex.morgan@example.com", "Alex", "Morgan", "CD-ACC-001"),
        new("sam.patel@example.com", "Sam", "Patel", "CD-ACC-002"),
        new("jordan.lee@example.com", "Jordan", "Lee", "CD-ACC-003"),
        new("casey.nguyen@example.com", "Casey", "Nguyen", "CD-ACC-004"),
        new("riley.okafor@example.com", "Riley", "Okafor", "CD-ACC-005"),
        new("taylor.brooks@example.com", "Taylor", "Brooks", "CD-ACC-001"),
    ];

    /// <summary>
    /// Sample cases. Due date, warning-on and SLA policy are intentionally NOT set: the
    /// CasePreCreatePlugin (FR3) computes them in the same transaction.
    /// </summary>
    public static readonly IReadOnlyList<CaseSeed> Cases =
    [
        new($"{SeedCasePrefix} Invoice charged twice", "Customer was billed twice for the October invoice.",
            Category.Billing, Priority.High, "CD-ACC-001", "alex.morgan@example.com"),
        new($"{SeedCasePrefix} Parcel not delivered", "Tracking shows delivered but nothing arrived.",
            Category.Delivery, Priority.Medium, "CD-ACC-002", "sam.patel@example.com"),
        new($"{SeedCasePrefix} Product stopped working", "Device will not power on after a firmware update.",
            Category.Product, Priority.Critical, "CD-ACC-003", "jordan.lee@example.com"),
        new($"{SeedCasePrefix} Refund for damaged item", "Item arrived damaged; customer requests a refund.",
            Category.Refund, Priority.Medium, "CD-ACC-004", "casey.nguyen@example.com", RefundAmount: 49.99m),
        new($"{SeedCasePrefix} Question about warranty", "Customer asks whether accessories are covered.",
            Category.Other, Priority.Low, "CD-ACC-005", "riley.okafor@example.com"),
        new($"{SeedCasePrefix} Wrong VAT on invoice", "VAT rate on the latest invoice looks incorrect.",
            Category.Billing, Priority.Low, "CD-ACC-001", "taylor.brooks@example.com"),
        new($"{SeedCasePrefix} Late delivery, no requester", "Logged by phone; no contact on file (exercises the no-email path).",
            Category.Delivery, Priority.High, "CD-ACC-002", ContactEmail: null),
    ];
}
