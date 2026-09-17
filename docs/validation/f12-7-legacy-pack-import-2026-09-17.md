# F12.7 — importing a legacy plain HD pack into a MEP project (2026-09-17)

Goal, per the PRD row: `mep_import.py` turns a `hires.txt` pack **without** an
IPS into a MEP project that rebuilds to the same rule set and the same pixels
(ADR-0198 §1). Stop when `build` on the imported project equals the input by
`(tileData, palette, condition)` **and** by pixels.

This log records the run: inputs and hashes, the exact commands, the per-rule
and per-pixel result on each pack, the refusals, and the one shape the
round-trip cannot reproduce — with the numbers that bound it.

## Tool and commands

- `scripts/mep_import.py`, stdlib only (ADR-0165), sha256
  `b9c407cd1b4de89992b16e63807afd3790bbc50d11e31f6cac5a1fc429a04ce0`,
  1010 lines. `scripts/test_mep_import.py` (44 checks, sha256
  `18f08bcd2cb11c7819f92e95696015bfd135bdc56818d2eb5243381e0e60afc1`) is
  picked up by `make python-tests` by name and covers the round-trips below
  on synthetic packs, every refusal path, and the moved-key report.
- The project is written next to the user's copy and never into a repository
  (ADR-0198 Consequences). The imported projects of this run live in the
  session scratchpad, not versioned.

```bash
python3 scripts/mep_import.py import "<pack folder>" --out <project folder>
python3 scripts/mep_build.py build <project folder>          # the rebuild under test
python3 scripts/mep_import.py verify "<pack folder>" <project folder> [--strict]
```

`verify` is ADR-0198 §1's acceptance test made executable: it maps every rule
of both manifests to its loader key `(tileData, palette, condition)`, compares
the key sets, compares the pixel block each key names against the art the
input's *first* matching rule names (the one the run time draws), and compares
the carried lines (`<condition>`, `<background>`, `<addition>`, `<fallback>`,
`<bgm>`/`<sfx>`) as sets. `--strict` additionally fails on the ADR-0189 §3
bare twins `build` adds to a conditioned key.

## 1. Inputs

| Pack | `hires.txt` sha256 | `<ver>` | scale | keys | rules | `<img>` | Source |
|---|---|---|---|---|---|---|---|
| Ninja Gaiden (1989) (Tecmo) | `71fe70b2e079ee62…fbc9cdd` | 100 | 1 | CHR index | 19153 | 296 | installed (local data home, 304 files) |
| Bomberman (USA) | `c4e3b12b2def646f…a7cd0534` | 105 | 1 | CHR index | 500 | 5 | catalog issue 207, zip sha256 `f048920df018f1ec…3ec87765` = the catalog's, fetched from the allow-listed Dropbox URL |
| Contra80s (Contra (USA)) | `ed68f6f9fd3e96f1…21d9996f8` | 106 | 2 | 16 data bytes | 13218 | 152 | installed by an earlier catalog run (issue 137, `tastichacks/contra80s`, pack sha256 `b174830de2f9a59a…d945ff9e`) |
| Super Mario Bros. (1985) (Nintendo) | `a49f19a51c1b87d3…14c84298` | 104 | 2 | CHR index | 3966 | 11 | already in the data home; **extra** data point, not one of the row's bounded inputs |

Bomberman was the only pack fetched for this run. `scripts/fetch_pack.py`
matched the Dropbox host and path against `scripts/pack_host_allowlist.json`
(ADR-0138 §41 / ADR-0187) and re-validated the `/scl/fi/` redirect hop; the
downloaded zip's sha256 equals the catalog's validated hash, so the artifact
is byte-identical to the accepted submission. Note for a managed machine: the
fetcher first failed with `CERTIFICATE_VERIFY_FAILED` because this python.org
3.12 has no CA bundle at its default path — with
`SSL_CERT_FILE=$(python3 -c 'import certifi;print(certifi.where())')` it
downloaded the same bytes (the same hash, twice, from two transports).

## 2. Ninja Gaiden — exact, `--strict` passes

```
imported … 19153 rule(s) over 9278 cell(s) in 296 sheet(s) (keyed by CHR index, hex tokens, <ver> raised to 103)
source … 19153 rule(s), 19147 distinct key(s)
built  … 19147 rule(s), 19147 distinct key(s)
rule set: 0 missing, 0 unexpected extra, 0 ADR-0189 §3 twin(s) added by build
pixels: 19147 key(s) draw exactly the art the source's first matching rule draws, 0 differ
carried: 0 condition/background/addition/fallback/audio line(s) (0 missing, 0 unexpected extra)
tokens: 19147 key(s) whose tileData *text* changed, e.g. 1 -> 01 — `_index_token`'s hex width (ADR-0172); the parsed key is unchanged
OK: build on the imported project regenerates the input's rule set with identical pixels
```

`build` exits 0 and its `mep_lint` run reports 1 warning (6 duplicate `<tile>`
lines in the input itself, which lint reports and the loader also resolves to
the first). The input has 19153 rules for 19147 keys — the six repeats are the
input's own, and the rebuild keeps one rule per key, as `mep_build` does for a
recorded pack. Its `<ver>` is below 103 and its keys are CHR indices, so the
import raised `<ver>` and rewrote each key through `_index_token`: that is the
only reason the token *text* of all 19147 keys changed while every parsed key
stayed the same.

## 3. Bomberman — exact, `--strict` passes

```
imported … 500 rule(s) over 500 cell(s) in 5 sheet(s) (keyed by CHR index, hex tokens)
  2 <background> PNG(s) copied into auto/textures/
source … 500 rule(s), 500 distinct key(s)
built  … 500 rule(s), 500 distinct key(s)
rule set: 0 missing, 0 unexpected extra, 0 ADR-0189 §3 twin(s) added by build
pixels: 500 key(s) draw exactly the art the source's first matching rule draws, 0 differ
carried: 3 condition/background/addition/fallback/audio line(s) (0 missing, 0 unexpected extra)
OK: build on the imported project regenerates the input's rule set with identical pixels
```

This is the smallest of the three bounded inputs and the only one that carries
`<background>` rules with a condition prefix (`[titlescreen]` /
`[!titlescreen]<background>bg01.png,1.0,1,0,N,0,0`, 8 fields) — carried
verbatim, and the two PNGs land in `auto/textures/`, which is where `build`
copies them up from. Its `<ver>105` is already at or above 103, so no version
rewrite happens (the CLI says so). `build` exits 0; lint's 2 warnings are the
input's own duplicate `<condition>titlescreen` line and the duplicate `<tile>`
report.

## 4. Contra80s — rule set exact, 914 of 8950 keys' art differs

```
imported … 13218 rule(s) over 6362 cell(s) in 152 sheet(s) (keyed by the tile's 16 data bytes)
  2969 <background> PNG(s) copied into auto/textures/
  WARNING: 987 key(s) share their (tile data, palette) pair with another key at a different crop; …
  note: 3 line(s) are not a tag and carry no rule … kept verbatim: line(s) 3210, 10229, 13478
  note: 18 key(s) get an ADR-0189 §3 bare twin on the first build
source … 13218 rule(s), 8950 distinct key(s)
built  … 8968 rule(s), 8968 distinct key(s)
rule set: 0 missing, 0 unexpected extra, 18 ADR-0189 §3 twin(s) added by build
pixels: 8036 key(s) draw exactly the art the source's first matching rule draws, 914 differ; 1610 key(s) name more than one crop in the source
carried: 3729 condition/background/addition/fallback/audio line(s) (0 missing, 0 unexpected extra)
FAIL: the import does not round-trip (10 finding(s) shown)
```

`build` on this project exits 1, but not because of the import: `mep_lint`
reports 2 errors for `hires.txt:3194 <background> Stage1.png does not exist`,
which is the input's own dangling entry — the catalog already carries it as
errata (`pack:known-missing`, ADR-0152) for pack sha256 `b174830d…`. The
rebuild writes the same line it read.

The three stray lines are the input's own comments that lost their `#`
(" Background 2 - second 2 seconds", "Leve 4 - Block"): `HdPackLoader` skips a
line that is not a tag, so the import carries them verbatim instead of turning
a typo into an unimportable pack; lint warns about each one on the way through.

The pixel half does **not** hold, and §5 is the measurement of why. This pack
is CHR RAM and data-keyed, so no `<ver>` rewrite is involved: the 914 keys are
a property of the shape, not of the key form.

## 5. Super Mario Bros. — the same shape, worse

```
source … 3966 rule(s), 3601 distinct key(s)
built  … 3899 rule(s), 3899 distinct key(s)
rule set: 0 missing, 0 unexpected extra, 298 ADR-0189 §3 twin(s) added by build
pixels: 2134 key(s) draw exactly the art the source's first matching rule draws, 1467 differ; 165 key(s) name more than one crop in the source
carried: 2889 condition/background/addition/fallback/audio line(s) (0 missing, 0 unexpected extra)
tokens: 1476 key(s) whose tileData *text* changed, e.g. 100 -> 0100 — `_index_token`'s hex width (ADR-0172); the parsed key is unchanged
```

`build` exits 0 (0 errors, 1 duplicate-tile warning). 1467 of 3601 keys —
41 % — draw a different crop after the rebuild. This pack is `<ver>104`
index-keyed, so no version rewrite happens at all; the delta is 100 % shape.

## 6. The shape `mep_build.py build` cannot express

A pattern is a `(tileData, palette)` pair; a key is that pair plus a
condition. `build` collects a pair's conditions from the key source **as a
whole** (`keysrc_attrs`, `_condition_variants`) and emits, for whichever crop
wins the pair, one rule per condition. So two conditions of one pair cannot be
drawn from two crops, and a legacy manifest that does exactly that — the same
tile art under `[norris1&crawl_step1]` and `[norris2&crawl_step2]` — loses the
association on the way through.

| Pack | rules | keys | patterns | patterns naming >1 crop | keys affected | keys whose art differs |
|---|---|---|---|---|---|---|
| Ninja Gaiden | 19153 | 19147 | — | 0 | 0 | 0 |
| Bomberman | 500 | 500 | — | 0 | 0 | 0 |
| Contra80s | 13218 | 8950 | 7836 | 592 | 987 | 914 (10.2 %) |
| Super Mario Bros. | 3966 | 3601 | 2076 | 430 | 1502 | 1467 (40.7 %) |

The import does not hide it: it names the count in `IMPORT.md` and on the CLI,
`_plan_cells` carries each pair with exactly one crop (the one the input's
first rule for that pair names) so the association it *can* keep is exact, and
`verify` measures the rest. Only keys whose pair is named at more than one
crop are affected, and for a subset the moved crop happens to hold identical
bytes (Contra80s: 987 moved, 914 differ — 73 coincide).

Two ways to close it, for the record:

1. **F12.6a** (ADR-0197 §1, accepted): "sheets accept a hand-written
   condition". Once a sidecar cell can carry its own condition, a crop can be
   the *only* source of one condition of a pair, and the association becomes
   expressible where it belongs — in the sheet.
2. A fallback to the ADR-0049 positional layout (one cell per **rule line**,
   16-column grid, mapped by index, not by key). It would satisfy the pixel
   test on these packs today, but it is a deprecated layout whose cells are
   tied to the key source's line order — inserting one line silently shifts
   every crop after it — and ADR-0198 §1 asks for ADR-0153 sheets. Not taken
   here; it is a decision for the user, not a silent divergence.

## 7. Refusals, measured on the real packs

| Input | Result |
|---|---|
| Castlevania, The Legend of Zelda, Mega Man, Metroid, Zelda II (installed) | refused: `this pack ships a <patch> … ADR-0198 §2 refuses the import until §3's patched-ROM slice lands` — **5 of the 10 installed packs** |
| Donkey Kong (1983) (Nintendo) | refused: index-keyed, and `line 372: frameRange operands are hex at <ver>101 and decimal at 102+` — the 101→103 raise would re-read the condition, so the import refuses instead of importing shifted keys |
| 1942 (1985) (Capcom) | refused: `no <tile> entries to import` — this pack is **audio-only** (16 `<bgm>` lines, no `<img>`); `mep_build.py build` also refuses a key source with no `<tile>`, so such a pack has no importable project shape today |

Bomberman, Ninja Gaiden, Super Mario Bros. and Contra80s carry no `<patch>`,
so they imported; the five refusals above are the plainest possible
confirmation of ADR-0198 §2 on real data.

## 8. Determinism

`build` run twice on each imported project produces a byte-identical
`textures/hires.txt`:

| Project | manifest sha256 (stable across rebuilds) |
|---|---|
| Ninja Gaiden | `0ecb29faa82f855ef40465b338deff81389a0587459a4936cc9f1d600b2c603e` |
| Contra80s | `60b0912b950f8d728947e3312f207bfe5870d51a38bdff502adf464ff779c551` |
| Super Mario Bros. | `2f8aa97716892f28e285c0ac85eafe6002f0f82460b0f59e7e0f87c7a7321a3f` |

Both `<ver>103`-rewriting imports (Ninja Gaiden) and verbatim ones (Contra80s)
reproduce byte for byte, and re-running the import itself reproduces the same
project (the same two hashes came from the API run and the CLI run).

## 9. Gaps

- The pixel half of the stop rule holds on Ninja Gaiden and Bomberman — the
  two packs whose inputs never name one pattern at two crops — and on no other
  pack measured: Contra80s (10.2 % of keys) and Super Mario Bros. (40.7 %) draw
  those keys from the pair's chosen crop, per §6. That is the honest state of
  F12.7 as implemented: the rule set and the carried lines round-trip exactly
  everywhere, the pixels do not on a pack that keys one pattern at several
  crops.
- Nothing was rendered in the emulator: this log compares manifests and pixel
  blocks, not a `headless_record` frame of an imported pack.
- `scripts/mep_import.py` is not listed in `scripts/tools-zip-manifest.txt`
  (that manifest is the import closure of the two authoring guides, and this
  slice's row does not extend it), so the release tools zip does not ship it
  yet. One entry in `scripts/check_tools_zip_closure.py` plus the manifest, and
  a paragraph in `docs/hd-pack-authoring.md`, would wire it — deliberately
  left out of this slice.
- Audio-only packs (1942) and `<patch>` packs (5 installed) stay unimportable
  by decision: ADR-0198 §2/§3 for the latter, and no project shape for the
  former until `mep_build` accepts a key source without tile keys.
- The PRD row and Part A §4 were not edited; only ADR-0198's Status line
  records that §1 is implemented.
