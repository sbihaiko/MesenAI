# Issue #451: `artist_map.py --verify` round-trips the banded panorama (2026-09-24)

Found in the Castlevania deep measurement (bug B2): since the ADR-0220 context
band, `scripts/artist_map.py --verify` failed on every panorama. The kit
assembler then printed "map FAILED" in `ARTIST.md`. This log records the root
cause, the fix and the measurements. Recordings, packs and kits lived in a
session scratchpad and are not versioned.

## Root cause

ADR-0220 §3 grows a context-carrying surface below its cell grid. The flat
panorama PNG and its `*.orig.png` twin grow together; on Castlevania the twin
is 560x254 at 1x for a 200-row grid. `verify()` built its throwaway pack by
copying the panorama in as a sheet: it upscaled the grown twin to the pack's
`<scale>` and wrote it next to the sidecar. `mep_build` sizes a sheet from its
`cells[]`, so it refused the canvas:

```
error: pano-stage1-000.png: 2240x1016 is not an integer multiple of the 560x200 sheet pano-stage1-000.json describes
```

The key comparison then read the failed build as a result. The unit suite
stayed green because `test_artist_map.py` had no verify case.

## Fix and the option chosen

ADR-0183 §4 says a surface is delivered when a pack carrying it rebuilds with
0 errors and an unchanged key set. ADR-0220 §5 says the artist's only return
path is the flat PNG over the surface's name. For a panorama, that PNG goes
back through `--slice` (`cut_painted`), not by copying it into
`textures/sheets/`. `ARTIST.md`, the kit notes and
`docs/remastering-a-game.md` §4 all say this.

Two options were considered:

- **Crop the reference copy to the grid.** This is the spike from the
  measurement. It passes, but it verifies a path no artist takes. A
  regression in `--slice` itself (band handling, scale growth, the
  first-occurrence rule) would stay invisible.
- **Verify through `--slice`.** This was chosen. `verify()` now hands the
  kit's own flat PNG, which carries the band, to `cut_painted`. Its drop-in
  lands in the throwaway pack's `textures/sheets/`, and `mep_build` rebuilds
  that pack. The rebuild sees exactly what the artist's export would produce,
  at the pack's `<scale>`.

The module docstring's claim that the panorama is itself a `textures/sheets/`
drop-in is corrected. It stopped being true when the band landed.

## TDD

New case `test_verify_round_trips_the_banded_panorama_the_kit_writes` runs
`artist_map.main([... "--verify"])` on a synthetic recording and a minimal real
recorded pack (`<scale>2`, CHR page with its twin, one base sheet). It asserts:

- the band is present;
- the rebuild has 0 errors and 0 keys lost;
- every cell is byte-identical to the pack's art;
- any added key comes from the source;
- the panorama really reached the rebuild (16 -> 690 keys);
- the exit code is 0.

Red before the fix (unmodified `artist_map.py`):

```
verify: FAILED - 2 rebuild error(s), 0 key(s) lost, 0 cell(s) no longer byte-identical to the pack's art
FAIL verify rebuilds the pack copy with 0 errors: {"ran": true, "errors": 2, "cellsChecked": 1680, "cellsByteIdentical": 1680, "keys_source": 690, "keys_before": 16, "keys_after": 690, "lost": 0, "added": 674, "addedAreFromSource": true}
FAIL so --verify exits 0: 1
18/20 case group(s) passed
```

The same fixture's underlying `mep_build` error:
`pano-s-000.png: 896x728 is not an integer multiple of the 448x240 sheet`.

Green after the fix: `20/20 case group(s) passed`.

Mutations:

- **Restore the old copy-as-sheet code.** The same two FAILs as above.
- **Slice into the pack root instead of `textures/`.** The panorama silently
  never reaches the rebuild:
  `FAIL the panorama really reached the rebuild: every key it shows is routed: 16 -> 16 of 690`.
  The first three checks alone would have passed this.

## E2E: Castlevania stage 1

Recording `fix416/cv1` (grid dump + `Castlevania/auto` pack), run with
`--stage stage1 --scale 4 --verify`. It produced 2 regions, with panorama
canvases of 2240x1016 and 1632x1016 against 200-row grids (band present).

| | exit | rebuild | cells byte-identical | keys | lost | added |
|---|---|---|---|---|---|---|
| before (HEAD) | 1 | exit 2 (baseline 0), size refusal | 3025/3025 | 532 -> 3209 (failed build) | 0 | 2677 |
| after | 0 | exit 0 (baseline 0) | 3025/3025 | 532 -> 540 | 0 | 8, all from source |

## Suites

- `python3 scripts/test_artist_map.py`: 20/20 case groups (was 19).
- `make python-tests`: 59 passed, 0 failed, 0 skipped.
- `make doc-checks`: exit 0.
