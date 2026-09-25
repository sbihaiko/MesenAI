# Issue #422: `build`'s #338 warning misses a cell `mep_add_cell.py` placed, or names the wrong capture

**Date:** 2026-09-24
**Base:** `main` @ `9a0ff899`
**Binary (E2E only):** `scripts/headless_record` built in the fix worktree
(`make capture-tool`), sha256
`86fb675759fd941b8f6d9ac3d842ce0dc49aa049a54f5fa2698696d19e2d4fd8`.
**Found by:** F14.2 (`f14.2-cold-read-rescore-2026-09-24.md`, *What the
passing runs still found*, item 1; 10 of 27 runs).

## Root cause

`mep_build.py build` found a painted key's capture only through
`adjacency.json`'s `screens[]`, by way of `screen_resident_keys`. The recorder
writes `screens[]` only on a **screen-resident** node, one no sheet shows
(ADR-0156 §1, ADR-0166; `core_unit_tests.cpp`: "a node a sheet shows never
carries screens[]"). Two defects follow from that:

1. **Silence (Mario Bros.).** A key that is on a sheet *and* drawn by a
   capture has no `screens[]`. That is the key a reader copies from the
   tilemap: Mario Bros.' girder tile 403 sits on `metatiles.json` cells 1 and
   7, and `mep_add_cell.py` says so when it places the copy. So the key was
   never in the lookup, and `build` printed only the generic
   "N captured screen(s)" line.
2. **The wrong capture (Ice Climber, Pac-Man).** When the key *was*
   resident, `build` named `screens[0]` of the first node that held it, and
   named only that one. Many captures draw the same key, and the one covering
   a given frame is whichever capture's probes match that frame (ADR-0217).
   So `build` named `screen002`/`screen013` while the runtime drew
   `screen004`/`screen012`.

## Fix

New module `scripts/mep_capture_scan.py`, called from the #338 block of
`mep_build.py`. It reads each live capture's `backgrounds/screenNNN.orig.png`,
which `HdPackBuilder::CaptureScreen` writes as the ROM's background plane,
nearest-upscaled and drawn with the 2C02 palette. It then looks for every
painted key's tile bitmap, under the key's palette, among the capture's 8x8
cells:

- **Columns** start at the capture's fine x scroll, read off its first
  `tileAtPosition` probe (`x = fineX + 8 * col`). A capture with no probe is
  read at phase 0.
- **Rows** are searched at all 8 offsets. A probe's y is the scanline it
  samples, not where its tile starts: Ice Climber draws its rows at
  `y = 8k + 1`, so reading the row phase off the probe found nothing there.
- **Provenance** from `screens[]` is kept and merged with the scan.
- **Only live captures count.** A capture whose `screenNNN.png` is gone is
  retired (#344) and never warned about.

The #338 line now names **every** live capture that draws the key. It still
counts only painted cells, per sheet. `mep_build.py` grows from 2056 to 2062
lines, under its 2070 ceiling, which is unchanged. The module joins
`scripts/tools-zip-manifest.txt`, the import closure of `mep_build`.

`docs/remastering-a-game.md` now says what the line lists: every capture that
draws the key, including one over a cell `mep_add_cell.py` placed. It also
says that not every listed capture covers the reader's frame.

No ADR decision is involved. ADR-0050 and ADR-0156 are unchanged: a capture
still wins on the frames it owns. This change only makes the build's warning
see a key that is both on a sheet and in a capture. The adjacency format
(ADR-0166) is untouched.

## Red → green

New tests in `scripts/test_mep_build.py` (`capture_shadow_tests`):

- **Mario Bros. shape, CHR ROM (index keys).** The fixture puts an
  `unsorted` sheet in the pack and places the copy with the real
  `mep_add_cell.py`, which reports the `metatiles.json` claim. It then paints
  the reported slot. The adjacency node holds the key with no `screens[]`,
  `screen002.orig.png` draws it, and `screen001.orig.png` does not.
- **Which capture.** The adjacency provenance points at `screen003`.
  `screen004` is 2x, has fine x scroll 3 (read off its probe) and draws its
  rows one line down; it draws the key. `screen001` does not. Expected: 003
  and 004 are named, and 001 is not.

Red, on `main`'s `mep_build.py`: `python3 scripts/test_mep_build.py`, exit 1.

```
FAIL: #422: a cell mep_add_cell.py placed and a capture draws got no (#338) line naming it:
FAIL: #422: the (#338) line names a capture that does not draw the key, or fired twice: []
FAIL: #422: the capture that draws the painted key (screen004) is not named:
```

The second case's old output named only `backgrounds/screen003.png`, the
adjacency one.

Green: exit 0, 118 PASS, 0 FAIL. The run includes the three new checks and
the pre-existing `#338`/`#343`/`#344` checks.

## Mutation check

Each mutation was applied alone, then `test_mep_build.py` was run, then the
file was restored.

| Mutation | Result |
|---|---|
| no pixel scan (adjacency only, i.e. `main`) | killed (3 FAIL) |
| no row-offset search (row phase 0 only) | killed (1 FAIL) |
| probe x phase ignored | killed (1 FAIL) |
| `screens[]` provenance dropped | killed (3 FAIL, incl. the old #338 test) |
| first matching capture only | killed (1 FAIL) |
| scan the painted `screenNNN.png` instead of `.orig.png` | killed (3 FAIL) |
| per-sheet line ignores the paint state | killed (5 FAIL) |
| unpainted keys also passed to the scan | survived: equivalent. The per-sheet filter still counts only painted cells, so only the cost changes |

## E2E

Recorded fresh with the fix worktree's binary, 60 s bootstrap, in an
isolated folder holding only the ROM. This is the F14.2 `record_one.sh`
body, and it gave the same capture count as F14.2 (Mario Bros. 3). The steps:

1. Build once.
2. Place the F14.2 run's own copy line with `mep_add_cell.py`, then paint a
   solid square on the slot it reports.
3. Build with `main`'s `mep_build.py` (before) and with the fix (after).
4. Deploy the work copy as `mep/`, render the F14.2 saved state
   (`headless_record <rom> 0 <prefix> screenshot log state=<mss>`), and read
   the capture the runtime log says is drawn over the painted tile.

| Game (copy line) | Before (`main`) | After | Runtime draws |
|---|---|---|---|
| Mario Bros. (tile 403, `0F302C12`) | no `(#338)` line | `screen002`, `screen003` | `screen002` |
| Ice Climber (tile 400, `0F271707`) | `screen002` only | 23 of 25 captures, incl. `screen004` (not 001, 003) | `screen004` |
| Pac-Man (blank tile 32, `0F200F06`) | `screen013` only | all 26 captures, incl. `screen012` | `screen012` |

The before column reproduces the F14.2 observations exactly: Mario Bros.
silent, Ice Climber `screen002`, Pac-Man `screen013`. After the fix, the
capture the runtime draws is in the list on all three games.

Build time: Pac-Man, 26 captures and one painted key, 0.13 s → 0.93 s. The
scan runs only when a sheet has a painted cell.

## Limits

- **A flat key matches by pixels, not by identity.** A single-colour tile
  (Pac-Man's blank backdrop) matches every capture cell of that colour, and
  such a cell may belong to a different key with identical pixels. On
  Pac-Man, all 26 captures are listed. The capture that covers the frame is
  still among them.
- **Colours assume the recorder's palette.** The scan draws tiles with the
  2C02 palette, which `headless_record` seeds and a default GUI recording
  uses. A capture recorded under a custom palette finds nothing, which is
  the pre-fix behaviour (silence).
- **No frame is named.** `build` cannot know the reader's frame, so it lists
  every capture that draws the key. The runtime log still names the one
  drawn on a given frame.

## Commands

```sh
make capture-tool
python3 scripts/test_mep_build.py
make doc-checks
# E2E (scratch scripts, not versioned): record_one.sh <stem>; reset_pack.sh <stem>;
# e2e_one.sh <stem> <copy-line.json> <label> [<F14.2 .mss>]
```

Recordings, `.mss` files and ROMs stay outside the repository.
