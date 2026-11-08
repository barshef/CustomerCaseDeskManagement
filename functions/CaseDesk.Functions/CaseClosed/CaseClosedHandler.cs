using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CaseDesk.Functions.Dataverse;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CaseDesk.Functions.CaseClosed;

/// <summary>App settings <c>ExternalStubUrl</c> and <c>ExternalStubKey</c> (Key Vault reference in Azure).</summary>
public sealed class ExternalStubOptions
{
    public string Url { get; set; } = "";
    public string Key { get; set; } = "";
}

public enum HandleOutcome
{
    /// <summary>Mapped and accepted by the external system (new or duplicate).</summary>
    Forwarded,
    /// <summary>Not a Closed Case Update — nothing to do. Never retried.</summary>
    Ignored,
    /// <summary>Malformed body or unmappable Closed case. Retrying would not help.</summary>
    Invalid,
}

public sealed record HandleResult(HandleOutcome Outcome, string? MessageId, string? Reason);

/// <summary>
/// Shared by <c>CaseClosedFromServiceBus</c> and <c>CaseClosedFromWebhook</c>: parse the Dataverse
/// RemoteExecutionContext, map it to CaseClosed v1 and POST it to the external system.
/// This is a typed HttpClient; the standard resilience handler (3 retries, exponential back-off)
/// is attached in <see cref="ServiceCollectionExtensions.AddCaseClosedForwarding"/>.
/// Downstream failures throw so the transport retries (Service Bus redelivery / Dataverse async retry).
/// </summary>
public sealed class CaseClosedHandler
{
    public const string FunctionKeyHeader = "x-functions-key";

    private readonly HttpClient _http;
    private readonly ExternalStubOptions _options;
    private readonly ILogger<CaseClosedHandler> _logger;

    public CaseClosedHandler(HttpClient http, IOptions<ExternalStubOptions> options, ILogger<CaseClosedHandler> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<HandleResult> HandleAsync(string body, string transport, CancellationToken cancellationToken = default)
    {
        RemoteExecutionContext context;
        try
        {
            context = RemoteExecutionContext.Parse(body);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "CaseClosedInvalid {Transport}: body is not a RemoteExecutionContext JSON document", transport);
            return new HandleResult(HandleOutcome.Invalid, null, "Body is not a RemoteExecutionContext JSON document: " + ex.Message);
        }

        return await HandleAsync(context, transport, cancellationToken).ConfigureAwait(false);
    }

    public async Task<HandleResult> HandleAsync(RemoteExecutionContext context, string transport, CancellationToken cancellationToken = default)
    {
        var map = CaseClosedMapper.Map(context, transport);
        switch (map.Status)
        {
            case MapStatus.Ignored:
                _logger.LogWarning(
                    "CaseClosedIgnored {Transport} {Entity} {Message} {CaseId} {CorrelationId}: {Reason}",
                    transport, context.PrimaryEntityName, context.MessageName, context.PrimaryEntityId, context.CorrelationId, map.Reason);
                return new HandleResult(HandleOutcome.Ignored, null, map.Reason);
            case MapStatus.Invalid:
                _logger.LogError(
                    "CaseClosedInvalid {Transport} {CaseId} {CorrelationId}: {Reason}",
                    transport, context.PrimaryEntityId, context.CorrelationId, map.Reason);
                return new HandleResult(HandleOutcome.Invalid, null, map.Reason);
        }

        var evt = map.Event!;
        if (string.IsNullOrWhiteSpace(_options.Url))
            throw new InvalidOperationException("App setting 'ExternalStubUrl' is not configured.");

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Url)
        {
            Content = new StringContent(evt.ToJson(), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrEmpty(_options.Key))
            request.Headers.TryAddWithoutValidation(FunctionKeyHeader, _options.Key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError(
                "CaseClosedForwardFailed {Transport} {MessageId} {CaseNumber} {StatusCode}",
                transport, evt.MessageId, evt.Case.CaseNumber, (int)response.StatusCode);
            throw new HttpRequestException(
                $"External system returned {(int)response.StatusCode} for {evt.MessageId}: {Truncate(detail, 500)}",
                null, response.StatusCode);
        }

        var duplicate = response.StatusCode == System.Net.HttpStatusCode.OK; // stub: 200 = duplicate, 201 = new
        _logger.LogInformation(
            "CaseClosedForwarded {Transport} {MessageId} {CaseNumber} {SlaMet} {Duplicate} {CorrelationId}",
            transport, evt.MessageId, evt.Case.CaseNumber, evt.Case.SlaMet, duplicate, evt.CorrelationId);

        return new HandleResult(HandleOutcome.Forwarded, evt.MessageId, duplicate ? "Duplicate at receiver" : null);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
