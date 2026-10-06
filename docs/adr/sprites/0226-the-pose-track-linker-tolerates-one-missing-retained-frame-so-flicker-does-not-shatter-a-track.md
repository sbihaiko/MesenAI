# ADR-0226: The pose-track linker tolerates one missing retained frame, so sprite flicker does not shatter a track

- Status: accepted (2026-09-23) — PRD Part A slice **F12.19** (`docs/roadmap/PRD-mesence-enhancement-ecosystem.md`, Phase 12).
- Date: 2026-09-23
- Related: ADR-0179 (§1 track linking, §3 cycles and sequences, §6 tests), ADR-0181 (§3 `driver`), ADR-0170 (the retained stream and the `RepeatCount` collapse), ADR-0183 (§2 the Figures surface), ADR-0225 (the companion pixel-offset decision; the same re-record serves both), `scripts/stages/contra/stage1-probe.txt`, `docs/validation/measurements/contra-pose-offsets-and-flicker-2026-09-23.md` §2–§3
- Amends ADR-0179 §1: a cluster with no partner in the next retained frame may be continued from the frame after. §2–§5 are untouched; §6 gains tests.

## Context

ADR-0179 §1 links a kept cluster in retained frame *i* to the nearest kept cluster in frame *i+1* within 16 px Manhattan, and "a cluster with no partner ends its track". NES games hide a sprite on alternate frames to show invincibility or share OAM slots — a figure there on every drawn frame but absent from every other retained frame; under §1 each drawn frame opens a one-frame track nothing continues.

The Contra recording of 2026-09-23 shows it (`docs/validation/measurements/contra-pose-offsets-and-flicker-2026-09-23.md` §2): of 69 tracks, 61 are single-frame tracks between retained frames 383 and 507, on every other frame — the player running under respawn invincibility, cut every 104 frames by the driver. ADR-0179 §3 finds a cycle only on one track holding two full turns; a shattered track never does, so the window the sequence search promotes is the recording driver's own input period. The previous Contra kit had 0 cycles, 12 sequences, and `seq000` is 10 poses held `[8, 8, 8, 1, 30, 7, 8, 8, 8, 8]` — `104f R / 30f - / 104f L` from `scripts/stages/contra/stage1-probe.txt` read back as an animation. A clean stretch finds the real animation: `rec` = 3 period-6 cycles (repeats 6, 4, 3), `full` = repeats 26 and 24 with `driver: "port1"`.

Non-goals: changing the driver scripts; changing `BuildPoses`, identity or thresholds (ADR-0170); capture or `kMaxSheetFrames`; anti-flicker at run time; a smarter sequence search.

## Decision

### 1. One missing retained frame is bridged

`LinkPoseTracks` (`Core/NES/HdPacks/SpriteGrouping.cpp`) keeps a cluster with no partner in frame *i+1* as a **pending** end for exactly one more frame. In frame *i+2* linking runs two nearest-first passes within `kPoseTrackMaxMove` (16 px Manhattan, unchanged), each later cluster used once: (a) frame *i+1* against *i+2* — today's rule; (b) the pending ends of *i* against the *i+2* clusters (a) left unlinked. An unlinked pending end ends its track; two or more missing frames still break it (`kPoseTrackMaxGap = 1`).

**The bridged frame's cadence is kept.** The skipped frame's `RepeatCount` is added to the run it interrupts — `Held` when the pose is unchanged across the gap, the earlier run otherwise — so `hold` (ADR-0179 §3) stays the cadence (a phase drawn 4 times in 8 frames is still held 8) and a pose's `frames`/`hold` (ADR-0170 §1) count only drawn frames; only the track's run length changes.

**Bounded in time.** The skipped frame must carry `RepeatCount <= kPoseTrackGapMaxRepeats = 2`. Flicker alternates `RepeatCount` 1 frames; a figure that vanished during a paused screen (one retained frame, many repeats) has really gone.

### 2. Nothing changes after the linker

`next[]`, `hold`, `cycles[]`, `sequences[]`, `variantOf`, `driver` (ADR-0179 §2–§4, ADR-0181 §3) are computed as today. The effect is upstream only: longer tracks can yield a cycle where none was, grow `repeats` and the ADR-0181 windows, and shrink the sequence remainder. A driver-period sequence is still possible when a recording holds no two turns — the honest result (`"the recording never held two turns"`, via `input.held`).

### 3. The Contra kit is regenerated from a fresh recording

The slice re-records Contra stage 1 with the new binary (the same recording serves ADR-0225's `px`/`py`) and regenerates the kit into period-6 rows. The pack is evidence, the kit a projection (ADR-0183 §1), never hand-edited.

### 4. Tests (extends ADR-0179 §6)

`core_unit_tests` with hand-built streams: a figure drawn on every other retained frame is **one** track whose 6-phase run is a period-6 cycle; absent two consecutive retained frames is **two** tracks; a skipped frame with `RepeatCount` 3 is not bridged; the skipped `RepeatCount` lands in `Held`; two figures where one flickers do not swap tracks across the gap (pass 1 before 2).

## Rejected options

- **Split sequences at long holds** — the run is still not a cycle, only chopped into 2026-09-12's four 3–4-pose sequences, and a real long hold would be cut in two.
- **Regenerate only** — leaves the fragility for every game that flickers longer than Contra.
- **Tolerate more than one frame** — not measured; one is what the observed flicker needs.

## Consequences

- Greedy linking now has a second chance to err: a figure that left and a similar one two frames later within 16 px are joined. Pass order confines it to clusters nothing else claimed, and a spurious edge is not a spurious cycle (`repeats >= 2`).
- `kPoseTrackMaxMove` is judged over two frames for a bridged link, so a figure faster than 8 px per frame can flicker out of a track; Contra's player moves 1–2 px per frame, a faster game shows as a shattered stretch in `tracks.txt`.
- Packs before this ADR keep their sidecars; the regenerated kit changes rows an artist has seen (the Phase 12 cold-read rule applies).

## Record

- 2026-09-23 — accepted. Pick, verbatim: *"Tolerar 1 frame"*. Go-ahead, verbatim: *"pode implementar as duas ADRs em paralelo"*. Shipped as **F12.19**.
- 2026-09-24 — all four stop conditions met after #399, #400, #401 and #413; the kit was regenerated from a fresh Contra re-record on `main` @ `89acdc10` (3 period-6 cycles, every part's `--verify` 0 lost / 0 added); a fresh evaluator returned *"Run cycle identifiable and paintable unaided: yes"*. Evidence: `docs/validation/slices/f12.19-flicker-tolerant-tracks-2026-09-23.md`, `docs/validation/slices/f1218-f1219-contra-rerecord-2026-09-23.md`, `docs/validation/slices/f1219-contra-kit-coldread-2026-09-23.md`, `docs/validation/slices/f1219-contra-kit-coldread-rerun-2026-09-24.md`, `docs/validation/measurements/contra-pose-offsets-and-flicker-2026-09-23.md`.
