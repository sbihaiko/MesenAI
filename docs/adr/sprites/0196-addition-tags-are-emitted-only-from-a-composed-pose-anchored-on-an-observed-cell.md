# ADR-0196: `<addition>` tags are emitted only from a composed pose, anchored on an observed cell, and their target key is provably unmatched

- Status: accepted (2026-09-16) — §3 decided as option (a), a reserved palette added to the reserved pattern; user's go-ahead quoted verbatim: "confirmo". Implemented as PRD Part A slice F12.5 (2026-09-19); its hand-added overflow cell is owed, carried by Phase 14's F14.8
- Date: 2026-09-16
- Related: ADR-0165 (composition editor, external stdlib tool), ADR-0171 (the sprite layer's unit is the pose), ADR-0179 (`poses.json` succession), ADR-0183 §3/§4 (evidence vs inference; round-trip is the acceptance test), ADR-0189 (conditions cite one observed edge; bare twin), ADR-0195 (`automaticFallbackTiles`), MEP-v1 §5, `docs/hd-pack-toolchain-comparison.md`

## Context

The HD Pack format has carried `<addition>` since version 107. `HdPackLoader::ProcessAdditionTag` reads

```
<addition>[origTileData],[origPalette],[offsetX],[offsetY],[addTileData],[addPalette][,ignorePalette]
```

and `HdNesPack::ProcessAdditionalSprites` draws the *additional* tile's HD art at `(offsetX, offsetY)` from every on-screen match of the *original* tile, without spending one of the PPU's eight sprites per scanline. One community pack uses it 1987 times; the fork's loader inherited it, but the toolchain never emitted it, a row the upstream hand-author still wins. The tag lets a pose overflow its hardware bounding box (a longer weapon, a glow, a cape) — the artist draws it in the composition editor over the pose the recorder grouped (ADR-0171, ADR-0179). The **anchor** (`origTileData`, `origPalette`) is a real key the game draws; the **target** (`addTileData`, `addPalette`) is synthetic **by construction**, the tension with ADR-0183 §3 (a synthetic key colliding with a real pattern would paint the overflow onto an unrelated tile). Non-goals: no automatic anti-flicker, no `<addition>` from the recorder, no loader or renderer change.

## Decision

### 1. `<addition>` is a compose-editor export, never a recorder output

Only `compose_editor.py` produces it, from an overflow layer drawn on a pose in `sheets/poses.json`: the editor writes the overflow into the pose's sheet and a per-pose `additions[]` record into the sidecar, and `mep_build.py build` serializes the `<addition>` lines and the target key's matching `<tile>` rule. Hand-editing `hires.txt` stays forbidden.

### 2. The anchor is the pose's most-seen cell, and the offset is measured

The anchor is the cell ADR-0189 §1 picks as the spanning-tree root of the pose's group, exactly what it does for `spriteNearby`: one observed cell, one real offset. The offset is the overflow rectangle's position relative to it, in native pixels. Nothing transitive, nothing cross-pose; one `<addition>` line per 8×8 (or 8×16) cell of overflow, matching the sprite size the recording used.

### 3. The target key must be provably unmatched by the ROM

- **CHR ROM.** The target index is `chrSize/16 + n` for the *n*-th synthetic cell — past the end of CHR, never fetched, never colliding. The build asserts `index >= chrTileCount` against the iNES header.
- **CHR RAM.** Decided 2026-09-16, option (a): reserved on **both** halves. `HdTileKey` on CHR RAM compares `PaletteColors` and the 16-byte pattern together (`HdData.h`, `operator==`), and `BuildAdditionalTileCache` in `HdNesPack.cpp` matches an `<addition>` with `ignorePalette` unset only on an exact palette. The synthetic target is: pattern all-zero rows except a fixed marker row encoding `n`; palette `$0D` (`0x0D0D0D0D`), the "blacker than black" index no shipping game writes; `ignorePalette` never set. A collision then needs that pattern **and** that palette. The build runs the evidence check — no retained frame showed the reserved palette — reported *evidence-bounded, not proven*, since recordings cover routes, not the game (the gap `docs/hd-pack-toolchain-comparison.md` names under "Recording"). Rejected: (b) refuse until a full-route recording proves the pattern unused (indefinite); (c) refuse CHR RAM (loses Metroid and Contra).

### 4. Round-trip and lint

A pack with additions rebuilds to the same `(tileData, palette)` key set plus the synthetic target keys, each in the sidecar with its anchor and offset (ADR-0183 §4). `mep_lint.py` errors when an `<addition>` cites an anchor no `<tile>` rule keys, when a target key is not marked synthetic, or when a synthetic key fails the §3 check. *Refined 2026-09-23 (#382/#386; verbatim: "Manter como está").* "Keyed" = the way `HdNesPack::GetMatchingTile` finds art: a `<tile>` rule matching the CHR index by value (decimal below `<ver>103`, hex from 103) under its exact palette, or a `defaultTile=Y` rule under any palette; a rule's `[condition]` prefix is **not** weighed (Metroid (USA) #148, `006/FF001431`, title-screen rules).

## Consequences

- The comparison row "Extra tiles drawn on match" becomes measurable by a pixel-exact pose screenshot (`headless_record`, `screenshot`) plus the round trip.
- Synthetic keys are the first keys no recording observed — confined to `<addition>` targets, marked in the sidecar; every reader (`artist_cover.py`, the kit generators) must skip them or coverage inflates. `<addition>` renders only in the fork and MesenCE ≥ 107, not the frozen 0.9.x loader.
- If §3 lands as (c), CHR RAM games (Metroid, Contra) get no overflow art; a product decision, not technical.

## Record

- 2026-09-16 — accepted; §3 option (a), verbatim: "confirmo". Shipped 2026-09-19 as F12.5 (Part A §3); the hand-added overflow cell is owed (F14.8). Related: ADR-0195 (`automaticFallbackTiles`).
- 2026-09-23 — §4's "keyed" reading refined (#382/#386).
