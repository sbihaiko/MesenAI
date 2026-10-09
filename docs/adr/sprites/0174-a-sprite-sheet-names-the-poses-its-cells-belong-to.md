# ADR-0174: A `sprNNN` sheet names the poses its cells belong to

- Status: accepted (2026-09-12, at the user's request, fix committed the same turn); in
  `Core/NES/HdPacks/` (`SpriteGrouping`, `SheetRender`, `HdPackBuilder`) and
  `scripts/core_unit_tests.cpp`
- Date: 2026-09-12
- Related: ADR-0153 §2/§4, ADR-0164 §1/§5, ADR-0170 (`sheets/poses.json`), ADR-0171 §1
  (the pose is the unit, the `sprNNN` figure the fallback), ADR-0172 (optional-field
  precedent), issue #174
- Supersedes / amends: ADR-0153 §4 and ADR-0164 §1 — an object/sprite sheet sidecar gains
  an optional `poses[]`; ADR-0153 §2 is untouched.

## Context

The Phase 9 panel (2026-09-12) failed 5 of 27 subjects of the Contra pack against
`Contra80s` 1.1 — all human figures, a soldier cut at the waist. In
`runs/golden-20260912/contra/.../auto/textures/sheets/adjacency.json`, **35 sprite pairs
carry `count == coFrames == 1338`** (the legs never appeared without that torso), 20 across
`spr023` (legs) and `spr018` (torso): the legs sit under two torsos, so each edge scores
`count / appearances(legs) ≈ 0.5` and both drop under ADR-0153 §2's denominator — which is
what lets the legs be stored once.

ADR-0171 made the **pose** the unit, the `sprNNN` figure the fallback, so the whole-figure
surface exists in `sheets/poses.json` (ADR-0170). Missing is a link from a sheet to it:
`poses.json` keys tiles by **node** (`{"node": 11, "dx": 0, "dy": 0}`), and no `spr*.json`
carries a `pose` reference — on the Contra pack, not one contains the substring `pose`, so
an artist opening `sheets/` sees the cut soldiers and nothing points at the whole figure.
Keeping co-occurring nodes on one sheet instead is rejected: the criterion re-cuts every
sheet and invalidates `sprNNN` names already cited by `usrNNN` exports and composition
sessions.

Non-goals: a change to a sheet's contents, to cell grouping, or to ADR-0153 §2; a change
to `poses.json`; a migration (a pack recorded before this ADR carries no `poses[]`); an
answer to "which subject is this".

## Decision

### 1. A group sheet's sidecar carries the pose ids its cells belong to

An object/sprite sheet sidecar gains one optional array of `poseNNN` ids, written only
when non-empty:

```json
{
  "kind": "sprite",
  "columns": 2,
  "poses": ["pose001", "pose025", "pose055"],
  "cells": [ ... ]
}
```

An id is the `id` of an entry of `sheets/poses.json`, so the join is a lookup, not an
order agreement. A sidecar without the field means "a pack recorded before this ADR, or a
sheet whose cells belong to no pose"; readers must not require it, exactly as ADR-0172 §2
made `index` optional. Only `sprNNN` group sheets get one — `sprites.png` is the whole OAM
vocabulary and would cite every pose.

### 2. Order: most of this sheet's nodes covered first

`MesenSheets::PosesForCells` (`Core/NES/HdPacks/SpriteGrouping.{h,cpp}`, host-free per
ADR-0127) returns the position in `poses.json` of every pose holding at least one of the
sheet's cells' vocabulary nodes, ordered by (1) the number of the sheet's **distinct**
nodes it covers, descending; then (2) the pose's own rank — ADR-0170 §1's frames
descending, then tiles. Capped at `kSheetMaxPoseRefs` (32); on the Contra pack the busiest
sheet joins to 23 poses, and the cap exists for a 223-pose run like Mega Man 3.

### 3. The poses are segmented once

`HdPackBuilder::WriteSpriteSheets` runs `BuildPoses` over the sprite vocabulary before it
names the first `sprNNN`, and `WritePoseFile` serializes that same table. The per-frame
clustering is O(n²) over up to `kMaxSheetFrames` frames and must not run twice.

## Consequences

- A consumer can join the surfaces: read `poses[0]` of a `sprNNN`, look it up in
  `poses.json`, draw the whole figure.
- **The figure is still cut on the sheet** — navigable now, not absent; the pack no longer
  *hides* it.
- Only newly recorded packs carry the field; otherwise the ADR-0171 §1 fallback ladder
  stands.
- One short array per group sheet — on the Contra pack, 218 ids across 41 sheets.
- A menu-only recording writes no `poses.json`, so no `poses[]` anywhere: the same
  "nothing to say" both files express by absence.
- `mep_build.py`, `mep_lint.py`, `compose_engine` and `sheet_repaint` read `cells[]`
  unaffected; like ADR-0172's `index`, the key must be ignored, not rejected.
