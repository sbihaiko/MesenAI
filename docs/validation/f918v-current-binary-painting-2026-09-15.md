# F9.18-V — current-binary painting re-run (2026-09-15)

Follow-up to the C.5 experiment (#253/#255/#256). Goal per the PRD row: re-run
the documented painting workflow on one CHR RAM game and one CHR ROM game
using a named current binary and unmodified documented tools, exercising
shared sheet keys, mirrored cells and a non-matching condition — with
structural, untouched-image and painted-image results recorded separately.

## Binary and inputs

- Rebuilt `capture-tool` at `main` HEAD `04d7fc63` before any measurement
  (the checked-in `scripts/headless_record`/`MesenCore.dylib` predated
  `f68093ec`, the last `Core/` commit).
- `scripts/headless_record` sha256 `511ba70f…7afa927`; `InteropDLL/.../MesenCore.dylib`
  sha256 `9c44d01f…311dcbd`.
- Contra (USA), UNROM/CHR RAM, sha256 `26541a55…5a5519`; golden route
  `scripts/stages/contra/{mint-,}stage1-run.txt`, minted state at frame 3607.
- Mega Man 3 (USA), CHR ROM, sha256 `eddbe571…088752f4675`; golden route
  `scripts/stages/mm3/{mint-,}stage1-run.txt`, minted state at frame 3607.
- Artifacts: `runs/f918v-20260915/{contra,mm3}/` (local, not versioned).

## 1. Structural gate — PASS on both games

| Step | Contra | Mega Man 3 |
|---|---|---|
| `artist_kit.py --verify` | PASS, 2102→343 keys, 0 lost/added | PASS, 9497→391 keys, 0 lost/added |
| `artist_bg_kit.py --verify` | PASS, 343→343, 0 lost/added | PASS, 391→391, 0 lost/added |
| `artist_chr_kit.py --verify` | PASS, 2102→2102 byte-identical | PASS, 9497→9497 byte-identical |
| `artist_kit_assemble.py` | 3 parts, 33 files, 0 errors | 3 parts, 89 files, 0 errors |
| `mep_build.py build` (post-paint) | exit 0, 0 errors | exit 0, 0 errors |
| `mep_lint.py` (post-paint) | exit 0, 0 errors | exit 0, 0 errors |

`artist_map.py` was not run for Contra: its `MESEN_SHEET_GRID_DUMP` file was
not produced by this recording (the route runs to a Game Over at its tail —
see below), and the panorama surface is not one of the three defect areas
this row targets (shared keys / mirror / condition fallback all live in the
sprite kit). Not a gate failure; recorded as a gap.

## 2. Mirror un-bake (#255) — structural evidence of the fix

Contra's `usr000.json` sidecar cell 0 carried `"source"`/`"mirror": "HV"`
before build. After `mep_build.py build`, the same cell's JSON has neither
field and its `tile` string equals the un-flipped source bitmap — the
idempotent un-bake from `fde0d4c7` (#259) reproduced directly, not just
inferred from the changelog.

## 3. Untouched-image / painted-image runtime check

Both packs were installed as `mep/` (documented install path, PR #254) and
replayed from the minted stage-1 state with `hdpack-off` omitted. All loads
were confirmed from the `[MEP] textures: loaded NES HD pack from …/mep/textures`
log line — no diagnosis of `hires.txt` was needed to reach or confirm this.

### First attempt — per-figure guessing, inconclusive

Painted a single low-frequency pose in each game (Contra: a `mirror:HV`
figure seen in only 10 of the capture's frames; Mega Man 3: one of 15
figures aggregated as "seen in 1560 frames", picked by eye). Neither showed
up in the sampled/swept frames — confirmed by an exhaustive per-pixel
magenta scan across a 3–60s screenshot sweep (`runs/f918v-20260915/mm3/sweep/`,
20 samples, 0 hits). Also learned `textures/chr/*.png` is **not** a `build`
input at all (`scripts/mep_build.py:70`: "`build` re-derives the sheets and
nothing else, so a `chr/` … key it was never asked to carry is not a drop"),
so a CHR-page paint is invisible at runtime by design — ruled out as a
shortcut.

### Second attempt — whole-sheet asymmetric paint, confirmed

Repainted **the entire left half of every opaque pixel** of Mega Man 3's
`usr000.png`/`usr001.png` magenta (`runs/f918v-20260915/mm3/painted3/`,
`build`/`mep_lint.py` both 0 errors), guaranteeing whatever figure the
route actually draws would carry the mark. A 12-sample screenshot sweep
(5–60s) found it once, at 35s: the pre-boss **"READY"** banner rendered
fully magenta, matching the edit exactly
(`runs/f918v-20260915/mm3/sweep2/mesen-home/Screenshots/MM3_s35.png`,
sha256 `bab0268b…8feabc1e487d`).

**Pixel-exact isolation (gate 6.3, paint application).** Built a third pack
with `usr000.png`/`usr001.png` restored to their untouched `.orig.png`
reference (upscaled ×4 to the pack's `<scale>`, `runs/f918v-20260915/mm3/unpainted/`,
`build` 0 errors) and replayed the identical ROM/state/input/frame count
(35s) with the same binary and rendering settings
(`.../mesen-home/Screenshots/MM3_004.png`, sha256 `d7f74194…8034db`).
Per-pixel diff against the painted-rebuild screenshot: **2336 differing
pixels, bounding box (432,516)–(583,543) — identical to the painted
region's own bounding box.** Zero pixels differ anywhere outside the edit.
This is exactly gate 6.3's requirement ("pixels outside the declared
affected key instances remain unchanged") on real evidence, not inference.

**Shared-key ownership (#253).** The painted region spans two sheets
(`usr000.png`, `usr001.png`); `build` reported 0 errors and no
`painted_losses` message, and the rendered banner is fully and evenly
magenta with no auto/-supplied fragment showing through — the current
binary's precedence fix resolves this pack's shared keys to the painted
art, not a partial/auto-diluted one.

**Condition fallback (#256), structural.** `textures/hires.txt` around the
painted sheet carries the documented conditional/bare pair for every
gated key, e.g.:
```
[spr000_n5]<tile>10,0B48,FF0F3029,4,4,1,N
<tile>10,0B48,FF0F3029,4,4,1,N
```
Both lines carry byte-identical tile data, sourced from the same painted
crop — so a condition miss on this key falls through to a bare rule that
already carries the paint, not to an unpainted one. This is the structural
half of the fix operating correctly on the actual rebuilt file; isolating a
frame where `spr000_n5`'s condition genuinely fails at runtime (to see the
fallback render live, not just infer it from the file) remains open.

**Mirror (#255).** Structural confirmation stands from §2 (the sidecar's
`source`/`mirror` fields drop after build, idempotently). No runtime
screenshot pairs a live mirrored instance with its un-mirrored twin in this
session.

### Third pass — Contra (CHR RAM side), same technique

Repeated the whole-sheet-half-paint on **all seven** of Contra's sprite
sheets (`usr000.png`–`usr006.png`, restored from `.orig.png` first, since
`usr000` still carried the first attempt's small mark) —
`runs/f918v-20260915/contra/painted-full/`, `build`/`mep_lint.py` both 0
errors. Installed as `mep/`, swept 12 samples (5–60s): magenta appeared in
**every** sampled frame — a generic, densely-repeated scenery element (tree
trunks tiling the whole jungle backdrop) carries this sheet's art, not just
the rare player/enemy figures.

Built the matching unpainted-rebuild control the same way as Mega Man 3's
(all seven sheets restored from `.orig.png`, upscaled ×4,
`runs/f918v-20260915/contra/unpainted-full/`, 0 build errors) and replayed
both at 10s from the same minted state. Per-pixel diff: 252521 of 983040
pixels differ (the painted scenery covers much more on-screen area here
than Mega Man 3's small banner) — but **every single differing pixel is
exactly pure magenta on the painted side, zero exceptions**. No pixel
differs for any reason other than the declared edit; the rest of the frame
(both player characters, rocks, water, mountains) is untouched.

## Verdict

**Structural gate: PASS on both games** (kit `--verify` ×6, `build`,
`mep_lint.py`, all 0 errors, on a binary rebuilt at current `main`).

**Runtime gates:**
- Paint application (6.3): **PASS on both games**, pixel-exact evidence —
  Mega Man 3 (CHR ROM, READY banner, bbox-exact diff) and Contra (CHR RAM,
  scenery tiling, every differing pixel provably magenta).
- Shared-key ownership (#253): **PASS**, structural (0 errors on both
  packs) + visual (fully painted, no dilution, on both).
- Condition fallback (#256): **structural PASS** (paired conditional/bare
  lines carry identical, correctly-painted tile data) — runtime
  condition-miss frame not isolated.
- Mirror (#255): **structural PASS only** — no live mirrored-instance
  screenshot obtained on either game.

The row's core evidence gap from C.5 — "does the painted figure actually
appear, and does nothing else change" — is now closed with pixel-exact
proof on both the CHR RAM and CHR ROM golden games, not inference from file
counts. What remains before the row is fully closed: a runtime
condition-miss frame and a live mirrored-pair screenshot. Both need a frame/
condition introspection tool this session did not build; recorded here as
explicit open debt rather than pursued further.

## Next step, if resumed

Build a small tool that reports, per frame of a recording, which `hires.txt`
keys and which `[condition]` labels were active — turning "find the frame
where X renders" from a screenshot sweep into a lookup. Without it, the
mirror-live and condition-miss checks stay a matter of chance sampling.
