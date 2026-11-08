/**
 * Model-driven app helpers. EVERY selector for the Dynamics 365 / Power Apps UI lives in SEL
 * below, so after the first real run against Dev the fixes are made here and nowhere else.
 *
 * Conventions used (Unified Interface):
 *   text / multiline     [data-id="<field>.fieldControl-text-box-text"]
 *   whole/decimal/money  [data-id="<field>.fieldControl-…-number-text-input"] (or the input in the field's container)
 *   lookup input         [data-id="<field>.fieldControl-LookupResultsDropdown_<field>_textInputBox_with_filter_new"]
 *   lookup results       li[data-id*="LookupResultsDropdown_<field>_resultsContainer"]
 *   choice               [data-id="<field>.fieldControl-option-set-select"] (a <select> or a Fluent combobox)
 *   yes/no toggle        [data-id="<field>.fieldControl-checkbox-toggle"]
 *   header fields        the same data-id prefixed with "header_"
 *
 * Reading values goes through Xrm.Page in page.evaluate: acceptable for assertions, and immune to
 * how a control formats its value. Writing always goes through the UI, as a user would, except
 * where a chapter says why it uses the Web API (setup the film does not need to show).
 */
import { expect, type Locator, type Page } from '@playwright/test';
import { DEMO } from './run';
import { soon } from './soon';

// ---------------------------------------------------------------------------------------------
// Selectors (check these on the first real run)
// ---------------------------------------------------------------------------------------------

const container = (field: string) => `[data-id="${field}-FieldSectionItemContainer"]`;

export const SEL = {
  text: (f: string) => `[data-id="${f}.fieldControl-text-box-text"], [data-id="header_${f}.fieldControl-text-box-text"]`,
  number: (f: string) =>
    [
      `[data-id="${f}.fieldControl-currency-number-text-input"]`,
      `[data-id="${f}.fieldControl-decimal-number-text-input"]`,
      `[data-id="${f}.fieldControl-whole-number-text-input"]`,
      `${container(f)} input`,
    ].join(', '),
  dateTime: (f: string) =>
    `[data-id="${f}.fieldControl-date-time-input"], [data-id="header_${f}.fieldControl-date-time-input"], ${container(f)} input`,
  lookupInput: (f: string) => `[data-id="${f}.fieldControl-LookupResultsDropdown_${f}_textInputBox_with_filter_new"]`,
  lookupResult: (f: string) => `li[data-id*="LookupResultsDropdown_${f}_resultsContainer"]`,
  lookupSelected: (f: string) => `[data-id="${f}.fieldControl-LookupResultsDropdown_${f}_selected_tag"]`,
  choice: (f: string) => `[data-id="${f}.fieldControl-option-set-select"], [data-id="header_${f}.fieldControl-option-set-select"]`,
  toggle: (f: string) => `[data-id="${f}.fieldControl-checkbox-toggle"], ${container(f)} [role="switch"], ${container(f)} input[type="checkbox"]`,
  /** Business process / plugin error dialog shown when a save is refused. */
  errorDialog: '[data-id="errorDialogdialog"], [role="alertdialog"]',
  errorDialogText: '[data-id="errorDialog_subtitle"], [data-id="errorDialogdialog"] [id*="subtitle"], [role="alertdialog"]',
  errorDialogOk: '[data-id="errorOkButton"], [data-id="okButton"]',
  /** Form notification bar (Xrm setFormNotification, and the Escalate result message). */
  formNotification: '[data-id="notificationWrapper"], [data-id="warningNotification"], [data-id="formNotification"]',
  /** "Saving…" indicator in the form header. */
  saving: '[data-id="form-saving-indicator"], [aria-label="Saving..."], [data-id="formSaveStatus"][aria-label*="Saving"]',
  /** Timeline (activities wall) on the case form. */
  timeline: '[data-id="notescontrol"], [data-id^="timeline"], [id^="timeline_"], [aria-label="Timeline"]',
  /** The SLA badge PCF (pcf/SlaBadge) renders data-sla-kind on its Fluent Badge. */
  slaBadge: '[data-sla-kind]',
  /** Model-driven app shell: present once the app has loaded (used by login.mjs too). */
  appShell: '[data-id="topBar"], #id-AppShell, [data-id="navbar-container"]',
  /** Deactivate dialog (Close a case): status reason choice and confirm button. */
  deactivateDialog: '[data-id="SetStateDialog"], [role="dialog"]',
  deactivateReason: '[data-id="SetStateDialog"] select, [role="dialog"] select, [role="dialog"] [role="combobox"]',
  deactivateConfirm: '[data-id="ok_id"], [data-id="SetStateDialog"] button[data-id="ok_id"]',
  /** The Escalate web resource dialog (solution/src/WebResources/cd_/html/escalate.html). */
  escalateFrame: 'iframe[src*="cd_/html/escalate.html"], iframe[src*="cd_%2Fhtml%2Fescalate.html"]',
};

/** Command bar button by its visible label (UCI renders them as menuitem or button). */
export function commandButton(page: Page, label: string | RegExp): Locator {
  const name = typeof label === 'string' ? new RegExp(`^${escapeRegExp(label)}$`, 'i') : label;
  return page
    .getByRole('menuitem', { name })
    .or(page.getByRole('button', { name }))
    .first();
}

// ---------------------------------------------------------------------------------------------
// URLs and navigation
// ---------------------------------------------------------------------------------------------

function env(name: string): string {
  const value = process.env[name];
  if (!value || value.includes('<')) throw new Error(`${name} is not set. Copy e2e/.env.example to e2e/.env and fill it in.`);
  return value.replace(/\/+$/, '');
}

/** main.aspx URL of the CaseDesk app, with extra query (e.g. "pagetype=entityrecord&etn=cd_case"). */
export function appUrl(query = ''): string {
  const base = `${env('CASEDESK_URL')}/main.aspx?appid=${env('CASEDESK_APP_ID')}`;
  return query ? `${base}&${query}` : base;
}

export function recordUrl(entity: string, id: string): string {
  return appUrl(`pagetype=entityrecord&etn=${entity}&id=${cleanId(id)}`);
}

export function cleanId(id: string): string {
  return id.replace(/[{}]/g, '').toLowerCase();
}

/** Record id from a main.aspx URL (…&id=<guid>), lower case without braces, or null. */
export function recordIdFromUrl(url: string): string | null {
  const match = /[?&]id=([^&#]+)/i.exec(url);
  return match ? cleanId(decodeURIComponent(match[1])) : null;
}

/** Waits for a model-driven form: Xrm.Page has data and (for a saved record) an id. */
export async function waitForFormReady(page: Page, options: { saved?: boolean } = { saved: true }): Promise<void> {
  if (options.saved === false) {
    await page.waitForFunction(() => {
      const xrm = (window as any).Xrm;
      return Boolean(xrm?.Page?.data?.entity && xrm.Page.ui?.controls?.getLength?.() > 0);
    });
  } else {
    await page.waitForFunction(() => Boolean((window as any).Xrm?.Page?.data?.entity?.getId?.()));
  }
  // Controls paint a moment after the data is there.
  await page.waitForLoadState('domcontentloaded');
  await page.waitForTimeout(DEMO ? 800 : 300);
}

export async function openNewCase(page: Page): Promise<void> {
  await page.goto(appUrl('pagetype=entityrecord&etn=cd_case'));
  await waitForFormReady(page, { saved: false });
  await expect(page.locator(SEL.text('cd_name')).first()).toBeVisible();
}

export async function openRecord(page: Page, entity: string, id: string): Promise<void> {
  await page.goto(recordUrl(entity, id));
  await waitForFormReady(page);
}

/** Opens the new-record form for a child of the current record, using the relationship's mappings. */
export async function openNewChildForm(page: Page, entity: string, parent: { entityType: string; id: string; name: string }): Promise<void> {
  await page.evaluate(
    ([entityName, p]) => {
      void (window as any).Xrm.Navigation.openForm({
        entityName,
        createFromEntity: { entityType: p.entityType, id: p.id, name: p.name },
      });
    },
    [entity, parent] as const,
  );
  await page.waitForFunction((name) => (window as any).Xrm?.Page?.data?.entity?.getEntityName?.() === name, entity);
  await waitForFormReady(page, { saved: false });
}

// ---------------------------------------------------------------------------------------------
// Field input (through the UI)
// ---------------------------------------------------------------------------------------------

/** Types like a person in film mode, fills instantly otherwise. */
async function typeInto(input: Locator, value: string): Promise<void> {
  await input.click();
  await input.fill('');
  if (DEMO) await input.pressSequentially(value, { delay: 35 });
  else await input.fill(value);
}

export async function setText(page: Page, field: string, value: string): Promise<void> {
  const input = page.locator(SEL.text(field)).first();
  await typeInto(input, value);
  await input.press('Tab');
}

export async function setNumber(page: Page, field: string, value: number): Promise<void> {
  const input = page.locator(SEL.number(field)).first();
  await typeInto(input, String(value));
  await input.press('Tab');
}

/** Types `search` into a lookup and picks the first result whose text contains `pick` (default: search). */
export async function setLookup(page: Page, field: string, search: string, pick: string = search): Promise<void> {
  const input = page.locator(SEL.lookupInput(field)).first();
  await typeInto(input, search);
  const result = page.locator(SEL.lookupResult(field)).filter({ hasText: pick }).first();
  await expect(result, `lookup ${field} shows "${pick}"`).toBeVisible({ timeout: 30_000 });
  await result.click();
  await page.waitForFunction(
    (name) => {
      const v = (window as any).Xrm?.Page?.getAttribute?.(name)?.getValue?.();
      return Array.isArray(v) && v.length > 0;
    },
    field,
  );
}

/** Sets a choice (option set) by its label. Works with the classic <select> and the Fluent combobox. */
export async function setChoice(page: Page, field: string, label: string): Promise<void> {
  const control = page.locator(SEL.choice(field)).first();
  await expect(control).toBeVisible();
  const tag = await control.evaluate((el) => el.tagName.toLowerCase());
  if (tag === 'select') {
    await control.selectOption({ label });
  } else {
    await control.click();
    await page.getByRole('option', { name: label, exact: true }).click();
  }
  await page.waitForFunction(
    ([name, text]) => (window as any).Xrm?.Page?.getAttribute?.(name)?.getText?.() === text,
    [field, label] as const,
  );
}

/** Sets a Yes/No (two options) field by clicking its toggle when it is not already `value`. */
export async function setYesNo(page: Page, field: string, value: boolean): Promise<void> {
  const current = await getValue<boolean>(page, field);
  if (Boolean(current) === value) return;
  await page.locator(SEL.toggle(field)).first().click();
  await page.waitForFunction(
    ([name, wanted]) => Boolean((window as any).Xrm?.Page?.getAttribute?.(name)?.getValue?.()) === wanted,
    [field, value] as const,
  );
}

/** Sets the Status Reason (statuscode) on the form; only reasons of the current state are offered. */
export async function setStatusReason(page: Page, label: string): Promise<void> {
  await setChoice(page, 'statuscode', label);
}

// ---------------------------------------------------------------------------------------------
// Save, errors, notifications
// ---------------------------------------------------------------------------------------------

export type SaveResult = { ok: true } | { ok: false; error: string };

/**
 * Saves with Ctrl+S and waits until the form is saved (not dirty, has an id) or the platform
 * shows an error dialog (a plugin's InvalidPluginExecutionException, e.g. "CD004: …").
 * The error dialog is left open so the film shows it; call dismissError() after.
 */
export async function trySave(page: Page): Promise<SaveResult> {
  await page.keyboard.press('Control+S');
  const saved = page
    .waitForFunction(
      () => {
        const e = (window as any).Xrm?.Page?.data?.entity;
        return Boolean(e?.getId?.()) && e.getIsDirty?.() === false;
      },
      undefined,
      { timeout: 60_000, polling: 250 },
    )
    .then(() => 'saved' as const);
  const refused = page
    .locator(SEL.errorDialog)
    .first()
    .waitFor({ state: 'visible', timeout: 60_000 })
    .then(() => 'error' as const);
  const outcome = await Promise.race([saved, refused]);
  saved.catch(() => undefined);
  refused.catch(() => undefined);
  if (outcome === 'error') return { ok: false, error: await errorDialogText(page) };
  await soon(page.locator(SEL.saving), 500).then(async (shown) => {
    if (shown) await page.locator(SEL.saving).first().waitFor({ state: 'hidden' });
  });
  return { ok: true };
}

/** Saves and fails the test if the platform refuses. */
export async function save(page: Page): Promise<void> {
  const result = await trySave(page);
  if (!result.ok) throw new Error(`Save was refused: ${result.error}`);
}

export async function errorDialogText(page: Page): Promise<string> {
  const text = await page.locator(SEL.errorDialogText).first().innerText().catch(() => '');
  return text.trim();
}

export async function dismissError(page: Page): Promise<void> {
  const ok = page.locator(SEL.errorDialogOk).first();
  if (await soon(ok, 3_000)) await ok.click();
  else await page.keyboard.press('Escape');
  await page.locator(SEL.errorDialog).first().waitFor({ state: 'hidden' });
}

/** Throws away unsaved changes on the form (after a refused save) by reloading its data. */
export async function discardChanges(page: Page): Promise<void> {
  await page.evaluate(() => (window as any).Xrm.Page.data.refresh(false));
  await page.waitForFunction(() => (window as any).Xrm?.Page?.data?.entity?.getIsDirty?.() === false);
}

/** Reloads the form's data from the server (after a server-side change). */
export async function refreshForm(page: Page): Promise<void> {
  await page.evaluate(() => (window as any).Xrm.Page.data.refresh(false));
  await page.waitForTimeout(DEMO ? 1_000 : 300);
}

export async function formNotification(page: Page): Promise<string> {
  const bar = page.locator(SEL.formNotification).first();
  if (!(await soon(bar, 10_000))) return '';
  return (await bar.innerText()).trim();
}

// ---------------------------------------------------------------------------------------------
// Reading values (Xrm in the page; for assertions)
// ---------------------------------------------------------------------------------------------

/**
 * A field's value from Xrm.Page. Dates come back as ISO strings, lookups as
 * [{ id, name, entityType }] with clean ids, choices as their numeric value.
 */
export async function getValue<T = unknown>(page: Page, field: string): Promise<T> {
  return page.evaluate((name) => {
    const attr = (window as any).Xrm?.Page?.getAttribute?.(name);
    if (!attr) throw new Error(`Field ${name} is not on the form.`);
    const v = attr.getValue();
    if (v instanceof Date) return v.toISOString();
    if (Array.isArray(v))
      return v.map((x: any) => ({ id: String(x.id).replace(/[{}]/g, '').toLowerCase(), name: x.name, entityType: x.entityType }));
    return v;
  }, field) as Promise<T>;
}

export async function getText(page: Page, field: string): Promise<string | null> {
  return page.evaluate((name) => (window as any).Xrm?.Page?.getAttribute?.(name)?.getText?.() ?? null, field);
}

export async function currentRecordId(page: Page): Promise<string> {
  const id: string = await page.evaluate(() => (window as any).Xrm.Page.data.entity.getId());
  return cleanId(id);
}

// ---------------------------------------------------------------------------------------------
// Web API from the page (runs as the signed-in persona, so privileges are real)
// ---------------------------------------------------------------------------------------------

export async function webCreate(page: Page, entity: string, data: Record<string, unknown>): Promise<string> {
  const id: string = await page.evaluate(
    async ([e, d]) => (await (window as any).Xrm.WebApi.createRecord(e, d)).id,
    [entity, data] as const,
  );
  return cleanId(id);
}

export async function webUpdate(page: Page, entity: string, id: string, data: Record<string, unknown>): Promise<void> {
  await page.evaluate(
    async ([e, i, d]) => {
      await (window as any).Xrm.WebApi.updateRecord(e, i, d);
    },
    [entity, cleanId(id), data] as const,
  );
}

export async function webRetrieve<T = Record<string, any>>(page: Page, entity: string, id: string, options = ''): Promise<T> {
  return page.evaluate(
    async ([e, i, o]) => (window as any).Xrm.WebApi.retrieveRecord(e, i, o),
    [entity, cleanId(id), options] as const,
  ) as Promise<T>;
}

export async function webQuery<T = Record<string, any>>(page: Page, entity: string, options: string): Promise<T[]> {
  return page.evaluate(
    async ([e, o]) => (await (window as any).Xrm.WebApi.retrieveMultipleRecords(e, o)).entities,
    [entity, options] as const,
  ) as Promise<T[]>;
}

/** True if the signed-in persona can read the record through the Web API. */
export async function canRead(page: Page, entity: string, id: string): Promise<boolean> {
  return page.evaluate(
    async ([e, i]) => {
      try {
        await (window as any).Xrm.WebApi.retrieveRecord(e, i, '?$select=createdon');
        return true;
      } catch {
        return false;
      }
    },
    [entity, cleanId(id)] as const,
  );
}

/** Waits for the app shell, for pages that are not a form (e.g. after an access error). */
export async function waitForAppShell(page: Page): Promise<void> {
  await page.waitForFunction(() => Boolean((window as any).Xrm?.Utility?.getGlobalContext?.()));
}

function escapeRegExp(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

// ---------------------------------------------------------------------------------------------
// CaseDesk values (data-model.md)
// ---------------------------------------------------------------------------------------------

export const CATEGORY = { Billing: 417200000, Delivery: 417200001, Product: 417200002, Refund: 417200003, Other: 417200004 } as const;
export const PRIORITY = { Low: 417200000, Medium: 417200001, High: 417200002, Critical: 417200003 } as const;
export const STATUS = {
  New: 1,
  Assigned: 417200001,
  InProgress: 417200002,
  AwaitingCustomer: 417200003,
  AwaitingApproval: 417200004,
  Resolved: 417200005,
  Closed: 2,
  Cancelled: 417200006,
} as const;
export const DECISION = { Pending: 417200000, Approved: 417200001, Rejected: 417200002 } as const;
export const NOTE_TYPE = { Internal: 417200000, CustomerFacing: 417200001 } as const;
