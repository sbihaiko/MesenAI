# ADR-0200: A pull request against `prod` builds the binaries

- Status: accepted (2026-09-16, at the user's direction: "vamos criar um brantch de PROD, para disparar os builds de binários quando um PR for feito", answered "PR contra `prod` dispara build" for the trigger and "todos os suportados" for the platforms; implemented in the same change, whose test is `scripts/checks/verify_ci_linux_only.sh`)
- Date: 2026-09-16
- Related: ADR-0191 (amended: §4), ADR-0193 (the gate's triggers, untouched), ADR-0131, PRD Part A §4 Phase 11 C.4 (`make release-macos`)
- Supersedes / amends: ADR-0191 §4's second clause — "`build.yml` is Linux only and **stays `workflow_dispatch`-only**". The Linux-only half of §4, and §1's ban on `macos-*`/`windows-*` runners, are unchanged and still enforced.

## Context

`build.yml` lost its `push` and `pull_request` triggers on 2026-09-13/14 (#230)
because its fourteen jobs — two Windows publish profiles, six Linux and
AppImage compiler×arch legs, four macOS legs with a codesign step — cost more
runner time than the commits they built. ADR-0191 then cut the file to the
eight Linux legs. Since then the only way to build a binary is
`gh workflow run build.yml --ref <branch>`.

The user asked for a `prod` branch such that opening a pull request against it
fires the binary builds, with **every supported platform**. What "every
supported platform" resolves to today is the eight legs already in the file —
`gcc`/`clang`/`clang_aot` × `ubuntu-22.04`(x64)/`ubuntu-22.04-arm`(arm64), plus
the two AppImage legs. It is *not* a request to bring back macOS or Windows:
ADR-0191 §1 deleted those legs and still forbids a `macos-*`/`windows-*`
runner, and restoring a platform is a revert of that ADR's commit, not a
side effect of this one. This ADR therefore amends §4's trigger clause only.

Two facts about the file's existing state decide the shape of the change.

1. **The uploads are gated on the event.** Both `Upload Mesen` steps carry
   `if: github.event_name != 'pull_request'`. Adding the trigger without
   touching them would run all eight legs on every pull request and publish
   **no artifact at all** — the expensive half of a build with none of the
   result. That `if:` is a leftover from a guard that only made sense when
   `push` and `pull_request` could fire for the same commit; with the trigger
   set reduced to a `prod`-filtered `pull_request` plus `workflow_dispatch`,
   the two can no longer overlap and the guard is dead weight. The file's own
   header already claims "No job below is aware of the gate — there is no
   `if:` to remove", which is simply wrong today; this change makes the
   comment true instead of leaving it to mislead the next reader.

2. **The reason the trigger was removed does not apply to a PR run.** The
   split of the gate into `checks.yml` exists because `build.yml`'s identity
   is "the workflow whose newest run on `main` carries the downloadable
   binaries" — the README's nightly.link URLs resolve against exactly that
   run, and `cancel-in-progress` groups on workflow+ref. A run triggered by a
   pull request executes at `refs/pull/N/merge`, which is neither `main` nor
   a ref a dispatched build uses, so it cannot supersede the downloadable run
   and cannot cancel one.

Non-goals. This does not restore `push` — a `push` trigger is what breaks the
nightly.link contract, which is why the verifier keeps forbidding it. It does
not protect the `prod` branch, does not change what a release is
(`make release-macos` is still the macOS path), does not touch `checks.yml` or
any other workflow, and does not make a `prod` PR a required check.

## Decision

1. **The branch is `prod`, and it is cut from `main`.** `build.yml`'s trigger
   names it explicitly, so the branch is part of the contract rather than a
   convention: renaming it is an edit to the workflow, the verifier and this
   ADR.

2. **`build.yml` gains `pull_request` filtered to `prod`, and keeps
   `workflow_dispatch`.**

   ```yaml
   on:
     pull_request:
       branches:
         - 'prod'
     workflow_dispatch:
   ```

   `pull_request.branches` filters on the **base** branch, which is exactly
   the requested semantic: a pull request opened *against* `prod` builds,
   an ordinary pull request against `main` does not. The default activity
   types (`opened`, `synchronize`, `reopened`) are kept — a push to the head
   branch of an open `prod` pull request must rebuild it.

3. **No `push` trigger.** Restated because the temptation is real and the
   cost is not obvious: a `push` run on `main` would become the newest run of
   this workflow there, and the README's download links would stop serving
   binaries until the next dispatch.

4. **The two `if: github.event_name != 'pull_request'` guards come off the
   upload steps**, so a pull request's build publishes the same eight
   artifacts a dispatch does. A build that produces nothing is not a build.

5. **The verifier changes with the trigger.** `scripts/checks/verify_ci_linux_only.sh`
   §5 currently fails on any `push`/`pull_request` key in `build.yml`. It
   becomes: `push` still fails outright; `pull_request` passes **only** with a
   `branches` list naming `prod`; a filter-less `pull_request` — which would
   fire eight LTO builds on every pull request in the repository, the exact
   cost #230 and ADR-0191 existed to stop — fails; `workflow_dispatch` must
   still be present.

6. **`prod` carries no extra CI of its own.** The always-on gate keeps
   running on pull requests into `main`; a pull request into `prod` gets the
   binary build and nothing else. Adding the gate there would compile the same
   tree twice for one merge.

## Consequences

- **A pull request against `prod` costs eight runner jobs**, each a full
  LTO/AOT build, against one job for an ordinary pull request. That is the
  price of the requested trigger and it is bounded by the branch filter: the
  cost lands on release pull requests, not on development ones. If it proves
  too expensive, the lever is fewer legs in the matrix (drop `clang_aot`, or
  the AppImage pair), not the trigger — say so and amend this ADR.
- **Binaries are now attached to a pull request run**, so a reviewer can
  download what the pull request would ship, before it merges. That is the
  point of the ADR and it is a capability the repository did not have.
- **A `prod` pull request is not a gate.** It runs no test and block nothing:
  `checks.yml` still reports the five required checks for `main`, and nothing
  requires a successful `build.yml` run before merging into `prod`. Merging a
  red build into `prod` remains possible by human choice.
- **Two branch names now matter and only one is protected.** `main` has the
  ruleset; `prod` has whatever protection a human configures. An unprotected
  `prod` means a direct push to it builds nothing at all under this trigger —
  silently, because no workflow watches that event.
- **`build.yml`'s header comment is corrected in the same change.** It
  asserted there was no `if:` to remove; the file's next reader would have
  believed it.
- **The macOS release is still local.** `make release-macos` remains the only
  way the macOS artifact is produced, for the reason ADR-0191 §5 gives, and a
  `prod` pull request does not change that.
