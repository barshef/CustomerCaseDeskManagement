# CaseDesk business rules (task T022)

The three business rules are in source as hand-written XAML in `solution/src/Workflows/`
(`*.xaml` + `*.xaml.data.xml`, workflow category 2). They follow the format of rules exported by
the designer, but nobody has imported them yet. The first import into Dev is the real test.

If the import fails on one of these files, or a rule imports but does nothing, delete the two
files for that rule, build it again in the maker portal as described below, then run
`pac solution sync` so the repo holds what the designer produced.

| Rule | Table | Scope (data.xml) | Files |
| --- | --- | --- | --- |
| CaseDesk - Refund amount visibility | Case (`cd_case`) | All Forms (`ProcessTriggerScope` 1, no form id) | `CaseDesk-Refundamountvisibility-7A05C865-…` |
| CaseDesk - Refund amount required | Case (`cd_case`) | Entity (`ProcessTriggerScope` 2) | `CaseDesk-Refundamountrequired-F389C89A-…` |
| CaseDesk - Warning hours less than resolution hours | SLA Policy (`cd_slapolicy`) | Entity (`ProcessTriggerScope` 2) | `CaseDesk-Warninghourslessthanresolutionhours-E6EC0DCB-…` |

The rules are imported **activated** (StateCode 1 / StatusCode 2).

## Rebuild in the maker portal

Open make.powerapps.com, go to **Solutions → CaseDesk → Tables → <table> → Business rules → New
business rule**. Set the name and the scope (top right of the designer), build the logic, then
**Save** and **Activate**.

### 1. CaseDesk - Refund amount visibility

- Table: **Case**. Scope: **All Forms**.
- **Condition** "Category equals Refund": Entity = Case, Field = **Category**, Operator = **Equals**,
  Type = **Value**, Value = **Refund**.
- **If true:**
  1. **Set Visibility**: Field = Refund Amount, *Show field* = **Yes**.
  2. **Set Business Required**: Field = Refund Amount, Status = **Business Required**.
- **If false:**
  1. **Set Visibility**: Field = Refund Amount, *Show field* = **No**.
  2. **Set Business Required**: Field = Refund Amount, Status = **Not Business Required**.
  3. **Set Field Value**: Field = Refund Amount, Type = **Clear**.

Refund Amount must be on the Case main form (General tab) for the show/hide to have an effect.

### 2. CaseDesk - Refund amount required

- Table: **Case**. Scope: **Entity**, so the rule also runs on the server for API writes.
- **Condition** "Refund case without refund amount", with two rules joined by **AND**:
  - Category **Equals** Value **Refund**
  - Refund Amount **Does not contain data**
- **If true:** **Show Error Message**: Field = Refund Amount,
  Message = `Enter the refund amount for a refund case.`

### 3. CaseDesk - Warning hours less than resolution hours

- Table: **SLA Policy**. Scope: **Entity**.
- **Condition**: Field = **Warning Hours**, Operator = **Is greater than or equal to**,
  Type = **Field**, Field = **Resolution Hours**.
- **If true:** **Show Error Message**: Field = Warning Hours,
  Message = `Warning Hours must be less than Resolution Hours.`

## Re-sync after rebuilding

```powershell
pac auth select --name <dev-profile>
pac solution sync --solution-folder solution --packagetype Both
```

Commit the regenerated files in `solution/src/Workflows/`. The designer assigns new workflow ids,
so also replace the matching `<RootComponent type="29" …>` entries in
`solution/src/Other/Solution.xml`. `pac solution sync` normally does this for you.

## Verify

1. On a new Case, choose Category = Refund. Refund Amount appears and is required. Switch to
   Billing: the field disappears and its value is cleared.
2. With the Web API (or Postman), create a `cd_case` with `cd_category` = 417200003 and no
   `cd_refundamount`. The request fails with "Enter the refund amount for a refund case."
3. Save an SLA Policy with Warning Hours 8 and Resolution Hours 8. The save is blocked with the
   warning-hours error.
