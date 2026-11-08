# CaseDesk beginner guide: from zero to a working app

This guide is for someone new to Dynamics 365 and the Power Platform. Follow it top to bottom.
Each part says what you do, why, and how to check that it worked. Detailed reference docs are
linked where you need them, but you should not have to open them to get through.

You can stop after any part and come back later. Parts 1–3 need nothing but your PC.

| Part | What you get | Needs |
| --- | --- | --- |
| [1. The ideas you need](#part-1-the-ideas-you-need-15-minutes) | A mental map of Dynamics | Nothing |
| [2. Open the code in Visual Studio](#part-2-open-the-code-in-visual-studio) | Every project in one tree | Visual Studio 2026 |
| [3. Build and test on your PC](#part-3-build-and-test-on-your-pc) | Green tests and the solution zip | .NET, Node, pac |
| [4. Get a Dev environment](#part-4-get-a-dev-environment) | Your own Dataverse to play in | A work account |
| [5. Import CaseDesk](#part-5-import-casedesk-into-dev) | The app running in the browser | Part 4 |
| [6. Users, data and email](#part-6-users-demo-data-and-email) | Agents, a supervisor, sample cases | Part 5 |
| [7. Turn on the flows](#part-7-turn-on-the-cloud-flows) | Emails, approvals, SLA warnings | Part 6 |
| [8. Try every feature by hand](#part-8-try-every-feature-by-hand) | You know how it behaves | Part 7 |
| [9. The Azure side](#part-9-the-azure-side-caseclosed-integration) | Closed cases reach an external system | Azure subscription |
| [10. Save Dataverse's version back to Git](#part-10-save-dataverses-version-back-to-git) | Repo matches the environment | Part 5 |
| [11. Walkthroughs and films](#part-11-browser-walkthroughs-and-films) | A captioned guide film | Parts 6–9 |
| [12. Deploy to UAT with GitHub](#part-12-deploy-to-uat-with-github-actions) | Merge to `main` = managed deploy | A second environment |
| [13. When something goes wrong](#part-13-when-something-goes-wrong) | Where to look | — |

---

## Part 1: The ideas you need (15 minutes)

### The words

| Term | Plain meaning | In CaseDesk |
| --- | --- | --- |
| **Dataverse** | The database and server behind Dynamics 365 and Power Apps. It stores tables and runs your server code. | Holds every case, note and SLA policy |
| **Environment** | One isolated copy of Dataverse with its own URL, data and apps, e.g. `https://yourorg.crm11.dynamics.com`. You normally have Dev, UAT and Production. | You build in Dev; GitHub deploys to UAT |
| **Table** (old name *entity*) | Like a SQL table. Has columns, relationships, forms and views. | `cd_case`, `cd_casenote`, `cd_slapolicy`, `cd_refundapproval` |
| **Column** (old name *field* / *attribute*) | A field on a table. | `cd_duedate`, `cd_priority` |
| **Choice** (old name *option set*) | A drop-down list stored as numbers. | Priority, Category; our numbers start with `41720…` |
| **Status / Status Reason** | Every table has `statecode` (Active / Inactive) and `statuscode` (the detailed reason). | New, Assigned, In Progress, … Resolved are *Active*; Closed and Cancelled are *Inactive* |
| **Publisher prefix** | Every custom thing gets your prefix so it never clashes with Microsoft's. | `cd_` |
| **Model-driven app** | An app generated from tables, forms, views and a site map. You don't draw screens; Dataverse renders them. | The **CaseDesk** app |
| **Form / View / Dashboard** | Form = one record's edit screen. View = a saved list query. Dashboard = charts and lists on one page. | Case main form, *My Open Cases*, two dashboards |
| **Solution** | A package (zip) of all your customisations. It is how you move work between environments. | `CaseDesk.zip` |
| **Unmanaged / managed solution** | Unmanaged = editable, used in Dev. Managed = locked, used in UAT and Production; removing it removes everything it brought. | Dev gets `CaseDesk.zip`, UAT gets `CaseDesk_managed.zip` |
| **Business rule** | No-code if/then logic on a form or table (show, hide, require, error). | Refund Amount shown and required only for Refund cases |
| **Plugin** | C# code that Dataverse runs inside its own transaction when something happens (Create, Update, …). It can block the save. | Due date calculation, the "can't resolve with open notes" rule, refund guard |
| **Plugin step** | The registration that says *which* event runs *which* plugin class, sync or async, before or after the save. | Steps S1–S4 in [plugin-steps.md](../specs/001-casedesk-case-management/contracts/plugin-steps.md) |
| **Custom API** | Your own named server operation (like a web API endpoint) backed by a plugin. | `cd_EscalateCase`, called by the **Escalate** button |
| **Ribbon / command bar** | The buttons along the top of a form. | **Escalate** |
| **Web resource** | A JavaScript, HTML or image file stored in Dataverse. | `case.ribbon.js`, `escalate.html` |
| **PCF control** | A custom UI component (TypeScript/React) that replaces how a column is drawn on a form. | The green/amber/red SLA badge |
| **Cloud flow** (Power Automate) | Low-code automation, triggered by data changes or a schedule. | Customer emails, refund approval, SLA warning, daily digest, auto-close |
| **Connection / connection reference** | A connection is a signed-in account a flow uses (e.g. your Office 365). The connection reference is the solution's placeholder for it, so each environment can plug in its own. | Dataverse, Office 365 Outlook, Approvals |
| **Environment variable** | A setting that lives in the solution but takes a different value per environment. | `cd_CaseClosedTransport`, `cd_SenderQueueEmail`, … |
| **Business unit (BU)** | A security boundary. Users belong to one BU. | CaseDesk BU A, CaseDesk BU B |
| **Security role** | What a user may do with each table, and at what depth (own records, BU, whole org). | CaseDesk Agent (BU), CaseDesk Supervisor (org) |
| **Team** | A group that can own records and hold roles. | CaseDesk Supervisors (escalations go here) |
| **Service endpoint** | Dataverse's registration of an outside destination (Azure Service Bus or a webhook URL) it can push messages to. | The two CaseClosed endpoints |
| **pac** | Power Platform CLI, the command-line tool for solutions, auth and tools. | You use it to import and sync |
| **Plugin Registration Tool (PRT)** | A Windows tool to see and register plugins, steps and service endpoints. Start it with `pac tool prt`. | You use it in Part 9 |

### How a case moves through the system

```
Agent clicks Save on a new case
  └─ Plugin S1 (sync, before save) sets Due Date, Warning On, SLA Policy ──► saved
       └─ Flow "Email customer on create" sends the customer an email (seconds later)

Agent sets status Resolved
  └─ Plugin S2 (sync, before save) checks: any open notes? → blocks with CD004
                                          Refund case?    → switches to Awaiting Approval
       └─ Flow "Refund approval" asks supervisors in Teams/Outlook → writes the answer back

Agent closes the case
  └─ Plugin S3 (async, after save) reads cd_CaseClosedTransport
       └─ Service Bus queue  or  webhook ─► Azure Function ─► external stub
```

The rule of thumb used everywhere: **if it must always be true, it lives on the server**
(plugin or table-level business rule). Forms and flows are for convenience and
notifications. [interview-notes.md](interview-notes.md) explains every choice in this style.

### Which folder becomes what in Dynamics

| Folder | What it is | Ends up in Dynamics as |
| --- | --- | --- |
| `solution/src/` | The solution, unpacked into XML/JSON files so Git can track it | Tables, forms, views, roles, app, flows, business rules, env vars, Custom API, web resources |
| `solution/CaseDesk.cdsproj` | The project that packs `src/` into the zips. It also builds the plugins and PCF and puts them inside | `CaseDesk.zip` / `CaseDesk_managed.zip` |
| `plugins/` | C# plugin code | A plugin assembly (DLL) stored in Dataverse |
| `pcf/SlaBadge/` | The badge control | A custom control on the case form |
| `functions/` | Azure Functions code and Bicep for Azure | Runs in Azure, **not** in Dataverse |
| `tools/CaseDesk.Seeder/` | Console app that creates BUs, team and sample data | Data rows (not customisation) |
| `tests/` | Unit tests | Nothing; they run on your PC and in CI |
| `e2e/` | Playwright browser walkthroughs | Nothing; they drive the app in a browser |

---

## Part 2: Open the code in Visual Studio

The repo is a set of folders because a Dynamics project mixes several kinds of code. One
**solution file at the root**, `CaseDesk.slnx`, pulls them together for Visual Studio.

1. Start **Visual Studio 2026**.
2. **File → Open → Project/Solution** → pick `CaseDesk.slnx` in the repo root.
   (Or double-click `CaseDesk.slnx` in File Explorer.)
3. In **Solution Explorer** you will see numbered folders:

   | Folder | Contains | Loads as |
   | --- | --- | --- |
   | 0 Start here | README, this guide, the BRD, quickstart | Files |
   | 1 Plugins | `CaseDesk.Plugins`, `CaseDesk.Plugins.Tests` | C# projects |
   | 2 Azure Functions | `CaseDesk.Functions`, `CaseDesk.Functions.Tests`, Bicep | C# projects + files |
   | 3 Seeder | `CaseDesk.Seeder` | C# project |
   | 4 Dataverse solution | `CaseDesk.cdsproj`, `Solution.xml`, `Customizations.xml` | Files (build it from the terminal, below) |
   | 5 PCF SLA badge | `SlaBadge.pcfproj`, the manifest | Files (TypeScript is easier in VS Code) |
   | 6 Pipelines | GitHub Actions YAML | Files |
   | 7 Docs | All docs | Files |

4. **Build → Build Solution** (Ctrl+Shift+B) builds the five C# projects.
5. **Test → Test Explorer → Run All** runs the plugin and Function tests (163 tests).

Useful things to try in Visual Studio:

- Open `plugins/CaseDesk.Plugins/CasePreUpdatePlugin.cs` and put a breakpoint in `Execute`. Then
  right-click a test in `CasePreUpdatePluginTests` → **Debug**. You can step through a plugin
  without any Dynamics environment, because FakeXrmEasy plays the part of Dataverse.
- Seeing the whole repo, not only the projects: in Solution Explorer click the
  **Switch Views** button (folder icon) → **Folder View**. Switch back the same way.
- The `.cdsproj` and the PCF project aren't loaded as projects because Visual Studio has no
  project type for them. Build them in the terminal (**View → Terminal**, Part 3). For the
  TypeScript, open the `pcf/SlaBadge` folder in **VS Code**.

---

## Part 3: Build and test on your PC

### Install once

| Tool | Check | Install |
| --- | --- | --- |
| .NET SDK 8 or later | `dotnet --version` | Comes with Visual Studio |
| .NET Framework 4.6.2+ | (Windows has 4.8) | — |
| Node.js 20+ | `node --version` | nodejs.org (LTS) |
| Power Platform CLI | `pac` | `dotnet tool install --global Microsoft.PowerApps.CLI.Tool` |
| Git | `git --version` | git-scm.com |

### Build everything

Run these in a PowerShell terminal in the repo root (Visual Studio: **View → Terminal**):

```powershell
dotnet test tests/CaseDesk.Plugins.Tests/CaseDesk.Plugins.Tests.csproj -c Release   # 90 tests
dotnet test functions/CaseDesk.Functions.slnx -c Release                             # 73 tests
cd pcf/SlaBadge; npm ci; npm test; npm run build; cd ../..                           # 28 tests
dotnet build solution/CaseDesk.cdsproj -c Release                                    # packs the solution
```

**Check:** `solution/bin/Release/` contains `CaseDesk.zip` and `CaseDesk_managed.zip`. These are
what you import into Dynamics.

---

## Part 4: Get a Dev environment

You need a Dataverse environment you can break. The free choice is the **Power Apps Developer
Plan**.

1. **You need a work or school account** (`you@something.onmicrosoft.com` or a company
   address). A personal Gmail or Outlook.com address can't sign up. If you don't have one,
   create a Microsoft 365 tenant first: a Microsoft 365 Business trial gives you a tenant,
   mailboxes and Teams, which CaseDesk needs for emails and approvals.
2. Go to the Power Apps Developer Plan page (search "Power Apps Developer Plan") and sign up
   with that account.
3. Open the **Power Platform admin centre** (`admin.powerplatform.microsoft.com`) →
   **Environments**. You should see a *Developer* environment. Open it and copy its
   **Environment URL** (e.g. `https://org1a2b3c.crm11.dynamics.com`).

> Alternative: a 30-day Dynamics 365 trial also works and comes with a ready tenant, but it
> expires. The Developer Plan lasts as long as you use it.

**Check:** open `https://make.powerapps.com`, pick the environment in the top-right picker, and
you can see **Tables**.

---

## Part 5: Import CaseDesk into Dev

### 5.1 Sign pac in to the environment

```powershell
pac auth create --name dev --environment https://<your-dev>.crm11.dynamics.com
pac org who     # shows the environment and your user — proves you are connected
```

### 5.2 Import the unmanaged solution

```powershell
pac solution import --path solution/bin/Release/CaseDesk.zip --publish-changes
```

This takes a few minutes. It creates the tables, forms, app, roles, Custom API, environment
variables and flows, uploads the plugin DLL, and registers its steps.

**If the import fails** it prints the component that broke. The solution XML was hand-written,
not exported, so this first import is its real test. Copy the error into Claude Code and fix
it in `solution/src/`, then rebuild (Part 3) and import again. The parts most likely to need
fixing are listed in [Part 13](#part-13-when-something-goes-wrong).

### 5.3 Look at what arrived

1. `make.powerapps.com` → **Solutions** → **CaseDesk**. Click through **Tables**, **Apps**,
   **Cloud flows**, **Environment variables**, **Security roles**. Everything in
   `solution/src/` is here.
2. **Tables → Case → Columns**: find Case Number (auto-number), Due Date, Priority.
3. **Apps → CaseDesk → Play**. The app opens. Create a case: pick a Customer, type a Title,
   choose High priority, **Save**.

**Check:** the case gets a number like `CD-00001`, and Due Date is about 8 hours from now. That
date was set by the plugin. If it is empty, the plugin step isn't registered (Part 13).

### 5.4 See the plugin registration (optional, but worth it)

```powershell
pac tool prt
```

Connect to Dev (Office 365 sign-in). Expand **(Assembly) CaseDesk.Plugins**. You will see the
plugin classes and, under each, its steps (message, table, stage, sync/async). This tool is
how most Dynamics developers manage plugins day to day.

---

## Part 6: Users, demo data and email

### 6.1 Run the seeder

The seeder creates the two business units, the *CaseDesk Supervisors* team, the four SLA
policies, five accounts, six contacts and seven sample cases.

```powershell
$env:CASEDESK_DATAVERSE_CONNECTION = "AuthType=OAuth;Url=https://<your-dev>.crm11.dynamics.com;LoginPrompt=Auto;AppId=51f81489-12ee-4a9e-aaae-a2591f45987d;RedirectUri=http://localhost"
dotnet run --project tools/CaseDesk.Seeder -- --dry-run   # shows what it would do
dotnet run --project tools/CaseDesk.Seeder                # does it
```

A browser sign-in pops up the first time. It is safe to run again: re-runs change nothing.
More in [the seeder README](../tools/CaseDesk.Seeder/README.md).

**Check:** in the app, **Cases** shows seven `[Seed] …` cases, each with a due date.

### 6.2 Create three test users

You need them to see the security model work.

1. **Microsoft 365 admin centre** (`admin.microsoft.com`) → **Users → Add a user**. Create
   `agent.a`, `agent.b` and `supervisor`. Give each a licence that includes Exchange
   (mailbox) and Teams if you have one. Each also needs a licence that lets them run Power
   Apps: each can sign up for the free Developer Plan themselves, or use a trial licence.
2. **Power Platform admin centre → your environment → Settings → Users + permissions →
   Users**. Add each user to the environment. Wait a few minutes for them to appear, then:

   | User | Change business unit to | Security role | Also |
   | --- | --- | --- | --- |
   | agent.a | CaseDesk BU A | CaseDesk Agent | — |
   | agent.b | CaseDesk BU B | CaseDesk Agent | — |
   | supervisor | (root BU, the one named after your org) | CaseDesk Supervisor | **Users + permissions → Teams → CaseDesk Supervisors → Add team members** → supervisor |

   To change a BU: select the user → **Change business unit**. Roles: **Manage security
   roles**.

**Check:** sign in as `agent.b` in a private browser window and open the CaseDesk app. Agent B
can't see BU A's cases. The supervisor sees all of them.

### 6.3 Make email work

The customer emails are Email records in Dataverse, sent by **server-side synchronisation**
through an Exchange mailbox. The flow looks up a **queue** whose email address equals
`cd_SenderQueueEmail`.

Easiest setup, using your own mailbox:

1. **Power Platform admin centre → environment → Settings → Email → Mailboxes** → open your
   own mailbox → **Approve Email**, then **Test & Enable Mailbox**. Wait until *Incoming* and
   *Outgoing* say **Success**. (Approval needs the Exchange admin or Global admin role, which
   you have in your own tenant.)
2. `make.powerapps.com` → **Solutions → CaseDesk → Environment variables → CaseDesk Sender
   Queue Email** → **New value** → your email address. Every user has a default queue with
   their own address, so the flow will find it.
3. Make sure the contacts you test with have an **Email** you can read, for example your own
   address with `+test` (`you+test@yourtenant.onmicrosoft.com`).

If the flow later fails with "no queue found", create one: in the app, **gear icon → Advanced
Settings → Settings → Service Management → Queues → New**, set
its email address to a shared mailbox, then approve and enable that mailbox as above.

---

## Part 7: Turn on the cloud flows

Flows arrive switched off, because each environment needs its own connections.

1. `make.powerapps.com` → **Solutions → CaseDesk → Connection references**. For each of the
   three (Dataverse, Office 365 Outlook, Approvals): open it → **New connection** → sign in →
   **Save**.
2. **Cloud flows**: open each of the six flows → **Turn on**:

   | Flow | Runs when | Does |
   | --- | --- | --- |
   | Email customer on create | A case is created | Emails the customer the case number and due date |
   | Email customer on close | A case is closed | Emails the resolution summary |
   | Refund approval | A Refund case reaches Awaiting Approval | Asks supervisors, first answer wins |
   | SLA warning | Every 15 minutes | Warns the owner when a case reaches its warning time |
   | Daily breach digest | 07:00 daily | Emails supervisors the overdue cases |
   | Auto-close resolved cases | 02:00 daily | Closes cases resolved for 5+ days |

3. **Environment variables** (same solution): set **CaseDesk Auto Close Days** to `0` while
   learning, so auto-close happens on the next run. The others have sensible defaults.

**Check:** create a case for a contact with your email address. Within a minute or two you
receive the creation email, and the case's **Timeline** shows it. If not, open the flow →
**Run history** to see which step failed and why.

---

## Part 8: Try every feature by hand

Do these in the CaseDesk app. Each is one of the BRD's acceptance steps.

| # | Do this | You should see | What did it |
| --- | --- | --- | --- |
| 1 | New case, High priority, Category Billing → Save | `CD-000nn`; Due Date = now + 8 h; Warning On = now + 6 h | Auto-number column; plugin S1 |
| 2 | On the case, add a Note with **Is Open = Yes**. Set status Resolved → Save | Error **CD004 … open note(s)**. Close the note, try again: Resolved, *Resolved On* filled | Plugin S2 |
| 3 | New case, Category **Refund** | Refund Amount appears and is required | Business rules |
| 4 | Enter Refund Amount 50, set Resolved → Save | Status becomes **Awaiting Approval**. The supervisor gets an approval in Teams / Outlook; when they approve, the case becomes Resolved | Plugin S2 + Refund approval flow |
| 5 | Run `dotnet run --project tools/CaseDesk.Seeder -- --sla-demo`, then open the newest seed case | SLA badge amber; the next one red | PCF control |
| 6 | Open a Medium case → **Escalate** → type a reason | Priority Critical, owner *CaseDesk Supervisors*, note "Escalated by…" | Custom API + ribbon button |
| 7 | Close a Resolved case | (After Part 9) the external stub records it within a minute | Plugin S3 → Azure |
| 8 | As agent.b, paste the URL of a BU A case | Access denied; the supervisor can open it | Security roles + BUs |
| 9 | Look at **Dashboards** | *My Open Cases* by priority; *SLA Breaches This Week* | Dashboards and views |

Open the **Plugin Trace Log** (Settings → Customizations → Plugin Trace Log, or the
*Plugin Trace Viewer* in XrmToolBox) to read what the plugins traced. If it's empty, turn on
logging: **Settings → Administration → System Settings → Customization → Enable logging to
plugin trace log: All**.

---

## Part 9: The Azure side (CaseClosed integration)

This part costs a little money (well under £1/month for this sample: Service Bus Basic and
Flex Consumption scale to zero). Skip it if you only want to learn Dataverse. Closing cases
still works without it; the message simply isn't delivered.

You need an Azure subscription, the **Azure CLI** (`az`) and **Azure Functions Core Tools v4**
(`func`). Use **Git Bash** for these commands, because the docs use bash syntax.

1. **Deploy the resources and publish the code:** follow
   [functions/infra/README.md](../functions/infra/README.md) sections *Deploy* and *Secrets*.
   That creates the Function app, Service Bus queue, Key Vault, storage and Application
   Insights.
2. **Register the two service endpoints in Dataverse:** follow
   [caseclosed-endpoints.md](caseclosed-endpoints.md). You paste a Service Bus key and a
   Function key into the Plugin Registration Tool. Name them exactly as the doc says, because
   the plugin finds them by name.
3. **Try it:** close a Resolved case, then open
   `https://<app>.azurewebsites.net/api/external/caseclosed?caseNumber=CD-000nn&code=<stub key>`.
   You see the received message.
4. **The learning exercise:** set environment variable **CaseDesk CaseClosed Transport** to
   `Webhook`, close another case, and watch it arrive the other way. Compare the two in
   Application Insights (`traces | where message has "CaseClosed"`). The table at the end of
   [interview-notes.md](interview-notes.md) lists the trade-offs.

---

## Part 10: Save Dataverse's version back to Git

When you import, Dataverse rewrites the XML in its own format. It also adds anything you
changed in the maker portal. Pull that back into the repo so Git stays the source of truth:

```powershell
cd solution
pac solution sync
cd ..
git status        # see what changed
git diff          # read it: mostly formatting the first time
```

Then rebuild (Part 3) and run the tests to prove the synced version still packs. Commit it.

**From now on, the loop is:** change something (in code, or in the maker portal in Dev) →
`pac solution sync` → build and test → commit → pull request → merge → GitHub deploys to UAT.

---

## Part 11: Browser walkthroughs and films

When Parts 6–9 work, Playwright can click through steps 1–8 for you in a visible browser and
turn the run into a captioned film.

```powershell
cd e2e
npm ci
npx playwright install chromium
copy .env.example .env            # then edit .env: environment URL, app id, stub URL and key
npm run login -- agentA           # sign in as agent.a in the window that opens
npm run login -- agentB
npm run login -- supervisor
npm run walk                      # watch it work
npm run film                      # makes e2e/demo-out/casedesk-demo.mp4
```

Details, options and the voice-over setup are in [walkthrough-films.md](walkthrough-films.md).
If a step can't find a button, the selector to fix is in `e2e/demo/mda.ts`.

---

## Part 12: Deploy to UAT with GitHub Actions

1. Create a **second environment** (another Developer environment, or a trial) for UAT.
2. Create an **app registration** in Entra ID and add it as an **application user** in UAT.
3. Push the repo to GitHub. In **Settings → Environments** create `uat`. Add the four secrets
   and the variables.
4. Open a pull request: **CI** runs all tests and packs the solution. Merge to `main`:
   **Deploy UAT** runs CI again and then imports the **managed** zip into UAT.

Every step is spelled out in [alm.md](alm.md). In UAT you can't edit CaseDesk's components,
which is the point of a managed solution: all changes come through Git.

---

## Part 13: When something goes wrong

| Symptom | Where to look | Likely cause |
| --- | --- | --- |
| Import fails on a business rule | The import error names the rule | Business rule XAML was hand-written. Delete it from `solution/src/Workflows`, import, recreate it in the maker portal using [business-rules.md](business-rules.md), then `pac solution sync` |
| Import fails on a security role | Error names a privilege | A privilege name is wrong in `solution/src/Roles/*.xml` |
| Import fails on a flow / connection reference | Error names the flow | Connection reference details in `solution/src/Workflows/*.json` |
| Due Date stays empty | PRT: is step S1 there and enabled? | Step not registered or disabled |
| Save shows an error starting `CD0…` | That's the plugin working | The full list of codes is in [plugin-steps.md](../specs/001-casedesk-case-management/contracts/plugin-steps.md) |
| Unexpected plugin error | Plugin Trace Log | The plugin's trace lines show what it saw |
| No customer email | Flow **Run history**; then the Email record's status on the timeline | Flow off, connection missing, mailbox not enabled, no queue for `cd_SenderQueueEmail` |
| Closed case never reaches the stub | **Settings → System Jobs** (async plugin failures); Service Bus dead-letter queue; Application Insights | Endpoint name doesn't match the env var; wrong key; Function not published |
| Escalate button missing | Publish all customisations; hard-refresh the browser | Ribbon cached |
| Agent B *can* see BU A cases | The user's BU and roles; did you also give them System Administrator? | System Administrator overrides everything |

Asking for help: copy the exact error text (and the correlation id, if the dialog shows one)
into Claude Code from the repo folder. It can read the matching source.

## Clean up and costs

- Developer environments are free. Delete an environment in the admin centre if you are done.
- Azure: `az group delete -n rg-casedesk` removes every Azure resource from Part 9.
- The optional film voice-over uses Azure AI Speech, which charges per character.

## Suggested learning order

1. Parts 1–3 (concepts and code on your PC). Read `CasePreUpdatePlugin.cs` with its tests.
2. Parts 4–8. After each feature in Part 8, open the code or configuration that did it.
3. Change something small in the maker portal (add a column to the Case form), then
   `pac solution sync` and look at the diff. This is how XML in `solution/src` relates to what
   you click.
4. Part 9, then flip the transport switch.
5. Parts 10–12: the professional ALM loop.
