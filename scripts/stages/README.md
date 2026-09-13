# Per-stage recording scripts (F9.22)

Input scripts for `scripts/headless_record` (`<count>f <buttons>` lines, or
`<count>f <port1>|<port2>` when the second player has to move — F9.22), one
folder per golden game. Three kinds:

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
spawning). A save-state boundary is not input-neutral: the same 600 f prone
script run in one piece and in ten 60 f pieces diverged after ~100 f, so a
chain is reproducible only as a chain, not as one concatenated script.

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
from power-on: `stage1-water` (`mint-stage1-water.txt` = `60f L` from the
stage-1 start; Bill drops into the water at x = 25, y = 212) and
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
