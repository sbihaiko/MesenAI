# ADR-0132: Per-shape palette-variant cap in the HD Pack Builder

- Status: accepted (decided; the cap has shipped in the code since F5.4b and is
  listed as a slice in the PRD — "F5.4b follow-ups | (a) saturation log when a
  shape hits `MaxPaletteVariantsPerTile`; (b) seed `_paletteVariantsByShape`
  from `_hdData` or document per-session scope | ADR-0132". Follow-ups (a) and
  (b) were implemented on 2026-08-29; this ADR was written retroactively the
  same day, since no ADR file had ever been created for the original cap.)
- Date: 2026-08-29
- Related: ADR-0043 (HD pack static export and UI expectations), ADR-0034
- Note: not part of the 2026-08-27 consolidation despite its id falling inside 0122–0137 — written retroactively for a cap that shipped without an ADR, hence no `Consolidates:` line

## Context

`HdPackBuilder::ProcessTile` (F5.4b, `Core/NES/HdPacks/HdPackBuilder.cpp`) captures one `HdPackTileInfo` per distinct `PaletteColors` value for a tile shape — the shape is `tile.GetKey(true)`, tile content with PaletteColors wildcarded. Per-shape growth was unbounded: a flat tile (e.g. all-zero TileData) renders identically under any palette, so screen state alone racks up dozens of "distinct" PaletteColors sightings. Measured on a 20s `roms/Zelda.nes` hdpack recording pre-cap: 182 shapes, median 14 variants/shape, p95 15, p99 27, one all-zero blank-tile shape reaching 71.

## Decision

1. **Cap.** A shape may hold at most `MaxPaletteVariantsPerTile = 32` real palette variants. 32 sits above the measured p99 so real diversity survives and only degenerate/near-blank outliers get bounded; beyond it, further sightings bump usage on the shape's last captured variant instead of growing the pack.

2. **Follow-up (a) — saturation log.** When the cap is reached, log a one-time `[HDPack]` message per shape (guarded by a `_variantCapLogged` set of shape hashes), so saturation is visible instead of silent.

3. **Follow-up (b) — seed from disk.** Seed `_paletteVariantsByShape` from the on-disk pack at construction (`HdPackBuilder` ctor load block), excluding `DefaultTile` neutral-ramp placeholders (the loader ignores their PaletteColors). The cap is therefore a per-shape **total** across re-record sessions, not a per-session ceiling — a re-record no longer stacks 32 more variants on disk.

## Consequences

- Pack files stay bounded for flat-tile-heavy games (blank tiles, solid-color walls) while keeping genuine per-shape diversity; a saturation message signals to draw art for a shape, not add more shots.
- Loading an existing pack costs one extra pass over `_hdData.Tiles` in the builder constructor (only when a pack exists on disk).
- The "most recently captured variant" fallback after a seed starts from the last on-disk entry in `_hdData.Tiles` order until the session captures its first new variant.
