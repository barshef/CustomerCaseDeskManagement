# CaseDesk.Seeder

.NET 8 console app that puts CaseDesk demo data and environment data into a Dataverse
environment through the Dataverse SDK (`Microsoft.PowerPlatform.Dataverse.Client`). It is
also the BRD's "inbound integration" example: an external .NET client talking to Dataverse.

Run it **after** the CaseDesk solution is imported (it needs the `cd_` tables, the alternate
key and the `CaseDesk Supervisor` role).

## What it does

Every step reads first and only writes what is missing or different, so you can run it as often
as you like. Re-runs print `= unchanged` and create no duplicates.

| Step | Records | How it matches existing rows |
| --- | --- | --- |
| a | Business units `CaseDesk BU A`, `CaseDesk BU B` under the root BU | `name` |
| b | Owner team `CaseDesk Supervisors` in the root BU, with security role `CaseDesk Supervisor` (the root BU's copy) | team `name` + BU; `teamroles` row |
| c | SLA policies Critical 4h/1h, High 8h/2h, Medium 24h/2h, Low 72h/2h | `UpsertRequest` on alternate key `cd_slapolicy_priority_key` (`cd_priority`) |
| d | Accounts `CD-ACC-001`..`005`, six contacts (`@example.com`) | `accountnumber` / `emailaddress1` (not keys by default, so query then create or update) |
| e | Seven sample cases titled `[Seed] ...` across all categories and priorities, one Refund with `cd_refundamount` = 49.99 and one with no requester | Created only when **no** `[Seed]` case exists |
| f | `--sla-demo`: the newest open seed case gets due in 90 min / warning 30 min ago (amber); the next one is due 1 h ago (red) | Newest open `[Seed]` cases |

Sample cases do **not** set Due Date, Warning On or SLA Policy. The `CasePreCreatePlugin`
(FR3) sets them in the same transaction, and the seeder prints each case's due date so you can
see the plugin worked. A case printed with "no due date" means plugin step S1 is not registered.

Re-running step (c) puts SLA hours back to the seed values if a supervisor changed them.

### About `--sla-demo`

This option writes `cd_duedate` and `cd_warningon` directly. That only works because the
pre-update plugin step (S2) filters on `statecode`, `statuscode`, `cd_priority` and
`cd_category`, not on the date columns. That gap is intended for the demo. A priority change on
the case recalculates both dates from the SLA policy again. It also resets `cd_slawarningsent`
so the SLA warning flow (FR7) fires on its next run.

## Usage

```powershell
# Interactive sign-in (dev) — uses the well-known Dataverse sample app id
$env:CASEDESK_DATAVERSE_CONNECTION = "AuthType=OAuth;Url=https://<org>.crm.dynamics.com;LoginPrompt=Auto;AppId=51f81489-12ee-4a9e-aaae-a2591f45987d;RedirectUri=http://localhost"

dotnet run --project tools/CaseDesk.Seeder -- --dry-run     # read only, shows the plan
dotnet run --project tools/CaseDesk.Seeder                  # seed
dotnet run --project tools/CaseDesk.Seeder -- --sla-demo    # seed (no-op if done) + SLA demo dates
```

Service principal (CI or scripted), with an application user in the target environment:

```powershell
dotnet run --project tools/CaseDesk.Seeder -- --connection "AuthType=ClientSecret;Url=https://<org>.crm.dynamics.com;ClientId=<app id>;ClientSecret=<secret>"
```

Keep client secrets in an environment variable or a secret store, not in shell history or
scripts. The repo is public (constitution V). The seeder never prints the connection string.

| Option | Meaning |
| --- | --- |
| `-c`, `--connection <string>` | Connection string; falls back to `CASEDESK_DATAVERSE_CONNECTION` |
| `--dry-run` | Connect and read only; lines marked `? dry-run` show what would change |
| `--sla-demo` | After seeding, move two open seed cases into amber / red SLA state |
| `-h`, `--help` | Help |

Exit codes: `0` success, `1` connection or Dataverse error, `2` bad arguments, `3` finished
with warnings (for example, the role was not found).

## Notes and limits

- **Users are not created.** For the FR13 demo (BU A agent cannot open a BU B case), create
  the agent users, move them to `CaseDesk BU A` / `CaseDesk BU B` and give them the
  `CaseDesk Agent` role in the admin centre. Then reassign some seed cases to them.
- Creating cases triggers the "email customer on create" flow when it is on. The addresses
  use the reserved `example.com` domain, so the emails go nowhere.
- To re-seed cases, delete the `[Seed] ...` cases (their notes cascade) and run again.
- Option values come from `Constants.cs`, which mirrors
  `specs/001-casedesk-case-management/data-model.md`.

## Build

```powershell
dotnet build tools/CaseDesk.Seeder
```
