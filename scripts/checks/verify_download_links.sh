#!/usr/bin/env bash
# ADR-0204: fetch every download link the README publishes and report the HTTP
# status of each. This is the check that would have caught the rot the ADR
# exists because of - the README's links served a 2026-09-14 build in silence
# from the day the push trigger was removed until someone happened to click one.
#
# It is NOT part of `make doc-checks`: doc-checks is hermetic and runs on every
# pull request, and this needs the network plus a published release, so a
# GitHub outage would turn the gate red for reasons the change did not cause.
# Run it by hand after publishing the rolling pre-release, or from a workflow
# that is allowed to fail for network reasons:
#
#   make check-download-links
#
# Exit status is 0 only when every link answered 200.
set -euo pipefail

README="${1:-README.md}"
FAIL=0
CHECKED=0

# The same URL shape verify_download_channel.sh binds to the workflow's staged
# asset names. Kept in sync by that script, not by this one.
urls="$(grep -oE "https://github\.com/sbihaiko/MesenAI/releases/download/[A-Za-z0-9._/-]+" "$README" | sort -u)"

if [ -z "$urls" ]; then
  echo "FAIL: $README publishes no release download links" >&2
  exit 1
fi

while IFS= read -r url; do
  [ -n "$url" ] || continue
  CHECKED=$((CHECKED + 1))
  # -L: the asset URL redirects to a signed objects.githubusercontent.com blob,
  # so the status that matters is the one at the end of the chain.
  code="$(curl -s -o /dev/null -w '%{http_code}' -L --max-time 60 "$url" || echo 000)"
  if [ "$code" = "200" ]; then
    echo "  OK   $code  ${url##*/}"
  else
    echo "  BAD  $code  ${url##*/}" >&2
    echo "       $url" >&2
    FAIL=1
  fi
done <<< "$urls"

if [ "$FAIL" -ne 0 ]; then
  echo "FAIL: at least one download link the README publishes does not resolve." >&2
  echo "      The assets come from build.yml's publish job (ADR-0204); after a" >&2
  echo "      promotion, refresh them with:" >&2
  echo "        gh workflow run build.yml --repo sbihaiko/MesenAI --ref prod" >&2
  exit 1
fi

echo "PASS: ADR-0204 (all $CHECKED download links the README publishes resolve)"
