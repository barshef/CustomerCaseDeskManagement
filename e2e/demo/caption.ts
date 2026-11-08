/**
 * What the film shows on top of the real screens, drawn by the page itself so the recording has
 * it: a caption band at the TOP of the screen (who is acting and what they do), a full-screen
 * title card, a chapter card before each chapter, and a panel for showing data that is not on a
 * CaseDesk screen (the external system's CaseClosed message).
 *
 * The overlay lives in a CLOSED shadow root on an element with aria-hidden="true" and
 * pointer-events: none, so:
 *   - getByText / getByRole never match caption words (Playwright pierces open shadow roots only),
 *   - clicks pass straight through to the app,
 *   - screen-reader based locators ignore it.
 * It is added with context.addInitScript, so it comes back after every full navigation; the
 * current caption is kept in sessionStorage and repainted on load.
 *
 * Every caption and card is also written to test-results/captions.json with its time, which the
 * film pipeline uses for the voice-over and for cutting idle stretches.
 */
import type { BrowserContext, Page } from '@playwright/test';
import { PERSONAS, type PersonaName } from './cast';
import { DEMO, pageInfo, recordMark } from './run';
import { pause } from './soon';
import { speechConfig, synthesize } from '../scripts/speech.mjs';

/** Height of the caption band in CSS pixels. */
export const BAND_HEIGHT = 88;

/**
 * 'push' moves the model-driven app down by the band's height so the band covers nothing;
 * 'overlay' lays the band over the top of the app (fallback if pushing breaks the app's layout).
 * Choose with DEMO_BAND=overlay.
 */
const BAND_MODE: 'push' | 'overlay' = process.env.DEMO_BAND === 'overlay' ? 'overlay' : 'push';

interface OverlayConfig {
  band: number;
  mode: 'push' | 'overlay';
}

/**
 * Runs inside the page (serialised by Playwright), so it must be self-contained.
 * Exposes window.__caseDeskDemo with say / card / panel.
 */
function overlayMain(cfg: OverlayConfig): void {
  const w = window as unknown as { __caseDeskDemo?: unknown };
  if (w.__caseDeskDemo || window.top !== window) return; // main frame only, once
  const KEY = 'casedesk-demo-caption';
  const font = "'Segoe UI', 'Segoe UI Web (West European)', system-ui, sans-serif";

  let shadow: ShadowRoot | null = null;
  const byId = (id: string) => (shadow ? (shadow.getElementById(id) as HTMLElement | null) : null);

  function build(): void {
    if (shadow || !document.documentElement) return;
    const host = document.createElement('casedesk-demo-overlay');
    host.setAttribute('aria-hidden', 'true');
    host.setAttribute(
      'style',
      'all:initial;position:fixed;inset:0;z-index:2147483646;pointer-events:none;display:block;',
    );
    shadow = host.attachShadow({ mode: 'closed' });
    shadow.innerHTML = `
      <style>
        * { box-sizing: border-box; }
        .band { position: fixed; top: 0; left: 0; right: 0; height: ${cfg.band}px; background: #0f1b2d;
                color: #f4f6fa; font-family: ${font}; display: flex; flex-direction: column; justify-content: center;
                padding: 0 28px; border-bottom: 3px solid #e8a33d; pointer-events: none; }
        .who { font-size: 15px; letter-spacing: .09em; text-transform: uppercase; color: #e8a33d; font-weight: 700;
               margin-bottom: 4px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
        .text { font-size: 27px; line-height: 1.25; font-weight: 500; max-height: 2.5em; overflow: hidden; }
        .card { position: fixed; inset: 0; background: radial-gradient(circle at 30% 30%, #1d3557, #0f1b2d 70%);
                color: #fff; font-family: ${font}; display: none; flex-direction: column; align-items: center;
                justify-content: center; text-align: center; pointer-events: none; padding: 0 10%; }
        .card .kicker { font-size: 26px; letter-spacing: .14em; text-transform: uppercase; color: #e8a33d; font-weight: 700; margin-bottom: 18px; }
        .card .heading { font-size: 64px; font-weight: 700; line-height: 1.15; }
        .card .sub { font-size: 28px; margin-top: 22px; color: #c9d6e8; line-height: 1.35; max-width: 1300px; }
        .panel { position: fixed; top: ${cfg.band + 40}px; right: 40px; width: 760px; max-height: 70vh; overflow: hidden;
                 background: #101826; color: #e6edf6; border: 2px solid #e8a33d; border-radius: 14px; box-shadow: 0 18px 60px rgba(0,0,0,.45);
                 font-family: ${font}; display: none; pointer-events: none; }
        .panel .ph { padding: 14px 20px; font-size: 20px; font-weight: 700; color: #e8a33d; border-bottom: 1px solid #2c3a50; }
        .panel pre { margin: 0; padding: 16px 20px; font: 16px/1.45 Consolas, 'Cascadia Mono', monospace; white-space: pre-wrap; word-break: break-word; }
      </style>
      <div class="band" id="band"><div class="who" id="who"></div><div class="text" id="text"></div></div>
      <div class="card" id="card"><div class="kicker" id="kicker"></div><div class="heading" id="heading"></div><div class="sub" id="sub"></div></div>
      <div class="panel" id="panel"><div class="ph" id="ph"></div><pre id="pbody"></pre></div>`;
    document.documentElement.appendChild(host);

    if (cfg.mode === 'push') {
      const style = document.createElement('style');
      style.setAttribute('data-casedesk-demo', '1');
      style.textContent =
        `html { padding-top: ${cfg.band}px !important; box-sizing: border-box !important; height: 100vh !important; overflow: hidden !important; }` +
        `body { height: calc(100vh - ${cfg.band}px) !important; position: relative !important; }`;
      (document.head || document.documentElement).appendChild(style);
    }

    try {
      const saved = JSON.parse(sessionStorage.getItem(KEY) || 'null') as { who: string; text: string } | null;
      if (saved) api.say(saved.who, saved.text);
    } catch {
      /* sessionStorage blocked: the next say() repaints */
    }
  }

  const api = {
    say(who: string, text: string): void {
      build();
      const whoEl = byId('who');
      const textEl = byId('text');
      if (whoEl) whoEl.textContent = who;
      if (textEl) textEl.textContent = text;
      try {
        sessionStorage.setItem(KEY, JSON.stringify({ who, text }));
      } catch {
        /* ignore */
      }
    },
    card(kicker: string, heading: string, sub: string): void {
      build();
      const card = byId('card');
      if (!card) return;
      if (!heading) {
        card.style.display = 'none';
        return;
      }
      byId('kicker')!.textContent = kicker;
      byId('heading')!.textContent = heading;
      byId('sub')!.textContent = sub;
      card.style.display = 'flex';
    },
    panel(heading: string, body: string): void {
      build();
      const panel = byId('panel');
      if (!panel) return;
      if (!heading) {
        panel.style.display = 'none';
        return;
      }
      byId('ph')!.textContent = heading;
      byId('pbody')!.textContent = body;
      panel.style.display = 'block';
    },
    ready(): boolean {
      return Boolean(shadow);
    },
  };
  Object.defineProperty(window, '__caseDeskDemo', { value: api, enumerable: false });

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', build, { once: true });
  else build();
}

type OverlayApi = {
  say(who: string, text: string): void;
  card(kicker: string, heading: string, sub: string): void;
  panel(heading: string, body: string): void;
  ready(): boolean;
};

/** Adds the overlay to every page of a context, including after full navigations. */
export async function installOverlay(context: BrowserContext): Promise<void> {
  await context.addInitScript(overlayMain, { band: BAND_HEIGHT, mode: BAND_MODE });
}

const CONFIG: OverlayConfig = { band: BAND_HEIGHT, mode: BAND_MODE };

/**
 * Runs `action` against the overlay in the page, adding the overlay first if this document does
 * not have it yet. Both go through page.evaluate (not eval inside the page), so a Content
 * Security Policy on the org does not block them.
 */
async function callOverlay(page: Page, action: (args: string[]) => void, args: string[]): Promise<void> {
  try {
    await page.evaluate(overlayMain, CONFIG);
    await page.evaluate(action, args);
  } catch {
    /* page navigating away: the init script repaints the caption on load */
  }
}

// Page functions (serialised into the page, so they may not use anything from this module).
const sayIn = (a: string[]) => (window as unknown as { __caseDeskDemo: OverlayApi }).__caseDeskDemo.say(a[0], a[1]);
const showCard = (a: string[]) => (window as unknown as { __caseDeskDemo: OverlayApi }).__caseDeskDemo.card(a[0], a[1], a[2]);
const showPanelIn = (a: string[]) => (window as unknown as { __caseDeskDemo: OverlayApi }).__caseDeskDemo.panel(a[0], a[1]);

function who(page: Page): string {
  const persona = PERSONAS[pageInfo(page).persona as PersonaName];
  return persona ? `${persona.label} · ${persona.role} · ${persona.businessUnit}` : '';
}

/** Reading time for a caption in film mode: about 2.5 words a second plus a beat. */
export function holdFor(text: string): number {
  const words = text.trim().split(/\s+/).length;
  return Math.min(7_000, Math.max(1_800, (words / 2.5) * 1000 + 700));
}

const voice = DEMO ? speechConfig() : null;
let voiceFailed = false;

/**
 * How long to hold a caption in film mode. With a voice configured, the clip is made now
 * (cached in .cache/speech, so the stitcher reuses it) and the caption is held until it has
 * been spoken, so picture and voice stay together. Without a voice: reading time.
 */
async function holdMs(text: string, minimum: number): Promise<number> {
  if (!DEMO) return 0;
  if (voice && !voiceFailed) {
    try {
      const clip = await synthesize(text, voice);
      return Math.max(minimum, Math.round(clip.seconds * 1000) + 500);
    } catch (error) {
      voiceFailed = true;
      console.warn(`Voice-over off for this run: ${(error as Error).message}`);
    }
  }
  return minimum;
}

/**
 * Shows a caption naming the persona and what they do, and records it for the voice-over.
 * In film mode it holds until read (or spoken); in a plain walk it does not wait.
 */
export async function say(page: Page, text: string): Promise<void> {
  const hold = await holdMs(text, holdFor(text));
  recordMark(page, 'say', text, hold);
  await callOverlay(page, sayIn, [who(page), text]);
  if (hold) await pause(hold);
}

/** Full-screen title card (once, at the start of a run). Only the heading is spoken. */
export async function titleCard(page: Page, heading: string, sub: string): Promise<void> {
  const hold = DEMO ? await holdMs(heading, 6_000) : 300;
  recordMark(page, 'title', heading, hold);
  await callOverlay(page, showCard, ['CaseDesk guide film', heading, sub]);
  await pause(hold);
  await callOverlay(page, showCard, ['', '', '']);
}

/** Chapter card shown before each chapter: "Chapter 6", the chapter's name, who does it. */
export async function chapterCard(page: Page, number: number, heading: string, sub: string): Promise<void> {
  const spoken = `Chapter ${number}. ${heading}.`;
  const hold = DEMO ? await holdMs(spoken, 3_500) : 300;
  recordMark(page, 'chapter', spoken, hold);
  await callOverlay(page, showCard, [`Chapter ${number}`, heading, sub]);
  await pause(hold);
  await callOverlay(page, showCard, ['', '', '']);
}

/** A panel over the app showing data from outside CaseDesk (e.g. the stub's JSON). */
export async function showPanel(page: Page, heading: string, body: string, holdMsInFilm = 7_000): Promise<void> {
  const hold = DEMO ? holdMsInFilm : 500;
  recordMark(page, 'show', heading, hold);
  await callOverlay(page, showPanelIn, [heading, body]);
  await pause(hold);
  await callOverlay(page, showPanelIn, ['', '']);
}

/**
 * Marks a stretch of waiting (an email arriving, a message reaching the stub) that the film
 * shortens: the stitcher keeps a second either side and cuts the middle.
 */
export async function fastForward<T>(page: Page, work: () => Promise<T>): Promise<T> {
  recordMark(page, 'cut-start', '');
  try {
    return await work();
  } finally {
    recordMark(page, 'cut-end', '');
  }
}
