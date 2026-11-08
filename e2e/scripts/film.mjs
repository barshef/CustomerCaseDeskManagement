// Makes the CaseDesk guide film:  npm run film            (all chapters)
//                                  npm run film -- --chapter 06   (one chapter → casedesk-demo-06.mp4)
//
// 1. Runs the walkthrough with DEMO=1 (headed, slower, captions held, every persona recorded).
// 2. Stops if any chapter failed: the failure, its screenshot and recording are in
//    playwright-report/ and test-results/; no film is made or published.
// 3. Stitches the recordings (stitch.mjs), with the Azure voice-over if a key is set (speech.mjs).
// 4. Publishes the film to e2e/demo-out/ (publish.mjs).
//
// Extra arguments after the chapter are passed to Playwright, e.g. `-- --chapter 06 --debug`.
import { spawnSync } from 'node:child_process';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { stitch } from './stitch.mjs';
import { publish } from './publish.mjs';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(import.meta.url);

const args = process.argv.slice(2);
let chapter;
const rest = [];
for (let i = 0; i < args.length; i++) {
  if (args[i] === '--chapter') chapter = String(args[++i] ?? '').padStart(2, '0');
  else rest.push(args[i]);
}
if (chapter !== undefined && !/^\d{2}$/.test(chapter)) {
  console.error('Usage: npm run film -- [--chapter NN]');
  process.exit(2);
}

// Playwright's CLI, run with this Node and no shell (a file filter is a regular expression;
// "06-" matches chapters/06-escalate.spec.ts).
const cli = join(dirname(require.resolve('@playwright/test/package.json')), 'cli.js');
const playwrightArgs = [cli, 'test', ...(chapter ? [`${chapter}-`] : []), ...rest];
console.log(`Filming ${chapter ? `chapter ${chapter}` : 'all chapters'} (headed, DEMO=1)…`);
const run = spawnSync(process.execPath, playwrightArgs, {
  cwd: root,
  stdio: 'inherit',
  env: { ...process.env, DEMO: '1' },
});
if (run.status !== 0) {
  console.error('\nA chapter failed, so no film was made. See playwright-report/ (npx playwright show-report) and test-results/.');
  process.exit(run.status ?? 1);
}

try {
  console.log('\nStitching…');
  await stitch({ chapter });
  const published = publish({ chapter });
  process.exit(published ? 0 : 1);
} catch (error) {
  console.error(error.message ?? error);
  process.exit(1);
}
