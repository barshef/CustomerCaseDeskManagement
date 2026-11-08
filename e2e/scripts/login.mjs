// Saves a persona's sign-in for the walkthrough:  npm run login -- agentA | agentB | supervisor
//
// Opens a visible browser on the CaseDesk app. Sign in by hand as that persona's user (MFA is
// fine; tick "Stay signed in"). When the model-driven app has loaded, the script saves the
// browser's storage state to e2e/.auth/<persona>.json (git-ignored) and closes. Re-run it when a
// walk fails because the sign-in has expired.
import { chromium } from '@playwright/test';
import { config as loadEnv } from 'dotenv';
import { mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
loadEnv({ path: join(root, '.env'), quiet: true });

const PERSONAS = {
  agentA: { label: 'Agent A', role: 'CaseDesk Agent', businessUnit: 'CaseDesk BU A' },
  agentB: { label: 'Agent B', role: 'CaseDesk Agent', businessUnit: 'CaseDesk BU B' },
  supervisor: { label: 'Supervisor', role: 'CaseDesk Supervisor', businessUnit: null },
};

const persona = process.argv[2];
if (!PERSONAS[persona]) {
  console.error(`Usage: npm run login -- <persona>   (one of: ${Object.keys(PERSONAS).join(', ')})`);
  process.exit(2);
}

const url = (process.env.CASEDESK_URL ?? '').replace(/\/+$/, '');
const appId = process.env.CASEDESK_APP_ID ?? '';
if (!url || url.includes('<') || !appId || appId.includes('<')) {
  console.error('Set CASEDESK_URL and CASEDESK_APP_ID in e2e/.env first (copy .env.example).');
  process.exit(2);
}

const who = PERSONAS[persona];
console.log(`Sign in as ${who.label} (${who.role}${who.businessUnit ? `, ${who.businessUnit}` : ''}) in the browser window.`);
console.log('Waiting up to 10 minutes for the CaseDesk app to load…');

// Always headed: the owner signs in by hand.
const browser = await chromium.launch({ headless: false, args: ['--window-size=1400,1000'] });
const context = await browser.newContext({ viewport: { width: 1380, height: 900 }, locale: 'en-GB' });
const page = await context.newPage();
await page.goto(`${url}/main.aspx?appid=${appId}`);

try {
  // The app shell is up once Xrm's global context knows the signed-in user.
  await page.waitForFunction(() => Boolean(window.Xrm?.Utility?.getGlobalContext?.()?.userSettings?.userId), undefined, {
    timeout: 10 * 60_000,
    polling: 1_000,
  });
} catch {
  console.error('The app did not load in 10 minutes. Nothing was saved.');
  await browser.close();
  process.exit(1);
}

// Tell the owner who signed in, and warn if it does not look like the persona.
const me = await page.evaluate(async () => {
  const ctx = window.Xrm.Utility.getGlobalContext();
  const id = ctx.userSettings.userId.replace(/[{}]/g, '');
  const roles = (ctx.userSettings.roles?.get?.() ?? []).map((r) => r.name);
  try {
    const user = await window.Xrm.WebApi.retrieveRecord('systemuser', id, '?$select=fullname&$expand=businessunitid($select=name)');
    return { name: user.fullname, businessUnit: user.businessunitid?.name ?? '', roles };
  } catch {
    return { name: ctx.userSettings.userName, businessUnit: '', roles };
  }
});
console.log(`Signed in as ${me.name}${me.businessUnit ? ` in ${me.businessUnit}` : ''}; roles: ${me.roles.join(', ') || '(none)'}.`);
if (who.businessUnit && me.businessUnit && me.businessUnit !== who.businessUnit) {
  console.warn(`Warning: ${who.label} should be in "${who.businessUnit}", not "${me.businessUnit}".`);
}
if (me.roles.length && !me.roles.includes(who.role)) console.warn(`Warning: ${who.label} should have the "${who.role}" role.`);

const authDir = join(root, '.auth');
mkdirSync(authDir, { recursive: true });
const file = join(authDir, `${persona}.json`);
await context.storageState({ path: file });
console.log(`Saved ${file}`);
await browser.close();
