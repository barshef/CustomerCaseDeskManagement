# Contract: Custom API `cd_EscalateCase`

| Property | Value |
| --- | --- |
| Unique name | `cd_EscalateCase` |
| Binding | Entity — `cd_case` |
| Type | Action (`IsFunction = false`), not private, allowed custom processing step: None |
| Plugin | `CaseDesk.Plugins.EscalateCasePlugin` (main operation, synchronous, in transaction) |
| Privilege | Caller needs Write access to the case (checked with `RetrievePrincipalAccess`). Agents have no Assign privilege (FR-013), so the plugin performs the priority change, assignment and note as SYSTEM after that check, and records the caller in the note. |

## Request parameters

| Name | Type | Required | Notes |
| --- | --- | --- | --- |
| `Target` | EntityReference (`cd_case`) | yes (binding) | The case to escalate |
| `Reason` | String | no | Free text, max 500 chars; default "No reason given" |

## Response properties

| Name | Type | Notes |
| --- | --- | --- |
| `Escalated` | Boolean | `true` if changes were made, `false` if already escalated |
| `Message` | String | Human-readable result shown in the form notification |

## Behaviour

1. Load case; if `statecode` = Inactive or `statuscode` = Resolved → throw
   `InvalidPluginExecutionException("Only open cases can be escalated.")`.
2. Resolve team by name from env var `cd_SupervisorTeamName`; missing → throw
   `"The supervisor team '<name>' was not found. Ask an administrator to create it."`.
3. If priority = Critical **and** owner = that team → return `Escalated=false`,
   `Message="Case CD-00012 is already escalated."` (idempotent, no writes).
4. Otherwise, in one transaction: Update `cd_priority` = Critical (this triggers the
   pre-update plugin which recalculates Due Date / Warning On); Assign owner = team; Create
   `cd_casenote` {Internal, Is Open = No, text `"Escalated by {user fullname}: {reason}"`}.
5. Return `Escalated=true`, `Message="Case CD-00012 escalated to CaseDesk Supervisors."`.

## Web API example

```http
POST [org]/api/data/v9.2/cd_cases(<caseid>)/Microsoft.Dynamics.CRM.cd_EscalateCase
Content-Type: application/json

{ "Reason": "Customer threatened to cancel contract" }
```

```json
{ "Escalated": true, "Message": "Case CD-00012 escalated to CaseDesk Supervisors." }
```

## Client (ribbon) call

`cd_/scripts/case.ribbon.js` → `CaseDesk.Ribbon.escalate(primaryControl)` opens a small HTML
web resource dialog `cd_/html/escalate.html` (via `Xrm.Navigation.navigateTo`, target 2) that
asks for the reason, executes the request with `Xrm.WebApi.online.execute`, refreshes the form
and shows `Message` as a form notification. Enable rule: case is active and not Resolved.
