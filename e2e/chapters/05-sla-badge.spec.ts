// Quickstart §4 step 5 — User Story 5 (SLA badge PCF).
//
// Why the dates are set from the page and not by the seeder's --sla-demo: the seeder's amber
// case is only amber for the 90 minutes after it ran, and it picks the newest [Seed] cases,
// which other demos may have moved on. Instead the supervisor creates a [Walkthrough] case and
// moves its Due Date / Warning On through Xrm.WebApi from the signed-in page — the same direct
// date write the seeder uses, which works because the pre-update plugin (S2) does not fire on
// the date columns. Each run is therefore self-contained and repeatable.
import { test, expect } from '../demo/personas';
import { say } from '../demo/caption';
import { SEED, createCaseViaApi } from '../demo/cases';
import { SEL, openRecord, refreshForm, webUpdate } from '../demo/mda';

const MIN = 60_000;

test('05 · The SLA badge turns amber in the warning window and red once the case is overdue', async ({ asPersona, chapter }) => {
  const supervisor = await asPersona('supervisor');
  const theCase = await createCaseViaApi(supervisor, { title: 'Parcel stuck at the depot', category: 'Delivery', priority: 'Medium', customer: SEED.tailspin });

  // Into the warning window: due in 90 minutes, warning began 30 minutes ago.
  await webUpdate(supervisor, 'cd_case', theCase.id, {
    cd_duedate: new Date(Date.now() + 90 * MIN).toISOString(),
    cd_warningon: new Date(Date.now() - 30 * MIN).toISOString(),
  });
  await openRecord(supervisor, 'cd_case', theCase.id);
  await chapter(supervisor, 'The SLA badge', 'The badge on the case form shows how close a case is to its due date');

  const badge = supervisor.locator(SEL.slaBadge).first();
  await expect(badge, 'SLA badge on the form').toBeVisible();
  await badge.scrollIntoViewIfNeeded();
  await say(supervisor, `${theCase.caseNumber} is due in 90 minutes and its warning time has passed.`);
  await expect(badge).toHaveAttribute('data-sla-kind', 'amber');
  await say(supervisor, 'The SLA badge is amber: inside the warning window.');

  // Past due: due an hour ago.
  await say(supervisor, 'Now the due date passes.');
  await webUpdate(supervisor, 'cd_case', theCase.id, {
    cd_duedate: new Date(Date.now() - 60 * MIN).toISOString(),
    cd_warningon: new Date(Date.now() - 180 * MIN).toISOString(),
  });
  await refreshForm(supervisor);
  await expect(badge).toHaveAttribute('data-sla-kind', 'red', { timeout: 45_000 });
  await say(supervisor, 'The badge is red: the case is overdue.');
});
