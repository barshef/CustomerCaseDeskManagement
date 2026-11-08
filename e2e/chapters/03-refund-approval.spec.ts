// Quickstart §4 step 3 — User Story 4 (refund approval).
//
// The approval itself is decided in Teams or Outlook (Approvals "first to respond"), which a
// browser walkthrough cannot drive reliably: a different app, its own sign-in, and a delay
// before the card arrives. So this chapter has two modes:
//
//   default            shows Resolve turning into Awaiting Approval and the Pending Refund
//                      Approval row, and asserts exactly that. The approval is not exercised.
//   APPROVE_BY_HAND=1  (for films) after the same steps, the walkthrough waits up to
//                      APPROVE_WAIT_MINUTES (default 10) for the owner to approve the request by
//                      hand as the supervisor in Teams/Outlook, then shows the case Resolved and
//                      the row Approved.
import { test, expect } from '../demo/personas';
import { fastForward, say } from '../demo/caption';
import { ensureApp, waitForStatus } from '../demo/cases';
import {
  DECISION,
  STATUS,
  currentRecordId,
  getValue,
  openNewCase,
  openRecord,
  refreshForm,
  save,
  setChoice,
  setLookup,
  setNumber,
  setStatusReason,
  setText,
  webQuery,
  webRetrieve,
} from '../demo/mda';
import { TITLE_PREFIX, stamp, writeState } from '../demo/run';
import { poll } from '../demo/soon';

const BY_HAND = process.env.APPROVE_BY_HAND === '1';
const WAIT_MINUTES = Number(process.env.APPROVE_WAIT_MINUTES ?? '10');

test('03 · A Refund case goes to Awaiting Approval on Resolve; the supervisor sees the Refund Approval request', async ({ asPersona, chapter }) => {
  if (BY_HAND) test.setTimeout((WAIT_MINUTES + 6) * 60_000);
  else test.info().annotations.push({ type: 'approval', description: 'Teams/Outlook approval not driven; asserts Awaiting Approval + Pending row only (set APPROVE_BY_HAND=1 to wait for a hand approval).' });

  const agent = await asPersona('agentA');
  await openNewCase(agent);
  await chapter(agent, 'Refund needs a supervisor', 'Agent A resolves a refund; CaseDesk asks a supervisor to approve it first');

  await say(agent, 'Agent A logs a Refund case. The Refund Amount field appears only for refunds.');
  const title = `${TITLE_PREFIX} Refund for a returned order — ${stamp()}`;
  await setText(agent, 'cd_name', title);
  await setChoice(agent, 'cd_category', 'Refund');
  await setNumber(agent, 'cd_refundamount', 50);
  await setChoice(agent, 'cd_priority', 'Medium');
  await setLookup(agent, 'cd_accountid', 'Fabrikam', 'Fabrikam Logistics');
  await setLookup(agent, 'cd_contactid', 'Sam Patel');
  await save(agent);
  const id = await currentRecordId(agent);
  const caseNumber = String((await webRetrieve(agent, 'cd_case', id, '?$select=cd_casenumber')).cd_casenumber);

  await say(agent, `Agent A writes a resolution summary for ${caseNumber}, sets Resolved and saves.`);
  await setText(agent, 'cd_resolutionsummary', 'Refund of 50.00 agreed with the customer for the returned order.');
  await setStatusReason(agent, 'Resolved');
  await save(agent);
  await refreshForm(agent);

  const status = await poll(
    async () => {
      const v = await getValue<number>(agent, 'statuscode');
      return v === STATUS.AwaitingApproval ? v : undefined;
    },
    { timeout: 30_000, interval: 2_000, what: 'status Awaiting Approval' },
  );
  expect(status).toBe(STATUS.AwaitingApproval);
  await say(agent, 'CaseDesk does not resolve it yet: the status is Awaiting Approval, because a refund needs a supervisor.');
  writeState({ refundCase: { id, caseNumber, title } });

  const supervisor = await asPersona('supervisor');
  await ensureApp(supervisor);
  const approval = await poll(
    async () => {
      const rows = await webQuery(supervisor, 'cd_refundapproval', `?$select=cd_refundapprovalid,cd_name,cd_decision&$filter=_cd_caseid_value eq ${id}&$orderby=createdon desc&$top=1`);
      return rows[0];
    },
    { timeout: 90_000, interval: 5_000, what: 'the Refund Approval row' },
  );
  expect(approval.cd_decision, 'decision').toBe(DECISION.Pending);

  await openRecord(supervisor, 'cd_refundapproval', approval.cd_refundapprovalid);
  await say(supervisor, `The supervisor sees the Refund Approval request for ${caseNumber}: decision Pending.`);

  if (!BY_HAND) {
    await say(supervisor, 'The supervisor approves it in Teams or Outlook; CaseDesk then resolves the case.');
    return;
  }

  await say(supervisor, `Now approve the request in Teams or Outlook. The walkthrough waits up to ${WAIT_MINUTES} minutes.`);
  await fastForward(supervisor, () => waitForStatus(supervisor, id, STATUS.Resolved, WAIT_MINUTES * 60_000));
  await refreshForm(supervisor);
  expect(await getValue<number>(supervisor, 'cd_decision')).toBe(DECISION.Approved);
  await say(supervisor, 'Approved. The Refund Approval row records the decision…');
  await openRecord(supervisor, 'cd_case', id);
  expect(await getValue<number>(supervisor, 'statuscode')).toBe(STATUS.Resolved);
  await say(supervisor, `…and ${caseNumber} is Resolved.`);
});
