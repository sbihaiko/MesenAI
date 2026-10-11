# The fork gate's full class never ran the pytest suite

Status: fixed in the fork (`agent-squad`, branch `gui/1822-gate-suites` at `a9d974e`).
Nothing in this repository changes: the gate lives in the fork, not here.
GitHub issue #1822. Measured 2026-10-11 against the skill's `gate-suites.sh` and
`fork-gate.sh`, and against the gate log of the landing that shipped #1809.

## What was measured

The gate picks one of three suite classes from the branch's diff
(`gate-suites.sh`), and `fork-gate.sh` runs that class:

- `^tests/test_gui_test_` → `full`
- `^tests/` → `python`
- `^contrib/squad-goal/` → `shell`
- anything else, no paths, or two classes mixed → `full`

`fork-gate.sh` ran its pytest command (`SG_FORK_PY_CMD`,
`uv run --extra dev pytest tests -q --ignore-glob='tests/test_gui_test_*'`) only
inside the `python` branch. So a diff that touched `tests/test_gui_test_*.py`
took `full` — the GUI suite plus the squad's shell suites — and the unit tests
never executed. The `tests/test_hub_*.py` files that ride in the same diff went
unexecuted with them.

#1809 is the landing that proves it. The gate log for that run carries

    === fork gate: GUI suite
    === fork gate: squad shell suites

and no pytest section at all, while `tests/test_hub_empty_column_strip.py` on
that branch asserted the inline shape (`class=chip` inside the `const html=`
line) and the branch rendered chips through `map(hidchip)`. The assertion could
not hold, and nothing in the gate ran it.

## Why it happened

The class table was read as "how expensive is this branch", not as "what can
this branch break": `full` meant the two suites that were already there (GUI and
shell), and the third suite was wired to the one class that names it (`python`).
A path that needs both the GUI suite and the unit tests — any GUI test file —
fell in the gap between the two readings.

## The correction (fork, not this repository)

- `fork-gate.sh` computes the pytest command once and runs it for the `full`
  class as well as for `python`. The `full` class is now the GUI suite, the
  shell suites and the pytest suite; a red one of the three fails the gate, and
  one red never hides another because all three always run.
- The pytest run joins the shell suites beside the GUI suite (both are offline
  and need no emulator, and the GUI suite holds the gui-suite lock itself), so
  the full gate grows by the pytest suite's own cost and not by a serial wait.
- `shell` keeps the shell suites alone and `python` the pytest suite alone, so
  the class still decides the cost of a narrow diff (#1392, #1409).
- `gate-suites.sh` is unchanged: `full` was already the right class for a GUI
  test path; it was the class's contents that were short.
- The fixtures that drive the gate pin `SG_FORK_PY_CMD`, the pytest command now
  being an input of the gate like `FG_SUITES`: `tests/test_squad_shell_suite_gate.py`,
  `tests/test_gui_test_e2e.py` (its `#1392` case) and the `test-forkclose-*`
  shell suites, whose stub forks hold no tests to run.

The fix reaches a running loop when `local/next` advances and the live skill
directory is refreshed from the fork; until then the live copy still gates with
the old `fork-gate.sh`.

## Evidence

`tests/test_squad_fork_gate_full_runs_pytest.py` in the fork, written before the
fix. Its RED run, against the code that shipped #1809's landing:

    >       assert marker.exists(), "the full class never ran the pytest suite, so a red unit test rides along (#1822)"
    E       AssertionError: the full class never ran the pytest suite, so a red unit test rides along (#1822)
    ...
    >       assert p.returncode != 0, "a red unit test must stop the landing, not ride a green GUI-and-shell run into it"
    E       AssertionError: a red unit test must stop the landing, not ride a green GUI-and-shell run into it
    E       assert 0 != 0
    FAILED tests/test_squad_fork_gate_full_runs_pytest.py::test_a_gui_test_path_takes_the_full_class_and_the_full_class_runs_pytest
    FAILED tests/test_squad_fork_gate_full_runs_pytest.py::test_a_red_pytest_suite_fails_the_full_gate_while_the_other_suites_are_green

The shell suite that pins the class's contents was RED with it:

    ok   FG_SUITES=shell runs the shell suites only
    ok   FG_SUITES=python runs the python tests only
    FAIL full mode
    FAIL default mode

GREEN at `a9d974e`: the three tests above pass, `shell-suites.sh` reports all its
suites green, and the `test-forkclose-*` suites that drive the gate — `suites`,
`serial`, `batch`, `reland`, `ancestor-revert` — pass with `SG_FORK_PY_CMD`
pinned.

One pre-existing red, unrelated to this change and left as it stands: with the
GUI kill switch on (`~/.squad-gui-tests-off`), the gate's GUI suite is skipped by
`serial.sh`, so `tests/test_gui_test_e2e.py::test_1392_...` fails on a missing
GUI marker both before and after this change, and `test-forkclose-suites.sh`
reports `FAIL gui runs` under a real `HOME`. Inside the gate — where
`shell-suites.sh` gives each suite its own `HOME` — the same cases pass.

## Related

- `router-model-overrun.md` — the earlier record of a gate that measured the
  wrong thing.
- `provider-outage-falls-through.md` — the same shape of record: the fix lands in
  the fork, the note is what this repository keeps.
- `~/.claude/skills/squad-goal/scripts/gate-suites.sh` and `fork-gate.sh` — the
  installed copy the loop runs, refreshed from the fork's
  `contrib/squad-goal/scripts/`.
