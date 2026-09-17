# ADR-0207: The `core_unit_tests.cpp` line ceiling — drop it, split the file, or keep paying the amendment

- Status: proposed
- Date: 2026-09-17
- Related: ADR-0137 (the guarded-file list and its three amendments), ADR-0131 (the unit-test harness invariants), ADR-0195 (the decision whose tests first hit the ceiling)

## Context

`make doc-checks` ceilings five files through `scripts/check-file-loc.sh`.
Four are implementation (`HdPackBuilder.cpp`, `artist_chr_kit.py`,
`mep_build.py`, `sheet_repaint.py`); the fifth is
`scripts/core_unit_tests.cpp`, the framework-free harness every Core decision
is expected to bring cases to.

The ceiling is a ratchet: the count at the commit that set it, shrink allowed,
growth fails the build. On the four implementation files that is the intended
instrument — it makes a file that is already too long stop getting longer.

On the test file it has not behaved that way. Three days of history:

- **2026-09-15**, Phase 11 C.7 ratchets it at its then-current **7 342**.
- **2026-09-16**, one day later, ADR-0195 needs the unit tests its own Status
  line promises and finds the file at *exactly* 7 342 — zero headroom. ADR-0137
  is amended a second time; the ceiling rises to **7 600**. That amendment
  states the reason in its own words: *"the ratchet's purpose is to stop
  implementation creeping, and a test file grows whenever a decision does."*
- **2026-09-17**, the next day, the fix for issue #302 adds 46 lines of cases
  and lands the file at **7 565/7 600** — 35 lines of headroom, about one
  test case.

So the guardrail has been hit once and brought to within one case of being hit
again, inside 72 hours, and both times by work the project had already decided
to do. Every hit costs an amendment to **two** places — ADR-0137's Status line
and the `doc-checks` recipe — because CLAUDE.md correctly forbids raising a
ceiling silently.

The slices queued behind this make the next hit near-certain rather than
likely: F12.3 (reload), F12.6a (lint against routes) and F12.6b (recorder
retains internal RAM) are all Core-side decisions, and this repository's own
rule is that a decision ships with the tests that cover it.

**Non-goals.** This ADR does not touch the four implementation ceilings, does
not change `check-file-loc.sh`, and does not revisit whether the harness should
adopt a third-party test framework (ADR-0131's invariants stand: no
`InteropDLL`/`MesenCore` link, no SDL2, no platform SDK, no ROM corpus).

## Decision

**Open.** Three candidates; a human picks, and this ADR becomes `accepted`
carrying only the chosen one.

### Option A — drop the ceiling on the test file

Delete the `check-file-loc.sh scripts/core_unit_tests.cpp` line from
`doc-checks` and record in ADR-0137 that the guarded list is four
implementation files, as its own second amendment already argues. A test file
growing is the system working; length there buys coverage, not complexity, and
nothing about a long test file makes the next change harder in the way a long
`HdPackBuilder.cpp` does.

Cost: no gate at all against a genuinely bloated or duplicated test file. The
mitigation is review, which is what catches duplicated cases anyway — the line
count never did.

### Option B — split the harness into per-block translation units

The file is already organised in named blocks (`BlocoP`, `BlocoT`, …). Split
it into `scripts/core_unit_tests/<block>.cpp` with a thin runner, add each to
`CUTSRC` in the makefile, and give each part its own ratchet at a size where
the ceiling means something again.

Cost: a real refactor — makefile `CUTSRC`/`CUTOBJ` wiring, the shared helpers
must move to a header, and every future block is a new file plus a new
makefile line plus a new ceiling. It also multiplies the amendment problem by
the number of parts rather than removing it, unless the per-part ceilings are
set with deliberate headroom.

### Option C — raise it again, with headroom sized to the queue

Keep the ratchet and move it to a number chosen from the work in flight rather
than from the current count — the amendment pattern's real defect is that each
raise lands exactly on today's line count, guaranteeing the next decision pays
for it. Pick a figure that absorbs F12.3, F12.6a and F12.6b.

Cost: postpones the question and keeps the two-file amendment ritual. It is the
honest choice only if someone believes the test file genuinely should stop
growing soon, which nobody has argued.

**Recommendation: Option A.** ADR-0137's own second amendment already contains
the argument for it — the ratchet exists to stop implementation creeping, and a
test file is not implementation. Two ceiling hits in three days, both from
sanctioned work, are evidence that the instrument is mismatched to the file
rather than that the file is misbehaving. Option B is worth doing for
readability if the harness keeps growing, but it should be its own slice with
its own reason, not a workaround for a gate.

## Consequences

- Under A, a reviewer is the only thing standing between the harness and
  duplicated cases. That is already true in practice: no ceiling ever
  distinguished a good 40 lines from a bad 40.
- Under A or C, `scripts/core_unit_tests.cpp` keeps being one file that several
  parallel tasks edit, so merge conflicts there stay likelier than elsewhere.
  Option B is the only one that fixes that, which is an argument for doing B
  eventually regardless of what is chosen here.
- Whichever is chosen, ADR-0137's Status line gains one more amendment naming
  this ADR, and the `doc-checks` recipe changes in the same commit. The
  guarded-file list and the recipe must never disagree.
