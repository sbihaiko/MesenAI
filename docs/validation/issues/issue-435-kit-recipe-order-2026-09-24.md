# Issue #435: the kit recipe imported figures before any build

**Date:** 2026-09-24
**Base:** `main` @ `755ba3da`, in a fresh worktree.
**Scope:** `scripts/mep_figure.py` (import guard), `scripts/artist_kit_assemble.py`
(the ARTIST.md "When you are done" recipe), `docs/remastering-a-game.md`, and
their tests. No Core or UI code changed.
**Consistent with:** ADR-0178 (the build un-bakes flip-baked crops in place),
ADR-0212 (the reload re-decodes images, so it needs an unchanged manifest),
ADR-0209 Q3 (h) / ADR-0213 §4 (explicit re-import, no hidden actions) and
ADR-0183 §4 (the round trip keeps the key set). No new decision: it is a fix
under those ADRs.

## Root cause

- `import` plans each painted cell against the manifest a throwaway build of
  the pack makes (#413, `manifest_owners`). That part was right: the throwaway
  owners matched the owners of a real first build key for key (172 of 172 on
  the Contra repro).
- What was stale was the *pack* the plan reads. The first `mep_build.py build`
  of a copy that holds the kit sheets rewrites some of them in place: it
  un-bakes the crops the recorder stored with the OAM flip baked in and drops
  their `source`/`mirror` sidecar fields (ADR-0178, #255). On the repro it
  rewrote `usr001`-`usr003` (`.png`, `.orig.png`, `.json`).
- Before that build, the owner crop's twin in `usr001` was still mirrored, so
  `plan_targets` saw it as "other art", skipped it (never paint over a
  different drawing), and wrote the vocabulary cell in `sprites.png` instead.
  The build then made that painted crop the owner of 21 keys: 55 `<tile>`
  rules moved, so *Reload Repainted Images* could not show the paint.
- The recipe ran exactly that order: copy, import, build.

## Fix

Both options the issue named, in one change:

1. **`import` refuses a pack that was never built with its sheets.**
   `probe_build` (the throwaway build `manifest_owners` already ran) now also
   compares the copy's `textures/sheets/*.png|*.json` before and after that
   build. If the build rewrote any of them, the first painted cell raises
   `FigureError` before anything is written. The message names the files and
   the build to run first, and the CLI exits 2. On a pack that has been built,
   the second build is idempotent (#255), so the guard does not fire. An
   unpainted import runs no build, so it never refuses.
2. **The recipe builds once before the imports.** When the kit has figures,
   `_recorded_done_steps` emits `python3 scripts/mep_build.py build
   <game>/painted &&` after the copies and before the `sh -c` import loop. The
   ARTIST.md prose and `docs/remastering-a-game.md` §5 say why.

A refusal was chosen over having `import` build the pack itself, for two
reasons. An import that rewrites `hires.txt` is a hidden action, and the
explicit return path of ADR-0209 (h) / ADR-0213 §4 does not do those. And
`--verify` reads the on-disk `hires.txt` as the recording baseline
(`drift_from_recording`), which a self-build would silently replace.

## Tests (TDD)

- **RED:** `test_an_import_before_the_first_build_is_refused_and_the_recipe_holds_the_manifest`
  in `scripts/test_mep_figure.py`. The synthetic pack's `spr000` group cell
  for node 1 is flip-baked the way the recorder writes an H-flipped sprite:
  mirrored pixels in the sheet and twin, `tile` = H mirror, plus `source` and
  `mirror: H`. The vocabulary cell holds the unflipped art. The test paints
  the pose figure and imports before any build. On `main`, 6 checks failed:
  the import wrote `sprites.png` and was not refused, and after the build
  `hires.txt` differed from the unpainted control.
- **GREEN:** 9/9. The import is refused and writes nothing, and the CLI exits
  2. After one build, the same import routes the paint to `spr000.png` with
  `moves == 0`, and the rebuilt `hires.txt` is byte-identical to the
  unpainted control.
- **Recipe order:** `test_the_done_steps_import_painted_figures_between_copy_and_build`
  in `scripts/test_artist_kit_assemble.py` now asserts copy < build < import <
  build. It also asserts that the first build ends in `&&` and that the
  section explains #435. It failed on `main` with 3 checks and passes now
  (16/16 cases).
- **Mutation proof** (each mutant applied to `mep_figure.py`, RED test re-run,
  file restored byte-for-byte):

  | mutant | result |
  |---|---|
  | guard removed (`if rewritten:` -> `if False:`) | killed, 6 checks fail |
  | probe blind to rewrites (only a deleted file counts) | killed, 6 checks fail |
  | snapshot empty (no sheet file compared) | killed, 6 checks fail |

## E2E: the issue's Contra repro

Input: the F12.19 re-run material (Contra stage 1 recording `rec/Contra/auto`,
kit, and `kit-painted` with every opaque pixel of `figures/usr000-figure.png`
magenta). Scripts are in the session scratchpad (`fix435/e2e435.sh`,
`fix435/e2e435-after.sh`).

| run | outcome |
|---|---|
| `main`, recipe order (copy -> import -> build), painted | `sprites.png` + `usr003.png` written; "21 painted cell(s) will re-point a key"; `hires.txt` differs from unpainted control by 114 diff lines (55 rules per the issue's tile-only count); 66 rules / 26 keys magenta |
| `main`, build first, painted | `usr001.png` + `usr003.png`; "26 ... no rule moves"; `hires.txt` byte-identical; 66 / 26 magenta |
| fix, pre-fix recipe order, painted | import refused ("`mep_build.py build` still rewrites usr001.json, usr001.orig.png, usr001.png, usr002.json and 5 more"), loop exit 1, every file under `textures/` unchanged (sha list identical) |
| fix, new recipe (copy -> build -> import -> build), unpainted | 0 errors; `hires.txt` byte-identical to the pre-fix unpainted control; 0 magenta rules |
| fix, new recipe, painted | 26 cells routed, "no rule moves"; **`hires.txt` diff vs unpainted control = 0** (byte-identical); **66 rules / 26 distinct keys draw magenta** |

The paint still reaches the pack (#399 holds). With the manifest unchanged,
the reload only has to re-decode `usr001.png` and `usr003.png` (ADR-0212 §1).
The live reload itself was not clicked in the GUI for this log.
