# F14.14 — Jev as the stall helper (ADR-0238 §3–§4)

- Date: 2026-09-26
- Slice: PRD F14.14, ADR-0238 §3–§4 **including every "amended 2026-09-26"
  bullet** (rewind ladder, cheat validation, situation tips, web research, loop
  guard). Go-ahead verbatim, on the ADR's Status line: *"implemente usando o
  deepseek"*.
- Worktree: `~/jev`, branch `feat/f1412-f1415-jev`, based on `539b609b4`
- Build: **this slice compiles nothing.** It did not touch `Core/`,
  `InteropDLL/` or `scripts/headless_record.cpp`, and ran no `make`; it uses
  the F14.12 session transport as it stands. The one shared file it did change
  is `scripts/step_emu.py`, additively (`cheats=` on the constructor, plus the
  launch's init chatter kept as `init_output` — that is where a cheat
  confirmation would appear).
- Not mine, in the same worktree: `makefile`, `InteropDLL/…`,
  `scripts/headless_record.cpp`, `scripts/stages/ninjagaiden/stage1-run.txt`,
  `scripts/route_search.py` and the F14.13 worker's macros. This slice imports
  `route_search` and never edits it.
- Revised 2026-09-26 for the Grok 4.6 review of this slice (verdict FAIL, three
  blockers and seven nits). The hashes below are of the revised files;
  `scripts/stages/README.md` was *not* this slice's file and this revision
  touched two of its paragraphs ("Situation tips", "The data a Jev run reads")
  because the review found the reader and the doc disagreeing — see "The review
  fixes" below.

| artefact | sha256 |
|---|---|
| `scripts/jev_harness.py` | `f43c4d2104b5c54a…` |
| `scripts/jev_stall_research.py` | `de02b80b87557c5e…` |
| `scripts/test_jev_harness.py` | `3374f31315926440…` |
| `scripts/test_mm3_stage_data.py` | `57c82bb343778a7a…` |
| `scripts/step_emu.py` | `c6fdab165a351e08…` |
| `scripts/stages/ninjagaiden/ram-map.json` | `24cfb1234b995133…` |
| `scripts/stages/ninjagaiden/jev-tips.json` | `bd40ab40b9b2da61…` |
| `scripts/stages/mm3/jev-tips.json` | `3103cd8774ebfa98…` |

The hashes are of the files as they stand; `runs/` is gitignored and holds
every log this document cites.

## What was built

**`scripts/jev_harness.py`** — the run. A game is described by
`scripts/stages/<game>/` alone: `ram-map.json` (named fields, word reads,
ranges, and `expr` arithmetic over the fields before them), `jev-tips.json`
(tips with RAM triggers) and an optional `cheats.json`. Nothing in the harness
is per-game: the smoke run below is Ninja Gaiden only because that is the ROM
whose stall the ADR was written about.

* The **base search** plays the fixed macro table (ADR's seven, plus the tip
  macros and, behind `--route-macros`, `route_search.CANDIDATES`), scores each
  candidate by the game's own progress field through `route_search.rank` when
  that module imports, keeps a death out of the answer, and settles for
  `--settle-seconds` after the macro so a payoff that lands late still counts.
* A **stall** is `--stall-seconds` (default 5) of emulated time without the
  progress watermark rising. Then, per rung of the **rewind ladder**
  (1, 2, 4, 8, 16 s, floored at the start of the current screen or the last
  real progress — the floor *clamps* the target, which measurement forced:
  dropping the rung instead blocked every rung), up to three Jev questions.
  `tried_here` carries `(macro, progress, death)` per checkpoint and a macro
  that failed there is withdrawn from the next question at that checkpoint.
* The **question** is a Jev `Choice` over the surviving macros, with the live
  RAM as JSON, the situation tips **whose trigger holds** folded into the
  option descriptions, and the tips file's sha256 logged with the decision.
* **Research** (ADR-0238 §3): when the ladder is spent, the stall report goes to
  `scripts/jev_stall_research.py` — one `claude -p --model
  'claude-deepseek-v4-flash[1m]' --allowedTools WebSearch,WebFetch
  --output-format json` worker, report on stdin as data, query carrying the game
  and the spot only. Its proposals land under `runs/`, are re-stamped with the
  harness's own macro durations (a proposal never picks how long a press lasts),
  and reach the versioned tips file only through `--promote-tips`, after the
  stall they were written for passed.
* The **loop guard** watches a fingerprint (position rounded to 8 px, camera,
  room, HP) seen 3× in one stall, a watermark flat for 60 emulated seconds, and a
  period-2..4 cycle repeated 3× in the last 12 choices: first reaction bans the
  cycle's macros and climbs a rung, second goes to research, third ends the
  stall as `loop`.
* **Caps**: `--max-decisions-per-stall` (18), `--max-stalls`, `--max-emulated-seconds`
  and `--budget` (US$ 1.00 default). The caps are the run's, not the model's.
* Every decision writes a dashboard line to stderr and a JSONL record to
  `runs/`; the run writes a summary JSON; the surviving path is written as a
  plain `scripts/stages/<game>/*.txt`-format script (`--out`, `runs/` by
  default) and, with `--verify`, replayed by a one-shot `headless_record` with
  no Jev in it, RAM-checkpoint by RAM-checkpoint.
* **Cheats** (ADR-0184 §1): `AAAA:VV[:CC]` only, `AAAA < 0x0800`, and the
  refused code is named — Game Genie letters, more than two colons and NES
  mirrors (`1000:99`, `2000:99`) are all refused rather than warned about. The
  harness then checks whether the session reports applying the code; see "the
  cheat finding" below for why that refusal is currently every cheated run.

**`scripts/jev_stall_research.py`** — the one web-search worker per stall, its
prompt (data-not-instruction, JSON-only answer, no durations), its query
builder, its answer parser, and a `--dry-run` that prints the query and the
argv and calls nothing.

**`scripts/test_jev_harness.py`** — 168 checks over a fake emulator (whose pin
behaves like Act 1-1's: only Left+A leaves it) and a scripted Jev: ladder order
and floor, tip gating, `tried_here` withdrawal, the three loop detectors and
their escalation, the caps, the research pass and its merge, `--promote-tips`,
cheat validation, the script output, `--verify`, and the refusals.

## Measurements

Every command below was run in this worktree; the ROM is the No-Intro Ninja
Gaiden (`CA513F84…` whole-file SHA1) and the state is the one
`scripts/stages/ninjagaiden/mint-stage1.txt` writes (emulated frame 1083).

**Unit tests** — `python3 scripts/test_jev_harness.py`: 123 checks, `ok`,
exit 0. Whole suite, `bash scripts/checks/run_python_tests.sh`: 72 passed,
1 failed in 83 s.

**Mutation check** — twelve single-behaviour mutations of `jev_harness.py`
(loop fingerprint 3→4, cycle period 2 dropped, watermark 60→59 s, decision cap
off by one, a failed macro not withdrawn, the floor no longer clamping, a tip's
trigger ignored, the settle window dropped, cheat address 0x800→0x8000, the
proposal's duration winning, the boundary frame dropped, frame 0 verified
anyway): **12/12 caught**, `not caught: none`.

**The artifact's own honesty (new, and the reason for the boundary frame).**
The search plays a *chain* of `play` calls; a script is one flat text, and the
two are not the same thing. Measured (`runs/f1414/probe*`):

| replay of the same 77-macro path | frames | abs x |
|---|---|---|
| chain of `play`, session frame counter | 1 155 | 991 |
| flat script as emitted, no boundary frame | 1 155 | 978 |
| flat script with `1f -` after each macro | 1 232 | **991** |

So `render_script` writes the boundary frame, `measure_script` replays the
finished script flat from the minted state, and **that** replay decides
`goal_reached` and the checkpoints — never the chain. A path only the chain
reaches ends the run as `chain-not-reproduced` (unit-tested). `--verify` skips
a frame-0 checkpoint and says so: a one-shot asked for zero frames still loads
the state and runs its own boot slack before reading (measured abs x 35 where
the session reads the state's own 32).

**End-to-end smoke, real ROM and real Jev** — from the minted Act 1-1 state,
`--budget 0.05`, the original macro set:

```
python3 scripts/jev_harness.py --rom "$NG_ROM" --game ninjagaiden \
    --state runs/f1414/ng/stage1-run.mss --work runs/f1414/e2e4 \
    --goal abs_x:990 --budget 0.05 --verify --max-stalls 6 \
    --max-emulated-seconds 300 --out runs/f1414/e2e4/route.txt \
    --summary runs/f1414/e2e4/summary.json --research-timeout 240
```

`runs/f1414/e2e4/summary.json`, exit 0 — **passed**:

| | |
|---|---|
| reason / goal | `goal`, `goal_reached: true` (the flat replay reaches abs x **991**, past the x 987 pin and the absolute 990 the run was asked for) |
| script | 77 macros, 1 232 frames (the chain's own counter: 1 155), `runs/f1414/e2e4/route.txt` |
| Jev decisions / loops / stalls | 2 / 0 / 2 — both stalls closed `"reason": "passed"` on the ladder's first rung |
| spend | US$ 0.000094 of the 0.05 budget |
| time | 159.49 emulated s in 45.75 s wall = **3.49× real time** (the target is ≥ 3×) |
| `--verify` | `ok: true` — a one-shot `headless_record` with no Jev in it, replaying the script from the minted state, read abs x 545 at frame 411, **987** at frame 821 and **991** at frame 1232, `lives` 2 and `hp` 16 at all three; frame 0 skipped as documented |

The Jev side of it, from `runs/f1414/e2e4/decisions.jsonl`: two decisions, both
`JUMP_LEFT_15` (served model `typesafe/jev-1.13-20260917`, confidence 0.91 and
0.93, probabilities 0.93/0.95 on the chosen option), tips id `wall-pin` held and
folded in with `tips_sha256 bd40ab40b9b2da61` — the sha256 of the
`jev-tips.json` above — and the first stall closed `"reason": "passed"`.

**The cheat finding (found here, fixed in the closures below).** The session
transport returned to its request loop *before* the cheat block ran, so
`cheat=` was silently dropped: verified by launching a session with a cheat and
reading both the launch's init lines (no `cheat applied:`) and RAM (unchanged).
ADR-0184 §1 says refuse, not warn, so a cheated session run was refused with the
codes named, which is why no cheated run exists - and the harness's refusal was
correct for as long as it held. The cheat block now runs before the request loop
and reports each code ahead of `ready`
(`scripts/test_session_protocol.py`), so the refusal is gone; a cheated run is
still not exercised end to end (open problem 5 below).

## The review fixes (Grok 4.6, 2026-09-26)

The review's verdict was FAIL and its finding was contract drift: the harness's
readers against the files this slice ships. Every blocker was reproduced as a
failing test **before** the fix. `runs/f1414-fix/red.txt` is that RED run
(`scripts/test_jev_harness.py` 15 failures plus a hard `RamMapError`;
`scripts/test_mm3_stage_data.py` the same `RamMapError` as a traceback, exit 1)
and `runs/f1414-fix/green.txt` the GREEN one, with the final test files (both
suites `0 failure(s)`, exit 0). Two checks in `test_loop_guard_escalation` were
adjusted while writing the fix, which `red.txt` carries a note about at its end;
no blocker line is affected.

| # | Finding | What changed |
|---|---|---|
| 1 | `route_macros` read `CANDIDATES` as `(label, buttons)`; it is `(label, (parts…))`, so `--route-macros` built macros whose buttons were the string `"(('R', None),)"` and died in `button_spec` | `Macro` is a window of `(buttons, frames)` parts, `macro_lines` writes one `<n>f <buttons>` line per part with the last one holding to the end and the idle frame padding the window (the convention `route_search.window_lines` uses), `_play_macro` plays the parts in order. `WALL_HOP`/`LEDGE_JUMP` come across whole, with the search's own fixed durations; a window a fixed part does not fit is dropped with a note, as `route_search.playable_candidates` drops one |
| 2 | The Mega Man 3 map did not load: `_as_int` used `int(s, 0)`, which refuses `"0027"`, and `state_frame`'s `address: null` raised | a string is read as hex with or without the `0x`; a spec with `address: null` is skipped and named in `RamMap.not_ram` instead of refusing the file |
| 3 | A tip's `value` clause was ignored by `Condition` (it read only `min`/`max`/`equals`), so an MM3 tip fired on any state carrying the field and never otherwise | `value` is read as `equals`, and the pin may be a string (`"snake-man"`) or a number. `Tips.load` also accepts the names a file *declares* under `runKeys` (a `_`-prefixed key like `_comment` declares nothing), which is what `mm3/jev-tips.json`'s `stage` is |
| nit | `test_jev_harness.py`'s `"loop guard"` assert ended in `or True` | it asserts every loop event carries the detector that fired, and the case where the cycle detector must name its macros moved to `test_loop_detectors`, where six alternating choices can actually accumulate |
| nit | `cycle_in` looked at a bare tail | it looks at the ADR's last 12 choices, and a constant run is no longer reported as a period-2 cycle (the watermark detector's finding), so a loop bans the macros it is actually about |
| nit | `tip.macro not in names` compared `JUMP_LEFT` with `JUMP_LEFT_15` | a tip's move is resolved through the table (`macro_named`), so the wall tip is folded into its own option and not into all seven |
| nit | `LIBRARY` had no `SHOOT`/`SLIDE` | `SHOOT`, `SHOOT_BURST`, `SLIDE`, `SLIDE_RIGHT`, `SLIDE_LEFT` and `JUMP_SHOOT` are in the tip table (Mega Man 3: B shoots, Down+A slides) |
| nit | `test_mm3_stage_data.py` checked the files as *data*, so the loader could drift from them | `check_harness_reads_the_files()` puts both stage sets through `RamMap.load`, `Tips.load`, `jev_harness.macro_table` and `Tip.holds`/`Condition.holds` — a `value` that never became `equals` fails it |
| nit | `extract_fields`' docstring claimed the MM3 shape was accepted | it now says what is accepted, what `address: null` means and what the return pair is |

**Measured after the fix** (same worktree, same ROM and minted state):

| check | result |
|---|---|
| `python3 scripts/test_jev_harness.py` | 168 checks, `0 failure(s)`, exit 0 |
| `python3 scripts/test_mm3_stage_data.py` | 340 checks, `0 failure(s)`, exit 0 |
| `python3 scripts/test_jev_client.py` | exit 0 |
| `python3 scripts/test_route_search.py` | exit 0 (the F14.13 owner's suite, untouched) |
| `RamMap.load`/`Tips.load` on `stages/mm3/` | loads: six fields (`abs_x` $0027, `camera_scroll_x` $0025, `screen_y` $0012, `player_hp` $00A2, `invincibility_timer` $0039, `lives` $00AE), `state_frame` reported as not RAM, six tips. `{"abs_x": 200, "stage": "snake-man"}` holds `big-snakey` and only it; `{"abs_x": 200}` holds none |
| `route_macros()` | 12 windows, `WH` = `(("LA", 4), ("RA", None))` → `WH_15` renders `4f LA` / `11f RA` |
| E2E after the review fix, `runs/f1414-fix/e2e/` (`--budget 0.01`, the command above) | exit 0, `reason: goal`, `goal_reached: true`, 77 macros / 1 232 frames, flat replay abs x **991**, **2 decisions** (both `JUMP_LEFT_15`, tip `wall-pin`, `tips_sha256 bd40ab40b9b2da61`), 0 loops, 2 stalls, US$ 0.000068, 159.49 emulated s in 42.33 s wall = **3.77×**, `--verify` `ok: true` (abs x 545 @ 411, 987 @ 821, 991 @ 1232, frame 0 skipped) — the same artifact the pre-review run produced |
| E2E, `runs/integ/e2e/` (same command, after the closures below) | exit 0, `reason: goal`, `goal_reached: **true**`, `chain_goal_reached: **true**`, 78 macros / 1 248 frames, `played_frames` 9 776, **2 decisions**, **0 loops**, **2 stalls**, US$ 0.000068, 162.67 emulated s in 39.83 s wall = **4.08×**, `--verify` `ok: true` (abs x 549 @ 416, 987 @ 832, 991 @ 1 248, frame 0 skipped) - the same artifact, with both goal flags agreeing for the first time |

## This revision's closures (after the review, 2026-09-26)

**#543, the idle frame, and the chain.** `HeadlessReadNesRam`, the `save` verb
and the state export no longer advance the emulated frame, so a read is inert -
and closing that exposed what the read had been hiding. A `run n` covers *n*
frames after a state a `run` ended on and *n + 1* after a `loadfile`; a
one-shot's `<seconds>` budget of `F - 1` is the session's `run_exact(F)`.
`StepEmu.run_exact`/`play_window` handle that asymmetry in one place, and
`route_search.play` and `jev_harness._play_macro` now *write* the boundary frame
into a window and *play* it, so the committed scripts stay valid unchanged.
`scripts/verify_chain.py` compares the three arms - one-shot, session-flat and
windowed chain - on all 2 048 bytes of RAM at three checkpoints, each arm twice:
**0 failures** on F14.13's route and on both Mega Man 3 routes.

**The cheats reach the console, and all three of Mega Man 3's hold.** The
session returned to its request loop before the cheat block, so `cheat=` was
parsed, validated and dropped in silence; the block now runs before the loop and
prints `cheat applied: <code>` ahead of `ready`
(`scripts/test_session_protocol.py` checks both, and that a code above internal
RAM is refused with no session at all). And this file's own finding - that two
of the three codes "do not hold" - was a measurement error rather than a
finding: on the NES a RAM cheat is *read-substitution*
(`Core/NES/NesMemoryManager.h:74` calls `CheatManager::ApplyCheat`), so the byte
in RAM keeps whatever the game wrote and a readback cannot see a code that is
working. Measured by effect (`runs/mm3-boss/cheat_effect.py`, one run per arm
from the minted boss state), all three hold and ADR-0184 §3's ranking is not
inverted. The table and the mechanism are in `scripts/stages/mm3/cheats.json`.

**The run-level `stage` key exists.** `--stage`, or a `stage` in the stage set,
rides on every state `RamMap.read()` produces; a stage the set does not name is
refused; and a run that declares none while its game's tips are gated on one
says so on stderr rather than going quiet. Ninja Gaiden's map verifies a `stage`
byte of its own (`$006D`), so its two stage-naming tips read RAM and are
unaffected - only a map without such a byte needs the key, which today is Mega
Man 3's.

**The session's requests are parsed strictly.** A count is digits only (a
`1xyz` used to be read as 1 by `strtoul`, i.e. a shorter route nobody asked
for), an address must be consumed whole, and a `save` that writes fewer bytes
than it was asked for is an error. `scripts/test_session_protocol.py` drives the
real binary with one malformed request per form and checks the session answers
`err` and keeps serving.

## Open problems

1. **`scripts/test_step_emu_rom.py` failed on F14.12's stale pin** (**closed**).
   The suite used to assert F14.12's reading of the committed route (abs x 987 at
   frame 688) with no record of *which* route those numbers were of, so F14.13's
   rewrite - uncommitted at the time - broke it for a reason that had nothing to
   do with the session. The route is now pinned by content
   (`PINNED_ROUTE_SHA256`, re-pinned on F14.13's route: abs x 1 170.50 at window
   30, abs x 988 at frame 688, `$0076` 2 throughout), the route-specific facts
   are asserted only while the pin holds, and a rewritten route makes the file
   print `skip` with both hashes rather than fail. 21 checks, `0 failure(s)`,
   exit 0, three runs in a row.
2. **The chain-vs-flat relation is stated where the numbers are published**
   (**closed**). `route_search.py` searches over chains of `play` calls, so its
   log's frames are chain frames and a script it writes has to be replayed flat
   before its numbers are believed - which is what `scripts/verify_chain.py` now
   does mechanically, and what the module docstring and
   `scripts/test_route_search.py` say. The relation itself is one line: the flat
   script carries the idle frame a window boundary needs, and the chain plays
   it, so the two are the same play.
3. **The research pass ran mocked, not live.** The unit test drives
   `research()` with a fake runner and a fake `claude`. No end-to-end run has
   exhausted the ladder (both E2E stalls passed on the first rung), so a real
   `claude -p` proposal has never been merged into a live run. What is proven:
   the query carries the game and the spot only, the answer is parsed, the
   harness re-stamps the duration, and nothing reaches the versioned tips file
   without `--promote-tips` after a passed stall.
4. **Only one tip has ever fired.** `wall-pin` gated correctly on the live state
   in both E2E runs; the other three tips in the file (`enemy-behind`, `birds`,
   `barbarian-boss`) have unit coverage of their triggers and no live run. The
   file says which of its gates are approximate.
5. **A cheat set reaches the console but no live run has used one**
   (**half closed**). `scripts/stages/mm3/cheats.json` ships Mega Man 3's three
   codes, all three verified by effect on this dump, and the session applies and
   reports them (`scripts/test_session_protocol.py`, against the real binary).
   What is still unexercised end to end: a Jev run that passes
   `--cheats` and the coverage pass that reads the file - so ADR-0238 §4's
   "a search-versus-Jev comparison is valid only under the same cheat set" is
   written down and measured (the invincibility timer makes the same 188-frame
   route reach *less* far) but not yet enforced by a run.
6. **A Mega Man 3 tip can hold now, but none ever has** (**closed as
   designed**). All six are gated on the run-level `stage` key as well as on a
   position band, and the key now reaches the state: `--stage snake-man`, or a
   `stage` in the stage set. What is left is a live run through the boss
   arena - the gate is tested, the tip is not. Closing `ram-map.json`'s
   `open.stage_id` would let the tips be gated on RAM like Ninja Gaiden's and
   drop the run key; that is a measurement nobody has made.
7. **`--route-macros` offers the search's windows to Jev only.** The route labels
   are not in `BASE_MACROS`, so the base search still never plays them (that is
   deliberate: it is what leaves the x 987 pin standing for F14.14 to be
   measured on). A future run that wants the search itself to use F14.13's
   windows has to say which of them join the base set — the harness has no flag
   for that today.
