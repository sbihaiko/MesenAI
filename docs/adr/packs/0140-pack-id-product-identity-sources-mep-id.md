# ADR-0140: pack_id: product identity sources (MEP id field, owner/repo, issue-n, local: fallback) and catalog uniqueness

- Status: accepted
- Date: 2026-08-28
- Related: ADR-0139 (`content_id`), ADR-0141 (one live slot per `pack_id`), ADR-0143 (amends source 2 — `pack_id` = origin × game)

## Context
Consolidated PRD Part B §3.3/§3.5 (accepted 2026-08-28). A pack is a product with revisions, but `content_id` (ADR-0139) marks a revision in a way that makes every version look like a different pack; competing packs for the same ROM must be distinguishable from revisions, in the catalog and in the per-ROM preference (P.3). Local drops (no catalog row) are keyed by container name (ADR-0040/0049), and the allow-list (`scripts/pack_host_allowlist.json`) includes gists, raw.githubusercontent.com and Google Drive, where no owner/repo exists.

## Decision
`pack_id` resolution, first match wins: (1) an explicit 'id' field in pack.json — lowercase slug [a-z0-9][a-z0-9-]{2,63}, unique in the official catalog; this is a MEP-v1 minor bump (v1.4) adding 'id' as SHOULD to §3.1 root fields; (2) else, for github.com / codeload.github.com pack URLs, 'owner/repo' lowercased (origin, not tag or release filename); (3) else 'issue-{n}' of the accepted submission — the only option for gist/raw/Drive links without 'id', so product-level deduplication does not exist there (documented non-goal); (4) local containers without 'id': 'local:<container-name>' (the ADR-0040/0049 discovery key). Catalog uniqueness (enforced by community-pack-validate / the catalog generator): one live row per `pack_id`; same `content_id` under any `pack_id` ⇒ byte-duplicate, comment 'duplicate of #N', not listed; same `pack_id` + new `content_id` ⇒ candidate for the single slot per the one-slot ADR; different `pack_id` + different `content_id` + same ROM sha1 ⇒ both listed (competing). Duplicate submit where `pack_id` matches another issue: comment pointing at the original and close the newer issue. Client: a local container whose `content_id` equals a catalog entry adopts that catalog `pack_id`; a 'local:' preference migrates silently to the catalog `pack_id` when a matching `content_id` is later installed. MEI gains additive MAY fields `pack_id`, `content_id` (and `votes`, integer, non-normative like issue) in P.2. Amendment 2026-08-28 (origin binding, PRD Part B §3.3): a `pack_id` is bound to the origin of its first accepted submission — the owner/repo of the pack URL, or the issue author's GitHub login for hosts without one — stored in mep-meta as `pack_origin`. A submission claiming an existing `pack_id` from a different origin is not a revision: it never competes for the slot, is not listed, and receives a comment plus the `pack:needs-review` label for human triage (a maintainer may re-bind the origin or treat it as a competing pack). Slices: P.2/P.3 of the consolidated PRD, Part B.

## Consequences

Verified 2026-09-01. `scripts/pack_id_rules.py` holds `resolve_pack_id`, `pack_origin`, `apply_mei_identity`; callers `.github/workflows/community-pack-validate.yml:1043-1049`, `scripts/generate_community_pack_catalog.py:84-87`, `scripts/mei_catalog_entry.py:170`; tests `scripts/test_pack_id_rules.py`; `select_catalog_rows` (`pack_id_rules.py:186`) drops byte-duplicates (same `content_id`) and foreign-origin claims, keeps the ADR-0141 slot winner; notices by `scripts/mep_identity_check.py`, step `identity-check`.
- `id` is SHOULD in MEP-v1 §3.1 (v1.4; spec v1.6); `mep_lint.py:650-652` warns on a missing/malformed `id`; `MepPackManager::EffectivePackId` (`MepPackManager.cpp:604-613`); `PackPreferenceResolver.DerivePackId` (`UI/Logic/PackPreferenceResolver.cs:51-54`); `.mep-install.json` carries `pack_id` (`MepRecipeInstaller.cpp:389`); the §5 `content_id` merge adopts a catalog candidate (`PackPreferenceResolver.cs:64-80`).
- Catalog today: 5 `owner/repo:<game>` ids, 6 `issue-N` ids (`docs/community-packs.json`); source (2) narrowed by ADR-0143 to `owner/repo:<game-slug>`, bare `owner/repo` only without a game identity (`pack_id_rules.py:104-110`).
- Open: `mep_identity_check.py` only comments on the duplicate ("still accepted"); the `local:` → catalog `pack_id` migration is unwritten (no rewrite of the stored `RomPackPreference`); MEI-v1 §2.5 (`docs/specs/MEI-v1.md`) documents `pack_id`/`content_id`/`votes`.

## Alternatives

- `content_id` alone: rejected — every revision would look like a new pack.
- Container/zip name (ADR-0040/0049): kept only as source (4) `local:`.
- Release tag/asset filename as the GitHub source: rejected for `owner/repo` (origin); `/archive/v1.2.zip` and `/releases/download/v1.2/pack.zip` of one repo are one product.
- Product-level dedup for gist/raw/Drive links: rejected non-goal — `issue-{n}` is the id.
- `id` as MUST in MEP-v1: rejected — a SHOULD keeps every legacy `hires.txt` pack loadable.
- Pure first-match (any origin claims an existing `id`): rejected — `id: contra80s, version: 99.0.0` from a stranger would take the slot.

## Amendments (2026-09-06, code-review pass)

- Core validates the stamped `pack_id` against the slug rule before writing `.mep-install.json` (`MepRecipeInstaller::WriteInstallStamp`); an invalid `pack.id` is logged and omitted, so the container degrades to `local:<container>`. Dep ids are validated against MEP-recipe-v1 §3.3. Every stamp key and value goes through the JSON string writer, closing a key-injection path.
