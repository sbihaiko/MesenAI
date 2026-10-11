# Evidence for #1283 — pad-only script: automated steps

Runs of `docs/validation/process/play-pad-only.gui-test.json` through `gui_test_run_script.py --adapter mesen-gui --batch <id>`
on the notebook's built-in display (`MESEN_GUI_WINDOW` unset, so `primary`), against a Debug build of this branch's UI
(`7f8e5b4be` plus the `play.library.search` AutomationId in `PlayerRomPickerView.axaml`) with the native core from a Release
build of `main` beside the launcher.

All four results files were produced against the committed script, sha256 `e4ca709c…` (the `script.sha256` field in each file).
Earlier runs of the launch, home and library batches used a previous script version (`2a22db0e…`); they are replaced.

| File | Batch | Result |
| --- | --- | --- |
| `play-pad-only-launch.results.json` | `launch` | 4 passed, 0 pending |
| `play-pad-only-home.results.json` | `home` | 10 passed, 3 pending (manual) |
| `play-pad-only-library.results.json` | `library` | 7 passed, 31 pending (manual) |
| `play-pad-only-lamps.results.json` | `lamps` | 11 passed (setup included), 2 pending (manual) |

The `home` batch's teardown reports `application exited -11` (SIGSEGV) with Settings open after Y; the batch verdict is
`passed` because the steps passed. Filed as #1484. `B` on Settings with no game making `ui.surface` read `play.pause` is #1485.

`lamp.hotplug-ring-stays` used to read `ui.ring` once after a 120-tick settle, which hid a ring that dropped on the unplug and
came back (it read `none` once with a 4-tick settle). The unplug window is now sampled at 2, 30 and 60 ticks and again after the
lamp dims.
