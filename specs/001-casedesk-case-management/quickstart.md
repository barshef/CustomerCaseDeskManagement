# Quickstart: build, deploy and validate CaseDesk

## Prerequisites

- Windows with .NET SDK 8+ (repo built with SDK 10), .NET Framework 4.6.2+ runtime, Node 20+.
- Power Platform CLI: `dotnet tool install --global Microsoft.PowerApps.CLI.Tool`.
- For deployment: two Dataverse environments (Dev, UAT), an Azure subscription, Azure CLI and
  Functions Core Tools v4.

## 1. Build and test locally

```powershell
dotnet test plugins/CaseDesk.Plugins.slnx -c Release     # plugin unit tests (net462)
dotnet test functions/CaseDesk.Functions.slnx -c Release # Function mapper/handler tests (net8)
cd pcf/SlaBadge; npm ci; npm test; npm run build; cd ../..
dotnet build solution/CaseDesk.cdsproj -c Release        # packs unmanaged + managed zips
```

Expected: all tests green; `solution/bin/Release/CaseDesk.zip` and `CaseDesk_managed.zip`.

## 2. Deploy to Dev (first time)

```powershell
pac auth create --name dev --environment https://<dev>.crm.dynamics.com
pac solution import --path solution/bin/Release/CaseDesk.zip --publish-changes
dotnet run --project tools/CaseDesk.Seeder -- --connection "<connection string>"
```

Then, once per environment:

1. Deploy Azure resources: `az deployment group create -g rg-casedesk -f functions/infra/main.bicep`
   and publish the Function app (`func azure functionapp publish <app>`).
2. Register the two service endpoints with the Plugin Registration Tool
   (`pac tool prt`): **CaseDesk CaseClosed ServiceBus** (queue `caseclosed`, message format
   JSON, SAS key from Azure) and **CaseDesk CaseClosed Webhook** (URL
   `https://<app>.azurewebsites.net/api/caseclosed`, auth WebhookKey = Function key).
3. Set environment variable values: `cd_SenderQueueEmail`; optionally
   `cd_CaseClosedTransport`.
4. Turn on the cloud flows and fix their connection references.
5. Run `pac solution sync` from `solution/` so the repo matches Dataverse's normalised XML.

## 3. Switch the CaseClosed transport (learning exercise)

Set `cd_CaseClosedTransport` to `Webhook` (or back to `ServiceBus`) in the solution's
environment variables. Close a case and watch either the Service Bus trigger or the HTTP
trigger fire in Application Insights (`traces | where message has "CaseClosed"`). `None`
disables publishing.

*No-code variant*: instead of the async plugin, register an async step directly on a service
endpoint (Update of `cd_case`, filtering `statuscode`). The Function's handler ignores
non-Closed contexts, so both variants work with the same Azure code.

## 4. Validate (BRD demo script)

| # | Steps | Expected |
| --- | --- | --- |
| 1 | Create a High Billing case for a seeded account + contact | Case Number `CD-000nn`, Due Date = now + 8h, Warning On = now + 6h |
| 2 | Add an internal note with Is Open = Yes; set status Resolved | Blocked with `CD004`; close the note; Resolve succeeds and Resolved On is set |
| 3 | Create a Refund case (Refund Amount 50); Resolve | Status Awaiting Approval; supervisor approves in Teams/Outlook → Resolved; Refund Approval row = Approved |
| 4 | Watch the requester's mailbox | Creation email within 1 minute; email on the case timeline |
| 5 | Edit Due Date/Warning On via seeder `--sla-demo` option | Badge amber in the warning window, red after due |
| 6 | Click Escalate on a Medium case | Priority Critical, owner CaseDesk Supervisors, note "Escalated by…" |
| 7 | Close a Resolved case | Stub logs `CaseClosedReceived` within 1 minute (`GET /api/external/caseclosed?caseNumber=…`) |
| 8 | Sign in as `agent.b` (BU B); open a BU A case URL | Access denied; supervisor can open it |
| 9 | Merge to `main` | `deploy-uat` workflow imports the managed solution to UAT after tests pass |
| 10 | `cd e2e; npm run login -- agentA` (and `agentB`, `supervisor`) once, then `npm run walk`; `npm run film` | Steps 1–8 pass in a visible browser; `e2e/demo-out/casedesk-demo.mp4` with one chapter per step ([docs/walkthrough-films.md](../../docs/walkthrough-films.md)) |
