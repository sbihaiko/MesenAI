# Issue #465: a library-job mint ends where its script ends (2026-09-25)

## Bug

`scripts/record_library.sh` ran every mint as
`headless_record <rom> $seconds ... input=<mint> save-state=<stage>.mss`, with
the batch's `<seconds>` (60 by default). `headless_record` writes
`save-state=` only when the run reaches its frame target,
`round(seconds * 60.0988)`, so every minted state was saved at frame 3 607.
Every versioned mint is shorter than that (Castlevania 320 f, Punch-Out!!
1 990 f), so each state carried 1 000-3 400 idle frames.

## Red before the fix

`python3 scripts/test_library_job.py`, with the new case
`test_a_mint_ends_where_its_script_ends_not_at_the_batch_target`, which runs
the real `record_library.sh` over a synthetic ROM with a fake `headless_record`
that logs its argv (temp paths shortened to `<tmp>`):

```
FAIL a 320-frame mint runs for 6 s, not the batch's 60 s: ['<tmp>/out/Game/mint/stage1-run/Game.nes 60 <tmp>/out/Game/mint/stage1-run/rec input=<tmp>/out/Game/stages-src/mint-stage1.txt save-state=<tmp>/out/Game/stages-src/stage1-run.mss', '<tmp>/out/Game/stages/stage1-run/Game.nes 60 <tmp>/out/Game/stages/stage1-run/rec bootstrap hdpack-off input=<tmp>/out/Game/stages-src/stage1-run.txt state=<tmp>/out/Game/stages-src/stage1-run.mss']
Traceback (most recent call last):
  ...
AttributeError: module 'library_job' has no attribute 'mint_seconds_for_frames'
```

## Fix

- `library_job.script_frames(path)` counts a mint script's frames with the
  parser's arithmetic (`Nf` = N, `Ns` = round(N * 60.0988), comments and
  blanks skipped; the same rule as `replay_chain.sh`).
- `library_job.mint_seconds_for_frames(frames)` is the smallest whole number
  of seconds whose frame target covers the script. It is whole seconds, not
  the exact frame count, because that is the duration both hand-mint
  procedures use (Castlevania `6`, Punch-Out!! `34`), so the job's state
  matches the one the routes were authored and measured on.
- `library_job.py starts` gives each mint step a fifth NUL field, `seconds`;
  `record_library.sh` passes it to `headless_record` instead of the batch
  `<seconds>`. An unreadable mint script gives an empty field, and the job
  counts that mint as failed instead of guessing a duration.

Chosen over a new `headless_record` option (save when the input ends)
because it needs no Core or tool rebuild and reproduces the documented hand
mints exactly.

## Mutations

- Restoring `"$seconds"` in the mint line of `record_library.sh`: the new case
  fails (`FAIL a 320-frame mint runs for 6 s, not the batch's 60 s`), 30/31.
- Replacing the target search with `ceil(frames / fps)`: fails on
  `601 frames -> 10 s` (gives 11, one second of idle frames too many), 30/31.

## Tests

- `python3 scripts/test_library_job.py`: 31/31.
- `make python-tests`: 59 passed, 0 failed. `make doc-checks`: pass.

## End to end (real ROMs, `record_library.sh <folder> <out>`, 60 s batch)

| ROM | mint script | mint run | state frame | hand mint | 60 s route retained |
|---|---|---|---|---|---|
| Castlevania | 320 f | 6 s | 362 (target 361) | same RAM (0 of 2 048 bytes differ) | 3 555 |
| Punch-Out!! | 1 990 f | 34 s | 2 044 (target 2 043) | same RAM when run from the repo root | 633 (was 355) |

Before the fix both mints stopped at frame 3 607. Two job runs on Punch-Out!!
gave the same RAM and the same 633 retained frames.

The hand-mint comparison exposed an unrelated bug, filed as #477:
`headless_record` loads the NES game database only when its working directory
is the repository root. Run from elsewhere, the Punch-Out!! state differs from
the repo-root one at `$01FC` (8 instead of 6). The job and the documented
commands both ran from the repository root here.
