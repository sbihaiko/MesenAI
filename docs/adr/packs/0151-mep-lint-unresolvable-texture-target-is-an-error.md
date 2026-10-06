# ADR-0151: An unresolvable `<background>` target is a `mep_lint` error, not a warning — the CI gate matches the runtime gate

- Status: accepted (reflected in `scripts/mep_lint.py`; no PRD slice pending)
- Date: 2026-09-04
- Related: ADR-0148, ADR-0138 §4/§37, MEP-v1 §2.1/§5, `scripts/mep_lint.py`, `scripts/smoke_pack_headless.sh`, `.github/ai/validate-classify.md`, issue #155
- Supersedes / amends: extends ADR-0148 ("a listed row must be a self-contained, verifiable artifact") from *artifact availability* to *intra-artifact reference resolution*, and tightens the classify contract stated in CLAUDE.md, "Community HD/MEP Pack triage"
- Amended by: ADR-0152 (narrows this rule — a reviewed `miss` errata may declare exact targets known-missing)

## Context

Two gates decide a pack's usability, and they disagreed. `scripts/mep_lint.py`
is the submission gate — `community-pack-validate.yml` runs it unmodified, and a
passing lint lets the classify step mint an `accepted` verdict and a live row in
`docs/community-packs.json`. `scripts/smoke_pack_headless.sh` is the runtime
gate — it loads the installed pack headless and fails on `HdPackLoader`'s
`Error while loading background: <name>`.

For a missing `<img>` target both rejected it; for a missing `<background>`
target they did not — `mep_lint` warned and passed while the smoke gate failed
the same pack. Manual run F6.5/F6.6 hit this on the live row `issue-139`, whose
`textures/hires.txt:6124` declares `selectscreen.png` while the artifact ships
only `selectscreen1..6.png` and `selectscreentop.png`; the row is accepted,
auto-installed on every matching ROM (ADR-0146), and drops the entry at load
with an error in the user's log.

An absent `<background>` PNG is not cosmetic:
`HdPackLoader::ProcessBackgroundTag` drops the entry, so the manifest describes
content the artifact cannot deliver — the "not a self-contained, verifiable
artifact" condition ADR-0148 de-lists rows for. The lint is the only gate before
a row goes live, so it is the wrong place to be lenient.

Non-goals: no change to `HdPackLoader` (dropping and logging is correct); no
change to the MEP-v1 spec (never assigned lint severities per tag); and it does
not make every `<background>` diagnostic fatal — condition-type, priority-range,
blend-mode and case-mismatch checks keep their severities.

## Decision

In `scripts/mep_lint.py`, a `<background>` whose referenced file does not exist
anywhere in the pack is reported with `rep.error`, same message shape as `<img>`:

```
error   textures/hires.txt:6124  <background> selectscreen.png does not exist —
        1 entry/entries would be dropped at load (HdPackLoader::ProcessBackgroundTag)
```

Consequences of the severity, in order:

1. `mep_lint` exits non-zero, so `community-pack-validate.yml` stops before the
   classify step and the pack never reaches an `accepted` verdict.
2. The pack-triage rule becomes: **a texture section counts as present only when
   every `<img>` and `<background>` target it declares resolves inside the
   archive.** CLAUDE.md's classify-contract sentence is updated to say so; the
   pre-existing `<bgm>`/`<sfx>` rule ("all targets missing ⇒ `invalid`",
   MEP-v1 §5) is unchanged.
3. A target existing under a different case stays a **warning** — it loads on
   macOS and Windows and fails only on Linux; a portability defect, not an
   unverifiable artifact.

This deliberately applies to already-accepted rows: `/revalidate` (manual or via
`community-pack-drift-check.yml`) now fails a row the old lint let through,
which is the intended way to surface them.

## Consequences

- **Row `issue-139` is now failing by design.** It is live, accepted and
  auto-installed; the next `/revalidate` will report the error. Fixing it means
  the author correcting `hires.txt:6124` (or shipping `selectscreen.png`) and
  re-submitting; until then ADR-0148's de-listing path applies. Other live rows
  with the same defect surface the same way — a one-time backlog, not a
  recurring cost.
- **A stricter gate rejects packs that "mostly work."** A 400-entry manifest
  with one dead `<background>` is rejected outright — the trade ADR-0148 already
  made for artifact availability, extended inward; loosening the smoke gate
  instead would let a row go live that logs an error on every user's machine.
- **The two gates must stay aligned.** `smoke_pack_headless.sh` matches on the
  literal strings `could not be read` and `Error while loading background`; a
  new fatal `HdPackLoader` diagnostic added to one gate and not the other
  re-opens exactly this divergence.
