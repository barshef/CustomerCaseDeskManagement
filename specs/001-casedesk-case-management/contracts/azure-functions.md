# Contract: Azure Functions (`/functions/CaseDesk.Functions`)

One Function app, .NET 8 isolated worker. All HTTP functions use `AuthorizationLevel.Function`
(key in `x-functions-key` header or `?code=`). Keys live in Key Vault / the Dataverse service
endpoint registration — never in source.

## `CaseClosedFromServiceBus` — Service Bus trigger (default transport)

- Queue: `caseclosed` on connection `ServiceBusConnection`.
- Body: Dataverse `RemoteExecutionContext` serialised as **JSON** (service endpoint message
  format = JSON).
- Behaviour: `CaseClosedHandler.HandleAsync(context, transport: "ServiceBus")`. Exceptions
  propagate so Service Bus retries (max delivery count 10) and then dead-letters.

## `CaseClosedFromWebhook` — HTTP trigger (alternative transport)

- `POST /api/caseclosed` — body: `RemoteExecutionContext` JSON as sent by a Dataverse webhook
  service endpoint. Auth type WebhookKey makes Dataverse append `?code=<function key>` to the
  URL (accepted by Functions); the HttpHeader auth type with `x-functions-key` also works.
- Responses: `202 Accepted` on success or duplicate; `400` if the body is not a Case Update
  context for a Closed case; `500` on downstream failure (Dataverse async job retries).

## `ExternalStub` — HTTP trigger (stand-in for the external system)

- `POST /api/external/caseclosed` — body: CaseClosed v1
  ([caseclosed-event.schema.json](caseclosed-event.schema.json)).
- Records `messageId` in Table Storage table `CaseClosedReceived` (PartitionKey = case id,
  RowKey = escaped message id). Repeat → `200 OK {"duplicate":true}`; new → logs the payload
  (`ILogger` → Application Insights, event `CaseClosedReceived`) and returns
  `201 Created {"duplicate":false}`.
- `GET /api/external/caseclosed?caseNumber=CD-00001` — returns recorded messages (demo aid).

## `CaseClosedHandler` (shared, unit tested)

1. Parse `RemoteExecutionContext`; require `PrimaryEntityName = cd_case`,
   `MessageName = Update`, post-image `PostImage` with `statecode = 1`, `statuscode = 2`.
2. Map to CaseClosed v1 (choice values → labels using the table in
   [data-model.md](../data-model.md)).
3. POST to `ExternalStubUrl` with header `x-functions-key: ExternalStubKey`, using
   `HttpClient` with Polly-style retry (3 attempts, exponential back-off) via
   `Microsoft.Extensions.Http.Resilience`.

## App settings

| Setting | Source |
| --- | --- |
| `ServiceBusConnection` | Key Vault reference (listen-only SAS) |
| `ExternalStubUrl` | Plain setting |
| `ExternalStubKey` | Key Vault reference |
| `AzureWebJobsStorage` | Managed by platform |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Platform |
