# CaseDesk guide films

This folder holds the published guide films. They are committed so the films can be shown to clients and linked from the docs.

| File | What it is |
| --- | --- |
| `casedesk-demo.mp4` | The full film: title card, then chapters 1–8 of the demo script, with caption band and (when a voice key was set) voice-over. H.264, 1920×1080, plays in any normal player and in browsers. |
| `casedesk-demo.chapters.txt` | The chapter marks in FFMETADATA format (already embedded in the mp4). |
| `casedesk-demo.vtt` | The captions as WebVTT, for players that show subtitles. |
| `casedesk-demo-NN.*` | A one-chapter film made with `npm run film -- --chapter NN`. |

A film is only copied here when every chapter of its run passed. To make or remake one, see [docs/walkthrough-films.md](../../docs/walkthrough-films.md).

Films never contain passwords, function keys or voice keys. Sign-in happens before recording starts, and the external system's reply in chapter 7 is fetched by the script, not opened in the browser.
