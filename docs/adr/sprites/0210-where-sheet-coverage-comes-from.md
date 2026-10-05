# ADR-0210: Sheet coverage is completed from the ROM's own CHR; a third-party index contributes palettes for CHR ROM games and the game's own pattern bytes for CHR RAM games — never a pixel of the other pack, and never conditions

- Status: accepted (2026-09-20). Go-aheads, verbatim: *"Sim, implementar agora"* (the decision), and *"em paralelo, rode a emenda da ADR-0210"* (the 2026-09-24 amendment of filter 2, shipped the same turn in `scripts/mep_import.py index` with unit tests `scripts/test_mep_import.py` / `test_index_patch`, the quote repeated in the implementing PR's body).
- Date: 2026-09-18. 2026-09-19: `defaultTile` already is the per-rule palette wildcard, so the "what stays open" claim is retracted and the title amended the same day to agree with §3 — the earlier "never art" contradicted the CHR RAM clause, where the 16 pattern bytes in a key are the game's art and are rendered by us. 2026-09-20: the Context retraction below; the Decision is unchanged. 2026-09-24: filter 2 no longer refuses a `<patch>` pack wholesale — each 32-hex key is admitted only if its 16 pattern bytes are verbatim in the stock dump, index-keyed `<patch>` keys stay refused, measured in `docs/validation/measurements/community-mapping-survey-2026-09-24.md` §1b and `docs/validation/adr/adr0210-patch-verbatim-guard-2026-09-24.md`.
- Related: ADR-0183 (the artist kit; "an observation, never a reading"), ADR-0209 Q4 (how coverage reaches 100%), ADR-0198 §2/§3 (patched-ROM key namespace; the 2026-09-24 amendment reaches the index read per key, ADR-0198 itself is unchanged), ADR-0145 (optimistic matcher), ADR-0153 (artist-legible sheets), MEP-v1 §5, PRD Part A F9.24, F12.2

## Context

ADR-0209 measured the gap blocking the artist loop — of Zelda's 2 203 recorded keys, only 319 (12.5%) have a cell on a sheet — and its Q4 option (m) proposed seeding coverage from a pack's key index on the premise that *"a `<tile>` key is `(tileData, palette)`: 16 bytes of original CHR plus four NES colours — that **is** the original art"*.

**That premise is only half true, and the false half is the majority of the library.** `HdPackLoader::ReadTileData` branches on the field's length:

- **32 hex characters or more** → the literal 16 bytes of the pattern, `IsChrRamTile = true`. A **CHR RAM** game: the art is genuinely carried in the key, because it is not in the ROM file in tile form.
- **shorter** → a **tile index** (hex from `<ver>`103 on), `IsChrRamTile = false`. A **CHR ROM** game: the key is a *pointer* into the ROM's own CHR and carries no art.

Measured 2026-09-18 across the 30-ROM bounded library: **23 CHR ROM** (88 576 tiles, statically present) and **7 CHR RAM** (Castlevania, Contra, Lifeforce, Mega Man, Mega Man 2, Metroid, The Legend of Zelda). Four facts narrowed the decision:

1. **For a CHR ROM game we already have every shape.** `scripts/artist_chr_kit.py` (ADR-0183) walks the ROM's CHR; Ninja Gaiden's `auto/` carries indices 0–8191, i.e. **8192 tiles**. A third-party index cannot add a shape to a set already complete by construction.
2. **A third-party index can be for a ROM that is not ours.** A first pass read the Ninja Gaiden pack in base 16 and reported "5 532 keys out of range" and 5 532 of its "distinct keys" as new art — **retracted 2026-09-20**: that pack is `<ver>100`, and `HdPackLoader::ReadTileData` reads the token as **decimal** below `<ver>103` and as hex from 103 on, so read the loader's way its highest index is **8 190**, inside the dump's 8 192 tiles, and nothing is out of range. The filter stands — `scripts/mep_import.py index` applies it through `Rule.parsed_index(ver)` and reports 19 153 of 19 153 rules in range. Evidence: `docs/validation/slices/f12.12-third-party-index-read-2026-09-20.md`.
3. **What a third-party index adds to a CHR ROM game is palettes.** Ninja Gaiden names 401 distinct palettes to our 364; Donkey Kong 19 to our 8. A pair whose palette we never observed will not match at run time however complete our shapes are — the palette set, not the shape set, is scarce there.
4. **Added 2026-09-24, measured.** Filter 2 first refused every `<patch>` pack outright, throwing away safe coverage. The survey (`docs/validation/measurements/community-mapping-survey-2026-09-24.md` §1b) ran the shipped read on the 32-hex `<patch>` packs with their `<patch>` lines stripped and searched each new 16-byte shape verbatim in stock: for Castlevania (2 673 / 2 673), Mega Man (3 350 / 3 350) and Zelda (1 612 / 1 612) **100 % of our recorded shapes are verbatim in stock**. Of the new shapes **249 / 257 / 44 are verbatim** (+550); the rest (236 / 239 / 18) are not — all 239 of Mega Man's sit in the *patched* ROM, the patch author's art. A 16-byte key is its own witness.

The non-goal is stated up front: this ADR does not decide how a marked figure reaches the artist's editor (that is ADR-0209), and it imports no pixel, colour choice or upscale from anyone's pack.

## Decision

Coverage has **three sources**, in this order; each cell records which it came from.

### 1. Recording — the only source that is `seen: true`

What `HdPackBuilder::ProcessTile` observed the PPU actually draw: the only source with a real `(shape, palette)` pair witnessed in play. Everything below fills holes and never overwrites it.

### 2. The ROM's own CHR — for the 23 CHR ROM games, this closes the shape gap

`artist_chr_kit.py`'s preference order (`evidence` → `borrowed` → `donated` → `fill` → `empty`) already does this; a `fill` cell is `seen: false`. For a CHR ROM game the shape side is therefore **100% by construction, with no third party and no further play** — the answer to ADR-0209 Q4 for 23 of 30 games. The residue is the palette: a shape pulled straight from CHR has no colours attached; source 3 supplies them.

### 3. A third-party key index — palettes always, art only for CHR RAM

A community pack's `hires.txt` is read as **an index of facts about the ROM** — which tiles exist, and which palettes the game puts them under. Its PNGs are never opened.

- **CHR RAM game** (7 of 30): the key carries the 16 pattern bytes, the game's own art, rendered through the same path a recorded key takes — the only static source of shape for these games. Under the amended filter 2 the admitted gain against the F12.2 sweep recordings is **Castlevania +249, Mega Man +257, Zelda +44** (+550); Contra's pack has no `<patch>` and is unaffected (+2 277).
- **CHR ROM game** (23 of 30): the shape half is discarded; only the **palette set** is taken, for indices that exist in our dump.

Three filters are mandatory:

- **Index range.** Drop any key whose `TileIndex` is outside our CHR's tile count — what catches a pack aimed at another dump.
- **`<patch>` packs — per key, amended 2026-09-24.** A pack carrying `<patch>` keys the patched ROM's namespace (ADR-0198 §2/§3):
  - **Index-keyed keys stay refused** whole (Zelda II's `Revamp.ips` re-keys CHR ROM; Metroid's `mmm.ips` converts a CHR RAM game to CHR ROM).
  - **A 32-hex key is admitted only if its 16 pattern bytes occur verbatim in the stock ROM dump** the read resolves against (`--rom`, the dump the recording's `<supportedRom>` names), over the ADR-0003 No-Intro byte range at any byte offset. Every other key — the patch author's own art, or a stock tile stored compressed — is refused and **counted** in the run's report and the index sheet's provenance.
  - Nothing else changes: an admitted key is handled like any other CHR RAM index key (`source: index`, `seen: false`, no condition, no pixel of that pack), and a pack without `<patch>` gets no guard. The guard only vets games whose CHR is stored plain; Contra's CHR is decompressed from PRG, so a `<patch>` pack for it would lose real stock shapes — accepted rather than widening the guard. A CHR ROM `<patch>` pack is index-keyed and stays refused.
- **Conditions.** `<condition>` lines are **never** imported, at any coverage cost: a `memoryCheck` is the other author's *reading*, and ADR-0183 §3 emits observations, never readings — it retains no RAM stream, so a `memoryCheck` would have to be invented rather than observed.

### Provenance is recorded per cell

Every cell carries its source: `recorded` (source 1), `chr` (source 2) or `index` (source 3). Only `recorded` is `seen: true`; a sheet built from sources 2 and 3 is an editable surface, not a claim that the tile was observed.

### The palette question is already answered — `defaultTile` is the wildcard

**Corrected 2026-09-19.** An earlier revision claimed a `<tile>` rule must name a concrete palette and that `hires.txt` exposed a wildcard only through `IgnorePalette` on `<addition>` and conditions. **That is wrong.** The last field of a `<tile>` rule — `Y`/`N`, parsed into `DefaultTile` — is not "the default artwork for this tile": when `Y`, `HdPackLoader` registers the rule under the exact key and `GetKey(true)`, whose `PaletteColors` is `0xFFFFFFFF`, and `HdNesPack` falls back to it on a miss. So source 2 is **already self-sufficient**: a shape lifted from CHR carries no palette, is emitted with `Y`, and matches whatever colours the game puts it under. Across the 30-ROM library, **88 576 of 117 650 `<tile>` rules (75%) are already `Y`** — exactly the CHR ROM tile count.

Comparing two `hires.txt` files by `(shape, palette)` equality **understates matching**, because it does not model the fallback. What stays open is therefore not palette but editing surface: ADR-0209's 1 928 Zelda keys with no cell are unreachable because nothing draws them, not because their colours disagree; Q4(k)'s remainder sheet is the answer, and no format change is involved.

## Consequences

- ADR-0209's Q4(m) is answerable concretely only for the 7 CHR RAM games; for the other 23 the answer is the ROM itself and no third party. Q4(k)'s remainder sheet remains the mechanism that makes *any* source reach the artist.
- The importer needs the ROM's CHR tile count, so it cannot run on a key index alone — a feature: it is also what catches a pack aimed at another ROM.
- Refusing conditions costs coverage (a pack like Metroid's, with 4 426 conditions); accepted, because the alternative asserts things we never saw, which is what ADR-0183 protects.
- Any tooling comparing two `hires.txt` files must normalise the index the loader's way — branch on `<ver>`, as `Rule.parsed_index` does. The prescription "Normalise the index (`int(field, 16)`)" and the base-16 reading of the index range were the same trap: base 16 is right only from `<ver>`103 on, and a `<ver>`100 pack's tokens are decimal.
- **Added 2026-09-24.** With the per-key `<patch>` guard three more catalog packs contribute shapes — **Castlevania +249, Mega Man +257, Zelda +44** (1 268 keys), with **0 admitted shapes absent from the stock dump** and 493 refused and counted (`docs/validation/adr/adr0210-patch-verbatim-guard-2026-09-24.md`). The guard is a byte search, not a judgment: a community tile equal to 16 stock bytes somewhere in PRG would pass it, and the admitted cell renders only those stock bytes, never the pack's PNG.
