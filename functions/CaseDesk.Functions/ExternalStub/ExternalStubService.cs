using System.Text.Json;
using CaseDesk.Functions.CaseClosed;
using Microsoft.Extensions.Logging;

namespace CaseDesk.Functions.ExternalStub;

public enum ReceiveStatus { Created, Duplicate, BadRequest }

public sealed record ReceiveResult(ReceiveStatus Status, string? MessageId, string? Error)
{
    public bool Duplicate => Status == ReceiveStatus.Duplicate;
}

/// <summary>The external system's behaviour, independent of the HTTP trigger: validate, de-dup, log.</summary>
public sealed class ExternalStubService
{
    private readonly ICaseClosedReceivedStore _store;
    private readonly ILogger<ExternalStubService> _logger;
    private readonly TimeProvider _time;

    public ExternalStubService(ICaseClosedReceivedStore store, ILogger<ExternalStubService> logger, TimeProvider? time = null)
    {
        _store = store;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<ReceiveResult> ReceiveAsync(string body, CancellationToken cancellationToken = default)
    {
        CaseClosedEvent? evt;
        try
        {
            evt = JsonSerializer.Deserialize<CaseClosedEvent>(body, CaseClosedEvent.JsonOptions);
        }
        catch (JsonException ex)
        {
            return new ReceiveResult(ReceiveStatus.BadRequest, null, "Body is not CaseClosed v1 JSON: " + ex.Message);
        }

        if (evt is null || string.IsNullOrWhiteSpace(evt.MessageId) || evt.Case is null || string.IsNullOrWhiteSpace(evt.Case.Id))
            return new ReceiveResult(ReceiveStatus.BadRequest, null, "messageId and case.id are required.");
        if (evt.EventType != "CaseClosed" || evt.SchemaVersion != "1.0")
            return new ReceiveResult(ReceiveStatus.BadRequest, evt.MessageId, "Only eventType 'CaseClosed' schemaVersion '1.0' is accepted.");
        if (!RowKeys.IsValidKey(evt.Case.Id))
            return new ReceiveResult(ReceiveStatus.BadRequest, evt.MessageId, "case.id contains characters not allowed in a key.");

        var record = new CaseClosedReceivedRecord(
            evt.Case.Id, evt.MessageId, evt.Case.CaseNumber, evt.Transport, _time.GetUtcNow(), body);

        if (!await _store.TryAddAsync(record, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation("CaseClosedDuplicate {MessageId} {CaseNumber} {Transport}",
                evt.MessageId, evt.Case.CaseNumber, evt.Transport);
            return new ReceiveResult(ReceiveStatus.Duplicate, evt.MessageId, null);
        }

        _logger.LogInformation(
            "CaseClosedReceived {MessageId} {CaseNumber} {Category} {Priority} {SlaMet} {Transport} {CorrelationId} {Payload}",
            evt.MessageId, evt.Case.CaseNumber, evt.Case.Category, evt.Case.Priority, evt.Case.SlaMet,
            evt.Transport, evt.CorrelationId, body);
        return new ReceiveResult(ReceiveStatus.Created, evt.MessageId, null);
    }

    public Task<IReadOnlyList<CaseClosedReceivedRecord>> GetByCaseNumberAsync(string caseNumber, CancellationToken cancellationToken = default) =>
        _store.GetByCaseNumberAsync(caseNumber, cancellationToken);
}
