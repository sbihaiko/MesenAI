# docs/roadmap/

## Purpose

The fork's planning lives in one consolidated PRD under this folder:

- `PRD-mesence-enhancement-ecosystem.md` — the single roadmap, organised
  as two Parts. Part A is the pack/core roadmap (vision, legal principles,
  standards, compact delivery record, and remaining work: Phase 9 painting
  verification, F9.18's human panel and bounded coverage; Phase 10 feasibility.
  Phase 11 consolidation is complete; its proxy experiment is not human
  product acceptance. Phase 12 artist-surface cold-reads are a fresh Fable
  session, ADR-0214). Part B is the default-GUI roadmap (player chrome,
  Advanced GUI, `pack_id`/`content_id`/version, duplicates, picker,
  quick-enhancements panel; P.1-local identity integration shipped 2026-09-17, ADR-0206). Part A's live work is Phase 12 and the ADR-0205 replay slices. Each Part carries its own header `Status`,
  slice table, and ADR map, which are the source of truth for that
  surface.

This is the 2026-08-30 unification of the former two PRDs
(`PRD-mesence-enhancement-ecosystem.md` and `PRD-player-shell.md`) into a
single file with two Parts. Each Part keeps its own internal `§N`
numbering verbatim, so a `§N` reference resolves within the Part that
uses it.

Do not revive `plano-*.md` or the 2026-08-27 deleted PRDs. Do not split
this file back into multiple PRDs; a new product surface is a phase in
one of the two Parts.

## Ownership

Owned with `docs/` (see parent `docs/AGENTS.md`). Does not own specs
(`docs/specs/`), ADRs (`docs/adr/`), or runtime behavior.

## Local Contracts

- One consolidated PRD, no `plano-*.md`. Pack/core slices go in Part A;
  player-shell slices go in Part B. When a slice ships, move it to its
  Part's shipped record as one line and delete its row — completed plans
  are not kept as prose here, git history is the record. The 2026-08-27
  consolidation (deleted `plano-execucao-F3/F5`, `plano-reducao-consoles`,
  `plano-host-input-tester`, and the two earlier ecosystem PRDs) still
  stands; the player-shell surface was added on 2026-08-28 and merged
  into this single file as Part B on 2026-08-30, not revived as a
  separate file.
- Product consoles on `main`: NES, GB/GBC/GBS, SMS/GG/SG-1000, GBA. Plans
  must not assume SNES, PC Engine, WonderSwan, or ColecoVision cores, MSU-1,
  or Super Game Boy. SNES **gamepads** (`SnesController` and related port
  devices) stay as input for the remaining cores.
- Decisions are not made in a PRD: an architecture/trade-off choice goes
  through an ADR (the `adr` skill), and the relevant Part's ADR map
  points at it.
- One slice per task; never hand a whole phase or the whole PRD to a single
  run — slice it and settle the slice's ADRs first.
- Acceptance distinguishes structural integrity, runtime visual correctness,
  independent agent workflow trials and human usability. Completion of an
  experiment does not certify the product promise. Missing evidence is recorded
  as not evaluated, never as a pass. Text summaries identify binary/input hashes
  and live in `docs/validation/`; ROM-derived artifacts remain local.
- Live slices state bounded inputs, prerequisites and a stop rule. Completed
  spikes leave the live table; implementation debts remain visible even when
  the surrounding phase has shipped. Reconcile duplicated status text in the
  same change. A documentation review may cover the entire PRD; the one-slice
  limit applies to implementation tasks.
- Prose is en-US (CLAUDE.md); quoted GitHub Project Status option names
  stay verbatim.
- The "delete the row when it ships" rule above is enforced:
  `python3 scripts/checks/verify_prd_live_rows.py` (wired into
  `make doc-checks`) reads every slice table under Part A §4 and Part B §8
  and fails when a row's Decision cell declares the slice shipped — the cell
  is split on `;`, `,` and a spaced em dash, and a fragment that *starts
  with* `shipped` is the offence. A cell that merely mentions the word
  mid-sentence, or that still owes work, passes.

## Verification

`python3 scripts/checks/verify_prd_live_rows.py` — no shipped row in a
live slice table (see Local Contracts above). Specs used by a plan:
`python3 scripts/validate-specs.py`. Nothing else about a plan is checked
automatically.

## Child DOX Index
