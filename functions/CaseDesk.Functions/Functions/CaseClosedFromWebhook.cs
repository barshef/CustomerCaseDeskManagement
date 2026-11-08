using System.Net;
using CaseDesk.Functions.CaseClosed;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace CaseDesk.Functions.Functions;

/// <summary>
/// Alternative transport. Dataverse webhook "CaseDesk CaseClosed Webhook" (auth WebhookKey, sent as
/// x-functions-key) POSTs the RemoteExecutionContext here. 202 = forwarded (new or duplicate),
/// 400 = not a Closed Case Update, 500 = downstream failure (the Dataverse async job retries).
/// </summary>
public sealed class CaseClosedFromWebhook
{
    public const string Transport = "Webhook";

    private readonly CaseClosedHandler _handler;
    private readonly ILogger<CaseClosedFromWebhook> _logger;

    public CaseClosedFromWebhook(CaseClosedHandler handler, ILogger<CaseClosedFromWebhook> logger)
    {
        _handler = handler;
        _logger = logger;
    }

    [Function(nameof(CaseClosedFromWebhook))]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "caseclosed")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var body = await req.ReadAsStringAsync() ?? "";
        HandleResult result;
        try
        {
            result = await _handler.HandleAsync(body, Transport, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "CaseClosedWebhookFailed {Transport} {DataverseRequestId}",
                Transport, Header(req, "x-request-id"));
            return await HttpJson.Write(req, HttpStatusCode.InternalServerError,
                new { error = "Forwarding to the external system failed; retry later." });
        }

        return result.Outcome == HandleOutcome.Forwarded
            ? await HttpJson.Write(req, HttpStatusCode.Accepted, new { messageId = result.MessageId, duplicate = result.Reason is not null })
            : await HttpJson.Write(req, HttpStatusCode.BadRequest, new { error = result.Reason });
    }

    private static string? Header(HttpRequestData req, string name) =>
        req.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
}

internal static class HttpJson
{
    public static async Task<HttpResponseData> Write(HttpRequestData req, HttpStatusCode status, object body)
    {
        var res = req.CreateResponse();
        await res.WriteAsJsonAsync(body);
        res.StatusCode = status; // WriteAsJsonAsync sets 200; override afterwards.
        return res;
    }
}
