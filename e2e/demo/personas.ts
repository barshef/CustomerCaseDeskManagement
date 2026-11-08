/**
 * Playwright fixtures for the walkthrough.
 *
 *   test('…', async ({ asPersona, chapter }) => {
 *     const agent = await asPersona('agentA');      // new context, Agent A's saved sign-in
 *     await chapter(agent, 'Escalate a case', 'Agent A');
 *   });
 *
 * Each asPersona() call opens a new browser context with that persona's storage state
 * (.auth/<persona>.json, made by `npm run login -- <persona>`) at 1920×1080, with the caption
 * overlay installed. Every persona context records its own video: in film mode the clips go to
 * test-results/film/ and are listed in clips.json for the stitcher; in a plain walk they are kept
 * only when the chapter fails (with a screenshot of each persona's page), and deleted otherwise.
 */
import { test as base, expect, type Browser, type BrowserContext, type Page, type TestInfo } from '@playwright/test';
import { existsSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { PERSONAS, type PersonaName } from './cast';
import { chapterCard, installOverlay, titleCard } from './caption';
import {
  AUTH_DIR,
  DEMO,
  FILM_DIR,
  RESULTS_DIR,
  chapterIdFromFile,
  readState,
  recordClip,
  registerPage,
  writeState,
} from './run';

export { expect };
export type { PersonaName };

interface Opened {
  persona: PersonaName;
  context: BrowserContext;
  page: Page;
  clip: string;
  startedAt: number;
}

export interface WalkFixtures {
  /** A page signed in as the persona, in its own context (and, when filming, its own clip). */
  asPersona: (name: PersonaName) => Promise<Page>;
  /**
   * Shows the title card (first chapter of the run only) and this chapter's card on `page`.
   * The chapter number comes from the file name (06-escalate.spec.ts → 6).
   */
  chapter: (page: Page, heading: string, sub: string) => Promise<void>;
}

function storageStateFor(name: PersonaName): string {
  const file = join(AUTH_DIR, `${name}.json`);
  if (!existsSync(file)) {
    throw new Error(
      `No saved sign-in for ${PERSONAS[name].label}. Run \`npm run login -- ${name}\` in e2e/ and sign in as the ` +
        `${PERSONAS[name].role} user in ${PERSONAS[name].businessUnit}.`,
    );
  }
  return file;
}

async function openPersona(browser: Browser, name: PersonaName, testInfo: TestInfo, seq: number): Promise<Opened> {
  const chapter = chapterIdFromFile(testInfo.file);
  const clip = `${chapter}-${String(seq).padStart(2, '0')}-${name}`;
  const videoDir = DEMO ? join(FILM_DIR, 'raw') : join(RESULTS_DIR, 'raw-video');
  mkdirSync(videoDir, { recursive: true });
  const context = await browser.newContext({
    storageState: storageStateFor(name),
    viewport: { width: 1920, height: 1080 },
    deviceScaleFactor: 1,
    locale: 'en-GB',
    recordVideo: { dir: videoDir, size: { width: 1920, height: 1080 } },
  });
  context.setDefaultTimeout(60_000);
  context.setDefaultNavigationTimeout(90_000);
  await installOverlay(context);
  const page = await context.newPage();
  const startedAt = Date.now();
  // "Leave this page?" prompts would otherwise stop a navigation away from an unsaved form.
  page.on('dialog', (dialog) => void dialog.accept().catch(() => undefined));
  registerPage(page, { clip, chapter, persona: name });
  return { persona: name, context, page, clip, startedAt };
}

export const test = base.extend<WalkFixtures & { opened: Opened[] }>({
  opened: async ({}, use) => {
    await use([]);
  },

  asPersona: async ({ browser, opened }, use, testInfo) => {
    await use(async (name: PersonaName) => {
      const o = await openPersona(browser, name, testInfo, opened.length + 1);
      opened.push(o);
      return o.page;
    });

    // Teardown: screenshots on failure, then close each context and keep or drop its video.
    const failed = testInfo.status !== testInfo.expectedStatus;
    const chapter = chapterIdFromFile(testInfo.file);
    for (const o of opened) {
      if (failed && !o.page.isClosed()) {
        const shot = await o.page.screenshot({ fullPage: false }).catch(() => undefined);
        if (shot) await testInfo.attach(`${o.persona}-at-failure.png`, { body: shot, contentType: 'image/png' });
      }
      const endedAt = Date.now();
      const video = o.page.video();
      await o.context.close();
      if (!video) continue;
      if (DEMO) {
        const file = join(FILM_DIR, `${o.clip}.webm`);
        await video.saveAs(file); // waits for the recording to finish writing
        await video.delete();
        recordClip({
          clip: o.clip,
          chapter,
          chapterTitle: testInfo.title,
          persona: o.persona,
          file,
          startedAt: o.startedAt,
          endedAt,
        });
        if (failed) await testInfo.attach(`${o.persona}.webm`, { path: file, contentType: 'video/webm' });
      } else if (failed) {
        const file = testInfo.outputPath(`${o.clip}.webm`);
        await video.saveAs(file);
        await video.delete();
        await testInfo.attach(`${o.persona}.webm`, { path: file, contentType: 'video/webm' });
      } else {
        await video.delete();
      }
    }
  },

  chapter: async ({}, use, testInfo) => {
    const id = chapterIdFromFile(testInfo.file);
    const number = Number.parseInt(id, 10);
    await use(async (page: Page, heading: string, sub: string) => {
      if (!readState().titleShown) {
        await titleCard(
          page,
          'CaseDesk — customer case management on Dynamics 365',
          'Two agents in different business units and a supervisor work cases from logging to closing.',
        );
        writeState({ titleShown: true });
      }
      await chapterCard(page, number, heading, sub);
    });
  },
});
