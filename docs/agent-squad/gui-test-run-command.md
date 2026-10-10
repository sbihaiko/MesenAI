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

## What a check may read

`capabilities()` advertises these, and every one of them is answered from the
hook's own UI state - never from a pixel (ADR-0271) and never from display text
(ADR-0272 item 3). An id the application does not have is `unknown id`, which
fails the run loudly rather than answering `false`.

| Check | Args | Reads |
| --- | --- | --- |
| `ui.screen` | `{"is":"play.home"}` | the surface the player is looking at. |
| `ui.focused` | `{"is":"<id>"}` / `{"within":"<prefix>"}` | the focused control's id. |
| `ui.visible` | `{"is":"<id>"}` | whether a named control is drawn. |
| `ui.dialogs` | `{"is":[...]}` | the dialogs the hook reports. |
| `ui.ring` | `{"is":"<id>"}` / `{"is":"none"}` | the focus ring PlayerTheme paints on `:focus-visible`; `none` is a control focused with no ring - the state a pad player must never be left in (#824, #1232). |
| `ui.footer` | `{"is":"confirm,settings"}` / `{"contains":"back"}` | the action bar the focused surface declared (`PlayBarDeclarations`), in order, as `PlayAction` ids. Any other word is `unknown id`. |
| `ui.surface` | `{"is":"play.pause"}` / `{"is":"none"}` | the topmost open sheet or overlay, from the focus arbiter's own claim order (`PlayFocusOnOpen`, ADR-0249), or `none` when none is up. The ids are `TestHookSurfaces`. |
| `ui.paused` | `{"is":true}` | the application's own paused flag. |
| `ui.lamps` | `{"port":1,"is":"lit"\|"dim"\|"hidden"}` | one port of the status line's four lamps (ADR-0249/ADR-0255). `hidden` is the bar not being drawn at all - the game running unpaused (ADR-0261) - and a lamp that is not drawn is never read as `dim`. A port outside 1-4 is `unknown id`. |
| `ui.items` | `{"is":[<ids>]}` / `{"contains":"<id>"}` | the entries of the list the focus is in, in the order it draws them. The whole list compares as ids, never as one joined string: a game title can carry a comma. |
| `ui.haptics` | `{"pad":1,"is":2}` / `{"pad":1,"at_least":1}` | the haptic tick requests the menu made on that pad since the last step, recorded at `HapticTickOutput` (#1106) while a hook runs. It is evidence of the request, never of a vibration: no motor is involved. A pad the menu never ticked reads `0`. |

A step's own `inject` is what starts a step, so a `ui.haptics` check reads what
that step caused and the state reads of a `wait` in between consume nothing.

`ui.paused`, `ui.lamps` and `ui.haptics` all read the application's own UI state
under ADR-0272 item 7's closed check vocabulary: checks are `ui.*` (from the
hook's snapshot) or `fs.*`/`log.*` (evaluated by the runner), `emu.*` is reserved
there for the emulator's RAM and is not part of v1, and `pad.*` is the action
namespace. Issue #1282 asks for the paused flag, the port lamps and the haptic
ticks and names no namespace for them, so they are `ui.*` - a script naming
`emu.paused`, `pad.lamps` or `pad.haptics` is refused by both the format
validator (`unknown check namespace`) and this adapter (an unadvertised check),
and the accepted ADR is unchanged.

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
