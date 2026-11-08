# Implementation Plan: CaseDesk — Customer Case Management

**Branch**: `001-casedesk-case-management` | **Date**: 2026-11-08 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-casedesk-case-management/spec.md`

## Summary

CaseDesk is a model-driven app on Dataverse with four custom tables (Case, Case Note, SLA
Policy, Refund Approval). Each BRD requirement is implemented with the simplest suitable
extension point: auto-number and alternate keys (platform), business rules (declarative),
cloud flows (emails, refund approval, SLA warning, breach digest, auto-close), C# plugins (due
date, lifecycle guard, refund routing, CaseClosed publishing), a plugin-backed Custom API with a
JavaScript ribbon button (Escalate), a PCF React control (SLA badge), and Azure Functions
receiving the CaseClosed event over **Service Bus (default) or webhook**, switchable by an
environment variable. GitHub Actions builds, tests and deploys the managed solution to UAT.
This phase delivers complete source and a green local build; importing into Dev/UAT is done by
the user or the pipeline (see [quickstart.md](quickstart.md)).

## Technical Context

**Language/Version**: C# (plugins: .NET Framework 4.6.2; Functions/seeder: .NET 8), TypeScript
5 (PCF), JavaScript ES2020 (ribbon web resource), Bicep, YAML (GitHub Actions)

**Primary Dependencies**: Microsoft.CrmSdk.CoreAssemblies 9.0.2.x; FakeXrmEasy.9 1.58.1;
xUnit; Microsoft.Azure.Functions.Worker (+ ServiceBus, Http extensions);
Microsoft.Extensions.Http.Resilience; Azure.Data.Tables; Microsoft.PowerPlatform.Dataverse.Client;
PCF tooling (`pcf-scripts`, React 16 platform library, Fluent UI v9); Jest;
microsoft/powerplatform-actions

**Storage**: Dataverse (tables in [data-model.md](data-model.md)); Azure Table Storage (stub
de-duplication)

**Testing**: xUnit + FakeXrmEasy (plugins), xUnit (Functions), Jest (PCF logic); Playwright
headed walkthroughs of the BRD demo script against Dev, optionally filmed (research R18); BRD
demo script as acceptance ([quickstart.md](quickstart.md) §4)

**Target Platform**: Dataverse online (developer plan), model-driven app (Unified Interface),
Azure Functions (Consumption/Flex), GitHub Actions (windows-latest)

**Project Type**: Power Platform solution + plugin library + PCF control + Azure Functions app +
console tool + CI pipeline

**Performance Goals**: Sync plugins < 2 s (target < 200 ms, ≤ 3 Dataverse calls each);
creation email and CaseClosed delivery < 1 minute

**Constraints**: No sync external HTTP; no secrets in source; single `cd_` solution; managed
downstream; no Customer Service licence dependency

**Scale/Scope**: 50 concurrent agents, 10,000 open cases, 4 tables, 4 plugin steps, 1 Custom
API, 6 cloud flows, 1 PCF control, 3 Functions, 2 security roles, 2 dashboards

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Result |
| --- | --- | --- |
| I. Simplest extension point | Case number = auto-number; one-policy-per-priority = alternate key; refund field = business rules; emails/approvals/time-based = flows; pro-code only for same-transaction (due date), all-callers guard (lifecycle/refund), custom render (PCF), reusable op (Custom API), async decoupling (Functions). Two transports are a user-requested learning feature, both behind one switch. | PASS |
| II. Server-side authoritative | Due date, lifecycle, open-note guard, refund routing, Title/Customer, Refund Amount (entity-scope rule) and escalation all enforced server side. Ribbon/form only add UX. | PASS |
| III. Fast, idempotent, traceable | Sync plugins do bounded queries only; CaseClosed publish is an async step; all plugins trace via `ITracingService`; stable message id; escalation early-return; Functions log to App Insights. | PASS |
| IV. Test-first | Pure logic classes (`CaseStateMachine`, `SlaCalculator`, `CaseClosedMapper`, `computeSlaState`) plus FakeXrmEasy tests for every plugin, written before plugin code in tasks; CI runs tests before packing. | PASS |
| V. Secrets never in source | SAS key and Function keys entered on service endpoint registrations / Key Vault; pipeline credentials in GitHub secrets; deployment settings file has placeholders only. `.snk` is a signing identity, not a secret (documented). | PASS |
| VI. One solution, managed downstream | Single `CaseDesk` solution, `cd_` prefix, cdsproj builds managed zip; UAT import only by pipeline. BUs, team and service endpoints are environment data (seeder/PRT), not customisation. | PASS |

**Post-design re-check (after Phase 1)**: PASS — the design added `cd_warningon`,
`cd_closedon`, `cd_slawarningsent`, `cd_customerreplypending` columns and six environment
variables; none breaks a principle. The SYSTEM-context assignment in Escalate is guarded by a
caller access check and recorded in the note.

## Project Structure

### Documentation (this feature)

```text
specs/001-casedesk-case-management/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── escalate-case.md
│   ├── caseclosed-event.schema.json
│   ├── azure-functions.md
│   └── plugin-steps.md
├── checklists/requirements.md
└── tasks.md                 # /speckit-tasks
```

### Source Code (repository root)

```text
solution/                         # Power Platform solution project (pac solution init)
├── CaseDesk.cdsproj              # references plugins + pcf; builds unmanaged & managed zips
├── deploymentSettings.uat.json   # env var values / connection refs (no secrets)
└── src/
    ├── Other/ (Solution.xml, Customizations.xml, Relationships.xml)
    ├── Entities/ (cd_case, cd_casenote, cd_slapolicy, cd_refundapproval, account, contact)
    ├── OptionSets/               # global choices
    ├── Roles/                    # CaseDesk Agent, CaseDesk Supervisor
    ├── Workflows/                # business rules (.xaml) + cloud flows (.json)
    ├── WebResources/cd_/         # scripts/case.ribbon.js, html/escalate.html, icons
    ├── environmentvariabledefinitions/
    ├── customapis/cd_EscalateCase/
    ├── PluginAssemblies/ + SdkMessageProcessingSteps/
    ├── AppModules/ + AppModuleSiteMaps/
    └── (dashboards under Entities/*/FormXml or Dashboards/)
plugins/
├── CaseDesk.Plugins.sln
├── CaseDesk.Plugins/             # net462, signed
│   ├── Domain/ (CaseStateMachine, SlaCalculator, CaseStatus, Choices, CaseDeskException)
│   ├── PluginBase.cs
│   ├── CasePreCreatePlugin.cs, CasePreUpdatePlugin.cs
│   ├── PublishCaseClosedPlugin.cs, EscalateCasePlugin.cs
│   └── CaseDesk.snk
└── ../tests/CaseDesk.Plugins.Tests/   # xUnit + FakeXrmEasy (net462)
pcf/SlaBadge/                     # pac pcf init --framework react; src/slaState.ts + Jest tests
functions/
├── CaseDesk.Functions.sln
├── CaseDesk.Functions/           # net8 isolated: ServiceBus, Webhook, ExternalStub, CaseClosedHandler
├── infra/main.bicep
└── ../tests/CaseDesk.Functions.Tests/
tools/CaseDesk.Seeder/            # net8 console, idempotent demo data + BUs + team
e2e/                              # Playwright walkthroughs (headed) + guide films (research R18)
├── playwright.config.ts, .env.example, .auth/ (gitignored)
├── demo/ (caption band, chapter/title cards, soon(), persona contexts)
├── chapters/01-log-case.spec.ts … 08-business-unit-access.spec.ts
├── scripts/ (login.mjs, stitch.mjs, speech.mjs, publish.mjs)
└── demo-out/                     # committed films
docs/                             # architecture.md, interview-notes.md (why each extension point)
.github/workflows/ci.yml, deploy-uat.yml
```

**Structure Decision**: Follows the BRD repo layout (`/solution`, `/plugins`, `/pcf`,
`/functions`, `/tests`, `/docs`) plus `/tools` for the seeder (constitution constraint).
Test projects live under `/tests` and are referenced from the plugin and Functions solutions.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
| --- | --- | --- |
| Two CaseClosed transports (Service Bus + webhook) | Explicit user request for learning; default Service Bus | Single transport would not meet the user's learning goal; both share one handler, so the extra cost is one Function and one endpoint registration |
| Async plugin for publishing instead of a direct endpoint step | Makes the transport a runtime switch via an environment variable | Direct step needs re-registration to switch; documented as the no-code variant |
