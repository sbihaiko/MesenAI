# ADR-0177: A pose whose tiles split into two poses the file already carries is labelled a fusion of them, and the composition editor stops offering it as a figure

- Status: accepted (2026-09-12, after the measurement below; implemented the same day in
  `Core/NES/HdPacks/SpriteGrouping.cpp`, the `poses.json` serializer and
  `scripts/compose_engine.py`)
- Date: 2026-09-12
- Related: ADR-0170 §1 (the pose sidecar this amends), ADR-0171 (the pose as the unit of
  the sprite layer), ADR-0174 (`sprNNN` sidecars' `poses[]`), ADR-0173 (same defect and
  label-don't-delete rule), issue #179
- Supersedes / amends: ADR-0170 §1 — a `poses[]` entry gains an optional `fusionOf`
- Amended by: ADR-0228 (§1 a kept pose plus a remainder of `kPoseMinTiles` or more tiles
  is a fusion even when the remainder is not a kept pose; §3/§4 `fusionOf` then carries
  the one kept part)

## Context

`BuildPoses` segments each retained OAM frame into spatially connected clusters — a DSU
over the entries, joined when both axes are within `kPoseMaxGap` of each other's top-left —
and a cluster is a pose. That rule says *these tiles touched*; the file reads it as *these
tiles are one figure*. Any time one actor walks over or into another the two fuse, and the
pack gains an entry holding both.

On the Contra golden pack (85 entries), 22 pairs stand in strict containment (one entry's
placement set, normalised to its own top-left, is inside another's). Rendered, the larger
is the smaller plus a stranger: `pose028` is `pose000` (a running soldier, seen in 1411
frames) with `pose014` (a prone soldier, seen in 140 frames on its own) beside it. An
artist scanning the pose list meets the same figure several times, each copy with a
different bystander. That is issue #179, the mirror of the split-figure defect: there the
unit arrived too small, here too large.

**The evidence is structural, and needs no threshold.** If a cluster's tiles split, at some
translation, into exactly two clusters the recorder *also* saw alone — both already entries
past `kPoseMinFrames` — then the cluster is those two figures touching. Measured over the
golden kit, reading the raw sidecars:

| pack | entries | split into two known poses |
|---|---|---|
| Mega Man 3 | 223 | 44 |
| Zelda 1 | 84 | 39 |
| Contra | 85 | 21 |
| Excitebike | 77 | 12 |

The residue fits: in Contra the four containments that do *not* split this way are all "a
pose plus two loose tiles" — shapes below `kPoseMinTiles` that never stand alone and are
genuinely part of the figure. In Zelda the rule catches `pose020 = pose019 + pose019`. A
frequency ratio was measured first and rejected: the contained/container ratio runs 0.0 to
562 with no plateau, and it gets Excitebike backwards, where the *rarer* silhouette is
contained (`pose002`, 541 frames, contains `pose059`, 5 frames).

Non-goals: deciding what a figure *is*; splitting a fused entry into its parts, already in
the file under their own ids; re-segmenting the clusters; anything at run time.

## Decision

**The recorder classifies and labels; the consumer filters. Nothing is deleted.** The rule
ADR-0173 set for `floors[]`.

1. A kept pose `B` is a **fusion** when there exist kept poses `A` and `C` and a
   translation `t` such that `translate(A, t)` is a subset of `B`'s tiles and the
   remainder `B \ translate(A, t)`, re-normalised, is exactly `C`'s tile set. `A` and `C`
   may be the same pose. Both parts are kept poses by construction, so both cleared
   `kPoseMinFrames` on their own. No threshold: the classification is a property of the
   tile sets alone.

2. **The split is chosen deterministically.** Candidates `A` are tried in file order —
   frames descending, then by tile set — and the first known remainder wins. So the named
   first part is the most-seen part the split admits.

3. `PoseEntry` gains `FusionOf`: the two pose indexes, or empty when not a fusion.
   `Tiles`, `Frames`, `Width` and `Height` are written exactly as before.

4. The sidecar entry gains one optional field, written only for a fusion, as **ids** rather
   than indexes — ADR-0174's choice, since ids are what `poses.json` keys an entry by.

   ```json
   { "id": "pose028", "frames": 19, "size": [5, 4],
     "fusionOf": ["pose000", "pose014"],
     "tiles": [ … ] }
   ```

   A sidecar without the field means *not classified as a fusion*, never *proved not to be
   one*.

5. `compose_engine` is the consumer. `Pose` gains `fusion_of`, and the two places that
   *choose a figure* act on it: `pose_band_members` excludes a fused pose, so `pose_rank`
   and the suggestion list stop offering an entry that is two figures; `pose_for_anchor`
   prefers non-fused candidates, falling back to the full list only when every pose holding
   the anchor is labelled. `Poses.by_id` and `Poses.containing` keep returning them
   unfiltered.

6. `PosesForCells` (ADR-0174's `poses[]`) skips fusions — that list says which poses a
   sheet's cells belong to, and a fused entry is not a subject: citing it spends the
   `kSheetMaxPoseRefs` budget on noise.

## Consequences

- On the kit, 44 of 223 Mega Man 3 poses, 39 of 84 Zelda 1, 21 of 85 Contra and 12 of 77
  Excitebike are labelled fusions and drop from the editor's suggestion list. Zelda's 46 %
  is honest: that capture is full of identical enemies walking into each other.
- **Known false-positive class: a single figure whose two halves are also drawn
  separately.** A boss whose head and body appear apart elsewhere, a vehicle whose rider
  dismounts — the whole is labelled a fusion of its parts, reachable only through the
  vocabulary sheet or by id. The trade is deliberate: a fused entry heading the list costs
  the artist the belief that a pose is a figure. The parts stay individually offerable.
- Nothing about the file's identity, ordering or counts changes, so a pack rebuilt with
  `mep_build.py build` is byte-identical apart from the new field; the field is optional on
  read both ways.
- Packs recorded before this ADR keep offering fused poses until re-recorded (a bootstrap
  run).
- The classification is O(poses × poses). It is bounded by indexing candidates on their
  top-left tile's node; `kMaxPoses` (4096) is the ceiling and no real kit pack exceeds 223.
- A three-figure pile is labelled a fusion of a figure and a fusion; the chain is left as
  it is — each link is true.
