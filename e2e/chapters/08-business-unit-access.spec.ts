// Quickstart §4 step 8 — User Story 9 (FR-013, access by business unit).
import { test, expect } from '../demo/personas';
import { say } from '../demo/caption';
import { ensureApp, ensureBillingCase } from '../demo/cases';
import { SEL, canRead, getValue, openRecord, recordUrl } from '../demo/mda';
import { readState } from '../demo/run';
import { soon } from '../demo/soon';

test('08 · Agent B in another business unit cannot open Agent A’s case; the supervisor can', async ({ asPersona, chapter }) => {
  // The case comes from chapter 1; when run alone, Agent A makes one first.
  const theCase = readState().billingCase ?? (await ensureBillingCase(await asPersona('agentA')));

  const agentB = await asPersona('agentB');
  await ensureApp(agentB);
  await chapter(agentB, 'Each business unit sees its own cases', `Agent B, in CaseDesk BU B, tries to open ${theCase.caseNumber} from BU A`);

  await say(agentB, `Agent B has a link to ${theCase.caseNumber}, which belongs to Agent A in BU A, and opens it.`);
  await agentB.goto(recordUrl('cd_case', theCase.id));
  const refused = agentB.locator(SEL.errorDialog).first();
  expect(await soon(refused, 60_000), 'an access error is shown').toBe(true);
  const text = (await refused.innerText()).trim();
  expect(text).toMatch(/privilege|permission|access|not exist|unavailable|cannot|could not/i);
  expect(await canRead(agentB, 'cd_case', theCase.id), 'Web API read as Agent B is refused too').toBe(false);
  await say(agentB, 'Refused: Agent B has no access to cases in another business unit — in the app and through the API alike.');

  const supervisor = await asPersona('supervisor');
  await openRecord(supervisor, 'cd_case', theCase.id);
  await say(supervisor, 'The supervisor opens the same link.');
  expect(await getValue<string>(supervisor, 'cd_casenumber')).toBe(theCase.caseNumber);
  await say(supervisor, `The supervisor sees ${theCase.caseNumber}: supervisors see every business unit.`);
});
