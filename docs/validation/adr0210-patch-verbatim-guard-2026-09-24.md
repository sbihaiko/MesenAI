# ADR-0210 filter 2 amended: a `<patch>` pack vetted per key against the stock bytes (2026-09-24)

User go-ahead, verbatim: *"em paralelo, rode a emenda da ADR-0210"*.

The community mapping survey
(`community-mapping-survey-2026-09-24.md` §1b) measured that ADR-0210 §3's
filter 2 discarded safe coverage by refusing every pack that carries
`<patch>`. The ADR is now amended in place, and the change is implemented in
the same turn with unit tests:

- **Index-keyed `<patch>` packs stay refused whole.** Their keys are tile
  indices into the patched ROM's CHR layout (ADR-0198 §2/§3), and an index
  carries no bytes that could be checked.
- **A 32-hex `<patch>` pack is read key by key.** A shape our recording lacks
  is admitted only if its 16 pattern bytes occur verbatim, at any byte
  offset, in the stock `--rom`'s ADR-0003 No-Intro range: header and trainer
  skipped, clamped to the declared PRG+CHR. Every other key is refused and
  counted. That covers the patch author's own art, and also stock tiles that
  the game stores compressed.
- **Unchanged:** a pack without `<patch>` (no guard at all), the CHR ROM
  palettes-only rule, conditions never read, and no pixel of the other pack
  ever opened.

## Code

- `scripts/mep_import.py`, `read_index`: the wholesale refusal now fires only
  for `theirs.patches and theirs.index_keyed`. In `_read_index_shapes`, when
  their pack has `<patch>`, each new shape is searched in
  `mep_patch.no_intro_body(rom)`. Refusals go to `dropped.not_in_stock_rom`
  (rules) and to `plan["patch_guard"]` (`patches`, `admitted_shapes`,
  `admitted_keys`, `not_in_stock_shapes`, `not_in_stock_keys`). The index
  sheet's sidecar carries the same block as `origin.patchGuard`, and the CLI
  prints one line about it.
- `scripts/mep_patch.py`: `no_intro_body(data, suffix)` is split out of
  `no_intro_sha1`, so the hash and the guard read the same byte range.

## Unit tests (`scripts/test_mep_import.py`, `test_index_patch`)

The synthetic CHR RAM stock dump holds shapes 2, 5 and 6 verbatim in PRG, at
odd offsets. Shape 8 sits only in trailing bytes past the declared PRG. A
real one-record IPS writes shape 7, and the fixture asserts that the patched
ROM contains shape 7 and the stock dump does not.

- **RED** before the change: *"a 32-hex <patch> pack is still refused
  wholesale"*.
- **GREEN** after it. Shapes 5 and 6 get cells: 3 keys, with a second palette
  as an alias and a conditioned rule contributing its bare key. Shapes 7 and 8
  are refused: 2 shapes, 3 keys, 3 rules. The already-recorded shape counts as
  such. The CLI exits 0 and reports the guard, and the sidecar carries
  `origin.patchGuard`. An index-keyed `<patch>` pack is still refused against
  a CHR RAM recording and against a CHR ROM one (exit 2, nothing written). A
  pack without `<patch>` still admits a shape that is absent from the stock
  bytes.
- **Mutation 1**, guard disabled (`if False and theirs.patches`): 7 checks
  fail, and shapes 7 and 8 are admitted.
- **Mutation 2**, the whole file searched instead of the No-Intro range: 6
  checks fail, and shape 8 is admitted from the trailing bytes.
- Restored: `all checks passed`.

## E2E: the survey's inputs, re-run with the shipped code

The inputs are the survey's downloads (catalog sha256 verified there, nothing
re-downloaded) and the survey's own references: the F12.2 sweep `auto/`
packs (scratch copies) and the local library dumps. Every pack's
`hires.txt` was used as distributed, `<patch>` lines included.

| # | game | `<patch>` | outcome | shapes admitted (keys) | refused as not verbatim: shapes (keys) | admitted but absent from stock dump | survey §1b "verbatim" |
|---|---|---|---|---|---|---|---|
| 143 | Castlevania | 3 | sheet written | **249** (455) | 236 (1 197) | **0** | 249 |
| 138 | Mega Man | 2 | sheet written | **257** (471) | 239 (411) | **0** | 257 |
| 139 | The Legend of Zelda | 1 | sheet written | **44** (342) | 18 (195) | **0** | 44 |
| 141 | Zelda II | 1 | refused, index-keyed (exit 2) | 0 | — | — | refused |
| 148 | Metroid | 5 | refused, index-keyed (exit 2) | 0 | — | — | refused |

- **Total: +550 shapes / 1 268 keys, the survey's +550 exactly.** For all
  three games the admitted set is a subset of the survey's relaxed
  (`<patch>`-stripped) set, and its size equals that set's verbatim count.
  Every admitted cell was re-checked against the dump's No-Intro range:
  **0 absent**.
- Mega Man's 239 refused shapes are the ones the survey found only in the
  *patched* ROM, which is the patch author's art, and they stay out.
- A rebuild check was run on Mega Man: `mep_build.py build` on the pack
  carrying the new `sheets/index.*` gives 0 errors, and all 471 index keys
  are in the rebuilt `hires.txt` as bare rules.
- **Non-`<patch>` packs are unchanged.** Contra80s still gives 2 277 shapes,
  4 946 keys and 125 palettes, and its cell list and palette list are
  identical to the survey's run. It gets no guard, which matters because
  1 348 of its 2 277 admitted shapes are not verbatim in the ROM (the CHR is
  decompressed). Ninja Gaiden, Donkey Kong, SMB, Pac-Man, Bomberman and Ice
  Climber give the same palette sets as the survey (401 / 20 / 46 / 25 / 12 /
  25), with no sheet and 0 out of range.

Input identity (their `hires.txt` sha256 prefix / dump sha1 prefix):
Castlevania `da731d3eb4caefc0` / `7A20C44F302F`, Mega Man `b88a5d14d806e0c2`
/ `2F8838155733`, Zelda `8b734893e21adf36` / `3CDFA4F28B04`, Contra80s
`ed68f6f9fd3e96f1` / `C9EA66BB7CB3`.

## Nothing contradicts the survey

Every §1b number was reproduced. One small addition to it: the ADR's quoted
Mega Man gain was +483 while the survey's relaxed run gave 496. The amendment
replaces both with the admitted 257.

## Limits

- The guard is a byte search. It vets only games whose tiles are stored plain
  (CHR ROM, or CHR RAM copied verbatim from PRG). A `<patch>` pack for a
  compressed-CHR game like Contra would lose real stock shapes to it, and the
  amendment accepts that cost.
- A community tile that happens to equal 16 stock bytes somewhere in PRG
  would pass. The calibration (100 % of recorded shapes verbatim for these
  three games) makes this the right bet, and an admitted cell renders only
  those stock bytes.
- Still manual. As the survey notes, nothing calls `mep_import.py index`
  automatically.

## Reproduce

```
python3 scripts/mep_import.py index "<pack>/hires.txt" \
    --pack <copy of the sweep auto/textures>/hires.txt --rom "<library>/<game>.nes" --report index.json
python3 scripts/test_mep_import.py
```
