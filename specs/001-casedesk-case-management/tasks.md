---

description: "Task list for CaseDesk — Customer Case Management"
---

# Tasks: CaseDesk — Customer Case Management

**Input**: Design documents from `/specs/001-casedesk-case-management/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Required — spec FR-014 and Constitution IV (test-first for plugins, Custom API,
Function handler and PCF logic). Test tasks precede implementation in each story.

**Organization**: Tasks are grouped by user story (US1–US10 from spec.md).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: User story label (US1…US10)

## Path Conventions

`solution/src/` (unpacked solution), `plugins/CaseDesk.Plugins/`, `tests/CaseDesk.Plugins.Tests/`,
`pcf/SlaBadge/`, `functions/CaseDesk.Functions/`, `tests/CaseDesk.Functions.Tests/`,
`tools/CaseDesk.Seeder/`, `.github/workflows/`, `docs/`.

---

## Phase 1: Setup (Shared Infrastructure)

- [X] T001 Install Power Platform CLI as a .NET global tool (`Microsoft.PowerApps.CLI.Tool`) and record versions in docs/toolchain.md
- [X] T002 Create repo layout `/solution /plugins /pcf /functions /tests /tools /docs` and root README.md describing each folder
- [X] T003 [P] Create plugin library plugins/CaseDesk.Plugins/CaseDesk.Plugins.csproj (net462, Microsoft.CrmSdk.CoreAssemblies 9.0.2.*, Microsoft.NETFramework.ReferenceAssemblies, SignAssembly with plugins/CaseDesk.Plugins/CaseDesk.snk) and plugins/CaseDesk.Plugins.sln
- [X] T004 [P] Create test project tests/CaseDesk.Plugins.Tests/CaseDesk.Plugins.Tests.csproj (net462, xUnit, FakeXrmEasy.9 1.58.1) referenced from plugins/CaseDesk.Plugins.sln
- [X] T005 [P] Create Functions project functions/CaseDesk.Functions/CaseDesk.Functions.csproj (net8.0 isolated worker) + tests/CaseDesk.Functions.Tests (xUnit) + functions/CaseDesk.Functions.sln
- [X] T006 [P] Create solution project with `pac solution init --publisher-name CaseDesk --publisher-prefix cd` in solution/ (CaseDesk.cdsproj, src/Other/Solution.xml) and set customization option value prefix 41720
- [X] T007 [P] Add .editorconfig (Directory.Build.props with warnings-as-errors deferred: projects already build with 0 warnings)

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T008 [P] Global choices in solution/src/OptionSets/: cd_casecategory (Billing 417200000 · Delivery 417200001 · Product 417200002 · Refund 417200003 · Other 417200004), cd_casepriority (Low 417200000 · Medium 417200001 · High 417200002 · Critical 417200003), cd_notetype, cd_refunddecision
- [X] T009 Case table solution/src/Entities/cd_case/Entity.xml with all columns from data-model.md (cd_casenumber auto-number `CD-{SEQNUM:5}`, cd_name Text (200) required, cd_description Multiline (4000), cd_category default Other, cd_priority default Medium, cd_accountid, cd_contactid, cd_slapolicyid, cd_duedate, cd_warningon, cd_slawarningsent default No, cd_resolvedon, cd_closedon, cd_resolutionsummary Multiline (2000), cd_refundamount Currency, cd_customerreplypending default No) and status reasons (New 1, Assigned 417200001, In Progress 417200002, Awaiting Customer 417200003, Awaiting Approval 417200004, Resolved 417200005 | Closed 2, Cancelled 417200006)
- [X] T010 SLA Policy table solution/src/Entities/cd_slapolicy/Entity.xml (Organization owned; cd_name Text (100), cd_priority, cd_resolutionhours Whole number 1–8760, cd_warninghours Whole number 0–8760) with alternate key `cd_slapolicy_priority_key` on cd_priority
- [X] T011 Relationships solution/src/Other/Relationships/*.xml: cd_account_cd_case (restrict delete), cd_contact_cd_case (remove link), cd_slapolicy_cd_case (restrict delete)
- [X] T012 [P] Environment variable definitions in solution/src/environmentvariabledefinitions/ (cd_CaseClosedTransport=ServiceBus, cd_CaseClosedServiceBusEndpointName, cd_CaseClosedWebhookEndpointName, cd_SupervisorTeamName, cd_SenderQueueEmail, cd_AutoCloseDays=5)
- [X] T013 [P] Plugin infrastructure plugins/CaseDesk.Plugins/PluginBase.cs (LocalPluginContext with tracing, user/system services, timing trace) and Domain/CaseDeskException.cs (coded messages CD001–CD007)
- [X] T014 [P] Domain constants plugins/CaseDesk.Plugins/Domain/Choices.cs and Domain/CaseStatus.cs (option values from data-model.md) and Domain/EnvironmentVariables.cs (read value with default fallback)
- [X] T015 [P] Test helpers tests/CaseDesk.Plugins.Tests/TestData.cs (SLA policies seed, case builder)
- [X] T016 Model-driven app shell: solution/src/AppModules/cd_casedesk/AppModule.xml + AppModuleSiteMaps/cd_casedesk/AppModuleSiteMap.xml (Cases, SLA Policies, Accounts, Contacts areas)

**Checkpoint**: Foundation ready

---

## Phase 3: User Story 1 - Log a case with an automatic SLA due date (P1) 🎯 MVP

**Goal**: Case number + due date populate on save; required fields enforced.

**Independent Test**: Create a High Billing case → CD-NNNNN and due = created + 8h.

### Tests for User Story 1 ⚠️ write first

- [X] T017 [P] [US1] SlaCalculator tests (due/warning maths, 24×7, warning ≥ resolution clamps to due) in tests/CaseDesk.Plugins.Tests/SlaCalculatorTests.cs
- [X] T018 [P] [US1] CasePreCreatePlugin tests (sets due/warning/policy; CD001 no title; CD002 no customer; CD003 no policy) in tests/CaseDesk.Plugins.Tests/CasePreCreatePluginTests.cs

### Implementation for User Story 1

- [X] T019 [P] [US1] SlaCalculator in plugins/CaseDesk.Plugins/Domain/SlaCalculator.cs
- [X] T020 [US1] CasePreCreatePlugin (PreOperation Create, contract S1) in plugins/CaseDesk.Plugins/CasePreCreatePlugin.cs
- [X] T021 [US1] Case main form solution/src/Entities/cd_case/FormXml/main/{case-main}.xml (General tab: number, title, customer, requester, category, priority, refund amount, description; SLA section: due date, warning on, SLA policy; Resolution section) + Active Cases / My Open Cases views in SavedQueries/
- [X] T022 [P] [US1] Business rules in solution/src/Workflows/: CaseRefundAmountVisibility (form scope: show + require when Category = Refund, else hide + clear) and CaseRefundAmountRequired (entity scope: error when Refund and amount empty); SlaPolicyWarningLessThanResolution (entity scope)
- [X] T023 [P] [US1] SLA Policy main form and views in solution/src/Entities/cd_slapolicy/
- [X] T024 [US1] Plugin assembly + step S1 registration in solution/src/PluginAssemblies/ and solution/src/SdkMessageProcessingSteps/

**Checkpoint**: US1 deliverable (MVP part 1)

---

## Phase 4: User Story 2 - Work a case through its lifecycle with notes (P1)

**Goal**: Enforced lifecycle; open-note guard; cancel; cascade delete of notes.

**Independent Test**: Open note blocks Resolve; closed note allows it.

### Tests for User Story 2 ⚠️ write first

- [X] T025 [P] [US2] CaseStateMachine tests (every allowed + rejected transition in data-model.md) in tests/CaseDesk.Plugins.Tests/CaseStateMachineTests.cs
- [X] T026 [P] [US2] CasePreUpdatePlugin tests (CD004 open notes, CD005 no summary, CD006 illegal transition, resolvedon set/cleared, closedon set, priority change recalculates due and resets warning flag) in tests/CaseDesk.Plugins.Tests/CasePreUpdatePluginTests.cs

### Implementation for User Story 2

- [X] T027 [P] [US2] Case Note table solution/src/Entities/cd_casenote/Entity.xml (cd_name Text (200), cd_notetext Multiline (10000) required, cd_notetype default Internal, cd_isopen default No, cd_caseid required) + relationship cd_case_cd_casenote (parental, cascade all) + form/views
- [X] T028 [P] [US2] CaseStateMachine in plugins/CaseDesk.Plugins/Domain/CaseStateMachine.cs
- [X] T029 [US2] CasePreUpdatePlugin (PreOperation Update, filtering statecode/statuscode/cd_priority/cd_category, PreImage; contract S2) in plugins/CaseDesk.Plugins/CasePreUpdatePlugin.cs
- [X] T030 [US2] Notes sub-grid on Case form; step S2 registration with pre-image in solution/src/SdkMessageProcessingSteps/

**Checkpoint**: US1 + US2 = MVP

---

## Phase 5: User Story 3 - Customer emailed on creation and closure (P2)

**Independent Test**: Creation email within 1 minute, visible on the timeline.

- [X] T031 [P] [US3] Connection reference cd_sharedcommondataserviceforapps in solution/src/connectionreferences (or Customizations.xml)
- [X] T032 [US3] Cloud flow "CaseDesk – Email customer on create" in solution/src/Workflows/CaseDesk-EmailCustomerOnCreate-*.json (+ .json.data.xml): add Email row regarding case → SendEmail; if no contact/email → add internal Case Note explaining why
- [X] T033 [US3] Cloud flow "CaseDesk – Email customer on close" in solution/src/Workflows/CaseDesk-EmailCustomerOnClose-*.json (+ data xml), trigger statuscode = Closed (2), includes resolution summary

---

## Phase 6: User Story 4 - Refund cases need supervisor approval (P2)

**Independent Test**: Refund → Awaiting Approval → approve → Resolved.

### Tests ⚠️ write first

- [X] T034 [P] [US4] Refund routing tests (Resolve → rewritten to Awaiting Approval; Resolved allowed with latest Approved approval; CD006 when Awaiting Approval → Closed; CD007 category change while pending) in tests/CaseDesk.Plugins.Tests/RefundRoutingTests.cs

### Implementation

- [X] T035 [P] [US4] Refund Approval table solution/src/Entities/cd_refundapproval/Entity.xml (cd_name Text (200), cd_caseid required, cd_requestedbyid required, cd_approverid, cd_decision default Pending, cd_decisiondate, cd_comment Multiline (2000)) + relationship cd_case_cd_refundapproval (parental) + form/views + sub-grid on Case form
- [X] T036 [US4] Refund routing in plugins/CaseDesk.Plugins/CasePreUpdatePlugin.cs (latest approval lookup)
- [X] T037 [US4] Cloud flow "CaseDesk – Refund approval" in solution/src/Workflows/CaseDesk-RefundApproval-*.json: trigger statuscode = Awaiting Approval → create Refund Approval (Pending) → list supervisors in owner BU (fallback all) → Start and wait for approval (First to respond) → update approval → set case Resolved or In Progress, and add an internal Case Note with the decision comment

---

## Phase 7: User Story 5 - SLA visibility, warnings and breach digest (P2)

**Independent Test**: Badge amber in warning window, red after due.

### Tests ⚠️ write first

- [X] T038 [P] [US5] computeSlaState Jest tests (green/amber/red/met/breached, formatting) in pcf/SlaBadge/SlaBadge/__tests__/slaState.test.ts

### Implementation

- [X] T039 [US5] Scaffold PCF with `pac pcf init --namespace CaseDesk --name SlaBadge --template field --framework react` in pcf/SlaBadge/; manifest binds dueDate (cd_duedate), warningOn (cd_warningon), resolvedOn (cd_resolvedon), closedOn (cd_closedon) — all bound DateAndTime
- [X] T040 [US5] computeSlaState in pcf/SlaBadge/SlaBadge/slaState.ts and React component pcf/SlaBadge/SlaBadge/SlaBadgeView.tsx (30 s tick, Fluent UI v9 Badge, accessible label)
- [X] T041 [US5] Control wiring pcf/SlaBadge/SlaBadge/index.ts; add PCF project reference to solution/CaseDesk.cdsproj; bind control to cd_duedate on Case form
- [X] T042 [P] [US5] Cloud flow "CaseDesk – SLA warning" (recurrence 15 min) in solution/src/Workflows/: cd_warningon <= utcNow and cd_duedate > utcNow and cd_slawarningsent = false → appnotification + email owner → set flag
- [X] T043 [P] [US5] Cloud flow "CaseDesk – Daily breach digest" (07:00) in solution/src/Workflows/: breached this week → HTML table email to each Supervisor role member

---

## Phase 8: User Story 6 - Escalate a case in one action (P3)

**Independent Test**: Escalate → Critical + team owner + note, one call.

### Tests ⚠️ write first

- [X] T044 [P] [US6] EscalateCasePlugin tests (happy path, already escalated → Escalated=false no writes, inactive case rejected, missing team rejected, no write access rejected) in tests/CaseDesk.Plugins.Tests/EscalateCasePluginTests.cs

### Implementation

- [X] T045 [US6] EscalateCasePlugin (contract escalate-case.md) in plugins/CaseDesk.Plugins/EscalateCasePlugin.cs
- [X] T046 [US6] Custom API definition solution/src/customapis/cd_EscalateCase/ (customapi.xml, request parameter Reason, response properties Escalated, Message) bound to cd_case with plugin type reference
- [X] T047 [P] [US6] Web resources solution/src/WebResources/cd_/scripts/case.ribbon.js and cd_/html/escalate.html (reason dialog, Xrm.WebApi.online.execute, form notification)
- [X] T048 [US6] Ribbon button "Escalate" in solution/src/Entities/cd_case/RibbonDiff.xml (form command bar, enable rule: active and not Resolved)

---

## Phase 9: User Story 7 - Closed cases announced to an external system (P3)

**Independent Test**: Close a case → stub logs CaseClosed within 1 minute.

### Tests ⚠️ write first

- [X] T049 [P] [US7] PublishCaseClosedPlugin tests (only fires on → Closed; transport ServiceBus/Webhook/None selects endpoint by name; missing endpoint throws for retry; message id stable) in tests/CaseDesk.Plugins.Tests/PublishCaseClosedPluginTests.cs
- [X] T050 [P] [US7] CaseClosedMapper + handler tests (RemoteExecutionContext JSON fixture → CaseClosed v1 matches schema, slaMet, non-Closed context ignored) in tests/CaseDesk.Functions.Tests/CaseClosedMapperTests.cs
- [X] T051 [P] [US7] ExternalStub de-dup tests in tests/CaseDesk.Functions.Tests/ExternalStubTests.cs

### Implementation

- [X] T052 [US7] PublishCaseClosedPlugin (async PostOperation, contract S3) in plugins/CaseDesk.Plugins/PublishCaseClosedPlugin.cs + step registration with images
- [X] T053 [US7] Functions: CaseClosedMapper.cs, CaseClosedHandler.cs, CaseClosedFromServiceBus.cs, CaseClosedFromWebhook.cs, ExternalStub.cs, Program.cs in functions/CaseDesk.Functions/
- [X] T054 [P] [US7] Bicep infra functions/infra/main.bicep (Function app, storage, Service Bus Basic + queue caseclosed, Key Vault, App Insights)
- [X] T055 [P] [US7] Endpoint registration guide docs/caseclosed-endpoints.md (ServiceBus + Webhook, switching transport, no-code variant)

---

## Phase 10: User Story 8 - Resolved cases auto-close (P3)

- [X] T056 [US8] Cloud flow "CaseDesk – Auto-close resolved cases" (daily 02:00) in solution/src/Workflows/: Resolved and cd_resolvedon <= utcNow − cd_AutoCloseDays; incoming Email regarding case after cd_resolvedon → set cd_customerreplypending = Yes + notify owner; else set Closed (2)

---

## Phase 11: User Story 9 - Dashboards and access by business unit (P3)

- [X] T057 [P] [US9] Security roles solution/src/Roles/CaseDesk Agent.xml and CaseDesk Supervisor.xml (privileges per research R13)
- [X] T058 [P] [US9] Charts (Open cases by priority, Breaches by owner, Breaches by priority) in solution/src/Entities/cd_case/Visualizations/ and views "My Open Cases", "SLA Breaches This Week"
- [X] T059 [US9] Dashboards "CaseDesk Agent" and "CaseDesk Supervisor" in solution/src/Dashboards/ and add to app module

---

## Phase 12: User Story 10 - Repeatable deployment and demo data (P3)

- [X] T060 [P] [US10] Seeder tools/CaseDesk.Seeder (BUs A/B, CaseDesk Supervisors team + role, SLA policies upsert by key, accounts/contacts, sample cases, `--sla-demo` option) 
- [X] T061 [P] [US10] CI workflow .github/workflows/ci.yml (plugins test → functions test → PCF test/build → solution build)
- [X] T062 [US10] Deploy workflow .github/workflows/deploy-uat.yml (on push main; calls ci.yml as a reusable workflow, deploy job needs ci: pack managed, import to UAT with deploymentSettings, publish) + solution/deploymentSettings.uat.json

---

## Phase 13: User Story 11 - Browser walkthroughs and guide films (P3)

**Goal**: Headed Playwright walkthrough of demo steps 1–8 as three personas, optionally filmed
with captions, chapter cards and voice-over (research R18).

**Independent Test**: `npm run walk` passes 8 chapters against Dev; `npm run film` writes
e2e/demo-out/casedesk-demo.mp4.

- [X] T067 [US11] Scaffold e2e/ Playwright project (package.json, playwright.config.ts with headless false, Chromium, 1920×1080, 60 s action timeout; .env.example with CASEDESK_URL, CASEDESK_APP_ID, STUB_URL; e2e/.gitignore for .auth/, .env, .cache/, test-results/)
- [X] T068 [US11] Persona sign-in script e2e/scripts/login.mjs (opens headed browser, owner signs in, saves e2e/.auth/<persona>.json) and persona fixtures in e2e/demo/personas.ts
- [X] T069 [P] [US11] Demo helper e2e/demo/ (caption band, title/chapter cards, slow-mo when DEMO=1, soon(), model-driven helpers: openNewCase, openRecord, setField by data-id, save, setStatus, readFormNotification)
- [X] T070 [US11] Chapters e2e/chapters/01-log-case … 08-business-unit-access .spec.ts following quickstart.md §4 steps 1–8 with demo-quality names and captions
- [X] T071 [P] [US11] Film pipeline e2e/scripts/stitch.mjs (ffmpeg-static: trim idle, chapter cards, concat, chapters metadata) and e2e/scripts/publish.mjs (copy to e2e/demo-out/)
- [X] T072 [P] [US11] Voice-over e2e/scripts/speech.mjs (Azure AI Speech REST with DEMO_SPEECH_KEY/DEMO_SPEECH_REGION, cache, silent fallback)
- [ ] T073 [US11] docs/walkthrough-films.md (setup, personas, commands, lessons) and README link; first film made after Dev deploy

---

## Phase 14: Polish & Cross-Cutting Concerns

- [X] T063 [P] docs/architecture.md (component diagram, sequence for CaseClosed both transports) and docs/interview-notes.md (why each extension point)
- [X] T064 Run full local build: `dotnet test` (plugins, functions), `npm test` + `npm run build` (PCF), `dotnet build solution/CaseDesk.cdsproj` (pack)
- [X] T065 Security pass: grep for secrets, confirm local.settings.json ignored, deployment settings placeholders only
- [X] T066 Update quickstart.md with any deviations found during implementation

---

## Dependencies & Execution Order

- Setup (P1) → Foundational (P2) → user stories.
- US1 → US2 (pre-update plugin shares Case form/plugin assembly). US4 extends the US2 plugin.
- US5 PCF, US6, US7, US8, US9, US10 depend only on Foundational + US1 (Case form) and can run in
  parallel.
- Polish last.

## Parallel Example: User Story 7

```text
Task: "PublishCaseClosedPlugin tests in tests/CaseDesk.Plugins.Tests/PublishCaseClosedPluginTests.cs"
Task: "CaseClosedMapper tests in tests/CaseDesk.Functions.Tests/CaseClosedMapperTests.cs"
Task: "Bicep infra in functions/infra/main.bicep"
```

## Implementation Strategy

1. MVP: Setup + Foundational + US1 + US2 → import to Dev, run demo steps 1–2.
2. Add US3, US4, US5 (P2) → demo steps 3–5.
3. Add US6–US10 (P3) → demo steps 6–9.

## Notes

- Solution XML is hand-authored; first Dev import + `pac solution sync` is its real validation.
- Commit after each phase.
