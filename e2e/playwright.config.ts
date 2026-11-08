import { defineConfig, devices } from '@playwright/test';
import { config as loadEnv } from 'dotenv';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
loadEnv({ path: join(here, '.env'), quiet: true });

/** DEMO=1 is set by `npm run film`: slower actions, captions held for reading, video on. */
const DEMO = process.env.DEMO === '1';

export default defineConfig({
  testDir: 'chapters',
  outputDir: 'test-results',
  // The chapters are one story in a fixed order (01..08) and share the cases they create.
  fullyParallel: false,
  workers: 1,
  retries: 0,
  // A whole walk with every 90 s poll stays well under 10 minutes; one chapter gets 6 minutes
  // (chapter 3 with APPROVE_BY_HAND waits longer and raises its own timeout).
  timeout: 6 * 60_000,
  expect: { timeout: 30_000 },
  reporter: DEMO
    ? [['list'], ['html', { open: 'never' }], ['json', { outputFile: 'test-results/results.json' }]]
    : [['list'], ['html', { open: 'never' }]],
  use: {
    ...devices['Desktop Chrome'],
    browserName: 'chromium',
    // Always headed. These runs are watched by the owner and filmed for clients; a model-driven
    // app also signs in with MFA and conditional access that treat headless browsers differently.
    // Never make this configurable.
    headless: false,
    viewport: { width: 1920, height: 1080 },
    deviceScaleFactor: 1,
    actionTimeout: 60_000,
    navigationTimeout: 90_000,
    launchOptions: { slowMo: DEMO ? 150 : 0, args: ['--window-size=1920,1080', '--window-position=0,0'] },
    video: DEMO ? { mode: 'on', size: { width: 1920, height: 1080 } } : 'retain-on-failure',
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
  },
});
