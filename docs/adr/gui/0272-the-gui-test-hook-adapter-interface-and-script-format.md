# ADR-0272: The GUI test hook, the target adapter interface and the versioned script format

- Status: accepted (2026-10-09). The owner answered the three open points in
  session — "A/A/A (Recommended)" — and all three answers are folded into the
  Decision below (items 6, 5 and 7: P1 into 6, P2 into 5, P3 into 7); the record of what was picked, and of the
  alternatives that were not, is the "Owner's picks" section. Listed as a slice
  in `docs/roadmap/PRD-mesence-enhancement-ecosystem.md` (Part B §8, slice
  T.0, tickets #1178, #1179, #1181, #1182, #1183). The implementation tickets
  read this as binding. Not implemented.
- Date: 2026-10-09
- Related: the navigation decision on PR #1201 (Jev drives pad/keyboard
  navigation from the hook's state; its option A is the one this ADR is the
  surface of). It is accepted but not in `main` yet, so this file cites it by
  PR; the Jev navigation ADR on PR #1201 gets its number cited here once both
  are on main. The squad spec travels with the same PR. Prior art: ADR-0157 §1–§3 and
  §6, ADR-0150, ADR-0167 (superseded — folded into ADR-0157, which carries it),
  ADR-0249, ADR-0238, ADR-0123, ADR-0137, ADR-0003, ADR-0250, ADR-0255,
  ADR-0262.
- Supersedes / amends: none. This is the hook/adapter/format half of the squad
  spec's step 0 and step 2; the navigation half is the PR #1201 ADR named above.

## Context

The GUI is verified by hand. The pad-only Play script under
`docs/validation/process/` is 67 cases a person walks with a pad, judging each
screen by eye, and the evidence of a failure is a sentence in a chat. Two things
follow: the checks are skipped under time pressure, and nothing a machine can
read exists — not the steps, not the screens, not the verdicts.

The squad spec answers that with a runner that knows nothing about a particular
application: it drives a **target adapter**, and the first adapter is the
emulator's own GUI. Driving it needs a seam inside the application — input
injected in-process, UI state read from it — because synthesising pad or key
events at the operating-system level needs Accessibility and Screen Recording
permissions on macOS, fights whoever is at the same desk, and cannot express
"hold Right for four frames". This ADR fixes that seam, the interface every
adapter implements, and the format the scripts are written in.

**How the hook relates to the prior art.** Neither of the existing harnesses can
do this job, and neither is replaced:

- **ADR-0150 (`UI.HeadlessTests`)** runs a real Avalonia application in-process
  under `dotnet test` with a null windowing backend. It is white-box: the test
  constructs the object graph, sets view-model properties and asserts on the
  visual tree. It cannot be driven from outside its own process, cannot inject
  input, and does not run the application a person would run. The hook is the
  black-box sibling — an external runner speaking to a **running** application.
  The wiring assertions ADR-0150 exists for stay there; the hook never asserts
  what a `[AvaloniaFact]` asserts.
- **ADR-0157 §1–§2** makes input frame-accurate: a script's unit is the emulated
  frame, resolved inside the frame by `HeadlessInputProvider`, because host time
  makes a run depend on machine load. **§3** then says the headless path is a
  *runtime* mode, not a compile-time one — no `#ifdef` in `Core/`, because a
  second binary splits the harness from the shipped application. §3 carries
  over verbatim: the hook's own switch is a runtime flag (item 5), not a
  second build. What §2's input path does **not** carry over is the *layer*: as
  §4 states, `IInputProvider::SetInput` runs once per emulated frame from
  `BaseControlManager::UpdateInputState()` and feeds in-game input only, while
  every GUI navigation consumer in this repository polls the host's pressed-key
  set instead (`InputApi.GetPressedKeys()` → `KeyManager::GetPressedKeys`). The
  hook therefore injects at both layers, each for what it feeds — frame-counted
  through `IInputProvider` for the running game, at the pressed-keys layer for
  the GUI. What §3 does **not** decide is the application-side gate, which is decided by
  P2-A (item 5).
- **ADR-0167** (folded into ADR-0157 §6) answers "did a toast appear" from the
  emulator's own HUD buffer, with no renderer and no checksum on faded pixels.
  The hook's `ui.*` checks are the GUI-layer counterpart — a toast is asked of
  the layer that displayed it, not of a rasterised surface — and where a check
  genuinely needs the emulated picture, it goes through that existing capture
  seam rather than a new one.
- **ADR-0249 §5**'s render gate is the capture precedent: the headless app
  renders with Skia and `HeadlessWindowExtensions.CaptureRenderedFrame(top)`
  returns a real bitmap, which `UI.HeadlessTests/PlayerRender.cs` saves as a PNG
  for a person to compare. Capture is therefore not an open feasibility question;
  the hook exposes the same thing to an out-of-process caller.

Non-goals: not a replacement for the headless suites; not an operating-system
input device; not a scripting or remote-control feature of the product; not a
model — the hook is deterministic, and no vision model is reachable from it.

## Decision

**1. Transport: one local endpoint, one JSON object per line.** The hook is a
listener inside the emulator process, reachable only over a local endpoint: a
Unix domain socket on macOS and Linux, a named pipe on Windows, named by the
runner as `--test-hook=<endpoint>`. No TCP port is opened, on loopback or
anywhere else — a port is reachable by every process on the machine and raises a
firewall prompt on macOS, while a socket is a file with permissions (0700
directory, 0600 socket). The protocol is newline-delimited JSON: one request
object per line, one response object per line, `{"id":<n>,"op":...}` in,
`{"id":<n>,"ok":true,...}` or `{"id":<n>,"ok":false,"error":"..."}` out. Every
request carries the per-run token the runner passed as `--test-hook-token`; a
wrong or missing token answers `ok:false, error:"unauthorized"` and leaves the
application running. Errors are responses, never a closed connection and never a
crashed application: a bad request must not cost the run.

**2. Operations: five, and the hook keeps no history.** `hello` (hook and
application version, and the namespaces this hook implements), `state` (the
snapshot below), `inject` (one action), `capture` (write a PNG of the
application window to a path the runner names, returning path, size and sha256 —
never in-band base64, so a line stays small), `quit` (shut the application down
the way its own Exit does, so settings are written). All five are stateless and
idempotent except `inject` and `quit`. The hook is not the record: it holds no
history, no verdicts and no snapshots, so a replay never asks the application to
rewind.

**3. State: named controls, never pixels and never display text.** `state`
returns the application's own UI state as one JSON object, and this is the whole
of what a check may read from the GUI layer:

```
{
  "screen": "play.home",
  "dialogs": ["play.controller-sheet"],
  "focus": "play.home.continue",
  "controls": [{"id":"play.home.continue","enabled":true,"visible":true,"focused":true}],
  "options": [{"id":"play.settings.display.scale","text":"3x"}],
  "window": {"mode":"windowed","size":[1280,720]},
  "tick": 4211,
  "frames": 0
}
```

The ids are the application's own test-facing names, taken from the control's
`AutomationProperties.AutomationId` (Avalonia's existing automation attribute —
no new mechanism, and the name stays where the control is declared). Two rules
follow and both are binding: **checks match ids, not display strings** (labels
change and are localized; a check on "Continuar" breaks on a language change),
and `text` is carried for humans and for the vision model only, never for a
check. A control with no id is invisible to the hook. `tick` is the
application's UI update counter and `frames` the emulated frame counter
(`0` when no game is loaded) — both are application-reported counters, and no
wait may spend host time (ADR-0157 §1). The navigation decision on PR #1201 widens this snapshot beyond the
active screen
and the focused control to **visible controls and menu options**, which is what
items 3 and 4 of that decision need; this ADR is that surface.

**4. Input is injected inside the application, at the layer that consumes it.**
There are two input layers in this application and they are fed differently.

- **The host pressed-key set** is what a physical press reaches, and every
  reader of a press polls it: `InputApi.GetPressedKeys()` →
  `KeyManager::GetPressedKeys` (`InteropDLL/InputApiWrapper.cpp`). Its readers
  are the GUI (`PlayPadNavigation`, `UI/Logic/PlayPadNavigation.cs:89-110`, fed
  by the bridge's tick, `UI/Windows/PlayPadNavigationWiring.cs:818`), the
  shortcuts (`Core/Shared/ShortcutKeyHandler.cpp:79,383`, including the
  ADR-0251 Select+Start chord), the Controller sheet's capture
  (`UI/Windows/PlayPadNavigationWiring.cs:741-751`, read while `HasAuthority` is
  false), the slot grid's Back edge (`PlayPadNavigation.cs:102-110`, asked at
  `PlayPadNavigationWiring.cs:883`) and the emulated console itself
  (`Core/Shared/BaseControlDevice.cpp:257`, `SetPressedState`). So `pad.*` and
  `key.*` **always** go into this set, on every screen and whether or not a game
  is loaded or paused: the inject layer is keyed by **who reads the press**, not
  by the screen.
- **`IInputProvider::SetInput`** is called once per emulated frame from
  `BaseControlManager::UpdateInputState()` (ADR-0157 §2) and feeds the emulated
  console and nothing else; no GUI consumer reads it, and it is not reachable
  with no game loaded. It is an **opt-in** layer, used only by a step that
  asserts console-only input while the game runs unpaused, and the step names it
  explicitly (`"layer":"console"`); a step that does not name it never uses it.

A step's duration unit follows from the layer it drives, and this is binding:
`frames` / `timeout_frames` (counted as ADR-0157 §1/§2 count them) **only while
the emulated clock advances** — a game is loaded and not paused — and `ticks` /
`timeout_ticks` (the hook's own tick, §7) in **every other state**: no game,
paused, the pause overlay, the load card, the picker. Outside a running game
there is no emulated frame counter to advance, and a step written in `frames`
would spin on a counter frozen at its last value. The validator/runner treats a
step whose unit does not match the live state as malformed. So one action is
written either `{"op":"inject","action":"pad.press","args":{"button":"Right","frames":4}}`
while the game runs, or `{"op":"inject","action":"pad.press","args":{"button":"Right","ticks":4}}`
on any other state. Buttons are named by the emulator's own key names
(`BaseControlDevice::GetKeyNameAssociations()`, as ADR-0157 §2 does), so one name
drives a NES, GB and SMS pad and the script is console-independent. Both layers
**overlay** physical input rather than replacing it: a pad plugged into the
machine does not break a run, and no run needs an OS permission. `key.*` arrives
in the same shape at step 4 of the squad spec; `pointer.*` is deliberately absent
from this hook — pointer position, hover and drag are geometry the state JSON
does not describe, they arrive with the real-window mode (step 7), and that mode
is local-only.

**5. Inert in release builds — the hook does nothing without its flag.** The hook
ships in every build and is gated at runtime: with no `--test-hook` argument the
listener is never created, no state is read, no input path changes, and the
application behaves exactly as it does today. Nothing in a config file, a
setting or the environment enables it, so a released binary cannot be listening
by accident. With the flag, the hook logs one line to stderr when the listener
starts (`test hook listening on <endpoint>`), so every run's log records that
the application was driven by a hook and a hook-driven screenshot can never be
mistaken for a person's. `--test-hook` with a path the process cannot create is a
startup failure with a non-zero exit, never a run that silently proceeds — an
adapter is told "unavailable" and the run fails.
  **The test-build route was the owner's point P2 and was rejected (P2-B).** Had
  it been picked, CI **would have had to build and run that separate build** for
  the GUI test job: its own build step, and a first action that is a `hello`
  against the hook, failing the run when the hook does not answer. That
  obligation stands whoever revisits this point, and so does the reason it is
  stated here: such a job must not inherit the headless suites' trap — those skip
  silently when the native core is unavailable and CI stays green (ADR-0150's own
  risk note, restated in the squad spec's testing decisions) — and a skipped GUI
  test run is a failed GUI test run. What the owner picked instead is one build
  for everybody, which is why the shipped path and the tested path are the same
  file and no CI plumbing is needed to keep two of them in step.

**6. The target adapter interface: seven operations, and the runner knows
nothing else.** An adapter is the only thing the runner talks to; it is selected
by the script's `target` id from a registry and implements:

- `capabilities()` — advertised before anything runs:
  `{actions:[...], checks:[...], input_families:[...], variants:{axis:[values]}}`.
  The validator compares the script against it and rejects a script naming an
  action, check or variant value the adapter does not advertise, naming what is
  missing. An adapter that advertises a name it does not implement fails the
  shared contract suite.
- `launch(fixtures)` — resolves the script's fixtures first (ROM by No-Intro
  SHA1 per ADR-0003, pack, settings profile, files placed on disk), then starts
  the application and returns a session. An adapter that cannot load what it
  needs **fails** — it never skips: a green run means the steps executed.
- `session.inject(action, args)` — one action, acknowledged.
- `session.check(name, args)` — an objective observation from UI state, a file
  or a log line. `ui.*` comes from the hook's snapshot; `fs.*` and `log.*` are
  evaluated by the runner and are therefore adapter-independent. A check never
  calls a model, and a check naming an id the application does not have is
  `failed (unknown id)`, never `false` — a renamed control must break the run
  loudly, not silently change what a case measured.
- `session.wait(condition, timeout)` — returns `met` or `timeout`; the adapter
  owns the polling of its own target and never raises on timeout. A capture is
  taken only after `met`, so no screenshot lands mid-transition, and a timeout is
  a `failed` step that names what it waited for, never a hang.
- `session.capture()` — a PNG of the application window only, never the desktop,
  returning path, size and sha256.
- `session.teardown()` — closes the application and restores settings and
  fixtures, reporting what it could not restore. A crash is reported, not raised:
  the runner marks the batch `failed (process crash)` with the captured output
  and starts the next batch in a fresh process.

The interface is the whole contract: application knowledge lives in the adapter,
test knowledge in the script, and a second application is a new adapter with no
runner change. An adapter is tested against the shared contract suite (launch,
inject, check, wait, capture, teardown, and advertised capabilities matching what
it implements), so the bar is the same for the emulator's adapter and the next
one.
  **Where each piece lives (the owner's point P1, picked A).** The runner, the
  format and the dashboard live with the squad, in the agent-squad plugin's own
  repository: they are application-independent, and a second target must not
  require a change here. This repository owns the emulator adapter, the in-app
  hook and the pilot script's rendered Markdown, plus the doc check that keeps
  that Markdown equal to its script. The cost is accepted and named: two
  repositories to keep in step, and a plugin release is needed before a script
  can run.

**7. The script format: JSON, versioned by a `format` string, vocabulary
namespaced.** A script is one JSON object. The human-readable view is the
rendered Markdown, so readability is not the source file's job, and JSON is the
format the runner reads and the dashboard renders **without a converter** —
whatever parses the file needs no step between the committed artifact and the
thing that consumes it (the doc checks are not stdlib-only: ADR-0137's
2026-09-15 amendment pins `PyYAML==6.0.3` in `scripts/requirements.txt` and
`checks.yml` installs it, which is why "a stdlib-only check can parse it" is not
the reason — item 7 and the Owner's picks section):

```
{
  "format": "gui-test/1",
  "name": "play-pad-only",
  "target": "mesen-gui",
  "requires": {"actions":["pad.press","window.mode"], "checks":["ui.screen","ui.focused","fs.exists"]},
  "variants": {"window.mode": ["windowed","fullscreen"]},
  "fixtures": {"rom": {"path":"<library>/Contra (USA).nes","sha1":"..."}, "settings": {...}},
  "batches": [
    {"id":"home", "setup":[], "teardown":[],
     "steps":[
       {"id":"home.focus-continue", "precondition":"ui.screen == play.home",
        "action":{"name":"pad.press","args":{"button":"Right","ticks":4}},
        "wait":{"check":"ui.focused == play.home.continue","timeout_ticks":120},
        "check":{"name":"ui.focused","args":{"is":"play.home.continue"}},
        "expect":"the Continue card has focus on the Home screen",
        "variants":{"window.mode":"windowed"}, "severity":"major", "mode":"automated"}
     ]}
  ]
}
```

Metadata (name, target, required capabilities, variant axes and values,
fixtures), batches (a setup, a teardown, an ordered list of steps), and steps (an
id, a precondition, an action, a wait, an optional objective check, an
expected-screen description for the vision fallback, the variant values it
applies to, a severity, and `automated` or `manual`). Names are
`<namespace>.<verb>` and the namespaces are closed:

- Actions: `pad.*` (hook, step 2), `key.*` (hook, step 4), `text.*` and
  `pointer.*` (real-window mode, step 7), `window.*` (mode and size, an adapter
  variant axis), `nav.goal` (the navigation decision's action, restricted to
  preconditions, setup and recovery — rejected as the action of a step under
  test).
- Checks: `ui.*` from the hook's snapshot, `fs.*` and `log.*` evaluated by the
  runner. `emu.*` (RAM) is reserved and not part of v1: when a script needs it,
  it is added by amending this ADR, not by a script inventing a name and not by
  widening the hook's snapshot with an emulator register view.
- Durations and waits: the unit is keyed by whether the emulated clock advances
  (§4): **`frames` / `timeout_frames` only while a game is loaded and not
  paused**, and **`ticks` / `timeout_ticks` in every other state** (no game,
  paused, the pause overlay, the load card, the picker). A step whose unit does
  not match the live state is malformed — the validator and the runner reject it
  — because the emulated frame counter does not move there and the step would
  wait forever on a number that never moves. So the pilot script's Home-screen
  step is `{"name":"pad.press","args":{"button":"Right","ticks":4}}` with
  `{"check":"...","timeout_ticks":120}`, and the same button held in a running
  game is `{"button":"Right","frames":4}`. Both units are application-reported counters,
  never host time (ADR-0157 §1). Every `wait` carries its `timeout_*`; a wait may
  also name a check predicate, and then the timeout applies to it.

A script's `format` is the version of the format, and a validator that does not
know the string refuses the file rather than guessing.

**8. Verdicts are the runner's, evidence is the run's.** The hook answers, the
adapter observes, and the runner decides: an objective check decides `passed` or
`failed` whenever one exists, and a vision verdict is asked only when none does,
with the model seeing the application window and the step's expected text and
never being able to overrule a check. `needs review` and `pending` steps do not
fail a run but are listed in the CI job summary and on the dashboard; any `failed`
step fails the run. Every automated step's capture is kept with its verdict.

## Owner's picks (2026-10-09)

The three points this ADR left open were answered in session, in one answer:
**"A/A/A (Recommended)"** — P1 = A, P2 = A, P3 = A. Each is folded into the
Decision: P1 into item 6, P2 into item 5, P3 into item 7. The alternatives below
were **not** picked and are recorded only so a later reader knows what was
already weighed and why it lost.

- **P1 — where the pieces live. Picked: A.** The runner, the format and the
  dashboard live with the squad (the agent-squad plugin, a separate repository:
  they are application-independent); the emulator adapter, the in-app hook and
  the pilot script's rendered Markdown live in this repository, and the doc check
  that keeps the rendered Markdown equal to its script runs here. *Rejected, B:*
  everything in this repository — it loses because an application-independent
  runner would live in the emulator's repository, which is where the squad's
  other work does not live. A's cost, accepted: two repositories to keep in step
  and a plugin release before a script can run.
- **P2 — the release-build posture (item 5). Picked: A.** The hook is in every
  build and inert without its flag — one binary to build, sign and ship, CI
  drives exactly the binary a user runs, and the flag-off path is the default
  path. *Rejected, B:* a separate test build carrying the hook — it loses on the
  second build to keep signed and shipped, and on the CI obligation item 5
  states (a job that builds and runs that build, where a missing hook must fail
  the run, never skip).
- **P3 — the script source syntax. Picked: A.** JSON, per item 7 — the format
  the runner reads and the dashboard renders without a converter, with the
  readable view rendered as Markdown anyway. *Rejected, B:* YAML as the source,
  rendered to JSON or read directly — pleasant to hand-edit, but it buys a
  converter step in the path the runner and the dashboard both walk, and the
  "the doc checks are stdlib-only, so JSON avoids a parser dependency" argument
  does not hold: ADR-0137's 2026-09-15 amendment pins `PyYAML==6.0.3` and
  `checks.yml` installs it, so a parser is already there.

## Consequences

- **The application pays a naming tax.** Every control a script checks needs a
  stable `AutomationProperties.AutomationId`, and the id is part of the app's
  test surface: renaming one is a breaking change to scripts, caught by the
  run's `failed (unknown id)` rather than by a silent `false`. The tax is paid
  where the control is declared, not in the scripts.
- **Two ways to drive the GUI now exist.** The boundary is stated in item 6 and
  must be written into `UI.HeadlessTests/AGENTS.md`: `UI.HeadlessTests` asserts
  wiring in-process, the hook drives behavior a person would judge from outside.
  Without that line, every new rule gets two homes.
- **A shipped hook is a shipped surface.** With P2-A picked, the listener's code is in
  every release, so the flag-off path needs a test of its own (the flag absent ⇒
  no endpoint exists after startup), and the security review of the change is the
  review of that path. The exposure is local-only and grants no privilege the
  caller does not already have; it must not grow a network transport later
  without a new ADR.
- **The `tick` counter is load-bearing and new.** ADR-0157 §1 exists because
  host-time waits flake; the GUI has no advancing emulated clock outside a running game, so
  a UI counter reported by the application is what keeps that flake out. A wait
  that spends host time is a defect, not a convenience.
- **`nav.goal` spends money and `emu.*` is not in v1.** A run containing goal
  steps is local-only until the navigation decision says otherwise, and a CI run
  marks those steps `pending`; a script needing RAM reads waits for an amendment
  to this ADR.
- **The agent-squad pieces start in a local fork.** With P1-A picked, the runner,
  the script format and the dashboard belong to agent-squad, but there is no
  write access to Korck-lab/agent-squad: until its owner accepts them they are
  developed in a local fork of agent-squad and delivered upstream as a patch set.
- **What this ADR does not test, and who carries it.** This is a documentation
  change. The squad's end-to-end suite **does not exist yet** — it is created
  with the format validator, in its own ticket (#1179, the slice that builds the
  format validator, the rendered Markdown and the squad's end-to-end suite). Issue #1178's
  acceptance criterion 5, "adds its own end-to-end case to the GUI test squad
  e2e suite, and the full suite stays green", is therefore **carried by #1179**
  and lands with that suite, not with this file: the case is a three-step script
  run against a fake adapter, plus the doc check that fails when a committed
  Markdown view differs from its render. Nothing here is dropped — it is parked
  on the ticket that can express it, because a suite asserted to exist before it
  exists is a criterion that can only be satisfied by writing it in the wrong
  repository.
- **Traps left behind.** An adapter that skips instead of failing when the hook
  or the native core is missing turns this whole harness into the green-but-blind
  CI the squad spec names; a check that matches display text breaks on the next
  label edit; a validator that accepts a name an adapter does not advertise moves
  the failure to the middle of a run. Each is named above where it is cheapest to
  prevent.
