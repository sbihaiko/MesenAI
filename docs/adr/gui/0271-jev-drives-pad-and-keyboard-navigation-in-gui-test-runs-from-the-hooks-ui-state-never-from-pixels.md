# ADR-0271: Jev drives pad and keyboard navigation in GUI test runs from the hook's UI state, never from pixels

- Status: accepted (2026-10-09). The owner's go-ahead for the whole ADR is,
  verbatim: *"vai com A"* (2026-10-09), which picked Decision 6 = **A**
  (local only); the owner agreed to Decisions 1–5 in the same session. Not
  implemented. It is listed as slices T.1–T.2 (#1199, #1200) in
  `docs/roadmap/PRD-mesence-enhancement-ecosystem.md` Part B §8.
- Date: 2026-10-09
- Related: GUI test squad spec (steps 9b #1199 and 9c #1200; the rest of the squad is #1178–#1198), ADR-0238, ADR-0242, ADR-0247, ADR-0157
- Supersedes / amends: amends the scope of ADR-0238 (Jev outside the recorder
  stall helper); ADR-0238's recorder use is unchanged.

## Context

GUI test runs drive the emulator's GUI through an in-app test hook (pad and
keyboard input injected inside the app, UI state read from it). Many steps
only need to *reach* a screen before the behavior under test: a fixed button
sequence for every precondition breaks on every layout change, and a vision
model choosing each move costs a model call and seconds per move.

Jev (`typesafe/jev-1.13`, OpenRouter decisions API) answers a Choice over a
closed set from typed JSON state in about 0.6 s for about US$ 0.00002 per
decision (measured under ADR-0238). It is **text only**: OpenRouter's own
description says it takes no images. ADR-0238 restricts it to the recorder's
stall helper, with RAM-derived state, no pixels, no CI calls and no key in CI.

Alternatives considered and set aside:

- **`typesafe/jev-router` with screenshots.** It accepts images only by
  routing each request to a generative model chosen per request, billed at
  that model's price (the listing shows a variable price). That is the cost
  and latency of a vision call, with a model that is neither fixed nor
  approved in advance, and that can differ between two runs of the same
  screen.
- **A vision model turning the screenshot into JSON for Jev.** One vision
  call per move dominates cost and latency and adds an extraction error the
  hook does not have; the same model could choose the move itself.

Non-goals: Jev never decides a verdict; Jev never drives the mouse; Jev
never chooses the action of a step under test; this ADR does not touch the
product's own Jev features (ADR-0242, ADR-0247).

## Decision

1. **State comes from the hook, never from pixels.** The Jev state is the UI
   state the test hook reads, as typed JSON: active screen, focused control,
   visible controls, menu options. No screenshot or image reaches Jev.
2. **Pad and keyboard only.** Options are the actions the target adapter
   advertises for the step's input family. Mouse steps keep fixed targets:
   pointer position, hover and drag are geometry the state JSON does not
   describe.
3. **`nav.goal` serves setup, preconditions and recovery only.** A step names
   a goal as a check-vocabulary predicate (for example
   `ui.screen == Settings.Display`); the runner asks one Choice per move,
   withdraws moves that already failed from a state (as ADR-0238 §3 does),
   and caps decisions and spend per goal. Past a cap the step ends
   `failed (goal not reached)` with the last state. The validator rejects a
   `nav.goal` as the action of a step that carries the case under test.
4. **The exploratory trap sweep uses the same mechanism**, steering toward
   unvisited screens and failing on a screen no advertised action leaves
   within a bound.
5. **Recorded and replayable.** Each decision is a sub-node of its step
   (options, choice, probabilities, served snapshot, cost) and counts against
   the run's spend cap. A replay reuses the recorded moves and never calls
   Jev.
6. **Where Jev may run — owner's pick: A.**
   - **A. Local only (chosen).** No key in CI; a CI run marks `nav.goal` and sweep
     steps `pending`, listed in the job summary.
   - **B. Local and CI.** An OpenRouter key becomes a CI secret scoped to
     the GUI test job; ADR-0238's "no key in CI" no longer holds for that job.

## Consequences

- The hook's UI state must include active screen, focused control, visible
  controls and menu options. This ADR requires those four fields, and the
  hook ADR (open PR #1202) must provide them.
- Scripts written with goals survive layout changes on the path; a
  regression *on* that path is caught only by the steps that test it with a
  fixed action, so scripts keep those steps.
- Under A, CI coverage of goal-driven steps is zero until a person runs them
  locally; under B, CI gains a paid external dependency and a secret to
  rotate.
- A target with no hook (a third-party app, a native OS dialog) gets no goal
  navigation from this ADR; vision-to-JSON is reconsidered only when such a
  target needs it.
