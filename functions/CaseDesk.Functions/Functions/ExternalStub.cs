using System.Net;
using CaseDesk.Functions.ExternalStub;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace CaseDesk.Functions.Functions;

/// <summary>
/// Stand-in for the external system. POST records CaseClosed v1 (de-dup by messageId in Table Storage
/// table CaseClosedReceived): 201 {"duplicate":false} for new, 200 {"duplicate":true} for repeats.
/// GET ?caseNumber=CD-00001 lists what was received (demo aid).
/// </summary>
public sealed class ExternalStub
{
    private readonly ExternalStubService _service;

    public ExternalStub(ExternalStubService service) => _service = service;

    [Function(nameof(ExternalStub))]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", "get", Route = "external/caseclosed")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        if (string.Equals(req.Method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            var caseNumber = req.Query["caseNumber"];
            if (string.IsNullOrWhiteSpace(caseNumber))
                return await HttpJson.Write(req, HttpStatusCode.BadRequest, new { error = "Query parameter 'caseNumber' is required." });
            var records = await _service.GetByCaseNumberAsync(caseNumber, cancellationToken);
            return await HttpJson.Write(req, HttpStatusCode.OK,
                records.Select(r => new { r.MessageId, r.CaseId, r.CaseNumber, r.Transport, r.ReceivedAt }).ToList());
        }

        var body = await req.ReadAsStringAsync() ?? "";
        var result = await _service.ReceiveAsync(body, cancellationToken);
        return result.Status switch
        {
            ReceiveStatus.Created => await HttpJson.Write(req, HttpStatusCode.Created, new { duplicate = false, messageId = result.MessageId }),
            ReceiveStatus.Duplicate => await HttpJson.Write(req, HttpStatusCode.OK, new { duplicate = true, messageId = result.MessageId }),
            _ => await HttpJson.Write(req, HttpStatusCode.BadRequest, new { error = result.Error }),
        };
    }
}
