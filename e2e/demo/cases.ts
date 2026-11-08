/**
 * Seed data the walkthrough relies on (tools/CaseDesk.Seeder/SeedData.cs) and the set-up a
 * chapter needs when it runs on its own (`npm run walk -- 07`): if an earlier chapter left a
 * case in walkthrough-state.json it is reused, otherwise one is created through the Web API as
 * the persona. Set-up through the Web API is not filmed as clicks; the UI path for creating a
 * case is shown in chapter 1.
 */
import { expect, type Page } from '@playwright/test';
import { CATEGORY, PRIORITY, STATUS, appUrl, cleanId, waitForAppShell, webCreate, webQuery, webRetrieve, webUpdate } from './mda';
import { TITLE_PREFIX, readState, stamp, writeState } from './run';
import { poll } from './soon';

export const SEED = {
  northwind: { accountNumber: 'CD-ACC-001', name: 'Northwind Traders (demo)', contactEmail: 'alex.morgan@example.com', contactName: 'Alex Morgan' },
  fabrikam: { accountNumber: 'CD-ACC-002', name: 'Fabrikam Logistics (demo)', contactEmail: 'sam.patel@example.com', contactName: 'Sam Patel' },
  tailspin: { accountNumber: 'CD-ACC-003', name: 'Tailspin Toys (demo)', contactEmail: 'jordan.lee@example.com', contactName: 'Jordan Lee' },
  adventureWorks: { accountNumber: 'CD-ACC-004', name: 'Adventure Works Cycles (demo)', contactEmail: 'casey.nguyen@example.com', contactName: 'Casey Nguyen' },
} as const;

export type SeedCustomer = (typeof SEED)[keyof typeof SEED];

export interface CaseRef {
  id: string;
  caseNumber: string;
  title: string;
}

/** Makes sure the page has the app loaded (Xrm.WebApi is only there inside the app). */
export async function ensureApp(page: Page): Promise<void> {
  const ready = await page.evaluate(() => Boolean((window as any).Xrm?.WebApi)).catch(() => false);
  if (ready) return;
  await page.goto(appUrl('pagetype=entitylist&etn=cd_case'));
  await waitForAppShell(page);
}

async function idOf(page: Page, entity: 'account' | 'contact', filter: string, what: string): Promise<string> {
  const idField = `${entity}id`;
  const rows = await webQuery(page, entity, `?$select=${idField}&$filter=${filter}&$top=1`);
  if (!rows.length) throw new Error(`Seed ${what} not found. Run the seeder (tools/CaseDesk.Seeder) against this environment.`);
  return cleanId(String(rows[0][idField]));
}

/** Creates a [Walkthrough] case through the Web API as the persona on `page`. */
export async function createCaseViaApi(
  page: Page,
  options: { title: string; category: keyof typeof CATEGORY; priority: keyof typeof PRIORITY; customer: SeedCustomer; refundAmount?: number },
): Promise<CaseRef> {
  await ensureApp(page);
  const accountId = await idOf(page, 'account', `accountnumber eq '${options.customer.accountNumber}'`, `account ${options.customer.accountNumber}`);
  const contactId = await idOf(page, 'contact', `emailaddress1 eq '${options.customer.contactEmail}'`, `contact ${options.customer.contactEmail}`);
  const title = `${TITLE_PREFIX} ${options.title} — ${stamp()}`;
  const data: Record<string, unknown> = {
    cd_name: title,
    cd_category: CATEGORY[options.category],
    cd_priority: PRIORITY[options.priority],
    'cd_accountid@odata.bind': `/accounts(${accountId})`,
    'cd_contactid@odata.bind': `/contacts(${contactId})`,
  };
  if (options.refundAmount !== undefined) data.cd_refundamount = options.refundAmount;
  const id = await webCreate(page, 'cd_case', data);
  const row = await webRetrieve(page, 'cd_case', id, '?$select=cd_casenumber');
  return { id, caseNumber: String(row.cd_casenumber), title };
}

/** Chapter 1's High Billing case, or a new one made by Agent A if chapter 1 has not run. */
export async function ensureBillingCase(agentA: Page): Promise<CaseRef> {
  const known = readState().billingCase;
  if (known) return known;
  const created = await createCaseViaApi(agentA, { title: 'Invoice charged twice', category: 'Billing', priority: 'High', customer: SEED.northwind });
  writeState({ billingCase: created, billingCaseResolved: false });
  return created;
}

/** A Resolved case owned by Agent A: chapter 2's, or one resolved here through the Web API. */
export async function ensureResolvedCase(agentA: Page): Promise<CaseRef> {
  const state = readState();
  if (state.billingCase && state.billingCaseResolved) return state.billingCase;
  const created = await createCaseViaApi(agentA, { title: 'Delivery arrived damaged', category: 'Delivery', priority: 'Medium', customer: SEED.northwind });
  await webUpdate(agentA, 'cd_case', created.id, {
    cd_resolutionsummary: 'Replacement sent; customer confirmed it arrived.',
    statuscode: STATUS.Resolved,
  });
  const row = await webRetrieve(agentA, 'cd_case', created.id, '?$select=statuscode');
  expect(row.statuscode, 'set-up case is Resolved').toBe(STATUS.Resolved);
  return created;
}

/** Waits until the case reaches `statuscode` (server side), returning the row. */
export async function waitForStatus(page: Page, id: string, statuscode: number, timeout: number): Promise<Record<string, any>> {
  return poll(
    async () => {
      const row = await webRetrieve(page, 'cd_case', id, '?$select=statuscode,statecode,cd_resolvedon,cd_closedon');
      return row.statuscode === statuscode ? row : undefined;
    },
    { timeout, interval: 5_000, what: `case ${id} to reach status ${statuscode}` },
  );
}
