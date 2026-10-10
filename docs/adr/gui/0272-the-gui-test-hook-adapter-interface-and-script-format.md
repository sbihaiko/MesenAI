# ADR-0272: The GUI test hook, the target adapter interface and the versioned script format

- Status: proposed (2026-10-09). Three points are the owner's to pick (the
  "Open points" below); everything else is decided here, and the implementation
  tickets read it as binding once the owner's picks are in and the status
  becomes `accepted`. Not implemented.
- Date: 2026-10-09
- Related: the GUI test squad spec and the navigation decision that goes with
  it, both carried by PR #1201 and not in `main` yet — the navigation decision
  is ADR id 0271 and is deliberately not written as `ADR-NNNN` on this line:
  `docs/adr/gui/0271-*.md` does not exist in `main` yet, and
  `verify_adr_refs.py` fails on a citation of an id with no file (the citation
  is added to this line the day PR #1201 lands). Prior art: ADR-0157 §1–§3 and
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
  second binary splits the harness from the shipped application. Both carry over
  verbatim: the hook's injected pad is counted in emulated frames and delivered
  through the same `IInputProvider` path, and the hook's own switch is a runtime
  flag (§4 below), not a second build. What §3 does **not** decide is the
  application-side gate, which is the owner's point P2.
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
wait may spend host time (ADR-0157 §1). The navigation decision carried by PR
#1201 (id 0271, not in `main` yet) widens this snapshot beyond the active screen
and the focused control to **visible controls and menu options**, which is what
items 3 and 4 of that decision need; this ADR is that surface.

**4. Input is injected inside the application, counted in frames.** `inject`
carries one action, e.g. `{"op":"inject","action":"pad.press",
"args":{"button":"Right","frames":4}}`. Buttons are named by the emulator's own
key names (`BaseControlDevice::GetKeyNameAssociations()`, as ADR-0157 §2 does),
so one name drives a NES, GB and SMS pad and the script is console-independent.
Injection goes through the same `IInputProvider` path the headless harness uses
and overlays physical input rather than replacing it: a pad plugged into the
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
  **The test-build alternative (owner's point P2).** If the owner picks a build
  that carries the hook and a build that does not, then CI **must build and run
  that test build** for the GUI test job: the job gets its own build step and its
  first action is a `hello` against the hook, which fails the run when the hook
  does not answer. It must not inherit the headless suites' trap — those skip
  silently when the native core is unavailable and CI stays green (ADR-0150's own
  risk note, restated in the squad spec's testing decisions) — and a skipped GUI
  test run is a failed GUI test run.

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

**7. The script format: JSON, versioned by a `format` string, vocabulary
namespaced.** A script is one JSON object (the human-readable view is the
rendered Markdown, so readability is not the source file's job, and JSON is what
a stdlib-only doc check can parse — ADR-0137):

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
        "action":{"name":"pad.press","args":{"button":"Right","frames":4}},
        "wait":{"check":"ui.focused == play.home.continue","timeout_frames":120},
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
- Waits: `frames` where the target has emulated frames, `tick` where it does not
  (a pure-GUI step has no emulated clock), always with a `timeout_*`; a wait may
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

## Open points (the owner picks; A is the recommendation)

**P1 — where the pieces live.** (A, recommended) The runner, the format and the
dashboard live with the squad (the agent-squad plugin, a separate repository:
they are application-independent); the emulator adapter, the in-app hook and the
pilot script's rendered Markdown live in this repository, and the doc check that
keeps the rendered Markdown equal to its script runs here. (B) Everything lands
in this repository. Cost of A: two repositories to keep in step and a release of
the plugin before a script can run; cost of B: an application-independent runner
living in the emulator's repository, which is where the squad's other work does
not live.

**P2 — the release-build posture (item 5).** (A, recommended) in every build,
inert without its flag — one binary to build, sign and ship, CI drives exactly
the binary a user runs, and the flag-off path is the default path. (B) a separate
test build that carries the hook — a smaller shipped attack surface, at the cost
of a second build to keep signed and shipped, and the CI obligation item 5 states:
the GUI test job must build and run that build, and a missing hook must fail the
run.

**P3 — the script source syntax.** (A, recommended) JSON, per item 7: parseable
by a stdlib-only check in either repository, and the readable view is rendered
anyway. (B) YAML as the source, rendered to JSON or read directly — pleasant to
hand-edit, at the cost of a parser dependency in whatever repository runs the doc
check (ADR-0137: the doc checks are stdlib-only).

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
- **A shipped hook is a shipped surface.** With P2-A the listener's code is in
  every release, so the flag-off path needs a test of its own (the flag absent ⇒
  no endpoint exists after startup), and the security review of the change is the
  review of that path. The exposure is local-only and grants no privilege the
  caller does not already have; it must not grow a network transport later
  without a new ADR.
- **The `tick` counter is load-bearing and new.** ADR-0157 §1 exists because
  host-time waits flake; the GUI has no emulated clock when no game is loaded, so
  a UI counter reported by the application is what keeps that flake out. A wait
  that spends host time is a defect, not a convenience.
- **`nav.goal` spends money and `emu.*` is not in v1.** A run containing goal
  steps is local-only until the navigation decision says otherwise, and a CI run
  marks those steps `pending`; a script needing RAM reads waits for an amendment
  to this ADR.
- **What this ADR does not test.** This is a documentation change; the squad's
  end-to-end suite is created with the runner (squad spec step 2), and the
  ADR's own end-to-end case is the first slice's — a three-step script against a
  fake adapter, plus the doc check that fails when a committed Markdown view
  differs from its render. The acceptance criterion "adds its own e2e case" is
  therefore satisfied by the runner slice, not by this file.
- **Traps left behind.** An adapter that skips instead of failing when the hook
  or the native core is missing turns this whole harness into the green-but-blind
  CI the squad spec names; a check that matches display text breaks on the next
  label edit; a validator that accepts a name an adapter does not advertise moves
  the failure to the middle of a run. Each is named above where it is cheapest to
  prevent.
