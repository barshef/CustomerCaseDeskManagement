/**
 * Waiting helpers. Playwright's isVisible() answers at once and never waits, which is wrong for
 * a model-driven app that paints controls late; soon() waits a while and answers true/false
 * without failing the test, so a chapter can choose what to do. poll() repeats a check until it
 * gives a value (an email row, a stub message, a status change) or the time runs out.
 */
import type { Locator } from '@playwright/test';

/** True if the locator becomes visible within `timeout` ms, false otherwise. Never throws. */
export async function soon(locator: Locator, timeout = 15_000): Promise<boolean> {
  try {
    await locator.first().waitFor({ state: 'visible', timeout });
    return true;
  } catch {
    return false;
  }
}

/** True if the locator is hidden (or gone) within `timeout` ms. Never throws. */
export async function soonGone(locator: Locator, timeout = 15_000): Promise<boolean> {
  try {
    await locator.first().waitFor({ state: 'hidden', timeout });
    return true;
  } catch {
    return false;
  }
}

export interface PollOptions {
  /** Total time to keep trying, ms. */
  timeout: number;
  /** Pause between tries, ms. */
  interval?: number;
  /** What was being waited for, for the error message. */
  what: string;
}

/**
 * Calls `check` until it returns something other than undefined/null/false, and returns that.
 * Errors thrown by `check` count as "not yet" (a 404 from the stub, a transient network blip);
 * the last one is reported if time runs out.
 */
export async function poll<T>(check: () => Promise<T | undefined | null | false>, options: PollOptions): Promise<T> {
  const interval = options.interval ?? 3_000;
  const deadline = Date.now() + options.timeout;
  let lastError: unknown;
  for (;;) {
    try {
      const value = await check();
      if (value !== undefined && value !== null && value !== false) return value as T;
    } catch (error) {
      lastError = error;
    }
    if (Date.now() + interval > deadline) break;
    await new Promise((resolve) => setTimeout(resolve, interval));
  }
  const seconds = Math.round(options.timeout / 1000);
  const detail = lastError instanceof Error ? ` Last error: ${lastError.message}` : '';
  throw new Error(`Gave up after ${seconds} s waiting for ${options.what}.${detail}`);
}

export function pause(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
