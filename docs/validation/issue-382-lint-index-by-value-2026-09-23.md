# Issue #382 — `mep_lint` compares `<addition>` keys by value (2026-09-23)

Validation record for the fix to
[#382](https://github.com/sbihaiko/MesenAI/issues/382): `mep_lint.py` compared
the anchor/target tokens of an `<addition>` line against the `<tile>` keys as
text, so a legacy pack's `000` never matched the `00` that `mep_build` re-emits
for the same CHR index, and the ADR-0196 §4 checks reported thousands of
"keyed by no `<tile>` rule" errors on keys the loader treats as one.

## Root cause

`HdPackLoader::ReadTileData` (`Core/NES/HdPacks/HdPackLoader.cpp`) builds one
`HdTileKey` from the text of either tag:

- 32 or more characters: CHR RAM pattern — 16 byte pairs, `FromHex` each;
- fewer: CHR ROM index — `std::stoi` (decimal) at `<ver>` ≤ 102,
  `HexUtilities::FromHex` at 103+;
- palette: `HexUtilities::FromHex`.

So `000`, `00` and `0` are the same key at `<ver>103+`, and so are `217` and
`0217`. The lint kept the author's text on both sides (`scripts/mep_lint.py`
`keyed`, `mep_addition.parse_addition`), and `mep_build` writes the `<tile>`
side in `mep_addition.index_token`'s shortest-even-width form while the
`<addition>` lines it carries keep the legacy spelling.

## Fix

- `scripts/mep_addition.py`: one shared helper, `canonical_key((data, pal),
  version)`, plus `parse_index(token, version)` and `HEX_INDEX_VERSION = 103`.
  It re-spells a CHR index through the loader's own version rule and
  `index_token`; keeps a pattern as its first 32 uppercased hex digits; pads
  the palette to 8 digits. Text no reading accepts comes back stripped and
  uppercased, so the caller's own diagnostic still names it.
- `scripts/mep_lint.py`: the `<tile>` key set (`keyed`), the duplicate-`<tile>`
  key, the sidecar synthetic keys and the `<addition>` anchor/target all go
  through `canonical_key`. Messages keep the author's spelling.
- `scripts/mep_import.py`: its two private index parsers and `_HEX_INDEX_VER`
  now delegate to the same helper, so import and lint cannot disagree on an
  index.

## Measurement — Metroid (USA), community pack #148

The F12.17 patched-ROM import (PR #385) is not on `main`, and `mep_import.py
import` refuses this pack (`<patch>`, ADR-0198 §2), so the post-build state was
reproduced directly: a copy of the installed pack
(`~/Library/Application Support/MesenCE/HdPacks/Metroid (USA)`, `<ver>108`,
1 987 `<addition>` lines) with every `<tile>` index token rewritten to
`index_token` form (143 059 tokens), leaving the `<addition>` lines as the
pack wrote them — exactly what `mep_import` + `mep_build build` produce.

| Manifest | Errors before | Errors after | anchor unkeyed | target unkeyed | ignorePalette | §3 range | other |
|---|---|---|---|---|---|---|---|
| Installed pack, as shipped | 3 403 | 3 403 | 460 → 460 | 1 → 1 | 954 → 954 | 1 987 → 1 987 | 1 → 1 |
| Same, `<tile>` tokens in build form | **6 396** | **3 403** | 1 467 → 460 | 1 987 → 1 | 954 → 954 | 1 987 → 1 987 | 1 → 1 |

Warnings: 13 422 on both, before and after. The duplicate-`<tile>` count rose
from 1 483 to 1 484 on the shipped pack: one pair of rules spelt `00`/`000`
with the same palette and conditions is one key to the loader, and is now
reported as such.

- **2 993 false errors removed** (1 007 anchors + 1 986 targets) on the
  build-form manifest, which now lints identically to the shipped pack.
- **Real ADR-0196 findings are intact**: the 954 `ignorePalette` errors and
  the 1 987 §3 range errors (`target index 000 is not past the pack's own
  CHR`) are unchanged — the pack's overflow sprites target real CHR index 0,
  which is the collision §3 forbids.
- The 1 surviving target error (`006/FF001431`, line 158 896) is genuine: the
  only `<tile>` rules for that key are commented out.
- The 460 surviving anchor errors are 6 distinct anchors, and each one's index
  is keyed by a `defaultTile=Y` `<tile>` rule under another palette. The
  runtime matches an anchor against the on-screen sprite key
  (`HdNesPack::BuildAdditionalTileCache`), not against `<tile>` rules, so these
  are a second, separate false-positive class (palette wildcard) — out of
  scope for #382 and filed as
  [#386](https://github.com/sbihaiko/MesenAI/issues/386).

## Tests

`scripts/test_mep_addition.py`: 59 → 73 checks (`parse_index`,
`canonical_key`). `scripts/test_mep_lint_addition.py`: 17 → 28 checks —
`000`/`00` and `217`/`0217` in both directions, a 7-digit palette, a 32-hex
CHR RAM pattern never re-spelt as an index (and compared case-insensitively),
the decimal rule below `<ver>103` on the helper and on the duplicate-`<tile>`
pass, and a genuinely unkeyed anchor/target still reported in the author's
spelling. `test_mep_import.py` (80), `test_mep_build.py` (104),
`test_mep_lint_border.py` (32), `test_mep_lint_caps.py` (3) and
`test_mep_lint_behind_bg_sprites.py` (6) unchanged and passing.
