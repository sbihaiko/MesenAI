# Per-stage recording scripts (F9.22)

Input scripts for `scripts/headless_record` (`<count>f <buttons>` lines, or
`<count>f <port1>|<port2>` when the second player has to move — F9.22), one
folder per golden game. Four kinds:

- `mint-<stage>.txt` — plays from power-on to the start of a stage. Run with
  `save-state=<stages-dir>/<stage>.mss` to mint the state that stage's
  recording starts from.
- `<stage>-probe.txt` — a *probe* (F9.23, ADR-0181 §3): from the stage's
  state, hold one direction for at least two turns of the figure's cycle
  plus a phase, release, idle, and repeat, without scrolling the screen. It
  is the run the recorder can read a cycle's `driver` off; the stage and
  entry scripts hold one button most of the time and attribute nothing. Run
  it with the stage state copied beside it as `<stage>-probe.mss`.
- `<stage>.txt` — plays *from* the state for <= 60 s (<= 3600 frames, the
  batch's default duration; a longer script is cut where the run ends), holding a direction long
  enough for every loop to complete two turns on one track (ADR-0179 §3 needs
  `repeats >= 2`; a Contra turn is 6 x 8 frames, so `90f R` is 1.9 turns and
  `240f R` is 5).
- `<a>-to-<b>.chain.txt` — the headless chain that produced state `<b>`
  from state `<a>`, flattened into one script (every step's input, then the
  idle frames its run added past the script). Replay it with
  `scripts/replay_chain.sh <rom> <a>.mss <chain> <b>.mss`; the state it
  writes is byte-identical to the one the chain minted, which is how the
  states below `stage3-waterfall` are reproduced from a checkout that has
  the `.mss` files of its predecessors. `record_stages.sh` skips these and
  the `mint-*.txt` files, so a game folder can be copied wholesale into a
  stages dir. A chain step may be `Ns`; the helper converts it the way the
  script parser does (`round(N * 60.0988)`).

Mint, then batch:

```sh
scripts/headless_record <rom> <seconds> <work>/mint input=scripts/stages/contra/mint-stage1.txt save-state=<work>/stages/stage1-run.mss
cp scripts/stages/contra/stage1-run.txt <work>/stages/
scripts/record_stages.sh <rom> <work>/stages <work>/by-stage 60
```

The `.mss` files are not versioned: a CHR RAM state carries the game's
graphics. `mint-stage1-30lives.txt` types the Konami code at the Contra title
(30 lives); the later stages are reached headlessly from it.

## Reaching a later stage without a human

A stage is played as a chain of short `headless_record` runs, each loading the
previous `.mss` and saving the next, steered by RAM read off the state with
`scripts/mss_ram.py <file.mss> [addr...]`. For Contra: lives `$0032`, screen
index `$0064`, stage `$0030` (0 = stage 1), player X/Y `$0334`/`$031A`, player
state `$0090` (1 alive, 2 dying), the stage-1 core's HP `$0585` (32, one per
hit); the fine scroll is `((ppu.tmpVideoRamAddr & 0x1f) << 3) | ppu.xScroll`.
Two search shapes were enough on 2026-09-12: a greedy explorer over ~20
candidate windows per hop (`Nf RB` then a jump then `RB`), keeping the window
with the largest `screen * 256 + scroll` that lost no life, crossed stage 1;
a depth-first search over 60 f windows (prone burst, standing burst, jump,
aim up, step left/right, wait) with survival as the constraint and the core's
HP as the objective beat the wall in four prone windows. What the search
found and a human would not guess: from the small platform in front of the
core only prone shots are at its height — standing shots pass above it from
the platform and below it from the ground. The chain and every intermediate
state live under `runs/golden-20260912/contra/play/` (unversioned); the
minted states are `stages/stage1-boss.mss` (the wall, with Bill respawning on
the top-left platform) and `stages/stage2-base.mss` (the corridor, Bill
spawning). A save-state boundary *is* input-neutral, but a run is longer
than its script: the recorder rounds the frame target up (`(F + 2) / fps +
0.05 s` in the solvers, five frames) and overshoots it by one, so every
step leaves six idle frames the next state carries. The 2026-09-12 test
that ran a 600 f prone script in one piece and in ten 60 f pieces and saw
them diverge after ~100 f was missing those frames; with them written into
the flat script (`Nf -` after each step), a 287-step chain of 33 020 frames
replays to the same RAM byte for byte (2026-09-13, both `.chain.txt` files
below, `scripts/mss_ram.py --diff` empty).

Measured 2026-09-12 (60 s from each stage-1 state): Contra 2 cycles (the
player's period-6 run on 10-tile poses and the soldier's on 8-tile ones),
Mega Man 3 7 (the run as `001 002 001 003`, period 4 with the middle frame
twice, 34 repeats), Zelda 1 15 (Link's two-frame walk in every direction,
hold 6), Excitebike 8 (the wheels, 398 repeats). Cycles with a period above
6 and a hold near 100 are echoes of the script's own loop, not animations.
From the two later Contra states the same day: `stage1-boss` 69 poses, 3
cycles (all period 2, a pose alternating with its muzzle-flash variant);
`stage2-base` 163 poses, 10 cycles — the base soldier's run as three period-3
cycles of 8-tile poses with hold 4, plus a period-4 cycle of 6–8-tile poses.

Since ADR-0181 §1–§2 (2026-09-12) `poses.json` carries an `input` block:
`held` frames per button and `never`, the buttons and direction+action pairs
the run never held at once. That is the check on a `<stage>.txt`: Contra's
`stage1-run` says `never: Select, Start, Left, Up+A, Down+A, Down+B`, so its
sidecar cannot hold the aim-while-jumping or prone-shooting states, and a
script that wants them has to press them. Two env-gated save-time dumps back
a measurement: `MESEN_OAM_STREAM_DUMP` (retained frame, repeat, port 1 and 2
button bytes, then `node,x,y` per sprite) and `MESEN_POSE_TRACK_DUMP` (one
ADR-0179 track per line as `frame:pose:held`).

## Probing which cycle answers the pad (F9.23)

`stage1-probe.txt` per game, 60 s from `stage1-run.mss` (2026-09-13), read
with the interruption rule (>= 4 windows, >= 2/3 stopping within 12 frames
of a release on the port): Zelda `60f D / 30f - / 60f R / 30f - / 60f U /
30f - / 60f L / 30f -` — Link's four walks `port1` (10/10, 10/10, 9/10,
10/10 windows). Mega Man 3 `100f R / 30f - / 100f L / 30f -` — the run
`port1` (12/13); two fast 9-tile enemy cycles 0/13 and 2/13, none.
Excitebike `120f A / 60f -` — the wheels 3/18, none, as the rule must (a
release of A does not stop them). Contra `104f R / 30f - / 104f L / 30f -`
from the stage-1 start — the player's run right and left `port1` (12/12
each), the soldier's run 1 window, none.

What the Contra probe took six shapes to learn: a turn of the run is 6 x 8
frames and a window needs two turns plus the partial first phase, so 40 f
and 96 f holds yield no window and 104 f does; walking Right 120 f from
the start scrolls the screen and every scroll brings soldiers that fuse
with the figure and end its track (one or two windows in twenty holds),
while 104 f Right then 104 f Left oscillates on the first screen with
nobody in the way; Left from the start (x = 48) drops Bill in the water,
where he does not animate; and a "clean" screen reached by letting the
soldiers kill him is the title screen. The rule itself needed one fix
from the measurement: a window stops at its last advance plus that phase's
median hold, because Link parks on a walk frame and the run's own held
frames would put the stop 30 f after the release.

## Water and the second player (F9.22, 2026-09-13)

Two more stage-1 states, both minted headlessly from `stage1-run.mss` or
from power-on: `stage1-water` (`mint-stage1-water.txt` = the stage-1 entry of
`mint-stage1.txt` then `60f L`, self-contained from power-on; Bill drops
into the water at x = 25, y = 212) and
`stage1-2p` (`mint-stage1-2p.txt` = the 30-lives code, then Select for
"2 players", then Start; both figures alive, Bill at x = 48 and Lance at
x = 32). The second player is the reason the input script grammar gained a
port-2 token — `<count>f <port1>|<port2>` — and `headless_record` plugs a
pad into port 2 only when a line names one (ADR-0157, amended).

Measured, 60 s each: `stage1-water` 13 poses, no cycle (Bill swims and
shoots without animating — a swim, a submerged and an aim-up pose and their
shot variants); `stage1-2p` 311 poses and 11 cycles, none with a driver
(both pads hold Right), among them two period-6 cycles of 20-tile poses
that are the two figures running side by side as one fused pose (ADR-0177),
and 58 poses of 22 tiles that are the pair standing adjacent at the spawn.

`stage1-2p-probe.txt` — `120f R|-` once, then `104f -|R / 30f - / 104f -|L
/ 30f -` — reads Lance's run right and left as `port2` (13/14 and 12/12
windows). Two lessons behind that shape. **Bill and Lance share every
pose**: the vocabulary is shape-keyed and the two differ by palette only,
so their runs are one cycle, and a probe that moves both in turn reports
7/13 port 1 and 6/13 port 2 — no driver, which is what the rule says about
a cycle two pads drive. The versioned probe therefore parks Bill out of the
way (x = 168, no scroll: the screen holds until at least x = 168 in two-
player mode) and moves only Lance, so `port2` is measured end to end.
**The figures must not cross**: Lance spawns left of Bill, and running him
right through Bill fused the pair for the first 40 frames of every hold,
leaving 64 clean frames — under the two turns a window needs — so the first
shape produced no port-2 window at all.

A run from a state counts its <seconds> and its script from the state's
frame (`headless_record` prints both); before 2026-09-12 both were absolute
emulator frames, so a state older than the run ended it on the spot.

## The base, the waterfall and its boss (F9.22, 2026-09-13)

Stages 2 and 3 were beaten headlessly the same way stage 1 was — chains of
short `headless_record` runs steered by RAM — and three more states came
out of it: `stage3-waterfall` (the vertical stage, 17 lives), `stage3-boss`
(the alien face at its top, arms active) and `stage4-base` (the second
base, 15 lives). The chain and every intermediate state live under
`runs/golden-20260913-f922/contra/` (unversioned).

**The base (stage 2).** Its RAM: `$0086` wall cores remaining, `$0037`
screen cleared (bit 7 set once the fence is gone — hold Up for 300 f to
walk in), `$003b` boss defeated, sixteen enemy slots with type at
`$0528+i`, HP at `$0578+i` (a value >= 0xF0 is a phase flag, the low
nibble is the HP), X at `$033e+i`, Y at `$0324+i`. The targets are the
static slots: cannons type 19 (HP 4) and the core type 20 (HP 8, 16 on the
fourth screen). Bullets travel into the screen from wherever Bill stands,
so the search walks to the target's lane (1 px per frame) and shoots
standing or prone; from x = 80 a burst also reaches the core. The boss room
has four pods (type 10, HP 10), then two eyes (type 8, hit with Up+B from
below at x = 176 and x = 80), then the head (slot 15, type 16, Up+B from any
central lane). Soldiers kill Bill, so the depth-first search keeps survival
as a constraint and spends a death budget only when no surviving window
progresses; a long Up+B run at the head also killed him, so windows stay at
60 f. Two things the RAM taught: a slot is reused by transient enemies, so
a target is (slot, type), not a slot; and the head's HP nibble is not
monotone, so the search reads `$003b` for the win.

**The waterfall (stage 3).** Vertical (`$0041` = 1): progress is
`$0064 * 256 + $0065`, screen and pixels climbed. The camera catches up
only after Bill lands, so every candidate window ends with 40 idle frames
or the next window's input is read against the wrong frame. A greedy
explorer over jump-and-hold windows (`20f RA / 40f R`, the mirror, a
straight jump, each after 0–56 f of walking) climbs seven screens on its
own and stalls once, on the screen whose exit is up and to the left of a
platform that is *lower* than Bill: the greedy rule (highest camera, then
highest Bill) refuses to descend. Four chained jumps left, then the
explorer again, reached the top. The versioned `stage3-waterfall.txt`
holds the start of that climb for 60 s (jumps in both directions, aim up,
prone, drop) and yields 252 poses and 12 cycles; `stage3-waterfall-probe`
(`104f R / 30f - / 104f L / 30f -`) reads Bill's run both ways as `port1`
(28 and 26 repeats).

**The stage-3 boss.** Slots 5 and 6 are the two sweeping arms (type 21,
HP 16 each, active ~180 f after the room is entered); `$0085` counts the
arms destroyed. The core is slot 13 (type 20): its `$0585` reads 32 while
the mouth is open and 0xF1 while it is shut, and the *lasting* HP is the
second table, `$05c5` (32 at the start, 0 at the win). Only Up+B from
x = 100–128, or Right+Up+B from x = 100, lands, and only while the mouth
is open; the search on `$05c5` with survival first took 105 windows of
60 f and no death from arms-dead to `$003b` set. `stage3-boss.txt` (Up+B
bursts from the left and centre lanes, diagonals, a prone burst) records
that room (517 poses, 12 cycles, 355 sequences — the arms' sweep is
not periodic); `stage3-boss-probe` (the stage-1 probe from the same state)
reads Bill's run both ways as `port1` at 8 repeats each, the room being
one screen wide; the state is the arms-active one.

**Reproducing the states.** `stage3-waterfall-to-stage3-boss.chain.txt`
(99 steps, 11 402 frames) and `stage3-boss-to-stage4-base.chain.txt` (188
steps, 21 618 frames) are the chains above, reconstructed from the
solvers' `(txt, mss)` pairs by walking the save states' frame counters
backwards and verifying every step by replay; two steps whose scripts a
later branch of the search had overwritten were regenerated from the
parent's RAM with the solver's own candidate rule. `stage3-waterfall.mss`
itself is the end of a hand-stitched chain (stage 1 explorer, wall search,
base search across several solver generations with states copied by hand
between them) whose intermediate scripts were partly overwritten; it could
not be rebuilt from what is on disk and is archived under `runs/` only, so
a fresh checkout reproduces the stage-1 states from power-on
(`mint-*.txt`), replays stage 3 → boss → stage 4 from `stage3-waterfall.mss`,
and cannot re-mint that one state. The next chains are written as chain
files from the start.

**The second base (stage 4).** `stage4-base.txt` is the stage-2 script
(runs along the corridor, jumps, aim up, prone) and its 60 s from the
minted state give 291 poses and 17 cycles — the base soldiers' run toward
the camera as period-3 cycles of 7–8-tile poses (37 and 23 repeats) and a
28–30-tile fused trio. `stage4-base-probe.txt` is the stage-1 probe and
attributes nothing: in a base Bill walks sideways facing the screen, and
that walk comes out as period-5 cycles of 16-tile poses with irregular
holds and 4 repeats, under the rule's 4-window floor. The soldiers' run,
at 37 repeats, correctly has no driver. The stage-4 boss and stages 5–8
are open; the base solver applies unchanged (its stage check is now
relative to the state it starts from).
