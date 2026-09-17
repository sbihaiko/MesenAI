# ADR-0204: The CI download channel is a rolling pre-release, not nightly.link

- Status: accepted (2026-09-17, at the user's direction: "b entao" — option B of the three laid out, a rolling pre-release as the download channel plus a check that the links resolve)
- Date: 2026-09-17
- Related: ADR-0203 (§6, amended here), ADR-0200 (the `prod` pull-request trigger), ADR-0191 (the trigger set this leaves alone), `.github/workflows/build.yml`, `README.md`, `scripts/checks/verify_download_channel.sh`
- Supersedes / amends: amends ADR-0203 §6 — the README's on-demand download links stop being nightly.link URLs on the `prod` channel and become fixed-name assets of a rolling pre-release.

## Context

ADR-0203 §6 moved the README's download links from nightly.link's `main`
channel to its `prod` channel, and recorded the refresh step as
`gh workflow run build.yml --ref prod`. Both halves were wrong, and the reason
is invisible from the outside: **nightly.link resolves a branch against a run
whose event is `push`.** Its own 404 body states the query it runs —
`.../actions?query=event%3Apush+is%3Asuccess+branch%3Aprod`.

`build.yml` has had no `push` trigger since ADR-0191 (2026-09-14). All 47 of its
push runs predate that; the last green one is `2026-09-14T15:09:54Z`. So:

- **Every `prod` link is a guaranteed 404**, however many times the workflow is
  dispatched. A dispatch produces `head_branch: prod`, which is what ADR-0203 §6
  reasoned about — but the event is `workflow_dispatch`, and that is the half
  nightly.link filters on. Measured, not inferred: the dispatched run went
  12/12 green and all six links still returned 404.
- **The `main` links resolve only by accident**, serving that 2026-09-14 build.
  That is precisely what the README called "frozen at the 2026-09-14 build"
  before ADR-0203 removed the note — the note was right and the fix was wrong.
- **And they are a time bomb regardless.** Actions artifacts expire, and this
  repository's retention is 90 days (`{"days":90,"maximum_allowed_days":90}`),
  so the `main` channel goes dark on 2026-12-13 and the `prod` channel would too
  if a push ever fed it.

A GitHub Actions artifact has no stable public URL to fall back on: the blob URL
nightly.link redirects to is signed and expires within hours, and the API URL
requires an authenticated session. Anything that rewrites the README per build
would therefore write links that die the same day.

Non-goals. This does not restore `push`, and the trigger set is untouched
(ADR-0203 §3 stands). It does not publish anything from a pull request. It does
not change the tagged release: `v0.1.0` stays macOS Apple Silicon only, built
locally by `scripts/release_macos.sh`.

## Decision

1. **A `publish` job in `build.yml`**, `needs: [windows, macos, linux, appimage]`
   and gated `if: github.event_name != 'pull_request'`. A pull request run
   executes at `refs/pull/N/merge` — a merge preview of code that has not landed
   — so it publishes nothing. A dispatch publishes, and so would a push if one
   is ever restored.

2. **It uploads to one rolling pre-release tagged `ci-latest`**, created on the
   first publish and replaced on every later one with `gh release upload
   --clobber`. Marking it a pre-release is load-bearing: that is what keeps
   `releases/latest` resolving to the real release, `mesence-v0.1.0`.

3. **The asset names are fixed**, so the README's URLs never change:
   `MesenAI-ci-linux-x64.zip`, `MesenAI-ci-linux-x64.AppImage`,
   `MesenAI-ci-linux-arm64.zip`, `MesenAI-ci-linux-arm64.AppImage`,
   `MesenAI-ci-macos-arm64.zip`, `MesenAI-ci-windows-x64-aot.zip`.

4. **The README's download links become**
   `https://github.com/sbihaiko/MesenAI/releases/download/ci-latest/<asset>`,
   and nightly.link disappears from the README entirely. The `-ci-` infix keeps
   them visibly distinct from the release's own
   `MesenAI-<version>-macos-arm64.zip` and `mesenai-tools-<version>.zip`
   (ADR-0202).

5. **The triggers do not change**, and neither does the refresh command:
   `gh workflow run build.yml --repo sbihaiko/MesenAI --ref prod`, run after a
   promotion. What changes is that the command now produces downloadable files
   instead of a run nobody can link to.

## Consequences

- **The links stop expiring.** A release asset persists; an Actions artifact is
  deleted after 90 days. That is the property nightly.link could never provide,
  because it can only ever point at an artifact.
- **The download needs no GitHub session.** Both nightly.link and release assets
  satisfy this; the workflow-runs page would not have, which is what ruled out
  pointing the README at Actions directly.
- **The channel is still manual.** Nothing publishes on a promotion merge; a
  maintainer has to dispatch. That was already true under ADR-0203 §6 and is the
  price of leaving the trigger set alone. The fix, if it is ever wanted, is
  `push: branches: [prod]` — one more full matrix per promotion, in exchange for
  a channel that refreshes itself.
- **`ci-latest` is visible on the releases page.** A pre-release titled "CI
  builds (not a release)" now sits beside the real ones. Marking it a
  pre-release keeps it out of `releases/latest` (badge, README links, and
  `gh release view` without a tag all still resolve to `mesence-v0.1.0`), but it
  is a new permanent object in the repository and it is not free.
- **A red publish leaves the last good assets in place.** `--clobber` only
  replaces what a successful run stages, and the publish job is `needs:`-gated
  on all four build jobs, so a failed matrix publishes nothing at all.
- **The verifier splits in two.** `verify_ci_platform_matrix.sh` §5 used to fail
  on any occurrence of the `event_name` guard, which was the right contract
  while no job had one; §5 now asserts instead that the guard appears **exactly
  once and only inside `publish`**, so a build job cannot quietly regain it and
  the publish job cannot quietly lose it. The new
  `verify_download_channel.sh` holds the rest: the publish job's shape, the six
  asset names, the README's six URLs, and the absence of nightly.link.
