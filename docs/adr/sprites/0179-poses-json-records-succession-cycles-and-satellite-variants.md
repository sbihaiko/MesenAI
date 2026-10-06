# ADR-0179: `poses.json` records pose succession, the cycles and sequences found on it, and labels a figure-plus-projectile as a variant of the figure

- Status: accepted (2026-09-12, by the user, after the spike below) —
  implemented the same day as Phase 9 slice F9.20 in
  `docs/roadmap/PRD-mesence-enhancement-ecosystem.md` (Part A §3).
  **§1 amended 2026-09-23 by ADR-0226** (implemented as PRD slice F12.19,
  merged 2026-09-23 in PR #394): a cluster
  with no partner in the next retained frame may be continued from the
  frame after — one missing frame is bridged, so flicker does not end a
  track.
- Date: 2026-09-12
- Related: ADR-0170 (the pose sidecar this amends; its non-goals name
  "ordering poses into an animation" as a separate question), ADR-0171,
  ADR-0177 (`fusionOf`), ADR-0173 / ADR-0176 (label-don't-delete and
  "statistic vs. belonging"), PRD Part A §4 Phase 9 validation test 2,
  `runs/golden-20260912/spike-pose-succession.md`
- Supersedes / amends: ADR-0170 §1 — a `poses[]` entry gains optional
  `next[]` and `variantOf`; the file gains optional top-level `cycles[]` and
  `sequences[]`. Nothing about how a pose is found, identified, counted or
  ordered changes.

## Context

After ADR-0177 our Contra sidecar carries 64 poses as an unordered list
sorted by frame count, so the player's run scatters with nothing naming the
loop or its order (`BillRizer.png` is a phase grid). ADR-0170 left this out ("ordering poses into an animation" were
separate questions). The 2026-09-12 spike
(`runs/golden-20260912/spike-pose-succession.md`) found the order derivable
from the retained data. `_oamFrames` is ordered and
`RepeatCount`-collapsed, so linking each kept cluster in frame *i* to the
nearest in frame *i+1* (top-left within 16 px Manhattan) yields tracks — on
Contra the soldier's run two closed 3-cycles with weakest edge >= 14
(`pose000 -> pose011 -> pose010`, `pose000 -> pose016 -> pose008`), the
somersault a 4-cycle (`pose006 -> pose015 -> pose017 -> pose013`); linear
animations recur too (the death `068 033 042 046 050 049 048 034 047`,
twice). A first-order graph is not enough: the player's run is a 6-phase
cycle held 8 frames per phase — `020 003 022 023 003 024` — where `pose003`
is *both* phase 2 and phase 5, reading as a 3-cycle on `next[]` but period 6
on the track, so cycles must be found on a track's sequence. The flicker
hypothesis was wrong ("legs" are explosions), so there is no `partialOf`;
the residue is 9 of 64 kept poses being another kept pose plus 1–3 tiles
(`pose025 = pose001 + shot`).

Non-goals: naming an animation ("run", "jump", "death"); identifying the
same character across cycles (a subject — ADR-0180); any change to
ADR-0170's clustering or thresholds; any change to capture, to
`kMaxSheetFrames` or to `hires.txt`; anything at run time.

## Decision

### 1. Tracks are linked at save time, from the retained stream

For every pair of consecutive retained frames, each kept cluster in the
earlier frame is linked to the kept cluster in the later frame whose top-left
is nearest, within `kPoseTrackMaxMove` = 16 px Manhattan, each later cluster
used at most once, nearest first; a cluster with no partner ends its track.
This is a free function next to `BuildPoses` in
`Core/NES/HdPacks/SpriteGrouping.{h,cpp}`, host-free (ADR-0127), returning
tracks as sequences of `(pose index, held frames)` where `held` sums
`RepeatCount` over the frames the pose was held. A pose below the ADR-0170 §2
thresholds is invisible to the linker, never a track member.

### 2. `next[]` — first-order succession, per pose

Every kept entry gains an optional `next[]`: the poses this one was linked
to, with the count of links, most-linked first, self-links excluded and
reported as `hold`. Written only when non-empty. `next[]` is the raw
evidence a reader needs to check §3 (see the fork at `pose003`).

### 3. `cycles[]` and `sequences[]` — found on tracks, by repetition

Top-level, optional, written only when non-empty:

- A **cycle** is a run of poses of period *p* >= 2 that repeats at least
  `kPoseCycleMinRepeats` = 2 consecutive times on one track, with at least 2
  distinct poses. Period detection is on the track's pose sequence (holds
  ignored for matching, reported after); the shortest repeating period wins,
  and the phase is rotated so the most-seen pose is first. Equal cycles from
  different tracks merge, summing `repeats`.
- A **sequence** is a run of >= `kPoseSequenceMinLength` = 3 distinct poses,
  not part of any cycle, that occurs identically on at least 2 tracks (or
  twice on one). It is the non-looping animation: a death, a spawn, an
  explosion.

```json
"cycles": [
  { "id": "cycle000", "period": 6, "repeats": 11,
    "poses": ["pose003", "pose022", "pose023", "pose003", "pose024", "pose020"],
    "hold":  [8, 8, 8, 8, 8, 8] },
  { "id": "cycle001", "period": 3, "repeats": 20,
    "poses": ["pose000", "pose011", "pose010"], "hold": [8, 8, 8] }
],
"sequences": [
  { "id": "seq000", "repeats": 2,
    "poses": ["pose068", "pose033", "pose042", "pose046", "pose050", "pose049", "pose048", "pose034", "pose047"],
    "hold":  [2, 4, 4, 4, 4, 4, 4, 2, 2] }
]
```

A pose may appear in several cycles, and in a cycle more than once. `hold`
is the median held frames per phase over the repeats, so a reader can play
the loop at the game's own cadence without `frameRange`. Ordering: cycles by `repeats` descending then `poses`;
sequences the same; the ids are positions in that order, as ADR-0170 §1 does
for poses.

### 4. `variantOf` — a kept pose that is another kept pose plus a satellite

A kept, non-fusion pose `V` is a **variant** of kept pose `P` when some
translation of `P`'s tiles is a strict subset of `V`'s and the remainder has
fewer than `kPoseMinTiles` tiles (so the remainder could never be a pose —
otherwise ADR-0177's fusion rule applies and wins). Candidates `P` are tried
in file order and the first match is named. Written as an id, like
`fusionOf` (an entry keeps its `size` and `tiles`).

Containment only, no threshold, no temporal evidence. A consumer that chooses
a figure (`compose_engine.pose_band_members`, `pose_for_anchor`) ranks the
base pose above its variants, and a variant stays reachable by id — the
ADR-0173/0177 rule. `PosesForCells` keeps citing variants: the projectile's
tiles belong to the sheet.

### 5. The consumer lays poses out by cycle

The composition editor's pose picker and any future subject sheet order the
sprite side as the artist does: one row per cycle or sequence, columns in
phase order, the unordered remainder after. This is the only consumer change
this ADR asks for; the layout itself is F9.18's business.

### 6. Tests

`core_unit_tests` (ADR-0126) with hand-built streams: a track follows a
figure moving 3 px per frame and breaks at 17 px; a 6-phase cycle with a
repeated silhouette is reported with period 6, not 3; two identical
non-looping runs become one sequence with `repeats: 2`; a pose plus a
1-tile satellite is a variant, a pose plus a 4-tile remainder that is itself
a pose is a fusion and not a variant; a pack whose stream has no repetition
writes no `cycles`/`sequences` key at all.

## Consequences

- **A cycle is what the recording showed, not what the game has.** The
  player's aim-up/aim-down runs are absent from the Contra golden sidecar
  (the entry script never pressed those combinations); no sidecar work
  invents them (per-stage recording roadmap, PRD Phase 9, F9.22). A cycle
  observed once (`repeats: 1`) is not written. The recorder reports
  found/kept counts on the save line, as ADR-0170 §2 does for poses.
- **The 4096-frame cap now costs order, not just coverage.** On the 120 s
  Contra run the stream held 4151 of 7213 frames, so the last 51 s produced
  no poses and no tracks. `kMaxSheetFrames` is left alone (~11.5 MB bound,
  ADR-0170's reason); the answer is the recording shape — several ~60 s runs
  from per-stage save states.
- Track linking is greedy nearest-first, so two identical figures crossing
  paths can swap tracks for a frame — a spurious edge, never a spurious
  cycle (a cycle needs `repeats >= 2` of the same period on one track).
  Fusions (ADR-0177) take part in tracks, so a cycle can pass through a
  fusion.
- `hold` is a median in retained frames (after `RepeatCount` collapse) — the
  game's cadence only if the emulator ran at full speed; a paused screen
  inflates one phase's hold, not the cycle.
- **Delivery (Contra golden run, 2026-09-12):** 5 cycles, 8 sequences, 690
  tracks. The soldier's two 3-cycles merge into one period-6 cycle
  (`000 011 010 000 016 008`); the player's run is *not* a cycle (no track
  holds it two full turns) and shows as four 3–4-pose sequences; a base pose
  alternating with its variant (`pose001` ↔ `pose025`) is a period-2 cycle by
  §3's letter.
