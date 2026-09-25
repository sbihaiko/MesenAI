# ADR-0226: The pose-track linker tolerates one missing retained frame, so sprite flicker does not shatter a track

- Status: **accepted 2026-09-23** — implemented by PRD Part A slice
  **F12.19** (`docs/roadmap/PRD-mesence-enhancement-ecosystem.md`,
  Phase 12), which also regenerates the Contra kit from a fresh recording;
  **implemented** (stop conditions 1–3 met; the kit's cold-read row, condition 4, logged 2026-09-23 in `docs/validation/f1219-contra-kit-coldread-2026-09-23.md` with verdict "no" (#399–#401), stays open until a re-run cold read after those fixes — `docs/validation/f12.19-flicker-tolerant-tracks-2026-09-23.md`; (1)–(3) reproduced on the shared re-record, `docs/validation/f1218-f1219-contra-rerecord-2026-09-23.md`). User's pick,
  verbatim: *"Tolerar 1 frame"*. Implementation go-ahead quoted verbatim
  (2026-09-23): *"pode implementar as duas ADRs em paralelo"* — the
  same-turn rule of CLAUDE.md applies: the change ships with §4's unit tests
  and the go-ahead is quoted in the PR body.
  **Condition 4 met 2026-09-24** after #399, #400, #401 and #413 merged. The
  kit was regenerated from a fresh Contra re-record on `main` @ `89acdc10`,
  with 3 period-6 cycles (26/26/3) and every part's `--verify` 0 lost /
  0 added. The same briefing then went to a fresh evaluator, who returned
  "Run cycle identifiable and paintable unaided: yes" with 0 stops
  (`docs/validation/f1219-contra-kit-coldread-rerun-2026-09-24.md`). All
  four stop conditions are met, and F12.19 is delivered.
- Date: 2026-09-23
- Related: ADR-0179 (§1 track linking, §3 cycles and sequences, §6 tests),
  ADR-0181 (§3 `driver` is attributed over cycle windows — more windows per
  track once tracks survive), ADR-0170 (the retained stream and the
  `RepeatCount` collapse), ADR-0183 (§2 the Figures surface reads cycles as
  rows), ADR-0225 (the companion decision on pixel offsets; the same
  re-record serves both), `scripts/stages/contra/stage1-probe.txt` (the
  driver whose period the fallback promotes),
  `docs/validation/contra-pose-offsets-and-flicker-2026-09-23.md` §2–§3
- Supersedes / amends: amends ADR-0179 §1 — a cluster with no partner in the
  next retained frame does not end its track at once; it may be continued
  from the frame after. §2–§5 are untouched; §6 gains tests.

## Context

ADR-0179 §1 links a kept cluster in retained frame *i* to the nearest kept
cluster in frame *i+1*, within 16 px Manhattan, and "a cluster with no
partner ends its track". NES games hide a sprite on alternate frames to show
invincibility or to share OAM slots — a figure that is there on every drawn
frame but absent from every other *retained* frame. Under §1 each drawn
frame opens a one-frame track that nothing continues.

The Contra recording of 2026-09-23 shows it
(`docs/validation/contra-pose-offsets-and-flicker-2026-09-23.md` §2): of 69
tracks, **61 are single-frame tracks, all between retained frames 383 and
507, on every other frame** — the player running under respawn invincibility.
The poses on them are the run phases in order; the run is happening, the
linker just cannot see it as one track.

The damage is downstream, and it is what the artist sees. ADR-0179 §3 finds a
cycle only on **one track** holding **two full turns**; what no cycle covers
goes to the sequence search, which promotes every all-distinct window of 3–32
poses that recurs identically (≥ 2 times). A track that never holds two full
turns — shattered by flicker, or cut every 104 frames by the driver — has no
cycle, and the window that *does* recur identically is the **recording
driver's own input period**. The previous Contra kit is the proof: 0 cycles,
12 sequences, and `seq000` is 10 poses held `[8, 8, 8, 1, 30, 7, 8, 8, 8, 8]`
— the right-facing run, a 1-frame transition, the 30-frame standing hold and
the left-facing run, which is `104f R / 30f - / 104f L` from
`scripts/stages/contra/stage1-probe.txt` read back as an animation. The kit
laid it out as one 10-column row. ADR-0179's Consequences already recorded
the same failure on 2026-09-12 ("the player's run is *not* a cycle … four
3–4-pose sequences instead") and attributed it to the entry script tapping
R in bursts.

The current build on a clean stretch finds the real animation — `rec`: 3
period-6 cycles (repeats 6, 4, 3); `full`: repeats 26 and 24 with
`driver: "port1"`, plus a third at 3 without one — because the stretches before the death and after the
respawn each hold more than two turns. The flicker stretch contributed
nothing to `repeats` and 61 junk tracks. A recording that dies early, or a
game whose figure flickers longer, has no clean stretch and falls back to
the driver's period.

Non-goals: changing the driver scripts (the period is theirs to have — a
hold *is* how a probe idles); changing `BuildPoses`, identity or thresholds
(ADR-0170); changing capture or `kMaxSheetFrames`; anti-flicker at run time
(a Phase 12 non-goal); making the sequence search smarter about holds (see
Rejected options).

## Decision

### 1. One missing retained frame is bridged

`LinkPoseTracks` (`Core/NES/HdPacks/SpriteGrouping.cpp`) keeps a cluster
that found no partner in frame *i+1* as a **pending** end for exactly one
more frame. In frame *i+2* the linking runs in two passes, both nearest-first
within `kPoseTrackMaxMove` (16 px Manhattan, unchanged), each later cluster
used at most once:

1. clusters of frame *i+1* against clusters of frame *i+2* — today's rule;
2. the pending ends of frame *i* against the clusters of frame *i+2* that
   pass 1 left unlinked.

A pending end that pass 2 does not link ends its track there. A gap of two
or more retained frames still breaks the track; the tolerance is one frame,
by the user's decision, and it is a named constant
(`kPoseTrackMaxGap = 1`) so a later measurement can argue with a number.

**The bridged frame's cadence is kept.** The skipped frame's `RepeatCount`
is added to the run it interrupts: when the pose is the same on both sides
it goes to that run's `Held`; when the pose changes across the gap it goes
to the earlier run. `hold` (ADR-0179 §3) thus stays the game's cadence — a
phase drawn 4 times in 8 emulated frames is still held 8 — and a pose's
`frames`/`hold` on the entry (ADR-0170 §1, §2) count only frames it was
actually drawn in, as today; only the track's run length changes.

**Bounded in time, not only in frames.** The skipped retained frame must
carry `RepeatCount <= kPoseTrackGapMaxRepeats = 2`. Flicker produces
alternating distinct frames (`RepeatCount` 1 each); a figure that vanished
during a paused or static screen (one retained frame, many repeats) has
really gone, and bridging it would join two sightings seconds apart.

### 2. Nothing changes after the linker

`next[]`, `hold`, `cycles[]`, `sequences[]`, `variantOf`, `driver`
(ADR-0179 §2–§4, ADR-0181 §3) are computed from the tracks exactly as today.
The effect is upstream only: tracks that survive flicker are longer, so a
cycle can be found where none was, its `repeats` and its ADR-0181 windows
grow, and the uncovered remainder the sequence search sees shrinks. A
sequence equal to a driver period can still be written when a recording
holds no two turns of anything; that is the honest result for that
recording, and the driver scripts stay as they are.

### 3. The Contra kit is regenerated from a fresh recording

The slice re-records Contra stage 1 with the new binary (the same recording
serves ADR-0225's `px`/`py`) and regenerates the kit, so the delivered
figure rows are period-6 cycles, not the driver's period. The recorded pack
is the evidence and the kit a projection (ADR-0183 §1); no kit is edited by
hand.

### 4. Tests (extends ADR-0179 §6)

`core_unit_tests` with hand-built streams: a figure drawn on every other
retained frame is **one** track and its 6-phase run is a period-6 cycle; the
same figure absent for two consecutive retained frames is **two** tracks; a
skipped frame with `RepeatCount` 3 is not bridged; the skipped frame's
`RepeatCount` lands in `Held` and the cycle's `hold` reads 8 for a phase
drawn 4 times in 8 frames; two figures where one flickers do not swap tracks
across the gap (pass 1 links before pass 2). The measurement on the Contra
stream is the slice's stop condition, not a unit test.

## Rejected options

- **Split sequences at long holds** (treat a hold ≥ N as a boundary so the
  driver's period is never one window). Treats the symptom: the run is still
  not a cycle, only chopped into shorter sequences — exactly the four
  3–4-pose sequences of 2026-09-12 — and a real animation with a long hold
  (a charge, a wind-up) would be cut in two.
- **Regenerate only** (re-record until a clean stretch appears). The current
  build does find the cycle on `full`, but only because the death happened
  early and the respawn ran long. It leaves the fragility in place for every
  game that flickers longer than Contra, and it makes kit quality depend on
  when the player died.
- **Tolerate more than one frame.** Not measured; every extra frame widens
  the window in which two similar figures can be joined. One frame is what
  the observed flicker needs, and the constant is there to revisit.

## Consequences

- Greedy nearest-first linking now has a second chance to be wrong: a
  figure that really left and a similar one that appeared two frames later
  within 16 px are joined. Pass ordering (adjacent frames first) confines
  it to clusters nothing else claimed, and a spurious edge is still not a
  spurious cycle (`repeats >= 2` on one track, ADR-0179 Consequences).
- `kPoseTrackMaxMove` is judged over two frames for a bridged link, so a
  figure moving faster than 8 px per frame can flicker itself out of a
  track. Contra's player moves 1–2 px per frame; a faster game shows up as a
  shattered stretch in `tracks.txt`, the same signal this ADR read.
- Packs recorded before this ADR keep their sidecars; only a re-record
  changes anything. The Contra kit regeneration is the visible deliverable,
  and it also changes the figure rows an artist has already seen — the
  Phase 12 cold-read rule applies to the regenerated kit as to any surface
  change.
- The driver period showing up as a sequence remains possible on a bad
  recording, and the fallback is not made to hide it. A reader who sees a
  `sequences[]` entry with a 30-frame hold in the middle should read it as
  "the recording never held two turns", which `input.held` beside it makes
  checkable.
