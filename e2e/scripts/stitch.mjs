// Turns the walkthrough's recordings into one film.
//
// Input (written by the walkthrough in film mode, DEMO=1):
//   test-results/film/clips.json   one entry per persona context: its .webm, start and end time
//   test-results/captions.json     every caption, card and cut with its time and clip
//
// What it does, per chapter in file order (01..08):
//   1. Splits the chapter into shots: a shot is a stretch where one persona is on screen, from
//      that persona's first caption/card until another persona takes over (or a little after
//      its last caption). The idle lead-in of each clip (sign-in redirect, app loading) is
//      dropped because a shot starts at its first mark. The title card and chapter cards are
//      drawn in the page, so they are already in the recording.
//   2. Cuts the middle out of waits marked by fastForward() (keeps 1.2 s either side).
//   3. Re-encodes every shot to 1920x1080, 25 fps, H.264 yuv420p and concatenates them.
//   4. Writes FFMETADATA chapters (one per walkthrough chapter) and a WebVTT caption file.
//   5. Adds the Azure voice-over (speech.mjs) when a key is configured; otherwise silent.
//
// Output: test-results/film/out/casedesk-demo.mp4 (+ .chapters.txt, .vtt); publish.mjs copies
// them to demo-out/. Run on its own:  node scripts/stitch.mjs [--chapter 06]
//                      ffmpeg check:  node scripts/stitch.mjs --self-test
import { copyFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { FFMPEG, concatLine, ffmpeg, probeSeconds, workFolder } from './ffmpeg.mjs';
import { buildVoiceTrack, layTrack } from './speech.mjs';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
export const FILM_DIR = join(root, 'test-results', 'film');
export const OUT_DIR = join(FILM_DIR, 'out');
const CLIPS_FILE = join(FILM_DIR, 'clips.json');
const MARKS_FILE = join(root, 'test-results', 'captions.json');

const W = 1920;
const H = 1080;
const FPS = 25;
const LEAD_MS = 400; // start a shot just before its first caption appears
const KEEP_MS = 1_200; // what is kept either side of a fast-forwarded wait
const AFTER_LAST_MS = 1_500; // after a persona's last caption (its reading time already elapsed)
const MIN_SHOT_MS = 600;

/** Same formula as demo/caption.ts holdFor(): reading time of a caption in film mode. */
function holdFor(text) {
  const words = text.trim().split(/\s+/).length;
  return Math.min(7_000, Math.max(1_800, (words / 2.5) * 1000 + 700));
}
const holdOf = (mark) => mark.hold ?? (mark.kind === 'title' ? 6_000 : mark.kind === 'chapter' ? 3_500 : mark.kind === 'show' ? 7_000 : mark.kind === 'say' ? holdFor(mark.text) : 0);

/**
 * Pure planning: chapters → shots [{ clip, from, to }] (epoch ms) from clips and marks.
 * Exported for the self-test and for reasoning about cuts without ffmpeg.
 */
export function planShots(clips, marks) {
  const byClip = new Map(clips.map((c) => [c.clip, c]));
  const chapterIds = [...new Set(clips.map((c) => c.chapter))].sort();
  const plan = [];
  for (const chapter of chapterIds) {
    const ms = marks.filter((m) => m.chapter === chapter && byClip.has(m.clip)).sort((a, b) => a.t - b.t);
    if (!ms.length) continue;
    // Group consecutive marks of the same clip into runs.
    const runs = [];
    for (const m of ms) {
      const last = runs[runs.length - 1];
      if (last && last.clip === m.clip) last.marks.push(m);
      else runs.push({ clip: m.clip, marks: [m] });
    }
    const shots = [];
    runs.forEach((run, i) => {
      const clip = byClip.get(run.clip);
      const first = run.marks[0];
      const lastSpoken = [...run.marks].reverse().find((m) => m.kind !== 'cut-start' && m.kind !== 'cut-end') ?? first;
      const next = runs[i + 1];
      let from = Math.max(clip.startedAt, first.t - LEAD_MS);
      let to = Math.min(clip.endedAt, lastSpoken.t + holdOf(lastSpoken) + AFTER_LAST_MS);
      if (next) to = Math.min(to, next.marks[0].t - LEAD_MS);
      // Actions after the last caption but before the next persona (e.g. clicks) stay in.
      const lastAny = run.marks[run.marks.length - 1];
      if (lastAny.t > to) to = Math.min(clip.endedAt, lastAny.t + AFTER_LAST_MS);
      // Fast-forwarded waits inside this run.
      const cuts = [];
      let open = null;
      for (const m of run.marks) {
        if (m.kind === 'cut-start') open = m.t;
        if (m.kind === 'cut-end' && open !== null) {
          cuts.push([open + KEEP_MS, m.t - KEEP_MS]);
          open = null;
        }
      }
      let cursor = from;
      for (const [a, b] of cuts) {
        if (b - a < 1_000 || a <= cursor || b >= to) continue;
        shots.push({ chapter, clip: run.clip, from: cursor, to: a });
        cursor = b;
      }
      shots.push({ chapter, clip: run.clip, from: cursor, to });
    });
    plan.push({
      chapter,
      title: clips.find((c) => c.chapter === chapter)?.chapterTitle ?? chapter,
      shots: shots.filter((s) => s.to - s.from >= MIN_SHOT_MS),
    });
  }
  return plan;
}

/** Film time (s) of a mark, given the shots laid end to end; undefined if the mark was cut. */
function filmTimeOf(mark, laid) {
  for (const s of laid) if (s.clip === mark.clip && mark.t >= s.from && mark.t < s.to) return s.start + (mark.t - s.from) / 1000;
  return undefined;
}

async function encodeShot(input, startSec, durSec, out) {
  await ffmpeg([
    '-ss', startSec.toFixed(3),
    '-i', input,
    '-t', durSec.toFixed(3),
    '-an',
    '-vf', `scale=${W}:${H}:force_original_aspect_ratio=decrease,pad=${W}:${H}:(ow-iw)/2:(oh-ih)/2:color=0x0f1b2d,fps=${FPS},format=yuv420p`,
    '-c:v', 'libx264', '-preset', 'veryfast', '-crf', '20',
    out,
  ]);
}

async function concatShots(files, listFile, out) {
  writeFileSync(listFile, files.map(concatLine).join('\n') + '\n', 'utf8');
  await ffmpeg(['-f', 'concat', '-safe', '0', '-i', listFile, '-c', 'copy', out]);
}

function ffmetadata(chapters, totalSeconds, title) {
  const esc = (s) => s.replace(/([=;#\\\n])/g, '\\$1');
  const lines = [';FFMETADATA1', `title=${esc(title)}`];
  chapters.forEach((c, i) => {
    const end = i + 1 < chapters.length ? chapters[i + 1].start : totalSeconds;
    lines.push('', '[CHAPTER]', 'TIMEBASE=1/1000', `START=${Math.round(c.start * 1000)}`, `END=${Math.round(end * 1000)}`, `title=${esc(c.title)}`);
  });
  return lines.join('\n') + '\n';
}

function vtt(cues, totalSeconds) {
  const ts = (s) => {
    const ms = Math.max(0, Math.round(s * 1000));
    const h = String(Math.floor(ms / 3_600_000)).padStart(2, '0');
    const m = String(Math.floor((ms % 3_600_000) / 60_000)).padStart(2, '0');
    const sec = String(Math.floor((ms % 60_000) / 1000)).padStart(2, '0');
    return `${h}:${m}:${sec}.${String(ms % 1000).padStart(3, '0')}`;
  };
  const out = ['WEBVTT', ''];
  cues.forEach((c, i) => {
    const end = Math.min(i + 1 < cues.length ? cues[i + 1].at : totalSeconds, c.at + 8);
    out.push(`${ts(c.at)} --> ${ts(Math.max(end, c.at + 1))}`, c.text, '');
  });
  return out.join('\n');
}

async function mux(video, audio, metadata, out) {
  const args = ['-i', video];
  if (audio) args.push('-i', audio);
  args.push('-i', metadata, '-map', '0:v');
  if (audio) args.push('-map', '1:a', '-c:a', 'aac', '-b:a', '128k');
  args.push('-map_metadata', audio ? '2' : '1', '-map_chapters', audio ? '2' : '1', '-c:v', 'copy', '-movflags', '+faststart', out);
  await ffmpeg(args);
}

/**
 * Makes the film. `chapter` ("06") limits it to one chapter. Returns the output paths.
 */
export async function stitch({ chapter } = {}) {
  if (!existsSync(CLIPS_FILE)) throw new Error(`No ${CLIPS_FILE}: run the walkthrough in film mode first (npm run film).`);
  let clips = JSON.parse(readFileSync(CLIPS_FILE, 'utf8'));
  const marks = existsSync(MARKS_FILE) ? JSON.parse(readFileSync(MARKS_FILE, 'utf8')) : [];
  if (chapter) clips = clips.filter((c) => c.chapter.startsWith(`${chapter}-`));
  if (!clips.length) throw new Error('No recorded clips to stitch.');

  const plan = planShots(clips, marks);
  const work = workFolder();
  try {
    // Copy the inputs into the short work folder (long Windows paths trip ffmpeg).
    const local = new Map();
    for (const c of clips) {
      const to = join(work.dir, `${c.clip}.webm`);
      copyFileSync(c.file, to);
      local.set(c.clip, to);
    }
    const byClip = new Map(clips.map((c) => [c.clip, c]));

    const files = [];
    const laid = [];
    const chapters = [];
    let at = 0;
    let n = 0;
    for (const ch of plan) {
      chapters.push({ start: at, title: ch.title.replace(/^\d+\s*·\s*/, '') });
      for (const s of ch.shots) {
        const clip = byClip.get(s.clip);
        const out = join(work.dir, `shot-${String(++n).padStart(3, '0')}.mp4`);
        await encodeShot(local.get(s.clip), (s.from - clip.startedAt) / 1000, (s.to - s.from) / 1000, out);
        const seconds = await probeSeconds(out);
        laid.push({ ...s, start: at });
        at += seconds;
        files.push(out);
      }
      console.log(`  ${ch.chapter}: ${ch.shots.length} shot(s)`);
    }
    if (!files.length) throw new Error('Every shot was empty: were captions recorded (test-results/captions.json)?');

    const silent = join(work.dir, 'silent.mp4');
    await concatShots(files, join(work.dir, 'shots.txt'), silent);
    const total = await probeSeconds(silent);

    const cues = marks
      .filter((m) => ['say', 'chapter', 'title'].includes(m.kind) && m.text)
      .map((m) => ({ at: filmTimeOf(m, laid), text: m.text }))
      .filter((c) => c.at !== undefined)
      .sort((a, b) => a.at - b.at);

    const name = chapter ? `casedesk-demo-${chapter}` : 'casedesk-demo';
    const metaFile = join(work.dir, 'chapters.txt');
    writeFileSync(metaFile, ffmetadata(chapters, total, 'CaseDesk — guide film'), 'utf8');
    const voice = await buildVoiceTrack(cues, total, join(work.dir, 'voice.wav'));
    const film = join(work.dir, `${name}.mp4`);
    await mux(silent, voice, metaFile, film);

    mkdirSync(OUT_DIR, { recursive: true });
    const result = {
      film: join(OUT_DIR, `${name}.mp4`),
      chapters: join(OUT_DIR, `${name}.chapters.txt`),
      captions: join(OUT_DIR, `${name}.vtt`),
      seconds: total,
      voiced: Boolean(voice),
      chapterCount: chapters.length,
    };
    copyFileSync(film, result.film);
    copyFileSync(metaFile, result.chapters);
    writeFileSync(result.captions, vtt(cues.filter((c) => !/^Chapter \d+\./.test(c.text)), total), 'utf8');
    console.log(`Film: ${result.film} (${Math.round(total)} s, ${chapters.length} chapter(s), ${voice ? 'voiced' : 'silent'})`);
    return result;
  } finally {
    work.remove();
  }
}

/** Proves the ffmpeg path on this machine: two 1 s colour clips (webm, like Playwright's) → one mp4 with chapters and a tone track. */
export async function selfTest() {
  console.log(`ffmpeg: ${FFMPEG}`);
  const work = workFolder('cdfilm-selftest-');
  try {
    const shots = [];
    for (const [i, colour] of [[1, '0x1d3557'], [2, '0xe8a33d']]) {
      const webm = join(work.dir, `clip${i}.webm`);
      await ffmpeg(['-f', 'lavfi', '-i', `color=c=${colour}:s=1280x720:d=1:r=25`, '-c:v', 'libvpx', '-b:v', '1M', webm]);
      const shot = join(work.dir, `shot${i}.mp4`);
      await encodeShot(webm, 0, 1, shot);
      shots.push(shot);
    }
    const silent = join(work.dir, 'silent.mp4');
    await concatShots(shots, join(work.dir, 'shots.txt'), silent);
    const total = await probeSeconds(silent);

    const tone = join(work.dir, 'tone.wav');
    await ffmpeg(['-f', 'lavfi', '-i', 'sine=frequency=440:duration=0.4', '-ac', '1', '-ar', '24000', tone]);
    const track = await layTrack(
      [
        { at: 0.2, file: tone, seconds: 0.4 },
        { at: 1.2, file: tone, seconds: 0.4 },
      ],
      total,
      join(work.dir, 'voice.wav'),
    );
    const meta = join(work.dir, 'chapters.txt');
    writeFileSync(meta, ffmetadata([{ start: 0, title: 'Blue' }, { start: 1, title: 'Amber' }], total, 'self-test'), 'utf8');
    const film = join(work.dir, 'selftest.mp4');
    await mux(silent, track, meta, film);
    const seconds = await probeSeconds(film);
    const info = await ffmpeg(['-i', film]).catch((e) => String(e.stderr ?? e.message));
    const ok = Math.abs(seconds - 2) < 0.25 && /h264/.test(info) && /yuv420p/.test(info) && /Chapters?:/.test(info) && /Audio: aac/.test(info);
    console.log(`${basename(film)}: ${seconds.toFixed(2)} s, h264=${/h264/.test(info)}, yuv420p=${/yuv420p/.test(info)}, chapters=${/Chapters?:/.test(info)}, aac=${/Audio: aac/.test(info)}`);
    if (!ok) throw new Error('Self-test film is not as expected.');
    console.log('Self-test passed: ffmpeg-static can encode, concatenate, mix and mux on this machine.');
  } finally {
    work.remove();
  }
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  const args = process.argv.slice(2);
  try {
    if (args.includes('--self-test')) await selfTest();
    else {
      const i = args.indexOf('--chapter');
      await stitch({ chapter: i >= 0 ? String(args[i + 1]).padStart(2, '0') : undefined });
    }
  } catch (error) {
    console.error(error.message ?? error);
    process.exit(1);
  }
}
