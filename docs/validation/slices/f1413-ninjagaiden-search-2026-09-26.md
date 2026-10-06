# F14.13 — fixing the Ninja Gaiden search before calling Jev (ADR-0238 §2)

- Date: 2026-09-26
- Slice: PRD F14.13, ADR-0238 §2 (**this is its first implementation**).
  Go-ahead verbatim, on the ADR's Status line: *"implemente usando o
  deepseek"*.
- Worktree: `~/jev`, branch `feat/f1412-f1415-jev`, based on `539b609b4`
- Build: nothing rebuilt. F14.12's `InteropDLL/obj.osx-arm64/MesenCore.dylib`
  and `scripts/headless_record` are the binaries this slice ran on; no Core,
  `InteropDLL` or `headless_record` source was touched.

Every number below comes from one machine (8 cores, Apple silicon), on Ninja
Gaiden `(1989) (Tecmo).nes` (whole-file SHA1 `ca513f841d75…6bb9c`) from the
state `scripts/stages/ninjagaiden/mint-stage1.txt` writes, minted here at
frame 1082 into `runs/f1413/mint/stage1-run.mss` (`runs/` is not versioned).

## 1. The answer: the pin is passable, and the search passes it

| | before (F14.12's eight macros) | after (this slice) |
|---|---|---|
| furthest abs x in the state's act | 988 | **3 035** (11.9 screens) |
| windows to get past x 988 | never | 24 (frame 720) |
| sections entered | one | two (`$006D` 00 → 01, `$006E` 0 → 1) |
| where it stops | on the pin, forever | abs x 220 in the second section, `$0065` at 02 |
| `$0076` at the end | 2 | 2 |

The route file is replaced: `scripts/stages/ninjagaiden/stage1-run.txt` is 111
windows (3 330 frames, 55.4 s), against 120 windows (3 600 frames) before.

## 2. What the pin answers to, measured

From the state the search lands on at abs x 988 / camera 860 / y 205
(`runs/f1413/pre/pre.mss`), one window at a time in a session:

| window | what happens |
|---|---|
| `A` (29f) | nothing: y stays 205, the state byte stays $00 |
| `RA` (29f) | nothing |
| `R` (29f) | nothing (he is already against the wall) |
| `L` (29f) | nothing |
| `LA` (29f) | jumps left, and keeps jumping: a 45-px arc every 30 frames |

So the state answers to exactly one input, and it is not a jump *toward*
anything. The way out is the wall hop - jump **off** the wall and come back at
it: `4f LA` + `25f RA`, applied window after window, walks Ryu up:

```
base    abs=  988.00 y=205
hop  1  abs=  987.00 y=190
hop  2  abs=  987.00 y=177
hop  3  abs=  987.00 y=164
hop  4  abs=  987.00 y=151
hop  5  abs= 1000.00 y=144   <- over the wall
hop  6  abs= 1034.50 y=102
```

Four windows that gain **no distance at all** and 90 px of height. That is the
whole difficulty of the slice: `rank()` reads distance first, so every one of
those windows ties with the window that stands still, and on a tie the window
whose parent was played first wins. Nothing in the ranking can tell the climb
from the pin.

## 3. What was built

**Four new candidates, with fixed durations** (`route_search.CANDIDATES`), and
a candidate is now a *run* of parts rather than one hold - a Ninja Gaiden jump
is edge-triggered and the wall hop is two moves:

| label | window | what it is |
|---|---|---|
| `LJ` | `29f LA` | the Left+A jump the pin needs |
| `WH` | `4f LA`, `25f RA` | wall hop: off the wall, then back at it |
| `WH5` | `5f LA`, `24f RA` | the same hop, one frame later (also measured to climb) |
| `LJA` | `4f LA`, `25f A` | the jump with the button held through the window |

No candidate holds Up or Down (a test pins that). `window_lines(action, chunk)`
turns a candidate into script lines and refuses parts that do not fit the
window; `flat_window` still adds the idle frame a one-shot boundary needs. A
`--chunk` too short for a wall hop's fixed frames drops those candidates with a
note in the log (`playable_candidates`) rather than crashing the hop that
reaches one - a shorter window is a legitimate thing to try on another stage.

**A beam that can keep a window which does not improve** (`choose_beam`,
`fingerprint`, `STICKY_HOPS`). Three rules, in this order:

1. the best `beam - 1` windows go through on rank, preferring different
   parents (F14.12's rule, unchanged);
2. the **last slot is reserved** for a window in a state the beam does not
   already have (a fingerprint: position rounded to 8 px, camera screen, y,
   room, hit points) - and a window that ends where the beam already stands,
   or where its own parent stands, is not a way out and does not take it;
3. that line is **followed**: for up to `STICKY_HOPS` (8) hops, a window whose
   parent was the reserved pick is preferred for the slot, even when it ranks
   last. This is what carries the four climb windows. Past the budget - or with
   no window reaching a new fingerprint - the slot falls to the next by rank,
   so the beam never shrinks for diversity's sake.

   The reserved slot is spent on **every** hop, not only on hops that look
   stuck: that is what the rule costs (12/15 against the archive's log, §3) and
   what it buys (the pin passed at hop 25). Narrowing it to hops whose rank
   picks are all one fingerprint was measured to climb the wall two hops later
   and less reliably.

Rules 2 and 3 were not a guess: they are what the first two runs lacked. The
`--only`-style history of the three runs is in §5, and the first of them is the
one that shows why rule 3 is needed - the beam kept the *shallowest* climber
every hop, so the climb restarted at the pin each time and never got deep enough
to clear the wall.

The log line gained a `y=` field for the same reason: without it a climb is
invisible in the log, because a climb is four identical `abs=987` lines.

`scripts/measure_step_emu.py` learned the candidate's new shape (one candidate,
one window, unchanged in cost) and now runs the ported search with
`--only <the scratch driver's eight>` when it diffs against the archive's log:
that log is the eight-macro search, F14.13 added four more, and comparing the
new set against it would measure the difference between two searches. Its
standings parser also had to take the log's new `y=` field, which it now treats
as optional and does not compare - the same rule it already applied to the
column widths.

**That comparison now reads 12/15, not F14.12's 15/15, and the difference is
this slice's beam rule.** With the scratch driver's eight macros and nothing
else changed, the ported search parts company with the archive's log at hop 5:
the reserved slot spends the last beam slot on a window in a state the rest of
the beam does not have, where F14.12 spent it on the next window by rank. The
route is unaffected in what it *reaches* - the replay in §5 is the check that
matters for the artifact - and a narrower version of the rule (fire the
reserved slot only on hops whose rank picks are all one fingerprint) was
measured to keep 14/15 while climbing the Act 1-1 wall two hops later and less
reliably (`runs/f1413/verify-climb`, not versioned): it was rejected. F14.12's
15/15 is a statement about F14.12's code and stands; what this slice owes is
saying that it no longer holds.

## 4. The run

```sh
python3 scripts/route_search.py --rom "$NG_ROM" \
    --state runs/f1413/mint/stage1-run.mss --work runs/f1413/run \
    --out runs/f1413/run/route.txt --log runs/f1413/run/log.txt \
    --hops 120 --chunk 29 --beam 3 --sessions 8 \
    --state-out runs/f1413/run/end.mss
```

- **It passes abs x 987/988.** Hop 23 ends on the pin (988.00, y 205); hop 24 is
  the first hop window (988.00, y 205 → the climb starts inside it); hop 25 reads
  **999.00, y 144**; hop 26 is 1042.50 and the route is walking right again.
- **How far it gets:** peak abs x **3 035.00** at hops 90-97, then at hop 98 the
  section changes (`$006D` 00 → 01, `$006E` 0 → 1) and the position restarts at
  219. It runs on to hop 112 and stops there: at hop 113 every candidate loses a
  life, so `ranked` is empty and the search stops as designed. Its last state is
  abs x 217, y 166, `$0065` 02, `$0076` 2.
- **Wall time: 77.9 s** (`time` reports `1:17.86`, 512 % CPU) for 112 hops x 3
  beam slots x 12 candidates = **4 032 candidates**, on `--sessions 8`. The same
  run at the eight old macros and 23 hops took 10.5 s.
- **Candidates evaluated per hop:** 36 (3 slots x 12), against 24 before.
- The flat script the run wrote is 236 lines / 3 360 frames / 55.91 s; the
  committed route is that script cut at window 111 (3 330 frames), because the
  112th window is the one that ends with a death (§6).

## 5. The route replays, deterministically, and matches the search

`runs/f1413/verify_route.py` truncates the flat script at whole windows, runs
each truncation **twice** through `headless_record` (one-shot, `input=`,
`mep-off hdpack-off`) and compares all 2 048 bytes of internal RAM:

```
  10 windows (  300 frames): abs=  448.00 y=208 hp=0F lives=2 stage=00 room=0 | two runs identical RAM: True | the search said ('R', 448.0, '0F', 2)
  20 windows (  600 frames): abs=  859.00 y=192 hp=0F lives=2 stage=00 room=0 | two runs identical RAM: True | the search said ('RB', 883.0, '0F', 2)
  24 windows (  720 frames): abs=  987.00 y=149 hp=0F lives=2 stage=00 room=0 | two runs identical RAM: True | the search said ('R', 988.0, '0F', 2)
  30 windows (  900 frames): abs= 1170.50 y=180 hp=0D lives=2 stage=00 room=0 | two runs identical RAM: True | the search said ('B', 1169.5, '0D', 2)
  60 windows ( 1800 frames): abs= 2071.00 y= 96 hp=0B lives=2 stage=00 room=0 | two runs identical RAM: True | the search said ('WH', 2071.0, '0B', 2)
  90 windows ( 2700 frames): abs= 2934.50 y=159 hp=04 lives=2 stage=00 room=0 | two runs identical RAM: True | the search said ('R', 3035.0, '04', 2)
 110 windows ( 3300 frames): abs=  220.00 y=208 hp=02 lives=2 stage=01 room=1 | two runs identical RAM: True | the search said ('R', 220.0, '02', 2)
```

Three facts: every checkpoint is byte-identical between two runs; the route
passes the pin (window 25 is 1005.00, y 144) and works in the second section
(window 110, `$006D` 01); and the positions agree with the search's own log
**exactly at windows 10, 24, 60 and 110**, and differ at 20, 30 and 90.

That difference is not drift and it is not the route: the log is a pre-#543
artifact. `route_search.py` reads RAM once per candidate, and before #543 that
read played one emulated frame, so the search's hop *k* is a state the flat
script only reaches later - by one pixel at windows 20 and 30 (the character is
against a wall or at the top of a jump, where a frame of input does not move
it) and by 100.50 at window 90, where it is running. The committed route is
byte-identical to `runs/f1413/run/route-111.txt`, the search's own 111-window
prefix, and replayed flat it passes the pin and reaches the second section
either way. What the log records is where the search *was*, not where the
script goes, and #543 is why the two had come apart at all.

**The route replaces `stage1-run.txt`.** It goes 2 047 px (eight screens) and one
whole section further than the committed one, replays deterministically, and
keeps `$0076` at 2 for all 3 330 frames. The old route's behaviour is kept in
the file's header: it reached abs x 988 by frame 690 and held the act there for
48 s with 120-frame direction holds, because the eight macros have nothing that
leaves the pin.

## 6. Open problems and what the next slice should know

- **The route ends one window short of a death.** The search stops at hop 113
  because every candidate there loses a life (`$0065` is 02 entering it); the
  committed script is cut at window 111, the last window whose state is still
  alive. The 112-window form of the same script replays to `$0076` 1: Ryu dies
  in the second section, and no candidate the search has survives that spot.
  That stall is F14.14's second candidate case after the pin - and the pin
  itself is now passed, which is what the ADR said would decide whether Jev
  has a Ninja Gaiden case at all (ADR-0238, Consequences).
- **A `ram` request advanced the emulated frame by one, so a read was not
  inert** (filed as #543; **fixed in F14.14**). Measured before the fix: after
  `play(1, "R")`, `frame()` answered 1084 four times in a row, then
  `read_ram(0x86)` left `frame()` at 1085. `HeadlessReadNesRam`, the `save`
  verb and the state export now leave the frame where the `run` parked it.
  Closing it exposed the arithmetic the read had been hiding, and that is the
  part F14.14 is built on: a `run n` covers `n` frames after a state a `run`
  ended on and `n + 1` after a `loadfile`, and a one-shot's `<seconds>` budget of
  `F - 1` is the session's `run_exact(F)`. `StepEmu.run_exact`/`play_window` are
  the one place that asymmetry is handled. `scripts/verify_chain.py` puts all
  three arms - one-shot, session-flat and windowed chain - against each other on
  all 2 048 bytes of RAM at three checkpoints, each arm run twice: **0
  failures** on this route and on both Mega Man 3 routes.
- **A windowed session replay of a multi-part script used to take a different
  play; it is the same play now** (**fixed**, same change). Before the fix,
  replaying the committed script with one `load_script` per window peaked at abs
  x 1867.50 and never changed section, where the one-shot run of the same script
  peaked at 3 035.00 and did. The cause was the window boundary: a window is 29
  input frames plus the idle frame a one-shot appends past its script, and the
  pre-fix `ram` read had been playing that frame by accident. `route_search.play`
  and `jev_harness._play_macro` now *write* the boundary frame into the window
  and *play* it (`StepEmu.play_window`), so the chain is the script's own play
  and the committed scripts stay valid unchanged. The window-30 arms agree to
  the byte with the one-shot - abs x 1170.50, y 180, state 47 - with or without
  a `ram` read after every window (`scripts/test_step_emu_rom.py`).
- **`rank()` is distance-first and knows nothing about height or section.** The
  reserved slot is what carries a climb; a stall that needs two different
  non-improving lines at once would want a wider beam (the run above is
  `--beam 3`). The section change at hop 98 also resets the position, so the
  beam briefly holds a 3 035 state and a 219 state as if the first were further
  along - it worked out here (the section line was followed) but a rank that
  read `$006D`/`$006E` first would be the honest fix.

## 7. Commands

```sh
# the pin, reproduced and passed
python3 scripts/route_search.py --rom "$NG_ROM" \
    --state runs/f1413/mint/stage1-run.mss --work runs/f1413/run \
    --out runs/f1413/run/route.txt --log runs/f1413/run/log.txt \
    --hops 120 --chunk 29 --beam 3 --sessions 8
# the route replays deterministically, twice per checkpoint
python3 runs/f1413/verify_route.py scripts/stages/ninjagaiden/stage1-run.txt \
    runs/f1413/mint/stage1-run.mss runs/f1413/verify 111 runs/f1413/run/log.txt
# no ROM needed
python3 scripts/test_route_search.py     # 58 checks, exit 0
```

`make doc-checks` lists the new test file. The scratch drivers this log cites
(`runs/f1413/*.py`) are not versioned, like the states they read.
