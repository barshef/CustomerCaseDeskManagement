// Quickstart §4 step 7 — User Story 7 (CaseClosed → Service Bus/webhook → Azure Function → external stub).
//
// The external system is the ExternalStub function; its GET endpoint (contracts/azure-functions.md)
// lists what it received for a case number. The function key is sent as a header from Node and
// never put in a URL the browser shows, so it cannot appear in a film.
import { test, expect } from '../demo/personas';
import { fastForward, say, showPanel } from '../demo/caption';
import { ensureResolvedCase } from '../demo/cases';
import { SEL, STATUS, commandButton, openRecord, webRetrieve } from '../demo/mda';
import { poll, soon } from '../demo/soon';

const LIMIT_MS = 90_000;

async function stubMessages(caseNumber: string): Promise<unknown> {
  const url = process.env.STUB_URL;
  if (!url || url.includes('<')) throw new Error('STUB_URL is not set in e2e/.env.');
  const headers: Record<string, string> = { accept: 'application/json' };
  if (process.env.STUB_KEY) headers['x-functions-key'] = process.env.STUB_KEY;
  const response = await fetch(`${url}?caseNumber=${encodeURIComponent(caseNumber)}`, { headers });
  if (!response.ok) throw new Error(`Stub answered ${response.status}`);
  return response.json();
}

/** True when the stub's answer holds at least one message for this case. */
function hasMessage(body: unknown, caseNumber: string): boolean {
  if (Array.isArray(body)) return body.length > 0 && JSON.stringify(body).includes(caseNumber);
  if (body && typeof body === 'object') {
    const list = Object.values(body as Record<string, unknown>).find(Array.isArray) as unknown[] | undefined;
    if (list) return list.length > 0 && JSON.stringify(list).includes(caseNumber);
    return JSON.stringify(body).includes(caseNumber);
  }
  return false;
}

test('07 · Closing a resolved case sends a CaseClosed message that reaches the external system within 90 seconds', async ({ asPersona, chapter }) => {
  const agent = await asPersona('agentA');
  const theCase = await ensureResolvedCase(agent);
  await openRecord(agent, 'cd_case', theCase.id);
  await chapter(agent, 'Closing tells the outside world', `Agent A closes ${theCase.caseNumber}; an Azure Function passes a CaseClosed message on`);

  await say(agent, `${theCase.caseNumber} is Resolved. Agent A closes it with Deactivate.`);
  await commandButton(agent, 'Deactivate').click();
  const dialog = agent.locator(SEL.deactivateDialog).first();
  await expect(dialog).toBeVisible();
  const reason = agent.locator(SEL.deactivateReason).first();
  if (await soon(reason, 3_000)) {
    const tag = await reason.evaluate((el) => el.tagName.toLowerCase());
    if (tag === 'select') await reason.selectOption({ label: 'Closed' });
    else {
      await reason.click();
      await agent.getByRole('option', { name: 'Closed', exact: true }).click();
    }
  }
  await dialog.getByRole('button', { name: /^deactivate$/i }).or(agent.locator(SEL.deactivateConfirm)).first().click();

  const closed = await poll(
    async () => {
      const row = await webRetrieve(agent, 'cd_case', theCase.id, '?$select=statecode,statuscode,cd_closedon');
      return row.statuscode === STATUS.Closed ? row : undefined;
    },
    { timeout: 30_000, interval: 2_000, what: 'the case to be Closed' },
  );
  expect(closed.cd_closedon, 'Closed On is set').toBeTruthy();
  const closedAt = Date.parse(closed.cd_closedon);
  await say(agent, 'Closed. CaseDesk publishes a CaseClosed message; an Azure Function passes it to the external system.');

  const body = await fastForward(agent, () =>
    poll(
      async () => {
        const answer = await stubMessages(theCase.caseNumber);
        return hasMessage(answer, theCase.caseNumber) ? answer : undefined;
      },
      { timeout: LIMIT_MS + 15_000, interval: 5_000, what: `the stub to receive CaseClosed for ${theCase.caseNumber}` },
    ),
  );
  expect(Date.now() - closedAt, 'received within 90 s (+ polling interval)').toBeLessThanOrEqual(LIMIT_MS + 15_000);

  const pretty = JSON.stringify(body, null, 2);
  await showPanel(agent, `External system received CaseClosed for ${theCase.caseNumber}`, pretty.length > 1800 ? `${pretty.slice(0, 1800)}\n…` : pretty);
  await say(agent, 'The external system has the CaseClosed message for this case.');
});
