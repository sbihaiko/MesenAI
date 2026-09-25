# Issue #474: a CHR ROM sprite tile drawn both flipped and unflipped is two shapes (2026-09-25)

Found while fixing #456/#457 (`docs/validation/issue-456-457-2026-09-24.md`,
"Second cause checked for #457"). On a CHR ROM game the recorder collapsed a
tile index drawn in both orientations into one shape, so kit figures showed
some poses in the wrong orientation (Punch-Out!! pose005 and pose023). This log
records the cause, the fix, the red-before-green output, the mutations and the
end-to-end numbers. The raw material (recordings, kits, scripts) lived in an
unversioned scratch directory.

## Root cause

`HdPackBuilder::ShapeIdFor` interned shapes in an
`unordered_map<HdTileKey, ShapeId>`. `HdTileKey` is the run time's key: on a
CHR ROM game its `operator==` compares `TileIndex` (and the palette, which the
recorder wildcards) and nothing else. `HdBuilderPpu` bakes the OAM flip bits
into a sprite's `TileData` before `RecordSprite` (ADR-0178), so one index drawn
unflipped and then flipped is two drawings. The map returned the first
sighting's shape for both. That shape kept the first orientation's baked art
and `Mirrors`, and `OamEntry` carries no flip bit, so every OAM entry of the
other orientation named the wrong art.

On CHR RAM the key already is the drawn data, so the two orientations were
always two shapes there. That is the behaviour the `RecordSprite` comment and
ADR-0178's Context describe: "the left and right halves of a mirrored figure
are distinct shapes".

## Fix

- `Core/NES/HdPacks/HdData.h` gains `HdShapeKey`, derived from `HdTileKey`.
  It keeps the palette wildcard and the hash, and its `operator==` also
  compares `TileData` on a CHR ROM tile. CHR RAM equality is unchanged, since
  there the data already is the identity.
- `HdPackBuilder::_shapeIds` is keyed by `HdShapeKey`, and `ShapeIdFor` builds
  one. `HdPackBuilder.cpp` has no net line change, so it stays at the ADR-0137
  ceiling.
- No format change. The grid and OAM dumps, the sidecar, `poses.json` and
  `hires.txt` keep their schemas. A CHR ROM index can now appear as more than
  one shape: several `K` lines, and several sidecar entries sharing `index`
  where the flipped ones carry `source`/`mirror`. ADR-0178 §7 already covers
  two cells that emit one key, and #457 (PR #475) un-bakes the mirrored crop
  on index-keyed packs. `compose_engine`, `artist_kit` and `mep_figure` read
  the sidecar per entry and need no change. `Core/AGENTS.md` states the
  contract.
- No ADR decision is involved. The fix restores the identity that ADR-0178
  and ADR-0153 already assume, and ADR-0222's dump format is unchanged.

## Red before the fix

`make core-unit-tests`, with `HdShapeKey` wired in but still comparing like
`HdTileKey`:

```
FAIL  #474: a CHR ROM tile drawn in three orientations is three shapes, each with its own baked art
PASS  #474: a second sighting in the same orientation reuses its shape
PASS  #474: the shape stays palette-wildcarded
PASS  #474: two CHR ROM indexes with the same art stay two shapes (ADR-0172)
PASS  #474: a CHR RAM tile is keyed by its drawn data, exactly as before

1057/1058 cases passed
```

## Mutations

| Mutation | Result |
|---|---|
| M1: `HdShapeKey::operator==` back to `HdTileKey`'s (the bug) | FAIL `a CHR ROM tile drawn in three orientations is three shapes` (the red above) |
| M2: guard inverted (`!IsChrRamTile \|\| memcmp(...)`) | FAIL `a CHR ROM tile drawn in three orientations is three shapes`, 1105/1106 |

`HdPackBuilder.cpp` is not in the unit-test link set, so the wiring itself
(`ShapeIdFor` using `HdShapeKey`) is covered by the end-to-end arm below, where
the `main` binary is the unwired control.

## End to end

Two binaries were built from clean trees with `make capture-tool`: `main` at
`33f92049` (the merge of #476, after #468 and #473) and this branch rebased
on top of it. `otool -L` resolved each
`headless_record` to its own `MesenCore.dylib`. Both were recorded from the
same minted states. A first pass on `main` at `bcdaebff`, before #468/#476,
reached the same conclusions: 0 against 57 orientations, and the same
figure-sheet diff.

### Punch-Out!! (CHR ROM, MMC2)

The route was `scripts/stages/punchout` as in the deep measurement: mint 34 s,
then record 70 s with 18 blocks of `fight1.txt`, `bootstrap hdpack-off log`,
and all three dumps on.

| Measure | `main` | this branch |
|---|---|---|
| Shadow-OAM tile bytes on screen / drawn in more than one orientation (`$0200` from the `M` lines) | 183 / 58 | 183 / 58 |
| Sidecar sprite indexes / with more than one orientation | 415 / **0** | 415 / **57** |
| OAM-dump shapes | 415 | 472 (+57) |
| OAM-dump CHR indexes whose art matches the ROM unambiguously / with more than one orientation | 369 / 0 | 369 / 51 |
| `hires.txt` `<tile>` keys (bare) | 9847 | 9847, the same set |
| `spriteNearby` conditions | 205 | 274 |
| `mep_lint` | 0 errors, 0 warnings | 0 errors, 0 warnings |
| `artist_kit.py --verify` | PASS, 1656 / 1656 keys, 0 lost, 0 invented | PASS, 1656 / 1656 keys, 0 lost, 0 invented |
| Poses / cycles / sequences | 34 / 6 / 15 | 34 / 6 / 15 |

- **pose005** (`usr009`) and **pose023** (`usr011`): on `main` both figures are
  torn, and each H cell shows the other orientation's art. On this branch both
  are whole Glass Joe figures, facing the way the game drew them.
- Other figure sheets that changed:
  - `usr010` (pose014): `main` shows a wrong-orientation tile where the head
    should be, and this branch shows the hair;
  - `usr004` (pose030, pose032): a 5-px row;
  - `usr012`: the loose figures.
  The other 8 figure sheets are pixel-identical.
- The `spriteNearby` change comes from the sprite grouping, which now sees
  each orientation as its own shapes. Each group's offsets therefore describe
  one orientation, not a mix of two. Every conditioned tile keeps its bare
  twin (ADR-0189), which is why the bare key set is unchanged.

### Castlevania (CHR RAM): no regression

The route was `scripts/stages/castlevania`: mint 6 s, then record 80 s with 16
blocks of `stage1-run.txt`. Between the two binaries, the pack tree
(`diff -rq`), the grid dump, the OAM dump and the pose-track dump are all
byte-identical. `hires.txt` sha256 is `3d7f0fdb…c503` in both. On the `bcdaebff`
pass it was `1b84b252…a298` in both, the value of the Castlevania deep
measurement; #468/#476/#473 moved it on `main` itself.

## Suites

- `make core-unit-tests`: 1152/1152 after the rebase, including 5 new #474
  checks.
- `make python-tests`: 61 passed, 0 failed (on `main` at `2c464ec4`).
- `make doc-checks`: passes.

## After merging #483 and #487 into this branch

#483 (#470/#471) feeds `ShapeIdFor` the background tiles of every scanline
(`MesenSheets::LayOutGridRuns`), not only a cell's origin. A background fetch
bakes no flips, so on a CHR ROM game one index always carries the same drawn
data and every scanline of it keys to the same `HdShapeKey`: the off-origin
runs add no shape, and only a mirrored sprite of that index is a new one.
`TestShapeKeyGivesOffOriginBackgroundRunsTheOriginsShape` pins that.

Re-run on a clean build of the merge (`origin/main` at `bc988553`), same
states and routes as above:

| Measure | Punch-Out!! fight 1 | Castlevania stage 1 |
|---|---|---|
| Sidecar sprite indexes / with more than one orientation | 406 / **57** | n/a (CHR RAM) |
| Drawn keys / sheet keys | 1623 / 1623 | 433 / 433 |
| Sheet keys never drawn / drawn keys with no cell | 0 / 0 | 0 / 0 |
| `sheet_keys_audit.py` | 833 entries, 0 leftover | 573 entries, 0 leftover |
| `artist_kit.py --verify` | PASS, 1623 keys, 0 lost, 0 invented | |

The sprite index count drops from 415 to 406 because #470 no longer records
all-zero sprite tiles, and the drawn keys drop because #479 makes no rule on
screen row 0. The kit's figure grouping moved with those key sets: pose005
now sits on `usr005`, pose023 stays on `usr011`, and both are whole and
facing the way the game drew them.

Suites on the merge: `core-unit-tests` 1167/1167, `python-tests` 62 passed,
`doc-checks` passes.
