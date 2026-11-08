// Quickstart §4 step 2 — User Story 2 (CD004 guard, Resolved On).
import { test, expect } from '../demo/personas';
import { say } from '../demo/caption';
import { ensureBillingCase } from '../demo/cases';
import {
  SEL,
  STATUS,
  currentRecordId,
  discardChanges,
  dismissError,
  getValue,
  openNewChildForm,
  openRecord,
  save,
  setStatusReason,
  setText,
  setYesNo,
  trySave,
  webRetrieve,
} from '../demo/mda';
import { writeState } from '../demo/run';
import { soon } from '../demo/soon';

test('02 · An open note blocks Resolve with CD004; Agent A closes the note and the case resolves', async ({ asPersona, chapter }) => {
  const agent = await asPersona('agentA');
  const theCase = await ensureBillingCase(agent);

  await openRecord(agent, 'cd_case', theCase.id);
  await chapter(agent, 'An open note blocks Resolve', `Agent A works ${theCase.caseNumber} and tries to resolve it too early`);

  await say(agent, 'Agent A adds an internal note and marks it open: the bank is still checking the second charge.');
  await openNewChildForm(agent, 'cd_casenote', { entityType: 'cd_case', id: theCase.id, name: theCase.title });
  const subject = agent.locator(SEL.text('cd_name')).first();
  if (await soon(subject, 3_000)) await setText(agent, 'cd_name', 'Waiting for the bank');
  await setText(agent, 'cd_notetext', 'Waiting for the bank to confirm the duplicate charge before refunding.');
  await setYesNo(agent, 'cd_isopen', true);
  await save(agent);
  const noteId = await currentRecordId(agent);

  await openRecord(agent, 'cd_case', theCase.id);
  await say(agent, 'Agent A writes a resolution summary, sets the status to Resolved and saves.');
  await setText(agent, 'cd_resolutionsummary', 'Duplicate charge refunded to the original card.');
  await setStatusReason(agent, 'Resolved');
  const refused = await trySave(agent);
  expect(refused.ok, 'resolve with an open note is refused').toBe(false);
  if (!refused.ok) expect(refused.error).toContain('CD004');
  await say(agent, 'CaseDesk refuses: CD004 — the case has an open note. The rule runs on the server, for every app and integration.');
  await dismissError(agent);
  await discardChanges(agent);

  await say(agent, 'Agent A opens the note and closes it.');
  await openRecord(agent, 'cd_casenote', noteId);
  await setYesNo(agent, 'cd_isopen', false);
  await save(agent);

  await say(agent, 'Back on the case, Agent A resolves it again.');
  await openRecord(agent, 'cd_case', theCase.id);
  await setText(agent, 'cd_resolutionsummary', 'Duplicate charge refunded to the original card.');
  await setStatusReason(agent, 'Resolved');
  await save(agent);

  const row = await webRetrieve(agent, 'cd_case', theCase.id, '?$select=statuscode,cd_resolvedon');
  expect(row.statuscode, 'status reason').toBe(STATUS.Resolved);
  expect(row.cd_resolvedon, 'Resolved On is set').toBeTruthy();
  expect(await getValue<number>(agent, 'statuscode')).toBe(STATUS.Resolved);
  await say(agent, `${theCase.caseNumber} is Resolved, and Resolved On is filled in by CaseDesk.`);

  writeState({ billingCaseResolved: true });
});
