# Issue #477: headless_record loaded the NES game DB only from the repo root (2026-09-25)

## Cause

`scripts/headless_record.cpp` copied the game database into the run's
`mesen-home` from `"UI/Dependencies/MesenNesDB.txt"`. That path is relative to
the working directory. From anywhere but the repo root the copy was skipped
without a message. `GameDatabase::InitDatabase` then read no file, logged
`[DB] Initialized - 0 games in DB`, and loaded the ROM from its iNES header
alone. That dropped the database's board, chip and input-type overrides; the
missing `[Input] 2 NES controllers connected` line is one result. The
`record_library.sh`, `record_stages.sh` and `replay_chain.sh` scripts never
`cd`, so every state they minted depended on the caller's cwd.

The controller setup needed no change of its own. `headless_record` sets
`Port1`/`Port2` through the config structs, which involve no path. The
`[Input]` line comes from `NesConsole::InitializeInputDevices`, which runs
only when the database has an entry for the game.

## Red before the fix

`python3 scripts/test_headless_record_cwd.py` against the binary built from
`origin/main` (2c464ec4):

```
ok   both runs exit 0
ok   run from the repo root loads a non-empty game DB
FAIL run from a foreign cwd loads a non-empty game DB
     count=0; log line: [DB] Initialized - 0 games in DB
FAIL both cwds load the same game DB
     foreign=0 root=10655

2/4 checks passed
```

## Fix

`ExecutableDir()` in `headless_record.cpp` finds the binary's own folder:
`_NSGetExecutablePath` on macOS, `/proc/self/exe` elsewhere, and a
canonicalised `argv[0]` as the fallback. The DB is copied from
`<exe>/../UI/Dependencies/MesenNesDB.txt`, or else from `<exe>/MesenNesDB.txt`.
If neither exists, the run warns on stderr instead of continuing in silence.
Core is not touched. The tool had no other cwd-relative path.

## Mutation

The first candidate was put back to the cwd-relative
`std::filesystem::path("UI/Dependencies/MesenNesDB.txt")` and the binary
rebuilt. The test went red again with the same output as above
(`foreign=0 root=10655`, 2/4). With the fix restored and rebuilt, it passed
4/4.

## E2E: Punch-Out!! mint from /tmp vs the repo root

The command was `headless_record "<Mike Tyson's Punch-Out!! (1987) (Nintendo).nes>" 34
<dir>/mint screenshot log input=scripts/stages/punchout/mint-fight1.txt
save-state=<dir>/fight1.mss`, with all paths absolute. Every run stopped at
frame 2044 (target 2043).

| binary | cwd | DB | `[Input]` line | state sha1 | RAM $01FC |
|---|---|---|---|---|---|
| before (origin/main) | /tmp | 0 games, not found | absent | `ba537079…` | 8 |
| before (origin/main) | repo root | 10655, found (NES-PNROM, MMC2-L) | present | `35a6a149…` | 6 |
| after | /tmp | 10655, found | present | `35a6a149…` | 6 |
| after | repo root | 10655, found | present | `35a6a149…` | 6 |

After the fix the two states are byte-identical (`cmp` is silent). Both also
match the state the repo-root run produced before the fix.
