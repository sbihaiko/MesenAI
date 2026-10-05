# ADR-0141: One live catalog slot per pack_id; client update trigger content_id (amends ADR-0138 §37); no auto-downgrade

- Status: accepted
- Date: 2026-08-28
- Related: ADR-0138 §37 (the reinstall trigger this ADR amends), ADR-0140 (`pack_id` sources), ADR-0143 (`pack_id` = origin × game — sibling expansion), ADR-0148 (de-listing on stale shared-zip `sha256`)

## Context
Part B §3.6 and §4 of the consolidated PRD (accepted 2026-08-28): the player must never choose between 1.0 and 1.2 of one pack — the catalog exposes one current revision per product. ADR-0138 §37 reinstalls F6.4b when catalog source.sha256 differs from .mep-install.json, which fires on wrapper-only changes and misses the real revision identity. Yanks and republished older labels must not silently downgrade an install; removing a pack from the catalog must not interrupt a player.

## Decision
Catalog (CI): one live slot per pack_id. When two candidates compete, the first rule that decides wins: (1) higher semver of pack.json version when both are comparable (an inflated version can win — triage warns, does not block — but only among same-origin candidates: ADR-0140 binding, amendment 2026-08-28, keeps a different-origin submission out of the contest); (2) else later validated_at; (3) else higher issue number. /revalidate rewrites that issue's provenance in place (mep-meta source_sha256, content_id, version, validated_at) but occupies the slot only by this order — a lower semver does not displace a higher one. History stays in mep-meta/git, never a second row. Client (amends ADR-0138 §37, superseding its trigger text): reinstall when the catalog slot's content_id differs from .mep-install.json content_id for the chosen pack_id; wrapper-only change (source.sha256 differs, content_id equal) does not reinstall. No automatic downgrade: if the installed semver is greater than the slot's, keep the install; Advanced may offer "use catalog revision" with confirmation; hd-legacy (no semver) still updates on content_id difference. Pack removed from the catalog: keep install and per-ROM choice, no toast; still visible in Advanced. Reinstall preserves DisabledPacks and per-section flags (keyed by container name, unchanged on update). Sibling folder always wins: no catalog write, update or picker while present. .mep-install.json gains pack_id and content_id next to recipe_hash / source.sha256 / deps / installed_at. Slices: P.2 (CI) and P.6 (client) of the consolidated PRD, Part B.

## Consequences

Implementation state (verified 2026-09-01):
- CI slot rule: `slot_winner` (`scripts/pack_id_rules.py:156-183`: semver → `validated_at` → issue number) inside `select_catalog_rows` (:186), applied by `scripts/generate_community_pack_catalog.py:132-134` writing `docs/community-packs.json`/`.md`; gated by ADR-0140's `pack_origin`. Tests: `scripts/test_pack_id_rules.py`.
- Client trigger: `UI/Logic/CommunityCatalogUpdateDecision.cs` (`Decide` :49-78 — `Updated` on `content_id` difference, `WrapperOnly` when only `sha256` differs, `NoDowngrade` when the installed semver is higher, `RemovedFromCatalog` keeps the install; `ReadStampFields` :83, `CompareSemver` :108), consumed by `UI/Services/CommunityPackInstallCoordinator.cs:203-226` — silent verdicts return `Skipped`, `Updated` clears and reinstalls (per ADR-0147, offers Restore when the `mep/` tree was edited locally).
- `.mep-install.json` gains `pack_id`/`content_id` (`Core/Shared/EnhancementPacks/MepRecipeInstaller.cpp:389-392`); the stamp carries no version, so the no-downgrade guard reads the installed version from `GetMepPackList` column 3 (`CommunityPackInstallCoordinator.cs:226-230`).
- DisabledPacks/per-section flags survive because the container name is unchanged (`CommunityPackInstallCoordinator.cs:196-199`).
- Sibling folder wins: `UI/Logic/PlayerPackPicker.cs:19-22` never opens the picker when a sibling pack is present; the coordinator then does not write the catalog install (PRD Part B §4).
- ADR-0138 §37's text was annotated in place (slice D5) to point here for the trigger.
Not implemented: the Advanced "use catalog revision" confirmation for a `NoDowngrade` install (no such action exists in `UI/`); no separate visibility marker for "removed from catalog" beyond keeping the install.

## Alternatives

- Keep ADR-0138 §37's `source.sha256` trigger: rejected — fires on wrapper-only repacks and misses a real revision under an unchanged wrapper.
- One row per accepted revision, player chooses: rejected — the player must never pick between 1.0 and 1.2 of one pack; history lives in mep-meta/git.
- Always follow the catalog (auto-downgrade on a republished older label): rejected — a user's newer install is kept; only hd-legacy packs (no semver) update on any `content_id` difference.
- Uninstall or toast when a pack leaves the catalog: rejected — removal must not interrupt a player.
- Block an inflated semver in CI: rejected — triage warns, does not block; the origin binding (ADR-0140) keeps strangers out.
