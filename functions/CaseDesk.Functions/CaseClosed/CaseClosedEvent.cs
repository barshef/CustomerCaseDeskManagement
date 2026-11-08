using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaseDesk.Functions.CaseClosed;

/// <summary>CaseClosed v1 — see specs/001-casedesk-case-management/contracts/caseclosed-event.schema.json.</summary>
public sealed record CaseClosedEvent
{
    [JsonPropertyName("messageId")] public required string MessageId { get; init; }
    [JsonPropertyName("eventType")] public string EventType { get; init; } = "CaseClosed";
    [JsonPropertyName("schemaVersion")] public string SchemaVersion { get; init; } = "1.0";
    [JsonPropertyName("occurredAt")] public required string OccurredAt { get; init; }
    [JsonPropertyName("transport")] public required string Transport { get; init; }
    [JsonPropertyName("correlationId")] public string? CorrelationId { get; init; }
    [JsonPropertyName("organization")] public string? Organization { get; init; }
    [JsonPropertyName("case")] public required CaseClosedCase Case { get; init; }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}

public sealed record CaseClosedCase
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("caseNumber")] public required string CaseNumber { get; init; }
    [JsonPropertyName("title")] public required string Title { get; init; }
    [JsonPropertyName("category")] public required string Category { get; init; }
    [JsonPropertyName("priority")] public required string Priority { get; init; }

    // Nullable-in-schema properties are always written (as null) so receivers see a stable shape.
    [JsonPropertyName("customer"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public CaseClosedCustomer? Customer { get; init; }

    [JsonPropertyName("requesterContactId"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? RequesterContactId { get; init; }

    [JsonPropertyName("createdOn")] public required string CreatedOn { get; init; }

    [JsonPropertyName("dueDate"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? DueDate { get; init; }

    [JsonPropertyName("resolvedOn"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ResolvedOn { get; init; }

    [JsonPropertyName("closedOn")] public required string ClosedOn { get; init; }
    [JsonPropertyName("slaMet")] public required bool SlaMet { get; init; }

    [JsonPropertyName("refundAmount"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public decimal? RefundAmount { get; init; }
}

public sealed record CaseClosedCustomer
{
    [JsonPropertyName("accountId")] public required string AccountId { get; init; }

    [JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Name { get; init; }
}
