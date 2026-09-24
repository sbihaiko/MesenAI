# ADR-0210: Sheet coverage is completed from the ROM's own CHR; a third-party index contributes palettes for CHR ROM games and the game's own pattern bytes for CHR RAM games — never a pixel of the other pack, and never conditions

- Status: accepted (2026-09-20). User go-ahead, verbatim: *"Sim, implementar agora"*. Same-turn implementation requires unit tests covering the decision; see the implementing PR for that coverage and this quote repeated in its body. **Amendment of 2026-09-24** (filter 2's per-shape verbatim guard, below): user go-ahead, verbatim: *"em paralelo, rode a emenda da ADR-0210"*. The amendment is implemented in the same turn, in `scripts/mep_import.py index`, with unit tests covering it (`scripts/test_mep_import.py`, `test_index_patch`); the quote is repeated in the implementing PR's body.
- Date: 2026-09-18 (amended 2026-09-24: filter 2 no longer refuses a `<patch>` pack wholesale — each of its 32-hex keys is admitted only if its 16 pattern bytes are verbatim in the stock dump, index-keyed `<patch>` keys stay refused; the Decision's quoted CHR RAM gains corrected to what the filters admit, measured in `docs/validation/community-mapping-survey-2026-09-24.md` §1b and `docs/validation/adr0210-patch-verbatim-guard-2026-09-24.md`. Amended 2026-09-20: Context item 2 retracted against the measurement — its "5 532 keys out of range" is a base-16 reading of a `<ver>`100 pack's decimal tokens, and the Consequences bullet that prescribed that reading is corrected; the Decision is unchanged. Amended 2026-09-19: `defaultTile` already is the per-rule palette wildcard; the "what stays open" claim is retracted. Title amended the same day to agree with §3 — the earlier "never art" contradicted the CHR RAM clause, where the 16 pattern bytes in a key are the game's art and are rendered by us)
- Related: ADR-0183 (the artist kit; "an observation, never a reading"), ADR-0209 Q4 (how coverage reaches 100%), ADR-0198 §2/§3 (patched-ROM key namespace), ADR-0145 (optimistic matcher), ADR-0153 (artist-legible sheets), MEP-v1 §5, PRD Part A F9.24, F12.2
- Supersedes / amends: corrects ADR-0209's Q4(m) premise — a `<tile>` key is *not* uniformly "16 bytes of original CHR". The 2026-09-24 amendment changes only this ADR's §3 filter 2: ADR-0198 §2/§3 now reaches the index read per key (index keys refused, 32-hex keys vetted against the stock bytes) instead of per pack; ADR-0198 itself, which governs the import, is unchanged

## Context

ADR-0209 measured the gap that blocks the four-step artist loop: of the 2 203
keys in Zelda's recorded pack, only 319 (12.5%) have a cell on a sheet. Its Q4
asked how coverage reaches 100%, and option (m) proposed seeding it from an
existing pack's key index on the premise that *"a `<tile>` key is
`(tileData, palette)`: 16 bytes of original CHR plus four NES colours — that
**is** the original art"*.

**That premise is only half true, and the half that is false is the majority of
the library.** `HdPackLoader::ReadTileData` branches on the field's length:

- **32 hex characters or more** → the literal 16 bytes of the pattern, and
  `IsChrRamTile = true`. This is a **CHR RAM** game: the art is genuinely
  carried in the key, because it is not in the ROM file in tile form.
- **shorter** → the field is a **tile index** (hex from `<ver>`103 on), and
  `IsChrRamTile = false`. This is a **CHR ROM** game: the key is a *pointer*
  into the ROM's own CHR, and carries no art whatsoever.

Measured across the 30-ROM bounded library on 2026-09-18: **23 games are CHR
ROM** (88 576 tiles, statically present in the files) and **7 are CHR RAM**
(Castlevania, Contra, Lifeforce, Mega Man, Mega Man 2, Metroid, The Legend of
Zelda).

Three further facts came out of the same measurement, and each one narrows the
decision:

1. **For a CHR ROM game we already have every shape, and we do not need anyone.**
   `scripts/artist_chr_kit.py` (ADR-0183) walks the ROM's CHR; Ninja Gaiden's
   `auto/` carries indices 0–8191, i.e. **8192 of 8192 tiles**, and 1942 and
   Super Mario Bros. carry 512 of 512. A third-party index cannot add a shape
   to a set that is already complete by construction.
2. **A third-party index can be for a ROM that is not ours, and says so only
   arithmetically.** ~~The Ninja Gaiden pack's indices run to **33 168** against
   our CHR's 8 192 tiles. 5 532 of its "distinct keys" address tiles that do not
   exist in the dump we load.~~ A first pass of this measurement reported those
   5 532 as new art; they are nothing of the kind.

   **Retracted 2026-09-20, measured.** The example is wrong and the figure is an
   artifact of the measurement, not a property of the pack. That pack is
   `<ver>100`, and `HdPackLoader::ReadTileData` reads the tile token as
   **decimal** below `<ver>103` and as hex from 103 on. Read the loader's way
   its highest index is **8 190**, inside the dump's 8 192, and **nothing is out
   of range**; read in base 16 the same tokens top out at 33 168 with 5 532 past
   the end. The first pass read base 16 unconditionally, which is also the error
   the last Consequences bullet used to prescribe (corrected below).

   The *filter* stands and is why this was caught at all — the shipped
   `scripts/mep_import.py index` applies it through `Rule.parsed_index(ver)`,
   which branches on `<ver>`, and reports 19 153 of 19 153 rules in range. What
   does not stand is the claim that index range "disqualifies the Ninja Gaiden
   pack wholesale": it disqualifies nothing there, and the pack contributes
   exactly what item 3 says a CHR ROM pack contributes — palettes, no shapes.
   A pack aimed at another dump is still the case the filter exists for; this
   simply is not one. Evidence:
   `docs/validation/f12.12-third-party-index-read-2026-09-20.md`.
3. **What a third-party index really adds to a CHR ROM game is palettes.** The
   Ninja Gaiden pack names 401 distinct palettes to our 364; Donkey Kong 19 to
   our 8. A pair whose palette we never observed will not match at run time
   however complete our shapes are — so the palette set, not the shape set, is
   the scarce resource there.

4. **Added 2026-09-24, measured.** Filter 2 (below) as first written refused
   every `<patch>` pack outright, and that threw away safe coverage. The
   community mapping survey
   (`docs/validation/community-mapping-survey-2026-09-24.md` §1b) ran the
   shipped read on the 32-hex `<patch>` packs with their `<patch>` lines
   stripped, then searched each new 16-byte shape verbatim in the stock dump.
   Calibration: **100 % of our recorded shapes** are verbatim in the stock ROM
   for Castlevania (2 673 / 2 673), Mega Man (3 350 / 3 350) and Zelda
   (1 612 / 1 612), so for those games a verbatim shape is a stock shape, and
   a shape absent from the ROM bytes is not. Of the new shapes, **249 / 257 /
   44 are verbatim in stock** (+550); the rest (236 / 239 / 18) are not — and
   all 239 of Mega Man's are found in the *patched* ROM, i.e. the patch
   author's art. The quoted gains in §3 below (+485 / +483 / +53) were only
   reachable with filter 2 lifted with no guard at all, which would import that
   art. A 16-byte key is its own witness: whatever a patch did to the binary,
   bytes the stock dump holds verbatim are the game's.

The non-goal is stated up front: this ADR does not decide how a marked figure
reaches the artist's editor (that is ADR-0209), and it does not import a single
pixel, colour choice or upscale from anyone's pack.

## Decision

Coverage has **three sources**, used in this order, and each cell records which
one it came from.

### 1. Recording — the only source that is `seen: true`

What `HdPackBuilder::ProcessTile` observed the PPU actually draw. It is the only
source that carries a real `(shape, palette)` pair witnessed in play, and it
stays the primary source. Everything below fills holes around it and never
overwrites it.

### 2. The ROM's own CHR — for the 23 CHR ROM games, this closes the shape gap

`artist_chr_kit.py`'s existing preference order (`evidence` → `borrowed` →
`donated` → `fill` → `empty`) already does this; a `fill` cell is marked
`seen: false`. For a CHR ROM game the shape side of coverage is therefore
**100% by construction, with no third party and no further play**. This ADR
changes nothing here except to name it as the answer to ADR-0209 Q4 for 23 of
30 games.

The residue is the palette: a shape pulled straight from CHR has no colours
attached. Source 3 supplies them.

### 3. A third-party key index — palettes always, art only for CHR RAM

A community pack's `hires.txt` is read as **an index of facts about the ROM**:
which tiles exist, and which palettes the game puts them under. Its PNGs are
never opened.

- **CHR RAM game** (7 of 30): the key carries the 16 pattern bytes. Those bytes
  are the game's own art, not the pack author's, so we render them ourselves
  through the same path a recorded key takes. This is the only static source of
  shape for these games, and it is a real gain — measured against our
  recordings: **Contra +2 585 shapes, Castlevania +485, Mega Man +483,
  Zelda +53**. *Corrected 2026-09-24:* the last three packs carry `<patch>`,
  so those figures counted shapes filter 2 blocks. Under the amended filter 2
  the admitted gain, against the F12.2 sweep recordings, is **Castlevania
  +249, Mega Man +257, Zelda +44** (+550); Contra's pack has no `<patch>` and
  is unaffected (+2 277 against the same sweep recording).
- **CHR ROM game** (23 of 30): the shape half of the key is discarded (we have
  the CHR). Only the **palette set** is taken, and only for indices that exist
  in our dump.

Three filters are mandatory, and each one exists because the measurement tripped
over it:

- **Index range.** Drop any key whose `TileIndex` is outside our CHR's tile
  count. This is what disqualifies the Ninja Gaiden pack wholesale.
- **`<patch>` packs — per key, amended 2026-09-24.** A pack carrying
  `<patch>` keys the patched ROM's namespace (ADR-0198 §2/§3). What that means
  depends on the key's form:
  - **Index-keyed keys stay refused.** An index names a tile of the patched
    ROM's CHR layout and carries no bytes to check, so an index-keyed
    `<patch>` pack is refused whole, naming ADR-0198 §2/§3 (Zelda II's
    `Revamp.ips` re-keys CHR ROM; Metroid's `mmm.ips` converts a CHR RAM game
    to CHR ROM).
  - **A 32-hex key is admitted only if its 16 pattern bytes occur verbatim in
    the stock ROM dump** the read is resolved against (`--rom`, the dump the
    recording's `<supportedRom>` names), searched over the ADR-0003 No-Intro
    byte range (header and trainer skipped, clamped to the declared PRG+CHR),
    at any byte offset. Every other key of that pack — the patch author's own
    art, or a stock tile the game stores compressed — is refused and
    **counted** in the run's report and in the index sheet's provenance, never
    dropped silently.
  - Nothing else changes: an admitted key is handled exactly like any other
    CHR RAM index key (rendered from its own bytes, `source: index`,
    `seen: false`, no condition, no pixel of that pack), and a pack without
    `<patch>` gets no guard at all.

  The guard's reach is limited on purpose. It only vets games whose CHR is
  stored plain — CHR ROM, or a CHR RAM game whose tiles sit verbatim in PRG.
  Contra's CHR is decompressed from PRG (1 834 of 2 337 recorded shapes, 78.5
  %, are verbatim), so a `<patch>` pack for Contra would lose real stock
  shapes to it; that cost is accepted rather than widening the guard to
  anything not literally in the dump. The CHR ROM palettes-only rule is
  untouched: a `<patch>` pack for a CHR ROM game is index-keyed and stays
  refused.
- **Conditions.** `<condition>` lines are **never** imported, at any coverage
  cost. A `memoryCheck` is the other author's *reading* of the machine, and
  ADR-0183 §3 is explicit that this recorder emits observations, never readings
  — it "retains no RAM stream at all, so a memoryCheck would have to be invented
  rather than observed". Importing one would launder someone else's claim into
  our evidence. The 18 392 conditions across the installed packs are left where
  they are.

### Provenance is recorded per cell

Every cell carries its source: `recorded` (source 1), `chr` (source 2),
`index` (source 3). Only `recorded` is `seen: true`. A sheet built from sources
2 and 3 is an editable surface, not a claim that the tile was observed.

### The palette question is already answered — `defaultTile` is the wildcard

**Corrected 2026-09-19.** An earlier revision of this section claimed that a
`<tile>` rule must name a concrete palette, that `hires.txt` exposes a palette
wildcard only through `IgnorePalette` on `<addition>` and on conditions, and
that a per-rule wildcard on `<tile>` was therefore a pending format change.
**That is wrong.** The mechanism exists, it is in the format today, and we
already emit it.

The last field of a `<tile>` rule — `Y`/`N`, parsed into `DefaultTile` — is not
"the default artwork for this tile". When it is `Y`, `HdPackLoader` registers
the rule under **two** keys: the exact one, and `GetKey(true)`, whose
`PaletteColors` is `0xFFFFFFFF`. The lookup in `HdNesPack` then tries the exact
key first and falls back to `GetKey(true)` when it misses. That is a per-rule
palette wildcard, with the precedence a wildcard needs: a cell painted for one
specific palette wins, and the palette-agnostic cell serves every other variant.

So source 2 is **already self-sufficient**. A shape lifted out of the ROM's CHR
carries no real palette, is emitted with `Y`, and matches whatever colours the
game puts it under. Measured across the 30-ROM library: **88 576 of 117 650
`<tile>` rules (75%) are already `Y`** — and 88 576 is exactly the CHR ROM tile
count of the same library, i.e. the whole static CHR fill is wildcarded.

One consequence for anyone measuring this pack space: comparing two `hires.txt`
files by `(shape, palette)` equality **understates matching**, because it does
not model the fallback. A rule whose palette looks unmatched may match at run
time through `GetKey(true)`. The trap that produced the retracted claim above
was exactly this — a dictionary comparison in Python standing in for a two-step
lookup in the Core.

What genuinely stays open is therefore **not** palette: it is editing surface.
The 1 928 Zelda keys with no cell on any sheet (ADR-0209) are unreachable
because nothing draws them onto a sheet, not because their colours disagree.
Q4(k)'s remainder sheet is the answer, and no format change is involved.

## Consequences

- ADR-0209's Q4(m) can be answered concretely, but only for the 7 CHR RAM games;
  for the other 23 the answer is Q4 option (b)-shaped — the ROM itself — and no
  third party is involved. Q4(k)'s remainder sheet remains the mechanism that
  makes *any* of these sources reach the artist.
- The importer needs the ROM's CHR tile count to apply the range filter, so it
  cannot run on a key index alone — it always needs the matching dump present.
  That is a feature: it is also what catches a pack aimed at another ROM.
- Refusing conditions costs real coverage. A pack like Metroid's, with 4 426
  conditions, has much of its behaviour in rules we will not take. This is
  accepted deliberately: the alternative is a pack that asserts things we never
  saw, which is exactly the property ADR-0183 exists to protect.
- A first pass of this measurement over-reported the gain by comparing a
  zero-padded hex index against an unpadded one, and then by treating
  out-of-range indices as art. Any future tooling that compares two `hires.txt`
  files must normalise the index (`int(field, 16)`) and must not compare a CHR
  RAM key space against a CHR ROM one. Both traps are cheap to fall into and
  silent.
- **Corrected 2026-09-20.** "Normalise the index (`int(field, 16)`)" is itself
  one of those traps: base 16 is right only from `<ver>`103 on, and a `<ver>`100
  pack's tokens are decimal. Normalise the loader's way — branch on `<ver>`, as
  `Rule.parsed_index` does — or a pack that is entirely in range reads as
  two-thirds out of it. This is what the Context item 2 retraction above cost.
- **Added 2026-09-24.** With the per-key `<patch>` guard, three more catalog
  packs contribute shapes to the index read — measured on the survey's packs
  against the F12.2 sweep recordings, **Castlevania +249, Mega Man +257,
  Zelda +44** (1 268 keys), with **0 admitted shapes absent from the stock
  dump** and 493 shapes refused and counted
  (`docs/validation/adr0210-patch-verbatim-guard-2026-09-24.md`). Zelda II and
  Metroid stay refused (index-keyed), and every non-`<patch>` result is byte
  for byte what it was. The guard is a byte search, not a judgment: a
  community tile that happens to equal 16 stock bytes somewhere in PRG would
  pass it. For these games the calibration above makes that the right bet —
  every recorded shape is verbatim — and the admitted cell still renders only
  those stock bytes, never the pack's PNG.
