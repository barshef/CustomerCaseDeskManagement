// Quickstart §4 step 4 — User Story 3 (customer email on creation, cloud flow).
//
// The requester's mailbox is a seeded @example.com address nobody can read, so the walkthrough
// checks what CaseDesk controls: the flow's Email row regarding the case, created within 90 s of
// the case, and shown on the case timeline.
import { test, expect } from '../demo/personas';
import { fastForward, say } from '../demo/caption';
import { ensureBillingCase } from '../demo/cases';
import { SEL, openRecord, webQuery, webRetrieve } from '../demo/mda';
import { poll } from '../demo/soon';

const LIMIT_MS = 90_000;

test('04 · The customer is emailed when the case is logged — the email is on the case timeline within 90 seconds', async ({ asPersona, chapter }) => {
  const agent = await asPersona('agentA');
  const theCase = await ensureBillingCase(agent);
  await openRecord(agent, 'cd_case', theCase.id);
  await chapter(agent, 'The customer hears from us', `A cloud flow emails Alex Morgan as soon as ${theCase.caseNumber} is logged`);

  await say(agent, 'When a case is logged, a cloud flow emails the requester. Agent A looks for it on the timeline.');
  const created = Date.parse((await webRetrieve(agent, 'cd_case', theCase.id, '?$select=createdon')).createdon);
  const waitMs = Math.max(10_000, created + LIMIT_MS + 15_000 - Date.now());

  const email = await fastForward(agent, () =>
    poll(
      async () => {
        const rows = await webQuery(agent, 'email', `?$select=subject,createdon,statuscode&$filter=_regardingobjectid_value eq ${theCase.id}&$orderby=createdon asc&$top=1`);
        return rows[0];
      },
      { timeout: waitMs, interval: 5_000, what: `the creation email regarding ${theCase.caseNumber}` },
    ),
  );
  const lag = Date.parse(email.createdon) - created;
  expect(lag, 'email created within 90 s of the case').toBeLessThanOrEqual(LIMIT_MS);

  await openRecord(agent, 'cd_case', theCase.id);
  const onTimeline = agent.locator(SEL.timeline).first().getByText(String(email.subject), { exact: false }).first();
  await expect(onTimeline, 'email shown on the timeline').toBeVisible({ timeout: 30_000 });
  await onTimeline.scrollIntoViewIfNeeded();

  await say(agent, `The email "${email.subject}" is on the timeline, sent ${Math.round(lag / 1000)} seconds after the case was logged.`);
});
