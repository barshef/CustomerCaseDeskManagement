/**
 * Run-wide bits shared by the demo helpers: the DEMO flag, where files go, the marks the film
 * pipeline reads (captions, cards, cuts) and the small state file chapters use to hand record
 * ids to each other. Playwright empties test-results/ at the start of every run, so each run
 * starts with no state and creates its own [Walkthrough] cases.
 */
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { Page } from '@playwright/test';

export const DEMO = process.env.DEMO === '1';

export const E2E_ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
export const RESULTS_DIR = join(E2E_ROOT, 'test-results');
export const FILM_DIR = join(RESULTS_DIR, 'film');
export const AUTH_DIR = join(E2E_ROOT, '.auth');
/** Every caption, card and cut with its time; the film pipeline turns these into cuts and the voice-over. */
export const MARKS_FILE = join(RESULTS_DIR, 'captions.json');
export const CLIPS_FILE = join(FILM_DIR, 'clips.json');
export const STATE_FILE = join(RESULTS_DIR, 'walkthrough-state.json');

/** Title prefix of every case the walkthrough creates. */
export const TITLE_PREFIX = '[Walkthrough]';

export type MarkKind = 'title' | 'chapter' | 'say' | 'show' | 'cut-start' | 'cut-end';

export interface Mark {
  kind: MarkKind;
  chapter: string;
  clip: string;
  persona: string;
  text: string;
  /** Epoch milliseconds, Node clock (the same clock as the clip's startedAt). */
  t: number;
  /** How long the caption/card was held on screen, ms (film mode). */
  hold?: number;
}

export interface ClipInfo {
  clip: string;
  chapter: string;
  chapterTitle: string;
  persona: string;
  file: string;
  startedAt: number;
  endedAt: number;
}

/** Which clip (persona context) a page belongs to; set by the persona fixture. */
const pageClips = new WeakMap<Page, { clip: string; chapter: string; persona: string }>();

export function registerPage(page: Page, info: { clip: string; chapter: string; persona: string }): void {
  pageClips.set(page, info);
}

export function pageInfo(page: Page): { clip: string; chapter: string; persona: string } {
  return pageClips.get(page) ?? { clip: 'unknown', chapter: 'unknown', persona: 'unknown' };
}

export function recordMark(page: Page, kind: MarkKind, text: string, hold?: number): void {
  const info = pageInfo(page);
  const mark: Mark = { kind, chapter: info.chapter, clip: info.clip, persona: info.persona, text, t: Date.now(), hold };
  mkdirSync(RESULTS_DIR, { recursive: true });
  // Rewritten on every mark, so a failed run still leaves every caption written so far.
  const all: Mark[] = existsSync(MARKS_FILE) ? JSON.parse(readFileSync(MARKS_FILE, 'utf8')) : [];
  all.push(mark);
  writeFileSync(MARKS_FILE, JSON.stringify(all, null, 2), 'utf8');
}

export function recordClip(info: ClipInfo): void {
  mkdirSync(FILM_DIR, { recursive: true });
  const all: ClipInfo[] = existsSync(CLIPS_FILE) ? JSON.parse(readFileSync(CLIPS_FILE, 'utf8')) : [];
  all.push(info);
  writeFileSync(CLIPS_FILE, JSON.stringify(all, null, 2), 'utf8');
}

/** Record ids handed from one chapter to the next (workers = 1, so no locking is needed). */
export interface WalkState {
  /** The High Billing case from chapter 1 (Agent A's), reused by chapters 2, 4, 7 and 8. */
  billingCase?: { id: string; caseNumber: string; title: string };
  /** Set by chapter 2 once the billing case is Resolved. */
  billingCaseResolved?: boolean;
  /** The Refund case from chapter 3. */
  refundCase?: { id: string; caseNumber: string; title: string };
  /** True once the title card has been shown in this run. */
  titleShown?: boolean;
}

export function readState(): WalkState {
  if (!existsSync(STATE_FILE)) return {};
  return JSON.parse(readFileSync(STATE_FILE, 'utf8')) as WalkState;
}

export function writeState(patch: Partial<WalkState>): WalkState {
  const next = { ...readState(), ...patch };
  mkdirSync(RESULTS_DIR, { recursive: true });
  writeFileSync(STATE_FILE, JSON.stringify(next, null, 2), 'utf8');
  return next;
}

/** Chapter id from a spec file name, e.g. ".../06-escalate.spec.ts" → "06-escalate". */
export function chapterIdFromFile(file: string): string {
  const base = file.replace(/\\/g, '/').split('/').pop() ?? file;
  return base.replace(/\.spec\.ts$/, '');
}

/** A unique, readable suffix for walkthrough titles, e.g. "08 Nov 14:32". */
export function stamp(): string {
  const d = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  const month = d.toLocaleString('en-GB', { month: 'short' });
  return `${pad(d.getDate())} ${month} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
}
