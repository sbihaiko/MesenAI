# ADR-0221's acceptance test, as a tool — and a second occurrence it found

- Date: 2026-09-20
- Tool: `scripts/measure_capture_overdraw.py` (unit tests:
  `scripts/test_measure_capture_overdraw.py`)
- Related: ADR-0221, issue #339, ADR-0217 / ADR-0218,
  `docs/validation/adr0217-0218-anchor-gate-collisions-2026-09-20.md`
- Decision-neutral: this measures, it does not pick among ADR-0221's options
  A–E. The same number is the stop condition under every one of them.

## Why a cell metric and not a pixel diff

ADR-0221 states the acceptance test in pixels — the ROM draws 7 808
`STARRING` / `LITTLE MAC` pixels and the render has 0 — which is exact for that
one frame and does not generalise: a rendered pack frame is 4x native, so any
direct pixel diff against the unpacked frame measures the upscale rather than
correctness. The tool therefore works per 8x8 NES cell:

- **detail** — the ROM's own frame has 2 or more distinct colours in the cell,
  so the ROM is drawing something there;
- **erased** — detail holds and the render's corresponding 32x32 region is a
  single flat colour, so whatever the ROM drew is gone.

`erased` is deliberately conservative. A cell the artist repainted with *any*
variation does not count, so the tool under-reports rather than inventing
regressions; a cell the ROM itself draws flat can never be erased.

## Inputs

The 36-timestamp sweep already recorded for #339 — Mike Tyson's Punch-Out!!
from power-on, 1 s steps from 10 s to 45 s, rendered three ways (no pack, the
pre-change pack, the post-change pack). The two packs are route-matched: same
60 s recording, same 11 606 `<tile>` keys, differing only in their capture
gates. No re-recording was needed for this measurement.

```
python3 scripts/measure_capture_overdraw.py \
    --sweep <sweep-dir> --baseline-prefix none --render-prefix post
```

## Result

**12 of 36 frames lose ROM content; 437 erased cells in total.** The pre-change
and post-change packs produce byte-identical JSON, independently reproducing
the earlier finding that ADR-0217 / ADR-0218 do not touch #339.

| window | erased cells | what is happening |
|---|---|---|
| 10–18 s | 0 | before the card, and at the moment `screen003` was frozen |
| 19–27 s | 10 → 71 | the pre-fight card fills in; #339 as filed |
| 28–40 s | 0 | |
| 41–43 s | 18, 18, 27 | **a second occurrence, not previously reported** |
| 44–45 s | 0 | |

The 19–27 s ramp is the signature of the failure ADR-0221 describes: the
capture is exactly right at the moment it was frozen (18 s, 0 erased) and gets
progressively wronger as the ROM adds content the capture does not carry,
peaking at 71 cells — a superset of the 41 text cells, because Doc Louis's
undrawn white block fills in as well.

**41–43 s was missed by the manual pass.** The hand measurement targeted the
window #339 names and found it; a sweep that asks the same question of every
frame found a second capture doing the same thing 14 s later. That is the
argument for the tool over the procedure, and it means any option that fixes
only the pre-fight card has not finished the job.

## What this does not deliver

- **No cause for 41–43 s.** The tool reports which cells are lost, not which
  capture drew them or what its gate is. Attributing it needs the recorder's
  anchor summary, the way the pre-fight card was attributed to `screen003`.
- **No library-wide number.** Only Punch-Out!! was swept, because only its
  renders were already on disk. Running this across the 30-ROM bounded library
  is ~15 minutes of wall clock and would give the real scale of #339 — that is
  the "measurement budget" question ADR-0221 §5 leaves to a human.
- **Nothing about sprites.** A `<background>` does not cover sprites, so sprite
  content is never erased and never counted here. *(Wrong, as it turned out:
  the priority-20 layer does cover behind-background sprites, and 63 of the
  437 cells above are exactly that — see the 2026-09-22 addendum below.)*

## Addendum 2026-09-22 — two columns under the total (F12.15, ADR-0224 §4)

The total misled: it scores a cell "erased" when the render is flat where the
ROM had detail, without asking whether the detail was *background* or a
*sprite*. On the fight frames (41–45 s) every flagged cell is a
behind-background sprite — Glass Joe's shorts and legs, OAM attribute bit 5
set — painted over by the priority-20 `<background>` layer, which
`HdNesPack::GetPixels` draws after the behind-background sprite pass. No gate
rule can move that number; ADR-0221's "0 erased" was therefore unreachable on
this route under every option, and "the number to beat" in ADR-0223 had to be
argued by hand. The tool now takes `--grid <grid.txt>` (the recording the sweep
replays), locates each no-pack screenshot in the grid dump by background match
(the residual, sprite-covered cells, is printed so a bad match is visible),
and splits `erased` by the retained frame's own background plane:
**`erased background`** — the ROM's background tiles have detail in the cell,
the capture sits on content it lacks, the number ADR-0221/ADR-0223 gates are
judged on — and **`erased sprite`** — the background is flat there, so the
buried detail can only be a sprite, ADR-0224's renderer change. Sprite presence
is cross-checked against the frame's `M` RAM line (shadow OAM at `$0200`, which
also yields the behind-BG bit as the `behind-bg` column) or, failing that, the
ADR-0222 OAM dump — whose `<shape>,<x>,<y>,<pal>` entry carries **no attribute
byte**, so from that source `behind-bg` is `n/a` rather than invented.
`--verdict background` makes the exit code follow the first column, the F12.15
stop condition; the default stays on the total for the older logs. Re-run on
the same sweep (`--sprite-height 16`, Punch-Out!! draws 8x16 sprites; at 8 the
tool reports 22 `unattributed` cells, at 16 none):

| pack | window | erased | background | sprite | behind-bg |
|---|---|---|---|---|---|
| before = after | 19–27 s | 10, 17, 19, 33, 39, 52, 62, 71, 71 | same | 0 | 0 |
| before = after | 41–43 s | 18, 18, 27 | 0 | 18, 18, 27 | 18, 18, 23 |
| before = after | 44–45 s | 0 | 0 | 0 | 0 |
| proto (ADR-0223 B+probes) | 19–27 s | 0 | 0 | 0 | 0 |
| proto | 41–45 s | 18, 18, 27, 34, 44 | 0 | 18, 18, 27, 34, 44 | 18, 18, 23, 33, 44 |

ADR-0224's prediction holds: the card is background loss with zero sprite
loss, the fight is sprite loss with zero background loss; the prototype's 141
is 0 on the background column, so it passes `--verdict background` while the
shipped pack's 374 does not.

## Traps honoured

Both of ADR-0221's measurement traps are enforced by the tool rather than left
to the operator. A render that comes back at native 256x240 means no pack was
loaded — the pack was probably installed at `mesen-home/HdPacks/<stem>/`, which
`scripts/headless_record` does not find, instead of the sibling convention
`<romdir>/<stem>/auto` — and the tool raises instead of reporting a misleading
zero. And a with-pack run leaves the pack's own hundreds of PNGs below the run
directory; only files under a `Screenshots` folder are read as frames.

## Reproduce

```bash
python3 scripts/test_measure_capture_overdraw.py     # synthetic, no ROM needed
python3 scripts/measure_capture_overdraw.py <baseline.png> <render.png>
```

Exit code is the verdict: 0 when no frame loses ROM content, 1 when any does.
