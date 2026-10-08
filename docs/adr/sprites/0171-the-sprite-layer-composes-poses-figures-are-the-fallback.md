# ADR-0171: The sprite layer's unit is the pose; a sheet sidecar labels `screenFixed` and states `emptySlots`

- Status: accepted (2026-09-11, by the user) — engine in `scripts/compose_engine.py`
  (`pose_for_anchor`/`pose_band_members`), `Core/NES/HdPacks/SpriteGrouping.{h,cpp}`
  and `SheetGrouping.{h,cpp}`; tests in `scripts/core_unit_tests.cpp`
- Date: 2026-09-11
- Related: ADR-0153, ADR-0156, ADR-0154 §5, ADR-0164, ADR-0165, ADR-0166, ADR-0168,
  ADR-0170, ADR-0172, ADR-0228
- Consolidates: ADR-0173, ADR-0175
- Supersedes: ADR-0168 — keeps its §1 principle (a node is not a unit an artist can
  judge) and demotes its §2 `evidence[]` walk to the fallback ladder's rung 2.

## Decision

### 1. The unit of the sprite layer is the pose

A pose is one entry of `sheets/poses.json`: a set of `(node, dx, dy)` in 8 px cells — e.g.
`{"node": 11, "dx": 0, "dy": 0}` — normalized to its own top-left, with the frame count
it was seen in. The fallback ladder, in order: (a) the pose containing the gesture's
anchor node, from `poses.json`; (b) failing that — no sidecar, or an anchor in no pose —
the `sprNNN` figure laid out by the ADR-0168 §2 walk; (c) failing that, the bare node, a
degenerate pose of one. Background and object layers are untouched: a background node is
a self-contained 16x16 metatile.

### 2. Layout comes from the pose, never from a walk when a pose exists

ADR-0170 §4 states the rule; `compose_engine.Pack` (`figure_layout_detail`) implements
the boundary and reports which source it used. A pose's cells are drawn at its own
`dx`/`dy`; its bounding box is `size`. A pose may hold nodes the anchor's `sprNNN` sheet
does not list.

### 3. A pose's band is its bottom row

Its band membership is the set of bands of its bottom-row nodes (the members at
`max(dy)`), so the part standing on the floor decides — a projectile at head height does
not join a ground band because its owner's feet do (ADR-0164 §5).

### 4. Ranking: summed co-presence, damped by size

`sprite_rank` scores a pose

```
score(pose) = ( sum over distinct member nodes of coFrames(node, locked) )
              / sqrt(number of distinct member nodes)
```

sorted by `score` descending, then the pose's `frames` descending, then by `id`. A pose
with no co-presence against the locked set is not a candidate, exactly as a node with
`co == 0` is not one today. The square root damps the size term without reversing it — a
bare sum favors the 11-tile pose, a mean the 4-tile fragment. `coFrames` is recorded
evidence, not a derived quantity.

### 5. Export is unchanged

A composed sprite band still exports as a `usrNNN` sidecar of kind `sprite` per
ADR-0164 §3 — `cells[]` at composed positions, `"composed": true`,
`seed`/`locked`/`band`. `seed`/`locked` keep naming **nodes**: a pose is resolved from
its anchor by `pose_of(anchor)` on reopen. `mep_build.py` needs no change; `pose_cells` is
what the export preview draws; no new field.

### 6. A sprite that never moved is labeled screen-fixed; the recorder labels, the consumer filters, nothing is deleted

`SpriteAdjacencyStats` gains, per node, `Positions` (distinct `(X, Y)` it was ever drawn
at) and `NodeFrames` (retained frames it appeared in at all, once per frame however many
instances). A node is screen-fixed when

```
NodeFrames >= kScreenFixedMinFrames (64)
and Positions * kScreenFixedRevisits (32) <= NodeFrames
```

`adjacency.json`'s `sprites.nodes[]` gains `positions`, `frames` and `screenFixed`;
`floors[]` is written unchanged even for a screen-fixed node — the label says where the
shape is *painted*. `compose_engine.Adjacency` acts on it: `band_members(band)` excludes
screen-fixed nodes and `floors()` drops bands only the HUD ever reached; `sprite_rank`,
`pose_band_members` and `pose_rank` route through `band_members`, so the filter applies
once. A missing `screenFixed` reads as *not classified*, never false-and-therefore-fine.

Implemented in `Core/NES/HdPacks/SpriteGrouping.cpp`, the `adjacency.json` serializer and
`scripts/compose_engine.py` (issue #167); the two thresholds sit beside the other
adjacency constants in `TileSheetTypes.h` and moving them is a recording change, not a
format change. The background statistics are out of scope — they already have a HUD split
of their own (`hud.json`/`font.json`, ADR-0153 §3). Measured over a 120 s scripted-play
capture (4096 retained OAM frames, 288 sprite nodes): the median node scored **4.3** frames
per position against 139–609 for the three HUD bars, and 30 of the 288 nodes classified as
screen-fixed, shedding 1–11 false members from 19 of the bands (the crowded ones hold 60–80
members each). **Known false-positive class**: an actor that genuinely never moved during
the capture — a turret, a boss in its idle loop — is classified as furniture and stops
being offered for its band. The trade is deliberate, because a false member heads a whole
band's suggestion list while a missing stationary enemy costs one suggestion the artist can
still reach through the vocabulary sheet. Still open, and not distinguished by any rule
here: node `#71` of the same capture (130 appearances, bands 216–240) looks like a genuine
spark effect rather than this class.

### 7. A group sheet states its blank slots; it never fills them with a repeat

`MesenSheets::EmptyGroupSlots` (`Core/NES/HdPacks/SheetGrouping.{h,cpp}`, host-free per
ADR-0127) returns the slots of a group's `Columns x Rows` grid that no member occupies,
row-major, written to `emptySlots[]` if any:

```json
{ "kind": "sprite", "columns": 2, "emptySlots": [{ "col": 0, "row": 0 }], "cells": [ ... ] }
```

`col`/`row` are in cells, the units the group lays its members out in, so a consumer
reaches the sheet pixels through the sidecar's own `cell` size and `gutter`; absent or
present and empty both mean "no blank worth stating". `EmptyGroupSlots` reads the
`SheetGroup`,
not the rendered cells — a `metatile` index outside the vocabulary would leave a blank the
list does not name. Filling the slot with the cell that would repeat there is
**rejected**: a sheet crop is keyed back to `hires.txt` by its tile key (ADR-0153 §4), one
key carries one piece of art, and §5's substitution model reads a hole as "already painted
next door", not "unpainted". An audit said 37 of 258 nodes unreachable; recomputed, the 14
of 258 are **screen-resident** (ADR-0156) — a `<background>` covers the cell, so a cell
spent on it in `metatiles.png` would be dead paint. They are painted through ADR-0166's
`screens[]` (the `screenNNN` frame plus the node's 8 px placement, resolving to
`textures/backgrounds/screenNNN.orig.png`).

Implemented in `Core/NES/HdPacks/` (`SheetGrouping`, `SheetRender`, `HdPackBuilder`), with
its unit test in `scripts/core_unit_tests.cpp` (issue #175). The field is amended onto
ADR-0153 §4, and a reader that validates the schema must ignore the new key rather than
reject it — like ADR-0172's `index` and ADR-0174's `poses`. **The `spr016` cause stays
open**: `spr016.orig.png` is a 6x2 grid carrying 6 cells and reads `GA M` / `OV R`, because
the letter `E` occurs twice in one frame ("GAME"/"OVER"), so its own `appearances`
denominator is double every partner's count and every edge from it is dropped — a tile that
repeats inside a frame cannot currently join a group. This decision makes the hole legible;
it does not put the `E` back, and the fix is a change to the grouping criterion itself, the
option ADR-0174 rejected for the same reason.

## Context

The Phase 9 editor needed a unit an artist can judge. ADR-0168 chose the `sprNNN` group,
but spike S10.a measured the `evidence[]` walk recovering **6.7 %** (Mega Man 3) and
**10.5 %** (Contra) of a character's poses: ADR-0153 §2's 0.80 mutual-predictability test
drops every edge from a tile that changes offset between poses, so a `sprNNN` group is a
sub-part (mean 5.1 tiles against a mean pose of 10.8). ADR-0170 made the recorder write
`textures/sheets/poses.json`, one entry per distinct silhouette, which
gives the complete silhouette (25 of 25 of a character's poses on 2026-09-11), so the pose
became the unit and the walk the fallback. ADR-0168's own title — that "the group sheet's
`evidence[]` offsets reassemble the character" — is what the measurement falsified, so this
is a supersede, not an amendment.

Two surfaces then each showed a defect of the same shape — the recorder saw something the
file did not state:

- **HUD in the bands.** On Mega Man 3 the weapon-energy node `#0` reached **7** distinct
  `floors[]` bands and headed each. Over a 120 s capture (4096 retained OAM frames, 288
  nodes) the HUD's frames/position ratio ran 139.6–609.0 against a median of **4.3**.
- **Holes.** `spr016.orig.png` is a `6x2` grid carrying 6 `cells` and reads `GA M` / `OV R`;
  `obj000` fills 12 of 15 slots, `obj001` 9 of 12, `obj009`/`obj010`/`obj011` 3 of 4 — a
  bounding-box layout, so blanks with no stated verdict.

Both defects live on the sheet surfaces `adjacency.json` and `sheets/adjacency.json`. The
layout is `SheetGrouping::PlaceMembers` (a `SheetGroup`'s bounding-box BFS), so
`RenderGroup` can leave `SheetGroup` slots nobody occupies. Reachability was recomputed
over `metatiles.json`, `misc.json`, every `obj*.json` and both maps' `placements`
(following `aliases`). `floors[]` uses the bottom edge `Y + 8`; the ratio was read from
`appearances / frames`. Raw sidecars live under `runs/golden-20260912/` (e.g.
`runs/golden-20260912/contra/.../auto/textures/sheets/adjacency.json`), the code under
`Core/NES/HdPacks/` — `SpriteGrouping`/`SheetGrouping`/`SheetRender`.

Non-goals: detecting "this is the HUD" semantically; answering "which subject is this"
(grouping poses stays with ADR-0170); a hole reads "already painted in the pose to its
left", not "unpainted"; an absent field is the files' "nothing to say".

The through-line, set by ADR-0173: **the recorder classifies and labels; the consumer
filters; nothing is deleted.** A pack recorded before a field exists reads it as *not
classified*, untouched until re-recorded.

## Consequences

- **A pose can be two characters.** ADR-0170's connectivity is the only OAM signal, so an
  enemy touching Bill Rizer is one cluster — accepted, not mitigated.
- **Not every pose is addressable.** `locked` names a node, so a pose is reached through
  `pose_of(anchor)`; on the 2026-09-11 Mega Man 3 pack (223 poses) **62 silhouettes** are
  addressable, covering **11 270** of 14 351 pose frames (78.5 %). The ceiling is
  structural; §5 declines a field naming the pose, and anchoring on the member that
  resolves back to its own pose moves 62 to 66. Strict set identity inflates the candidate
  list (`budget` caps it): 223 poses on a 300 s run.
- **A composed sheet shows later poses with holes** (a node placed once is not emitted
  twice; a shared tile is painted once); the editor marks them.
- Variable-sized cells (a pose is roughly 16x16 to 48x48) rewrite the editor's fixed-cell
  geometry and hit-testing; the host-free `compose_editor_layout.py`
  (`cell_origin`/`index_at` are exact inverses) keeps it affordable.
- Two layout paths (pose and walk) live forever — a pack recorded before ADR-0170 keeps
  the walk, so it is not dead code and ADR-0168 stays readable.
- **`screenFixed` false positives:** a genuinely stationary actor (a turret, a boss idle)
  drops off its band; the trade is deliberate — furniture is always the most-seen shape and
  heads the list. On the measured capture 30 of 288 sprite nodes are screen-fixed and 19 of
  the 30 bands shed 1–11 false members apiece. The thresholds are a judgment (median 4.3
  against 139–609), beside the adjacency constants in `TileSheetTypes.h`; moving them is a
  recording change, not a format change. `#71` of that capture (a spark effect) is not
  distinguished.
- **The `spr016` cause stays open:** a tile that repeats inside one frame is excluded from
  every group (the `E` of `GA M`/`OV R` occurs twice per frame), so the sheet is short a
  subject, not only a slot.

## Record

- 2026-09-11 — accepted by the user; shipped as PRD Part A slice **F9.18** in `5eec2061`.
- 2026-09-12 — ADR-0173 and ADR-0175 accepted by the user and implemented the same day.
- 2026-09-15 — ADR-0170's pose-identity revisit closed (Phase 11 C.8), no loosening.
- 2026-09-23 — ADR-0228 amends the fusion definition (ADR-0177 §1).
- 2026-10-05 — consolidated: absorbs ADR-0173 and ADR-0175.
