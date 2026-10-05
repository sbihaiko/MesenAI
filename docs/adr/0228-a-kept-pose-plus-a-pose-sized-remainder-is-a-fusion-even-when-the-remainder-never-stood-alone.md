# ADR-0228: A kept pose plus a pose-sized remainder is a fusion, even when the remainder never stood alone

- Status: accepted 2026-09-23, implemented the same day (issue #401); amended 2026-09-25 by issue #504 (§1's screen-edge gate).
- Date: 2026-09-23
- Related: ADR-0170 §1 (the pose sidecar and `kPoseMinTiles`), ADR-0173 (label-don't-delete), ADR-0174 (`poses[]` on a `sprNNN` sidecar), ADR-0179 §4 (`variantOf`), ADR-0183 §2 (the Figures surface excludes fusions), ADR-0209 Q4 (the `unsorted` sheet), ADR-0225 (the `px`/`py` layout the evidence was read from), issue #401, issue #504 (§6)
- Amends ADR-0177 §1 (a fusion no longer requires the remainder to be a kept pose) and §3/§4 (`FusionOf` / `fusionOf` may carry one part instead of two). Leaves ADR-0179 §4 unchanged; it takes the case §4 hands back to ADR-0177 and makes sure ADR-0177 takes it.

## Context

ADR-0177 labels a pose a fusion when its tiles split into two poses the recorder also kept; ADR-0179 §4 labels a pose a variant when it holds a kept pose plus fewer than `kPoseMinTiles` other tiles. Between them sits a case neither covers: a pose holding a kept pose plus **`kPoseMinTiles` or more** other tiles never kept as a pose of their own, which every consumer treats as a figure.

The Contra stage-1 re-record of 2026-09-23 (the F12.18/F12.19 shared recording, 27 poses) has exactly four such entries, measured on `poses.json` and `adjacency.json` and seen as Bill fused with a soldier on the kit's rest sheet `usr003`:

| pose | frames | holds (kept pose) | remainder |
|---|---|---|---|
| `pose018` | 7 | `pose016`, a running-soldier phase (`FF36160F` torso, `FF20000F` legs) | 8 tiles: a Bill death-tumble frame plus the blank tile |
| `pose020` | 5 | `pose012`, a running-soldier phase | 8 tiles: another tumble frame plus the blank tile |
| `pose025` | 3 | `pose017`, a running-soldier phase | 8 tiles: a tumble frame plus the blank tile |
| `pose026` | 3 | `pose017` | the same 8 tiles as `pose025`, at another offset |

The two halves are two actors on every piece of evidence: the soldier half steps through cycle002's own phases (`pose025 → pose026 → pose018`) while the Bill half steps through tumble frames, and the input never pressed A, so the ball is the death tumble. The tumble frames once Bill clears the soldier (`pose019`, `pose023`, `pose024`) are kept on their own; the first frames never are, because Bill dies touching the soldier — the frames that would make the remainder a kept pose do not exist. Palette and OAM order cannot separate the two figures, so the only structural fact left is this ADR's: a kept figure is inside, and what is left over is big enough to be a figure.

Non-goals: splitting the entry or minting a pose for the remainder (that would invent a figure never seen alone); changing how clusters are formed, identified, ranked or counted; any threshold beyond `kPoseMinTiles`; anything at run time.

## Decision

### 1. A kept pose plus a pose-sized remainder is a fusion

A kept pose `B` is a **fusion** when some kept pose `A` fits inside it at a translation `t` (the containment test ADR-0177 §1 and ADR-0179 §4 use, on the tile-unit `dx`/`dy` layout) and the remainder `B \ translate(A, t)` has **`kPoseMinTiles` or more tiles**, whether or not that remainder is a kept pose.

### 2. The two-part split still wins

ADR-0177 §2's search runs first and unchanged: if any candidate `A` leaves a remainder that is a kept pose `C`, `B` is labelled `[A, C]`; only when none does is `B` labelled with the first candidate (file order) that fits with a pose-sized remainder, so every pack with a two-part label keeps it.

### 3. `FusionOf` holds one position

`PoseEntry::FusionOf` holds **one** position in that case, written as a one-id list:

```json
{ "id": "pose018", "frames": 7, "size": [3, 7], "fusionOf": ["pose016"], "tiles": [ … ] }
```

A reader treats any non-empty `fusionOf` as "fused", as `compose_engine.Pose.fused` and `artist_kit` already do. A one-id list names the kept part; the rest of the tiles have no pose id to name.

### 4. With ADR-0179 §4 this is a partition

For a strict containment of a kept pose `A` in a kept pose `B`:

- remainder of 1 to `kPoseMinTiles - 1` tiles: `B` is a **variant** of `A` (ADR-0179 §4, unchanged);
- remainder of `kPoseMinTiles` or more tiles: `B` is a **fusion**, with two parts if the remainder is a kept pose (ADR-0177) and one part otherwise (this ADR).

The variant pass skips fused entries, so no entry carries both labels; a pose containing no kept pose carries neither.

### 5. Tests (`scripts/core_unit_tests.cpp`, Bloco P)

A kept pose plus an 8-tile remainder that never stands alone is a one-part fusion, not a variant, serialised `"fusionOf": ["pose000"]`; the boundary (`kPoseMinTiles` tiles) too; a 3-tile remainder is still a variant; ADR-0177's two-part cases keep their labels. `scripts/test_artist_kit.py` checks a one-part fusion is kept out of every grid and `dropped[]` cites this ADR, not ADR-0177's "both halves are laid out".

### 6. A part the screen edge cut is not a part (issue #504)

§1's containment test gains one gate: a candidate `A` fitting inside `B` at `t` is skipped — in §1's one-part remainder and §2's two-part split alike — when `A` was never once drawn clear of the screen, i.e. at *every* frame `A` appeared in, `A`'s own bounds reached or passed one of the four edges of the 256x240 screen and at least one tile of `B \ translate(A, t)` would have sat past **that same** edge. One appearance clear of all four edges, or one where the other part would have been on screen and was not drawn, keeps the label.

`poses.json` never says where a silhouette was drawn, so the recorder carries the per-appearance positions (`BuildPoses` → `PoseOrigins`) to `LabelPoseFusions`; **no field is added to the OAM dump or the sidecar**, which is what makes this a refinement of §1, not a format change.

Measured on the SMB3 World 1-1 route (`scripts/stages/smb3`, 2185 retained frames): fusions go from **13 of 37 to 2 of 37**, and `pose004` — the Piranha Plant, 475 frames, the file's most-seen pose — is a figure. All 13 labels ran through `pose017` (17 frames) at x=248..255 with the plant's other half starting at x=256; `pose013` and `pose024` stay (parts `pose026`, `pose015` seen clear). Castlevania's stage-1 route (`scripts/stages/castlevania`, 3554 retained frames) is unchanged at 99 of 211.

Tests (`BlocoP`): `TestAPoseWhosePartIsOnlyEverSeenClippedByTheScreenEdgeIsNotAFusion` (the issue's case), `TestAPoseWhosePartIsOnlyEverSeenClippedByAScreenEdgeIsNotAFusion` (x=248, x=0, y=0, y=208), `TestAPoseWhoseClippedPartIsAlsoSeenClearOfTheEdgesStaysAFusion` (positive control: one appearance clear of the edges keeps the label), `TestATwoPartSplitWhoseHalvesAreBothEdgeClippedIsNotAFusion` (§2's branch). Disabling the gate turns 7 cases red (`runs/504-mutation2.txt`).

## Consequences

- On the Contra stage-1 re-record fusions go from 2 to 6 (`pose018`, `pose020`, `pose025`, `pose026` join `pose021`, `pose022`); `poses.json` is otherwise unchanged (same 27 poses, tiles, `next`, cycles, ids); the kit's rest sheet `usr003` drops from 10 figures to 6, all Bill alone; the sprite, background and CHR `--verify` stay 0 lost / 0 added.
- The remainder's tiles stay paintable, but not through `unsorted` (ADR-0209 Q4): `sprites.png` claims every sprite shape, so the 21 tumble tiles stay on `sprites.png` and on `spr003`/`spr004`/`spr006`, and on the CHR pages in the kit; they lose their place in the whole-figure grids.
- `PosesForCells` (ADR-0177 §6) skips one-part fusions, so on Contra `spr003`, `spr004` and `spr006` cite no pose, and `spr002`/`spr022`/`spr023`/`spr024` stop citing the composites.
- `compose_engine.pose_for_anchor` still falls back to a fused pose when every pose holding an anchor is fused (ADR-0177 §5), so the tumble tiles stay reachable from the composition editor, labelled.
- The false-positive class ADR-0177 accepted widens: a kept figure plus a big attachment never seen alone (a rider on a mount never drawn riderless). The trade is ADR-0177's; the label costs one suggestion and the entry stays reachable by id.
- §6 buys `pose004` back at the price of a real fusion it can no longer see (an actor flush against an edge); it costs Castlevania nothing (99 of 211) and `poses.json`'s schema does not move.

## Record

- 2026-09-23 — accepted and implemented the same day (issue #401, `Core/NES/HdPacks/SpriteGrouping.cpp` `LabelPoseFusions`, the kit's `dropped[]` wording in `scripts/artist_kit.py`). Pick, verbatim: *"Tratar como fundida (Recommended)"*. Implementation go-ahead, verbatim: *"pode corrigir o #400 e o #401 em paralelo também"*. Evidence: `docs/validation/issue-401-rest-grid-composites-2026-09-23.md`.
- 2026-09-25 — §6 added by issue #504, implemented the same day (`LabelPoseFusions`, `PartOnlyEverSeenClippedByTheScreenEdge`) with the `BlocoP` cases §6 names. Instruction, verbatim: *"A narrow refinement is fine: then also amend ADR-0228 in place per .claude/skills/adr/SKILL.md (dated note on Status line + the refined rule + evidence), without changing its Decision otherwise."* This refines §1 rather than reversing it.
