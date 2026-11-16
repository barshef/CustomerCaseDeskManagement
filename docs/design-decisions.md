# Interview notes: why each extension point

For each BRD requirement: what implements it, the short "why" answer, and the alternative
that was rejected and the reason. The order of preference comes from constitution I: platform
config → declarative → low-code → pro-code. Pro-code needs a reason that low-code cannot meet.

## FR1: Case Number `CD-00001`, never edited

- **Used**: Auto-number column `cd_casenumber`, format `CD-{SEQNUM:5}`, read-only.
- **Why**: The platform does it. It is gap-free enough for a reference number, concurrency
  safe, and needs no code.
- **Alternative**: A pre-create plugin with a counter table. That means locking, contention and
  code to test, all to rebuild a platform feature.

## FR2: Title and Customer mandatory; Refund Amount only for Refund

- **Used**: Business rules: form scope (show and require when Refund, else hide and clear) and
  entity scope (error when Refund and no amount). Title and Customer are form-required, and the
  pre-create plugin also rejects them missing (CD001/CD002).
- **Why**: Declarative logic that admins can read. Entity-scope rules also run server side, so
  API writes obey them. The plugin check is there because "Business Required" is enforced only
  on the form.
- **Alternative**: Form JavaScript (client only, bypassed by the API), or plugin only (works,
  but hides simple UX rules in code).

## FR3: Due Date = created on + Resolution Hours

- **Used**: C# plugin S1, pre-operation, synchronous on Create. It also sets `cd_warningon`
  and the SLA policy lookup, and recalculates on a priority change (S2). The alternate key
  `cd_slapolicy_priority_key` keeps one policy per priority.
- **Why**: It must happen in the same transaction, so no case is ever saved without a due
  date. Pre-operation means the value is written in the same database write, with no second
  update.
- **Alternative**: Power Automate (async, so the case exists for seconds without a due date and
  a failed run leaves it blank). A calculated column cannot look up "the policy whose priority
  matches".

## FR4: Cannot resolve with open notes

- **Used**: Plugin S2, pre-operation on Update (filtered on status, priority and category),
  throws `InvalidPluginExecutionException` "CD004: This case has n open note(s)…".
- **Why**: A rule that must hold for the UI, flows and the Web API alike has to be on the
  server. Throwing in pre-op rolls back the whole save and shows the message to the user.
- **Alternative**: Business rule (cannot query child rows), or form script (bypassed by any
  integration).

## FR5: Refund approval

- **Used**: S2 rewrites Resolved → Awaiting Approval for Refund cases unless the latest Refund
  Approval is Approved. A cloud flow creates the approval row and runs a "first to respond"
  approval to supervisors in Teams or Outlook, then writes back the result.
- **Why**: Approvals are a native flow action, with Teams cards, email, reminders and history.
  The plugin is only the guard, so nobody can skip approval through the API.
- **Alternative**: Flow only (an API caller could set Resolved directly). Plugin only (you
  would have to build approval UX and notifications yourself).

## FR6: Customer email on create and on close

- **Used**: Automated cloud flows (Dataverse trigger) that create an Email row regarding the
  case and call `SendEmail`. Server-side sync sends it. The sender comes from env var
  `cd_SenderQueueEmail`.
- **Why**: Low-code, visible to admins, and templates are easy to change. Using the Email table
  puts the message on the case timeline (native pattern, BRD decision of 8 Nov).
- **Alternative**: The Office 365 Outlook connector. It is simpler to set up but leaves no
  timeline record, so it is kept as a fallback. A plugin calling SMTP would be sync external
  HTTP, which the constitution bans.

## FR7: SLA warning and daily breach digest

- **Used**: Scheduled flows: every 15 min (`cd_warningon <= now`, not yet warned → in-app
  notification + email, set `cd_slawarningsent`), and daily at 07:00 (breach digest to
  supervisors).
- **Why**: Time-based. No data event happens at the moment a case crosses its warning time, so
  there is nothing for a plugin to hook. Storing `cd_warningon` keeps the query a simple date
  comparison.
- **Alternative**: Dynamics 365 Customer Service SLA KPIs. They need a Customer Service licence,
  which is out of scope. Another option is an Azure timer Function (more moving parts for the
  same result).

## FR8: Live SLA countdown badge

- **Used**: PCF field control (TypeScript + React, Fluent UI v9) bound to `cd_duedate`, with
  a pure `computeSlaState()` function tested in Jest. It re-renders every 30 s.
- **Why**: The platform cannot render a live, colour-changing countdown. PCF is the supported
  way to build a custom control that is reusable, packaged in the solution and bound to a column.
- **Alternative**: An HTML web resource in an iframe (legacy, not bound to the field, harder to
  theme), or a calculated "status" column (not live; it only changes on save).

## FR9: Escalate button

- **Used**: Custom API `cd_EscalateCase` (bound to `cd_case`) backed by `EscalateCasePlugin`
  in the main operation, called from a ribbon command (JavaScript, `Xrm.WebApi.online.execute`).
  It sets priority Critical, assigns to "CaseDesk Supervisors" and adds a note in one
  transaction. It is idempotent.
- **Why**: One reusable server operation with a typed contract (request and response) that JS,
  flows and external callers can all use. It is atomic, and the logic is in one tested place.
- **Alternative**: Three client-side Web API calls from JS (not atomic, logic in the browser),
  a classic custom action / workflow (legacy), or a Power Fx command (fine for UI, but not
  reusable by external callers).

## FR10: CaseClosed to an external system

- **Used**: Async post-op plugin S3 that picks a service endpoint by env var
  `cd_CaseClosedTransport` → Service Bus queue (default) or webhook → Azure Function →
  external stub. Details: [architecture.md](architecture.md#caseclosed-sequence-both-transports).
- **Why**: It decouples Dataverse from the external system. Closing a case never waits on it
  or fails because of it, and delivery is retried. The env var makes the transport a
  configuration switch.
- **Alternative**: A sync plugin calling HTTP (banned: slow and fragile, and it rolls back the
  user's save on an outage). A flow with an HTTP action (premium connector, weaker retry and
  ordering control). An async step registered directly on a service endpoint with no plugin
  (the "no-code variant": simpler, but switching transport means re-registering steps).

## FR11: Auto-close after 5 days

- **Used**: Scheduled flow, daily at 02:00. It checks `cd_resolvedon <= now − cd_AutoCloseDays`
  and skips cases with an inbound email after Resolved On (it flags them instead).
- **Why**: Time-based, like FR7. The threshold is an env var so UAT can test with 0 days.
- **Alternative**: A bulk-delete style system job (cannot change status), or a plugin (no event
  to hook).

## FR12: Dashboards

- **Used**: Model-driven dashboards, charts and system views (My Open Cases by priority; SLA
  Breaches This Week).
- **Why**: Configuration. It is solution-aware, respects security automatically and needs no
  code.
- **Alternative**: Power BI embedded (better analytics, but needs a licence and a dataset
  refresh, which is too much for two charts).

## FR13: Agents see their own BU; supervisors see all

- **Used**: Security roles (Agent: Case at BU level; Supervisor: Org level) and business units
  `CaseDesk BU A` / `B` created by the seeder. The escalation team is in the root BU.
- **Why**: Platform security is enforced in every client, API and report, with no code to get
  wrong.
- **Alternative**: Filtering views or a plugin on RetrieveMultiple. That is security by
  obscurity: one missed query path leaks data.

## FR14: Tests and deploy on merge

- **Used**: xUnit + FakeXrmEasy for plugins, xUnit for Functions and Jest for PCF. GitHub
  Actions `ci.yml` runs all tests and then packs the solution. `deploy-uat.yml` on push to master
  calls CI as a reusable workflow, then imports the **managed** zip to UAT with
  `microsoft/powerplatform-actions`, using a GitHub `uat` environment for approvals.
- **Why**: ALM is an explicit PL-400 topic. Tests run before packing, so a red test blocks
  deployment. Managed downstream means UAT is never hand-edited.
- **Alternative**: Power Platform Pipelines (in-product, simple, but less control over tests
  and code builds) or Azure DevOps (same capability; GitHub fits a public repo).

## FR15: Browser walkthroughs and guide films

- **Used**: Playwright Test (TypeScript) in `e2e/`, always headed, driving the model-driven app
  as three personas (Agent BU A, Agent BU B, Supervisor) with saved sign-ins. Film mode records
  video, adds a caption band and chapter cards, stitches with ffmpeg and adds an Azure Speech
  voice-over.
- **Why**: Unit tests prove the server rules; only a browser run proves the form, ribbon, PCF,
  flows and security work together. The same run becomes a repeatable client demo.
- **Alternative**: EasyRepro (Selenium-based, Microsoft's older UI test framework for
  model-driven apps) or Power Apps Test Engine (strong for canvas apps, weaker for model-driven
  forms); neither produces captioned films.

## Two-transport learning notes (Service Bus vs webhook)

| | Service Bus (default) | Webhook (alternative) |
| --- | --- | --- |
| Endpoint registration | Service endpoint, contract Queue, message format **JSON**, SAS key (send) | Service endpoint, contract Webhook, auth **WebhookKey** = Function key |
| Delivery | Dataverse → queue; Function pulls | Dataverse POSTs directly to the Function URL |
| If Azure Function is down | Messages wait in the queue (durable buffer) | Async job fails; Dataverse retries the system job with back-off, then marks it failed |
| Retries | Service Bus redelivery (max delivery count 10) → **dead-letter queue** to inspect | Dataverse async retries; failures visible in *System Jobs* |
| Ordering / load levelling | Queue smooths bursts; consumer sets concurrency | Each close is one HTTP call; bursts hit the Function directly |
| Duplicate risk | At-least-once delivery → receiver de-dup (Basic tier has no broker de-dup) | Retries after a timeout can resend → same receiver de-dup |
| Cost / setup | Namespace + queue + SAS rule; Basic tier is cents | Nothing extra beyond the Function |
| Payload | `RemoteExecutionContext` JSON in the message body | Same `RemoteExecutionContext` JSON in the POST body |
| Best when | Receiver may be offline, you want buffering, replay and dead-letter | Simple, low volume, receiver always up, fewest moving parts |

Things worth saying in an interview:

- **One switch, one handler.** `cd_CaseClosedTransport` picks the endpoint at run time. Both
  Functions call the same `CaseClosedHandler`, so only the transport differs and the mapping is
  unit tested once.
- **Idempotency is the receiver's job.** Both paths are at-least-once. The stable message id
  `{caseId}:{closedOn}` plus a Table Storage record makes repeats harmless.
- **Why an async plugin instead of a step registered directly on the endpoint?** A direct step
  is the "no-code" option, but switching transport then means re-registering. The plugin turns
  that into a configuration change and lets us skip non-Closed updates cheaply. The Function
  ignores non-Closed contexts anyway, so the no-code variant also works with the same Azure code.
- **Secrets stay out of source.** The SAS key and Function key are typed into the endpoint
  registration per environment. Function app settings use Key Vault references.
- **Never call HTTP synchronously from a plugin.** That is the anti-pattern this whole design
  avoids: the user's save would depend on a third party's uptime.
- **Observability.** `ITracingService` in the plugin (Plugin Trace Log), System Jobs for async
  failures, Application Insights for both Functions (`traces | where message has
  "CaseClosed"`), and the Service Bus dead-letter queue.
