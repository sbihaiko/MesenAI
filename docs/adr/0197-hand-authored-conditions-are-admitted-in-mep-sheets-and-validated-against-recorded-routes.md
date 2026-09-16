# ADR-0197: Hand-authored conditions are admitted in MEP sheets and validated against the recorded routes; the toolchain still never emits the refused three

- Status: proposed (2026-09-16) — open question in §3: whether the recorder
  should start retaining watched memory values so `memoryCheckConstant` can be
  validated at all. Implementing slices, once accepted: PRD Part A §4,
  Phase 12, F12.6a (lint) and F12.6b (recorder capture)
- Date: 2026-09-16
- Related: ADR-0189 §4 (the three refused condition types), ADR-0190
  (`tileNearby` auto-attached), ADR-0183 §3 (evidence vs inference),
  ADR-0157 (frame-counted headless input), ADR-0185 (movie driver),
  ADR-0186 (CDL map), MEP-v1 §5, `docs/hd-pack-toolchain-comparison.md`
- Supersedes / amends: ADR-0189 §4 — narrows its scope to *emission*; the
  refusal to auto-emit `frameRange`, `tileAtPosition` and
  `memoryCheckConstant` stands unchanged

## Context

ADR-0189 §4 refuses to **emit** three condition types, each for a reason
that still holds: `frameRange` is tested against the emulator's global frame
counter and the recording knows only a period, not a phase; `tileAtPosition`
would spend the screen-anchor evidence twice; `memoryCheckConstant` cannot be
observed because the builder retains no memory stream, so any address would
be chosen, not seen.

The comparison table turns that into a row the upstream hand-author wins: all
13 types are available to a human writing `hires.txt`, and the most prolific
community author uses `memoryCheckConstant` 13 647 times, almost all as
`<background>` gates. Our guide forbids hand-editing `hires.txt`, and the
sheets that `mep_build.py` regenerates it from have no place for a condition
a human wrote. So the artist who knows the game's state byte has no legal way
to use it here.

The recorder does retain, per retained frame, the background grid and the
OAM stream with a `FrameNumber` (`HdPackBuilder::OnFrameEnd` dumps), for
every route a recording ran (`input=`, `movie=`, `state=`). That is enough to
**evaluate** a `frameRange`, `tileAtPosition`, `tileNearby` or
`spriteNearby` condition on every retained frame after the fact and report
where it holds, where it fails, and where it fires on a tile the author did
not mean. It is not enough for `memoryCheckConstant`.

Non-goals: no `ram_probe` or any tool that picks an address for the author
(that is the interpretation ADR-0183 §3 forbids); no auto-emission of the
three types; no condition editor GUI in this ADR.

## Decision

### 1. A sheet may carry a hand-authored condition

`mep_build.py` sheets gain an optional `conditions` block: a named condition
in the loader's own syntax (`[name]` definition and the `<condition>` line
fields), attached to sheet cells by name. `build` serializes it exactly as
the loader reads it and keeps the bare twin (ADR-0189 §3) for every
conditioned cell. The condition is marked `authored: true` in the sidecar;
nothing in the toolchain ever generates one of these.

### 2. Lint validates every authored condition against the recordings

`mep_lint.py` gains a `--routes <recording dir>...` mode. For each authored
condition it evaluates the condition on every retained frame of every
recording and reports, per condition: frames where it held, frames where it
failed, and frames where it held on a key the author did not condition. A
`frameRange` is additionally reported with the phase offset that would make
it hold on each route, so a value that works on one route and not another is
visible as a number, not a guess. This is a report, not a gate: an authored
condition is the author's decision, and lint says whether the recorded
evidence agrees.

### 3. `memoryCheckConstant` needs recorder capture first — open

Validating a memory condition requires the value of that address on each
retained frame. Two shapes, for the human to pick:

- (a) the recorder retains, per retained frame, the values of the addresses
  the *loaded* pack's conditions watch (`HdPackData::WatchedMemoryAddresses`,
  already computed by the loader) — cheap, but the pack must exist before the
  recording that validates it;
- (b) the recorder retains a fixed window (`$0000`–`$07FF`, the RAM the
  RAM-cheat rule of ADR-0184 already bounds) on every retained frame —
  route-independent, costs about 2 KB per retained frame in the dump.

Until one is accepted, lint reports a `memoryCheckConstant` as
`not evaluable: no memory stream in recording` and never as a pass.

## Consequences

- ADR-0189 §4 keeps its three refusals; this ADR gives the human author the
  expressive power those refusals withheld, with a measurement the upstream
  hand-author does not have. The comparison row "Conditions deliberately
  refused" is re-measured on that basis.
- Authored conditions are the second thing in a sheet that a generator did
  not derive (the first is the artist's pixels). `--verify` round trips must
  preserve them byte for byte, and every kit generator must pass them through
  untouched.
- Option (b) in §3 changes the recorder's dump format; `make capture-tool`
  and the viewer's wire format are affected, and ADR-0169 readers must skip
  the new block.
- Lint over routes is only as good as the route set: a condition that holds
  on every recorded frame can still fail on a frame nobody recorded. The
  report says how many frames it saw.
