// Quickstart §4 step 6 — User Story 6 (Escalate: Custom API cd_EscalateCase, contracts/escalate-case.md).
//
// After escalation the case belongs to the CaseDesk Supervisors team in the root business unit,
// so Agent A may no longer read it (spec edge case). The result is therefore checked as the
// supervisor.
import { test, expect } from '../demo/personas';
import { say } from '../demo/caption';
import { SEED, createCaseViaApi, ensureApp } from '../demo/cases';
import { PRIORITY, SEL, commandButton, dismissError, formNotification, getValue, openRecord, webQuery } from '../demo/mda';
import { soon, soonGone } from '../demo/soon';

const REASON = 'Customer threatened to cancel the contract';

test('06 · One click on Escalate makes the case Critical, gives it to CaseDesk Supervisors and writes the escalation note', async ({ asPersona, chapter }) => {
  const agent = await asPersona('agentA');
  const theCase = await createCaseViaApi(agent, { title: 'Bike frame cracked after a week', category: 'Product', priority: 'Medium', customer: SEED.adventureWorks });
  await openRecord(agent, 'cd_case', theCase.id);
  await chapter(agent, 'Escalate in one action', `Agent A escalates ${theCase.caseNumber}, a Medium case, to the supervisors`);

  await say(agent, 'The customer is angry. Agent A clicks Escalate on the command bar.');
  await commandButton(agent, 'Escalate').click();

  const dialog = agent.frameLocator(SEL.escalateFrame);
  const reason = dialog.getByRole('textbox').first();
  await expect(reason, 'escalate dialog asks for a reason').toBeVisible({ timeout: 30_000 });
  await say(agent, 'Agent A gives the reason and confirms.');
  await reason.fill(REASON);
  await dialog.getByRole('button', { name: /escalate/i }).first().click();
  await soonGone(agent.locator(SEL.escalateFrame), 30_000);

  // The form refreshes: either the result message, or (since Agent A has lost access) an error.
  const message = await formNotification(agent);
  if (message) expect(message).toMatch(/escalated/i);
  else if (await soon(agent.locator(SEL.errorDialog), 5_000)) await dismissError(agent);
  await say(agent, 'One action: the case now belongs to the CaseDesk Supervisors team, so it leaves Agent A’s business unit.');

  const supervisor = await asPersona('supervisor');
  await openRecord(supervisor, 'cd_case', theCase.id);
  await say(supervisor, `The supervisor opens ${theCase.caseNumber}.`);
  expect(await getValue<number>(supervisor, 'cd_priority'), 'priority').toBe(PRIORITY.Critical);
  const owner = await getValue<Array<{ name: string; entityType: string }>>(supervisor, 'ownerid');
  expect(owner?.[0]?.entityType, 'owner is a team').toBe('team');
  expect(owner?.[0]?.name, 'owner team').toBe('CaseDesk Supervisors');

  await ensureApp(supervisor);
  const notes = await webQuery(supervisor, 'cd_casenote', `?$select=cd_notetext,cd_isopen&$filter=_cd_caseid_value eq ${theCase.id} and startswith(cd_notetext,'Escalated by')`);
  expect(notes.length, 'escalation note').toBe(1);
  expect(String(notes[0].cd_notetext)).toContain(REASON);
  expect(notes[0].cd_isopen, 'escalation note is closed').toBe(false);

  await say(supervisor, `Priority Critical, owner CaseDesk Supervisors, and a note: "${notes[0].cd_notetext}".`);
});
