# Play couch GUI: ratifying the autonomy panel's decisions

**Purpose:** While the owner was away, an AI "autonomy panel" (Claude Opus 5.5 acting as human proxy) and the coordinating agent made 7 judgment calls on the couch-ready Play GUI work (spec #1102, issues #1103–#1107, #1111, #1134). Each is labelled `needs-ratification`. The owner needs an engineer's verdict on each: keep the choice that shipped, or pick a different alternative.

**From:** the owner (sbihaiko), **To:** tech lead / architect, **How your answers will be used:** each answer is recorded on its issue (quoted as the ratification), and any "change it" answer becomes a follow-up issue before the work is considered final.

## Context

The spec #1102 was split into slices; every slice shipped to `main` (e2e `UI.Tests` green, 0 failures on the latest main). Several slices hit the circuit breaker (4–5 review rounds, each finding a new class of defect), so the panel picked an option instead of continuing to iterate. Those picks are provisional: they were cheapest to reverse, not necessarily best. Nothing here needs you to run code; the references (issue numbers, PRs, ADR-0268/0269/0270) show what was decided and where.

## How to answer

About 20 minutes. For each question, put **a**, **b**, **c** (or your own option) under the stub and, if you disagree with the shipped choice, one line on why. Partial answers and "I don't know" are useful: mark what you are unsure of instead of skipping it. Deadline: [set before sending].

## Release risk first

### Menu sounds (#1105): which audio path should the sounds use?

_Why this matters: the first design had a second writer pausing and unpausing the game's audio device; four review rounds each found a new Core audio defect. The panel cut the scope to the Settings row and wiring, and the sounds later shipped on a separate host-owned output stream (ADR-0270, PRs #1117, #1148, #1156). Listening on real hardware has not been done._

- **a)** Keep the host-owned separate stream (ADR-0270), as shipped.
- **b)** Mix the sounds into the emulation audio path instead.
- **c)** Cut menu sounds from this release (the spec names haptics, not sounds, as the first slice to cut).

>

### Haptic tick (#1106): is macOS-only acceptable for now?

_Why this matters: Windows XInput and Linux evdev backends kept failing review on timing and threading, and no CI job compiles them. The panel shipped the host API and the macOS tick only; Windows and Linux report every pad as not aimable and run no tick code (follow-ups #1121, #1122). The reasoning: a pad stuck buzzing is worse than a missing nicety._

- **a)** Ship macOS only, as done; the Menu tick row is hidden for pads that cannot be aimed.
- **b)** Hold the feature until all three backends are tested on real hardware.
- **c)** Remove the tick feature altogether.

>

If **a**: which follow-up should go first, #1121 (Windows XInput) or #1122 (Linux evdev), and does either need hardware we must buy or borrow?

>

### Interface size (#1111): is 1024x640 the right size to pin the layout test to?

_Why this matters: ADR-0269 D6 said "minimum window size" (512x505); the spec and PRD draw Play at about 1024x640. The panel pinned the test at 1024x640 and amended D6 accordingly, and moved the minimum-width cap and the pill scale to #1123 and #1124. Extra large (1.5x) is only proven at the larger size._

- **a)** Keep 1024x640 as the pinned size; ship with #1123/#1124 open.
- **b)** Require Extra large to fit at the true minimum window (512x505) before release.
- **c)** Something else (say what).

>

## Process shortcuts taken

### Pad-walk test (#1107): was it right to merge over two blocking review findings?

_Why this matters: PR #1131 merged (cb7af1bd9) with two blocking reviewer findings left. The panel fixed two, and filed the other three as harness-strictness follow-up #1134 (later merged in #1136 and #1139). Every later slice now relies on this test._

- **a)** Accept: the strictness items only bite future regressions, and #1134 closed them.
- **b)** Split the harness out and re-review it on its own before more slices lean on it.
- **c)** Abandon the approach (say what you would use).

>

### Shared action bar (#1104): is quarantining one flaky test acceptable?

_Why this matters: the connected-count headless test is skipped behind #1129 (Flaky trait + skip message). A deterministic `UI.Tests` unit test covers the same acceptance criterion, but headless tests skip silently in CI, so the skipped one gives no CI signal._

- **a)** Keep the quarantine; the unit test is enough coverage.
- **b)** Keep it, but put a deadline on #1129 (say when).
- **c)** Fix the test before accepting the slice.

>

## Product and decision records

### Focus leaks on Home (#1134 / #1137): where should the pad's focus be allowed to land?

_Why this matters: on Home, pad navigation moves focus to ProfileButton and ToolsMenuButton, which sit outside the PlayHomeHost root the walk checks. The coordinating agent recorded them as a named, asserted-both-ways `KnownFocusLeaks` list and made no product change._

- **a)** Keep the named known-leaks list (current).
- **b)** Widen the walk's root to include those two controls.
- **c)** Treat it as a product bug: keep focus inside the host (#1137).

>

### ADR-0254 amendment (#1103): should the controller-disconnect pause obey PauseWhenInBackground?

_Why this matters: the panel left this open; the coordinating agent decided alone that the pause is NOT gated by PauseWhenInBackground. A player who unplugs a pad while another window is focused is the case where it matters._

- **a)** Not gated, as decided: disconnect always pauses.
- **b)** Gated: if the user chose not to pause in the background, do not pause.

>

### Favorites (#1103, ADR-0268): is the model right when a file vanishes?

_Why this matters: X toggles Favorite on a focused cover and Y stays search. The list is newest-first, identified by library path, hidden when empty. If the file disappears, the entry is kept and drawn dimmed in the library, so X can still unfavorite it._

- **a)** Keep the entry, dimmed (as decided).
- **b)** Remove it silently once the file is gone.
- **c)** Keep it, but also show a "missing file" cue.

>

## Anything else?

Anything we did not ask that we should know, such as a decision above that you would not have framed this way?

>
