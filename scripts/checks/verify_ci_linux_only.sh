#!/usr/bin/env bash
# ADR-0191: CI compiles Linux only, the macOS release is built locally by
# `make release-macos`, and Windows is retired from CI until the user lifts
# the rule.
#
# The ADR was accepted and implemented in the same change, so per CLAUDE.md
# these greps ARE its unit tests: they fail the moment a workflow reintroduces
# a macOS or Windows runner, or the moment one of the two deleted workflows
# comes back without its contract being restated.
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

# 2. No workflow runs a job on a macOS or Windows runner.
while IFS= read -r line; do
  file="${line%%:*}"
  rest="${line#*:}"
  runner="$(printf '%s' "$rest" | sed -e 's/.*runs-on:[[:space:]]*//' -e 's/[[:space:]]*$//' -e "s/^['\"]//" -e "s/['\"]$//")"
  case "$runner" in
    macos-*|macOS-*)
      fail "$file runs a job on '$runner'; ADR-0191 allows no macOS runner (the macOS release is built locally with 'make release-macos')"
      ;;
    windows-*|windows|windows-latest)
      fail "$file runs a job on '$runner'; ADR-0191 retired Windows from CI"
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

# 5. build.yml stays dispatch-only and Linux-only.
if grep -qE "^  (push|pull_request):" "$WORKFLOWS/build.yml"; then
  fail "$WORKFLOWS/build.yml regained a push/pull_request trigger; binaries stay on workflow_dispatch (#230, ADR-0191)"
fi
if ! grep -q "ADR-0191" "$WORKFLOWS/build.yml"; then
  fail "$WORKFLOWS/build.yml's header no longer states the ADR-0191 policy"
fi

if [ "$FAIL" -ne 0 ]; then
  exit 1
fi

echo "PASS: ADR-0191 (CI compiles Linux only; no macOS/Windows compilation job; checks.yml holds the five gate jobs)"
