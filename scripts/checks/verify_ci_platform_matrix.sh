#!/usr/bin/env bash
# ADR-0191: CI's always-on gate (`checks.yml`) compiles Linux only, and
# Windows/macOS are otherwise banned from every workflow's `runs-on:` - the
# single documented exception is `dotnet-format-check.yml`, disabled and
# compiling nothing.
#
# ADR-0193 (2026-09-15) extends it with the gate's trigger contract:
# `checks.yml` keeps `pull_request` on `main` and `workflow_dispatch`. The
# `push` trigger is not asserted on purpose - see section 6.
#
# ADR-0200 (2026-09-16) amends ADR-0191 §4's "stays workflow_dispatch-only"
# clause: `build.yml` builds on a pull request opened against `prod`. Section 5
# is where that is asserted - the branch filter that keeps the matrix off
# every ordinary pull request, and the absence of the `event_name` guard that
# would otherwise publish nothing.
#
# ADR-0203 (2026-09-16) amends ADR-0191 §1/§2: `build.yml` (and only
# `build.yml`) may run a job on `windows-*`/`macos-*` again, restoring the
# Windows and macOS-Apple-Silicon-only legs. `checks.yml` and every other
# workflow stay Linux-only - section 2 below is scoped to exclude `build.yml`
# for exactly that reason, and section 7 asserts what `build.yml` is allowed
# to have instead. Section 8 asserts the one thing the restored Windows job
# cannot be transcribed without: the MSBuild native-compile step that has to
# run before `dotnet publish`.
#
# The ADR was accepted and implemented in the same change, so per CLAUDE.md
# these greps ARE its unit tests: they fail the moment a workflow other than
# `build.yml` reintroduces a macOS or Windows runner, the moment `build.yml`
# drops the Windows/macOS jobs ADR-0203 restored, or the moment one of the two
# deleted workflows comes back without its contract being restated.
#
# `dotnet-format-check.yml` was the one documented exception (a Windows
# runner that compiled nothing) and was deleted on 2026-09-14 by the user's
# decision, since ADR-0191 already forbids CI from touching a Windows
# runner for anything. A changed-files-only format check may return as a
# separate PR; if it does, it belongs on Linux.
set -euo pipefail

WORKFLOWS=".github/workflows"
FAIL=0

fail() {
  echo "FAIL: $1" >&2
  FAIL=1
}

# 1. The deleted workflows stay deleted.
for gone in tests.yml unit-tests.yml; do
  if [ -e "$WORKFLOWS/$gone" ]; then
    fail "$WORKFLOWS/$gone is back; ADR-0191 deleted it (tests.yml was the Windows MSBuild + PGOHelper citests suite, unit-tests.yml folded into checks.yml)"
  fi
done

# 2. No workflow OTHER THAN build.yml runs a job on a macOS or Windows
#    runner (ADR-0203 scopes the restored platforms to build.yml alone).
while IFS= read -r line; do
  file="${line%%:*}"
  rest="${line#*:}"
  case "$file" in
    "$WORKFLOWS/build.yml") continue ;;
  esac
  runner="$(printf '%s' "$rest" | sed -e 's/.*runs-on:[[:space:]]*//' -e 's/[[:space:]]*$//' -e "s/^['\"]//" -e "s/['\"]$//")"
  case "$runner" in
    macos-*|macOS-*)
      fail "$file runs a job on '$runner'; only build.yml may (ADR-0203) - the macOS release is otherwise built locally with 'make release-macos'"
      ;;
    windows-*|windows|windows-latest)
      fail "$file runs a job on '$runner'; only build.yml may (ADR-0203)"
      ;;
  esac
done < <(grep -rn "runs-on:" "$WORKFLOWS" --include='*.yml' | grep -v '\${{')

# 3. The gate's five jobs are the ones ADR-0191 leaves in checks.yml.
for job in checks python-tests core-unit-tests ui-tests headless-ui-tests; do
  if ! grep -qE "^  $job:" "$WORKFLOWS/checks.yml"; then
    fail "$WORKFLOWS/checks.yml has no job '$job' (ADR-0191 folded ui-tests/headless-ui-tests in here; the three older jobs are Phase 11 C.1's)"
  fi
done

# 4. ADR-0131's invariants travelled with the folded jobs: no native link, no
#    SDL2 in the two dotnet jobs, and the 10.x pin.
if grep -qE "^  ui-tests:" "$WORKFLOWS/checks.yml"; then
  if ! grep -q "verify-ui-logic-firewall" "$WORKFLOWS/checks.yml"; then
    fail "the ui-tests job lost its UI/Logic firewall pre-check (ADR-0123 H5)"
  fi
  if ! grep -q "dotnet test UI.Tests/UI.Tests.csproj" "$WORKFLOWS/checks.yml"; then
    fail "the ui-tests job no longer runs dotnet test UI.Tests/UI.Tests.csproj"
  fi
fi
if ! grep -q "dotnet test UI.HeadlessTests/UI.HeadlessTests.csproj" "$WORKFLOWS/checks.yml"; then
  fail "the headless-ui-tests job no longer runs dotnet test UI.HeadlessTests (ADR-0150)"
fi
# InteropDLL/MesenCore may appear in a comment explaining the invariant, but
# never in a step that runs. Only non-comment lines are inspected.
if grep -vE "^\\s*#" "$WORKFLOWS/checks.yml" | grep -qE "InteropDLL|MesenCore"; then
  fail "$WORKFLOWS/checks.yml builds or links InteropDLL/MesenCore outside a comment; the host-free jobs must never do that (ADR-0131)"
fi
if [ "$(grep -c "dotnet-version: 10.x" "$WORKFLOWS/checks.yml")" -lt 2 ]; then
  fail "the two dotnet jobs in checks.yml must both pin 'dotnet-version: 10.x' (ADR-0131 item 4)"
fi

# 5. build.yml keeps its dispatch escape, and takes exactly one trigger
#    besides it: a pull_request filtered to the `prod` base branch
#    (ADR-0200, 2026-09-16).
#
#    `push` still fails outright. A push run on `main` would become the newest
#    run of this workflow there, and the README's nightly.link URLs resolve
#    against exactly that run; its cancel-in-progress group (workflow+ref)
#    would also kill a dispatched build. A pull_request run executes at
#    `refs/pull/N/merge`, so it can do neither - which is what makes it safe
#    where `push` is not.
#
#    The branch filter is the entire cost guard. Without it, every pull request
#    in the repository would fire the full matrix - the cost #230 and
#    ADR-0191 existed to stop.
if grep -qE "^  push:" "$WORKFLOWS/build.yml"; then
  fail "$WORKFLOWS/build.yml regained a push trigger; a push run on main supersedes the binary run the README's links resolve against (#230, ADR-0191)"
fi
if grep -qE "^  pull_request:$" "$WORKFLOWS/build.yml"; then
  if [ "$(grep -A3 '^  pull_request:$' "$WORKFLOWS/build.yml" | grep -c -- "- 'prod'")" -eq 0 ]; then
    fail "$WORKFLOWS/build.yml's pull_request trigger is not filtered to 'prod'; an unfiltered one fires the full binary matrix on every pull request (ADR-0200)"
  fi
else
  fail "$WORKFLOWS/build.yml lost its pull_request trigger; a PR opened against 'prod' no longer builds (ADR-0200)"
fi
if ! grep -qE "^  workflow_dispatch:$" "$WORKFLOWS/build.yml"; then
  fail "$WORKFLOWS/build.yml lost its workflow_dispatch trigger; that is still the hand-run path for a release build (ADR-0191 §4, ADR-0200)"
fi
if ! grep -q "ADR-0191" "$WORKFLOWS/build.yml"; then
  fail "$WORKFLOWS/build.yml's header no longer states the ADR-0191 policy"
fi
if ! grep -q "ADR-0203" "$WORKFLOWS/build.yml"; then
  fail "$WORKFLOWS/build.yml's header no longer states the ADR-0203 policy (Windows/macOS restored)"
fi
# The uploads must not be gated on the event: an `if:` there would run every
# leg of a `prod` pull request and publish no artifact at all - the expensive
# half of a build with none of the result (ADR-0200 §4, ADR-0203 §4).
if grep -q "github.event_name != 'pull_request'" "$WORKFLOWS/build.yml"; then
  fail "$WORKFLOWS/build.yml gates an upload on github.event_name; a 'prod' pull request would run every leg and publish no artifact (ADR-0200, ADR-0203)"
fi

# 6. ADR-0193: the gate keeps its pre-merge trigger and its dispatch escape.
#    The trigger deleted in a future "cut the redundant run" edit is the push
#    one, so it is deliberately NOT asserted here - ADR-0193 §5 lists the
#    conditions that would let it go, and a guard against its removal would
#    freeze a decision that is meant to be revisitable. What is frozen is the
#    half the ruleset depends on: without `pull_request` on `main`, the five
#    required checks never report and every merge blocks on a name that never
#    arrives.
if ! grep -qE "^  pull_request:$" "$WORKFLOWS/checks.yml"; then
  fail "$WORKFLOWS/checks.yml has no pull_request trigger; the five required checks would never report on a PR and the main ruleset would block every merge (ADR-0193)"
fi
if ! grep -qE "^  workflow_dispatch:$" "$WORKFLOWS/checks.yml"; then
  fail "$WORKFLOWS/checks.yml lost its workflow_dispatch trigger; per ADR-0193 §3 that is the hand-run path for a red main"
fi
# The branch filter is part of the contract: a filter-less pull_request widens
# the gate to every branch of every fork.
if [ "$(grep -A2 '^  pull_request:$' "$WORKFLOWS/checks.yml" | grep -c -- "- 'main'")" -eq 0 ]; then
  fail "$WORKFLOWS/checks.yml's pull_request trigger no longer names 'main' in its branches list (ADR-0193)"
fi

# 7. ADR-0203: build.yml restores exactly one Windows job and one macOS job,
#    the macOS one scoped to Apple Silicon only (no macos-15-intel leg), and
#    neither carries the deleted event_name guard on its own upload (checked
#    generically for the whole file in section 5, asserted again here so a
#    mutation local to just these two jobs is still caught).
if ! grep -qE "^  windows:$" "$WORKFLOWS/build.yml"; then
  fail "$WORKFLOWS/build.yml has no 'windows' job; ADR-0203 restores it"
fi
if ! grep -q "runs-on: windows-2025-vs2026" "$WORKFLOWS/build.yml"; then
  fail "$WORKFLOWS/build.yml's windows job does not run on windows-2025-vs2026 (ADR-0203)"
fi
if ! grep -qE "^  macos:$" "$WORKFLOWS/build.yml"; then
  fail "$WORKFLOWS/build.yml has no 'macos' job; ADR-0203 restores it, Apple Silicon only"
fi
if ! grep -q "os: macos-15}" "$WORKFLOWS/build.yml" && ! grep -qE 'os: macos-15\s*[,}]' "$WORKFLOWS/build.yml"; then
  fail "$WORKFLOWS/build.yml's macos job does not declare os: macos-15 (ADR-0203)"
fi
# The old job also had a comment recalling the deleted macos-15-intel leg;
# only a non-comment line naming it as an actual matrix entry should fail.
if grep -vE "^\\s*#" "$WORKFLOWS/build.yml" | grep -q "macos-15-intel"; then
  fail "$WORKFLOWS/build.yml's macos job carries a macos-15-intel leg; ADR-0203 keeps this Apple-Silicon-only, the published release is arm64"
fi
# The CI macOS leg must not carry distribution signing - that is
# scripts/release_macos.sh's job (ADR-0203 §5).
if grep -q "MACOS_CERTIFICATE" "$WORKFLOWS/build.yml"; then
  fail "$WORKFLOWS/build.yml's macos job signs with a Developer ID certificate; ADR-0203 §5 keeps that out of CI"
fi

# 8. ADR-0203: the Windows job must COMPILE the native interop library before
#    it publishes. `dotnet publish` does not build MesenCore.dll, it only
#    copies it into the publish output, so the MSBuild `-t:...UI` target has to
#    run first. Dropping that step does not fail at compile time - it fails
#    publish with `MSB3030: Could not copy the file ...MesenCore.dll because it
#    was not found`, which is exactly what the first ADR-0203 run did. Both the
#    presence and the ORDER are asserted: a publish that runs first would fail
#    the same way.
# Anchored on the step's own `run:` line, not on the bare command name: the
# comment above the step explains the trap by naming `dotnet publish`, and a
# looser match reads that comment as the publish step and reports the order
# backwards. (Found by mutating this section, which is why it is spelled out.)
msbuild_line="$(awk '/^[[:space:]]*run: msbuild/ {print NR; exit}' "$WORKFLOWS/build.yml")"
publish_line="$(awk '/^[[:space:]]*run: dotnet publish/ {print NR; exit}' "$WORKFLOWS/build.yml")"
if [ -z "$msbuild_line" ]; then
  fail "$WORKFLOWS/build.yml has no 'run: msbuild' step; the windows job must compile the native MesenCore.dll before publishing or publish fails with MSB3030 (ADR-0203)"
else
  msbuild_cmd="$(awk '/^[[:space:]]*run: msbuild/ {print; exit}' "$WORKFLOWS/build.yml")"
  case "$msbuild_cmd" in
    *-t:*UI*) ;;
    *) fail "$WORKFLOWS/build.yml's msbuild step does not target the UI project ('-t:...UI'); that target is what builds the native MesenCore.dll (ADR-0203)" ;;
  esac
  if [ -z "$publish_line" ]; then
    fail "$WORKFLOWS/build.yml has no 'run: dotnet publish' step; the windows job would build nothing to upload (ADR-0203)"
  elif [ "$msbuild_line" -ge "$publish_line" ]; then
    fail "$WORKFLOWS/build.yml runs 'dotnet publish' before the msbuild native-compile step; publish only copies MesenCore.dll and fails with MSB3030 when nothing built it (ADR-0203)"
  fi
fi

if [ "$FAIL" -ne 0 ]; then
  exit 1
fi

echo "PASS: ADR-0191 (checks.yml compiles Linux only; no macOS/Windows runner outside build.yml; checks.yml holds the five gate jobs) + ADR-0193 (the gate keeps pull_request on main and workflow_dispatch) + ADR-0200 (build.yml takes pull_request filtered to prod, keeps workflow_dispatch, and publishes artifacts on a PR run) + ADR-0203 (build.yml restores an unsigned Windows job and an Apple-Silicon-only macOS job, and the Windows job compiles the native interop library before it publishes)"
