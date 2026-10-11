# Evidence for #1227 — the `/to-test-scenario` loop, once, on the pilot

Run with the agent-squad fork commit `36f1a08` (branch `gui/1227`, local only).

| File | What it is |
| --- | --- |
| `play-pad-only-home.results.json` | The results file of the Home batch, run through `python -m squad.to_test_scenario start-run` with the native core loaded: 7 passed, 5 pending (manual), no failed step, so the runner opened no bug. |
| `write-back.out.json` | Output of the separate `write-back` command that read that file. |
| `issue-state.json` | The resulting state on GitHub: each Home case's issue (passed → closed, pending → open) and the parent's run summary comment. |
| `../play-pad-only.gui-test.issues.json` | The map written by `publish`: parent #1286, and the 100 case issues the 100-sub-issue cap allowed. Since #1761 the scenario is published as areas (ADR-0275 §1), so those records were re-parented into the 16 areas' 📋 Cases containers and the rest created: the map's records are #1288–#1787 and it carries `"layout": "areas"` with the legend rendered from it. |

The pilot's script carries a `content_hash` per step now (`publish --stamp`); nothing else in it changed.

At the time of the pilot, GitHub allowed 100 sub-issues per parent, so a per-case publish of the pilot's 281 cases
stopped at the 100th. The first 100 cases (launch, home, after-game, library, favorites, game, game-archive,
game-bios and the first dialogs case) were published; the rest needed a per-batch publish or a split. The module
refused such a plan before it created anything.
