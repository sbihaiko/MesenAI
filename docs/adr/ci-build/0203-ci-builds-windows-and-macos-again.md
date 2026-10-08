# ADR-0203: CI builds Windows and macOS again, on the existing triggers

- Status: accepted (2026-09-16, at the user's direction: "quero bild de windows e mac", narrowed by two follow-up choices — macOS Apple Silicon only, and the trigger set unchanged — and extended by "os links de binarios devem apontar para PROD")
- Date: 2026-09-16
- Related: ADR-0191 (the Linux-only decision this amends), ADR-0193, ADR-0200 (the `prod` pull-request trigger, which stays exactly as it is), ADR-0204 (the download channel that replaces §6), ADR-0131, ADR-0137, ADR-0150, ADR-0202, PRD Part A §4 Phase 11 C.1/C.4, `.github/workflows/build.yml`, `scripts/checks/verify_ci_platform_matrix.sh`
- Supersedes / amends: amends ADR-0191 §1 and §2 — CI is no longer Linux-only, and the macOS and Windows legs are restored. ADR-0191 §4 was already amended by ADR-0200 and is untouched here.

## Context

ADR-0191 (2026-09-14) cut `build.yml` down to Linux: two Windows publish jobs and four macOS legs deleted, the policy being every *binary* build macOS Apple Silicon only and CI compiling Linux only. That left the README shipping a Windows link labeled **frozen at the 2026-09-14 build** — it resolves only through nightly.link's fallback to an older run and can never refresh — and macOS with no CI leg, so a macOS break was found only by a hand `make release-macos`.

The user reversed the policy on 2026-09-16 ("quero bild de windows e mac"), narrowed by two choices from the same conversation: **macOS Apple Silicon only** and **the trigger set unchanged**. A second decision followed: the README's links named `main`, and the user asked that they name `prod`. That is more than a text edit — nightly.link resolves a branch against the `head_branch` Actions recorded for a run, which for a `pull_request` run is the PR's **source** branch and for a `workflow_dispatch` run is whatever `ref` the dispatch named. No run of `build.yml` has ever had `head_branch: prod`, because nothing has dispatched it with `--ref prod`; the existing `main` links resolved only because every `prod` pull request so far had `main` as its source.

Non-goals. This does not restore `push`, does not add an Intel macOS leg, does not make CI produce the published macOS release (still `make release-macos`, locally, §5), and does not make `prod` builds automatic — the freshness of the links depends on someone dispatching after a promotion.

## Decision

1. **A `windows` job returns, with the two publish profiles it had**: net10.0 single-file and net10.0 AoT, on `windows-2025-vs2026`; the README names the AoT artifact, so that is what users download.

2. **A `macos` job returns for Apple Silicon only**: `clang` and `clang_aot` on `macos-15`. The `macos-15-intel` legs are **not** restored — the release is arm64, so an Intel leg would build an artifact nothing links to. The old four-leg job becomes two.

3. **The triggers do not change.** `pull_request` filtered to the `prod` base branch, plus `workflow_dispatch` (ADR-0200). The legs run on a `prod` pull request or a dispatch, not on every push. Restoring `push` reintroduces what #230 and ADR-0191 removed: a full LTO/AOT matrix per push, and a push canceling the run the download links resolve against.

4. **The `if: github.event_name != 'pull_request'` guard does not come back.** Every deleted upload carried it; ADR-0200 removed it from the Linux jobs because a `prod` pull request runs the whole matrix and publishes nothing. The restored jobs publish on every trigger they have.

5. **The CI macOS artifact is not code-signed.** The deleted job signed with a Developer ID certificate from repository secrets — distribution signing belongs to the release path, where `scripts/release_macos.sh` ad-hoc signs and verifies the bundle. The CI leg proves the build compiles, it does not replace that.

6. **The README's on-demand links move from the `main` channel to the `prod` channel.** Every `nightly.link/.../workflows/build/main/...` URL becomes `.../workflows/build/prod/...`, and the refresh command becomes `gh workflow run build.yml --repo sbihaiko/MesenAI --ref prod`. `prod` is the releasable branch, so a downloader should track it. Promoting `main` into `prod` is then two commands: merge the promotion PR, then dispatch `build.yml --ref prod` so a run with `head_branch: prod` exists. Nothing automates the second — no `push` trigger on `prod` (§3) — so the links are as fresh as the last dispatch. (ADR-0204 replaces this channel: nightly.link resolves against a `push` run this workflow does not have.)

## Consequences

- **The README's Windows row stops being a lie**; it refreshes on the next `prod` pull request or dispatch, not on a schedule.
- **A macOS compile break is now caught by CI**, which the local-only policy could not do.
- **The matrix is 10 jobs again, not 14**: 6 Linux legs, 2 AppImages, 2 Windows profiles, 2 macOS legs.
- **Two thirds of the matrix never runs on an ordinary pull request** (the `prod` filter), so a Windows/macOS break can reach `main` and surface only when a `prod` pull request is opened; `gh workflow run build.yml --ref <branch>` catches it earlier. That is the user's choice, not an oversight.
- **The verifier is rewritten, not deleted.** `verify_ci_linux_only.sh` asserted the absence of exactly these runners, so it fails the moment this lands; it becomes `verify_ci_platform_matrix.sh`, which asserts the platform set, the trigger set, the absence of the `event_name` guard and of the `macos-15-intel` legs, and the Windows job's two-step shape.
- **The Windows job is two steps, and that is load-bearing.** The native `MesenCore.dll` is built by the MSBuild `-t:...UI` target, not by `dotnet publish`, which only copies it into the publish output. A single-step transcription fails publish not with a native compile error but with `MSB3030: Could not copy the file ...MesenCore.dll because it was not found`. The original 14-job file carries both steps; `verify_ci_platform_matrix.sh` §8 asserts presence, the `UI` target and the order, because the failure needs a Windows runner and 90 seconds to surface.

## Record

- 2026-09-16 — ADR-0203 accepted: CI builds Windows and macOS Apple Silicon again on the existing `prod`-filtered triggers.
