# Research: CaseDesk — Customer Case Management

All Technical Context unknowns are resolved below. Format: Decision / Rationale / Alternatives.

## R1. Solution source format

- **Decision**: A Power Platform *solution project* (`/solution/CaseDesk.cdsproj`, created with
  `pac solution init`) holding the unpacked solution under `/solution/src` in SolutionPackager
  format. The cdsproj references the plugin and PCF projects so `dotnet build` produces the
  unmanaged and managed zips.
- **Rationale**: Single build entry point for CI; the unpacked XML is diff-able source of truth
  (Constitution VI).
- **Alternatives**: Export/unpack from a Dev environment only (no Dev in this phase — the user
  chose source + local build); provisioning tables through SDK metadata calls (not a solution
  source, defeats ALM principle).
- **Risk**: The solution XML is hand-authored, so the first import into Dev is the real
  validation. After that import, run `pac solution sync` so the repo matches what Dataverse
  normalises.

## R2. Plugin target and packaging

- **Decision**: One class library `CaseDesk.Plugins`, .NET Framework 4.6.2,
  `Microsoft.CrmSdk.CoreAssemblies` 9.0.2.x, strong-name signed with a committed `.snk`, no
  third-party runtime dependencies. Pure domain logic (state machine, SLA maths, payload
  mapping) lives in the same assembly in plain classes with no SDK dependency so it is unit
  testable without fakes.
- **Rationale**: Classic plugin assemblies need no dependent-assembly package and are the most
  widely understood PL-400 pattern. The `.snk` only gives the assembly a stable identity; it is
  not a secret, and it must be stable across builds for in-place updates.
- **Alternatives**: Plugin packages (`pac plugin init`, NuGet-based, allows dependencies) — not
  needed because we have no dependencies.

## R3. Plugin unit testing

- **Decision**: xUnit on net462 with `FakeXrmEasy.9` 1.58.1 (v1 line, MIT, targets .NET
  Framework, in-memory `IOrganizationService`).
- **Rationale**: Matches the BRD ("xUnit + FakeXrmEasy"), runs on Windows dev machines and
  `windows-latest` CI runners, no licence key.
- **Alternatives**: FakeXrmEasy v2/v3 (needs a licence context — RPL-1.5 or commercial — and is
  aimed at .NET Core-style plugins); hand-rolled mocks with Moq (more code, less realistic).

## R4. Status lifecycle on a custom table

- **Decision**: Use `statecode`/`statuscode`. Active state (0): New (1), Assigned, In Progress,
  Awaiting Customer, Awaiting Approval, **Resolved**. Inactive state (1): **Closed** (2),
  Cancelled. Resolved stays in the Active state so the record remains editable for reopen and
  follow-up notes; Closed and Cancelled are read-only.
- **Rationale**: Gives the platform's read-only behaviour for finished cases for free, while
  allowing reopen from Resolved without a SetState round trip.
- **Alternatives**: A separate custom Status choice column (loses the platform's
  active/inactive semantics, views and read-only behaviour).
- Transitions are enforced by the Case pre-update plugin using a pure `CaseStateMachine` class
  (Constitution II). Modern Dataverse maps `SetState` to `Update`, so one Update step covers
  both.

## R5. Refund approval mechanics

- **Decision**: The pre-update plugin, on a transition into Resolved for a Refund case,
  rewrites the target status to **Awaiting Approval** unless the most recent Refund Approval for
  the case is **Approved** and was decided after the last request. A solution-aware cloud flow
  triggers on status = Awaiting Approval, creates the Refund Approval (Pending), starts an
  "Approve/Reject – First to respond" approval to the supervisors in the owner's business unit
  (fallback: all supervisors), writes the decision back and sets the case to Resolved (allowed
  now, because an Approved record exists) or In Progress.
- **Rationale**: Server-side guard for API and UI alike (Constitution II); approvals are a native
  flow action (BRD FR5) — the plugin does not reinvent them.
- **Alternatives**: Flow-only (agents could bypass via API); plugin-only approvals (no Teams /
  email approval UX).

## R6. Field rules (FR2)

- **Decision**: Two business rules on Case: (a) form scope — show and require Refund Amount when
  Category = Refund, hide and clear otherwise; (b) entity scope — show an error when Category =
  Refund and Refund Amount is empty (entity-scope error rules run server side). Title and
  Customer are `ApplicationRequired` on the form, and the pre-create plugin also rejects a
  missing Title or Customer so API writes obey the rule.
- **Rationale**: Declarative first (Constitution I); server-side backstop where the platform's
  required level is form-only (Constitution II).

## R7. SLA due date and warning

- **Decision**: Pre-operation (sync) plugin on Create sets `cd_slapolicy`, `cd_duedate` =
  `createdon` (UTC now during pre-op) + Resolution Hours, and `cd_warningon` = due date −
  Warning Hours. On a priority change of an active, unresolved case it recomputes both from the
  original `createdon`. SLA hours are 24×7 (Clarification Q1). One policy per priority is
  enforced by an **alternate key** on `cd_slapolicy.cd_priority`.
- **Rationale**: Same-transaction guarantee (BRD FR3). Storing `cd_warningon` lets the PCF
  control and the warning flow use a simple date comparison instead of joining to the policy.
- **Alternatives**: Calculated column (cannot reference a related record's value by matching
  priority); flow (not in the transaction — a case could exist without a due date).

## R8. SLA countdown badge (FR8)

- **Decision**: A PCF field control `CaseDesk.SlaBadge` (TypeScript + React 16 via platform
  libraries, `pac pcf init --framework react`) bound to `cd_duedate`, with further bound
  properties `warningOn` (`cd_warningon`), `resolvedOn` (`cd_resolvedon`) and `closedOn`
  (`cd_closedon`, set when the case becomes Closed or Cancelled). A pure `computeSlaState(now,
  due, warningOn, resolvedOn, closedOn)` function decides none/green/amber/red/met/breached/
  stopped and is unit tested with Jest. The control re-renders every 30 s. Packaged name:
  `cd_CaseDesk.SlaBadge`.
- **Rationale**: The platform cannot render a live countdown natively (BRD FR8); the pure
  function satisfies Constitution IV for PCF.

## R9. Escalate (FR9)

- **Decision**: Custom API `cd_EscalateCase`, bound to `cd_case`, one optional input `Reason`
  (String), outputs `Escalated` (Boolean) and `Message` (String), implemented by
  `EscalateCasePlugin` in the main operation (inside the transaction → atomic). Target team:
  environment variable `cd_SupervisorTeamName` (default "CaseDesk Supervisors"). Idempotent: if
  the case is already Critical and team-owned, it returns `Escalated = false` with a message
  and makes no changes. Called from a ribbon button (RibbonDiffXml + JavaScript web resource
  `cd_/scripts/case.ribbon.js` using `Xrm.WebApi.online.execute`).
- **Alternatives**: Power Fx modern command (BRD names JavaScript ribbon and the PL-400 topic);
  custom action/workflow (legacy).

## R10. CaseClosed integration (FR10) — configurable transport

- **Decision**: Two Dataverse **service endpoints** are registered per environment:
  "CaseDesk CaseClosed ServiceBus" (Azure Service Bus queue `caseclosed`, JSON message format,
  SAS key) and "CaseDesk CaseClosed Webhook" (HTTPS → Azure Function, function key in the
  webhook auth header). An **async post-operation plugin** `PublishCaseClosedPlugin` on Case
  Update fires only on the transition into Closed, reads the environment variable
  `cd_CaseClosedTransport` (`ServiceBus` default | `Webhook` | `None`), looks up the matching
  endpoint by name and calls `IServiceEndpointNotificationService.Execute`. Azure side: one
  Function app with `CaseClosedFromServiceBus` (Service Bus trigger) and `CaseClosedFromWebhook`
  (HTTP trigger), both delegating to the same `CaseClosedHandler` that maps the
  `RemoteExecutionContext` JSON to the CaseClosed contract and POSTs it to `ExternalStub`
  (HTTP trigger with function key, logs payload to Application Insights).
- **Rationale**: User asked for both transports, Service Bus as default, switchable for
  learning. The async plugin keeps sync work under 2 s and never calls HTTP synchronously
  (Constitution III). The environment variable makes the switch a configuration change with no
  re-registration. Endpoint secrets (SAS key, function key) are entered on the service endpoint
  registration per environment and never stored in source (Constitution V).
- **Idempotency**: Message id = `{caseId}:{cd_closedon:O}` (stable across async retries). The
  stub records received ids in Azure Table Storage and ignores repeats. Service Bus stays on the
  Basic tier (no broker duplicate detection, which needs Standard).
- **Alternatives**: Registering a step directly on the service endpoint (no plugin) — simpler,
  but switching transport means re-registering steps; documented in quickstart as the "no-code"
  variant.

## R11. Customer email (FR6)

- **Decision**: Solution-aware cloud flows using the Dataverse connector: on Case create /
  on Case status = Closed → *Add a new row* (Email) with `regardingobjectid` = case,
  from = queue/system user (environment variable `cd_SenderQueueEmail`), to = requester contact
  → *Perform a bound action* `SendEmail`. Sent by server-side synchronisation. If the contact
  or email address is missing, the flow adds an internal Case Note explaining why.
- **Rationale**: BRD decision of 8 Nov 2026 — native Email activity on the timeline first.

## R12. Time-based flows (FR7, FR11)

- **Decision**: Scheduled cloud flows: *SLA Warning* every 15 min (active cases with
  `cd_warningon <= utcNow`, `cd_duedate > utcNow`, `cd_slawarningsent = false` → in-app
  notification (`appnotification` row) + email to owner, then set `cd_slawarningsent = true`);
  *Breach Digest* daily 07:00 (cases with `cd_duedate < utcNow` and unresolved, or resolved
  after due date, this week → HTML table email to each supervisor); *Auto-close* daily 02:00
  (status Resolved and `cd_resolvedon <= utcNow − 5 days`; skip and notify owner if an incoming
  Email regarding the case has `createdon > cd_resolvedon` (Clarification Q4); else set Closed).
- **Rationale**: Time-based, no event to hook a plugin on (BRD FR7, FR11).

## R13. Security (FR13)

- **Decision**: Security roles `CaseDesk Agent` (Case: Create/Read/Write/Append/AppendTo at BU,
  no Delete, no Assign beyond own; Case Note: user-level Create/Write/Delete, BU-level Read;
  SLA Policy: Org Read; Refund Approval: BU Read) and `CaseDesk Supervisor` (Case, Case Note,
  SLA Policy, Refund Approval at Org with Assign and Share; no Delete on Case). Business units
  and the "CaseDesk Supervisors" team are environment data created by the seeder, not solution
  components.

## R14. Seeding / inbound integration

- **Decision**: .NET 8 console app `/tools/CaseDesk.Seeder` using
  `Microsoft.PowerPlatform.Dataverse.Client`, connection string from environment variable
  `CASEDESK_DATAVERSE_CONNECTION` or user-secrets. Upserts by alternate key (SLA policy by
  priority, accounts by `accountnumber`, contacts by `emailaddress1` lookup) so re-runs create
  no duplicates; creates BUs "CaseDesk BU A"/"CaseDesk BU B" and the supervisors team if absent.

## R15. Azure Functions

- **Decision**: .NET 8 isolated worker, `Microsoft.Azure.Functions.Worker` +
  `.Extensions.ServiceBus` + `.Extensions.Http`, Application Insights. Settings
  (`ServiceBusConnection`, `ExternalStubUrl`, `ExternalStubKey`) come from app settings backed by
  Key Vault references. Unit tests (xUnit, net8.0) cover the mapper and de-duplication.
  Infrastructure as Bicep in `/functions/infra`: Function app (Flex/Consumption), storage
  account (also holds the stub's de-dup table), Service Bus namespace Basic tier with queue
  `caseclosed`, Key Vault, Application Insights.

## R16. CI/CD (FR14)

- **Decision**: GitHub Actions. `ci.yml` (pull requests and pushes): windows-latest, build and
  test plugins (net462), test Functions (net8), build and test PCF (Jest), build solution
  project (validates XML + packs). `deploy-uat.yml` (push to `main`): after `ci` succeeds, pack
  managed solution and import to UAT with `microsoft/powerplatform-actions` (`import-solution`,
  `publish-solution`), authenticating with an app registration stored in GitHub secrets
  `PP_APP_ID`, `PP_CLIENT_SECRET`, `PP_TENANT_ID`, `PP_UAT_URL`. Deployment settings file
  (`/solution/deploymentSettings.uat.json`) supplies environment variable values and connection
  references, with secrets injected from GitHub secrets at run time.

## R18. Browser walkthroughs and guide films (FR-017, BRD FR15)

- **Decision**: `/e2e` Playwright Test project (TypeScript, Chromium, `headless: false` always —
  owner rule from Rotaro). One spec file per chapter (`01-log-case` … `08-business-unit-access`)
  following the BRD demo script. Personas `agentA`, `agentB`, `supervisor` each get a saved
  Playwright `storageState` produced by a one-off `npm run login -- <persona>` where the owner
  signs in by hand (MFA-safe); state files live in `e2e/.auth/` (gitignored). The environment
  URL and app id come from `e2e/.env` (gitignored; `.env.example` committed). A small demo
  helper (written fresh for CaseDesk, modelled on Moniqora/Rotaro, nothing copied) adds: a
  caption band injected at the top of the page (`page.addInitScript` + `evaluate`), chapter and
  title cards (full-screen HTML overlay for N seconds), slowed actions when `DEMO=1`, and
  `soon()` waits. Model-driven app specifics: navigate by URL
  (`main.aspx?appid=…&pagetype=entityrecord&etn=cd_case`), locate controls by
  `data-id` attributes and accessible names, wait for `Xrm.Page`/form ready via
  `page.waitForFunction`, save with Ctrl+S, read notifications from the form notification bar.
- **Filming**: `recordVideo` per browser context at 1920×1080 (each persona switch is a new
  context → separate clip); `scripts/stitch.mjs` uses `ffmpeg-static` to trim idle lead-in, add
  chapter cards and concatenate into `e2e/demo-out/casedesk-demo.mp4` with a chapters file.
  Voice-over: `scripts/speech.mjs` calls Azure AI Speech (REST, neural voice) with the PC's
  `DEMO_SPEECH_KEY` / `DEMO_SPEECH_REGION` user environment variables, caches audio in
  `e2e/.cache/speech/`, and mixes it under each caption; no key → silent film.
- **Run modes**: `npm run walk` (plain headed test run), `npm run film` (DEMO=1, record,
  stitch, publish to `demo-out/`), `npm run film -- --chapter 06` for one chapter. Made on the
  PC on demand only; not part of the pipeline (needs interactive sign-in and a live Dev).
- **Data**: the walkthrough creates its own cases with a `[Walkthrough]` title prefix and
  depends on the seeder (SLA policies, accounts/contacts, BUs, supervisors team). Chapters 4
  (email) and 7 (CaseClosed) check outcomes through the case timeline and the stub's GET
  endpoint respectively, polling up to 90 s.
- **Alternatives**: EasyRepro (Microsoft's Selenium-based UI test framework for model-driven
  apps — older, Selenium, no film tooling); Power Apps Test Engine (YAML/Power Fx, good for
  canvas apps, limited for model-driven forms and no captioned films). Playwright matches the
  owner's other products and gives video recording out of the box.

## R17. Local toolchain

- **Decision**: Install `pac` as a .NET global tool (`Microsoft.PowerApps.CLI.Tool`). Existing:
  .NET SDK 10 (builds net462 via `Microsoft.NETFramework.ReferenceAssemblies` and net8.0),
  Node 24 / npm 11. Azure CLI and Functions Core Tools are not required to build or test, only
  to deploy Azure resources.
