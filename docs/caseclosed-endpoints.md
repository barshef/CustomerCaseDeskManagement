# CaseClosed endpoints — registering Service Bus and webhook in Dataverse

When a case is **Closed**, CaseDesk tells an external system about it (User Story 7). Dataverse
never calls Azure from inside the user's transaction: an **asynchronous** plugin hands the
execution context to a Dataverse **service endpoint**, and Dataverse delivers it in the
background. Two endpoints exist so you can compare the transports; an environment variable
picks one at run time.

```
Agent closes case
   │  (sync: state change commits immediately)
   ▼
PublishCaseClosedPlugin  ── async, PostOperation, Update of cd_case ──┐
   │ reads cd_CaseClosedTransport                                     │
   ├─ ServiceBus ─► "CaseDesk CaseClosed ServiceBus" ─► queue caseclosed ─► CaseClosedFromServiceBus ─┐
   ├─ Webhook ────► "CaseDesk CaseClosed Webhook" ───► POST /api/caseclosed ─► CaseClosedFromWebhook ─┤
   └─ None ───────► nothing                                                                            ▼
                                                     CaseClosedHandler ─► POST /api/external/caseclosed (ExternalStub)
                                                     (maps RemoteExecutionContext → CaseClosed v1,          de-dups by messageId
                                                      3 HTTP retries with back-off)                          in Table Storage
```

Prerequisites: Azure resources deployed and the Function app published —
[functions/infra/README.md](../functions/infra/README.md). Commands below reuse its `$RG`,
`$APP`, `$SB` and `$HOST` variables.

## 1. Open the Plugin Registration Tool

```bash
pac tool prt
```

*Create New Connection* → Office 365 → sign in → pick the CaseDesk Dev environment.

## 2. Register the Service Bus endpoint (default transport)

1. Get the **Send** connection string of the queue-scoped rule `dataverse-send`:

   ```bash
   az servicebus queue authorization-rule keys list -g $RG --namespace-name $SB \
     --queue-name caseclosed --name dataverse-send --query primaryConnectionString -o tsv
   ```

2. PRT → **Register → Register New Service Endpoint** → paste the connection string → **Next**.
3. Complete the *Service Endpoint Details* dialog:

   | Field | Value |
   | --- | --- |
   | Name | `CaseDesk CaseClosed ServiceBus` (exactly — the plugin looks it up by name) |
   | Namespace Address | `sb://<namespace>.servicebus.windows.net/` (pre-filled) |
   | Designation Type | **Queue** |
   | Queue Name | `caseclosed` |
   | Message Format | **JSON** (the Function parses JSON; *.NET Binary* and *XML* will not work) |
   | SAS Key Name | `dataverse-send` |
   | SAS Key | the primary key (pre-filled from the connection string) |
   | User Information Sent | `UserId` |

4. **Save**. Do *not* register a step on it if you use the plugin (section 4).

## 3. Register the webhook endpoint (alternative transport)

1. Create a function key for `CaseClosedFromWebhook`:

   ```bash
   az functionapp function keys set -g $RG -n $APP --function-name CaseClosedFromWebhook \
     --key-name dataverse-webhook --query value -o tsv
   ```

2. PRT → **Register → Register New Web Hook**:

   | Field | Value |
   | --- | --- |
   | Name | `CaseDesk CaseClosed Webhook` (exactly) |
   | Endpoint URL | `https://<app>.azurewebsites.net/api/caseclosed` (output `webhookUrl` of the deployment) |
   | Authentication | **WebhookKey** |
   | Value | the function key from step 1 |

   *WebhookKey* makes Dataverse call `…/api/caseclosed?code=<key>`, which is exactly how Azure
   Functions accepts a function key. (Choosing *HttpHeader* with header `x-functions-key` works
   too.)

3. **Save**.

The keys live only on the endpoint registrations in each environment — never in the solution
or in source control. Service endpoints are environment-specific; register them in every
environment (Dev, UAT) that should publish.

## 4. How the plugin picks an endpoint

`PublishCaseClosedPlugin` is registered on **Update of `cd_case`**, **PostOperation**,
**Asynchronous**, filtering attributes `statecode, statuscode`, with images `PreImage`
(`statuscode`) and `PostImage` (all columns). It:

1. Returns unless the update is a *transition into* Closed (pre-image status ≠ Closed, post-image
   status = Closed).
2. Reads environment variables:

   | Variable | Default | Meaning |
   | --- | --- | --- |
   | `cd_CaseClosedTransport` | `ServiceBus` | `ServiceBus` \| `Webhook` \| `None` (publishing off) |
   | `cd_CaseClosedServiceBusEndpointName` | `CaseDesk CaseClosed ServiceBus` | endpoint name used for `ServiceBus` |
   | `cd_CaseClosedWebhookEndpointName` | `CaseDesk CaseClosed Webhook` | endpoint name used for `Webhook` |

3. Looks up the `serviceendpoint` row **by name** and calls
   `IServiceEndpointNotificationService.Execute(endpoint, context)`. The context it sends is its
   own execution context, so the Function receives `MessageName = Update`,
   `PrimaryEntityName = cd_case` and the `PostImage`.
4. If the endpoint is missing, it throws, so the system job fails visibly (*Settings → System
   jobs*). Register the endpoint or set the transport to `None`, then resubmit.

**Switching transport** is a configuration change only: *Solutions → CaseDesk → Environment
variables → CaseClosed Transport → Current value* = `Webhook` (or `ServiceBus` / `None`). No
re-registration, no deployment.

The Function side builds `messageId = "{caseId}:{cd_closedon in ISO-8601 'O' UTC}"`. Every retry
of the same close — by the async job, by Service Bus redelivery, or by the handler's HTTP
retries — carries the same id, and the stub answers repeats with `200 {"duplicate":true}`.

## 5. The no-code variant

You can skip the plugin and let Dataverse post the context directly:

1. PRT → select the endpoint (`CaseDesk CaseClosed ServiceBus` *or* `… Webhook`) →
   **Register New Step**.
2. Message `Update`, Primary Entity `cd_case`, Filtering Attributes `statuscode`, Stage
   **PostOperation**, Execution Mode **Asynchronous**.
3. Register a **Post Image** on that step with alias **`PostImage`** (the Function requires this
   alias) containing at least: `cd_casenumber, cd_name, cd_category, cd_priority, cd_accountid,
   cd_contactid, createdon, cd_duedate, cd_resolvedon, cd_closedon, cd_refundamount, statecode,
   statuscode`.
4. Disable (or don't register) `PublishCaseClosedPlugin`, or each close is sent twice (harmless
   thanks to the messageId, but noisy).

Trade-offs: no code to maintain, but the step fires on *every* status change, not just "into
Closed". The handler ignores those contexts — the Service Bus function completes them quietly,
while the webhook function answers `400`, which Dataverse records as a **failed system job** for
each non-close status change. Switching transport also means moving the step to the other
endpoint. That is why CaseDesk uses the plugin by default.

## 6. Service Bus vs webhook — which and why

| | Service Bus queue (default) | Webhook |
| --- | --- | --- |
| Coupling | Dataverse only needs the queue up; the Function can be stopped, redeployed or slow | Dataverse calls the Function directly; it must be reachable |
| Buffering | Messages wait in the queue (TTL 14 days), then dead-letter after 10 failed deliveries | None — the async job retries a few times, then fails |
| Retries | Function exception → redelivery up to `maxDeliveryCount` 10 | `500` → Dataverse async job retry policy |
| Payload size | Up to 256 KB per message on Basic tier | Large contexts are truncated by Dataverse (images dropped) |
| Moving parts / cost | Namespace + SAS rules + Key Vault secret | Just a function key; nothing extra in Azure |
| Visibility | Queue metrics, dead-letter queue | Dataverse system jobs + Function requests |

Rule of thumb: Service Bus when the receiver must be able to be down without losing events;
webhook for a simple, low-volume push where an occasional failed system job is acceptable.

## 7. Watching it in Application Insights

Close a Resolved case, then in the Function app's Application Insights → **Logs**:

Which trigger handled each close, and what reached the external system:

```kusto
traces
| where timestamp > ago(1h)
| where message has_any ("CaseClosedForwarded", "CaseClosedReceived", "CaseClosedIgnored", "CaseClosedInvalid", "CaseClosedForwardFailed")
| extend Transport = tostring(customDimensions.Transport),
         MessageId = tostring(customDimensions.MessageId),
         CaseNumber = tostring(customDimensions.CaseNumber)
| project timestamp, operation_Name, Transport, CaseNumber, MessageId, message
| order by timestamp desc
```

Service Bus path only (the trigger runs as `CaseClosedFromServiceBus`):

```kusto
requests
| where timestamp > ago(1h) and operation_Name == "CaseClosedFromServiceBus"
| project timestamp, success, duration, resultCode, operation_Id
| order by timestamp desc
```

Webhook path only (`CaseClosedFromWebhook`, HTTP result codes 202 / 400 / 500):

```kusto
requests
| where timestamp > ago(1h) and operation_Name == "CaseClosedFromWebhook"
| summarize count() by resultCode, bin(timestamp, 5m)
```

End-to-end latency from Dataverse close to stub receipt (< 1 minute target):

```kusto
traces
| where timestamp > ago(1d) and message has "CaseClosedReceived"
| extend MessageId = tostring(customDimensions.MessageId)
| extend ClosedOn = todatetime(extract(@":(\d{4}-.+Z)$", 1, MessageId))
| project CaseNumber = tostring(customDimensions.CaseNumber), ClosedOn, ReceivedAt = timestamp,
          LatencySeconds = datetime_diff('second', timestamp, ClosedOn)
| order by ReceivedAt desc
```

Duplicates the stub recognised (retries / redeliveries) and failures:

```kusto
union traces, exceptions
| where timestamp > ago(1d)
| where message has "CaseClosedDuplicate" or message has "CaseClosedForwardFailed" or itemType == "exception"
| project timestamp, itemType, operation_Name, message, outerMessage
| order by timestamp desc
```

> Property names in `customDimensions` come from the log message template
> (`{Transport}`, `{MessageId}`, …). Logs written by the Functions host rather than the worker may
> prefix them with `prop__`.

Other places to look:

- **Stub record**: `GET https://$HOST/api/external/caseclosed?caseNumber=CD-00042` with header
  `x-functions-key`.
- **Dead letters**: `az servicebus queue show -g $RG --namespace-name $SB -n caseclosed --query countDetails`.
- **Dataverse side**: *Advanced Settings → System Jobs*, filter on *PublishCaseClosedPlugin*
  or the endpoint step, for waiting/failed async operations.
