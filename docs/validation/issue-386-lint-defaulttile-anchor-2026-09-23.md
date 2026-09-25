# Issue #386 — `mep_lint` honours the `defaultTile=Y` palette wildcard (2026-09-23)

Validation record for the fix to
[#386](https://github.com/sbihaiko/MesenAI/issues/386): after the #382 fix
(`issue-382-lint-index-by-value-2026-09-23.md`), the installed Metroid (USA)
pack still drew 460 "`<addition>` anchor X/P is keyed by no `<tile>` rule"
errors on 6 anchors whose CHR index is keyed by a `defaultTile=Y` `<tile>`
rule under a different palette.

## Root cause

The lint's key set (`keyed` in `scripts/mep_lint.py`) held each `<tile>`
rule's exact `(tileData, palette)` only. The runtime does more for a
`defaultTile=Y` rule:

- `HdPackLoader::InitializeHdPack` (`Core/NES/HdPacks/HdPackLoader.cpp`) files
  every tile under `GetKey(false)` and, when `DefaultTile` is set, also under
  `GetKey(true)` — the same tileData with `PaletteColors = 0xFFFFFFFF`
  (`HdTileKey::GetKey`, `Core/NES/HdPacks/HdData.h`). The stacked-pack merge
  in the same file does the same.
- `HdNesPack::GetMatchingTile` (`Core/NES/HdPacks/HdNesPack.cpp`) looks up the
  exact key first and falls back to `tile->GetKey(true)`, so a
  `defaultTile=Y` rule draws its index (or CHR RAM pattern) under any palette.
- `HdNesPack::BuildAdditionalTileCache` fires an `<addition>` on
  `tile == additionalSprite.OriginalTile` — the on-screen sprite key, not a
  `<tile>` rule — and `InsertAdditionalSprite` pushes the target as a sprite
  that is then drawn through the same `GetMatchingTile`.

So an anchor (or target) whose tileData only a `defaultTile=Y` rule keys under
another palette does fire and does have art.

## Fix

- `scripts/mep_addition.py`: `DEFAULT_KEY_PALETTE = "FFFFFFFF"`,
  `default_key(key)` and `is_keyed(key, keyed)` — exact key, or the default
  key, mirroring `GetMatchingTile`'s two lookups.
- `scripts/mep_lint.py`: a `<tile>` rule whose seventh field is a loader
  "true" spelling (`HDPACK_BOOL_TRUE`, as `ParseBooleanValue`) also adds its
  default key to `keyed`, built from #382's canonical key, so index padding
  still compares by value. The anchor **and** target checks use `is_keyed` —
  both go through `GetMatchingTile` at runtime. The ADR-0196 §3 "past the
  pack's own CHR" floor skips default keys (each one has its exact twin in
  the set, which is what the sidecar's synthetic marking names).
- A `defaultTile=N` rule still keys its exact palette only, so the palette
  half of the anchor check keeps biting for non-default rules.

`mep_import.py` and `mep_build.py` have no key-set check of their own for
anchors/targets, so nothing else changes.

## Measurement — Metroid (USA), community pack #148

Installed pack: `~/Library/Application Support/MesenCE/HdPacks/Metroid (USA)`
(`<ver>108`, 1 987 `<addition>` lines), linted via
`mep_lint.lint_nes_hires` before (commit `295e6395`, the #382 fix) and after.

| Manifest | Errors before | Errors after | anchor unkeyed | target unkeyed | ignorePalette | §3 range | other |
|---|---|---|---|---|---|---|---|
| Installed pack, as shipped | 3 403 | **2 942** | 460 → 0 | 1 → 0 | 954 → 954 | 1 987 → 1 987 | 1 → 1 |
| Same, `<tile>` tokens in build form | 3 403 | **2 942** | 460 → 0 | 1 → 0 | 954 → 954 | 1 987 → 1 987 | 1 → 1 |

Warnings are unchanged by the fix (13 422 on the shipped pack). The
build-form copy was regenerated for this record (every unconditioned and
conditioned short `<tile>` index rewritten to `index_token` form, 150 199
tokens, 13 472 warnings), so its token and warning counts differ slightly from
the #382 record's reproduction; its error counts match it.

- The 460 anchor errors (6 anchors: `1066/0F162700`, `1072/20021600`,
  `1072/0F201600`, `1AE1/FF161927`, `1AFA/FF161927`, `12E/FF161926`) are gone;
  each is keyed by a `defaultTile=Y` rule on its index, e.g. `hires.txt:7930`
  `<tile>0,1066,0F162000,0,0,1,Y`.
- The one target error the #382 record called genuine (`006/FF001431`,
  `hires.txt:158896`) also clears: index `06` is keyed by the `defaultTile=Y`
  rules at `hires.txt:1074`/`1091`, so `GetMatchingTile` does find art for it.
  Those rules are conditioned (`FirstTitleScreen` and two negated
  conditions), so the art only draws when they hold. The lint has never
  weighed conditions when deciding "keyed" (a conditioned exact rule has
  always counted, per ADR-0196 §4's "an `<addition>` cites a key"); this fix
  keeps that rule rather than special-casing the wildcard.
- Real ADR-0196 findings are intact: 954 `ignorePalette` errors and 1 987 §3
  range errors unchanged.

## Tests

`scripts/test_mep_lint_addition.py`: 28 → 36 checks — an anchor keyed only by
a `defaultTile=Y` rule under another palette lints clean (also with a `yes`
spelling and a condition prefix); a `defaultTile=N` rule under another palette
and a `defaultTile=Y` rule on another index still raise the anchor error;
padded/unpadded index (`012E`/`12E`, `12E`/`0012E`) combined with the
wildcard; a target keyed only by a `defaultTile=Y` rule has art, and a
`defaultTile=Y` synthetic target does not move the §3 floor. Five of the new
checks fail against `295e6395`'s `mep_lint.py`.
`scripts/test_mep_addition.py`: 73 → 78 checks (`default_key`, `is_keyed`).
`test_mep_import.py` (80), `test_mep_build.py` (104),
`test_mep_lint_border.py` (32), `test_mep_lint_caps.py` (3) and
`test_mep_lint_behind_bg_sprites.py` (6) unchanged and passing.
