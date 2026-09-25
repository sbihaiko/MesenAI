# ADR-0228: A kept pose plus a pose-sized remainder is a fusion, even when the remainder never stood alone

- Status: **accepted 2026-09-23 and implemented the same day** (issue #401,
  `Core/NES/HdPacks/SpriteGrouping.cpp` `LabelPoseFusions`, the kit's
  `dropped[]` wording in `scripts/artist_kit.py`). The user picked this option
  from the three the issue #401 measurement laid out, verbatim: *"Tratar como
  fundida (Recommended)"*. Implementation go-ahead, verbatim: *"pode corrigir o
  #400 e o #401 em paralelo também"*. CLAUDE.md's same-turn rule applies: the
  change ships with the unit tests of §4, and both quotes also go in the PR
  body. Evidence:
  `docs/validation/issue-401-rest-grid-composites-2026-09-23.md`.
  **Amended 2026-09-25 by issue #504:** §1's containment test gained the
  screen-edge gate of §6, implemented the same day in
  `Core/NES/HdPacks/SpriteGrouping.cpp` (`LabelPoseFusions`,
  `PartOnlyEverSeenClippedByTheScreenEdge`) with the `BlocoP` cases §6 names. The
  task's instruction, verbatim: *"A narrow refinement is fine: then also amend
  ADR-0228 in place per .claude/skills/adr/SKILL.md (dated note on Status line +
  the refined rule + evidence), without changing its Decision otherwise."* This
  refines §1 rather than reversing it: a part the recorder saw clear of all four
  screen edges even once still labels the entry.
- Date: 2026-09-23
- Related: ADR-0170 §1 (the pose sidecar and `kPoseMinTiles`), ADR-0173 (the
  label-don't-delete rule), ADR-0174 (`poses[]` on a `sprNNN` sidecar),
  ADR-0179 §4 (`variantOf`), ADR-0183 §2 (the Figures surface excludes
  fusions), ADR-0209 Q4 (the `unsorted` sheet), ADR-0225 (the `px`/`py` layout
  the evidence was read from), issue #401, issue #504 (§6, the screen-edge gate)
- Supersedes / amends: amends ADR-0177 §1 (a fusion no longer requires the
  remainder to be a kept pose) and §3/§4 (`FusionOf` / `fusionOf` may carry one
  part instead of two). Leaves ADR-0179 §4 unchanged; this ADR takes the case
  §4 hands back to ADR-0177 ("otherwise ADR-0177's fusion rule applies and
  wins") and makes sure ADR-0177 actually takes it.

## Context

ADR-0177 labels a pose a fusion when its tiles split into two poses the
recorder also kept. ADR-0179 §4 labels a pose a variant when it holds a kept
pose plus fewer than `kPoseMinTiles` other tiles. Between the two sits a case
neither covers: a pose that holds a kept pose plus **`kPoseMinTiles` or more**
other tiles that were never kept as a pose of their own. That entry is neither
a fusion nor a variant, so every consumer treats it as a figure.

The Contra stage-1 re-record of 2026-09-23 (the F12.18/F12.19 shared
recording, 27 poses) has exactly four such entries, and a cold reader of the
kit's rest sheet (`usr003`) saw them as Bill fused with a soldier. Measured on
`poses.json` and `adjacency.json`:

| pose | frames | holds (kept pose) | remainder |
|---|---|---|---|
| `pose018` | 7 | `pose016`, a running-soldier phase (`FF36160F` torso, `FF20000F` legs) | 8 tiles: a Bill death-tumble frame (nodes 94–100) plus the blank tile |
| `pose020` | 5 | `pose012`, a running-soldier phase | 8 tiles: another tumble frame (101–107) plus the blank tile |
| `pose025` | 3 | `pose017`, a running-soldier phase | 8 tiles: a tumble frame (115–121) plus the blank tile |
| `pose026` | 3 | `pose017` | the same 8 tiles as `pose025`, at another offset |

The two halves are two actors on every piece of evidence the file carries.
The soldier half steps through cycle002's own phases (017 → 017 → 016 along
`pose025 → pose026 → pose018`) while the Bill half steps through tumble
frames. The input never pressed A, so the ball is the death tumble and not a
jump. The tumble frames that follow once Bill clears the soldier
(`pose019`, `pose023`, `pose024`) are kept on their own. The first frames
never are, because Bill dies by touching the soldier: the frames that would
make the remainder a kept pose do not exist in this recording, or in any
other. Palette and OAM order cannot separate the two figures (both wear
`FF36160F`, and the OAM rank interleaves them in 8x16 pairs). So the only
structural fact left is the one this ADR uses: a kept figure is inside, and
what is left over is big enough to be a figure.

Scanning every non-fused pose of that recording, at both the tile-unit
(`dx`/`dy`) and the pixel (`px`/`py`) layout, finds exactly these four
entries in the gap.

Non-goals: splitting the entry, or minting a pose for the remainder (that was
the other option, and it would invent a figure the recorder never saw alone);
changing how clusters are formed, identified, ranked or counted; any
threshold beyond the existing `kPoseMinTiles`; anything at run time.

## Decision

1. A kept pose `B` is a **fusion** when some kept pose `A` fits inside it at a
   translation (the same containment test ADR-0177 §1 and ADR-0179 §4 use,
   on the tile-unit `dx`/`dy` layout) and the remainder `B \ translate(A, t)`
   has **`kPoseMinTiles` or more tiles**, whether or not that remainder is a
   kept pose.

2. **The two-part split still wins.** ADR-0177 §2's search runs first and
   unchanged: if any candidate `A` leaves a remainder that is a kept pose `C`,
   `B` is labelled `[A, C]`. Only when no candidate does is `B` labelled with
   the first candidate (file order, as ADR-0177 §2) that fits with a
   pose-sized remainder. So every pack that already carried a two-part label
   keeps it.

3. `PoseEntry::FusionOf` holds **one** position in that case, and the sidecar
   writes it as a one-id list:

   ```json
   { "id": "pose018", "frames": 7, "size": [3, 7],
     "fusionOf": ["pose016"], "tiles": [ … ] }
   ```

   A reader treats any non-empty `fusionOf` as "fused", as
   `compose_engine.Pose.fused` and `artist_kit` already do. A one-id list
   names the kept part; the rest of the tiles have no pose id to name.

4. **With ADR-0179 §4 this is a partition.** For a strict containment of a
   kept pose `A` in a kept pose `B`:
   - remainder of 1 to `kPoseMinTiles - 1` tiles: `B` is a **variant** of `A`
     (ADR-0179 §4, unchanged);
   - remainder of `kPoseMinTiles` or more tiles: `B` is a **fusion**, with two
     parts if the remainder is a kept pose (ADR-0177) and one part otherwise
     (this ADR).

   The variant pass already skips fused entries, so no entry carries both
   labels. A pose that contains no kept pose carries neither label.

5. Tests (`scripts/core_unit_tests.cpp`, Bloco P): a kept pose plus an
   8-tile remainder that never stands alone is a one-part fusion of that pose,
   not a variant, and serialises as `"fusionOf": ["pose000"]`. The same holds
   at the boundary, a remainder of exactly `kPoseMinTiles` tiles. A kept pose
   plus a 3-tile remainder is still a variant. The existing ADR-0177 cases
   (two figures seen apart, two copies of one shape) keep their two-part
   labels unchanged. `scripts/test_artist_kit.py` checks that a one-part
   fusion is kept out of every grid and that `dropped[]` names the one part
   and cites this ADR, not ADR-0177's "both halves are laid out".

6. **A part the screen edge cut is not a part (issue #504).** §1's containment
   test gains one gate. A candidate `A` that fits inside `B` at translation `t`
   is skipped — in §1's one-part remainder and in §2's two-part split alike —
   when `A` was never once drawn clear of the screen. Both have to hold at
   *every* frame `A` appeared in:

   - `A`'s own bounds reached or passed one of the four edges of the 256x240
     screen; and
   - at least one tile of `B \ translate(A, t)` would have sat past **that same**
     edge.

   The NES draws no entry a screen edge cut, so at such a frame the frame could
   not have shown the rest of the figure: `A` alone is the edge's doing, not a
   figure standing on its own. One appearance clear of all four edges, or one
   where the other part would have been on screen and was not drawn, is real
   evidence and keeps the label.

   `poses.json` states a silhouette's tiles and never where it was drawn, so the
   recorder carries the per-appearance positions in memory from the segmentation
   it already runs (`BuildPoses` → `PoseOrigins`) and hands them to
   `LabelPoseFusions`. **No field is added to the OAM dump or to the sidecar**,
   which is what makes this a refinement of §1 and not a change of format.

   Measured on the SMB3 World 1-1 route (`scripts/stages/smb3`, 2185 retained
   frames): fusions go from **13 of 37 to 2 of 37**, and `pose004` — the Piranha
   Plant, 475 frames, the file's most-seen pose — is laid out as a figure instead
   of being listed as touched by another. All 13 labels ran through `pose017`, the
   plant's half, 17 frames, every one of them at x=248..255 with the plant's other
   half starting at x=256. The two that stay are `pose013` and `pose024`, whose
   parts (`pose026`, `pose015`) are seen clear of every edge. Castlevania's
   stage-1 route (`scripts/stages/castlevania`, 3554 retained frames) is unchanged
   at 99 of 211, so the gate cost no measured pack a label.

   Every one of those 13 parts also carries the pipe-mask nodes 0/1 (issue #505),
   which no decision excludes from the cluster yet. The gate reads the part's
   screen positions and the split's geometry, so the mask does not move it: were
   #505 to drop nodes 0/1 from the cluster, `pose017` would be the plant's column
   alone at the same x, and the same gate would fire.

   Tests (`scripts/core_unit_tests.cpp`, `BlocoP`). `TestAPoseWhosePartIsOnlyEverSeenClippedByTheScreenEdgeIsNotAFusion`
   is the issue's case: the two-column figure of §5 with its left column alone at
   x=200 keeps its one-part fusion, and with that column alone only at x=248 —
   where its other half would start at 256 — the figure is not labelled at all.
   `TestAPoseWhosePartIsOnlyEverSeenClippedByAScreenEdgeIsNotAFusion` runs the
   same fixture against each of the four edges (the other half past the right
   edge at x=248, past the left at x=0, above at y=0, below at y=208).
   `TestAPoseWhoseClippedPartIsAlsoSeenClearOfTheEdgesStaysAFusion` pins the other
   direction: the same five appearances with two of them clear of every edge keep
   the label, naming the block. `TestATwoPartSplitWhoseHalvesAreBothEdgeClippedIsNotAFusion`
   is §2's branch, with both halves kept poses and both seen only at an edge.
   Disabling the gate turns all four edge cases, the clipped-only half of the
   third test and the two-part test red (7 cases, `runs/504-mutation2.txt`); the
   "one appearance clear of the edges" case is a positive control and cannot move
   under a gate that only withholds labels.

## Consequences

- On the Contra stage-1 re-record, fusions go from 2 to 6 (`pose018`,
  `pose020`, `pose025`, `pose026` join `pose021`, `pose022`). Nothing else in
  `poses.json` changes: same 27 poses, tiles, `next`, cycles and ids. The
  kit's rest sheet `usr003` drops from 10 figures to 6, all of them Bill
  alone. The sprite, background and CHR `--verify` all stay at 0 lost / 0
  added.
- **The remainder's tiles stay paintable, but not through `unsorted`.** The
  `unsorted` sheet (ADR-0209 Q4) holds only shapes no other sheet claimed, and
  the recorder's sprite vocabulary sheet `sprites.png` claims every sprite
  shape, so `unsorted` never sees them. The 21 tumble tiles of the four Contra
  entries stay on the pack's `sprites.png` (21 of 21, as cells or aliases) and
  on its `spr003`/`spr004`/`spr006` group sheets. In the kit they stay on the
  CHR pages (21 of 21 patterns, same as before this change). What they lose is
  a place in the kit's whole-figure grids, because no kept pose holds them
  without a stranger, and the recording has no frame of them alone.
- `PosesForCells` (ADR-0177 §6) now also skips the one-part fusions, so on
  Contra `spr003`, `spr004` and `spr006` cite no pose, and the soldier sheets
  `spr002`/`spr022`/`spr023`/`spr024` stop citing the composites.
- `compose_engine.pose_for_anchor` still falls back to a fused pose when every
  pose holding an anchor is fused (ADR-0177 §5). So the tumble tiles stay
  reachable from the composition editor, labelled.
- **The false-positive class ADR-0177 accepted gets wider.** ADR-0177 already
  mislabels a single figure whose halves are also drawn apart. This ADR adds
  a single figure that is a kept figure plus a big attachment never seen on
  its own: a rider on a mount that never appears riderless, a boss that is
  only ever drawn with its arm. The trade is ADR-0177's own. The label costs
  one suggestion, and the entry stays reachable by id. A figure mislabelled
  as two costs less than two figures offered as one.
- The pass stays inside ADR-0177's cost bound: the same candidate index and
  the same containment test, with one extra rank remembered per entry. §6 adds
  one set of positions per silhouette, bounded by the frames it appeared in, and
  one edge test per candidate placement — no new asymptotic term.
- A pack recorded before this ADR keeps its old labels until it is
  re-recorded; nothing breaks. `fusionOf` was always a list with no length a
  reader could rely on. §6's gate is in the recorder too, so it is the same
  re-record.
- **§6 buys one figure back at the price of a real fusion it can no longer see
  (issue #504).** An actor that only ever stopped flush against a screen edge,
  with another actor just past it, is indistinguishable in this file from one
  figure the edge cut — and the label is now withheld. The trade is the mirror of
  the one above, and it was the one the artist paid: the kit had excluded the
  plant's most-seen pose. Measured, it costs nothing on Castlevania's stage-1
  recording (99 of 211 fusions before and after), and `poses.json`'s schema does
  not move, so a reader of §3 above is unaffected.
