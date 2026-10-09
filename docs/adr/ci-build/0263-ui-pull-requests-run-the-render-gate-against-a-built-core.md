# ADR-0263: UI pull requests run the ADR-0249 render gate against a built core, in a path-filtered Linux job outside build.yml

- Status: accepted (2026-10-07) — implemented in the same change (#968),
  under CLAUDE.md's same-turn rule: it ships with unit tests covering the
  decision (`scripts/test_verify_render_gate.py`), and the go-ahead is quoted
  here and in the PR body. Ratified on issue #968 by the owner's designated
  human proxy, GPT Astra fast (`codex exec -m gpt-6-astra -c
  service_tier=fast`, read-only, origin/main). The owner's own sentence
  (2026-10-07), verbatim: *"se precisar de ajuda para decidir use o gpt astra
  fast como proxy humano"*. The proxy's ruling, verbatim: **"#968 | PICK: per
  UI PR, one path-filtered Linux render job in `tests.yml` | ADR-0249 requires
  fresh renders before UI changes merge; dispatch/prod-only testing misses
  that review point. One Linux core build is a bounded, reversible cost
  without restoring the release matrix on ordinary PRs. Current CI also
  compares committed renders, but those cannot validate an unrendered layout
  change. | Conditions/limits: preserve `build.yml` triggers and the
  host-free boundary; cover UI, headless tests, render specifications and
  gate dependencies in filters; cancel superseded runs. Require successful
  core loading, non-skipped render cases, fresh PNG/report pairs for mapped
  implemented W-ids, and a deliberately broken theme token producing
  failure. | Files opened: ADR-0122, ADR-0191, ADR-0200, ADR-0203, ADR-0249,
  proposed ADR-0263; `.github/workflows/checks.yml`;
  `.github/workflows/build.yml`."** It ratified the autonomy panel's AGREED
  2–1 recommendation (option (a), outside `build.yml`), which this ADR
  carried while `proposed`.
- Date: 2026-10-06 (proposed), 2026-10-07 (accepted)
- Related: ADR-0249 Decision 5 (the render gate; PNGs attached to UI pull
  requests before merge), ADR-0122 invariant 9 (core builds stay out of the
  host-free job), ADR-0131 (the host-free jobs' invariants), ADR-0191,
  ADR-0200 and ADR-0203 (the CI cost policy and `build.yml`'s
  `prod`-PR-plus-dispatch trigger), ADR-0150 (Avalonia.Headless), issue #968.
- Supersedes / amends: none. ADR-0122 invariant 9 and ADR-0191/0200/0203
  are respected, not amended.

## Context

ADR-0249 Decision 5 makes the headless suite render each implemented
wireframe, and says the PNGs are kept as CI artifacts and attached to UI pull
requests so a person compares them with `docs/media/gui-redesign/` before
merge. In practice the job that runs those classes on an ordinary PR,
`headless-ui-tests` in `.github/workflows/checks.yml`, builds no MesenCore,
and every render class gates on `Assert.SkipWhen(!NativeCore.IsAvailable, …)`.
So the `player-renders` artifact on a normal PR is empty and the gate passes
vacuously. Measured locally on 2026-10-07 with an unloadable core: the render
classes report `Passed: 27, Skipped: 34` and `dotnet test` exits 0.

Two accepted decisions fence where a core build may go. ADR-0122 invariant 9:
anything that needs the `core` makefile target belongs outside the host-free
job (then `build.yml`/`tests.yml`). And `build.yml` runs only on pull requests
into `prod` or a manual dispatch (ADR-0200, ADR-0203), after ADR-0191 cut the
per-push matrix for cost. ADR-0191 also deleted `tests.yml`, and
`scripts/checks/verify_ci_platform_matrix.sh` section 1 fails if that file
name comes back.

## Decision

1. **One Linux job builds MesenCore and runs the render gate against it.**
   `.github/workflows/render-gate.yml` holds exactly one job, `render-gate`,
   on `ubuntu-22.04`. It builds the core (`make core`, ccache-backed, falling
   back to the cache `checks.yml`'s shard 1 saves on `main`), runs every
   `*RenderTests` class of `UI.HeadlessTests` with `MESEN_CORE_LIB` set to
   that core and a TRX logger, and uploads the `player-renders` artifact (a
   PNG per render plus a `.wireframe.md` per `W-P*` render) with
   `if-no-files-found: error` and `if: always()`, so a red gate still attaches
   what it rendered.
2. **It fails unless it really rendered.** `scripts/checks/verify_render_gate.py`
   wraps the test step: `core` loads the library and calls its `TestDll`
   export before any test runs; `verify` reads the TRX and the renders folder
   and fails on a skipped (`NotExecuted`) or failed render case — naming the
   core when that is the skip reason — on zero passed cases, on no PNG, on a
   `W-P*` PNG without its report (or a report without its PNG), and on a PNG
   older than the run. A broken theme token fails the render assertions
   themselves (ADR-0249 Decision 5), which `dotnet test` already turns red.
3. **It runs only on pull requests that can change a render.** The
   workflow's only trigger is `pull_request` into `main`, path-filtered to
   `UI/**`, `UI.HeadlessTests/**`, the render specs
   (`docs/media/gui-redesign/**`, `scripts/render_gui_wireframes.py`,
   `UI.Tests/Theme/PlayerRenders/**`) and the gate's own dependencies
   (`InteropDLL/**`, `makefile`, the workflow and its check script). `Core/`
   is deliberately not listed: the render cases exercise no emulation, and a
   PR that breaks the core build is already red in `checks.yml` shard 1. A
   new push to the PR cancels the superseded run (`concurrency` on
   workflow+ref, `cancel-in-progress: true`).
4. **It sits outside `build.yml` and outside `checks.yml`**, in a workflow
   file of its own. `build.yml` keeps its `prod`-PR-plus-dispatch trigger
   (ADR-0200, ADR-0203); `checks.yml`'s host-free jobs keep building and
   linking no core (ADR-0122 invariant 9, ADR-0131, enforced by
   `verify_ci_platform_matrix.sh` section 4); and only a workflow of its own
   gets a real `paths:` filter, so an unrelated PR starts no run at all. The
   ruling named the file `tests.yml`; it is `render-gate.yml` instead,
   because `tests.yml` is the name ADR-0191 deleted and section 1 of that
   guard reserves — same job, a name the guard does not forbid. It is not a
   required check of the `main` ruleset: a required check that a
   path-filtered PR never starts would block that PR forever.
5. **The CI-minutes reasoning, against the cost policy.** ADR-0191 cut the
   per-push binary matrix (14 jobs, every OS) because it ran on every change
   and produced nothing a pull request consumed; ADR-0200 and ADR-0203 put
   the release matrix back only on `prod` pull requests and dispatch. This
   job restores none of that: it is one ubuntu leg, builds the core only (no
   LTO, no publish, no GUI binary), and runs only on the PRs whose render can
   change — the review point ADR-0249 Decision 5 names, which a `prod`-only
   or dispatch-only run never reaches. The compile is mostly a ccache hit for
   a PR that did not touch `Core/`. The repository is public, so
   standard-runner minutes are not billed; the bound that matters is queue
   time, and one path-gated leg per UI PR, canceled when superseded, is the
   smallest cost that makes the gate real. ADR-0122 invariant 9 is honored
   rather than amended: the core build lives in its own workflow, never in
   the host-free job. The change is reversible by deleting one file.
6. **The contract is pinned.** `scripts/test_verify_render_gate.py`, run by
   the `python-tests` job on every PR, feeds the checker a skipped render, a
   core that did not load, a failed case, an empty run, a missing TRX, no
   PNG, unpaired PNG/report, and a stale PNG (each must fail) plus a fresh
   real run (must pass), and asserts that the committed `render-gate.yml`
   still holds Decisions 1–4 — and that dropping the path filter, a required
   path, the cancellation, the ubuntu runner, either checker call or the
   empty-artifact error is reported.

## Consequences

- On a UI pull request the render classes report every case `Passed`, none
  skipped (61 of 61 locally on 2026-10-07 against a built core), and the
  artifact holds the fresh PNG/report pairs #951's per-region reports
  compare.
- A skipped render turns the job red: measured locally, the same run with an
  unloadable core exits 0 from `dotnet test` (27 passed, 34 skipped) and 1
  from `verify_render_gate.py verify`, one `FAIL` line per skipped case.
- A PR that touches no render path pays nothing.
- The Linux headless host had never loaded a real core on CI before this
  job; if it cannot (a missing system library on the runner), the `core`
  step names the library and the PR goes red, which is the gate working, not
  a flake.
- `checks.yml`'s `headless-ui-tests` keeps its empty, `ignore`-on-empty
  upload; its comment now points at this workflow for the fresh renders.
