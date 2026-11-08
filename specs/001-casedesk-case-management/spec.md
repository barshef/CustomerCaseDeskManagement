# Feature Specification: CaseDesk — Customer Case Management

**Feature Branch**: `001-casedesk-case-management`

**Created**: 2026-11-08

**Status**: Draft

**Input**: User description: "Using the BRD 'CaseDesk — Customer Case Management on Dynamics 365 CE', build the app: a small service team logs, assigns, works and closes customer cases against an SLA, with the customer notified at each step and an external system told when a case closes."

## Clarifications

### Session 2026-11-08

- Q: Should SLA hours count every hour of the day (24×7), or only business hours? → A: 24×7
  calendar hours; due date = created on + Resolution Hours.
- Q: Who should receive the approval request when an agent resolves a Refund case? → A: Every
  Supervisor in the case owner's business unit (all supervisors if that unit has none); the
  first response decides.
- Q: When an agent clicks Escalate, who should the case be assigned to? → A: A single
  "CaseDesk Supervisors" team in the root business unit (the "supervisor queue").
- Q: For the 5-day auto-close, how should CaseDesk know the customer has replied after a case
  was resolved? → A: Both signals — the case was reopened (moved back to In Progress) or an
  inbound email regarding the case was received after Resolved On; either one stops auto-close.
- Q (owner request, mid-implementation): Can we run CaseDesk in a browser with Playwright and
  make a guide film, as for Rotaro and Moniqora? → A: Yes — added as User Story 11 / FR-017
  (BRD FR15), following the same pattern: headed runs, caption band, chapter cards, optional
  Azure speech voice-over, films committed under `e2e/demo-out/`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Log a case with an automatic SLA due date (Priority: P1)

An agent takes a call or email from a customer and logs a case for them: picks the customer
(organisation and/or contact person), gives it a title, description, category and priority.
On save the case gets a unique, human-readable case number and a due date calculated from the
SLA policy for its priority. The agent never types either value.

**Why this priority**: Without logging cases there is no product. Case number and due date are
the backbone that every later story (SLA warnings, dashboards, escalation) depends on.

**Independent Test**: Create a High priority Billing case for a customer and confirm the case
number (format `CD-00001`) and due date (created time + High resolution hours) are filled in
without user input.

**Acceptance Scenarios**:

1. **Given** SLA policies exist for every priority, **When** an agent saves a new High priority
   Billing case with a title and customer, **Then** the case receives the next case number in
   the `CD-NNNNN` sequence and a due date equal to its creation time plus the High policy's
   resolution hours.
2. **Given** a new case form, **When** the agent tries to save without a title or without a
   customer, **Then** the save is blocked and the missing fields are indicated.
3. **Given** a new case with Category = Refund, **When** the agent views the form, **Then** a
   Refund Amount field is shown and required; for any other category it is hidden and not
   required.
4. **Given** a saved case, **When** anyone tries to change its case number, **Then** the change
   is not possible.
5. **Given** no SLA policy exists for the chosen priority, **When** the agent saves the case,
   **Then** the save is blocked with a message telling the agent that the SLA policy for that
   priority is missing.

---

### User Story 2 - Work a case through its lifecycle with notes (Priority: P1)

An agent works the case: moves it through New → Assigned → In Progress → Awaiting Customer →
Resolved → Closed, adds internal or customer-facing notes, and marks notes open (needing
follow-up) or closed. A case cannot be resolved while any of its notes is still open. A case
can be cancelled from any active state.

**Why this priority**: This is the daily work of the service team; together with Story 1 it is
the minimum viable product.

**Independent Test**: Add an open internal note to a case and try to resolve it — blocked with
a clear message; close the note, resolve again — succeeds.

**Acceptance Scenarios**:

1. **Given** a case with one note marked open, **When** the agent sets the case to Resolved
   (from the form, a bulk edit or an integration), **Then** the change is rejected with a
   message stating that open notes must be closed first.
2. **Given** the same case after the note is marked closed, **When** the agent resolves it with
   a resolution summary, **Then** the case becomes Resolved and the resolved-on time is
   recorded.
3. **Given** an active case in any state other than Resolved or Closed, **When** the agent
   cancels it, **Then** it becomes Cancelled and no longer counts toward SLA metrics.
4. **Given** a Resolved or Closed case, **When** the agent tries to cancel it, **Then** the
   action is not available.
5. **Given** a case is deleted by an administrator, **When** the deletion completes, **Then** all
   of its notes are deleted with it.

---

### User Story 3 - Customer is emailed on creation and closure (Priority: P2)

The customer contact receives an email when their case is logged (including the case number
and due date) and another when it is closed (including the resolution summary). Emails are
recorded on the case history.

**Why this priority**: Keeps the customer informed without agent effort; visible business
value in the demo but not required to work cases.

**Independent Test**: Create a case for a contact with an email address and confirm the
creation email arrives within 1 minute and appears on the case timeline.

**Acceptance Scenarios**:

1. **Given** a case is created for a contact with an email address, **When** the case is
   saved, **Then** the contact receives a creation email within 1 minute containing the case
   number, title and due date, and the email is visible in the case history.
2. **Given** a case moves to Closed, **When** the change is saved, **Then** the contact receives
   a closure email containing the case number and resolution summary.
3. **Given** the case has no requester contact, or the contact has no email address, **When**
   the email would be sent, **Then** no email is sent and the case history records why.

---

### User Story 4 - Refund cases need supervisor approval (Priority: P2)

When an agent resolves a Refund case, it first goes to Awaiting Approval. A supervisor receives
an approval request (in Teams or email), approves or rejects it with a comment, and the
decision is recorded. Approved → the case becomes Resolved. Rejected → the case returns to In
Progress and the agent is told why.

**Why this priority**: A control requirement — refunds are money — and a distinct business rule
in the BRD, but not needed for non-refund cases.

**Independent Test**: Create a Refund case, resolve it, confirm it lands in Awaiting Approval;
approve as a supervisor and confirm it becomes Resolved with an approval record.

**Acceptance Scenarios**:

1. **Given** a Refund case with no open notes, **When** an agent sets it to Resolved, **Then** it
   is placed in Awaiting Approval instead, and an approval record with decision Pending is
   created naming the requesting agent.
2. **Given** a pending approval, **When** a supervisor approves it, **Then** the approval record
   shows Approved, approver, decision date and comment, and the case becomes Resolved.
3. **Given** a pending approval, **When** a supervisor rejects it, **Then** the approval shows
   Rejected and the case returns to In Progress with the rejection comment visible to the
   agent.
4. **Given** a Refund case in Awaiting Approval, **When** an agent tries to set it to Resolved
   or Closed directly, **Then** the change is rejected.

---

### User Story 5 - SLA visibility, warnings and breach digest (Priority: P2)

On the case form the agent sees a live countdown badge to the due date: green while comfortably
inside the SLA, amber inside the warning window, red once breached. Agents are warned shortly
before a case they own breaches, and supervisors receive a daily digest of breached cases.

**Why this priority**: SLAs are the reason the team uses CaseDesk rather than a spreadsheet;
depends on Story 1's due date.

**Independent Test**: Open a case whose due date is inside its warning window and confirm the
badge is amber; move the due date into the past and confirm red.

**Acceptance Scenarios**:

1. **Given** an open case whose due date is more than the policy's warning hours away, **When**
   the form is open, **Then** the badge is green and shows the remaining time, updating at least
   every minute without reloading.
2. **Given** an open case inside its warning window, **Then** the badge is amber; **given** a
   case past its due date, **then** the badge is red and shows time overdue.
3. **Given** a Resolved, Closed or Cancelled case, **Then** the badge shows the final outcome
   (met or breached) and stops counting.
4. **Given** an open case enters its warning window, **When** the warning check runs, **Then**
   the owning agent is notified once for that case (not repeatedly).
5. **Given** the end of each day, **When** the digest runs, **Then** each supervisor receives a
   list of cases breached in their scope, or no message if there are none.

---

### User Story 6 - Escalate a case in one action (Priority: P3)

An agent clicks Escalate on a case. In one operation the priority becomes Critical, the case is
reassigned to the supervisor queue, and a note recording who escalated and why is added. Either
all three happen or none do. The same operation is available to automation and outside callers.

**Why this priority**: Valuable for urgent cases and a showcase of a reusable server operation,
but the team can escalate manually without it.

**Independent Test**: Click Escalate on a Medium case and confirm priority Critical, new owner,
and an escalation note — from a single action.

**Acceptance Scenarios**:

1. **Given** an open Medium case owned by an agent, **When** the agent escalates it with a
   reason, **Then** priority becomes Critical, the owner becomes the supervisor queue, and an
   internal note "Escalated by <agent>: <reason>" is added, all together.
2. **Given** any step of the escalation fails, **Then** none of the changes are kept and the
   agent sees an error.
3. **Given** an already Critical case owned by the supervisor queue, **When** Escalate is
   invoked again, **Then** no duplicate changes or notes are made and the agent is told it is
   already escalated.
4. **Given** a Resolved, Closed or Cancelled case, **Then** Escalate is not available.

---

### User Story 7 - Closed cases are announced to an external system (Priority: P3)

When a case is Closed, a CaseClosed message (case number, customer, category, priority,
created, resolved and closed times, SLA met or breached) is delivered to an external system
within 1 minute. If the external system is unavailable, delivery is retried without affecting
the agent.

**Why this priority**: The only outbound integration; demonstrates decoupling but no agent
depends on it.

**Independent Test**: Close a case and confirm the external stub logs the CaseClosed payload
within 1 minute.

**Acceptance Scenarios**:

1. **Given** a Resolved case, **When** it is Closed, **Then** the external stub receives exactly
   one CaseClosed message for it within 1 minute.
2. **Given** the external endpoint is down, **When** a case is Closed, **Then** the close
   succeeds immediately for the agent and the message is delivered once the endpoint recovers.
3. **Given** a delivery is retried, **Then** the external system can recognise duplicates by a
   stable message identifier.

---

### User Story 8 - Resolved cases auto-close (Priority: P3)

A Resolved case with no customer reply for 5 days is closed automatically, triggering the
closure email and the CaseClosed message as if an agent had closed it.

**Why this priority**: Housekeeping that keeps queues clean; manual closing works without it.

**Independent Test**: Set a case's resolved-on date to 6 days ago, run the auto-close, and
confirm it becomes Closed.

**Acceptance Scenarios**:

1. **Given** a case Resolved more than 5 days ago with no customer reply since, **When** the
   daily auto-close runs, **Then** the case becomes Closed.
2. **Given** a case Resolved 3 days ago, **Then** it is left Resolved.
3. **Given** a case Resolved 6 days ago with an inbound customer email received after it was
   resolved, **When** auto-close runs, **Then** it stays Resolved and its owner is notified to
   review the reply.

---

### User Story 9 - Dashboards and access by business unit (Priority: P3)

Agents see a dashboard of their own open cases by priority; supervisors see a dashboard of team
SLA breaches this week. Agents only see cases in their own business unit; supervisors see all.
Only supervisors can reassign cases, approve refunds and edit SLA policies; nobody but an
administrator can delete cases.

**Why this priority**: Configuration-level work that rounds out the product; access control is
important but the platform enforces it once configured.

**Independent Test**: Log in as an agent in Business Unit A and confirm a case owned in
Business Unit B cannot be opened; log in as a supervisor and confirm it can.

**Acceptance Scenarios**:

1. **Given** an agent in Business Unit A, **When** they search for or open a case owned in
   Business Unit B, **Then** it is not visible.
2. **Given** a supervisor, **When** they open the same case, **Then** it is visible and editable.
3. **Given** an agent, **When** they try to delete a case or edit an SLA policy, **Then** they are
   not permitted.
4. **Given** an agent opens their dashboard, **Then** they see a chart of their open cases by
   priority and a list of their open cases ordered by due date.
5. **Given** a supervisor opens their dashboard, **Then** they see cases that breached SLA this
   week (Monday 00:00 to now) by agent and by priority.

---

### User Story 10 - Repeatable deployment and demo data (Priority: P3)

When a change is merged to the main branch, automated tests run and, if they pass, the solution
is deployed to the UAT environment as a managed solution. Demo data (accounts, contacts, SLA
policies, sample cases) can be loaded into an environment with one command.

**Why this priority**: Enables reliable demos and is an explicit ALM requirement, but delivers
no end-user feature on its own.

**Independent Test**: Merge a trivial change and confirm the pipeline runs tests and imports
the managed solution into UAT.

**Acceptance Scenarios**:

1. **Given** a merge to main, **When** tests pass, **Then** the managed solution is imported into
   UAT without manual steps.
2. **Given** a failing test, **Then** nothing is deployed and the pipeline reports the failure.
3. **Given** an empty environment with the solution installed, **When** the seeding command runs,
   **Then** SLA policies for all four priorities, sample customers and sample cases exist, and
   re-running it does not create duplicates.

---

### User Story 11 - Browser walkthroughs and guide films (Priority: P3)

The owner runs one command and watches CaseDesk being used in a visible browser, the way a
real agent, an agent in another business unit and a supervisor would use it: each demo-script
step is performed through the app's screens, with a caption at the top of the screen saying
what is happening. The run is recorded and turned into a guide film — a title card, one
chapter per step with its own chapter card, and optionally a spoken voice-over — that can be
shown to clients, used for interviews and reused for listing screenshots.

**Why this priority**: Proves the whole product works through the user interface, not only in
unit tests, and gives a repeatable demo; depends on a deployed environment, so it comes last.

**Independent Test**: Against a Dev environment with the solution and seed data installed, run
the walkthrough; every chapter passes in the visible browser and a film with one chapter per
step is written to the films folder.

**Acceptance Scenarios**:

1. **Given** a Dev environment with CaseDesk, seed data and the three demo users, **When** the
   owner starts the walkthrough, **Then** a visible browser performs demo steps 1–8 as the
   right persona for each step, and each step is reported pass or fail.
2. **Given** filming is turned on, **When** the walkthrough finishes, **Then** one film exists
   with a title card, a chapter card before each step and a caption band naming the persona
   and the action, and the film plays in a normal video player.
3. **Given** a voice-over key is configured on the PC, **When** a film is made, **Then** each
   caption is also spoken; **given** no key, **then** the film is made silent without failing.
4. **Given** a step fails, **When** the run ends, **Then** the failure names the chapter and
   keeps a screenshot and the recording up to the failure, and no film is published.
5. **Given** the walkthrough is run without filming, **Then** it completes as a plain browser
   test run in under 10 minutes.
6. **Given** sign-in needs multi-factor authentication, **When** the walkthrough is set up the
   first time, **Then** the owner signs in once per persona by hand and later runs reuse that
   sign-in until it expires; no password is stored in the repository.

### Edge Cases

- A case's priority is changed after creation: the due date is recalculated from the original
  creation time and the new priority's resolution hours, unless the case is already Resolved,
  Closed or Cancelled.
- An SLA policy's hours are edited: existing cases keep their due dates; only new cases and
  priority changes use the new value.
- Two SLA policies for the same priority: prevented — only one policy per priority may exist.
- Category is changed away from Refund while Awaiting Approval: not allowed; the pending
  approval must be decided first.
- Category is changed to Refund on an already Resolved case: refund approval does not apply
  retroactively; only transitions into Resolved are checked.
- A note is added or reopened on a Resolved case: allowed (follow-up), but it does not move the
  case out of Resolved; it would block closing only if the business later decides so (not v1).
- Status set directly to Closed from an active state other than Resolved: rejected — a case must
  be Resolved before Closed.
- The same status update is retried by an integration: produces the same result with no
  duplicate notes, approvals, emails or CaseClosed messages.
- Customer replies after resolution: the agent reopens the case to In Progress, which cancels
  the 5-day auto-close countdown.
- The supervisor queue has no members: escalation still succeeds; supervisors see the case in
  the queue view.
- After escalation the case belongs to the root business unit, so the escalating agent can no
  longer open it (expected; supervisors own it from then on). The escalation note records the
  agent for audit.

## Requirements *(mandatory)*

### Functional Requirements

**Case capture**

- **FR-001**: System MUST give every new case a unique, sequential case number in the format
  `CD-NNNNN` (starting `CD-00001`) that no user can edit.
- **FR-002**: System MUST require Title and Customer on every case; Refund Amount MUST be shown
  and required only when Category = Refund, and hidden otherwise. The rule MUST also hold for
  records written outside the form.
- **FR-003**: System MUST set a case's Due Date on creation to the creation time plus the
  Resolution Hours of the SLA policy whose priority matches the case, in the same save, so a
  case is never stored without a due date; MUST recalculate it when priority changes on an
  active case.
- **FR-003a**: System MUST block creating a case when no SLA policy exists for its priority, and
  MUST allow at most one SLA policy per priority.

**Lifecycle**

- **FR-004**: System MUST reject any change of a case to Resolved while any of its notes is
  open, regardless of which client or integration makes the change, with a clear message.
- **FR-004a**: System MUST enforce the lifecycle New → Assigned → In Progress ⇄ Awaiting
  Customer → (Awaiting Approval, Refund only) → Resolved → Closed, with Cancelled reachable from
  any state except Resolved and Closed, and In Progress reachable again from Resolved (reopen).
- **FR-004b**: System MUST record Resolved On and require a Resolution Summary when a case is
  resolved.
- **FR-005**: System MUST route a Refund case that is being resolved to Awaiting Approval,
  create a pending approval record, and request a decision from every Supervisor in the case
  owner's business unit (all supervisors if that unit has none), where the first response
  decides; on approval the case becomes Resolved, on rejection it returns to In Progress with
  the comment recorded.

**Notifications**

- **FR-006**: System MUST email the case's requester contact on case creation and on closure,
  and record the email on the case history.
- **FR-007**: System MUST notify the owning agent once when an open case enters its SLA warning
  window (Warning Hours of its SLA policy before the due date), and MUST send each supervisor a
  daily digest of breached cases.

**Agent tools**

- **FR-008**: System MUST show on the case form a live SLA indicator: green outside the warning
  window, amber inside it, red after the due date; final outcome (met/breached) once the case is
  no longer active.
- **FR-009**: System MUST provide an Escalate action that, atomically and idempotently, sets
  priority to Critical, assigns the case to the "CaseDesk Supervisors" team (the supervisor
  queue, in the root business unit) and adds an escalation note;
  the action MUST be callable from the form, from automation and from external callers.

**Integration and housekeeping**

- **FR-010**: System MUST publish exactly one logical CaseClosed message per closure to an
  external system within 1 minute, asynchronously and with retries, so that closing never waits
  on or fails because of the external system.
- **FR-011**: System MUST automatically close cases that have been Resolved for more than 5 days
  without a customer reply. A customer reply is either (a) the case being reopened to In
  Progress, or (b) an inbound email regarding the case received after Resolved On. A case with
  an inbound reply but not yet reopened stays Resolved and is flagged to its owner.

**Reporting and security**

- **FR-012**: System MUST provide an agent dashboard (my open cases by priority) and a supervisor
  dashboard (team SLA breaches this week).
- **FR-013**: System MUST restrict agents to cases in their own business unit and allow
  supervisors to see all cases; only supervisors may reassign cases, decide refund approvals and
  edit SLA policies; only administrators may delete cases.

**Quality and delivery**

- **FR-014**: All server-side business logic MUST have automated tests, and every merge to main
  MUST run them and, on success, deploy the managed solution to UAT.
- **FR-015**: System MUST provide a repeatable, idempotent way to load demo data (SLA policies,
  customers, cases) into an environment.
- **FR-016**: No secret (client secret, endpoint key) may be stored in the source repository or
  solution files.

**Walkthroughs and films**

- **FR-017**: System MUST provide an end-to-end browser walkthrough of demo-script steps 1–8,
  run as three personas (Agent in Business Unit A, Agent in Business Unit B, Supervisor), that
  always runs in a visible browser and can optionally record a guide film with a title card,
  chapter cards, a caption band and an optional voice-over. Films are kept in the repository's
  films folder; saved sign-ins, passwords and voice keys are never committed.

### Key Entities *(include if feature involves data)*

- **Case**: A customer issue being worked. Case number, title, description, category (Billing,
  Delivery, Product, Refund, Other), priority (Low, Medium, High, Critical), status, due date,
  resolved on, resolution summary, refund amount. Belongs to one customer organisation, may name
  one requester contact, uses one SLA policy, is owned by one agent or the supervisor queue, has
  many notes and approvals.
- **Case Note**: A note on a case. Text, type (Internal, Customer-facing), open flag. Belongs to
  exactly one case and is deleted with it.
- **SLA Policy**: Service target for one priority. Name, priority, resolution hours, warning
  hours. One per priority; edited by supervisors.
- **Refund Approval**: The decision on a refund. Case, requested by, approver, decision
  (Pending, Approved, Rejected), decision date, comment.
- **Customer (Account / Contact)**: Existing organisation and person records, with name and
  email; a case links to them.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An agent can log a complete case in under 1 minute, with case number and due date
  appearing with zero manual entry in 100% of cases.
- **SC-002**: 100% of attempts to resolve a case with open notes, or to resolve/close a refund
  case without approval, are blocked — from the form and from integrations.
- **SC-003**: Customers receive the creation email within 1 minute in 95% of cases.
- **SC-004**: The external system receives the CaseClosed message within 1 minute of closure in
  95% of cases, and eventually in 100% after an outage.
- **SC-005**: Escalation completes in a single action and takes under 3 seconds as perceived by
  the agent.
- **SC-006**: Saving a case takes under 2 seconds as perceived by the agent, with 50 agents
  working concurrently and 10,000 open cases.
- **SC-007**: All 9 steps of the BRD demo script pass in UAT end to end.
- **SC-008**: A merge to main reaches UAT with no manual steps in under 20 minutes.
- **SC-009**: The browser walkthrough passes all 8 chapters in Dev, and a complete guide film
  (one chapter per step) is produced from a single command.

## Assumptions

- Hours in SLA policies are calendar hours (24×7) (confirmed in Clarifications); business-hours
  calendars and holidays are out of scope for v1.
- Seed SLA values: Critical 4h / warn 1h, High 8h / warn 2h, Medium 24h / warn 2h, Low 72h /
  warn 2h; supervisors can change them. The BRD's "2 hours before breach" is the default Warning
  Hours, not a fixed constant.
- "Customer" on a case means the organisation (Account) and is mandatory; the requester Contact
  is optional but needed for customer emails.
- Agents may set any forward status manually; Assigned is set when the case gets an owner other
  than its creator or the queue.
- The supervisor queue is the "CaseDesk Supervisors" team in the root business unit, whose
  members are all supervisors (confirmed in Clarifications).
- Inbound customer emails are tracked against the case (regarding = case) by mailbox
  synchronisation; if incoming sync is not available in an environment, auto-close still works
  using the reopen signal alone.
- The supervisor dashboard week runs Monday 00:00 to now, in the organisation's time zone.
- Agent SLA warnings are delivered as an in-app notification plus email; a check every 15
  minutes is accurate enough for the "about 2 hours before" warning.
- The external system is a stub owned by the team; it accepts a JSON message over HTTPS with a
  shared key.
- Customer self-service, knowledge base, omnichannel, telephony and Dynamics 365 Customer
  Service licensing are out of scope (per BRD).
- Walkthrough personas are real Dataverse users in the Dev environment (`agent.a`, `agent.b`,
  `supervisor`) with the CaseDesk roles; the walkthrough does not create users. Step 9 (pipeline
  deploy) is not filmed in the browser.
- Environments: Dev and UAT on developer plans; the AppSource listing is a post-v1 activity and
  not part of this feature.
