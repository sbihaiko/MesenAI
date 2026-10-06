# ADR-0146: Auto-load every accepted community pack registered in a GitHub issue, whenever possible

- Status: accepted
- Date: 2026-09-01

## Context
Community HD/MEP packs from `.github/ISSUE_TEMPLATE/community-pack.yml` land, via `community-pack-validate.yml` with an `accepted` verdict (label `pack:valid`), in `docs/community-packs.md` / `docs/community-packs.json` for the client's auto-install hook (ADR-0138). The client's first automatic download is gated behind `EnhancementPackConfig.CommunityPackAutoInstallConsentGiven` (default `false`, `UI/Logic/CommunityPackConsentState.cs`, ADR-0138 §38, §51 and §54), so a registered accepted pack matching the loaded ROM by SHA1 (ADR-0003 / ADR-0039) is silently absent unless the user opted in. Issue #144: the Donkey Kong entry carries SHA1 `D222DBBA5BD3716BBF62CA91167C6A9D15C60065`, yet the flag was still `false`, so no download ran and the emulator fell back to a local bootstrap auto-only pack.

## Decision
Every community pack registered in a GitHub issue and accepted onto the "MesenCE Community Packs" board (verdict `accepted`, label `pack:valid`, i.e. a row in `docs/community-packs.json`) MUST be automatically downloaded, installed and loaded by the client whenever possible. "Whenever possible" means: the pack is reachable from an allow-listed host (ADR-0138 §41), matches the loaded ROM (No-Intro SHA1 per ADR-0003 / ADR-0039, or an optimistic texture/BPS match per ADR-0145), and is not disabled by the user. No first-run or per-pack consent dialog may block this; the `CommunityPackAutoInstallConsentGiven` gate (and the consent prompt in `EnhancementPacksWindow.EnsureCommunityPackAutoInstallConsent`) is removed for the auto-install path. This supersedes the consent-gate clauses of ADR-0138 (§38 "first-run consent", §51 "consent gate ownership", §54 "consent dialog placement"). The single master switch remains `AutoInstallCommunityPacks` (default `true`); turning it off disables every auto-load, and a per-pack manual disable (by pack_id / container) still overrides the blanket rule.

## Consequences
- An accepted pack for the loaded game is fetched, installed and applied on ROM load — no consent dialog, no per-pack opt-in.
- `CommunityPackConsentState` / `CommunityPackAutoInstallConsentGiven` become dead for the auto-install path and should be removed, so `Evaluate` always yields `CanDownloadNow=true` while `AutoInstallCommunityPacks` is on.
- The auto-installed accepted pack wins over any local bootstrap auto-only pack (human/accepted art > auto upscale; ADR-0049 sibling-folder precedence, ADR-0050 auto backgrounds), so a vanilla auto pack no longer masks accepted community art.
- Trust framing: downloading from an allow-listed host is already the accepted model (ADR-0138 §4, §41); the catalog (and the `pack:invalid` path / `pack:needs-review` label) becomes the trust boundary — a bad pack is removed from the catalog, not gated behind a prompt.
- Risk: accepted packs are trusted without a per-install prompt; a broken pack is cleaned up through the catalog verdict flow, not by consent, so the allow-list + SHA1/NI matching are the safety net.

## Alternatives
- Keep the first-run consent gate — rejected: it silently blocks the promised auto-load and caused the Donkey Kong (#144) stall.
- Auto-load only manually downloaded packs — rejected: defeats "registered pack auto-loads".
- Auto-load with no catalog/host restriction — rejected: the allow-list (ADR-0138 §41) and SHA1/NI matching (ADR-0003 / ADR-0039) already bound what may be installed.
