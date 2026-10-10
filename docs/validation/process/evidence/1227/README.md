# Evidence for #1227 — the `/to-test-scenario` loop, once, on the pilot

Run with the agent-squad fork commit `36f1a08` (branch `gui/1227`, local only).

| File | What it is |
| --- | --- |
| `play-pad-only-home.results.json` | The results file of the Home batch, run through `python -m squad.to_test_scenario start-run` with the native core loaded: 7 passed, 5 pending (manual), no failed step, so the runner opened no bug. |
| `write-back.out.json` | Output of the separate `write-back` command that read that file. |
| `issue-state.json` | The resulting state on GitHub: each Home case's issue (passed → closed, pending → open) and the parent's run summary comment. |
| `../play-pad-only.gui-test.issues.json` | The map written by `publish`: parent #1286, and 100 case issues (#1288–#1387). |

The pilot's script carries a `content_hash` per step now (`publish --stamp`); nothing else in it changed.

GitHub allows 100 sub-issues per parent, so a per-case publish of the pilot's 281 cases stops at the 100th. The first
100 cases (launch, home, after-game, library, favorites, game, game-archive, game-bios and the first dialogs case) are
published; the rest need a per-batch publish or a split. The module now refuses such a plan before it creates anything.
