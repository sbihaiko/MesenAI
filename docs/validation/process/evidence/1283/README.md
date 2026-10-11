# Evidence for #1283 — pad-only script: automated steps

Runs of `docs/validation/process/play-pad-only.gui-test.json` through `gui_test_run_script.py --adapter mesen-gui --batch <id>`
on the notebook's built-in display (`MESEN_GUI_WINDOW` unset, so `primary`), against a debug build of this branch's base
(`origin/main` at `ccf140a1e`) with the native core beside the launcher.

| File | Batch | Result |
| --- | --- | --- |
| `play-pad-only-launch.results.json` | `launch` | 4 passed, 0 pending |
| `play-pad-only-home.results.json` | `home` | 10 passed, 3 pending (manual) |
| `play-pad-only-library.results.json` | `library` | 7 passed, 31 pending (manual) |
| `play-pad-only-lamps.results.json` | `lamps` | 8 passed (setup included), 2 pending (manual) |

`lamp.hotplug-ring-stays` failed once in five runs with a 4-tick settle (`observed none`); with a 120-tick settle it passed
in six of six, and the committed script carries the 120.
