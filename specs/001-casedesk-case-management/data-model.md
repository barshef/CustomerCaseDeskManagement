# Data Model: CaseDesk

Publisher: **CaseDesk** (`cd_`), choice value prefix **41720** (values start at `417200000`).
Solution unique name: `CaseDesk`.

## Global choices

| Choice | Values |
| --- | --- |
| `cd_casecategory` | Billing 417200000 · Delivery 417200001 · Product 417200002 · Refund 417200003 · Other 417200004 |
| `cd_casepriority` | Low 417200000 · Medium 417200001 · High 417200002 · Critical 417200003 |
| `cd_notetype` | Internal 417200000 · Customer-facing 417200001 |
| `cd_refunddecision` | Pending 417200000 · Approved 417200001 · Rejected 417200002 |

## Case — `cd_case` (User/Team owned)

| Column | Logical name | Type | Rules |
| --- | --- | --- | --- |
| Case Number | `cd_casenumber` | Auto-number text (100), format `CD-{SEQNUM:5}` | Read-only; never edited (FR-001) |
| Title | `cd_name` (primary) | Text (200) | Required (form + pre-create plugin) |
| Description | `cd_description` | Multiline text (4000) | — |
| Category | `cd_category` | Choice `cd_casecategory` | Required; default Other |
| Priority | `cd_priority` | Choice `cd_casepriority` | Required; default Medium |
| Customer | `cd_accountid` | Lookup → `account` | Required (form + pre-create plugin) |
| Requester | `cd_contactid` | Lookup → `contact` | Optional; needed for customer email |
| SLA Policy | `cd_slapolicyid` | Lookup → `cd_slapolicy` | Set by plugin |
| Due Date | `cd_duedate` | Date and time (UTC, user local) | Set by plugin; read-only on form |
| Warning On | `cd_warningon` | Date and time | Set by plugin = Due Date − Warning Hours |
| SLA Warning Sent | `cd_slawarningsent` | Yes/No, default No | Set by warning flow; reset on priority change |
| Resolved On | `cd_resolvedon` | Date and time | Set by plugin on → Resolved; cleared on reopen |
| Closed On | `cd_closedon` | Date and time | Set by plugin when the case becomes inactive (→ Closed or → Cancelled); part of CaseClosed message id; stops the SLA badge |
| Resolution Summary | `cd_resolutionsummary` | Multiline text (2000) | Required on → Resolved (plugin) |
| Refund Amount | `cd_refundamount` | Currency | Visible + required only if Category = Refund (business rules) |
| Customer Reply Pending | `cd_customerreplypending` | Yes/No, default No | Set by auto-close flow when an inbound reply blocks auto-close |
| Status | `statecode` / `statuscode` | State / Status reason | See lifecycle |

Relationships: `cd_account_cd_case` (N:1, referential, restrict delete),
`cd_contact_cd_case` (N:1, referential, remove link), `cd_slapolicy_cd_case` (N:1, restrict
delete), `cd_case_cd_casenote` (1:N, **parental — cascade all**), `cd_case_cd_refundapproval`
(1:N, parental), owner (system).

### Status reasons and lifecycle

| State | Status reason | Value |
| --- | --- | --- |
| Active (0) | New | 1 |
| Active (0) | Assigned | 417200001 |
| Active (0) | In Progress | 417200002 |
| Active (0) | Awaiting Customer | 417200003 |
| Active (0) | Awaiting Approval | 417200004 |
| Active (0) | Resolved | 417200005 |
| Inactive (1) | Closed | 2 |
| Inactive (1) | Cancelled | 417200006 |

Allowed transitions (enforced by `CaseStateMachine` in the pre-update plugin):

```text
New               → Assigned, In Progress, Awaiting Customer, Resolved*, Cancelled
Assigned          → In Progress, Awaiting Customer, Resolved*, Cancelled
In Progress       → Assigned, Awaiting Customer, Resolved*, Cancelled
Awaiting Customer → In Progress, Resolved*, Cancelled
Awaiting Approval → In Progress (rejected), Resolved (only with Approved approval)
Resolved          → In Progress (reopen), Closed
Closed            → (none)
Cancelled         → (none)

* Resolved requires: no open notes, Resolution Summary present.
  Refund + no current Approved approval → rewritten to Awaiting Approval.
```

Side effects in the same transaction: → Resolved sets `cd_resolvedon`; → In Progress from
Resolved clears `cd_resolvedon` and `cd_customerreplypending`; → Closed or → Cancelled sets
`cd_closedon`.
Category may not change while Awaiting Approval.

## Case Note — `cd_casenote` (User owned)

| Column | Logical name | Type | Rules |
| --- | --- | --- | --- |
| Subject | `cd_name` (primary) | Text (200) | Defaults to first 100 chars of note text (form) |
| Note | `cd_notetext` | Multiline text (10000) | Required |
| Note Type | `cd_notetype` | Choice `cd_notetype` | Default Internal |
| Is Open | `cd_isopen` | Yes/No | Default No |
| Case | `cd_caseid` | Lookup → `cd_case` | Required; parental |

## SLA Policy — `cd_slapolicy` (Organization owned)

| Column | Logical name | Type | Rules |
| --- | --- | --- | --- |
| Name | `cd_name` | Text (100) | Required |
| Priority | `cd_priority` | Choice `cd_casepriority` | Required; **alternate key `cd_slapolicy_priority_key`** (one per priority) |
| Resolution Hours | `cd_resolutionhours` | Whole number 1–8760 | Required |
| Warning Hours | `cd_warninghours` | Whole number 0–8760 | Required; must be < Resolution Hours (business rule error) |

Seed: Critical 4/1, High 8/2, Medium 24/2, Low 72/2.

## Refund Approval — `cd_refundapproval` (User owned)

| Column | Logical name | Type | Rules |
| --- | --- | --- | --- |
| Name | `cd_name` | Text (200) | "Refund approval – {case number}" |
| Case | `cd_caseid` | Lookup → `cd_case` | Required |
| Requested By | `cd_requestedbyid` | Lookup → `systemuser` | Required |
| Approver | `cd_approverid` | Lookup → `systemuser` | Set on decision |
| Decision | `cd_decision` | Choice `cd_refunddecision` | Default Pending |
| Decision Date | `cd_decisiondate` | Date and time | Set on decision |
| Comment | `cd_comment` | Multiline text (2000) | — |

"Current approval" for a case = the most recently created Refund Approval for it. The plugin
allows Awaiting Approval → Resolved only if that row's Decision = Approved.

## Account / Contact (standard)

Not customised beyond a Cases sub-grid on the main form. Email = `emailaddress1`. Seeder sets
`accountnumber` (e.g. `CD-ACC-001`) for idempotent upsert.

## Environment variables

| Schema name | Type | Default | Purpose |
| --- | --- | --- | --- |
| `cd_CaseClosedTransport` | Text | `ServiceBus` | `ServiceBus` \| `Webhook` \| `None` (R10) |
| `cd_CaseClosedServiceBusEndpointName` | Text | `CaseDesk CaseClosed ServiceBus` | Service endpoint name lookup |
| `cd_CaseClosedWebhookEndpointName` | Text | `CaseDesk CaseClosed Webhook` | Service endpoint name lookup |
| `cd_SupervisorTeamName` | Text | `CaseDesk Supervisors` | Escalation owner team |
| `cd_SenderQueueEmail` | Text | (none — set per environment) | From address for customer emails |
| `cd_AutoCloseDays` | Number | `5` | Auto-close threshold (FR-011) |

## Non-solution environment data (seeder)

Business units `CaseDesk BU A`, `CaseDesk BU B`; team `CaseDesk Supervisors` (root BU, owner
team, role CaseDesk Supervisor); service endpoints (registered with Plugin Registration Tool or
`pac`, secrets entered there).
