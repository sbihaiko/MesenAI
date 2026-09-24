# Issue #413 — a kit figure's paint now reaches the running game by reload (2026-09-24)

Scope: issue #413, found by F14.1
(`docs/validation/f14.1-painted-round-trip-2026-09-23.md` §5). The issue:
importing a kit figure (`kit/figures/usrNNN-figure.png`, or
`mep_figure.py export … poseNNN` on a kit project) re-pointed keys in
`hires.txt`. After that, *Reload Repainted Images* (F12.3, ADR-0212)
re-decoded nothing, and the ROM had to be reopened.
`docs/remastering-a-game.md` §4 promises import → build → reload.

This log records five things: the reproduction on a fresh binary, the root
cause, the fix, the unit tests and their mutation check, and F14.1 path (b)
repeated with the kit figure `usr003-figure.png`.

## 1. Binary

- Worktree on `main` `56d49588`, with no object files before this build.
  `make -j8 capture-tool` (which builds `core` first) ran with the
  CommandLineTools `make` and `clang++`, with `SDKROOT` set to the CLT SDK.
  This build compiled all 212 `Core/**/*.o`.
- `InteropDLL/obj.osx-arm64/MesenCore.dylib` sha256
  `25e3c6f97ae475636d88e812b2e875c37bc11b9081c2180146695a38c9e9ba1a`.
- `scripts/headless_record` sha256
  `76e3d4df6606e99ec01c5dec7ef9cf0cc6aa117306e9574fa6732385f6dd886f`.
  `otool -L` resolves `MesenCore.dylib` to this worktree's absolute path.
- The dylib carries the ADR-0212 reload: `strings` finds the
  `tile rule(s) re-cut` literal, and every reload run below prints it.

The fix is Python only (`scripts/mep_figure.py`), so the same binary serves
the runs before and after the fix.

## 2. Bounded input (the F14.1 recipe, re-run)

Same ROM, same scripts and same commands as F14.1 §2–§3: the ROM is
`Contra (1988) (Konami).nes` with sha1 `c9ea66bb…319b`, and the helpers are
the F14.1 scratch helpers, `record.sh`, `mkproj.sh`, `play.sh`,
`paint_figure.py`, `compare.py` and `probe_edited.py`. Nothing below is
versioned. States, recordings, packs and captures live in the session
scratchpad.

```sh
scripts/headless_record <mint>/Contra.nes 30 <mint>/out \
  input=scripts/stages/contra/mint-stage1.txt save-state=<s>/stage1.mss
#   -> capture finished: 1804 frames, result: ok
scripts/headless_record <rec>/Contra.nes 61 <rec>/out bootstrap hdpack-off \
  input=scripts/stages/contra/stage1-probe.txt state=<s>/stage1.mss
#   -> capture finished: 5471 frames, result: ok
python3 scripts/artist_kit.py <rec>/Contra/auto --out <kit> --verify
#   -> 172 after a control rebuild, 172 with the kit; 0 lost, 0 invented: PASS
```

The run is deterministic: the minted `stage1.mss` (`be50a81b…345afd`), the
recording's `textures/hires.txt` (`6f8e6c4e…a52a`) and the control kit
project's built `hires.txt` (`c663fcca…90fb`) are byte-identical to F14.1's.

**Control project `proj-ctrl`**: a copy of the recording with the kit's
`sheets/*.png` and `*.json` dropped in and `mep_build.py build` run.
Result: 0 errors, 17 images, 493 tiles. The torso key
`5E5E5D733D1F0F0F6161634E23100C0F` / `FF36160F` is drawn from `usr003.png`
at `(4, 40)` by three rules (`[spr011_n2]`, `[spr013_n2]`, plain).

**The paint.** The F14.1 stroke is drawn inside the figure cell whose sheet
cell carries the torso key: RGBA `(32, 224, 96, 255)`, a 14x11 rectangle plus
an 11-pixel diagonal, 165 pixels in all. The figure cell is node 37, sidecar
`sprites.json` index 37, at figure pixels `(8, 32)`.

## 3. Reproduced before the fix

On the unmodified `main` code, `kit/figures/usr003-figure.*` was copied,
painted as above, and run through import and build:

```
usr003-figure: 50 cells, 1 painted, 1 written, 0 already on the sheet
  wrote …/proj-before/textures/sheets/sprites.png
verify: build errors 0, keys 172 -> 172, 0 lost, 0 added: PASS
```

`mep_build.py build`, then the per-`<img>` rule count:
`sheets/sprites.png` becomes image 14 (plain 1, conditioned 2), and
`sheets/usr003.png` drops from plain 29 / conditioned 52 to 28 / 50. There
are now 18 `<img>` lines instead of 17, and `hires.txt` changed
(sha256 `fbdd9ca0…4581`).

| run | pack at load | reload | frame-5470 checksum |
|---|---|---|---|
| A — control | `proj-ctrl` | none | `0xAB9EB665` |
| C-before — painted from the start | `proj-before` | none | `0x06DC9F9A` |
| B-before — under test | `proj-ctrl`, `sprites.png` replaced by `proj-before`'s | frame 3000 | `0xAB9EB665` |

B-before's log: `[MEP] reload: 0 re-decoded, 0 refused, 0 failed, of 17
image(s), 0 tile rule(s) re-cut`. The running manifest never loaded
`sprites.png`. This is exactly the issue.

## 4. Root cause

- The figure sidecar records the sheet each cell was *cut from*.
  `mep_figure.home_cell` resolves a pose member (every kit figure and every
  `poseNNN` export) through `Pack.sprite_home`, which only looks at the
  `sprites` vocabulary (`compose_engine._SPRITE_ART_KINDS`). So every cell
  maps to `sprites.json`.
- `import_figure` wrote each painted cell into exactly that sheet.
- In the built kit project, the key is owned by the **untouched** `usr003`
  row. Its kind `sprite` (rank 4) outranks `sprites` (rank 1) in
  `mep_build._SHEET_RANK`. Once `sprites.png`'s cell was painted,
  ADR-0153 §4's "a painted cell always beats an untouched one" handed the
  key to `sprites.png` at the next build. That is a manifest change, which
  ADR-0212 excludes from the in-place reload by design.
- A `sprNNN` figure whose own sheet already owns the key (F14.1 path (b))
  never hit this, because source and owner were the same crop.

## 5. The fix (`scripts/mep_figure.py`)

**Where the paint goes.** `import` now writes each painted cell where the
pack's *built* manifest already draws its key from:

- `manifest_owners` runs `mep_build.py build` on a throwaway copy of the pack
  as it is. It runs only once a painted cell is found, so an unpainted import
  builds nothing. It maps each canonical `(tileData, palette)` to the
  `sheets/` crops its `<tile>` rules point at.
- `plan_targets` takes every key the source cell emits (its own `tiles[]` and
  its aliases' tiles, keyed the way the build keys them: CHR index per
  ADR-0172, unflipped `source` per ADR-0178). There are two cases:
  - If the source crop owns them, the source is written, as before.
  - Otherwise the 8x8 sub-tile is copied onto each owner crop, and the source
    cell is left alone.

**Why the source is not written too.** Writing both makes the source a
painted sprite crop that loses its key, and `mep_build` rejects that as an
error (#253). This was measured on the first attempt: `verify: build errors 1`.

**When the paint is not routed.** An owner whose `*.orig.png` art differs
from the source's is never painted over. In that case, and when a key has no
owner at all, the source is written, so paint is never dropped. The report's
`moves` counter then says the next build re-points a rule, and the CLI prints
a "reopen the ROM" note.

**Other changes:**

- `export` overlays paint that was routed to an owner, so re-exporting the
  figure shows it. It overlays only where the owner carries paint, so an
  unpainted pack exports byte-for-byte as before.
- `verify` now snapshots every sheet PNG, not only the sidecar's, because the
  import may write any of them. It also reports `manifest_unchanged`
  (the two rebuilt `hires.txt` are byte-identical). This check informs and
  does not fail the run.
- The import counts crops that already carried other paint (`overwrote`) and
  prints a note. A figure and its `usr*` row are the same tiles. Painting both
  used to stop the build (#399); now the figure's paint replaces the row's.

No accepted ADR changes. This corrects ADR-0209 Q3's return target (amended
in its Status line). Precedence (ADR-0153 §4), #253 and ADR-0212 are
untouched.

## 6. Unit tests and mutation check

Two new cases were added to `scripts/test_mep_figure.py`:

- `test_a_figure_cut_from_the_vocabulary_lands_on_the_sheet_that_owns_the_key`:
  a synthetic pack, built first. A `pose000` figure maps every cell to
  `sprites.json`, while the untouched `spr000` group owns the keys. It asserts
  that painting node 1 writes only `spr000.png`, inside that cell;
  `sprites.png` is untouched; the rebuilt `hires.txt` is byte-identical to the
  build before the import; a second import is a no-op; a re-export shows the
  paint; and `verify` reports `manifest_unchanged`.
- `test_an_owner_with_other_art_is_skipped_and_the_move_is_reported`: the
  owner's twin art differs, so the source is written, `moves == 1`, and
  `verify` reports the manifest change.

In `scripts/test_pose_pixel_offsets.py`, the overlap-ownership case (ADR-0225
§3) now drops the synthetic `spr000` group first. That case tests the overlap
rule, not the target, and it had asserted the pre-fix target (`sprites.png`
while `spr000` owned the keys). The ARTIST.md wording check in
`scripts/test_artist_kit_assemble.py` follows the new text.

Each mutation of `mep_figure.py` below makes the suite fail, and the restored
file passes:

| mutation | failing checks |
|---|---|
| M1 import ignores the owner (the pre-fix target) | 6, including "written into spr000.png", "sprites.png is untouched" and "hires.txt is byte-identical" |
| M2 export ignores routed paint | 1: "a re-export shows the paint" |
| M3 `verify` always reports the manifest unchanged | 1: "verify reports the manifest change" |
| M4 no twin check before routing | 2 |
| M5 the source is written as well as the owner | 5, including "the repainted pack builds" (#253) |

Suites run, each on its own and never while compiling:
`test_mep_figure.py` (all ok), `test_pose_pixel_offsets.py` (all ok),
`test_artist_kit_assemble.py` (16/16), `test_artist_kit.py` (15/15),
`test_ora_writer.py` (0 failed checks), `test_sidecar_labels.py` (all ok),
`test_compose_engine.py` (37/37).

## 7. End to end after the fix: F14.1 path (b) with `usr003-figure.png`

The same copy, paint, import and build as §3:

```
usr003-figure: 50 cells, 1 painted, 1 written, 0 already on the sheet
  1 painted cell(s) written where the built hires.txt draws their key from; 1 of their source cell(s) left as they were, so no rule moves
  wrote …/proj-after/textures/sheets/usr003.png
verify: build errors 0, keys 172 -> 172, 0 lost, 0 added: PASS
  hires.txt unchanged by this import: Reload Repainted Images shows it
```

- The build reports 0 errors, 17 images and 493 tiles, with lint included.
  **`textures/hires.txt` is byte-identical to `proj-ctrl`'s**
  (`c663fcca…90fb`).
- The only file that differs is `sheets/usr003.png`, by 165 pixels, bbox
  `(9, 47)`–`(34, 69)`.
- `_EditedProbe`: `sprites.png` 0 of 142 cells edited, `usr000`–`usr002`
  0 edited, `usr003.png` 1 edited (`[3]`) with 49 unchanged.

| run | pack at load | reload | frame-5470 checksum |
|---|---|---|---|
| A — control | `proj-ctrl` | none | `0xAB9EB665` |
| C-after — painted from the start | `proj-after` | none | `0x06DC9F9A` |
| **B-after — under test** | `proj-ctrl`, `usr003.png` replaced by `proj-after`'s | frame 3000 | **`0x06DC9F9A`** |

B-after's log: `[MEP] reload: sheets/usr003.png re-decoded` / `1 re-decoded,
0 refused, 0 failed, of 17 image(s), 81 tile rule(s) re-cut, in 1 ms`.

Numbers from `compare.py`, with the painted sheet `usr003.png` cell at `(4, 40)`
and its screen position `(484, 364)`:

| measure | value |
|---|---|
| sheet pixels painted | 165, bbox `(9, 47)`–`(34, 69)` |
| expected frame pixels | 165, bbox `(489, 371)`–`(514, 393)` |
| control vs reference: pixels differing | **165**, bbox `(489, 371)`–`(514, 393)` |
| control-vs-reference diff **equal** to the painted set | **true** |
| reference: painted pixels carrying the stroke colour | 165 / 165 |
| reload vs reference: pixels differing (whole frame) | **0** |
| reload: painted pixels carrying the stroke colour | 165 / 165 |
| reload: opaque pixels of the whole painted cell matching the frame | 758 / 758 |

The checksums are F14.1's own. The reference `0x06DC9F9A` is the frame F14.1
path (a) produced by painting `usr003.ora` directly.

**The `poseNNN` export on the kit project.** `mep_figure.py export
<proj-pose> pose001` gives 11 cells, the same torso stroke, and then
`import --verify`:
`1 painted, 1 written` into `usr003.png`, `172 -> 172, 0 lost, 0 added:
PASS`, and `hires.txt` unchanged. The rebuilt `hires.txt` is byte-identical
to `proj-ctrl`'s. `usr003.png` is byte-identical to the one the
`usr003-figure` path wrote. A second import reports `0 written, 1 already on
the sheet`. A fresh `export … pose001` after the import matches the painted
figure pixel for pixel (0 of 104x160 differ).

**Regression, `sprNNN` path.** F14.1 path (b) as it was: `spr013` on the
rebuilt recording, where `spr013` owns the key. The import still writes
`spr013.png`, `verify` passes with `hires.txt` unchanged, and the rebuilt
`hires.txt` is byte-identical to the unpainted build.

## 8. Verdict

| # | Stop condition | Result |
|---|---|---|
| 1 | Reproduce on a freshly built binary | **met**: §3, dylib `25e3c6f9…a1a`, reload `0 re-decoded … of 17`, B = control `0xAB9EB665` |
| 2 | Reload frame identical to painted-from-the-start | **met**: B-after = C-after = `0x06DC9F9A`, 0 pixels differ |
| 3 | Control differs in exactly the painted pixels | **met**: 165 pixels, equal to the painted set |
| 4 | `hires.txt` byte-identical painted vs unpainted | **met**: `c663fcca…90fb` on both |
| 5 | `import --verify` 0 lost / 0 added | **met**: `172 -> 172, 0 lost, 0 added: PASS` |
| 6 | Unit tests fail before and pass after | **met**: 5 of 5 mutations fail the suite; the restored file passes |
