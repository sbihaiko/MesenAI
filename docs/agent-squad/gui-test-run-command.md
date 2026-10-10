# Running a GUI test script

The run command, its switches and the environment a run is given. The runner
itself (`gui_test_run_script.py`), the script format and the dashboard live in
the agent-squad fork (ADR-0272 item 6); this repository owns the emulator side
of the same contract - the adapter (`scripts/gui_test/mesen_gui_adapter.py`)
and the in-app hook it speaks to (`UI/Logic/TestHook`, `UI/Windows/TestHookWiring`).
The variables below are the emulator side's: the adapter and the application
both read them, so a run script only has to pass them through unchanged (#1255).

## The command

```bash
export MESENAI_ROOT=<MesenAI checkout>          # holds scripts/gui_test/mesen_gui_adapter.py
export MESEN_GUI_BINARY=<built launcher>        # a build carrying the in-app test hook
export MESEN_CORE_LIB=<MesenCore next to it>    # the native library of that same build
python3 scripts/gui_test_run_script.py <script>.gui-test.json \
    --adapter mesen-gui --out /tmp/run.json
```

The fork's runner writes the captures and batch logs of a run to
`<out>.artifacts`; they stay out of this tree.

## Environment

| Variable | Values | Read by | Effect |
| --- | --- | --- | --- |
| `MESENAI_ROOT` | a checkout path | the fork's runner | which checkout holds the adapter to load. |
| `MESEN_GUI_BINARY` | path to a built launcher | the adapter | the application a launch starts. Empty or missing fails the launch (`no application binary`), never a skipped step. |
| `MESEN_GUI_WINDOW` | `primary` (default), `any` | the adapter **and** the application | `primary` - and unset, and the empty value - places every window of the run on the primary (built-in) display: the main window is pinned at 1100x700, position 40,60, and the adapter refuses a launch - or the step that opened a window - whose window is not fully inside the primary display's working area; the hook reports the window (its frame) and the display in one unit - the display's own, which is points on macOS (1100x700 for the window on a display that reads 1440x900 even at 2x) and physical pixels on Windows and X11 (1650x1050 for the same window on a 150% display) - and the adapter compares the two as reported, never re-scaling either. `any` is the escape hatch, for a CI, Linux or headless runner where there is no primary-display notion. Any other value is an error: the adapter refuses it before a clone, a process or a socket exists, and a run started without the adapter has it refused at hook startup (`App.axaml.cs` prints `test hook unavailable` and exits 2). |
| `MESEN_GUI_FOCUS_E2E` | `1` | `scripts/test_gui_test_window_focus_e2e.py` | runs the opt-in real-binary check that a launch never takes the front from the application that was frontmost; needs `MESEN_GUI_BINARY`. Without it that class skips and the criterion is stated, not implied. |

## The hook's own switches

The adapter passes both to the application it launches; a person never types
them (#1255 is why a run is invisible: a window shown without activating, and
`NSApplicationActivationPolicyAccessory` on macOS, keep the keyboard where it
is).

| Switch | Meaning |
| --- | --- |
| `--test-hook=<socket path>` | open the hook's line-per-JSON-object socket there. Without the switch the process has no hook at all: no socket, no state, no input path changes, and `MESEN_GUI_WINDOW` is not read. |
| `--test-hook-token=<secret>` | the token every request has to carry. Either switch with no value is a startup failure, not a run that quietly goes on without a hook. |

## Input actions

A step's `inject` carries one of these (#1281). Every one of them is a pad the
application simulates in its own input path - the same pressed-key set a real pad
writes, or the application's own on-screen keyboard - never a synthetic OS event,
and the step's effect is read back from the application's state, never from a
pixel (ADR-0271).

| Action | Args | Meaning |
| --- | --- | --- |
| `pad.press` | `button`, `pad` (0), `ticks` \| `frames` | one press and its release. The duration unit is the state's: while the emulated clock runs the press is written in `frames`, otherwise in `ticks`. |
| `pad.hold` | `button`, `pad` (0), `ticks` | the button goes down and stays down for that many ticks - the GUI's own tick, which never freezes, so a hold is always written in `ticks` and never in `frames`. |
| `pad.release` | `button`, `pad` (0) | the button goes up now, before the hold it is under has run out - a two-button gesture is written `pad.hold` then `pad.release`. A button no hold is keeping down is put up anyway: a release never fails a step. |
| `text.type` | `text` | types through the on-screen keyboard the application itself shows (the one pad keyboard, ADR-0262), by walking its grid and pressing each key. A keyboard that is not open, or a character it has no key for, fails the step. |
| `pad.connect` | `index`, `family` (`xbox` \| `playstation`, default `xbox`) | hot-plugs a simulated pad on that device index. While a run is up the connected-pad count the window polls for its port lamps and its pad-loss pause is the hook's, so a script's connect is seen the way a real pad's is; until a script touches it, that count is the backend's own. |
| `pad.disconnect` | `index` | unplugs the pad on that index. The family goes with the pad: a `pad.connect` after a disconnect, with no `family`, is the default one (`xbox`), never the family the unplugged pad had - a PlayStation pad put back without `family` has its buttons read as Xbox, so name the family again on the reconnect. |

`pad` is a **device index**: `0` is the pad in the hand - ADR-0272 item 4's
"device 0" - and `1` is the second pad. The hook resolves it to the
backend's key names as `Pad<pad+1>` / `Joy<pad+1>`. A button is named the way the pad bridge names it
(ADR-0272 item 4): `Up`, `Down`, `Left`, `Right`, `A`, `B`, `Start`, ... in the
family the pad on that index was connected with.

ADR-0272 is an accepted ADR and is **not amended** by any of this: the actions
above are the emulator side's implementation of the interface it pins, and this
document is where they are written down - including the two places the actions
run ahead of the ADR's text, `pad.hold`/`pad.release` and `text.*` being
documented here rather than folded into its step inventory.

## Running the emulator-side checks

```sh
python3 -m unittest scripts.test_gui_test_mesen_adapter     # the adapter, against a fake hook over a real socket
python3 -m unittest scripts.test_gui_test_window_focus_e2e # the frontmost-application instrument (add MESEN_GUI_FOCUS_E2E=1 and MESEN_GUI_BINARY for the real-run half)
dotnet test UI.Tests/UI.Tests.csproj --nologo                # the host-free placement/activation rule
dotnet test UI.HeadlessTests/UI.HeadlessTests.csproj -p:RuntimeIdentifier=osx-arm64 \
  --filter "FullyQualifiedName~GuiTestWindowPlacementTests|FullyQualifiedName~GuiTestHook"
```

`UI.HeadlessTests` cases that open a `MainWindow` need a built `MesenCore`
(`make core`) and skip with their reason without one - the documented posture
in `UI.HeadlessTests/AGENTS.md`, and the same one `unit-tests.yml` runs under.
