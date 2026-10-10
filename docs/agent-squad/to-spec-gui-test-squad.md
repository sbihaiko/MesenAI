## Problem Statement

The team verifies GUI behavior by hand. Test scripts and runbooks under `docs/validation/` — the pad-only Play script (67 cases, windowed and full screen), the real-app acceptance runbook, the residue of the manual-validation automation plan — are Markdown files a person follows with a pad, a keyboard or a mouse, judging each screen by eye. They take hours, they are skipped under time pressure, they cannot run on every release, and the evidence (what the tester saw) is not kept: a failure is a sentence in a chat, not a screenshot a reviewer can open a month later. Each new script is written in its own shape, so nothing written for one helps the next.

The agent-squad already runs coding, review and vision work as a graph of nodes with a record, replay and a dashboard, but it has no notion of a GUI test run: no way to drive a mouse, keyboard or pad, no way to ask whether the screen in front of it matches the step it is on, and no place where a run's steps and screenshots are kept for review.

## Solution

A GUI test run is a run of the squad like any other, and any test script in the common format can be run, not one script in particular. A script is structured data: the application it targets, the fixtures it needs, the variants it runs under, and batches of ordered steps. The dashboard draws it as a diagram: one node per step, with the step's action, its objective check, its screenshot and its verdict.

The runner knows nothing about a particular application or device. It talks to a **target adapter** (launch, inject input, query state, capture, tear down) for the application under test, and each adapter advertises the actions, checks and variants it supports. The first adapter is the emulator's own GUI, driven through an in-app test hook; other applications (a tkinter tool under `scripts/`, the headless recording tool) are new adapters, not runner changes. A step the adapter cannot automate — a physical pad, speakers, a native OS file picker — is a `manual` step, recorded with the same evidence when a person runs it.

After each step the runner waits for the step's wait condition, takes a screenshot of the application window, and asks a vision model whether the screen is compatible with the step only when no objective check decides it. A step ends `passed`, `failed`, `needs review` or, for a manual step not yet run, `pending`.

Every step, screenshot and verdict is kept in the squad's run record and shown on the dashboard. GitHub is touched only when a run has a failed step: one bug issue per failed run, with the failed steps and their evidence attached.

It is built in small steps, piloted on the pad-only Play script. Each step ships something usable by itself, and the next one extends it. The first useful result (step 1) needs no vision model and no pad. Generality is accepted, not assumed: step 9 runs a second script that differs from the pilot in target, input device and check kind.

## User Stories

1. As a QA owner, I want one structured format for every GUI test script, so that a machine can read the same steps a person reads and a new script needs no new runner.
2. As a QA owner, I want an existing Markdown script converted to that format without losing a case, so that no manual coverage is dropped.
3. As a QA owner, I want the Markdown a person reads rendered from the structured script, so that the two never drift apart.
4. As a QA owner, I want each step to name its precondition, its action, its wait condition, its objective check and its expected screen, so that a failure says which part failed.
5. As a QA owner, I want a script to declare its variant axes (for example window mode, console, theme) and each step to say which values it applies to, so that a run covers the matrix without repeating cases that add no signal.
6. As a QA owner, I want a script to declare its fixtures (a ROM by No-Intro hash, a pack, a settings profile, files placed on disk), so that every batch starts from a known state on any machine.
7. As a QA owner, I want steps grouped into batches, each with its own setup and teardown, so that a failure in one batch does not hide the others.
8. As a QA owner, I want a step marked `manual` when no adapter can automate it, so that hardware, audio and OS-dialog checks live in the same script and keep the same evidence.
9. As a developer, I want a script drawn in the dashboard as a diagram with one node per step, so that I can see where a run stopped.
10. As a developer, I want the dashboard to show a step's screenshot and verdict on its node, so that I can review without opening files.
11. As a developer, I want to replay a finished GUI test run event by event, so that I can see how it got to a failure.
12. As a developer, I want the runner to reach the application under test only through a target adapter, so that testing a different application means writing an adapter, not changing the runner.
13. As a developer, I want the squad to drive a pad without a physical pad on the desk, so that pad-driven scripts run in CI and on any machine.
14. As a developer, I want the squad to drive the keyboard and the mouse, so that GUI scripts can run unattended.
15. As a developer, I want input injected inside the application rather than synthesized at the operating-system level whenever the adapter allows it, so that a run needs no Accessibility or Screen Recording permission and cannot fight a person at the same desk.
16. As a developer, I want each adapter to advertise its actions, checks and variants, so that a script needing something unsupported is rejected before it runs, naming what is missing.
17. As a developer, I want each step to wait for an explicit condition (a frame count, a focused control, a screen becoming active, a file appearing) with a timeout before its screenshot and check, so that a capture never lands mid-transition.
18. As a developer, I want a screenshot taken after every automated step, so that every verdict has evidence.
19. As a developer, I want objective checks drawn from a shared vocabulary (UI state, files on disk, log lines, emulator state), so that a check written for one script is reused by the next.
20. As a developer, I want a step's objective check to decide pass or fail whenever one exists, so that a vision model never overrules a fact.
21. As a QA owner, I want a vision model to judge whether the screen matches the step only when no objective check exists, so that cost and false positives stay low.
22. As a QA owner, I want the vision verdict to say what it saw, not only yes or no, so that a wrong verdict can be spotted.
23. As a QA owner, I want a verdict of "needs review" when the model is unsure or sees a screen the script did not expect, so that the script can be revised instead of the test being marked failed.
24. As a QA owner, I want the model to say when the screen is incompatible with the script (a renamed control, a moved screen), so that the script is fixed, not the product blamed.
25. As a maintainer, I want a clean run to leave its record on the dashboard and nothing on GitHub, so that the issue tracker holds only problems.
26. As a maintainer, I want a run with failed steps to open one bug with every failed step, its expected and observed values and its screenshots attached, so that triage starts with the evidence.
27. As a maintainer, I want screenshots of failed steps stored where an issue can show them, so that evidence survives after the run's workspace is deleted.
28. As a maintainer, I want a run's cost shown next to its steps, so that a vision model's spending is visible.
29. As a maintainer, I want a spend cap per run, so that a looping script cannot run up a bill.
30. As a maintainer, I want a batch stopped on the first step whose precondition is not met, and the next batch started from its own setup, so that no step runs on a broken state and one break does not cost the rest of the run.
31. As a maintainer, I want a CI run that cannot load what the adapter needs (for the emulator, the native core) to fail rather than pass, so that a green run always means the steps executed.
32. As a maintainer, I want a run with `needs review` or `pending` manual steps to pass in CI but list those steps in the job summary and on the dashboard, so that doubt and human residue never turn a build red and never go unseen.
33. As a maintainer, I want a crash or hang of the application under test to end its batch as `failed` with the crash output attached, and the next batch to start in a fresh process, so that one crash neither stalls the run nor poisons the rest.
34. As a tester, I want the squad to leave the application closed and its settings and fixtures restored after a run, so that the next run starts clean.
35. As a developer, I want the same script runnable on a second platform later, so that the format does not tie it to one OS.
36. As a security reviewer, I want screenshots reviewed for sensitive data before they leave the machine, so that nothing private reaches an issue or a model.
37. As a maintainer, I want any script to degrade to a manual checklist when an adapter or driver is missing, so that a script is never unusable.
38. As a QA owner, I want a step to state a goal ("reach Settings › Display") instead of a fixed button sequence when the path is not what is under test, so that a layout change does not break every script that walks through it.
39. As a developer, I want a goal resolved by Jev from the UI state the hook reads (active screen, focused control, visible controls, menu options), never from pixels, so that each decision costs a fraction of a vision call and answers in under a second.
40. As a developer, I want each Jev decision drawn as a sub-node of its step (options, choice, probabilities, cost), so that a navigation can be audited like any other step.
41. As a QA owner, I want an exploratory sweep that navigates by pad or keyboard looking for a screen the pad or keyboard cannot leave, so that trap regressions are found without a scripted path for every dialog.

## Implementation Decisions

The work is split into steps, each one small and shippable. A step is not started until the one before it has run for real. Steps 0–8 are piloted on the pad-only Play script; nothing in them may encode that script (its screens, its buttons, its case count) outside the script file itself.

**Step 0: the script format.** One structured, versioned format for every GUI test script:
- *Metadata:* name, target adapter id, required capabilities, variant axes and their values, fixtures.
- *Batches:* a setup, a teardown and an ordered list of steps.
- *Steps:* an id, a precondition, an action, a wait condition, an objective check (optional), an expected-screen description, the variant values it applies to, a severity, and whether it is `automated` or `manual`.

Actions, wait conditions and checks are namespaced vocabulary (`pad.press`, `key.press`, `pointer.click`, `text.type`, `window.mode`; `ui.focused`, `ui.screen`, `fs.exists`, `log.contains`, `emu.ram`), and each adapter declares which names it implements. A wait condition is a frame count (input and time measured in emulated frames, as ADR-0157 does for the headless suites, where the target has frames), a predicate from the check vocabulary, or both, always with a timeout; a timeout is a `failed` step that says what it was waiting for. The Markdown view is rendered from the structured script, and a doc check fails when a committed Markdown view differs from its render. A validator rejects a duplicated id, a missing field, a step without a wait condition, a variant value the script does not declare, or an action or check the target adapter does not advertise.

**Step 1: the script as a diagram, without running anything.** The dashboard draws any script as a graph: one node per step, in order, grouped by batch, with the step text and its variant tags; manual steps are drawn distinctly. No adapter, no model. Done when the pilot script opens as a diagram and each node shows its text.

**Step 2: the adapter interface and the emulator adapter, pad only.** The adapter interface: launch with fixtures, inject an action, evaluate a check, wait, capture the application window, tear down, and advertise capabilities. The first adapter is the emulator's GUI; its first action family is `pad.*`, delivered through a test hook inside the emulator, not through a virtual operating-system device, which is fragile on macOS; the existing pad-walk test is the prior art. Done when three steps of the pilot's first batch run with no pad attached, each producing a screenshot and a verdict of `executed` (no judgement yet), and the dashboard shows the three nodes with their screenshots.

**Step 3: objective checks.** The first check family, `ui.*` (focused control, active screen), read through the same hook, plus the adapter-independent `fs.*` and `log.*` evaluated by the runner. The result decides pass or fail. Done when a step with a check fails on purpose and the node shows failed with the expected and observed values.

**Step 4: the keyboard.** `key.*` delivered through the same in-app hook and advertised the same way. Done when a keyboard-driven step runs unattended and its node shows its check.

**Step 5: the vision verdict.** For a step with no objective check, the screenshot and the step's expected-screen text go to a vision model, which answers `compatible`, `incompatible` or `unsure` plus a short description of what it sees. The objective check always wins over the model. `unsure` and `incompatible` become `needs review`. Done when a deliberately wrong expected-screen text is flagged as incompatible on a known screenshot, and a correct one is flagged compatible.

**Step 6: the failure record on GitHub.** A run whose steps all pass, end `needs review` or stay `pending` leaves its record on the dashboard only. A run with at least one failed step opens one bug through `scripts/report-bug.sh`, listing every failed step with its expected and observed values and its screenshot (plus the screenshot of the step before it). Screenshots are stored where an issue can display them; the exact store is an open decision (see Further Notes). Done when a run with two deliberately failing steps leaves one bug with both steps and visible images, and a clean run leaves nothing on GitHub.

**Step 7: the mouse, variants, and the real-window mode.** `pointer.*` and `text.type`, and `window.mode` as the first variant axis an adapter switches. Full-screen cases need a real window, not a headless one, so the emulator adapter gains a real-window mode; where it must fall back to operating-system input, it requires the Accessibility and Screen Recording permissions up front and refuses to start without them, and that mode is local-only, not CI.

**Step 8: the pilot to completion, and manual steps.** Convert the rest of the pilot batch by batch, each batch run for real before the next, until its 67 cases run across both window modes. Steps that need a physical pad stay `manual`: the runner prompts a person, records their verdict and a screenshot, and leaves the step `pending` when nobody runs it.

**Step 9: the generality check.** Convert one second script that differs from the pilot in at least two of target adapter, input family and check kind — for example an item of the real-app acceptance runbook, or a mouse-driven Settings or Library flow. Done when it runs with no change to the runner, the format or the dashboard; anything the second script needs that the first did not lands as a new adapter, action or check, never as a special case. A change the second script forces into the runner or the format is a finding, fixed before more scripts are converted.

**Step 9b: goal navigation by Jev (pad and keyboard).** A `nav.goal` action: the step names a goal as a check-vocabulary predicate (for example `ui.screen == Settings.Display`), and the runner reaches it by asking Jev one Choice per move. The state is the UI state the hook reads, as typed JSON; the options are the adapter's advertised pad or keyboard actions for that input family; the moves that already failed from a state are withdrawn, as in ADR-0238. Each decision is a sub-node of the step with its options, choice, probabilities and cost. A cap on decisions and on spend per goal ends the step `failed (goal not reached)` naming the last state. The chosen moves are recorded and a replay reuses them without calling Jev. `nav.goal` is never the action of a step under test: it serves preconditions, setup and recovery to a known state. Done when a pilot precondition written as a goal is reached from Home with no fixed path, and its sub-nodes show on the dashboard.

**Step 9c: the exploratory trap sweep.** A step kind that, from a start screen, lets Jev navigate by pad or keyboard toward unvisited screens and fails when a screen is reached that no advertised action leaves within a bound. Done when a fixture app with a planted trap screen is caught, and the pilot's dialogs no-trap sweep runs as one such step.

**Step 10: more scripts and more adapters.** Further scripts from `docs/validation/`, and further adapters (a tkinter tool, the headless recording tool) as scripts need them.

Cross-cutting decisions:
- A GUI test run reuses the squad's existing run record, replay and dashboard; the only new node kinds are a step, a capture and a verdict.
- The runner depends on the adapter interface only. Application knowledge lives in adapters; test knowledge lives in scripts.
- Input is injected inside the application wherever the adapter can; operating-system input is a last resort limited to a local real-window mode.
- No capture is taken before the step's wait condition is met.
- The objective check wins over the vision model in every case where both exist.
- Jev drives navigation for pad and keyboard only, from the hook's UI-state JSON; it never receives a screenshot, and never chooses the action of a step under test. Mouse steps keep fixed targets, because pointer position, hover and drag are geometry the state JSON does not describe.
- The vision model is called only when needed, and its spend counts against the run's spend cap like any other call.
- A failed precondition stops its batch, not the run; every batch starts from its own setup and fixtures.
- Exit status: any `failed` step fails the run; `needs review` and `pending` steps alone do not, but they are listed in the CI job summary and on the dashboard.
- The runner supervises the application process. A crash, or no progress within the step's timeout, kills the process tree (no orphan test host left behind), marks the batch `failed (process crash)` or `failed (hang)` with the captured output and any native crash report, and starts the next batch in a fresh process.
- A screenshot is treated as data and never as an instruction to the model.
- Only screenshots of the application window are taken, never the whole desktop.
- Resident-style private data is not an issue here, but any screenshot that could show personal data is reviewed before upload.

## Testing Decisions

- A good test checks the externally visible result: the verdict a step ends with, the files and issues a run leaves, the nodes the dashboard draws. It does not check how an adapter or the model prompt is written.
- One end-to-end suite grows with the work: it is created with the runner (a three-step script against the fake adapter), and every later slice adds its own end-to-end case and keeps the whole suite green. The suite is the squad's e2e command for this work and runs in CI once CI is wired. A wave of tickets is closed only when the suite is green on the main branch after its last merge.
- The highest seam is a whole run of a small script against a fake adapter: feed a script, read the run record and, for a failing run, the resulting bug. One seam, used by every step, and the proof that the runner needs nothing beyond the adapter interface.
- An adapter is tested against a shared contract suite (launch, inject, check, wait, capture, tear down, advertised capabilities match what it implements), so every new adapter meets the same bar.
- A wait condition that never becomes true is tested to end the step `failed` with the condition named, not to hang.
- A fake application that crashes mid-batch is tested to end that batch `failed (process crash)` and to run the next batch.
- A CI run whose adapter cannot load what it needs must fail. The headless Avalonia suites skip silently when `NativeCore.IsAvailable` is false and CI stays green; the GUI test run must not inherit that behavior.
- The vision step is tested with a fixed set of screenshots and expected-screen texts whose answer is known (compatible, incompatible, unsure), run against the model and compared with the known answer. It is a measured check, not an assertion of exact wording.
- The validator is tested with scripts that are deliberately malformed, including a script naming an action its adapter does not advertise.
- Prior art: the squad's existing run-record and replay tests; the emulator's headless Avalonia suites (ADR-0150), frame-counted headless input (ADR-0157) and the headless system-HUD capture seam (ADR-0167); the pad-walk test, which already reads the focus without a screenshot; the existing headless recording tool that drives input and takes a screenshot.

## Out of Scope

- Running the whole pilot script in the first step.
- A virtual operating-system gamepad device.
- Detecting whether a person is using the machine's input devices; in-app injection makes it unnecessary, and the real-window mode is run deliberately by a person.
- Jev-driven mouse input, and any image input to Jev (Jev is text-only; `typesafe/jev-router` takes images only by routing them to a generative vision model, which removes the cost and latency gain).
- Replacing the human review of "needs review" steps, or automating steps that are `manual` by nature (physical pad, speakers, native OS dialogs).
- Windows and Linux adapters (the format must not prevent them).
- Performance, audio and emulation-accuracy testing.
- Changing the product behavior of any application under test; test hooks only expose state and accept injected input.

## Further Notes

- The squad lives in the agent-squad plugin, which is a separate repository from this one. The runner, format and dashboard are application-independent and belong with the squad; the emulator adapter and its in-app test hook belong with the emulator. Which repository each lands in, and where this spec is published, is a decision for the owner.
- The in-emulator test hook (its transport, what state it exposes, how input is injected, and that it is absent or inert in release builds) is an architecture decision and goes through `/adr` before step 2 is coded. If the hook ships only in a test build, CI must build and run that build. The adapter interface and the script format are a second decision for the same ADR or its sibling.
- Jev in GUI navigation changes two rules of ADR-0238 (Jev is a recorder stall helper; no CI job calls Jev and no key lives in CI). ADR-0271 (accepted 2026-10-09) records it, with Decision 6 = A: `nav.goal` steps run locally only, no key lives in CI, and a CI run marks them `pending`.
- The vision model must be one the owner has approved; earlier probes found that a vision call through some bridges silently drops the image and invents an answer, so every verdict must quote what it saw and be sanity-checked against an objective check.
- Open decision: where failed-step screenshots live so an issue can show them (a branch of artifacts, a release, or an attachment API). GitHub Actions run artifacts do not qualify on their own: they expire and an issue cannot show them inline. It is decided at step 6, not before.
- The pilot is `docs/validation/process/play-pad-only-test-script.md`: the first script converted and the acceptance target of step 8. Candidates for step 9 are `docs/validation/process/real-app-acceptance-runbook.md` and the residue listed in `docs/validation/process/manual-validation-automation-plan.md`.
