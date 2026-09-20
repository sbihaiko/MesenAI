# ADR-0214: A fresh Opus session is the evaluator for an artist-surface cold read

- Status: **accepted 2026-09-19**, amended the same day — the evaluator is a
  fresh **Opus** session, not Fable. The protocol (briefing, dispatcher
  script, label-identity test) shipped on 2026-09-19 under the original
  Fable wording; the amendment comes after the same protocol was run 28
  times, once per ROM in the library, with Opus in the seat
  (`docs/validation/f12.2-opus-sweep-2026-09-19.md`). User go-ahead for the
  amendment, verbatim: *"se funcionar, substitua definitivamente o fable
  pelo opus"*, on the strength of the sweep's criterion 1 (28 of 28 runs
  named the action unaided, 5–55 s) and criterion 4 (one self-reported
  failure in 28). The original go-ahead, verbatim: *"quero usar o Fable,
  ajuste o que for necessário"*.
- Date: 2026-09-19
- Related: PRD Part A F12.2 (and any later Phase 12 slice whose stop rule is a cold-read of what the artist sees), ADR-0188 (an AI judgement is a proposal), ADR-0150 (Avalonia.Headless), C.5 logs (`docs/validation/c5-fable-artist-run-zelda-2026-09-14.md`, `…-mega-man-3-2026-09-14.md`), `docs/validation/f12.2-fable-panel-2026-09-19.md` (the Fable half, kept as the record of the two-game panel this amendment supersedes as the standing evaluator)
- Amends: PRD Part A Phase 12 principle "a person who did not build it logs the cold-read rows"; F12.2's "human panel row"; the F12.2 mechanical-replay log's claim that the remaining half is only measurable on a person. Does **not** amend F9.18, S10.b, or ADR-0188 §5 (promotion stays gated).

## Context

F12.2's remaining row asked a person who did not build the feature to find
*Copy as MEP sheet cell*, paste one cell and paint it without reading
`hires.txt`. The structural and runtime halves of that row already exist:
`UI.HeadlessTests/CopyAsMepSheetCellTests.cs` drives the real Tilemap Viewer
and asserts the clipboard; `scripts/replay_f122_panel.py` replays setup
S1–S4 and paste-and-paint P9–P14 until a magenta pixel is in a screenshot
(`docs/validation/f12.2-mechanical-replay-2026-09-19.md`). Those cannot
close the row. A machine that looks the item up by `ActionType` cannot be
surprised by the UI; the 2026-09-18 P5/P6 corrections (grid off by default;
hover is not a click) came from an evaluator who was.

The project's standing goal already names Fable as the proxy for human
decision and vision. Phase 11 C.5 ran that proxy on the one-hour
record→kit→paint path: two fresh sessions, 12 and 11 minutes, real defects
(#253/#255/#256, guide gaps). The PRD then classified C.5 as a completed
*proxy experiment*, not product acceptance, because the sessions opened
`hires.txt` to diagnose a green lint, and because "I would switch from my
spreadsheet" from a model that has never used the spreadsheet is not a
revealed preference.

Leaving F12.2's last 20 minutes as "a person, once" after that history is
the same stall: there is no external user, every previous panel has been a
proxy or a builder, and the mechanical replay already took the half a
person should not spend a cold read on. The honest move is to name Fable
as the evaluator, write a protocol that does not hand it the answer, and
say which criteria that can close.

Non-goals: a pointer driver for Avalonia context-menu flyouts (the AX tree
does not expose them; `System Events` keystrokes do not reach the window —
`docs/validation` GUI-capability notes). This ADR does not claim Fable
right-clicks. It does not auto-promote an AI proposal (ADR-0188 §5). It
does not close F9.18. It does not treat P15 ("beats a spreadsheet?") as a
gate.

## Decision

### 1. The evaluator is a fresh Opus session

For a Phase 12 slice whose remaining acceptance is a cold-read of what the
artist sees, the evaluator is a **new Opus agent** (`Agent`, not `fork`;
model `opus`) with no prior context from the builder session. The C.5
shape stands: the session sees the binary, the three artist guides, the
ROMs, and the work copies. It does not see the repository checkout's
`docs/adr/`, the PRD, `scripts/*.py` source, the panel dispatcher script,
the mechanical replay, this ADR, the memory directory, or any session
transcript. Reading anything outside that sandbox is a **stop**, logged
with the clock time and the path; it does not by itself fail the run
unless the file is `hires.txt` (criterion 4) or the dispatcher script /
this ADR (the run is then invalid, not failed — it was not a cold read).

Dispatch is a separately scoped task. Accepting this ADR is a request for
that run, not the run.

**Why Opus and not Fable (amendment, 2026-09-19).** Fable ran the panel
twice and passed it (`docs/validation/f12.2-fable-panel-2026-09-19.md`).
The sweep then ran the same protocol on 28 ROMs with Opus, because one game
is a panel and 28 are a sample: the difference between "this game's pack is
odd" and "the feature is broken" only shows up at 28, and the sweep found
seven defects the two-game panel could not, including a silent wrong-bank
key on CHR-banked games (#341). Opus was chosen for the standing seat on
that evidence, not against Fable's: **28 of 28 runs named the action
unaided from the label dump between 5 and 55 seconds**, and 27 of 28 never
opened `hires.txt` — the one failure self-reported and caused by a missing
feature, not by a weaker evaluator. The seat is a model that
understands the goal and can follow a rule it was given; both did.

### 2. The briefing is the goal, not the path

The evaluator is given `docs/validation/f12.2-sweep-evaluator-briefing.md`
(one game; `docs/validation/f12.2-fable-evaluator-briefing.md` is the
Fable-era two-game version, kept as the record) and nothing that names
`Debug > Tilemap Viewer`, `Ctrl+1`, `SelectionRect`, `ActionType`, crop
pixels, or the P5–P14 answer key.
`docs/validation/f12.2-copy-sheet-cell-panel-script.md` is the
**dispatcher** script: setup S1–S4, the dump in §3, scoring, known traps.
Handing it to the evaluator invalidates the run the same way handing a
person the marked exam would.

### 3. The menu's identity is the visible label

What the evaluator can be asked to find is the string a user reads, not
the enum the code uses. `Copy as MEP sheet cell` is
`ResourceHelper.GetEnumText(ActionType.CopyToMepSheetCell)` from
`UI/Localization/resources.en.xml`. The headless test that stands in for
"the item is on the menu" finds that entry by `Name`, then checks it is
wired to `ActionType.CopyToMepSheetCell`. Looking it up by enum first is
the mechanical replay's job, and is the opposite of a cold read.

Because this machine cannot open an Avalonia context-menu flyout, criterion
1 is scored on a **label dump** of the Tilemap Viewer's visible, enabled
context-menu entries at a selected tile, produced in setup (not on the
evaluator's clock) and placed in the evaluator's sandbox as
`tilemap-menu-labels.txt` — the menu a right-click would have shown. The
same dump is pasted into the dated log's Binary and inputs header. The evaluator names the line it would click, in under two minutes
from pause, without being told which. Picking *Copy tile (HD pack format)*
is a fail of criterion 1, not a skip. Pointer-level discoverability
(hover vs click, grid off, flyout never appearing) stays **not
evaluated** until a pointer harness exists; it is not recorded as a pass.

### 4. Which criteria the evaluator may close

| # | Criterion | May the evaluator close it? |
|---|---|---|
| 1 | The action is findable from visible labels, under 2 min from pause | **yes**, on the label dump in §3. Pointer/AX discoverability is not evaluated |
| 2 | Clipboard text is usable as-is | **yes** — paste into a scratch file; valid JSON, one line, no hand-edit of the object |
| 3 | Paste-and-paint reaches the screen | **yes** — magenta (or any unmistakable recoulour) on the picked shape after `mep_build.py build` and a reopen; the mechanical replay remains the pixel oracle if the evaluator's pack is handed to it |
| 4 | `hires.txt` never opened | **yes** — any read is a fail, with the clock time and the question it answered. This is the C.5 miss, now a gate |
| 5 | Round-trip through `mep_build.py build` | **yes** — already mechanical; the evaluator's key must come back as a `<tile>` at the painted crop |
| 6 | `mep_lint.py` exits 0 | **yes** |
| 7 | P1→P14 under 20 min per game | **yes**, wall clock of the evaluator's session |
| 8 | Did they expect feedback after clicking? | **observation**, recorded verbatim or omitted. Not a gate |
| P15 | Would this beat typing the key from a spreadsheet? | **observation**. An evaluator's "yes" does not close a market claim; it is logged as the C.5 sentence was, and is not a pass |

A run that fails criterion 4 (opens `hires.txt`) cannot be scored as
product acceptance even if the figure appears. That is the rule C.5 lacked.

### 5. What this does not become

- Evidence that a human would return, or that the tool displaces Excel.
  Those stay unmeasured until a person who already ships packs from a
  spreadsheet repeats the briefing. Reopening that is a product decision,
  not a follow-up slice of this ADR.
- A substitute for ADR-0188. Naming a figure from a PNG is still a
  proposal; promoting it still needs `promote`. The evaluator here is
  walking a GUI path, not judging art.
- A substitute for the mechanical replay. S1–S4 and P9–P14 stay green on
  a machine so the evaluator's run cannot be silently defeated by `mep/`
  precedence, a stray `EnhancementPacks/` stub, or a whole-screen
  `<background>` (replay findings 1–3).

## Consequences

- **F12.2's open row is an evaluator dispatch, not a calendar wait for a
  person.** The row still exists until the dated log is filled; it is no
  longer blocked on finding an external artist.
- **The dispatcher must not contaminate the session.** The briefing file
  is the only evaluator-facing document. If a future slice needs a
  similar panel, it gets its own briefing; it does not reuse this one
  with the answers filled in.
- **Criterion 1 is weaker than a person's right-click and is labelled
  so.** A pass on the label dump does not prove the flyout opens,
  that hovering was not mistaken for a click, or that the silent
  clipboard (known trap 2) was understood. Those stay in the dispatcher
  script as known traps and in criterion 8 as an observation.
- **C.5's "pass" is not a precedent for ignoring `hires.txt`.** Criterion
  4 is the load-bearing change this ADR makes to that experiment's
  scoring.
- F9.18, hardware/audio listening, and "would a human return" are
  unchanged. A later decision to run F9.18 the same way is a new ADR or
  an amendment, not an implication.
