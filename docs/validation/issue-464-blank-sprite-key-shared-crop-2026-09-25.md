# Issue #464 — a blank sprite key no longer picks up art through a shared kit crop (2026-09-25)

Validation record for the fix to
[#464](https://github.com/sbihaiko/MesenAI/issues/464), found while fixing
#452. The evidence is in
`docs/validation/issue-452-453-figure-import-blank-tiles-and-recipe-order-2026-09-24.md`,
"Follow-ups observed".

## Root cause

The artist kit's composed sprite sheets place each pose's tiles at their
on-screen positions, so two cells can share one rect. Castlevania's
`usr017.json` places the blank sprite tile (`0000…/FF23340F`, cell 1) and a
spark tile (`000044EE…/FF16250F`, cell 2) at the same 1x position (1, 10).
The #452 whole-figure paint writes the spark's crop, which is correct.

`mep_build` asks "was this cell painted?" per cell rect, by comparing it with
the `*.orig.png` twin (ADR-0153 §4). The blank tile's rect is the spark's
rect, so the blank key's cell also read as painted. Painted beats untouched,
so the blank key's 22 rules moved from the untouched `hud.png` crop (4, 36) to
the painted `usr017.png` crop (4, 40), which holds 480 magenta pixels. The
build passed, but a tile the NES draws as fully transparent now drew art.

## Fix

- `mep_addition.is_blank_sprite(tile_data, palette)` is true for 32 zero hex
  digits under a sprite palette key (first byte `FF`, which `HdBuilderPpu` sets
  on every sprite palette). A background tile with the same data is not blank,
  because its colour 0 is the backdrop, which the NES draws.
- In `mep_build.py`, a blank sprite crop never claims its key by paint. Its
  entry carries `claim = False` even when its cell differs from the twin, so
  it never joins the muted-paint reports (#343, #253).
- Each entry also carries whether its cell is unpainted. That flag is a new
  tie-break, after "painted" and before the kind rank. For every non-blank
  entry it is just "not painted", so the order does not change. For a blank
  key it prefers a crop whose cell nobody painted over one painted for
  another key, both across sheets and between repeats on one sheet. An entry
  with no twin (a legacy 16-column sheet, a blind sheet) counts as painted
  there too, so it keeps its old place.
- ADR-0153 gains a dated §4 amendment. PR #472 (#447, not merged when
  this was written) re-emits the recorded rule for an untouched cell. With
  this fix a blank key is always untouched, so once #472 lands it also keeps
  its recorded rule. #472 must read the untouched flag from the same entry
  field (`entry[3]`, now `claim`) for that to hold.

`mep_build.py` is at its 2070-line ceiling (ADR-0137). The predicate lives in
`mep_addition`, and the ceiling was not raised.

## Red before the fix

`python3 scripts/test_mep_build_blank_key.py`, run against `origin/main`'s
`mep_build.py` and `mep_addition.py`:

```
PASS: #464: the build with a painted spark sharing the blank tile's crop passes
FAIL: #464: the blank sprite key keeps its hud.png crop instead of the spark's painted usr017 crop: blank key -> usr017.png, 64 magenta px
PASS: #464: the painted spark still reaches the screen
PASS: #464: the blank key's shared painted crop is not reported as lost paint (#253)
PASS: #464: the build passes when the blank key lives on two sprite sheets
FAIL: #464: among untouched crops, the blank key takes the one whose cell nobody painted: blank key -> usr017.png, 64 magenta px
PASS: #464: the build passes with the blank key repeated on one sheet
FAIL: #464: on one sheet, the blank key's unpainted repeat wins over the shared painted crop: blank key -> ('sheets/usr017.png', 1, 1)
PASS: guard: the build passes
PASS: guard: a painted non-blank sprite cell still beats the untouched hud.png cell
PASS: guard: a painted all-zero background tile still claims its key (the backdrop is drawn)
PASS: guard: the build with a legacy sheet passes
PASS: guard: a painted ADR-0153 cell still beats the legacy 16-column sheet
3 failure(s)
```

The PASS lines are guards that must hold both before and after the fix.
After the fix: 13/13 pass.

## Mutations

| Mutation | Result |
|---|---|
| `is_blank_sprite` returns `False` (no blank rule) | the 3 red checks fail again |
| `is_blank_sprite` ignores the palette (background tiles count as blank) | "a painted all-zero background tile still claims its key" fails |
| no "unpainted cell first" tie-break across sheets | "among untouched crops, the blank key takes the one whose cell nobody painted" fails |
| a twin-less entry counts as unpainted (`else True`) | "the build with a legacy sheet passes" fails: the painted sprite cell loses to the legacy sheet and trips #253 |
| the same-sheet repeat keeps the old "painted beats untouched" test | "on one sheet, the blank key's unpainted repeat wins" fails |

Each mutation was reverted, and the suite is green again.

## E2E: Castlevania, whole-figure paint

This reruns the #452 recipe: `runs/p1` (80 s of stage 1) and its kit, the
"When you are done" steps (copy, build, import every figure, build twice),
with `figures/usr002-figure.png` painted solid `#FF00FF` (70 960 px). The
pre-fix arm is a copy of `scripts/` with `origin/main`'s `mep_build.py` and
`mep_addition.py`. `origin/main` already has the #452 import fix.

| | Pre-fix | Fixed | Control (nothing painted) |
|---|---|---|---|
| build 1 / 2 / 3 | 0 / 0 / 0 errors, build 3 byte-identical | 0 / 0 / 0 errors, build 3 byte-identical | 0 / 0 / 0, byte-identical |
| blank key `0000…/FF23340F` | 22 rules → `usr017.png` (4, 40), **480 magenta px** | 22 rules → `hud.png` (4, 36), **0 magenta px** | 22 rules → `hud.png` (4, 36) |
| rules whose crop holds `#FF00FF` | 98 rules, 33 keys | 76 rules, 32 keys | 0 |
| rules, key set | 782 | 782, same key set | 782 |

Between the pre-fix and fixed builds, exactly 22 rules move, all of them the
blank key (`usr017.png` (4, 40) → `hud.png` (4, 36)). Every other rule is
unchanged. The blank key's rules now match the control.

## Suites

- `test_mep_build_blank_key.py`: 13/13. It is wired into `make doc-checks`,
  and `make python-tests` picks it up.
- `test_mep_build.py`: all checks pass (124 PASS lines).
- `make python-tests`: 60 passed, 0 failed, 0 skipped. `make doc-checks`: exit 0.
