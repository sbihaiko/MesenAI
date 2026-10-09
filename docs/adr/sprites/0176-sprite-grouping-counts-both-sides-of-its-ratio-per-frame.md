# ADR-0176: Sprite grouping counts both sides of its ratio per frame, so a shape drawn twice in one frame can still join a group

- Status: accepted (2026-09-12, after the measurement below; implemented the same day in
  `Core/NES/HdPacks/SpriteGrouping.cpp`)
- Date: 2026-09-12
- Related: ADR-0153 §2 (the criterion this corrects on the sprite side), ADR-0164 §1,
  ADR-0173 (the same biased denominator, fixed for `floors[]`), ADR-0174 (the pose join),
  issue #176
- Supersedes / amends: ADR-0153 §2, OAM analog only — the background criterion is
  unchanged.

## Context

`SelectSpriteEdges` keeps a pair only when each shape predicts the other:

```cpp
double probAb = (double)count / appearA;
double probBa = (double)count / appearB;
if(probAb < minProb || probBa < minProb) { continue; }   // kSheetMinPairProb = 0.80
```

`count` is how often the pair was seen at its dominant offset; `appearA` is
`Appearances[cell]++`, which `Accumulate` increments once per **OAM entry**. Different
bases: a shape drawn twice in a frame scores two appearances while the dominant offset can
be hit by only one. The ceiling is `1 / (appearances per frame)` — at two per frame the
best probability is 0.5 against the 0.80 threshold, so **every edge from that shape is
dropped**; the denominator decides, not the evidence.

The symptom: `spr016.orig.png` on the Contra pack renders `GA M` / `OV R`, and the missing
`E` is in **zero** slots — the glyph occurs twice per frame ("GAME" and "OVER"). Measured
over the golden kit (`runs/golden-20260912/`, not versioned), from `appearances / frames`
(in `adjacency.json` since ADR-0173):

| pack | sprite nodes | repeat within a frame (> 1.01) | ungroupable by arithmetic (> 1.25) |
|---|---|---|---|
| Mega Man 3 | 288 | 73 (25 %) | **42 (15 %)** |
| Zelda 1 | 123 | — | **18 (15 %)** |
| Contra | 248 | 30 (12 %) | **16 (6 %)** |
| Excitebike | 208 | — | **9 (4 %)** |

The median node sits at 1.00, so this is a clean minority silently excluded; three Mega
Man 3 nodes sit near ratio 31, which no threshold in (0, 1] could admit. Same error
ADR-0173 found in `floors[]` — a per-frame numerator over an instance-counted denominator;
that ADR added `NodeFrames` for it, the grouping criterion never did.

Non-goals: changing `kSheetMinPairCount` or `kSheetMinPairProb` (the threshold is not
wrong); the background criterion in `SheetGrouping::SelectDirection` (already
per-placement); making a pack recorded before this ADR group differently, which only a
re-record can do.

## Decision

**Both sides of the ratio are counted once per frame.**

1. `SpriteStats` counts, per vocabulary cell, the number of **frames** the shape appeared
   in at all (once per frame, however many instances) — the same quantity ADR-0173 named
   `NodeFrames`, from the same de-duplicated stream.

2. For an ordered pair and offset, the tally is incremented **at most once per frame**: the
   frame counts when *some* instance of A has *some* instance of B there; a second pair at
   the same offset does not raise it.

3. The criterion is read as *of the frames in which A appears, in how many is there an A
   with a B at this offset*. Both `probAb` and `probBa` use the per-frame numerator over
   the per-frame denominator; `kSheetMinPairCount` / `kSheetMinPairProb` keep their
   **values**. `kSheetMinPairProb` keeps its meaning; `kSheetMinPairCount` does not — its
   floor is now a count of frames, not instance pairs.

4. `Appearances` stays in `adjacency.json` unchanged — the honest instance count, which
   ADR-0173's `positions`/`frames` pair reads against; only the grouping ratio stops using
   it.

## Consequences

- A shape that repeats within a frame becomes groupable on the evidence rather than
  excluded by arithmetic — 4–15 % of sprite nodes per pack on the measured kit.
- **A deliberate loosening.** A shape appearing twice with one instance beside B scored 0.5
  under the old reading. Under the new one it scores 1.0, "whenever A is on screen, an A
  is beside B" — a sheet cell is a shape, not an instance. It does admit pairs the old rule
  refused for a non-denominator reason; the `kSheetMinPairCount` floor of 3 frames still
  guards coincidence.
- **Sheet layout changes for newly recorded packs.** Existing packs keep loading untouched
  and do not benefit until re-recorded — the cost ADR-0174 declined for the split-figure
  problem, paid here because an edge never created cannot be cross-referenced later.
- **`kSheetMinPairCount` becomes a floor on frames, and that removes some groups.** On
  Excitebike, nodes 183 and 184 formed `spr033` on 8 instance pairs, but those came from
  **2 frames**. The old rule saw `count = 8 >= 3` and `prob = 8/8 = 1.0`; the new one sees
  `count = 2 < 3` and drops the edge — the concern `Accumulate`'s comment names about
  `RepeatCount`. Net across the kit: Mega Man 3 +18 placements, Zelda 1 +5, Contra +1,
  Excitebike loses exactly this pair.
- ADR-0174's `poses[]` join is unaffected — poses come from the silhouette stream, not
  these edges.
- The `E` of `spr016` is the acceptance case.
