<!--
Sync Impact Report
- Version change: 1.0.0 → 1.0.1 (PATCH: release branch renamed `main` → `master`; work happens on
  `develop`. No principle changed.)
- Earlier: (template) → 1.0.0
- Principles defined: I. Simplest Extension Point Wins; II. Server-Side Rules Are Authoritative;
  III. Fast, Idempotent, Traceable Plugins; IV. Test-First for Code Components;
  V. Secrets Never in Source; VI. One Solution, Managed Downstream
- Added sections: Platform & Technology Constraints; Development Workflow & Quality Gates; Governance
- Removed sections: none
- Templates requiring updates: none (plan/spec/tasks templates read this file at runtime)
- Deferred TODOs: none
-->

# CaseDesk Constitution

## Core Principles

### I. Simplest Extension Point Wins

Every requirement MUST be implemented with the simplest Power Platform extension point that
satisfies it, in this order of preference: platform configuration (columns, auto-number,
security roles, views, dashboards) → declarative logic (business rules) → low-code
(Power Automate) → pro-code (C# plugins, Custom APIs, PCF, Azure Functions).
Pro-code MUST be justified by a need low-code cannot meet (same-transaction guarantee,
server-side enforcement for all callers, custom rendering, reusable callable operation,
decoupled async integration). Each extension point type is used for a real reason, never only
for demonstration.

*Rationale*: CaseDesk exists to show judgement about Power Platform; the "why" for each
choice is part of the deliverable.

### II. Server-Side Rules Are Authoritative

Any business rule that must hold for the Web API, flows and the UI alike (due date
calculation, close/resolve guards, escalation) MUST be enforced on the server (plugin or Custom
API). Client script and form configuration MAY add convenience but MUST NOT be the only
enforcement of a rule. Violations MUST surface as `InvalidPluginExecutionException` with a
message an agent can act on.

*Rationale*: Data integrity cannot depend on which client wrote the record.

### III. Fast, Idempotent, Traceable Plugins

Synchronous plugins MUST complete in under 2 seconds and MUST NOT call external HTTP
endpoints. Anything slow or external MUST go asynchronous (Service Bus / webhook → Azure
Function). Every plugin and Custom API MUST be idempotent on retry and MUST log through
`ITracingService`. Azure Functions MUST log to Application Insights.

*Rationale*: Sync plugins run inside the user's transaction; slowness and side effects there
are user-visible failures.

### IV. Test-First for Code Components (NON-NEGOTIABLE)

All plugin and Custom API logic MUST have xUnit + FakeXrmEasy unit tests written before or
alongside the implementation, covering the happy path and each rule violation. PCF logic that
computes state (e.g. SLA colour) MUST be unit tested. CI MUST run tests before packing or
deploying; a failing test blocks deployment.

*Rationale*: ALM and testability are explicit PL-400 and interview topics and the main risk
reducer for server logic.

### V. Secrets Never in Source

Client secrets, endpoint keys and connection strings MUST live in Dataverse environment
variables (secret type backed by Key Vault where possible) or Azure Key Vault / GitHub
encrypted secrets. They MUST NOT appear in code, solution XML, flow definitions, test
fixtures or commit history. The repository is public.

*Rationale*: A public repo turns any committed secret into an incident.

### VI. One Solution, Managed Downstream

All customisation MUST live in a single solution with publisher prefix `cd_`. The source of
truth is the unpacked solution under `/solution`. Downstream environments (UAT, AppSource)
MUST receive the managed solution only, deployed by pipeline; no manual customisation in UAT.

*Rationale*: Reproducible deployments and clean ALM history.

## Platform & Technology Constraints

- Runs on Dataverse with a custom `cd_case` table; MUST NOT depend on Dynamics 365 Customer
  Service licensing (free developer plan + Power Apps per-user licence).
- Plugins: C# targeting .NET Framework 4.6.2, Microsoft.CrmSdk.CoreAssemblies, signed assembly.
- Tests: xUnit + FakeXrmEasy.
- PCF: TypeScript (React allowed), built with the `pac` CLI tooling.
- Integration: Azure Functions (.NET 8 isolated), Service Bus or webhook endpoint, Application
  Insights, Key Vault. Demo data seeding via .NET 8 console app on the Dataverse Web API.
- Scale target: 50 concurrent agents, 10,000 open cases using Dataverse defaults (no custom
  indexing).
- Repo layout: `/solution`, `/plugins`, `/pcf`, `/functions`, `/tests`, `/docs`, plus
  `/tools` for the seeding console app.

## Development Workflow & Quality Gates

- Work proceeds through the Spec Kit flow: specify → clarify → plan → tasks → implement.
- Every plan MUST include a Constitution Check against Principles I–VI.
- Work happens on `develop`; releases merge `develop` into `master`.
- GitHub Actions on push to `master`: build and test plugins → pack solution (managed) → import
  to UAT. Pull requests run build and tests only.
- A requirement is done when its acceptance step from the demo script passes in Dev, and the
  change is in the unpacked solution in source control.

## Governance

This constitution supersedes other practices for CaseDesk. Amendments require a written change
to this file with a Sync Impact Report, a version bump, and a review of open plans for impact.
Versioning: MAJOR for removed or redefined principles, MINOR for new principles or sections,
PATCH for wording. Every plan and code review MUST check compliance; any deviation MUST be
recorded in the plan's Complexity Tracking table with its justification.

**Version**: 1.0.1 | **Ratified**: 2026-11-08 | **Last Amended**: 2026-11-08
