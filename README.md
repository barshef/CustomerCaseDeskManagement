# CaseDesk: customer case management on Dynamics 365 CE / Dataverse

CaseDesk lets a small service team log, assign, work and close customer cases against an SLA.
The customer is emailed at each step, and an external system is told when a case closes. It
runs on a custom `cd_case` table, so no Dynamics 365 Customer Service licence is needed.

It uses every Power Platform extension point a PL-400 interviewer asks about, each for a real
reason: auto-number, business rules, cloud flows and approvals, C# plugins, a plugin-backed
Custom API with a ribbon button, a PCF React control, Azure Functions over **Service Bus or
webhook** (switchable by an environment variable), security roles and business units, and
GitHub Actions ALM.

- **New to Dynamics 365? Start with the [beginner guide](docs/beginner-guide.md)**: concepts, opening
  the code in Visual Studio (`CaseDesk.slnx`), and a step-by-step deploy.
- Business requirements: [BRD](./BRD%20—%20CaseDesk%20Customer%20Case%20Management%20on%20Dynamics%20365%20CE.md)
- Design (spec-kit): [`specs/001-casedesk-case-management`](specs/001-casedesk-case-management/spec.md)
- Architecture: [docs/architecture.md](docs/architecture.md) · Why each extension point:
  [docs/interview-notes.md](docs/interview-notes.md) · ALM: [docs/alm.md](docs/alm.md)

## Repository layout

| Path | Contents |
| --- | --- |
| `solution/` | Power Platform solution project `CaseDesk.cdsproj`; unpacked solution under `src/` (tables, choices, forms, business rules, flows, roles, Custom API, web resources, env vars, app); `deploymentSettings.uat.json` |
| `plugins/` | `CaseDesk.Plugins` (C#, .NET Framework 4.6.2, signed): pre-create, pre-update, async CaseClosed publisher, Escalate Custom API; `CaseDesk.Plugins.slnx` |
| `pcf/` | `SlaBadge` PCF field control (TypeScript + React) with Jest tests |
| `functions/` | Azure Functions (.NET 8 isolated): CaseClosed from Service Bus / webhook, external stub; `infra/main.bicep`; `CaseDesk.Functions.slnx` |
| `tests/` | `CaseDesk.Plugins.Tests` (xUnit + FakeXrmEasy, net462), `CaseDesk.Functions.Tests` (xUnit, net8) |
| `tools/` | `CaseDesk.Seeder`: .NET 8 console app for idempotent demo data, business units and the supervisor team ([README](tools/CaseDesk.Seeder/README.md)) |
| `e2e/` | Playwright walkthroughs of demo steps 1–8 (always headed) and guide films; films in `e2e/demo-out/` |
| `docs/` | Architecture, interview notes, ALM, toolchain, endpoint registration, business rules, walkthrough films |
| `specs/` | Spec-kit feature folder: spec, plan, research, data model, contracts, tasks, quickstart |
| `.specify/` | Spec-kit memory (`constitution.md`) and templates |
| `.github/workflows/` | `ci.yml` (build, test, pack) and `deploy-uat.yml` (managed import to UAT) |

## Prerequisites

- Windows with .NET SDK 8 or later (built with SDK 10.0.403) and .NET Framework 4.6.2+ (4.8
  ships with Windows). The plugins target net462; everything else targets net8.0.
- Node.js 20 or later (built with Node 24.19 / npm 11.17).
- Power Platform CLI: `dotnet tool install --global Microsoft.PowerApps.CLI.Tool`.
- Deploying only: two Dataverse environments (Dev, UAT), an Azure subscription, Azure CLI,
  Azure Functions Core Tools v4.

See [docs/toolchain.md](docs/toolchain.md) for exact versions.

## Build and test

```powershell
dotnet test tests/CaseDesk.Plugins.Tests/CaseDesk.Plugins.Tests.csproj -c Release   # plugins (net462)
dotnet test functions/CaseDesk.Functions.slnx -c Release                             # Functions (net8)
cd pcf/SlaBadge; npm ci; npm test; npm run build; cd ../..                           # PCF
dotnet build solution/CaseDesk.cdsproj -c Release                                    # packs the solution
dotnet build tools/CaseDesk.Seeder                                                   # seeder
```

The solution build writes `solution/bin/Release/CaseDesk.zip` (unmanaged) and
`CaseDesk_managed.zip`.

## Deploy

- **Dev (first time)**: import the unmanaged zip with `pac solution import`, run the seeder,
  deploy the Azure side, register the two service endpoints, set environment variables, turn
  on flows, then run `pac solution sync`. Full steps are in
  [quickstart.md](specs/001-casedesk-case-management/quickstart.md) §2.
- **UAT**: merge to `main`. **Deploy UAT** runs CI and then imports the managed solution. Set
  up the secrets, variables and application user in [docs/alm.md](docs/alm.md).
- **CaseClosed endpoints and the transport switch**:
  [docs/caseclosed-endpoints.md](docs/caseclosed-endpoints.md) and quickstart §3.
- **Demo data**: `dotnet run --project tools/CaseDesk.Seeder -- --connection "<connection string>"`
  (add `--sla-demo` for the amber/red badge demo).

- **Walkthroughs and guide films** — headed Playwright run of demo steps 1–8 as three personas,
  filmed with captions and voice-over: see [docs/walkthrough-films.md](docs/walkthrough-films.md)
  (`cd e2e; npm run walk` / `npm run film`; films in `e2e/demo-out/`). Needs Dev deployed first.

Acceptance is the BRD's demo script (quickstart §4), steps 1–10.

## How this was designed (spec-kit)

CaseDesk was built with the [Spec Kit](https://github.com/github/spec-kit) flow: specify →
clarify → plan → tasks → implement. The source of the design is
[`specs/001-casedesk-case-management`](specs/001-casedesk-case-management/). `spec.md` holds
the requirements and acceptance, `plan.md` the technical plan and Constitution Check,
`research.md` the decisions and rejected alternatives, `data-model.md` the tables and option
values, `contracts/` the interfaces, and `tasks.md` the build order. The principles every
change is checked against are in
[`.specify/memory/constitution.md`](.specify/memory/constitution.md).

## Security

The repository is public. No secrets are kept in code, solution files or tests. Pipeline
credentials are GitHub secrets, Azure secrets are in Key Vault, and endpoint keys are entered
on the Dataverse service endpoint registration in each environment.
