// Copies a stitched film to e2e/demo-out/ — only when every chapter of the run passed.
//
//   node scripts/publish.mjs [--chapter 06]      (film.mjs calls publish() itself)
//
// Reads Playwright's JSON report (test-results/results.json, written in film mode). A full film
// needs all chapters in chapters/ to have run and passed; a one-chapter film needs that chapter.
import { copyFileSync, existsSync, mkdirSync, readFileSync, readdirSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const RESULTS = join(root, 'test-results', 'results.json');
const OUT_DIR = join(root, 'test-results', 'film', 'out');
export const DEMO_OUT = join(root, 'demo-out');

function collectTests(suite, file, into) {
  const here = suite.file ?? file;
  for (const spec of suite.specs ?? []) {
    for (const t of spec.tests ?? []) {
      const last = t.results?.[t.results.length - 1];
      into.push({ file: basename(spec.file ?? here ?? ''), title: spec.title, status: last?.status ?? 'not run' });
    }
  }
  for (const child of suite.suites ?? []) collectTests(child, here, into);
}

/** Every expected chapter ran and passed? Returns { ok, problems }. */
export function checkRun(chapter) {
  if (!existsSync(RESULTS)) return { ok: false, problems: [`No ${RESULTS}: the run did not finish in film mode.`] };
  const report = JSON.parse(readFileSync(RESULTS, 'utf8'));
  const tests = [];
  for (const suite of report.suites ?? []) collectTests(suite, undefined, tests);
  const expected = readdirSync(join(root, 'chapters'))
    .filter((f) => f.endsWith('.spec.ts'))
    .filter((f) => !chapter || f.startsWith(`${chapter}-`));
  const problems = [];
  for (const file of expected) {
    const t = tests.find((x) => x.file === file);
    if (!t) problems.push(`${file}: did not run`);
    else if (t.status !== 'passed') problems.push(`${file}: ${t.status}`);
  }
  return { ok: expected.length > 0 && problems.length === 0, problems };
}

/** Copies the film, its chapters file and captions into demo-out/. Returns the published paths. */
export function publish({ chapter } = {}) {
  const check = checkRun(chapter);
  if (!check.ok) {
    console.error('Not published — not every chapter passed:');
    for (const p of check.problems) console.error(`  ${p}`);
    return null;
  }
  const name = chapter ? `casedesk-demo-${chapter}` : 'casedesk-demo';
  const files = [`${name}.mp4`, `${name}.chapters.txt`, `${name}.vtt`];
  mkdirSync(DEMO_OUT, { recursive: true });
  const published = [];
  for (const f of files) {
    const from = join(OUT_DIR, f);
    if (!existsSync(from)) {
      if (f.endsWith('.mp4')) throw new Error(`No stitched film at ${from}. Run node scripts/stitch.mjs first.`);
      continue;
    }
    const to = join(DEMO_OUT, f);
    copyFileSync(from, to);
    published.push(to);
  }
  console.log(`Published to ${DEMO_OUT}:`);
  for (const p of published) console.log(`  ${basename(p)}`);
  return published;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  const i = process.argv.indexOf('--chapter');
  const result = publish({ chapter: i >= 0 ? String(process.argv[i + 1]).padStart(2, '0') : undefined });
  process.exit(result ? 0 : 1);
}
