# BRD — CaseDesk: Customer Case Management on Dynamics 365 CE

2026 · @Barshef

## Purpose and scope

CaseDesk lets a small service team log, assign, work and close customer cases against an SLA, with the customer notified at each step and an external system told when a case closes. It is built on Dynamics 365 CE / Dataverse so that every Power Platform extension point a hiring manager asks about (plugin, flow, business rule, custom API, PCF, Azure integration, ALM) is exercised once, for a real reason.

**Owner:** your limited company, as an internal product used for client demos. **Build window:** 10 Nov – 7 Dec 2026. **Repo:** public GitHub, managed solution plus source.

**In scope**

- Cases raised by agents on behalf of customers (Account/Contact), categorised, prioritised and assigned.
- SLA policy per priority; due date calculated automatically; breach warnings.
- Case notes, status lifecycle, supervisor approval for refund cases.
- Customer email on creation and closure; closed-case event pushed to an external system via Azure.
- Agent and supervisor dashboards.

**Out of scope (v1)**

- Customer self-service portal (Power Pages).
- Knowledge base, omnichannel chat, telephony.
- Dynamics 365 Customer Service licensing — CaseDesk uses a custom Case table on Dataverse so it runs on the free developer plan and a Power Apps per-user licence in production.

## Users and roles

Four roles, two of them Dataverse security roles; the customer never logs in.

| Role | Who | Can | Cannot | Dataverse security |
| --- | --- | --- | --- | --- |
| Agent | Service team member | Create cases, add notes, update status, resolve own and team cases | Close a refund case without approval; delete cases | Custom role: Case CRUD at Business Unit level, Case Note user-owned |
| Supervisor | Team lead | Everything an Agent can plus reassign, approve refunds, edit SLA policies, see all dashboards | Change solution components | Custom role: Case and SLA Policy at Organization level |
| Customer | Account or Contact record | Receives emails | No access | None |
| Admin | You | Deploy solutions, manage environments and flows | — | System Administrator |

## Data model

Four custom tables plus the standard Account and Contact; publisher prefix `cd_`.

| Table | Key columns | Relationships | Notes |
| --- | --- | --- | --- |
| Case (`cd_case`) | Case Number (auto-number CD-00001), Title, Description, Category (choice: Billing, Delivery, Product, Refund, Other), Priority (choice: Low, Medium, High, Critical), Status (status reason), Due Date, Resolved On, Resolution Summary, Refund Amount (currency) | N:1 Account (customer), N:1 Contact (requester), N:1 SLA Policy, N:1 User (owner), 1:N Case Note | Owner = assigned agent; Due Date set by plugin |
| Case Note (`cd_casenote`) | Note text, Note Type (choice: Internal, Customer-facing), Is Open (yes/no) | N:1 Case (parental, cascade delete) | A case cannot close while any note has Is Open = yes |
| SLA Policy (`cd_slapolicy`) | Name, Priority (choice), Resolution Hours (whole number), Warning Hours (whole number) | 1:N Case | One row per priority; supervisor-editable |
| Refund Approval (`cd_refundapproval`) | Case, Requested By, Approver, Decision (choice: Pending, Approved, Rejected), Decision Date, Comment | N:1 Case | Written by the approval flow |
| Account / Contact (standard) | Name, Email | 1:N Case | Reused, not customised beyond a Case sub-grid |

Status reasons on Case: New → Assigned → In Progress → Awaiting Customer → Awaiting Approval (Refund only) → Resolved → Closed, plus Cancelled from any active state.

## Case lifecycle

&#91;embedded content: case status lifecycle · 8 states\]

Solid arrows are agent actions; the dashed arrow is Cancel, allowed from any state except Resolved and Closed. Only the refund category passes through Awaiting Approval.

## Functional requirements

Each requirement names the extension point that implements it, so the finished solution demonstrates every one a PL-400 syllabus or interviewer expects. Pick the simplest tool that works; the "why" column is the interview answer.

| # | Requirement | Implemented with | Why this extension point |
| --- | --- | --- | --- |
| FR1 | Case Number is generated as CD-00001 and never edited | Auto-number column | Platform feature; no code needed |
| FR2 | Title and Customer are mandatory; Refund Amount is required and visible only when Category = Refund | Business rule + form configuration | Declarative, runs client and server side |
| FR3 | On create, Due Date = created on + Resolution Hours from the SLA Policy matching the case Priority | C# plugin, pre-operation, synchronous | Must be in the same transaction so the record is never saved without a due date |
| FR4 | A case cannot move to Resolved while any Case Note has Is Open = yes | C# plugin, pre-operation on Update of status, throws `InvalidPluginExecutionException` | Server-side rule that must hold for API and UI alike |
| FR5 | A Refund case moving to Resolved first goes to Awaiting Approval; the supervisor approves or rejects | Power Automate approval flow + Refund Approval table | Approvals are a native flow action; a plugin would reinvent them |
| FR6 | Customer receives an email on case creation and on closure | Power Automate automated flow (Dataverse trigger) that creates a standard Email row, sent by server-side sync; a plain Office 365 Outlook connector action is a later option | Low-code, visible to admins, easy to change templates |
| FR7 | Agents see a warning 2 hours before SLA breach; supervisors get a daily breach digest | Power Automate scheduled flow + Due Date query | Time-based; no event to hook a plugin on |
| FR8 | The Case form shows a live SLA countdown badge (green / amber / red) | PCF control (TypeScript/React) bound to Due Date | A visual control the platform cannot render natively |
| FR9 | "Escalate" button on the Case form sets Priority = Critical, reassigns to the supervisor queue and logs a note | Custom API (plugin-backed) called from a ribbon command (JavaScript) | Reusable server operation callable from JS, flows and external callers |
| FR10 | When a case is Closed, publish a CaseClosed event to an external system | Dataverse webhook or Service Bus endpoint → Azure Function → external HTTP endpoint (stub) | Decouples Dataverse from the external system; async, retriable |
| FR11 | Resolved cases auto-close after 5 days with no customer reply | Power Automate scheduled flow | Time-based |
| FR12 | Agent dashboard: my open cases by priority; Supervisor dashboard: team SLA breaches this week | Model-driven app dashboards with charts and views | Configuration |
| FR13 | Agents can only see cases in their business unit; supervisors see all | Security roles and business units | Platform security, no code |
| FR14 | All plugins have unit tests; solution deploys to a UAT environment on merge to master | xUnit + FakeXrmEasy; GitHub Actions with Power Platform actions | ALM is an explicit PL-400 topic and a partner interview topic |
| FR15 | The demo script runs end to end in a real browser as Agent, second-unit Agent and Supervisor, and is recorded as a captioned guide film (optionally voiced) for client demos and learning | Playwright (TypeScript) walkthroughs against the model-driven app, video recording, caption band + chapter cards, ffmpeg stitching, Azure speech for voice-over | Proves the whole solution works through the UI, not just in unit tests, and gives a repeatable demo film for interviews and AppSource screenshots |

## Integration

&#91;embedded content: solution architecture · app, Dataverse, Azure, ALM\]

The only outbound integration is the CaseClosed event (FR10): a Dataverse webhook or Service Bus endpoint registered against the Case Update step publishes a message, an Azure Function consumes it and POSTs to a stub external endpoint (an Azure Function with a static key is enough for the demo). Inbound integration is through the Dataverse Web API from a .NET 8 console app, used for seeding demo data and as the Week 2 exercise.

## Non-functional requirements, ALM and acceptance

**Non-functional**

- Plugins complete in under 2 seconds and never call external HTTP synchronously; anything slow goes async via Service Bus.
- Every plugin is idempotent on retry and logs through `ITracingService`; the Azure Function uses Application Insights.
- Secrets (client secret, external endpoint key) live in environment variables in Dataverse and Key Vault in Azure, never in code or solution files.
- Solution is managed in UAT; all customisation is in one solution with publisher prefix `cd_`.
- Supports 50 concurrent agents and 10,000 open cases without custom indexing (Dataverse defaults).

**ALM**

- Environments: Dev (developer plan), UAT (second developer environment or trial).
- Repo layout: `/solution` (unpacked), `/plugins` (C#), `/pcf`, `/functions` (Azure), `/tests`, `/e2e` (Playwright walkthroughs and films), `/docs`.
- Branches: day-to-day work on `develop`; release by merging `develop` into `master`.
- GitHub Actions: on push to `master` pack the solution, import to UAT as managed, run plugin tests first.

**Acceptance criteria (demo script)**

1. Create a High priority Billing case → Case Number and Due Date populate without user input (FR1, FR3).
2. Add an open internal note, try to Resolve → blocked with a clear message; close the note → Resolve succeeds (FR4).
3. Create a Refund case and Resolve → status becomes Awaiting Approval; supervisor approves in Teams/email → Resolved (FR5).
4. Customer receives creation email within 1 minute (FR6).
5. SLA badge on the form shows amber inside the warning window and red after breach (FR8).
6. Click Escalate → priority Critical, owner changed, note added, all in one call (FR9).
7. Close the case → external stub logs the CaseClosed payload within 1 minute (FR10).
8. Agent in Business Unit A cannot open a case owned in Business Unit B; supervisor can (FR13).
9. Merge `develop` into `master` → GitHub Actions deploys the managed solution to UAT and tests pass (FR14).
10. Run the walkthrough against Dev → steps 1–8 pass in a visible browser and a captioned film of them is produced, one chapter per step (FR15).

**Decisions**

- FR6 uses the standard Email table and server-side synchronisation first, so emails appear on the case timeline and the solution shows the native pattern; swapping to a plain Office 365 Outlook connector action is a later simplification if mailbox setup proves awkward in the developer environment.
- FR15 (added 8 Nov 2026): Playwright walkthroughs and guide films follow the Rotaro/Moniqora pattern — runs are always headed (visible), captions at the top, chapter cards, films committed under `e2e/demo-out/`, voice-over via Azure speech using the PC's `DEMO_SPEECH_KEY`/`DEMO_SPEECH_REGION`, made on demand on the PC (not in the pipeline). Needs a deployed Dev environment and demo users per persona.
- After v1 is accepted, publish CaseDesk to AppSource as a free app under your limited company for visibility. This needs a Partner Center account, the managed solution packaged with `pac package`, and a listing (description, screenshots, support contact); allow a week for certification.
