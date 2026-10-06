# Issue #416: every map placement resolves (2026-09-24)

`scripts/mep_build.py build` warned
`map-000.png: 3 placement(s) do not resolve in the metatile vocabulary — skipped`
on a Castlevania recording, and those crops never reached the pack. This log
records the root cause, the fix and the measurements. Recordings, packs and
scripts lived in a session scratchpad and are not versioned.

## Root cause

A map's `placements[].cell` is an **absolute background-vocabulary index**
(ADR-0153 §4). ADR-0164 §1 already says why that is not a `metatiles.json`
cell: hud/font/misc entries go to their own sheets (ADR-0153 §3), an alias sits
inside its canonical cell's `aliases[]` (F9.7), and a cell a captured screen
owns is on no sheet at all (ADR-0156). The recorder was right. The map places
entries of its own vocabulary, and `adjacency.json` carries the keys of every
one of them.

`mep_build._vocabulary` looked the index up in `metatiles.json` alone, by
`cells[].metatile`, and then **fell back to the array position**:

```python
cell = by_vocab.get(idx, by_pos.get(idx))
```

That fallback caused two failures, and only one of them was visible:

- **Loud (the issue).** An index past the end of the array resolved to
  nothing and was dropped with the warning. On Castlevania these were the 3
  placements of alias entries 286, 303 and 304.
- **Silent (worse).** An index inside the array resolved to *whatever cell sat
  at that position*. The crop was emitted under another entry's keys. On
  Castlevania, 22 placements were affected: 19 screen-resident entries and 3
  placements of misc entry 148. After precedence, 5 crops reached `hires.txt`
  with the wrong key. For example, the key of the glyph "1"
  (`3F0C0C0C0C0C0C00…`, `0F201917`) pointed at the map crop of an "R".

## Fix

`scripts/mep_build.py` `_vocabulary` now resolves a placement against the
whole background vocabulary, as ADR-0164 §1 describes it:

1. `cells[].metatile` and `aliases[].metatile` of every background sheet
   (`metatiles`, `hud`, `font`, `misc`; `unsorted` numbers a vocabulary of its
   own and is excluded);
2. then the background `nodes[]` of `adjacency.json` (`cell` → `tiles`), which
   cover the screen-resident entries no sheet shows.

Array position is used only when no cell names a `metatile` at all (a legacy
sidecar). It is never used as a fallback beside real indexes. An index that
still cannot be resolved (for example a pack recorded before `adjacency.json`
that places a routed entry) is skipped. The warning now names the index and
the reason (`cell 2, 99: on no background sheet and not in adjacency.json`).
Core is unchanged, the sidecar schema is unchanged, and no ADR is amended. The
fix makes the reader do what ADR-0153 §4/§6 and ADR-0164 §1 already specify.

## Binary

Worktree on `main` `56d49588`, `make capture-tool` (CommandLineTools
toolchain, `SDKROOT=…/CommandLineTools/SDKs/MacOSX.sdk`):

- `MesenCore.dylib` sha256 `4fbebd50a22662c6eab8e45565fa66f78f97e9dd9be88b02bd98662134c5117f`
- `headless_record` sha256 `4e3cd287fce8c3b5847a1a127ee76e16e9ce2f9a8c7f80722808be916e9c6d46`

## Commands

```sh
# fresh sandbox dir per game, holding only a copy of the ROM
export MESEN_SHEET_GRID_DUMP=<dir>/grid-dump.txt MESEN_OAM_STREAM_DUMP=<dir>/oam-dump.txt
# Castlevania (1987) (Konami).nes, user library (whole-file SHA1 7A20C44F…)
headless_record <dir>/Castlevania.nes 60 <dir>/rec bootstrap hdpack-off log
# Contra (1988) (Konami).nes (whole-file SHA1 C9EA66BB…), input =
# scripts/stages/contra/mint-stage1.txt + stage1-run.txt
headless_record <dir>/Contra.nes 90 <dir>/rec bootstrap hdpack-off log input=<mint+run>.txt
# roms/Zelda.nes (whole-file SHA1 3701381A…), input =
# scripts/stages/zelda/mint-stage1.txt + stage1-run.txt (the F14.4 recipe)
headless_record <dir>/Zelda.nes 85 <dir>/rec bootstrap hdpack-off log input=<mint+run>.txt

# round trip (ADR-0183 §4): copy the pack, promote auto/textures to textures,
# build twice, compare the two textures/hires.txt
python3 scripts/mep_build.py build <copy> --quiet
```

"Before" is `scripts/mep_build.py` at `main` `56d49588`. "After" is this
change. Both arms read the same recording.

## Results

The "wrong key" column compares each emitted map crop against the keys that
`adjacency.json` records for the placement's own vocabulary entry.

| Recording | Placements | Before: warning | Before: wrong key in `hires.txt` | After: warning | After: wrong key | Keys carried (before = after) | Round trip after |
|---|---|---|---|---|---|---|---|
| Castlevania 60 s | 388 | 3 skipped | 5 crops | none | 0 | 532 | byte-identical |
| Contra stage 1 | 420 | 1 skipped | 2 crops | none | 0 | 505 | byte-identical |
| Zelda stage 1 | 352 | none | 0 | none | 0 | 262 | byte-identical |

- The carried key sets are unchanged (532 / 505 / 262; Castlevania and Zelda
  match the F14.4 log). Only the *source crop* of a key changes. Castlevania:
  9 `<tile>` lines move, each from a crop that drew another entry to the crop
  that draws its own. Contra: 2 lines. Zelda: `hires.txt` is byte-identical
  before and after (sha256 `f6bcafb6…afee`).
- **Paint reaches the pack.** On the Castlevania project, the one map crop of
  alias entry 286 (map pixel (232, 96), sheet pixel (928, 384) at scale 4)
  was repainted, and both builds were rerun. Before, the placement was skipped,
  and the entry's keys stayed on `obj026.png`, `font.png` and other map crops.
  After, all four of its keys point at the painted crop:
  `FFFF…/0F201917` at (928, 384), `4C4C4C00…/0F192201` at (960, 384) and
  `0000…/FF2A0010` at (960, 416).

### How widespread (existing recordings)

The same comparison was run offline over every recorded pack under the local
`runs/` tree that has a map plus `adjacency.json` (older binaries, many
duplicates of one recording). 73 packs, 71 641 placements. **69 packs were
affected**: 129 placements were dropped with the warning, and **7 199
placements resolved silently to another entry's keys**. The worst was Mega Man
3's 1 635-placement map, with 289 affected. With the fix, 0 placements are
left unresolved on all 73 packs.

## Tests

`scripts/test_mep_build.py` `map_full_vocabulary_tests` (8 checks). A map
places an alias entry, a misc entry and a routed entry (off `metatiles.json`,
so array position 2 holds a different entry). The test checks that each is
sliced from the map under its own keys, that an unknown index is reported by
number, that without `adjacency.json` the alias and misc entries still resolve
while the routed entry is reported rather than mis-resolved, and that a
legacy vocabulary with no `metatile` ids still resolves by position.

- The test fails on `main`'s `mep_build.py` (7 of 8 checks fail; only the legacy-position check passes) and passes
  after the change.
- Mutation-proven: each of five mutants of `_vocabulary` is killed. The
  mutants are: no `adjacency.json` lookup, `metatiles.json` only, aliases
  ignored, positional fallback restored, and legacy positional path removed.
- `scripts/checks/run_python_tests.sh`: 57 passed, 0 failed.
- `make doc-checks`: passes (`mep_build.py` 2 056 / 2 070 lines).
