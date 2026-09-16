# ADR-0195: The recorder always asks for `automaticFallbackTiles` on a CHR ROM pack

- Status: accepted (2026-09-16, at the user's request — "liga o
  automaticFallbackTiles no gravador" — implemented in the same change with
  unit tests covering the emitted line, `make core-unit-tests` 792/792)
- Date: 2026-09-16
- Related: ADR-0005 (the `hires.txt` envelope), ADR-0043 (CHR ROM keys are
  index-based), ADR-0127 (host-free formatting, unit-tested away from the
  emulator), ADR-0153 §3 (the alias pass, which collapses the *sheet*),
  ADR-0172 (the sidecar records the tile index), ADR-0178 (the sidecar records
  the unflipped tile data)

## Context

On a CHR ROM cartridge the same sixteen bytes of pattern data can live at more
than one bank index. The pack format keys those tiles by index (ADR-0043), so
each index needs its own `<tile>` line, and an artist who paints one of them
leaves the others rendering vanilla.

Measured on `Mega Man 3 (USA)` (128 KB CHR ROM, 8192 indices):

| | |
| --- | --- |
| CHR indices | 8192 |
| distinct 16-byte bitmaps | **6663** |
| indices that are a second or later copy of a bitmap | **1529** |
| share of the keyed `<tile>` lines that are redundant | **18.7 %** |

The format already answers this. `HdPackOptions::AutomaticFallbackTiles` makes
`HdNesPack::InitializeFallbackTiles` scan the cartridge at load time and map
every index whose bytes already belong to a keyed index onto that index, so a
lookup that misses retries against the twin. It is read
(`HdNesPack.cpp`, `InitializeFallbackTiles`), parsed by
`HdPackLoader::ProcessOptionTag` from an `<options>` line, and can be written
by `HdPackBuilder::SaveHdPack`.

**Nothing ever set it.** `HdPackData::OptionFlags` starts at zero, no line in
`Core/`, `UI/` or `scripts/` assigns the flag, and every pack the recorder has
ever written therefore carries no `<options>` line at all. The hand-written
community packs do carry one — they were the only way to reach the feature.

So the writer's `<options>` path had never been exercised by our own output,
and it was broken in a way only an exercised path shows. It emitted a comma
after **every** token:

```
<options>disableSpriteLimit,automaticFallbackTiles,
```

`StringUtilities::Split` always pushes a final token, so the trailing comma
arrives at `ProcessOptionTag` as an empty token, which falls through every
comparison into `logError("Invalid option: ")` and is counted as a load error.
The hand-written packs carry no trailing comma, which is why the defect was
never seen. It was reachable before this ADR by exactly one route: re-recording
over an existing pack, because the builder loads `hires.txt` from its save
folder at construction and inherits that file's flags.

Non-goals: this does not shrink the file. The recorder still writes one
`<tile>` line per index; only the run time stops needing all of them. It does
not touch CHR RAM, where the key is already the bitmap and the flag is inert.
It does not add a UI toggle, and it does not document the tag in MEP-v1 (that
spec has no `<options>` section at all today).

## Decision

### 1. Every CHR ROM recording asks for it, and no CHR RAM recording does

`HdPackBuilder::SaveHdPack` sets `AutomaticFallbackTiles` on
`_hdData.OptionFlags` when `_isChrRam` is false. On CHR RAM the flag is not
written: `InitializeFallbackTiles` returns immediately without
`BaseMapper::HasChrRom()`, so the option would be a claim the pack cannot use,
and writing it would put churn into every CHR RAM golden pack for nothing.

### 2. The line is built by one host-free function, with no trailing comma

`HdPackOptionsToString(uint32_t flags)` in `Core/NES/HdPacks/HdData.h` renders
the token list in the order the writer has always emitted it, joined by single
commas with none trailing, and returns the empty string for zero flags so the
`<options>` line is omitted entirely. `SaveHdPack` writes
`"<options>" + HdPackOptionsToString(...)` followed by a newline.

The formatting is host-free on purpose (ADR-0127): the recorder itself needs a
console and a PPU to run, so the emitted line is only testable if the string
logic is separable from it.

### 3. The gate is the file, not the intent

`make core-unit-tests` covers four cases: the single flag renders without a
trailing comma; the loader's own `StringUtilities::Split` sees exactly one
non-empty token for it; zero flags render the empty string; and all five flags
render in the historical order and split back into five non-empty tokens.

End to end, recorded on `Mega Man 3 (USA)` with the rebuilt tool: the pack
carries `<options>automaticFallbackTiles` on its own line and loads with no
`Invalid option` and a 100 % background tile match rate.

The load-time effect was measured as a controlled A/B on one recording. Both
copies of the pack had the 1529 redundant `<tile>` lines deleted, so neither
could match those indices directly; the only difference was the `<options>`
line:

| pack | bg tiles matched, last ~60 frames |
| --- | --- |
| with `<options>automaticFallbackTiles` | **625 920 / 3 571 200** |
| with the `<options>` line removed | 599 040 / 3 571 200 |

**26 880 tile draws recovered** by the fallback alone, on a ten-second
recording that never leaves the title screen.

## Consequences

- **Packs recorded before this ADR do not gain it.** They carry no
  `<options>` line and the run time derives nothing from a flag that is not
  there. Re-recording is the only repair, the same shape ADR-0172 and ADR-0178
  already established for their own gaps.
- **The recorder's file is unchanged in size.** Only the run time got smarter.
  That is the deliberate half-measure: now that a miss can be recovered from
  the cartridge, a later slice may stop emitting the redundant lines at all —
  about 18.7 % of a CHR ROM pack's `<tile>` lines. That is a separate decision
  and it is not taken here, because it changes what the file means to a
  *reader* that is not this run time.
- **It overlaps the alias pass without duplicating it.** ADR-0153 §3 collapses
  bank-swapped duplicates on the *sheet*, so the artist paints fewer cells;
  this ADR makes the *run time* recover an unpainted index from the ROM. A
  pack that loses one still has the other, and neither is sufficient alone: the
  alias pass only helps a pack rebuilt through `mep_build`, and the fallback
  only helps a game with CHR ROM.
- **The fallback is palette-aware, and that is what makes it safe.**
  `GetMatchingTile` replaces only `TileIndex` and re-looks-up with the
  original `PaletteColors`, so an index falls back to a twin's art only when
  that twin has art recorded under the *same* palette. Identical bytes are the
  same drawing; the palette is not assumed.
- **`mep_build.py build` round-trips the line untouched.** `<options>` is in
  `_HEADER_TAGS`, so a rebuilt pack keeps it, and `mep_lint.py` accepts the tag
  (`NES_TAGS`). What is missing is the spec: `docs/specs/MEP-v1.md` never
  mentions `<options>`, so a pack can now be lint-clean and carry a directive
  the spec does not describe. Recording `<options>` in MEP-v1 is open work.
- **One load error per pack is fixed for hand-written packs too.** Re-recording
  over a community pack that carries `<options>` used to re-emit the line with
  a trailing comma, turning every option into an error and an empty token
  beyond it.
