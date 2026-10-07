# ADR-0263: UI pull requests run the ADR-0249 render gate against a built core, in a path-filtered Linux job outside build.yml

- Status: accepted (2026-10-06). **Decided by the autonomy panel standing in
  for the owner** on issue #968, ruling **AGREED 2–1 — option (a), outside
  build.yml**. Composition, quoted from the ruling: *"adversarial fallback
  (Codex out of usage until 23:42); lenses: Anthropic panel lens B,
  agy/Gemini 3.8 Flash sitting in; split → blind third lens Grok 4.6."*
  Majority reasons, quoted from the ruling: *"ADR-0249 Decision 5 wants
  renders attached to UI PRs before merge; (b)/(c)/(d) never reach the
  ordinary review path; cost is one path-gated ubuntu core build, not the
  10-job matrix ADR-0191/0200/0203 cut."* Dissent (lens B), verbatim:
  **"Other: reuse the core that the PR gate already builds. ... shard 1 would
  upload that file as an artifact. A render job (or headless-ui-tests)
  declares needs: on it, downloads it, sets MESEN_CORE_LIB and runs the render
  and wireframe classes. Limit it to PRs that touch UI/** or UI.HeadlessTests/**.
  Fallback if the owner refuses an ADR-0131/ADR-0122 amendment: (b)."** — kept
  by the ruling as an implementation option (Decision 4). Accepting this ADR
  is a request for work: the implementation is a **separate slice** and is
  **not implemented in this PR**, which is docs only.
- Date: 2026-10-06
- Related: ADR-0249 Decision 5 (the render gate; PNGs attached to UI pull
  requests before merge), ADR-0122 invariant 9 (core builds stay out of the
  host-free job), ADR-0131 (the `unit-tests.yml` contract invariants),
  ADR-0191, ADR-0200 and ADR-0203 (the CI cost policy and `build.yml`'s
  `prod`-PR-plus-dispatch trigger), ADR-0150 (Avalonia.Headless), issue #968.
- Supersedes / amends: none. ADR-0122 invariant 9, ADR-0131 and
  ADR-0191/0200/0203 are respected, not amended.

## Context

ADR-0249 Decision 5 makes the headless suite render each implemented
wireframe, and says the PNGs are kept as CI artifacts and attached to UI pull
requests so a person compares them with `docs/media/gui-redesign/` before
merge. In practice the job that runs those classes on an ordinary PR builds
no MesenCore (`.github/workflows/checks.yml`), and every render class gates on
`Assert.SkipWhen(!NativeCore.IsAvailable, …)`. So the `player-renders`
artifact on a normal PR is empty and the gate passes vacuously.

Two accepted decisions fence where a core build may go. ADR-0122 invariant 9:
anything that needs the `core` makefile target belongs in
`build.yml`/`tests.yml`, never in the host-free job (ADR-0131 states that
contract). And `build.yml` runs only on pull requests into `prod` or a manual
dispatch (ADR-0200, ADR-0203), after ADR-0191 cut the per-push matrix for
cost. No existing workflow builds the core for an ordinary UI PR.

## Decision

1. **One Linux job builds MesenCore and runs the render gate against it.** It
   runs the ADR-0249 render and wireframe classes with `MESEN_CORE_LIB` set to
   the core it built, and uploads a **non-empty** `player-renders` artifact (a
   PNG plus a `.wireframe.md` per implemented W-id).
2. **It runs on every pull request that touches `UI/**`,
   `UI.HeadlessTests/**`, or the Player theme**, path-filtered, on
   `ubuntu`. The repository is public, so Actions minutes are not a spend
   carve-out; the cost is one path-gated core build, not the matrix
   ADR-0191/0200/0203 cut.
3. **It sits outside `build.yml` and outside the core-free headless job**, as
   a sibling workflow or job. `build.yml` keeps its `prod`-PR-plus-dispatch
   trigger (ADR-0200, ADR-0203), and the host-free job keeps building no core
   (ADR-0122 invariant 9, ADR-0131).
4. **Allowed implementation variant (lens B's artifact reuse).** If the core
   that shard 1 of the PR gate already builds is usable by the headless host,
   shard 1 may upload that library as an artifact and the render job may
   declare `needs:` on it, download it, set `MESEN_CORE_LIB` and run the
   render and wireframe classes under the same path filter — saving a second
   compile. Either way, the render job itself stays outside `build.yml` and
   outside the core-free headless job.

## Consequences

- On a UI pull request the render classes report `Passed: N > 0`, not
  skipped, and the artifact holds what #951's per-region reports compare.
- A deliberately broken theme token turns the job red; the gate stops being
  vacuous.
- A PR that touches no UI path pays nothing.
- The `checks.yml` comment and upload step that admit an empty folder become
  stale once the job lands and are corrected in the implementation slice.
- Implementation is a separate slice, tracked from #968; no workflow changes
  with this ADR.
