// Voice-over for the films: Azure AI Speech (REST, neural voice), reading the captions.
//
// Key and region come from the PC's user environment variables DEMO_SPEECH_KEY and
// DEMO_SPEECH_REGION (e.g. uksouth). A shell opened before `setx` does not see them, so on
// Windows they are also read from HKCU\Environment. No key → no voice, and the film is made
// silent without failing. DEMO_SPEECH=none forces a silent film; DEMO_VOICE picks another voice
// (default en-GB-SoniaNeural).
//
// Every clip is cached in e2e/.cache/speech/<voice>/<sha256>.wav, so re-making a film costs
// nothing for captions that did not change.
//
//   node scripts/speech.mjs --say "Agent A opens a new case."     makes (or reuses) one clip, prints its path
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { ffmpeg } from './ffmpeg.mjs';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
export const CACHE_DIR = join(root, '.cache', 'speech');
export const DEFAULT_VOICE = 'en-GB-SoniaNeural';
const OUTPUT_FORMAT = 'riff-24khz-16bit-mono-pcm';

function fromWindowsUserEnv(name) {
  if (process.platform !== 'win32') return undefined;
  try {
    const out = execFileSync('reg', ['query', 'HKCU\\Environment', '/v', name], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] });
    const m = new RegExp(`${name}\\s+REG_(?:EXPAND_)?SZ\\s+(.+)`).exec(out);
    return m ? m[1].trim() : undefined;
  } catch {
    return undefined;
  }
}

/** { key, region, voice } or null when there is no voice-over. */
export function speechConfig(env = process.env) {
  if ((env.DEMO_SPEECH ?? '').toLowerCase() === 'none') return null;
  const key = env.DEMO_SPEECH_KEY || fromWindowsUserEnv('DEMO_SPEECH_KEY');
  const region = env.DEMO_SPEECH_REGION || fromWindowsUserEnv('DEMO_SPEECH_REGION');
  if (!key || !region) return null;
  return { key, region, voice: env.DEMO_VOICE || DEFAULT_VOICE };
}

const escapeXml = (text) =>
  text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&apos;');

export function ssml(text, voice) {
  const lang = voice.split('-').slice(0, 2).join('-');
  return `<speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="${lang}"><voice name="${voice}">${escapeXml(text)}</voice></speak>`;
}

export function clipPath(text, voice) {
  const hash = createHash('sha256').update(`${voice}\n${text}`, 'utf8').digest('hex').slice(0, 40);
  return join(CACHE_DIR, voice.replace(/[^A-Za-z0-9_-]/g, '_'), `${hash}.wav`);
}

/** Seconds of sound in a PCM WAV file, from its header. */
export function wavSeconds(bytes) {
  if (bytes.length < 44 || bytes.toString('ascii', 0, 4) !== 'RIFF') throw new Error('Not a WAV file.');
  let offset = 12;
  let byteRate = 0;
  while (offset + 8 <= bytes.length) {
    const id = bytes.toString('ascii', offset, offset + 4);
    const size = bytes.readUInt32LE(offset + 4);
    if (id === 'fmt ') byteRate = bytes.readUInt32LE(offset + 16);
    if (id === 'data') return byteRate ? Math.min(size, bytes.length - offset - 8) / byteRate : 0;
    offset += 8 + size + (size % 2);
  }
  throw new Error('WAV file has no data chunk.');
}

/** Makes (or reuses from the cache) the clip for `text`. Returns { file, seconds }. */
export async function synthesize(text, config) {
  const file = clipPath(text, config.voice);
  if (!existsSync(file)) {
    const response = await fetch(`https://${config.region}.tts.speech.microsoft.com/cognitiveservices/v1`, {
      method: 'POST',
      headers: {
        'Ocp-Apim-Subscription-Key': config.key,
        'Content-Type': 'application/ssml+xml',
        'X-Microsoft-OutputFormat': OUTPUT_FORMAT,
        'User-Agent': 'casedesk-walkthrough',
      },
      body: ssml(text, config.voice),
    });
    if (!response.ok) throw new Error(`Azure Speech answered ${response.status} ${response.statusText}`);
    mkdirSync(dirname(file), { recursive: true });
    writeFileSync(file, Buffer.from(await response.arrayBuffer()));
  }
  return { file, seconds: wavSeconds(readFileSync(file)) };
}

/**
 * Lays clips on one track at the cue times (seconds into the film), never overlapping: a clip
 * that would start before the previous one ends waits for it. Mixed with adelay + amix
 * (normalize=0, so volume does not drop with the number of clips), padded/trimmed to the film.
 *
 * `clips` = [{ at, file, seconds }]. Returns the path of the track (WAV).
 */
export async function layTrack(clips, totalSeconds, outFile) {
  const placed = [];
  let free = 0;
  for (const c of [...clips].sort((a, b) => a.at - b.at)) {
    const start = Math.max(c.at, free);
    if (start >= totalSeconds) continue;
    placed.push({ ...c, start });
    free = start + c.seconds + 0.15;
  }
  if (!placed.length) return null;
  const args = [];
  for (const p of placed) args.push('-i', p.file);
  const chains = placed.map((p, i) => `[${i}:a]aresample=48000,adelay=delays=${Math.round(p.start * 1000)}:all=1[v${i}]`);
  const mix = `${placed.map((_, i) => `[v${i}]`).join('')}amix=inputs=${placed.length}:normalize=0:dropout_transition=0,apad,atrim=0:${totalSeconds.toFixed(3)}[out]`;
  args.push('-filter_complex', [...chains, mix].join(';'), '-map', '[out]', '-ac', '1', '-ar', '48000', outFile);
  await ffmpeg(args);
  return outFile;
}

/**
 * The voice-over for a film: synthesises each cue's text and lays it at its time.
 * `cues` = [{ at, text }]. Returns the track path, or null (no key, or nothing to say).
 */
export async function buildVoiceTrack(cues, totalSeconds, outFile, config = speechConfig()) {
  if (!config) {
    console.log('No DEMO_SPEECH_KEY / DEMO_SPEECH_REGION: the film is silent.');
    return null;
  }
  const clips = [];
  for (const cue of cues) {
    const clip = await synthesize(cue.text, config);
    clips.push({ at: cue.at, ...clip });
  }
  console.log(`Voice-over: ${clips.length} captions in ${config.voice}.`);
  return layTrack(clips, totalSeconds, outFile);
}

// CLI: node scripts/speech.mjs --say "text"
if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  const i = process.argv.indexOf('--say');
  if (i < 0 || !process.argv[i + 1]) {
    console.error('Usage: node scripts/speech.mjs --say "text to speak"');
    process.exit(2);
  }
  const config = speechConfig();
  if (!config) {
    console.log('No DEMO_SPEECH_KEY / DEMO_SPEECH_REGION set: nothing to do (films will be silent).');
    process.exit(0);
  }
  const clip = await synthesize(process.argv[i + 1], config);
  console.log(JSON.stringify(clip));
}
