# ADR-0191: CI compiles Linux only, the macOS release is built locally, Windows is retired from CI

- Status: accepted (2026-09-14, at the user's direction: "por enquanto limite qq compilação para mac apple silicon somente" and, asked "prefere no ci compilar somente linux e compilar o mac localmente quando necessário?", "sim"; implemented in the same change — the invariants below are the tests)
- Date: 2026-09-14
- Related: ADR-0131 (amended: the unit-test contract moves from `unit-tests.yml` to `checks.yml`), ADR-0137 (`make doc-checks` as the wiring point), ADR-0150 (`UI.HeadlessTests/`), PRD Part A §4 Phase 11 C.1 and C.4
- Supersedes / amends: ADR-0131's CI contract — every invariant it states for `unit-tests.yml` now binds the `ui-tests` and `headless-ui-tests` jobs of `checks.yml`, and the file it named no longer exists. Phase 11 C.1's line "a ruleset on `main` requires `Checks`, `ui-tests`, `headless-ui-tests` and the Windows `tests.yml` job" loses its last clause: there is no Windows job to require.

## Context

Three facts collided on 2026-09-14.

1. `build.yml` had been cut to `workflow_dispatch` on 2026-09-13/14 (#230)
   because its fourteen jobs — two Windows publish profiles, six Linux and
   AppImage compiler×arch legs, four macOS legs with a codesign step — cost
   more runner time than the commits they built. Phase 11 C.1 (#245) then put
   the compile back on the pull-request path as `checks.yml`'s
   `core-unit-tests` job, on Linux.
2. Phase 11 C.4 needs a shippable binary. The user's decision for it was
   narrow and explicit: "por enquanto limite qq compilação para mac apple
   silicon somente" — for now, every *binary* build is macOS Apple Silicon
   only. Not a matrix; one platform.
3. Asked whether CI should therefore compile Linux only and build macOS
   locally when it is needed, the user said yes, and added: "cancele github
   actions que falem o contrário e reorganize tudo" — cancel the Actions that
   say otherwise, and reorganize.

The workflow set as it stood contradicted all three. `tests.yml` (Windows
MSBuild + the upstream `PGOHelper … citests` accuracy suite) and
`unit-tests.yml` (the `ui-tests` and `headless-ui-tests` jobs) were both
`disabled_manually` and had produced no run since; they were dead files
describing a contract nobody executed, and C.1 had already had to work around
them by rebuilding two of their jobs elsewhere rather than re-enabling them.
`build.yml` still carried the Windows and macOS legs a dispatch would fire.
`.github/AGENTS.md` documented all of it as live.

Non-goals. This does not retire the Windows or macOS *product* — it retires
them from **continuous integration**. It does not change how the release is
made (that is C.4's `make release-macos`), does not touch `README.md` or
`docs/releases/`, and does not enable any currently disabled workflow.

## Decision

1. **CI compiles Linux only.** Every job that invokes a C++ or a .NET
   compiler runs on an `ubuntu-*` runner. No workflow in this repository may
   declare `runs-on:` naming a `macos-*` or `windows-*` runner. The single
   documented exception is `dotnet-format-check.yml`, which is
   `disabled_manually`, compiles nothing, and is pinned to Windows because
   `dotnet format` is only wired for the Windows-hosted `Mesen.sln` here; it
   stays listed by name in the verifier so that enabling it is a deliberate
   edit.

2. **`tests.yml` is deleted.** It was the only Windows compilation left and
   the only caller of the upstream ROM accuracy suite. That coverage is
   **not reproducible on Linux here**: `PGOHelper.exe` is an MSBuild target
   of `Mesen.sln` producing a Windows executable, and `citests` drives it
   against the private `nesdev-org/MesenTests` corpus through the Windows
   build's screenshot comparison. Porting it is a project, not a workflow
   edit. It goes away with Windows and comes back with Windows. ADR-0162
   already recorded the accuracy suite as "not in CI by decision"; this makes
   that literally true.

3. **`unit-tests.yml` is folded into `checks.yml` and deleted.** Its two jobs
   move verbatim, on `ubuntu-22.04`, as jobs of the always-on gate:
   - `ui-tests` — `./scripts/verify-ui-logic-firewall.sh` (ADR-0123 H5) then
     `dotnet test UI.Tests/UI.Tests.csproj`. It does **not** run
     `make core-unit-tests`: `checks.yml`'s own `core-unit-tests` job already
     compiles and runs that harness, and duplicating it would pay for the
     same compile twice and produce two check runs that fail for one reason.
     ADR-0131's note that the `ui-tests` job id covers both the C# and the
     C++ host-free suites is therefore withdrawn — after this change the id
     means exactly what it says.
   - `headless-ui-tests` — `dotnet test UI.HeadlessTests/UI.HeadlessTests.csproj
     -p:RuntimeIdentifier=linux-x64` (ADR-0150), kept as its own job so a
     headless-host failure never reds the cheap host-free leg. The RID
     override is required because `UI/UI.csproj` hardcodes `win-x64`, and it
     is also the known cross-RID `obj/` trap: a local run that restored for
     `osx-arm64` leaves artefacts a `linux-x64` restore trips over. A clean
     checkout (CI) or `rm -rf UI.HeadlessTests/obj` is what makes the two
     agree.

   ADR-0131's invariants come along unchanged and now bind these two jobs:
   never link `InteropDLL`/`MesenCore`, never require SDL2, never require a
   platform SDK or a ROM corpus, and pin `dotnet-version: 10.x` (the same pin
   `build.yml` uses; `dotnet-format-check.yml`'s `10.0.x` remains a separate,
   independent pin).

   The gate is therefore five jobs — `checks`, `python-tests`,
   `core-unit-tests`, `ui-tests`, `headless-ui-tests` — and each is a
   required status check of the `main` ruleset ("main gate", id 23377340).
   Renaming a job renames a required check: update the ruleset in the same
   pull request.

4. **`build.yml` is Linux only and stays `workflow_dispatch`-only.** The
   Windows and macOS jobs are deleted from the file rather than commented out
   or disabled with `if:` — the revision history is the archive. What remains
   is the six-leg Linux matrix and the two AppImage legs; neither consumed an
   artifact of a removed job, so nothing had to be rewired. The file's header
   states the policy, its date, and that the macOS release is `make
   release-macos` locally.

5. **The macOS binary is built locally.** `make release-macos` (Phase 11 C.4)
   injects the fresh `MesenCore.dylib` into the `.app`, codesigns ad hoc,
   fails on a hash mismatch and writes `SHA256SUMS` plus the commit. That
   hash check is the verification of the macOS artefact. There is no macOS
   runner to compare it against, by decision.

6. **Windows is retired from CI until the user lifts the rule.** Restoring it
   is a revert of the commit that landed this ADR, plus a line here saying
   why.

7. **Verification.** `scripts/checks/verify_ci_linux_only.sh`, wired into
   `make doc-checks`, is the test of this decision: it fails when a workflow
   declares a `macos-*` or `windows-*` runner (outside the one exception),
   when `tests.yml` or `unit-tests.yml` reappears, when `checks.yml` loses one
   of the five jobs or one of ADR-0131's invariants, or when `build.yml`
   regains a `push`/`pull_request` trigger or loses its policy header.
   `.github/AGENTS.md` Verification carries the plain greps alongside it.

## Consequences

- **No CI job compiles C++ on macOS or on Windows.** MSVC-only breakage is
  caught only when Windows returns — concretely the `/W4 /WX` class, of which
  the `getenv` C4996 trap is the recorded example: it passes on Linux and
  macOS and fails only under MSVC, so a pragma-guarded fix now lands unverified
  until a human builds on Windows. Apple-clang-only breakage is equally
  invisible to the gate; a local `make ui` is the only thing that sees it.
- **The macOS `.app` is verified by the local target's hash check, not by
  CI.** Nobody but the person running `make release-macos` observes that the
  bundle built, was signed, and carries a non-stale `MesenCore.dylib`. The
  `SHA256SUMS` + commit pair is what makes that run auditable after the fact.
- **The upstream ROM accuracy suite has no runner at all.** `tests.yml` is
  gone and its Windows-only toolchain is not reproducible here. A regression
  it would have caught now reaches `main`.
- **The gate got slower and broader.** Two dotnet jobs joined the three C.1
  jobs. They run in parallel with them, so wall clock is roughly unchanged,
  but a pull request now consumes five runner slots instead of three.
- `.github/AGENTS.md`'s `unit-tests.yml` and `tests.yml` contracts describe
  files that no longer exist; they are rewritten as the `checks.yml` contract
  in the same change. Any future reader following an old commit's link lands
  on a deleted path — that is deliberate and cheaper than keeping two
  stub files alive.
- Two questions are deliberately left to the user rather than decided here:
  whether the format-check workflows (`clang-format-check.yml`,
  `dotnet-format-check.yml`) come back — the clang one is Linux and cheap, the
  dotnet one is Windows and therefore currently forbidden by §1 — and whether
  `community-pack-drift-check.yml`, which CLAUDE.md describes as a daily job
  and which is `disabled_manually`, should be enabled. Neither is changed by
  this ADR.

## Alternatives

- **Keep `tests.yml` and `unit-tests.yml` disabled instead of deleting
  them.** Rejected: that is the state Phase 11 C.1 had to route around, and a
  documented-but-unrun workflow is exactly the rot ADR-0137 was written
  against. A disabled file also cannot be a required check, so it buys
  nothing.
- **Re-enable `unit-tests.yml` rather than fold its jobs in.** Rejected: two
  always-on workflows on the same triggers means two concurrency groups, two
  places to keep the dotnet pin, and a second file whose name the ruleset must
  track. One gate file is the point.
- **Comment the Windows/macOS jobs out in `build.yml`.** Rejected: a commented
  matrix is a merge-conflict magnet and reads as "about to come back". The
  revision history keeps them, and the header says where.
- **Keep one macOS leg in `build.yml` for signal.** Rejected by the user's
  first quote: the limit is on *every* compilation, and the macOS artefact is
  built locally by decision.
