# ADR-0145: Optimistic pack matching fallback on SHA1 mismatch: textures + BPS auto-apply, IPS stays gated

- Status: accepted
- Date: 2026-08-31

## Context
MEP/HD Pack requires an exact No-Intro SHA1 match (ADR-0003, ADR-0039), blocking legitimate near-matches (same game, different dump/region) that are safe to apply. Patches apply in memory only (`VirtualFile::ApplyPatch`, `VirtualFile.cpp:297-322`), never written to disk, re-applied fresh from the original .nes on every load, so an optimistic attempt risks only a bad runtime session, never permanent corruption. `BpsPatcher.cpp` embeds and validates source+output CRC32 and refuses bad applies; `IpsPatcher.cpp` has no validation (only the "PATCH" magic, no checksum), so a mismatched-ROM IPS apply silently accepts garbage with no detection point. `HdNesPack` matches per-tile by pixel hash (`HdTileKey`); a non-match falls through to the original NES tile.

## Decision
Relax the mandatory-exact-match gate scoped by content type: (1) HD textures apply optimistically on SHA1 mismatch — no corruption risk since `HdNesPack` falls through to the original tile on a non-match; promote the existing bg-tile-match-rate diagnostic (`HdNesPack.cpp` `_debugBgTileLookups`/`_debugBgTileMatches`) into a runtime health signal to warn/auto-disable when match rate stays low. (2) BPS patches apply optimistically — the format self-validates via embedded CRC32 and refuses bad applies, so no new work. (3) IPS patches do NOT relax — keep exact SHA1 required until a manifest field for expected output hash is added (spec bump) to give IPS the self-validation BPS has. (4) Audio/MEP fingerprint replacement is out of scope — keep exact match (offsets/timing are calibrated per-ROM). Catalog/local packs for a near-matching ROM (same game, different dump/revision) can now apply textures and BPS patches; IPS-only packs remain blocked until output-hash validation lands. This amends the scope of ADR-0003/ADR-0039 for texture and BPS-patch application specifically — the No-Intro SHA1 contract and computation are unchanged, only the mandatory-block behavior on mismatch is narrowed by content type.

## Consequences

Implementation state (verified 2026-09-01), shipped in `162a48d3`:
- Candidate selection: `Core/Shared/EnhancementPacks/MepPackManager.cpp:753-770` keeps a SHA1-mismatched container as an optimistic candidate (`_optimisticContainers`, `MepPackManager.h:32-38`) and sorts exact matches ahead (`:765-770`); a name-matched convention pack is never optimistic (`:739-740`); `IsOptimistic` (`:798`).
- Content gating: only `Textures` may come from an optimistic pack — Audio/Synth sections are skipped (`:864-866`, `:898-902`); patches: a BPS patch is attempted on mismatch (`FindFirstBpsPatch`, `:983-992`), IPS/UPS stay gated unless the pre-existing `ApplyPatchOnHashMismatch` override is on (`:993-996`; `SettingTypes.h:820`, `UI/Config/EnhancementPackConfig.cs:19`).
- Health signal: `Core/NES/HdPacks/HdNesPack.h:83-88` (`kHealthSignalMinMatchRate = 25`, `kHealthSignalWindowLimit = 5` one-second windows); `HdNesPack.cpp:647-672` fires `HandleLowTextureMatchRate()` once per HD pack load; `MepPackManager.cpp:816-830` auto-disables the textures pack only when the load-time snapshot (`_texturesIsOptimistic`, `:545-554`) says it was optimistic, and logs it.
- Load log names the optimistic packs (`MepPackManager.cpp:567`) so a wrong-game pack is diagnosable.
- Auto-install consequence: catalog rows whose No-Intro sha1 differs from the loaded dump can now apply textures/BPS (CLAUDE.md "Policy — auto-load all registered packs" cites this ADR).
Not implemented: the IPS expected-output-hash manifest field (no such key in `docs/specs/MEP-v1.md`), so IPS-only packs still require an exact SHA1; the health signal covers NES bg tiles only (GB/SMS HD packs have no equivalent monitor); no UI toast accompanies the auto-disable (log only).

## Alternatives

- Full ROM backup-and-restore before an optimistic patch: rejected — patches apply in memory only (`VirtualFile::ApplyPatch`), the disk is never touched.
- Relax IPS like BPS: rejected — IPS has no source/output checksum, so a mismatched apply is silently accepted garbage.
- Relax audio/synth fingerprint replacement too: rejected — offsets and timing are calibrated per ROM.
- Prompt on every mismatch: rejected — ADR-0146 forbids consent gates on auto-load; the runtime health signal replaces the question.
- Keep the mandatory exact-match contract (ADR-0003/0039): rejected — it blocked legitimate near-matches that are self-validating or self-healing.
