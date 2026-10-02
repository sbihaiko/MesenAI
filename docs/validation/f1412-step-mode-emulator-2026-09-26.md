# F14.12 — a persistent step-mode emulator session (ADR-0238 §1)

- Date: 2026-09-26
- Slice: PRD F14.12, ADR-0238 §1 (**this is its first implementation**).
  Go-ahead verbatim, now on the ADR's Status line: *"implemente usando o
  deepseek"*.
- Worktree: `~/jev`, branch `feat/f1412-f1415-jev`, based on `539b609b4`
- Build: `/Library/Developer/CommandLineTools/usr/bin/make core` then
  `make capture-tool`, both exit 0. Notes: only macOS arm64, and the worktree
  is shared with the F14.14 worker (`scripts/jev_client.py`), which touched
  `makefile` and nothing this slice owns.

| artefact | sha256 |
|---|---|
| `InteropDLL/obj.osx-arm64/MesenCore.dylib` | `ee47a8f688f2…` (rebuilt after §7's fix) |
| `scripts/headless_record` | `f080f83534c6…` (unchanged by §7's fix - it is in the dylib) |
| `scripts/step_emu.py` | `6973ef7e4341…14bc` |
| `scripts/route_search.py` | `d91c40de9e99…f733` |

The two Python hashes are of the files as they stand; `route_search.py`'s only
change after the measurement run was an `--sessions` help string, which no run
reads.

Every number below comes from `scripts/measure_step_emu.py`, one run on this
machine (8 cores, Apple silicon), Ninja Gaiden `(1989) (Tecmo).nes`
(whole-file SHA1 `ca513f841d75…6bb9c`) from the state
`scripts/stages/ninjagaiden/mint-stage1.txt` writes at emulated frame 1082.

## 1. What was built

**Transport: a `session` mode of `scripts/headless_record`.** ADR-0238 §1 asks
for the choice to be made by measurement, and measurement does not separate the
two candidates: either transport spends microseconds per request against tens
of milliseconds of emulation (below, `restore` is 0.4 ms and `play` is 86 ms).
What separates them is what has to be got right to be comparable at all. The
ctypes path would have to reproduce, field by field through the ABI, the init
`main()` already does — the scratch home, the `MesenNesDB.txt` copy beside it,
`SetNesConfig`'s controller type and `AllZeros` power-on RAM, `EmulationSpeed
0`, the pack flags — and nothing in the ABI would fail loudly if one field
drifted. Driving the tool reuses that init, so a session run and a one-shot run
of the same script are the same run by construction; §3 and §4 are the check.

- `scripts/headless_record <rom> <seconds> <prefix> session …` — the same init
  as a one-shot run, then a request loop on stdin/stdout instead of a single
  run (`RunStepSession`, `scripts/headless_record.cpp`). Requests: `input`,
  `run`, `ram`, `save`, `restore`, `drop`, `savefile`, `loadfile`, `frame`,
  `quit`; one reply line each, `ok …` or `err …`. stdout carries replies only,
  and the loop prints `ready` before it reads its first request, which is where
  the module stops skipping the init chatter.
- `InteropDLL/EmuApiWrapperHeadless.cpp` — two exports, `HeadlessSaveState`
  (size, then bytes) and `HeadlessLoadState`. This is the smallest Core-side
  addition the slice needed: the state is the same serialization
  `SaveStateFile` wraps (`SaveStateManager::SaveState(ostream&)`), held in a
  buffer instead of a file so a candidate costs a memcpy and not a file round
  trip. No `EventType::StateSaved`/`StateLoaded` is raised — a search saving
  thousands of throwaway states would queue a toast per state for no reader.
  RAM needed no new export (`HeadlessReadNesRam` exists), and neither did frame
  stepping: `HeadlessSetPauseFrame` + `Resume()` + `IsPaused()` is the in-frame
  stop a one-shot run already uses.
- `scripts/step_emu.py` — the Python module: `StepEmu(rom, state=…, work=…)`
  with `load_script`/`run`/`play`, `read_ram`, `save`/`restore`/`drop`,
  `save_file`/`load_file`, `frame`, `close`.
- `scripts/route_search.py` — the Ninja Gaiden search ported (§4).
- Tests: `scripts/test_step_emu.py` (no ROM), `scripts/test_step_emu_rom.py`
  (ROM-backed, skips cleanly without the ROM).

## 2. Determinism: a script through the session is the script a one-shot run plays

`python3 scripts/test_step_emu_rom.py` — 22/22 checks, exit 0, 0 failures,
three runs in a row, on F14.13's route (after §7's fix; before it, one of
two runs failed).

```
minted state: 2048 bytes of RAM, lives $76=2, ryu x $86=32
session parks on frame 1083 after the state load
route script: 3330 frames, sha256 5f6c7c135d10b5a7
checkpoints: 688, 1800, 3330
ok   frame 688 replays to the same position 3 times
ok   the frame a run replies with is the frame it parks on
ok   a ram read leaves the frame where run parked it
ok   a whole-RAM read leaves it there too
ok   a save leaves it there too
ok   restore returns to the frame its state was saved on
ok   the one-shot reference runs to frame 1983
     window 30 one-shot: abs_x=1170.50 y=180 state=47
ok   the one-shot reference still plays the pinned route at window 30 (abs x 1170.50, 2 lives)
     window 30 session (no reads between windows): abs_x=1170.50 y=180 state=47
ok   session replay with no reads between windows is the one-shot play
ok   absolute x at window 30 matches with no reads between windows
     window 30 session (a RAM read after every window): abs_x=1170.50 y=180 state=47
ok   session replay with a RAM read after every window is the one-shot play
ok   absolute x at window 30 matches with a RAM read after every window
ok   RAM at frame 688 is byte-identical to a one-shot run
ok   the state written at frame 688 is byte-identical
ok   lives $0076 is 2 at frame 688
ok   absolute x is 988 at frame 688
     frame 1771: abs_x=988.00 hp=0F lives=2 room=0
ok   RAM at frame 1800 is byte-identical to a one-shot run
ok   the state written at frame 1800 is byte-identical
ok   lives $0076 is 2 at frame 1800
     frame 2883: abs_x=2072.50 hp=0B lives=2 room=0
ok   RAM at frame 3330 is byte-identical to a one-shot run
ok   the state written at frame 3330 is byte-identical
ok   lives $0076 is 2 at frame 3330
     frame 4413: abs_x=220.00 hp=02 lives=2 room=1
0 failure(s)
```

The compared script is the committed
`scripts/stages/ninjagaiden/stage1-run.txt` (3 330 frames, F14.13's route - the
pin in `PINNED_ROUTE_SHA256` is that file), run from the state `mint-stage1.txt`
writes. Four facts, all of them needed:

- **The same question asked three times gets the same answer.** A session is a
  live emulator driven from another thread, and this is the check that found
  §7's bug: 3 of 9 identical 688-frame runs came back somewhere else entirely.
- **All 2 048 bytes of NES internal RAM** at frames 688, 1800 and 3600 are
  equal to what `headless_record` produces from the same state with the same
  script truncated at the same frame. This is the check the slice asked for.
- **The state bytes are equal too**: the session's in-memory save written out
  with `savefile` is byte-for-byte the `.mss` a one-shot run writes at the same
  frame. That is what makes the in-memory export and the file export the same
  thing, and it is what a route chain depends on.
- **The route's own claims hold, while the pin says which route they are
  about**: F14.13's route passes the x 987 pin rather than sitting on it, so
  the committed facts are abs x 1 170.50 at window 30 (y 180, state `$47`),
  abs x 988 at frame 688 and `$0076` 2 at every checkpoint. The minted state's
  RAM is byte-identical to the scratch tree's own `mint/stage1-run.mss`, so
  this reproduces the state the route was authored on and not merely a similar
  one.

Where the checkpoint sits: the minted state parks on emulated frame 1083, so
the route's frame 688 is emulated frame 1771.

## 3. The ported search finds what the scratch search found

`scripts/route_search.py`, 16 hops from the minted state,
`--chunk 29 --beam 3`, against the scratch search's own log
(`runs/route-ninjagaiden/work/beam22.log` in the archive tree):
**15/15 hop standings identical**, same labels in the same order, same
`abs`/`hp`/`sc`/`st`/`lives` to the digit. The archive log is not versioned, so
this is not a check that runs in CI — `scripts/measure_step_emu.py
--archive-log <beam22.log>` is how to repeat it, and it reports 0 hops compared
when handed nothing.

```
start abs=   32.00 hp=10 sc=    0 st=00 lives=2
hop   1: R abs=   75.50 … | RJ abs=   74.00 … | RB abs=   51.50 … | RJB …
hop   8: R abs=  361.00 … | RJ abs=  361.00 … | R … | RJ …
```

(`abs=75.50` prints in 8 columns here and 7 in the scratch log; the numbers are
what is compared, not the padding.)

`--sessions 1` and `--sessions 8` produce the **same route file**, so sharding
candidates across sessions changes the CPU spent and not the tree explored:
`measure_step_emu.py` prints `1 and 8 sessions give the same route: True`.

## 4. What a candidate costs, before and after

One candidate is the same work on both sides: a state in, one 29-frame window
played, the RAM the search judges on out, a state out.

**before** — one `headless_record` per candidate, which is what the scratch
driver did (`subprocess.run` per candidate, `state=` in, `save-state=` out).
Decomposed with `make capture-tool`'s binary, 5 runs each, warm home:

| invocation | seconds |
|---|---|
| launch + ROM load + 1 frame (`rom 0.0166 prefix`) | 0.172 |
| … + 29 frames | 0.226 |
| … + state in, 29 frames, state out (the archive's candidate) | 0.227-0.232 |

**after** — one session for the whole run, states kept in it. One candidate, in
one session, split by request:

| step | ms |
|---|---|
| `restore` | 0.37 |
| `play 29` | 85.5 |
| `ram` (two blocks, names + enemy table) | 1.66 |
| `save` | 2.21 |

`play` is the whole cost: 29 frames at 1.85 ms each (54 ms) plus a fixed
**~30 ms** per run. That fixed part is `Emulator::WaitForPauseEnd`
(`Emulator::WaitForPauseEnd`, `Core/Shared/Emulator.cpp`) sleeping `30 ms` per iteration while paused,
so a `Resume()` is answered 0-30 ms later. It is a third of a candidate and the
whole reason a session is not far ahead of a launch per unit of CPU. It is a
Core timing decision with a GUI consequence (the same sleep bounds how fast the
front end unpauses), so this slice measures it and does not touch it.

192 candidates, both drivers at both worker counts:

| driver | workers | total | per candidate | candidates/s |
|---|---|---|---|---|
| one-shot | 1 | 44.52 s | **0.232 s** | 4.3 |
| one-shot (the archive's `--jobs 8`) | 8 | 5.76 s | **0.030 s** | 33.3 |
| session | 1 | 17.39 s | **0.091 s** | 11.0 |
| session (`--sessions 8`) | 8 | 2.72 s | **0.014 s** | 70.7 |

- **Per candidate, serial: 2.6× cheaper** (0.232 → 0.091 s). That is the number
  the slice asked for, and it is the one that does not depend on how many
  workers anyone runs.
- **At the same eight workers: 1.4-2.1×** (0.030 → 0.014-0.021 s). The
  eight-session number is the noisier one — five sweeps gave 2.69, 3.62, 3.67,
  4.08 and 2.72 s (14.0, 18.9, 19.1, 21.3 and 14.1 ms per candidate), so quote
  the range and not a digit. It is smaller than the serial 2.6× because eight
  sessions and eight one-shot processes both saturate the same eight cores:
  what the session saves per candidate is CPU, and CPU is what the archive was
  already buying more of.
- **A session is one process, so on its own it is *slower* than the archive's
  eight launches** (0.091 vs 0.030 s per candidate, 0.33×). Stated plainly
  because it is the honest reading of the two rows above: this slice does not
  make a search faster by loading a ROM once, it makes a *candidate* cheaper,
  and a search has to shard across sessions to spend that. `route_search.py
  --sessions N` is what does.

End to end, 15 hops (360 candidates, `--beam 3`):

| driver | wall |
|---|---|
| `route_search.py --sessions 1` | 31.5 s |
| `route_search.py --sessions 8` | **6.9 s** |
| the archive, projected: 360 candidates x its measured 0.030 s | ≈ 10.8 s (arithmetic, not a run) |

The archive's own number is a projection from its measured per-candidate cost;
its driver was not re-run, because its tree's `scripts/headless_record` is gone
and the per-candidate measurement above is the same invocation it used.

Session start (ROM load + init) is 0.054 s, so eight sessions pay 0.43 s of
startup in total — which is why the per-candidate figure is quoted over 192
candidates and not over one hop of 24, where a third of a session's 3 candidates
would be startup.

## 5. Commands

```sh
make core && make capture-tool
python3 scripts/test_step_emu.py                       # no ROM, 38 checks, exit 0
python3 scripts/test_step_emu_rom.py                   # needs the ROM, 22 checks, exit 0
python3 scripts/measure_step_emu.py \
    --rom "$NG_ROM" --work runs/f1412/measure \
    --archive-log ~/runs-archive/route-ng/runs/route-ninjagaiden/work/beam22.log \
    --json runs/f1412/measure/numbers.json
python3 scripts/route_search.py --rom "$NG_ROM" \
    --state <mint.mss> --work runs/ng --out runs/ng/route.txt \
    --hops 120 --chunk 29 --beam 3 --sessions 8
```

`make doc-checks` exit 0 with `scripts/test_step_emu.py` on its list;
`make python-tests` picks up both new test files with no edit.

## 6. For F14.13

- The macro set is deliberately unchanged, so Act 1-1 still stops at x 987.
  F14.13 adds the Left+A jump to `route_search.CANDIDATES` and re-runs.
- `--sessions 8` is what a comparison against the archive's numbers needs;
  `--sessions 1` is the cleanest way to read what the session itself does.
- A left-over: the 30 ms pause wake. A semaphore instead of the 30 ms poll in
  `Emulator::WaitForPauseEnd` would cut a third off every candidate, and every
  step-mode driver after this one (F14.14's rewind ladder, F14.15's
  measurement) pays it too. It is a Core timing change with a front-end
  consequence and wants its own decision, not this slice's.

## 7. The bug the determinism check found: an unsynchronized state export

The first version of `HeadlessSaveState`/`HeadlessLoadState` reasoned that a
session only calls them while parked, so no emulator lock was needed - the
sibling file exports (`SaveStateFile`) do take one, but a session looked like a
narrower case. It is not, and the only reason this log has a §2 at all is that
the check in it was run more than once.

**Symptom.** `scripts/test_step_emu_rom.py` passed on one run and failed on the
next, with the same binary, the same ROM and the same state:

```
FAIL RAM at frame 688 is byte-identical to a one-shot run: 476 byte(s) differ,
     first at $01A6
```

The 488 bytes that did match were the ones the load had just written, so the
console had simply kept running past the frame the session thought it had
stopped on. An instrumented loop of 9 identical runs diverged **3 times**: twice
at frame 688 (`abs_x` 626.50 instead of 987), once answering frame 2162 with
`abs_x` 32, and once producing a truncated `.mss` that `zlib` refused
(`Error -5`) - a save taken mid-write, not a wrong one. One replay per
checkpoint would have caught it roughly a third of the time, which is a coin
toss and not a check; the 3-replay check in the test file exists because of it.

**Cause.** `Emulator::Pause()` is not a barrier. The emulation thread sets
`_paused` from *inside* the frame it is executing and then finishes that frame -
`ProcessSystemActions`, then `WaitForLock`, then `WaitForPauseEnd` - so a caller
that observed `IsPaused()` has a console that is still being touched. The
session parked, saw `IsPaused()`, and serialized `_emu`, the console and the
mapper through `SaveStateManager::SaveState(ostream&)` while the emulation
thread was still in the frame. `LoadState` had the mirror problem, and a
deserialized state read while the thread ran is how a run ends up 2 162 frames
out. `IsRunning()` is not a substitute: it is true while paused.

**Fix.** Both exports now wrap their work in `_emu->Lock()`/`_emu->Unlock()`,
the same pair `SaveStateFile` reaches through `AcquireLock()` (and the same one
`EmuApiWrapper.cpp`'s `WaitForLock` spin uses). `Lock()` parks the emulation
thread at an end-of-frame boundary, so the state is one consistent end-of-frame
state, which is also exactly what a `.mss` written by `save-state=` is - that
equality is what §2's byte-for-byte state check asserts.

```cpp
	_emu->Lock();
	std::ostringstream stream(std::ios::binary);
	manager->SaveState(stream);
	_emu->Unlock();
```

**Confirmation.** 0 of 12 diverged immediately after the rebuild, 0 of 24 after
that, and `test_step_emu_rom.py` has since been green on every run - three in a
row before the numbers in §4 were taken, and once more after them.
The measurement numbers in §4 are all from the rebuilt binary; the lock costs
nothing visible (a `save` is 2.21 ms against a 29-frame `play` of 85.5 ms).

**The lesson worth keeping for F14.13/F14.14.** A persistent session is a
second driver of an emulator that was written for one, and the pause flag is a
state a thread reports, not a guarantee about what it has stopped doing. Any new
session request that reads or writes console state has to hold the lock even
when the machine looks parked - and a determinism check has to be run repeatedly
before it is believed, because this bug passed once.
