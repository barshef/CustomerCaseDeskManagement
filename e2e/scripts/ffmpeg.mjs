// Thin wrapper over the ffmpeg binary from the ffmpeg-static npm package (nothing is installed
// on the machine). Shared by stitch.mjs and speech.mjs.
import ffmpegPath from 'ffmpeg-static';
import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

export const FFMPEG = ffmpegPath;

/** Runs ffmpeg with `args`; resolves with stderr (ffmpeg logs there), rejects on a non-zero exit. */
export function ffmpeg(args, { quiet = true } = {}) {
  if (!FFMPEG) return Promise.reject(new Error('ffmpeg-static has no binary for this platform. Run `npm ci` in e2e/.'));
  return new Promise((resolve, reject) => {
    const child = spawn(FFMPEG, ['-hide_banner', '-y', ...args], { windowsHide: true });
    let err = '';
    child.stderr.on('data', (chunk) => {
      err += chunk;
      if (!quiet) process.stderr.write(chunk);
    });
    child.on('error', reject);
    child.on('close', (code) => {
      if (code === 0) resolve(err);
      else {
        const error = new Error(`ffmpeg exited ${code}: ${err.split(/\r?\n/).filter(Boolean).slice(-6).join('\n')}`);
        error.stderr = err; // full log, e.g. for the "Duration:" header of `ffmpeg -i`
        reject(error);
      }
    });
  });
}

/** Duration of a media file in seconds (parsed from ffmpeg's "Duration:" line). */
export async function probeSeconds(file) {
  let out = '';
  try {
    out = await ffmpeg(['-i', file]);
  } catch (error) {
    out = String(error.stderr ?? error.message); // `ffmpeg -i` with no output always "fails"; the header is still printed
  }
  const m = /Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)/.exec(out);
  if (!m) throw new Error(`Could not read the duration of ${file}`);
  return Number(m[1]) * 3600 + Number(m[2]) * 60 + Number(m[3]);
}

/**
 * A short temporary folder for ffmpeg's work. Windows paths over 260 characters trip some ffmpeg
 * builds, and the repo path is long, so all intermediate files live here and are removed after.
 */
export function workFolder(prefix = 'cdfilm-') {
  const dir = mkdtempSync(join(tmpdir(), prefix));
  return {
    dir,
    remove: () => rmSync(dir, { recursive: true, force: true }),
  };
}

/** ffmpeg concat-list line for a path (forward slashes, single quotes escaped). */
export function concatLine(file) {
  return `file '${file.replace(/\\/g, '/').replace(/'/g, "'\\''")}'`;
}
