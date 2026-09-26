# F14.11 — a recorded capture draws only the cells whose live key matches its record (ADR-0236, #499)

- Date: 2026-09-25
- Slice: PRD F14.11, ADR-0236 (**this is its first implementation**)
- Worktree: `~/f1411`, branch `feat/f1411-capture-cell-guard`, based on
  `origin/main` = `384c2d74c`
- Builds, and the provenance that makes the comparisons mean anything:

| arm | tree | `MesenCore.dylib` sha256 | `bgCellRecord` in the binary |
|---|---|---|---|
| `before` | `~/f1411-before` (worktree at `origin/main`) | `3c7ad303d03243f0bfce5c52b2a354b997dc15e201e8c56d8c385077d4a2fc4d` | 0 strings |
| `after` | `~/f1411` (this slice, rebuilt clean) | `6e8dcd06bb097bbf20175860d17ecefddf12d38c3de0dfca428a5579b2bcb42b` | 3 strings |
| `after`, final | `~/f1411` after the review fix of §6.1, rebuilt clean | `79bc8ea89987543ec2e1326f170ee456274554bb4340ca4af30f7f33db4ccfba` | 3 strings |
| `measure` | `~/f1411-measure` (`after` + `runs/f1411/instrument.py`) | carries `MESEN_TRACE_BG_LAYER` and `MESEN_TRACE_CELL_GUARD` | — |

**Rebuilt after the gates, and re-verified.** Running the final `make core`
recompiled the 13 TUs whose headers the mutation pass had `touch`ed (content
unchanged) and relinked, so the dylib's sha256 is now
`9869286a963986627c9fb5ef99557eb54780923c69b040a4a6966d1325147bf3`. Rather than
argue that a rebuild from identical sources is identical, both stop conditions
were **re-run on the rebuilt binary** and reproduce the numbers below exactly:
Contra80s `0x2362EC83` / screenshot `2a1eec7e…` on both arms (60 s of library
runs, 30 s here), Ninja Gaiden `0x26109C01` → `0xA67B1BC1` with the no-pack
control at `0xC70F580A`, 15 `<background>` captures / 15 records (the harness line reads "112": it counts the 56 `screen*.png` plus their 56 `.orig.png`) — **and again, unchanged,
after the review fix of §6.1** (`runs/f1411/ng-stage1-final.txt` on
`79bc8ea8…`). The tables below are from the earlier binaries; the crisp
measurements were replayed on each rebuilt one, and the 30-ROM sweep was not
re-run (it is a 30-minute pass and its inputs — the `before` arm's pack and this
slice's source — did not change). The §6.1 fix touches only which lines may close
the record binding, and every one of the 219 records the sweep measured is on the
line directly under its own `<background>` (60 packs checked, 219/219), so no
sweep number can move; the sweep's own packs are the `before` arm's, which carry
no records at all.

`before` is a *fresh clean build of main*, not `~/f1410-base`: that worktree sits
at `daf4a72e1`, and `HdPackBuilder.{cpp,h}` and `HdBuilderPpu.h` all differ
between it and `main`, i.e. its recorder is not main's recorder (main gained
#505, #520 and #524 in between). The `after` build is a clean rebuild — the
makefile tracks headers through `-MMD`, but the lesson of ADR-0217/0218 is that a
binary has to be *shown* to carry the change, so the check is both the sha256
and `strings | grep -c bgCellRecord`.

`before` and `after` record with their own build and are replayed by the
**`measure`** build, so the trace itself is not a variable: what differs between
the two trace arms is the pack, and what differs between the two recordings is
the recorder.

---

## 1. A bug this measured, and the fix (RED first)

The first `after` route run wrote 15 `<bgCellRecord>` lines and the loader
dropped **all 15**:

```
[HDPack - Line 436] <bgCellRecord> does not follow a <background> line; the record was dropped
[HDPack] <bgCellRecord> does not follow a <background> line; the record was dropped (15 occurrences)
```

`LoadHdPack: 24 ms; tiles=8722 keys=16567 images=38 backgrounds=15`, the
`MESEN_TRACE_CELL_GUARD` stream 0 bytes — no guard armed anywhere — and the 31 s
frame **byte-identical to the `before` build** (`0x26109C01` on both, the frozen
HUD). The whole slice was inert and nothing said so.

Cause: the loader cleared the binding at the top of *every* line, including the
one the record sits on, so `ProcessCellRecordTag` read `-1` by construction.

Raw RED: `runs/f1411/RED-loader-drops-every-record.log` (the recorder's own
`log` dump). Fix: `HdCellRecordBinder` in
`Core/NES/HdPacks/HdCaptureCellGuard.h` — a host-free state machine where
`Line()` is *both* the read and the clear, so the mistake is not writable by
accident — pinned by `TestTheLoaderBindsARecordToTheLineDirectlyAbove` and by the
`binding-cleared-early` mutation. The unit tests that existed before it all
passed with the bug present: they drove `HdCellKeyRecord::Parse`, never the
loader's binding.

## 2. Stop condition (1) — Ninja Gaiden `stage1-run`, 31 s

Route and state are F14.10's: `scripts/stages/ninjagaiden/stage1-run.txt` from
`deep-ng/runs/deep-ninjagaiden/mint/stage1-run.mss`, 60 s recorded, 49.1 s
(= 2 946 frames) replayed for the screenshot. `runs/f1411/ng_stage1.sh`, output
`runs/f1411/ng-stage1.txt`.

| arm | captures | records | `LoadHdPack` | 31 s frame checksum | SCORE / TIMER |
|---|---|---|---|---|---|
| `before` (main) | 15 | 0 | 19 ms; tiles | `0x26109C01` | **000100 / 145 — frozen** |
| `after` (this slice) | 15 | 15 | 26 ms; tiles | `0xA67B1BC1` | 000400 / **102** |
| none (no pack) | — | — | — | `0xC70F580A` | 000400 / **102** |

The HUD band of the three frames, stacked: `runs/f1411/ng/ng-hud.png`. `after`
is the live HUD and reads exactly what the no-pack control reads; `before` is
the frame `screen001` was captured from. **The `after` capture is still written**
— this slice does not refuse it, which is the whole difference from ADR-0235
option 2 (that option refused 87 of 219 captures library-wide to close #499; see
`docs/validation/f1410-probe-evidence-2026-09-25.md`).

What the guard did on that frame (`MESEN_TRACE_CELL_GUARD`, frame 4034 — this
tool's counter is absolute, F14.10's was state-relative; the scene is the same):

```
F 4034 p20 bg3 s0 masked=10 routed=10 vanilla=0 none=0 neutral=0 img0=1 img1=9
```

10 of the 960 cells of `screen004`-era capture were masked and **all 10 were
filled by a routed `<tile>` rule** (ADR-0236 §5): nine from one bitmap and one
from another, none the bootstrap's neutral ramp, none vanilla. `s0` = the
capture's scroll ratio is 0, so the whole-frame mask was the one counted.

## 3. Stop condition (4) — a hand-made pack renders byte-identically

The Contra80s spike pack, read-only source
`~/VSCodeProjects/MesenCE/roms/spike-contra80s/live-validate-test/out/mesen-home/HdPacks/Contra (USA)/`
— **3 007 `<background>` lines and 0 `<bgCellRecord>` lines**: deliberate
replacement art written by hand, which §3 says must not move by a byte.
`runs/f1411/contra80s_identical.sh`, output `runs/f1411/contra80s-identical.txt`.

| arm | pack loaded | frame | checksum | screenshot sha256 |
|---|---|---|---|---|
| `before` | 282 ms; tiles | 1803 | `0x2362EC83` | `2a1eec7e…` |
| `after` | 223 ms; tiles | 1803 | `0x2362EC83` | `2a1eec7e…` |
| no pack | — | 1803 | `0x9C31BC1D` | `fd239576…` |

The run is not vacuous: 512x480 (the pack draws), and **1 711 of 1 803 frames**
had an active `<background>` (`TitleScreenBackground.png`, priority 11, ~35 700
of 61 440 pixels differing from the live plane — `MESEN_TRACE_BG_LAYER`). The
`measure` build's `cell-trace.txt` for that pack is **0 bytes**: with no record
no guard is armed, which is §3 as a measurement rather than as a claim.

Two traps this cost, both worth keeping: the ROM link must be named
`Contra (USA).nes` (the folder Mesen looks the pack up under is the ROM *file*
name — as `Contra.nes` the run loads no pack and every arm agrees on a frame
none of them drew, which is exactly how the first attempt passed), and the
pack's own `backgrounds/` must come along (`cp -Rc`, APFS clonefile) or the
`<background>` lines are dangling and get dropped.

## 4. Stop condition (3) — the 30-ROM library

Same ROM set and the same 60 s power-on as `~/sweep30/runs/sweep30` (see its
`COMMANDS.md`); `runs/f1411/sweep.py record|trace`, `runs/f1411/score.py`.

**Captures written — the number that says this slice refuses nothing.**
`runs/f1411/captures-written.json`:

| | `before` (main) | `after` (this slice) |
|---|---|---|
| `<background>` lines | 219 | **219** |
| `<bgCellRecord>` lines | 0 | **219** (100 % of captures) |

Per game the two columns are identical line for line, including the two games
that capture nothing at all (`1942`, `Zelda II` — no static screen on a 60 s
power-on). ADR-0235 option 2 would have refused 87 of these 219 captures to close
#499; this slice refuses none of them and fixes the same frames with cells
instead, which is the whole reason the owner picked option 3.

**What "stale" measures, and one correction to the metric.** A frame is *stale*
when a capture draws on it with more than 2 000 of the 61 440 native pixels
differing from the live background plane — the floor for a capture's own frame is
870–930 px (the bootstrap image is a smoothed upscale, so edge pixels differ), so
2 000 is ~2× the floor. That is the sweep30 metric, unchanged.

It had to be made **guard-aware** for this pass: as `runs/sweep30` wrote it, the
trace counts every pixel where the capture's image differs from the live plane,
which is the layer's *potential*, not what reaches the screen. Left alone it
reported the library as `drawn` 69 016 and `stale` 2 995 **identically on both
arms** — a measurement that cannot see the slice it was built to measure. A pixel
the guard masks is not drawn, so the trace now skips it
(`runs/f1411/instrument.py`; the `before` arm's traces are unaffected because it
has no records, and the earlier trace files were discarded and re-run rather than
patched up).

**Scope of this pass, stated because it is narrower than the ADR.** Every
recorded capture has scroll ratio 0, so every armed guard in this sweep is the
whole-frame mask: of 6 798 armed-guard lines across the two arms, **0 carry
`s1`** (a scrolling background). The per-scanline branch in `OnLineStart` is
covered by unit tests only — no pack in the library, and none the recorder can
write today, reaches it.

Per game, both arms, `runs/f1411/score.py` → `library-tables.md`:

| game | `<background>` lines | records written | drawn `before` | drawn `after` | stale>2000 `before` | stale>2000 `after` | masked cells | routed `<tile>` | vanilla | no tile | neutral ramp |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1942 (1985) (Capcom) | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Bomberman (1985) (Hudson Soft) | 16 | 16 | 2811 | 2811 | 62 | 62 | 3147 | 3147 | 0 | 0 | 0 |
| Bubble Bobble (1987) (Taito) | 1 | 1 | 3359 | 3359 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Castlevania (1987) (Konami) | 21 | 21 | 3073 | 3073 | 593 | 353 | 164197 | 56677 | 0 | 107520 | 0 |
| Contra (1988) (Konami) | 3 | 3 | 976 | 976 | 38 | 0 | 33661 | 125 | 0 | 33536 | 0 |
| Donkey Kong (1983) (Nintendo) | 30 | 30 | 3579 | 3579 | 0 | 0 | 1447 | 1447 | 0 | 0 | 0 |
| Double Dragon (1988) (Technos) | 2 | 2 | 3572 | 3572 | 1750 | 0 | 630000 | 630000 | 0 | 0 | 0 |
| Dr. Mario (1990) (Nintendo) | 1 | 1 | 1027 | 1027 | 0 | 0 | 6144 | 6144 | 0 | 0 | 0 |
| Excitebike (1984) (Nintendo) | 2 | 2 | 1666 | 1666 | 56 | 56 | 2981 | 2981 | 0 | 0 | 0 |
| F-1 Race (1984) (Nintendo) | 5 | 5 | 2763 | 2763 | 3 | 0 | 317 | 317 | 0 | 0 | 0 |
| Gauntlet (1988) (Tengen) | 2 | 2 | 130 | 130 | 5 | 5 | 128 | 128 | 0 | 0 | 0 |
| Golf (1984) (Nintendo) | 7 | 7 | 3569 | 3569 | 0 | 0 | 1392 | 1392 | 0 | 0 | 0 |
| Ice Climber (USA, Europe, Korea) | 23 | 23 | 3260 | 3260 | 6 | 6 | 14664 | 14664 | 0 | 0 | 0 |
| Lemmings (1993) (Sunsoft) | 1 | 1 | 2462 | 2462 | 0 | 0 | 552 | 552 | 0 | 0 | 0 |
| Lifeforce (1988) (Konami) | 1 | 1 | 591 | 591 | 117 | 0 | 112064 | 0 | 0 | 112064 | 0 |
| Mario Bros. (1983) (Nintendo) | 3 | 3 | 3517 | 3517 | 28 | 28 | 964 | 964 | 0 | 0 | 0 |
| Mega Man (1987) (Capcom) | 1 | 1 | 3598 | 3598 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Mega Man 2 (1988) (Capcom) | 13 | 13 | 2453 | 2453 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Metroid (USA) | 11 | 11 | 3329 | 3329 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Mike Tyson's Punch-Out!! (1987) (Nintendo) | 9 | 9 | 2012 | 2012 | 3 | 3 | 15986 | 15986 | 0 | 0 | 0 |
| Ninja Gaiden (1989) (Tecmo) | 4 | 4 | 947 | 947 | 62 | 62 | 6528 | 6528 | 0 | 0 | 0 |
| Pac-Man (1984) (Namco) | 25 | 25 | 3245 | 3245 | 0 | 0 | 47226 | 47226 | 0 | 0 | 0 |
| Super Mario Bros. (1985) (Nintendo) | 1 | 1 | 1577 | 1577 | 21 | 21 | 3140 | 3140 | 0 | 0 | 0 |
| Super Mario Bros. 3 (1988) (Nintendo) | 4 | 4 | 513 | 513 | 230 | 7 | 130752 | 130752 | 0 | 0 | 0 |
| Tennis (1984) (Nintendo) | 5 | 5 | 3586 | 3586 | 0 | 0 | 42 | 42 | 0 | 0 | 0 |
| Tetris (1989) (Nintendo) | 17 | 17 | 3574 | 3574 | 0 | 0 | 4989 | 4989 | 0 | 0 | 0 |
| Tetris 2 (1993) (Nintendo) | 4 | 4 | 3500 | 3500 | 0 | 0 | 857 | 857 | 0 | 0 | 0 |
| The Flintstones - The Surprise at Dinosaur Peak! (1994) (Taito) | 3 | 3 | 3474 | 3474 | 1 | 1 | 0 | 0 | 0 | 0 | 0 |
| The Legend of Zelda (1987) (Nintendo) | 4 | 4 | 853 | 853 | 20 | 14 | 5008 | 720 | 640 | 3648 | 0 |
| Zelda II - The Adventure of Link (1988) (Nintendo) | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

**before** 219 `<background>` lines, 0 records, stale>2000 **2 995**.
**after** 219 records, stale>2000 **618** (−79 %), guard armed on 69 016 frames in
28/30 games (the two games with no capture have nothing to arm).

### Every game whose numbers move (7)

| game | stale `before` | stale `after` | recovered | what is left, and why |
|---|---|---|---|---|
| Double Dragon (1988) | 1 750 | **0** | 1 750 | — |
| Castlevania (1987) | 593 | **353** | 240 | 308 masked-but-not-enough + 45 masked=0 |
| Super Mario Bros. 3 (1988) | 230 | **7** | 223 | 7 masked=0 |
| Lifeforce (1988) | 117 | **0** | 117 | — |
| Contra (1988) | 38 | **0** | 38 | — |
| The Legend of Zelda (1987) | 20 | **14** | 6 | 14 masked=0 |
| F-1 Race (1984) | 3 | **0** | 3 | — |

(Double Dragon and Lifeforce are the two extremes of the same mechanism: their
stale frames are the multi-frame flicker of a screen whose tiles change every
frame — 1 750 and 117 frames — so the guard masks nearly the whole screen
(630 000 and 112 064 masked cells over the run) and the capture stops drawing on
the frames it was not taken from. Contra is the same story with 33 661 masked
cells. Castlevania's 353 and SMB3's 7 are the honest residue: the guard lowers
the diff but does not always cross the 2 000 px floor.)

**The 618 residual, grouped by *why* it survived** — this is the limit of the
slice, and it is the key predicate's limit, not a bug:

| category | frames | meaning |
|---|---|---|
| masked > 0, still > 2 000 px | 519 | the guard fired, but the cells whose key still *matches* the record already differ by > 2 000 px on their own: the capture shows through where the key cannot tell the frames apart (Castlevania 308, Bomberman 56, Excitebike 56, Ninja Gaiden 48, Mario Bros. 28, SMB 21, …) |
| masked = 0 on the whole frame | 99 | the run time's keys on that frame **are** the ones the capture was recorded from — the image is stale, but by the ADR's own identity it is the right image, so there is nothing to mask (Zelda 14, SMB3 7, Ice Climber 6, Bomberman 6, Punch-Out 3, Gauntlet 3, Flintstones 1) |

And the other direction, which is what says the guard is doing work rather than
sitting inert: **9 324 frames** in the `after` arm have masked > 0 and a diff
**below** the floor — frames the guard took pixels off, which the 2 000 px metric
never flagged in either arm (Donkey Kong 1 319, Double Dragon 1 750, Pac-Man
1 382, Ice Climber 1 122, SMB 764, …). Those frames are why `drawn` stays
69 016 while `masked` is 1 186 186: the metric counts *frames with any* drawing,
not pixels. No game has a **new** stale frame: in all 30, the `after` arm's stale
set is a strict subset of the `before` arm's.

The games that are armed and do **not** move are the 99-frame category at their
own scale — Bomberman (62), Excitebike (56), Ninja Gaiden (62), Mario Bros. (28),
SMB (21), Ice Climber (6), Gauntlet (5), Punch-Out (3), Flintstones (1) — plus
Bubble Bobble, Mega Man, Mega Man 2, Metroid, Dr. Mario, Golf, Lemmings, Pac-Man,
Tennis, Tetris, Tetris 2 and Donkey Kong, which had **0** stale frames to begin
with and still show masked cells (Donkey Kong 1 447, Pac-Man 47 226, Tetris
4 989, …): the guard edits frames the 2 000 px metric never flagged, which is why
`drawn` is unchanged (69 016) while `masked` is not 0.

**Ninja Gaiden is the counter-example that has to be stated**, because it is the
ROM #499 was opened on and its library number does **not** move (62 → 62). The
library run is a 60 s *power-on*, so its 62 stale frames are the attract/title
loop, where the capture's keys do change and the guard fires on 48 of them — but
the frame that carries the bug is the one stop condition (1) replays from the
route's state at 31 s, and that is `0x26109C01` → `0xA67B1BC1` (§2). The
tile-set mismatch is 10 cells of that screen, which is far under the 2 000 px
floor: **the library metric cannot see this slice's own bug fix**, and the
metric that can is stop condition (1).

**The regression control, and why the guard cannot be blamed for it.** The
`before` arm's own packs carry no record, so §3 says the guard is inert on them
— which makes "the `before` pack rendered by the `before` build" and "the
`before` pack rendered by the `after` build" a byte-identical pair unless this
slice changes the output of a pack it promised not to touch. That pair is also
the only thing that covers the shared predicate's refactor on a CHR RAM game
(Contra, `1942`, and the other CHR RAM titles in the set): the guard may not fire
there, but `HdPackTileAtPositionCondition` was rewritten to call
`HdCellKeyMatches`, so the gate's behaviour has to be shown unchanged, not
argued to be.

`runs/f1411/sweep.py identical 4` → `runs/f1411/identical/*/done.json`, rendered
by `runs/f1411/render_identical.py`. Both arms render the **`before`** arm's pack
(0 records in all 30), 60 s power-on, `capture` + `screenshot` + `mep-off`, each
run in its own home with the pack under the ROM's file-name stem:

| game | `before` build, checksum | `after` build, checksum | verdict |
|---|---|---|---|
| 1942 (1985) (Capcom) | `0xB3D7FD56` | `0xB3D7FD56` | same |
| Bomberman (1985) (Hudson Soft) | `0x4BCBD003` | `0x4BCBD003` | same |
| Bubble Bobble (1987) (Taito) | `0x53210F3E` | `0x53210F3E` | same |
| Castlevania (1987) (Konami) | `0xC5E3A82E` | `0xC5E3A82E` | same |
| Contra (1988) (Konami) | `0xFC0D4BE5` | `0xFC0D4BE5` | same |
| Donkey Kong (1983) (Nintendo) | `0x1FF851AA` | `0x1FF851AA` | same |
| Double Dragon (1988) (Technos) | `0x5173CD86` | `0x5173CD86` | same |
| Dr. Mario (1990) (Nintendo) | `0x5934724F` | `0x5934724F` | same |
| Excitebike (1984) (Nintendo) | `0xA39699F7` | `0xA39699F7` | same |
| F-1 Race (1984) (Nintendo) | `0xFC481E66` | `0xFC481E66` | same |
| Gauntlet (1988) (Tengen) | `0xBB6292CA` | `0xBB6292CA` | same |
| Golf (1984) (Nintendo) | `0x520BA660` | `0x520BA660` | same |
| Ice Climber (USA, Europe, Korea) | `0x773AC0DA` | `0x773AC0DA` | same |
| Lemmings (1993) (Sunsoft) | `0x5A26837A` | `0x5A26837A` | same |
| Lifeforce (1988) (Konami) | `0xDA9D1DF4` | `0xDA9D1DF4` | same |
| Mario Bros. (1983) (Nintendo) | `0xF4D11560` | `0xF4D11560` | same |
| Mega Man (1987) (Capcom) | `0x99800824` | `0x99800824` | same |
| Mega Man 2 (1988) (Capcom) | `0x448CA328` | `0x448CA328` | same |
| Metroid (USA) | `0xCAB75C6F` | `0xCAB75C6F` | same |
| Mike Tyson's Punch-Out!! (1987) (Nintendo) | `0x2BD7DD37` | `0x2BD7DD37` | same |
| Ninja Gaiden (1989) (Tecmo) | `0xF0564E45` | `0xF0564E45` | same |
| Pac-Man (1984) (Namco) | `0x2C3A691D` | `0x2C3A691D` | same |
| Super Mario Bros. (1985) (Nintendo) | `0xACFA1F7F` | `0xACFA1F7F` | same |
| Super Mario Bros. 3 (1988) (Nintendo) | `0x2FF4E0D4` | `0x2FF4E0D4` | same |
| Tennis (1984) (Nintendo) | `0x805E3374` | `0x805E3374` | same |
| Tetris (1989) (Nintendo) | `0x16769254` | `0x16769254` | same |
| Tetris 2 (1993) (Nintendo) | `0x4705A361` | `0x4705A361` | same |
| The Flintstones - The Surprise at Dinosaur Peak! (1994) (Taito) | `0x38D59889` | `0x38D59889` | same |
| The Legend of Zelda (1987) (Nintendo) | `0xA33A247C` | `0xA33A247C` | same |
| Zelda II - The Adventure of Link (1988) (Nintendo) | `0xBFFED082` | `0xBFFED082` | same |

**30/30 byte-identical.** Every run exited 0 and every run loaded a pack
(`LoadHdPack:` non-`no-pack`) — the pair is not vacuous on any title, which the
first attempt at stop condition (4) taught the hard way (§3). This also clears
the CHR RAM half of the library — the set is an even **15 CHR RAM / 15 CHR ROM**
split by iNES header (`CHR banks = 0`: Castlevania, Contra, Double Dragon,
Lemmings, Lifeforce, Mega Man, Mega Man 2, Metroid, Punch-Out, Ninja Gaiden,
SMB3, Tetris 2, Flintstones, Zelda, Zelda II) — where the refactored
`HdPackTileAtPositionCondition` compares 16-byte patterns instead of indices, so
its behaviour there is shown unchanged rather than argued to be.

## 5. Tooling: the tag travels

`mep_lint`, `mep_carry` and `mep_import` were each checked on a *real* recorded
pack, not on a fixture:

- `mep_lint` on the freshly recorded Contra and Ninja Gaiden packs: **0 error(s),
  0 warning(s)** (`runs/f1411/after-rec/…/auto/textures/hires.txt`, 3804 and
  12507 tiles).
- `mep_build`'s own lint pass on the imported project: 0 errors, 0 warnings.
- The whole pipeline, on the recorded Contra pack: `mep_import` → `mep_build
  build` keeps 3 `<background>` and 3 `<bgCellRecord>` lines in both
  `auto/textures/hires.txt` and `textures/hires.txt`, each record directly under
  its own `<background>`.

`mep_import` needed a one-line change and it is worth naming why: its
`_KNOWN_TAGS` is "every tag `HdPackLoader` reads, and no other", and it
**refuses** rather than drops an unknown tag. Left alone it would have rejected
every pack the recorder produces from now on. That is exactly the failure mode
ADR-0236's §"Spec and tooling" names, one tool further along than the ADR
expected.

**Found while checking that, since fixed upstream — and a conflict to expect.**
At this branch's base (`384c2d74c`), `mep_import` *also* refused
`<bgPreservesBehindBgSprites>` (ADR-0224) with the same error, so it could not
import a pack the recorder writes at all — the recorder emits that line on every
pack it produces. Reproduced on the recorded Contra pack:
`error: legacy/hires.txt:618: unknown tag <bgPreservesBehindBgSprites>; refusing
it rather than dropping whatever it says (ADR-0198)`. It was not filed (this
session must not write to GitHub); **`main` fixed it in #531** (commit
`910b7abb8`, `fix(mep_import): accept ADR-0224's
<bgPreservesBehindBgSprites>`), which adds that tag to the very same
`_KNOWN_TAGS` literal this slice adds `<bgCellRecord>` to:

```
main   "<bgm>", "<sfx>", "<patch>", "<bgPreservesBehindBgSprites>"}
here   "<bgm>", "<sfx>", "<patch>", "<bgCellRecord>"}
```

One line in `scripts/mep_import.py`, a textual conflict with an obvious
resolution — keep both. Nothing else in the two diffs overlaps.

## 6. Review pass (2026-09-25, Grok 4.6): the blank line, and the 1299/1307

### 6.1 A blank line was the one spelling the Core accepted and the tools refused

The spec (§8, **Form**) says nothing may separate a `<background>` from its
`<bgCellRecord>` — "not a comment, not another tag, not a blank line" — and
`mep_lint` errors and `mep_carry` drops on all three. The Core did not agree, in
*both* directions: the loader tested `lineContent.empty()` **before** it rolled
the binding, so an `LF` blank line kept the binding (the Core bound a record the
tools refuse) while a `CRLF` blank line cancelled it (a lone `\r` is not empty,
so that spelling reached the roll). Divergence in one direction is a pack that
lints clean and then loses its guard; in the other it is a guard the tools say
cannot exist.

Reproduced end-to-end on the real loader — four synthetic packs, identical but
for the line between the two tags, 3 s of Contra each,
`runs/f1411/blank_line_probe.sh`, raw in
`runs/f1411/RED-blank-line-probe.txt`:

| between the two tags | `before` the fix | `after` |
|---|---|---|
| nothing (`adjacent`, the control) | record **BOUND** | record **BOUND** |
| `# a comment` | record DROPPED | record DROPPED |
| one empty line, `LF` | record **BOUND** ← the divergence | record **DROPPED** |
| one empty line, `CRLF` | record DROPPED | record DROPPED |

The `after` column is re-run on the shipped binary (`79bc8ea8…`, `make core
capture-tool` clean) and is unchanged, `runs/f1411/GREEN-blank-line-probe-final.txt`.

The `adjacent` arm is the control that says the probe can bind at all: without it
a run where everything is dropped would read as a pass, which is how the first
attempt at this probe "passed" — the `<background>` line's first field is the
**PNG path**, not a name, so the line was failing to load and every record was an
orphan for the wrong reason.

**The fix, and why it is in the header.** The rule moved out of the call site
into `HdCellRecordBinder::Step`, which is both the roll and the blank test:
`if(!_cellRecordBinder.Step(lineContent)) { continue; }` replaces the loader's
own `if(lineContent.empty()) { continue; }` **and** the separate `Line()` call
made before the dispatch. The loader has one call per physical line and no
ordering left to write wrong — the same move as `Line()` being its own clear.
That is also what makes it testable at all: `HdPackLoader.cpp` is not in
`CUTSRC` (it pulls in `NesConsole`, `PNGHelper`, `ZipReader`), so a rule that
lives in the loop can only be unit-tested by a model of the loop, and a model of
a loop is the thing that passed with the §1 bug present.

`TestABlankLineEndsTheRecordBinding` drives `Step` exactly as the loader does —
the blank argument is the line *as the loader hands it over*, an empty string
for `LF` and a lone `\r` for `CRLF`, because `Step` runs before the CR strip —
and asserts the record line that follows takes nothing, for both spellings, with
an adjacent-lines control and a comment case beside them. 8 cases, all green at
1316/1316.

Mutation `blank-line-skipped-before-the-roll` (`Step` rolls only when the line is
non-empty — the loader's old skip, moved into the shared header where a test can
reach it): **1 F14.11 case red**, the `LF` one. The `CRLF` case stays green
under it, and that is the reason to have it: a lone `\r` was never the broken
spelling, and the test is there so the next fix cannot make it the broken one.
`runs/f1411/mutation-tests.txt`.

### 6.2 The 1299/1307 was the working directory, not this slice

Grok's clean rebuild scored the unit tests 1299/1307 with 8 `BlocoE` failures
(`mep_recipe.py apply`, `relative path sets differ (6 vs 0 files)`). Reproduced
from a directory that has `docs/` but not `scripts/`: **1299/1307, exit 1, the
same 8**, `runs/f1411/RED-cut-from-partial-cwd.log`. `BlocoB` passes there and `BlocoE`
fails, because `kFixtureDir` is relative (`docs/specs/golden/mep-recipe/fixture`)
and the recipe tests shell out to `python3 scripts/mep_recipe.py` — **relative
too**: the fixtures resolve, the script does not, and the C++ side writes 6 files
against Python's 0.

Not this slice, and not new. Both are checked without touching a build:

- `scripts/mep_recipe.py` is byte-identical to `main` (`git diff main --
  scripts/mep_recipe.py` is empty) and this slice does not modify it;
- `scripts/core_unit_tests.cpp` is **additions only** — `git diff -U0` shows no
  removed line, so no `BlocoE` case was touched;
- and origin/main's own binary, built clean in `~/f1411-before`, fails the same
  8 from the same directory: **1254/1262, exit 1**.

From the repository root both are green: this slice **1316/1316**, `main`
**1262/1262**. A different CWD entirely (`/tmp`) is worse than 8 failures — it
crashes on the first relative fixture (`exit 139`). So the suite has always had
"run me from the repo root" as an unstated precondition, and Grok's run met a
case the repo has never guarded.

## 7. Gates

Every command with its real exit code:

| gate | command | exit |
|---|---|---|
| core unit tests, clean rebuild of the `.cut.o` set | `make core-unit-tests` (`-Wall -Werror`) | 0 — **1316/1316 cases passed**, 0 `FAIL` |
| core + tool | `make core capture-tool` | 0 — dylib `79bc8ea89987543ec2e1326f170ee456274554bb4340ca4af30f7f33db4ccfba`, 3 `bgCellRecord` strings |
| python suite | `make python-tests` | 0 — **66 passed, 0 failed, 0 skipped** (52 s) |
| doc checks, including the LOC ratchet | `make doc-checks` | 0 |
| ADR references | `python3 scripts/checks/verify_adr_refs.py` | 0 |

`Core/NES/HdPacks/HdPackBuilder.cpp` is the only file near its ceiling (2 432 of
2 438 lines); no ceiling was raised and no file was split for this slice. The
builds are `/Library/Developer/CommandLineTools/usr/bin/make` with
`CXX=/Library/Developer/CommandLineTools/usr/bin/clang++ -isysroot
/Library/Developer/CommandLineTools/SDKs/MacOSX.sdk`. No `getenv` pragma was
added to shipped code (the only `getenv` calls are in the scratch measurement
tree, which is not part of the slice).

## 8. What this does not do

- It does not fix packs already written: none carries a record, and §3 is why
  that is not a regression — those packs draw exactly as they did before. The
  record is written from now on.
- ADR-0235's option 2 was **not** merged as code (no `SatisfiesProbe`,
  `RowPhase` or `CellOriginX` on `main`); ADR-0236 supersedes it on paper, and
  there was nothing to revert. The recorder's probe evidence is unchanged by
  this slice.
