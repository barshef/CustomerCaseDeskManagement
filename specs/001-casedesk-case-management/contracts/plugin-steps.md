# Contract: Plugin step registrations (`CaseDesk.Plugins`)

| # | Plugin class | Message / table | Stage | Mode | Filtering attributes | Images | FR |
| --- | --- | --- | --- | --- | --- | --- | --- |
| S1 | `CasePreCreatePlugin` | Create / `cd_case` | PreOperation (20) | Sync | — | — | FR-002, FR-003 |
| S2 | `CasePreUpdatePlugin` | Update / `cd_case` | PreOperation (20) | Sync | `statecode, statuscode, cd_priority, cd_category` | Pre-image `PreImage` (all columns used) | FR-003, FR-004, FR-004a/b, FR-005 |
| S3 | `PublishCaseClosedPlugin` | Update / `cd_case` | PostOperation (40) | **Async** (delete job on success) | `statecode, statuscode` | Pre-image `PreImage` (`statuscode`), Post-image `PostImage` (all) | FR-010 |
| S4 | `EscalateCasePlugin` | `cd_EscalateCase` (Custom API) | Main operation | Sync | — | — | FR-009 |

## Error messages (shown to users)

| Code | When | Message |
| --- | --- | --- |
| CD001 | Create without Title | "A case needs a title." |
| CD002 | Create without Customer | "A case needs a customer (account)." |
| CD003 | No SLA policy for priority | "No SLA policy exists for priority {P}. Ask a supervisor to create one." |
| CD004 | Resolve with open notes | "This case has {n} open note(s). Close them before resolving." |
| CD005 | Resolve without summary | "Enter a resolution summary before resolving." |
| CD006 | Illegal transition | "A case cannot move from {from} to {to}." |
| CD007 | Category change while Awaiting Approval | "The category cannot change while a refund approval is pending." |
| CD008 | Escalate without edit access | "You need edit access to this case to escalate it." |
| CD009 | Escalate a Resolved/Closed/Cancelled case | "Only open cases can be escalated." |
| CD010 | Supervisor team missing | "The supervisor team '{name}' was not found. Ask an administrator to create it." |

Messages are prefixed with the code, e.g. `CD004: This case has 1 open note(s)...`, so
tests and support can match on the code.

## Idempotency notes

- S1/S2 only compute values from the target + pre-image; re-running yields the same result.
- S3 builds the message id from `cd_closedon`, so an async retry publishes the same id.
- S4 returns early when already escalated (contract step 3).
