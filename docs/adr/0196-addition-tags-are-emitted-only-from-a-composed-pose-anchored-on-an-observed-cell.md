# ADR-0196: `<addition>` tags are emitted only from a composed pose, anchored on an observed cell, and their target key is provably unmatched

- Status: accepted (2026-09-16) — §3 decided as option (a) with a reserved
  palette added to the reserved pattern; user's go-ahead quoted verbatim:
  "confirmo". Implemented as PRD Part A slice F12.5 (2026-09-19; Part A
  §3); its hand-added overflow cell is still owed and is carried by Phase 14's
  F14.8
- Date: 2026-09-16
- Related: ADR-0165 (composition editor, external stdlib tool), ADR-0171
  (the sprite layer's unit is the pose), ADR-0179 (`poses.json` succession),
  ADR-0183 §3/§4 (evidence vs inference; round-trip is the acceptance test),
  ADR-0189 (conditions cite one observed edge; bare twin), ADR-0195
  (`automaticFallbackTiles`), MEP-v1 §5, `docs/hd-pack-toolchain-comparison.md`

## Context

The HD Pack format has carried `<addition>` since version 107. The loader
(`HdPackLoader::ProcessAdditionTag`) reads

```
<addition>[origTileData],[origPalette],[offsetX],[offsetY],[addTileData],[addPalette][,ignorePalette]
```

and `HdNesPack::ProcessAdditionalSprites` draws the *additional* tile's HD
art at `(offsetX, offsetY)` from every on-screen match of the *original*
tile, without spending one of the PPU's eight sprites per scanline. One
community pack uses it 1987 times. The fork's loader inherited it; the
fork's toolchain has never emitted it, and the comparison table records that
as a row the upstream hand-author still wins.

What the tag does for an artist is let a pose overflow its hardware bounding
box: a longer weapon, a glow, a cape. The artist draws that overflow in the
composition editor over the pose the recorder already grouped (ADR-0171,
ADR-0179). Two properties of the tag matter here:

1. The **anchor** (`origTileData`, `origPalette`) is a real key the game
   draws. Anchoring on an observed cell of the pose is exactly what
   ADR-0189 does for `spriteNearby`: one observed cell, one real offset.
2. The **target** (`addTileData`, `addPalette`) is a second key that must
   itself have a `<tile>` rule pointing at the overflow pixels. The game
   never draws that tile, so the key is synthetic **by construction**. That
   is the tension with ADR-0183 §3 ("anything that would change what a
   rebuilt pack renders is only emitted when the key it carries was actually
   observed"): a synthetic key that happens to collide with a real pattern
   would paint the artist's overflow onto an unrelated tile.

Non-goals: no automatic anti-flicker (re-emitting cells the hardware dropped
would be a generator inventing placements, ADR-0183 §3); no `<addition>`
from the recorder; no change to the loader or the renderer.

## Decision

### 1. `<addition>` is a compose-editor export, never a recorder output

Only `compose_editor.py` produces it, from an overflow layer the artist drew
on a pose that exists in `sheets/poses.json`. The export is legal build input
(ADR-0165): the editor writes the overflow into the pose's sheet and a
per-pose `additions[]` record into the pose sidecar; `mep_build.py build`
serializes the `<addition>` lines and the matching `<tile>` rule for the
target key. Hand-editing `hires.txt` stays forbidden.

### 2. The anchor is the pose's most-seen cell, and the offset is measured

The anchor is the cell ADR-0189 §1 already picks as the spanning-tree root
of the pose's group. The offset is the overflow rectangle's position relative
to that cell, in native pixels, as laid out in the editor over the pose's
recorded geometry. Nothing transitive, nothing composed across poses. One
`<addition>` line per 8×8 (or 8×16) cell of overflow, matching the sprite
size the recording used.

### 3. The target key must be provably unmatched by the ROM

- **CHR ROM.** The target index is `chrSize/16 + n` for the *n*-th synthetic
  cell of the pack: an index past the end of CHR can never be fetched, so it
  can never collide. The build asserts `index >= chrTileCount` against the
  ROM's iNES header.
- **CHR RAM.** Decided 2026-09-16: option (a), a key reserved by
  convention, with the reservation placed on **both** halves of the key.
  `HdTileKey` on CHR RAM compares `PaletteColors` and the 16-byte pattern
  together (`HdData.h`, `operator==`), and `BuildAdditionalTileCache` in
  `HdNesPack.cpp` matches an `<addition>` whose `ignorePalette` is unset only
  on an exact palette. So the synthetic target is:
  - pattern: all-zero rows except a fixed marker row, `n` encoded in the
    marker for the *n*-th synthetic cell;
  - palette: the four entries `$0D` (`0x0D0D0D0D`) — the "blacker than
    black" index no shipping game writes to a palette;
  - `ignorePalette` never set on a synthetic target.

  A collision then needs the game to write that pattern **and** load that
  palette on the same tile. The build still runs the evidence check — no
  retained frame of any recording under the project showed the reserved
  palette — and reports it as *evidence-bounded, not proven*, because the
  recordings cover routes, not the game (the gap `docs/hd-pack-toolchain-
  comparison.md` names under "Recording"). The check is on the palette, not
  the pattern: the pattern half alone would inherit the coverage gap
  without the palette's protection.

  Rejected: (b) refuse until a full-route recording proves the pattern
  unused — a barrier the coverage gap makes indefinite; (c) refuse CHR RAM
  outright — it removes Metroid and Contra, the two CHR RAM games this
  toolchain is measured on, from the overflow layer.

### 4. Round-trip and lint

A pack with additions rebuilds to the same `(tileData, palette)` key set plus
the synthetic target keys, each listed in the sidecar with its anchor and
offset (ADR-0183 §4). `mep_lint.py` errors when an `<addition>` cites an
anchor that no `<tile>` rule keys, when a target key is not marked synthetic
in the sidecar, or when a synthetic key fails the §3 check for the console's
key kind.

## Consequences

- The comparison row "Extra tiles drawn on match" becomes measurable:
  pixel-exact screenshot of the expanded pose on a known frame, taken with
  `headless_record` and the `screenshot` flag, plus the round-trip.
- Synthetic keys are the first keys this toolchain emits that no recording
  observed. They are confined to `<addition>` targets, marked in the sidecar,
  and every reader of the sidecar (`artist_cover.py`, the kit generators)
  must skip them when counting coverage, or coverage inflates.
- `<addition>` renders only in the fork and in upstream MesenCE ≥ 107; a pack
  using it is not backward-portable to the frozen 0.9.x loader.
- If §3 lands as (c), CHR RAM games (Metroid, Contra) get no overflow art and
  the row stays half-won; that is a product decision, not a technical one.
