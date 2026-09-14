# docs/

## Purpose

Durable documentation for this fork: open specs, execution plans, and the enhancement-ecosystem narrative. Not source of truth for runtime behavior — the code and ADRs win if they diverge.

## Ownership

Owns `docs/specs/` (CC0), `docs/roadmap/` (the consolidated PRD), `docs/adr/` (the decision register, moved here from `.dev-squad/adr/` on 2026-09-03), `docs/media/`, `docs/validation/` (manual acceptance/validation run scripts), and top-level ecosystem notes. Does not own `AGENTS.md` files in other trees or Core/UI source.

## Local Contracts

- Specs under `specs/` stay CC0 and RFC 2119; breaking change = major semver bump of that spec.
- `adr/` is a different contract from `specs/`: not CC0, not RFC 2119, ids never reused (ADR-0035), three statuses only (`proposed`/`accepted`/`superseded`). Write one with the `adr` skill; see CLAUDE.md for the binding rules.
- Roadmap plans record status in the header and do not duplicate ADRs.
- No derivative game content except the short `media/` demo excerpts already allowed by `CONTRIBUTING.md`.

## Work Guidance

- Planning: `docs/roadmap/PRD-mesence-enhancement-ecosystem.md` — one
  consolidated PRD, two Parts: Part A (pack/core roadmap) and Part B (default
  GUI, pack identity, picker). No `plano-*.md`. See `docs/roadmap/AGENTS.md`
  for the contract.
- Community-pack catalog rows (`docs/community-packs.md` / `.json`) are
  **generated** by the validate/catalog workflows — never hand-edit pack rows.
  Extra ROM hashes for auto-install belong in `scripts/rom_target.py`;
  `community-packs.json`'s `rom.sha1`/`rom.sha1s` must match that map. The
  intake mechanics (host allow-list, labels, Issue Form pipeline) are
  documented by the `.github/` workflows and the human-facing
  `docs/hd-pack-authoring.md`. The 2026-08-30 agent intake brief
  (`community-pack-intake-handoff.md`) was consumed and deleted 2026-09-03;
  its full text lives in git history — do not revive it as a task.
- `remastering-a-game.md` - **the artist's entry point**, and the only
  top-level doc written for someone who wants to *redraw a game's art* rather
  than browse, play, or submit. Task-ordered: record (the four drivers -
  `input=`, `movie=`, `cheat=`, `state=`), measure with `artist_cover.py`,
  unpack into a kit (the four generators + the assembler), paint, build and
  verify, optionally run the AI proposer, ship. It exists because the pipeline
  was reachable only through the ADRs, which is the failure
  [ADR-0182](../docs/adr/)–0189's own motivation names: a tool nobody can find
  is not a tool. Keep it that way - every command in it is meant to be
  copy-pasteable, and a command that drifts is worse than no doc.
- `hd-pack-authoring.md` - human-facing guide for community HD/MEP pack
  submissions, linked from `.github/ISSUE_TEMPLATE/community-pack.yml`;
  summarizes the "Aceito (MEP completo)" vs. "Aceito parcial (HD Mesen)" vs.
  "Inválido" triage outcomes and cites `docs/specs/MEP-v1.md` §5.1/§5.2/§5.3/§6.
  Also documents the split-distribution/MEP Recipe flow (ADR-0138 §12):
  the `external_assets`/`external_assets_license` form fields and the
  `assets:external` label, citing `docs/specs/MEP-recipe-v1.md`.

## Verification

- Specs: `python3 scripts/validate-specs.py` from the repo root.
- ADRs: `python3 scripts/checks/verify_adr_refs.py` (also in `make doc-checks`) — every cited `ADR-NNNN` resolves to a file.
- Upstream coexistence (ADR-0163): tiers = `scripts/upstream_tiers.py`; `Upstream-Delta:` trailer check = `scripts/checks/verify_upstream_delta.py`; sync = `scripts/sync-upstream.sh` (local, merge on `main`) + `.github/workflows/sync-upstream.yml` (scheduled PR when upstream moves).
- `hd-pack-authoring.md`: `./scripts/checks/verify_hd_pack_authoring_doc.sh`.
- `remastering-a-game.md`: `python3 scripts/checks/verify_remastering_guide.py`
  (also in `make doc-checks`) — every local link resolves, and every flag the
  guide prints on a harness or generator it documents is a flag that script
  actually accepts. A guide whose commands have silently rotted is the exact
  discoverability failure it was written to fix, so it is checked rather than
  trusted.
- Plans have no automated check.

## Child DOX Index

- adr/ — the decision register (`NNNN-<kebab-title>.md`); accepted ADRs are binding
- specs/ — ESP, MEP, MEI, MEP-recipe, hires-gbsms drafts and `golden/`
- roadmap/ — consolidated PRD (Part A: pack/core; Part B: player shell) (product consoles: NES, GB, SMS-family, GBA)
- validation/ — manual acceptance/validation run scripts (e.g. F6.5 checklist, validation-automation plan)
