# Browser walkthroughs and guide films

The walkthrough runs CaseDesk's demo script (quickstart §4, steps 1–8) in a visible Chromium window, the way the three demo users would. A caption band at the top of the screen says who is acting and what they do. The same run, in film mode, records every persona's screen and turns it into a guide film with a title card, a chapter card before each step and, optionally, a spoken voice-over.

The code is in `e2e/`. It is a Playwright Test project. The browser is always headed, never headless.

| Chapter | File | What it shows | Personas |
| --- | --- | --- | --- |
| 1 | `01-log-case.spec.ts` | A High Billing case gets case number `CD-NNNNN`, a due date (created + 8 h) and a warning time (due − 2 h) without anyone typing them | Agent A |
| 2 | `02-open-note-blocks-resolve.spec.ts` | An open note blocks Resolve with `CD004`; the agent closes the note and the case resolves with Resolved On set | Agent A |
| 3 | `03-refund-approval.spec.ts` | A Refund case goes to Awaiting Approval on Resolve; the supervisor sees the Pending Refund Approval row | Agent A, Supervisor |
| 4 | `04-creation-email.spec.ts` | The creation email is on the case timeline, created within 90 s of the case | Agent A |
| 5 | `05-sla-badge.spec.ts` | The SLA badge is amber in the warning window, then red once the case is overdue | Supervisor |
| 6 | `06-escalate.spec.ts` | Escalate → reason → Critical, owner CaseDesk Supervisors, "Escalated by…" note, in one action | Agent A, Supervisor |
| 7 | `07-caseclosed-integration.spec.ts` | Closing a resolved case gets a CaseClosed message to the external stub within 90 s | Agent A |
| 8 | `08-business-unit-access.spec.ts` | Agent B (BU B) cannot open Agent A's case link; the supervisor can | Agent B, Supervisor |

Chapters run in file order with one worker. They pass record ids along in `e2e/test-results/walkthrough-state.json`. Every case they create has a title starting with `[Walkthrough]`. A chapter run on its own makes whatever case it needs through the Web API first.

## Before the first run

You need:

1. **The Dev environment, deployed and seeded.** The solution is imported, plugin steps and the Custom API are registered, the cloud flows are on, and the Function app and service endpoints are set up (quickstart §2). Then run the seeder (`tools/CaseDesk.Seeder`). It creates the business units, the SLA policies, the `CaseDesk Supervisors` team and the demo accounts and contacts the chapters pick (Northwind Traders, Fabrikam Logistics, Tailspin Toys, Adventure Works Cycles).
2. **Three demo users**, created by hand in the Power Platform admin centre:

   | Persona | Business unit | Security role | Also |
   | --- | --- | --- | --- |
   | `agentA` | CaseDesk BU A | CaseDesk Agent | — |
   | `agentB` | CaseDesk BU B | CaseDesk Agent | — |
   | `supervisor` | root business unit | CaseDesk Supervisor | Member of the team **CaseDesk Supervisors**; an approver for refund approvals |

   Each user also needs a licence that lets them use the CaseDesk app.
3. **`e2e/.env`.** Copy `e2e/.env.example` to `e2e/.env` and fill in:
   - `CASEDESK_URL`: the Dev environment URL, e.g. `https://contoso-dev.crm11.dynamics.com`.
   - `CASEDESK_APP_ID`: the CaseDesk app's id (Power Apps → Apps → CaseDesk → Details).
   - `STUB_URL`: the ExternalStub GET endpoint, `https://<func>.azurewebsites.net/api/external/caseclosed`.
   - `STUB_KEY`: the ExternalStub function key (Azure portal → Function app → ExternalStub → Function keys).

   `.env` is git-ignored. Never commit it.

## Install

```powershell
cd e2e
npm ci
npx playwright install chromium
```

`npm ci` also fetches the ffmpeg binary (package `ffmpeg-static`, approved in `package.json` under `allowScripts`). Nothing else needs to be installed on the PC.

## Sign in once per persona

```powershell
npm run login -- agentA
npm run login -- agentB
npm run login -- supervisor
```

Each command opens a visible browser on the CaseDesk app. Sign in as that persona's user, complete MFA and answer **Yes** to "Stay signed in?". When the app has loaded, the script prints who signed in, with their business unit and roles. It warns if they don't match the persona, then saves the sign-in to `e2e/.auth/<persona>.json` (git-ignored) and closes. Use a private window or sign out of other accounts first if the browser picks the wrong one.

## Run the walkthrough

```powershell
npm run walk                 # all 8 chapters, headed, as a plain test run
npm run walk -- 06           # one chapter (a file filter: "06" matches 06-escalate.spec.ts)
npx playwright show-report   # the HTML report of the last run
```

A plain walk does not hold captions or cards. With the 90 s waits for the email and the CaseClosed message, all chapters take about 5–8 minutes. When a chapter fails, the report shows which one and keeps a screenshot of each persona's page and its recording up to the failure.

### Chapter 3 and the approval

The refund is approved in Teams or Outlook (Approvals, "first to respond"). The walkthrough can't drive that reliably: it is a different app with its own sign-in, and the card arrives after a delay. So chapter 3 runs in one of two ways:

- **Default:** it shows Resolve turning into Awaiting Approval and the Pending Refund Approval row, and asserts exactly that.
- **`APPROVE_BY_HAND=1`** (in `.env` or the shell, meant for films): after the same steps, the caption asks you to approve. The run waits up to `APPROVE_WAIT_MINUTES` (default 10) while you approve the request by hand as the supervisor in Teams or Outlook. It then shows the case Resolved and the row Approved. The wait is cut out of the film.

### Chapter 5 and the SLA dates

Chapter 5 doesn't use the seeder's `--sla-demo` cases. Those are only amber for 90 minutes after the seeder runs. Instead, the supervisor creates a `[Walkthrough]` case and moves its Due Date and Warning On through `Xrm.WebApi` from the signed-in page. It is the same direct date write the seeder uses, and it works for the same reason: the pre-update plugin step doesn't fire on the date columns. Each run is therefore self-contained.

## Make a film

```powershell
npm run film                       # all chapters → e2e/demo-out/casedesk-demo.mp4
npm run film -- --chapter 06       # one chapter  → e2e/demo-out/casedesk-demo-06.mp4
```

Film mode (`DEMO=1`) slows actions down, types letter by letter and holds each caption until it has been read or spoken. It shows the title card on the first chapter and a chapter card before each chapter, and records every persona's browser context as its own clip. Afterwards:

1. If any chapter failed, nothing is stitched or published. Fix the failure and run again.
2. `scripts/stitch.mjs` cuts each chapter into shots, one per persona on screen. It drops each clip's idle lead-in (sign-in redirects, the app loading) and shortens marked waits to a second or so either side. It encodes the shots to H.264 1920×1080 and joins them in chapter order. The output gets chapter marks, a WebVTT caption file and the voice-over.
3. `scripts/publish.mjs` copies `casedesk-demo.mp4`, `.chapters.txt` and `.vtt` to `e2e/demo-out/`, but only when every expected chapter passed.

Films are made on the PC when wanted, never in CI: they need interactive sign-ins and a live Dev environment. Commit the new files in `e2e/demo-out/` in a change of their own.

To check that ffmpeg works on a PC without running anything else:

```powershell
node scripts/stitch.mjs --self-test
```

It builds two one-second colour clips, joins them with a tone track and chapters, and checks the result.

## Voice-over

The voice is Azure AI Speech (neural voice `en-GB-SoniaNeural`). Set the key and region once as user environment variables:

```powershell
setx DEMO_SPEECH_KEY "<speech resource key>"
setx DEMO_SPEECH_REGION "uksouth"
```

Open a new terminal afterwards. The scripts also read the values from the Windows user environment directly, so an older terminal works too. Options:

- `DEMO_VOICE=en-GB-RyanNeural` uses another voice.
- `DEMO_SPEECH=none` makes a silent film even when a key is set.
- No key at all: the film is made silent and nothing fails.

Each spoken caption is cached in `e2e/.cache/speech/` (git-ignored), keyed by voice and text, so remaking a film only pays for captions that changed. To try a sentence: `node scripts/speech.mjs --say "Agent A opens a new case."`.

## Where things go

| Path | What | In git |
| --- | --- | --- |
| `e2e/.env` | Environment URL, app id, stub URL and key | No |
| `e2e/.auth/*.json` | Saved sign-ins per persona | No |
| `e2e/.cache/speech/` | Cached voice clips | No |
| `e2e/test-results/` | Last run: state, `captions.json`, raw clips (`film/`), failure screenshots and videos | No |
| `e2e/playwright-report/` | HTML report of the last run | No |
| `e2e/demo-out/` | Published films | Yes |

## How it is built

- `e2e/demo/personas.ts`: the `asPersona(name)` fixture. It opens a new browser context with the persona's saved sign-in, at 1920×1080, with the overlay and its own video. The `chapter(page, heading, sub)` fixture shows the title and chapter cards.
- `e2e/demo/caption.ts`: the caption band, cards and data panel. They live in a closed shadow root with `aria-hidden` and `pointer-events: none`, so `getByText`/`getByRole` never match caption words and clicks pass through. The band pushes the app down by its height instead of covering it (`DEMO_BAND=overlay` lays it over the app instead). Every caption is written with its time to `test-results/captions.json` for the voice-over and the cuts.
- `e2e/demo/mda.ts`: model-driven app helpers and every UI selector (see below).
- `e2e/demo/cases.ts`: seed data names and the Web API set-up used when a chapter runs on its own.
- `e2e/demo/soon.ts`: `soon(locator)` waits and answers true or false instead of `isVisible()`, which doesn't wait. `poll()` waits for a server-side outcome.
- `e2e/scripts/`: `login.mjs`, `film.mjs`, `stitch.mjs`, `speech.mjs`, `publish.mjs` and `ffmpeg.mjs`.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| "No saved sign-in for …" or the page lands on the Microsoft sign-in screen | The saved sign-in is missing or has expired. Run `npm run login -- <persona>` again. |
| A field, button or dialog is not found | All selectors are in `SEL` at the top of `e2e/demo/mda.ts`. Open the form in a normal browser, inspect the control's `data-id` and fix it there. |
| Lookups don't pick a result | Check `SEL.lookupInput` / `SEL.lookupResult`, and that the seeder has run (the chapters search for the seeded account and contact names). |
| Chapter 1 due date is off by an hour | Check the user's time zone setting is right. The check compares UTC values with 3 minutes of slack. |
| Chapter 4 or 7 times out | The cloud flow is off or its connection reference is broken (4), or the service endpoint, Function app or `STUB_URL`/`STUB_KEY` is wrong (7). Check Application Insights `traces | where message has "CaseClosed"`. |
| Chapter 8 does not show an access error | Agent B's user is not in CaseDesk BU B, or has a role with wider than business-unit read on cases. |
| The app's layout looks squashed under the caption band | Use `DEMO_BAND=overlay`, or change how the band pushes the page in `caption.ts`. |
| The film is silent though a key is set | Run `node scripts/speech.mjs --say "test"` to see the Azure error. Check the region matches the key's resource. |

## Lessons

Fill this in after the first real run against Dev: selectors that changed, timings that needed raising, anything a model-driven app did differently from the assumptions in `mda.ts`.

-
