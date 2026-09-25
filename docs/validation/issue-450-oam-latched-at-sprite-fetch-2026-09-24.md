# Issue #450: the recorder's OAM snapshot is latched at sprite fetch (2026-09-24)

Castlevania's sprite sheets had cells naming keys the frame never drew. This
log records the cause, the fix and what it was measured against. It is based
on branch `feat/f149-adr0230-colourways` (PR #461). It does not change the
recorded format and does not contradict an ADR.

## Cause

- `HdBuilderPpu::CaptureOam` decoded every sprite at frame end, in
  `OnBeforeSendFrame` at scanline 240. It read OAM, PPUCTRL's sprite size and
  pattern table, the CHR mapping and palette RAM at that point.
- A spike logged PPU register writes during the stage 1 run from
  `stage1.mss`. In PPU frame 659, the retained OAM frame 297 of the stream
  (the doc's "frame 658"), the game writes **PPUCTRL=$00 at line 0, cycle 240
  and PPUMASK=$00 at line 0, cycle 252**. This is the cut from the gate screen
  to stage 1. It was the only frame in the first 12 s whose sprite-enable
  state ended before line 239.
- #183's gate samples PPUMASK per drawn pixel, and sprites were on for the
  first ~250 pixels of line 0, so it admitted the frame. No sprite row is drawn
  on line 0, and every later sprite fetch happens with rendering off, so the
  frame drew no sprite at all.
- The frame-end decode then read the game's 8x16 OAM with PPUCTRL in 8x8 mode.
  The result was 23 single 8x8 halves, against 46 and 42 in the neighbouring
  frames. 7 of their (tile, palette) keys carry no `<tile>` line.

## Fix

- New host-free header `Core/NES/HdPacks/OamFetchLatch.h`. HdBuilderPpu calls
  it once per visible scanline, at cycle 257 (the start of the sprite fetch),
  with the state at that moment.
- A sprite half is latched the first time one of its rows is fetched with
  rendering on and PPUMASK sprites enabled. The latch decodes it with the
  PPUCTRL, CHR mapping and palette of that fetch.
- `OnBeforeSendFrame` hands the latched halves to `RecordSprite` in the same
  order as before: OAM index, then the top half first.
- It still reads OAM, not secondary OAM, so sprites hidden by the 8-per-line
  limit or by background priority are still recorded (ADR-0153 §2).
- A save-state load clears the latch, because halves latched before the load
  belong to a frame the loaded timeline never drew.
- A frame that holds its PPU state steady records exactly what the frame-end
  decode recorded. #183's case (power-on, rendering disabled) still records
  nothing.
- `HdPackBuilder.cpp` is untouched. It is still 2438 lines, at its ADR-0137
  ceiling.

## TDD

Four cases were added to `scripts/core_unit_tests.cpp` (`Issue #450: …`). The
red step ran them against the header's first version, which was a model of
the pre-fix decode: the last state it was handed (the frame-end state) decodes
every sprite, behind a frame-wide "sprites seen" gate. Output, verbatim:

```
PASS  Issue #450: a frame whose rendering stopped before any sprite fetch records no sprite
FAIL  Issue #450: an 8x16 sprite drawn before a mid-frame PPUCTRL switch keeps both halves, and an undrawn one is absent: got (40,21,1110) (90,151,1220) 
FAIL  Issue #450: a partly drawn sprite is recorded and a sprite above the enable line is not: got (8,11,0300) (8,19,0310) (16,101,0300) (16,109,0310) 
PASS  Issue #450: a steady 8x16 frame records the frame-end decode unchanged
PASS  Issue #450: a steady 8x8 frame records the frame-end decode unchanged
1097/1099 cases passed
```

- The first case passes on that model. The model can only sample PPUMASK at
  cycle 257, not per pixel as #183 does, so it cannot express the exact
  Castlevania line-0 write. That case is proven red by the E2E baseline below
  and by the mutation.
- After the fix: 1099/1099 cases passed.
- **Mutation.** Dropping the `spritesDrawn` gate in `OnSpriteFetch` fails 3
  of the 4 cases (1096/1099). The steady-frame case holds, as it should.

## E2E (Castlevania stage 1, 80 s from `stage1.mss`, `bootstrap hdpack-off`)

`stage1.mss` was minted with `mint-stage1.txt` and is sha1-identical to the
deep measurement's state. The baseline recording reproduces the deep
measurement's `hires.txt` exactly (sha256 `1b84b252…`).

| | Baseline | Fix |
|---|---|---|
| `sheet_keys_audit.py`, gameplay | 598 entries, **13 leftover** (7 keys) | 590 entries, **0 leftover** |
| `mep_build build`, gameplay | 452 carried, **7 new key(s) the sheets brought** | 452 carried, **0 new** |
| OAM stream, gameplay (4 096 frames, cap) | — | frame 297 (23 entries) gone; every other frame identical after resolving ids to (tile, palette, x, y); one more frame retained at the cap's end |
| `hires.txt`, gameplay | 4 731 lines | 4 731 lines, 3 033 tile keys as before (mep_build); 24 lines differ, all `spr030`/`spr036` sprite-group conditions (ADR-0189) the bogus frame had shaped |
| Idle 60 s from power-on | 551 entries, 0 leftover | **byte-identical pack** (354 files) and OAM stream |
| Determinism | — | two fix passes byte-identical (pack and OAM stream) |

## Binary

- Base: `MesenCore.dylib` sha256 `6172ea78…f1e2`. Fix: `569fcd6a…d309`.
  Final, with the load-time clear: `8a3b9e27…b0d6`. The final binary
  re-recorded both runs. Gameplay is byte-identical to the fix run (pack and
  OAM stream), and idle is byte-identical to the baseline.
- Both are private copies. `headless_record` is relinked to the matching dylib
  and ad-hoc signed.
- `nm` on the base finds `CaptureOam` and no latch symbol. On the fix and the final binary it finds
  2 `OamFetchLatch`/`LatchFetchedSprites` symbols and no `CaptureOam`.
