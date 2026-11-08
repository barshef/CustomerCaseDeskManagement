// Quickstart §4 step 1 — User Story 1 (FR-001, FR-003).
import { test, expect } from '../demo/personas';
import { say } from '../demo/caption';
import { SEL, getValue, openNewCase, save, setChoice, setLookup, setText, webRetrieve, currentRecordId } from '../demo/mda';
import { TITLE_PREFIX, stamp, writeState } from '../demo/run';
import { poll, soon } from '../demo/soon';

const HOUR = 3_600_000;
const SLACK = 3 * 60_000; // clock difference between plugin, server and this PC

test('01 · Agent A logs a High Billing case — the case number CD-NNNNN and the due date appear without typing them', async ({ asPersona, chapter }) => {
  const agent = await asPersona('agentA');
  await openNewCase(agent);
  await chapter(agent, 'Log a case', 'Agent A logs a High priority Billing case for Northwind Traders');

  await say(agent, 'Agent A opens a new case and types what the customer reported.');
  const title = `${TITLE_PREFIX} Invoice charged twice — ${stamp()}`;
  await setText(agent, 'cd_name', title);
  await setText(agent, 'cd_description', 'The customer was charged twice for invoice INV-2041 and wants one charge refunded.');

  await say(agent, 'Agent A picks Billing and High priority.');
  await setChoice(agent, 'cd_category', 'Billing');
  await setChoice(agent, 'cd_priority', 'High');

  await say(agent, 'Agent A picks the customer, Northwind Traders, and the requester, Alex Morgan.');
  await setLookup(agent, 'cd_accountid', 'Northwind', 'Northwind Traders');
  await setLookup(agent, 'cd_contactid', 'Alex Morgan');

  await say(agent, 'Case number and due date are left empty — Agent A never types them. Agent A saves.');
  await save(agent);

  const id = await currentRecordId(agent);
  const caseNumber = await poll(
    async () => {
      const value = await getValue<string | null>(agent, 'cd_casenumber').catch(() => null);
      return value || (await webRetrieve(agent, 'cd_case', id, '?$select=cd_casenumber')).cd_casenumber;
    },
    { timeout: 30_000, interval: 2_000, what: 'the case number' },
  );
  expect(caseNumber, 'auto-number format').toMatch(/^CD-\d{5}$/);

  const row = await webRetrieve(agent, 'cd_case', id, '?$select=createdon,cd_duedate,cd_warningon,_cd_slapolicyid_value');
  const created = Date.parse(row.createdon);
  const due = Date.parse(row.cd_duedate);
  const warning = Date.parse(row.cd_warningon);
  expect(Math.abs(due - created - 8 * HOUR), 'Due Date = created + 8 h (High policy)').toBeLessThan(SLACK);
  expect(Math.abs(due - warning - 2 * HOUR), 'Warning On = Due Date − 2 h').toBeLessThan(SLACK);
  expect(row._cd_slapolicyid_value, 'SLA policy linked').toBeTruthy();

  const numberOnForm = agent.locator(SEL.text('cd_casenumber')).first();
  if (await soon(numberOnForm, 5_000)) await expect(numberOnForm).toHaveValue(caseNumber);
  const dueOnForm = agent.locator(SEL.dateTime('cd_duedate')).first();
  if (await soon(dueOnForm, 5_000)) await expect(dueOnForm).not.toHaveValue('');

  await say(agent, `Saved as ${caseNumber}. The due date is 8 hours from now, with a warning 2 hours before — both from the High SLA policy.`);

  writeState({ billingCase: { id, caseNumber, title }, billingCaseResolved: false });
});
