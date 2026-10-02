# ADR-0242: Remaster offers an AI recorder that drives Jev under the user's own OpenRouter key

- Status: accepted (2026-10-02). The user picked *"Aceito"* after answering Q1–Q4 (below). It was requested verbatim: *"quero adiciona um gravador como o JEV como AI e BYK"*. Listed as slices **F14.19** (RAM maps first) and **F14.20** (the recorder) in PRD Part A §4, Phase 14. Not implemented. Accepting is a request for work, and implementation waits for an explicit go-ahead. The button stays disabled until ADR-0238 §5's two clauses pass on the F14.19 games (Q3). Wireframe: PRD Part B §13, W-R8; entry point on W-R1.
- Date: 2026-10-02
- Related: ADR-0238 (Jev as the stall helper — §3 harness, §4 artifact rule, §5 adoption), ADR-0185 (a movie is input, never evidence), ADR-0188 (an AI's judgement is a proposal, never evidence), ADR-0184 (RAM-only cheats), ADR-0241 (Play / Remaster / Share), PRD Part B §13 (W-R1, W-R3, W-R8)
- Supersedes / amends: amends ADR-0238 — the "not the default recorder" non-goal stays, but §5's verdict no longer keeps Jev out of the GUI; the adoption criterion for the GUI is ADR-0238 §5 unchanged, applied to the games Q2's RAM maps cover (Q3). ADR-0238 §1–§4 are unchanged.

## Context

Remaster has two recorders in the redesign (W-R1): *Record While I Play*
and *Record from a TAS Movie…*. Both need someone, or some published run,
that gets through the game. An artist who cannot pass a stage cannot record
what lies past it.

ADR-0238 already built the machine that plays without a human:
`scripts/jev_harness.py` runs a search on the step-mode emulator
(`scripts/step_emu.py`) and asks Jev (`typesafe/jev-1.13` via OpenRouter)
only at stalls, with a rewind ladder, per-game tips and hard budget caps.
Its output is a plain `<n>f <buttons>` script that replays without Jev
(§4). F14.15 measured it:

- **Clause 1 passes:** Jev passed three Mega Man 3 stalls the search alone
  could not, deterministically, at 3.6–4.0× real time.
- **Clause 2 fails:** the routes gained 0 keys that no committed pack
  already has, and Jev passed 0 of 4 arms on Ninja Gaiden's stall.

Clause 2 asks whether the *maintainers'* kit grows. A GUI recorder answers
a different question: whether *a user without a route* can record past
the point where they get stuck. F14.15 did not measure that.

Hard constraints the GUI inherits from what exists today:

- **Per-game RAM map.** The harness reads progress (position, camera,
  room, HP) from `scripts/stages/<game>/ram-map.json`. Only `mm3/` and
  `ninjagaiden/` ship one. Both already have committed routes, so today
  the button would add nothing the routes do not already record.
- **A start state.** The harness starts from a minted `.mss`. The GUI can
  mint it from the current game, the way Record While I Play does.
- **The key.** ADR-0238 reads `OPENROUTER_API_KEY` from the environment or
  a gitignored repo `.env`. A shipped app has neither. MesenAI stores no
  secret today.
- **Speed.** It is not real time: the emulator pauses while Jev decides.
  It is a job (W-R3), not a game view.

Non-goals:

- Jev never becomes the default recorder and never runs without the
  user's own key. MesenAI ships no key and pays for nothing.
- No pixels, screenshots or ROM bytes leave the machine (ADR-0238
  non-goal, unchanged). Jev only receives numbers derived from RAM.
- The `claude -p` web-research worker (ADR-0238 §3) is not part of the
  GUI path. It needs the Claude CLI and web search, and it writes tips
  that a maintainer reviews.
- No CI job calls Jev.

## Decision

1. **Entry point.** W-R1 zone ① gets a third button, *Let the AI Play…*,
   next to the TAS one. It is disabled, with a reason, when the game has
   no RAM map ("No AI map for this game yet"). It is not hidden (rule 4).
2. **The sheet (W-R8).** The sheet holds four things:
   - *Start from*: the current game or a project recording's state;
   - *Goal*: until the end of the stage, or a time cap;
   - *OpenRouter key*: entered once, shown masked, with *Change…*;
   - *Spend limit*: per run, default US$ 0.25.

   One sentence states what leaves the machine: "The AI sees numbers read
   from the game's memory — never the picture or the game file."
3. **The run** is `jev_harness.py` as a job, using the W-R3 job card.
   The card shows the stage, the stalls passed, the spend and *Stop*. The
   stage's game view is not shown while it runs, because Jev pauses the
   game. On finish, the produced script is replayed through the
   ordinary recorder. The replay never calls Jev, and the recording lands
   in the project like any other (ADR-0238 §4). A stall the AI gives up
   on is reported as "Stopped at <stage> x <pos> — try recording that
   part yourself".
4. **The key never touches** `settings.json`, logs, `runs/` sidecars,
   command lines or crash reports. It is passed to the harness through
   the child's environment only.
5. **The artifact** is the script plus its cheat list (if any), saved in
   the project. As with a TAS, it is input, never evidence (ADR-0185,
   ADR-0188).

## Open questions — all four answered (user, 2026-10-02)

- **Q1 — the key lives in the OS credential store** (*"Cofre do sistema"*):
  - macOS Keychain, Windows Credential Manager, libsecret on Linux, behind
    one small interface;
  - never in `settings.json`, logs, `runs/` or a command line;
  - handed to the harness through the child's environment only.

  This is the template for any later BYOK feature.
- **Q2 — RAM maps come first** (*"Seguir e fazer mapas antes"*). A
  prerequisite slice writes `scripts/stages/<game>/ram-map.json` (the
  progress fields: position, camera, room, HP) for golden games that have
  **no committed route** past their first stall. Until at least two such
  games have a map, the button stays disabled with its reason. The
  feature is kept, not archived.
- **Q3 — adoption keeps ADR-0238's kit criterion** (*"Ganho no kit"*). The
  GUI recorder is adopted only when an AI-driven recording adds keys that
  no existing pack or committed route has: the two clauses of ADR-0238 §5,
  clause 2 included, measured on the games Q2's maps cover. Until then the
  button stays disabled. Passing a stall the user could not pass is
  necessary, but it is not enough.

- **Q4 — the harness runs on the user's Python** (*"Mesmo gate do
  Python"*). It sits behind the same W-R0b "Needs Python 3" gate as the kit
  and the build, so nothing new is packaged. Not ported to C#.

## Consequences

- **First secret in the app.** Q1's answer becomes the template for any
  later BYOK feature (Phase 10's skin studio has the same need).
- **A paid vendor call on an `alpha` endpoint,** from users' machines. The
  model pin and the served-snapshot log stay. A 402/429 has to read as
  "your key is out of credit", never as a crash.
- **Cost is the user's.** At about US$ 0.000023 per decision, the default
  US$ 0.25 cap is about 10 000 decisions, far above one stage.
- **W-R1 grows from 5 to 6 controls at rest, and W-R3 from 5 to 6.** Both
  stay within rule 2.
- **Reproducibility never depends on the model:** the committed script is
  what replays.
