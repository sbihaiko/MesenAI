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

## The contract version

`hello` is a run's first request and its answer is the contract line: a runner
reads what the hook answers before it spends a step finding out.

| Field | Meaning |
| --- | --- |
| `hook` | the protocol version. **1**: `pad.press`, `pad.hold`, `pad.release`, `key.press` and the two pad hot-plugs are answered, and the answer carries **no** `actions` list. **2** (#1779): `pad.chord` and `nav.goal` are answered as well, and `actions` carries the sorted name of every `inject` action this hook implements - so an old hook is told apart from a new one by asking, never by failing a step. The adapter speaks both. |
| `ops` | the five operations: `hello`, `state`, `inject`, `capture`, `quit`. |
| `namespaces` | `pad`, `key`, `ui` (ADR-0272 item 7). |
| `actions` | the `inject` actions this hook answers (version 2 and later). |

The verbs a GUI run needs beside a single press, and where each one is answered:

| Verb | Answered as |
| --- | --- |
| release a held button | `pad.release` |
| chord two buttons or more | `pad.chord` |
| navigate to a goal | `nav.goal` |
| drive the keyboard | `key.press` |
| read the focused control | the `state` op's `focus` field, which is what the `ui.focused` check reads |

`capabilities()` is what a script is validated against, and it advertises the
verbs a run can currently write: a verb the hook answers and the adapter does not
advertise is one no committed script writes yet. The two move together because
`scripts/checks/verify_gui_test_adapter_surface.py` fails the moment the adapter
advertises an action whose `manual` step carries a `needs action <name>` claim -
the claim is what retires with the step that stops being manual.

## Input actions

A step's `inject` carries one of these (#1281). Every one of them is a pad the
application simulates in its own input path - the same pressed-key set a real pad
writes - never a synthetic OS event, and the step's effect is read back from the
application's state, never from a pixel (ADR-0271).

| Action | Args | Meaning |
| --- | --- | --- |
| `pad.press` | `button`, `pad` (0), `ticks` \| `frames` | one press and its release. The duration unit is the state's: while the emulated clock runs the press is written in `frames`, otherwise in `ticks`. |
| `pad.hold` | `button`, `pad` (0), `ticks` \| `frames` | the button goes down and stays down for that duration before letting go on its own. The unit is the state's, exactly as `pad.press`'s is (ADR-0272 item 4): `frames` while the emulated clock runs, `ticks` otherwise, and the family the state does not use is a failed step rather than a hold counted by a clock that is not moving. |
| `pad.release` | `button`, `pad` (0) | the button goes up now, before the hold it is under has run out - a two-button gesture is written `pad.hold` then `pad.release`. A button no hold is keeping down is put up anyway: a release never fails a step. |
| `pad.chord` | `buttons` (two or more), `pad` (0), `ticks` \| `frames` | every button of the chord goes down together for that duration and comes up together. A chord is not two `pad.press` requests: the readers of a press poll the pressed-key set once per tick, so a gesture that means "these together" - ADR-0256 gives the Play pause Select+Start - has to reach the set in one. The unit is the state's, as it is for a press. Nothing is pressed unless every button resolves: a chord that named one button, a stranger, or the same button twice is the step's error, and a half-pressed chord is a gesture nobody made. |
| `key.press` | `key`, `ticks` \| `frames` | a literal key name, as the backend defines it (`Enter`, `Up Arrow`, `1`), held down for that duration. It reaches the pressed-key set **and** is raised on the application's own keyboard as KeyDown/KeyUp - the layer the menu's keyboard reads - so a step that drives the keyboard needs no pad connected, and a pad press raises nothing there. The unit is the state's, as it is for a press. |
| `pad.connect` | `index`, `family` (`xbox` \| `playstation`, default `xbox`) | hot-plugs a simulated pad on that device index. While a run is up the connected-pad count the window polls for its port lamps and its pad-loss pause is the hook's, so a script's connect is seen the way a real pad's is; until a script touches it, that count is the backend's own. |
| `pad.disconnect` | `index` | unplugs the pad on that index. The family goes with the pad: a `pad.connect` after a disconnect, with no `family`, is the default one (`xbox`), never the family the unplugged pad had - a PlayStation pad put back without `family` has its buttons read as Xbox, so name the family again on the reconnect. |

`pad` is a **device index**: `0` is the pad in the hand - ADR-0272 item 4's
"device 0" - and `1` is the second pad. The hook resolves it to the
backend's key names as `Pad<pad+1>` / `Joy<pad+1>`. A button is named the way the pad bridge names it
(ADR-0272 item 4): `Up`, `Down`, `Left`, `Right`, `A`, `B`, `Start`, ... in the
family the pad on that index was connected with. A `key.press` carries no `pad`:
the keyboard is not a pad, and the hook reads the name the backend defines.

### `nav.goal`: the moves, not the search

| Action | Args | Meaning |
| --- | --- | --- |
| `nav.goal` | `goal` | ADR-0271's navigation action: the step names a goal as a check-vocabulary predicate (`ui.screen == play.library`) and the run reaches it by choosing one move at a time. The hook answers what only it has - the UI state the choice is made from (the `state` snapshot, never a screenshot) and the moves it will accept, which are the pad's six navigation presses, each written as a ready-to-send `pad.press` in the duration family the state allows - and **injects nothing**: asking it is not a move, it declares no step, and no model is reachable from the hook. |

The `nav.goal` answer is the request's own object plus:

```json
{
  "goal": "ui.screen == play.library",
  "family": "ticks",
  "state": {"screen": "play.home", "focus": "play.home.continue", "controls": [], ...},
  "moves": [
    {"name": "pad.press", "args": {"pad": 0, "button": "Up", "ticks": 1}},
    {"name": "pad.press", "args": {"pad": 0, "button": "Down", "ticks": 1}}
  ]
}
```

`family` is `frames` while the emulated clock advances and `ticks` in every other
state (ADR-0272 item 4), so the number a move must carry is the hook's ruling and
never a guess. Which moves already failed from a state, the cap on decisions and
on spend, and the recording of each decision are the runner's (ADR-0271 items 3
and 5): the hook holds no history (ADR-0272 item 2), and the chosen move comes
back as one more `inject`.

The actions above are the two namespaces ADR-0272 item 7 gives this hook - `pad.*`
and `key.*`, beside `nav.goal`, which that item already lists as the navigation
decision's action - and the ADR is accepted and **not amended** by any of this:
the actions are the emulator side's implementation of the interface it pins, and
the names inside those namespaces are written down here. `text.*` and `pointer.*` are
deliberately **not** here: item 7 places both in the real-window mode (step 7),
so a step that writes `text.type` gets `unknown action text.type` back from the
hook. Typing on the application's own pad keyboard (ADR-0262) stays a product
feature a player drives, not a script action, until an amendment to that item
puts it somewhere a run may use.

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
| `ui.haptics` | `{"pad":0,"is":2}` / `{"pad":0,"at_least":1}` | the haptic tick requests the menu made on that pad (0-based, 0 is the pad in the hand, 0-3) since the last step, recorded at `HapticTickOutput` (#1106) while a hook runs. It is evidence of the request, never of a vibration: no motor is involved. A pad the menu never ticked reads `0`. |

Every `pad` field - the input actions' and `ui.haptics`' - is one 0-based index
(ADR-0272 item 4's "device 0"), so `pad.press {"pad":0}` followed by
`ui.haptics {"pad":0}` names the same pad. `ui.lamps` keeps its `port` field,
1-based as the lamps label them ("P1").

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
