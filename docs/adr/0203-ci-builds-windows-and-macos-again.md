# ADR-0203: CI builds Windows and macOS again, on the existing triggers

- Status: accepted (2026-09-16, at the user's direction: "quero bild de windows e mac", narrowed by two follow-up choices — macOS Apple Silicon only, and the trigger set unchanged — and extended by "os links de binarios devem apontar para PROD")
- Date: 2026-09-16
- Related: ADR-0191 (the Linux-only decision this amends), ADR-0200 (the `prod` pull-request trigger, which stays exactly as it is), `.github/workflows/build.yml`, `scripts/checks/verify_ci_platform_matrix.sh`
- Supersedes / amends: amends ADR-0191 §1 and §2 — CI is no longer Linux-only, and the macOS and Windows legs are restored. ADR-0191 §4 was already amended by ADR-0200 and is untouched here.

## Context

ADR-0191 (2026-09-14) cut `build.yml` down to Linux. Two publish jobs for
Windows and four macOS legs were deleted, not commented out, and the policy
behind it was stated as: every *binary* build is macOS Apple Silicon only, CI
compiles Linux only, and the macOS build is made locally when it is needed.

That left the README shipping a Windows link it labels **frozen at the
2026-09-14 build** — the URL still resolves, but only through nightly.link's
fallback to an older run, and it can never refresh. And it left macOS with no
CI leg at all, so the only way to learn that a change broke the macOS build is
to run `make release-macos` by hand.

The user reversed the policy on 2026-09-16 ("quero bild de windows e mac"),
narrowed by two choices made in the same conversation: **macOS Apple Silicon
only**, and **the trigger set unchanged**.

A second decision followed in the same conversation: the README's on-demand CI
links named `main` in their nightly.link URL, and the user asked that they name
`prod` instead. That is more than a text edit. nightly.link resolves a branch
segment against the `head_branch` GitHub Actions recorded for a run of that
workflow — for a `pull_request` run that is the PR's **source** branch, not its
base, and for a `workflow_dispatch` run it is whatever `ref` the dispatch named.
No run of `build.yml` has ever had `head_branch: prod`, because nothing has
ever dispatched it with `--ref prod` — every `prod` pull request so far had
`main` as its source, which is the only reason the existing `main` links have
ever resolved to anything.

Non-goals. This does not restore `push`. It does not add an Intel macOS leg. It
does not make CI produce the published macOS release — that is still
`make release-macos`, locally, and this ADR says why below. It does not make
`prod` builds automatic — there is still no `push` trigger on `prod`; the freshness
of the linked binaries depends on someone dispatching after a promotion, exactly
as it already depended on someone dispatching after a `main` change.

## Decision

1. **A `windows` job returns, with the two publish profiles it had**: net10.0
   single-file and net10.0 AoT, on `windows-2025-vs2026`. The README's Windows
   link names the AoT artifact, so that profile is the one users download.

2. **A `macos` job returns for Apple Silicon only**: `clang` and `clang_aot` on
   `macos-15`. The `macos-15-intel` legs are **not** restored — the published
   release is arm64 and the README says Apple Silicon, so an Intel leg would
   build an artifact nothing links to. The old job was four legs (2 compilers ×
   2 architectures); this is two.

3. **The triggers do not change.** `pull_request` filtered to the `prod` base
   branch, plus `workflow_dispatch` (ADR-0200). Windows and macOS legs therefore
   run when a `prod` pull request is opened or when a maintainer dispatches the
   workflow — not on every push. Restoring `push` would reintroduce exactly what
   #230 and ADR-0191 removed: a full LTO/AOT matrix per push, and a push during
   a dispatch cancelling the run the download links resolve against.

4. **The `if: github.event_name != 'pull_request'` guard does not come back.**
   Every upload in the deleted jobs carried it, and ADR-0200 removed it from the
   Linux jobs for a measured reason: a `prod` pull request runs the whole matrix
   and publishes nothing, which is the expensive half of a build with none of
   the result. The restored jobs publish on every trigger they have.

5. **The CI macOS artifact is not code-signed.** The deleted job signed the
   bundle with a Developer ID certificate from repository secrets, which is
   distribution signing and belongs to the release path. The published macOS
   release is still built by `scripts/release_macos.sh`, which ad-hoc signs and
   verifies the bundle itself; the CI leg exists to prove the build compiles and
   to produce a runnable artifact, not to replace that.

6. **The README's on-demand links move from the `main` channel to the `prod`
   channel.** Every `nightly.link/.../workflows/build/main/...` URL becomes
   `.../workflows/build/prod/...`, and the refresh command becomes
   `gh workflow run build.yml --repo sbihaiko/MesenAI --ref prod`. `prod` is
   the branch that is meant to be releasable, so what a downloader gets should
   track it, not the branch still taking commits. This makes the maintainer's
   step after promoting `main` into `prod` two commands, not one: merge the
   promotion PR, then dispatch `build.yml --ref prod` so a run with
   `head_branch: prod` exists for the links to resolve against. Nothing
   automates the second command — there is still no `push` trigger on `prod`
   (§3) — so the links are exactly as fresh as the last time someone
   remembered to run it, same as they always were for `main`.

## Consequences

- **The README's Windows row stops being a lie.** The link points at a run of
  this workflow, and runs now exist again. It refreshes on the next `prod`
  pull request or dispatch, not on a schedule.
- **A macOS compile break is now caught by CI**, which is what the local-only
  policy could not do.
- **The matrix is 10 jobs again, not 14**: 6 Linux legs, 2 AppImages, 2 Windows
  profiles, 2 macOS legs. The four-leg macOS job is the part that shrank.
- **Two thirds of the matrix never runs on an ordinary pull request**, because
  the `prod` filter from ADR-0200 is unchanged. A change that breaks Windows or
  macOS compilation can therefore reach `main` and only be caught when someone
  opens a `prod` pull request. That is the cost of keeping the triggers narrow,
  and it is the user's choice, not an oversight — `gh workflow run build.yml
  --ref <branch>` is the hand-run path for catching it earlier.
- **The verifier is rewritten, not deleted.** `verify_ci_linux_only.sh` asserted
  the absence of exactly these runners, so it fails the moment this lands. It
  becomes `verify_ci_platform_matrix.sh`, which asserts the new contract: the
  platform set, the trigger set, the absence of the `event_name` guard, the
  absence of the `macos-15-intel` legs, and — section 8 — the Windows job's
  two-step shape below.
- **The Windows job is two steps, and that is load-bearing.** The native
  `MesenCore.dll` (the C++ interop project) is built by the MSBuild `-t:...UI`
  target, not by `dotnet publish`, which only copies it into the publish
  output. Transcribing the job as a single publish step therefore does not
  produce a native compile error: it fails publish with
  `MSB3030: Could not copy the file ...MesenCore.dll because it was not found`,
  which is what the first run of this ADR did. The original 14-job file in
  history carries both steps for that reason, and the transcription lost the
  distinction. `verify_ci_platform_matrix.sh` §8 now asserts presence, the `UI`
  target and the order, because the failure is invisible to every other check —
  it needs a Windows runner and 90 seconds to surface.
