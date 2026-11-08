using System.Globalization;
using CaseDesk.Functions.Dataverse;

namespace CaseDesk.Functions.CaseClosed;

public enum MapStatus
{
    /// <summary>A Case Update into Closed — <see cref="MapResult.Event"/> is set.</summary>
    Mapped,
    /// <summary>A valid context that is not a Closed Case Update (wrong entity/message/status). Not an error.</summary>
    Ignored,
    /// <summary>A Closed Case Update whose data cannot be mapped (missing/unknown values).</summary>
    Invalid,
}

public sealed record MapResult(MapStatus Status, CaseClosedEvent? Event, string? Reason)
{
    public static MapResult Ignore(string reason) => new(MapStatus.Ignored, null, reason);
    public static MapResult Fail(string reason) => new(MapStatus.Invalid, null, reason);
}

/// <summary>Maps a Dataverse <see cref="RemoteExecutionContext"/> to CaseClosed v1. Pure; no I/O.</summary>
public static class CaseClosedMapper
{
    public const string EntityName = "cd_case";
    public const string PostImageName = "PostImage";
    public const int StateInactive = 1;
    public const int StatusClosed = 2;

    // Choice values — data-model.md (publisher prefix 41720).
    private static readonly IReadOnlyDictionary<int, string> Categories = new Dictionary<int, string>
    {
        [417200000] = "Billing",
        [417200001] = "Delivery",
        [417200002] = "Product",
        [417200003] = "Refund",
        [417200004] = "Other",
    };

    private static readonly IReadOnlyDictionary<int, string> Priorities = new Dictionary<int, string>
    {
        [417200000] = "Low",
        [417200001] = "Medium",
        [417200002] = "High",
        [417200003] = "Critical",
    };

    /// <summary>ISO-8601 round-trip, always UTC ("2026-11-20T10:15:00.0000000Z").</summary>
    public static string FormatUtc(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Stable idempotency key: "{caseId}:{closedOn:O}". Same value on every retry of the same close.</summary>
    public static string BuildMessageId(Guid caseId, DateTime closedOnUtc) =>
        $"{caseId:D}:{FormatUtc(closedOnUtc)}";

    /// <summary>SLA met when the case was resolved (or closed, if never resolved) on or before the due date.</summary>
    public static bool IsSlaMet(DateTime? dueDate, DateTime? resolvedOn, DateTime closedOn) =>
        dueDate is null || (resolvedOn ?? closedOn) <= dueDate.Value;

    public static MapResult Map(RemoteExecutionContext context, string transport)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (transport is not ("ServiceBus" or "Webhook"))
            throw new ArgumentOutOfRangeException(nameof(transport), transport, "Expected 'ServiceBus' or 'Webhook'.");

        if (!string.Equals(context.PrimaryEntityName, EntityName, StringComparison.OrdinalIgnoreCase))
            return MapResult.Ignore($"PrimaryEntityName is '{context.PrimaryEntityName}', expected '{EntityName}'.");
        if (!string.Equals(context.MessageName, "Update", StringComparison.OrdinalIgnoreCase))
            return MapResult.Ignore($"MessageName is '{context.MessageName}', expected 'Update'.");

        var post = context.GetPostImage(PostImageName);
        if (post is null)
            return MapResult.Ignore($"No post-image named '{PostImageName}' in the context.");

        var state = post.GetOptionSetValue("statecode");
        var status = post.GetOptionSetValue("statuscode");
        if (state != StateInactive || status != StatusClosed)
            return MapResult.Ignore($"Case is not Closed (statecode={state?.ToString() ?? "null"}, statuscode={status?.ToString() ?? "null"}).");

        try
        {
            var caseId = context.PrimaryEntityId != Guid.Empty ? context.PrimaryEntityId : post.Id;
            if (caseId == Guid.Empty) return MapResult.Fail("Case id is missing.");

            var caseNumber = post.GetString("cd_casenumber");
            if (string.IsNullOrWhiteSpace(caseNumber)) return MapResult.Fail("cd_casenumber is missing from the post-image.");

            var title = post.GetString("cd_name");
            if (title is null) return MapResult.Fail("cd_name is missing from the post-image.");

            var categoryValue = post.GetOptionSetValue("cd_category");
            if (categoryValue is null || !Categories.TryGetValue(categoryValue.Value, out var category))
                return MapResult.Fail($"cd_category value '{categoryValue}' is missing or unknown.");

            var priorityValue = post.GetOptionSetValue("cd_priority");
            if (priorityValue is null || !Priorities.TryGetValue(priorityValue.Value, out var priority))
                return MapResult.Fail($"cd_priority value '{priorityValue}' is missing or unknown.");

            var closedOn = post.GetDateTime("cd_closedon");
            if (closedOn is null) return MapResult.Fail("cd_closedon is missing from the post-image (needed for the message id).");

            var createdOn = post.GetDateTime("createdon");
            if (createdOn is null) return MapResult.Fail("createdon is missing from the post-image.");

            var dueDate = post.GetDateTime("cd_duedate");
            var resolvedOn = post.GetDateTime("cd_resolvedon");
            var account = post.GetEntityReference("cd_accountid");
            var contact = post.GetEntityReference("cd_contactid");

            var evt = new CaseClosedEvent
            {
                MessageId = BuildMessageId(caseId, closedOn.Value),
                OccurredAt = FormatUtc(closedOn.Value),
                Transport = transport,
                CorrelationId = context.CorrelationId == Guid.Empty ? null : context.CorrelationId.ToString("D"),
                Organization = context.OrganizationName,
                Case = new CaseClosedCase
                {
                    Id = caseId.ToString("D"),
                    CaseNumber = caseNumber,
                    Title = title,
                    Category = category,
                    Priority = priority,
                    Customer = account is null ? null : new CaseClosedCustomer { AccountId = account.Id.ToString("D"), Name = account.Name },
                    RequesterContactId = contact?.Id.ToString("D"),
                    CreatedOn = FormatUtc(createdOn.Value),
                    DueDate = dueDate is null ? null : FormatUtc(dueDate.Value),
                    ResolvedOn = resolvedOn is null ? null : FormatUtc(resolvedOn.Value),
                    ClosedOn = FormatUtc(closedOn.Value),
                    SlaMet = IsSlaMet(dueDate, resolvedOn, closedOn.Value),
                    RefundAmount = post.GetMoney("cd_refundamount"),
                },
            };
            return new MapResult(MapStatus.Mapped, evt, null);
        }
        catch (FormatException ex)
        {
            return MapResult.Fail(ex.Message);
        }
    }
}
