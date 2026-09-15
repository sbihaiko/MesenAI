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

## 4. Runtime control run — the two open checks (same day, second pass)

The two checks left open above were closed without touching `Core/`: the
same binary (`scripts/headless_record` sha256 `511ba70f…`, `MesenCore.dylib`
`9c44d01f…`; no `Core/`/`InteropDLL/` commit between `04d7fc63` and HEAD
`1f6190cc`), the same ROM, minted state and route, replayed against a
**negative-control pack** derived from `painted-full`.

**Why a control pack and not a screenshot sweep.** A condition miss is
invisible by construction: the builder emits the gated line and its bare
twin pointing at the *same* PNG cell (`HdPackBuilder.cpp`, F9.28 dual
emission), so hit and miss draw identical pixels. The runtime does expose
the decision, though — `HdNesPack::GetMatchingTile` returns the first entry
in file order whose conditions pass — so re-pointing each *line class* at an
unmistakable cell turns the decision into a colour. This is a validator
artifact: it edits a copy of the built pack for measurement, never the
artist's path (gate 6.3 forbids `hires.txt` diagnosis on the *user's*
success path). The generator is
`scripts/validation/f918v-control/make_control_pack.py` (versioned after the
run; the run used the same logic with the four mirror keys listed by hand,
the versioned script reads them from the kit's `usr*.json` sidecars and so
marks all seven `usr000.json` flags); the pack is `runs/f918v-20260915/contra/control-2c/`,
`textures/hires.txt` sha256 `73010de3…`, `mep_lint.py` **0 errors** (the
same 14 sheet-size warnings as `painted-full`).

| Line class in `hires.txt` | Rendered as | Meaning at run time |
|---|---|---|
| `[cond]<tile>` (142 gated lines) | Y(8) \| **cyan**(16) \| B(8) cell | the gated rule matched live |
| bare twin of a gated line (65) | its sheet cell, sheets repainted Y \| **magenta** \| B over opaque px | every gate for that key missed; the bare twin rendered |
| bare line with no gated sibling (274) | Y \| **orange** \| B cell | key outside both checks |
| the 4 keys `kit/sheets/usr000.json` flags `"mirror": "HV"` (10 lines, any class) | one **solid four-quadrant** cell: TL red, TR green, BL blue, BR white | quadrant order reads the flip; no transparency involved |

Runs: `scripts/headless_record runs/f918v-20260915/contra/Contra.nes <s>
<prefix> screenshot state=runs/f918v-20260915/contra/stage1.mss
input=scripts/stages/contra/stage1-run.txt`, the control installed as
`Contra/mep/textures` for the duration of the sweep and `painted-full`
restored afterwards (`cmp` identical). Note `hdpack` is the builder's
*recording* flag and disables pack rendering — the first attempt with it
produced native 256×240 frames; the runs below omit it, as the sweeps in §3
did. Every run logged `[MEP] textures: loaded NES HD pack from
'…/Contra/mep/textures'`. Screenshots and per-run logs:
`runs/f918v-20260915/contra/control-sweep/` (1–17 s, 0.25 s step, 65
frames) and `control-sweep-jump/` (8.40–10.60 s, 0.02 s step, 111 frames).
Census/orientation scripts: `scripts/validation/f918v-control/analyze_control.py`
and `find_mirror_marker.py` (usage in that folder's `README.md`).

**Condition fallback (#256) — runtime PASS.** In every one of the 65 frames
the two outcomes coexist: cyan (gated rule matched) and Y|magenta|B twin
cells (gated rule missed, bare twin drawn), with zero orange in the frames
below, so every magenta is a twin. Examples, `Contra_c10.png` (frame 4208,
sha256 `e14f21b4…`): cyan 233 313 px, magenta 2 432 px, 92 twin rows, 0
mirrored; `Contra_c12.png` (frame 4328, sha256 `6059c809…`): cyan 232 004,
magenta 51 200 — a scroll-transition frame where the `tileNearby` gates on
the canopy fail wholesale and the painted twin carries the whole row. The
claim under test — a miss never costs the paint — is now observed live, not
inferred from the file: the fallback target is the painted bare line,
exactly as §3 read it off `hires.txt`.

**Mirror (#255) — runtime PASS.** The four marked `mirror: "HV"` keys (of
the seven `usr000.json` flags, all `HV`) are the player's somersault tiles. Sampling the jump at 0.02 s: the marker renders
with **quadrant order HV-flipped** (white|blue over green|red) at 9.04 s
(frame 4150, `Contra_c9.04.png` sha256 `67daf835…`), 9.30 s (4166,
`Contra_c9.3.png` `b99a38bd…`), 9.34 s (4168) and 9.36 s (4170), and
**un-flipped** (red|green over blue|white) at 9.14 s (4156), 9.16, 9.20,
9.46, 9.50 s (4178, `Contra_c9.5.png` `e2786c1a…`) and 9.54 s — the same
key, same route, both orientations, 2 784–2 928 marker px per instance
(about three cells). This is the live pair the un-bake of ADR-0178 predicts:
one stored un-flipped cell, `DrawTile` applying the OAM flip at draw time.
The Y|mid|B census had earlier flagged 4–12-row "mirrored" slivers in
sprite-over-background regions; those are artifacts of painting opaque
pixels only (a transparent yellow band exposes the background's colours) and
are why the mirror check uses the solid marker instead.

Observations outside the checks: frames ≥ 17 s of this route are the Game
Over screen, whose blank background tile resolves to a painted `usr` key and
renders the whole screen magenta — the §3 sweep's "magenta in every frame"
past 20 s was this screen, not scenery. `artist_map.py`/`MESEN_SHEET_GRID_DUMP`
for Contra (§1) stays a gap.

## Verdict

**Structural gate: PASS on both games** (kit `--verify` ×6, `build`,
`mep_lint.py`, all 0 errors, on a binary rebuilt at current `main`).

**Runtime gates:**
- Paint application (6.3): **PASS on both games**, pixel-exact evidence —
  Mega Man 3 (CHR ROM, READY banner, bbox-exact diff) and Contra (CHR RAM,
  scenery tiling, every differing pixel provably magenta).
- Shared-key ownership (#253): **PASS**, structural (0 errors on both
  packs) + visual (fully painted, no dilution, on both).
- Condition fallback (#256): **PASS**, structural (§3) + runtime (§4: hit
  and miss rendered side by side in the same frame, the miss landing on the
  painted twin).
- Mirror (#255): **PASS**, structural (§2) + runtime (§4: the same
  `mirror: "HV"` key drawn flipped at frames 4150/4166/4168/4170 and
  un-flipped at 4156/4178 of the same route).

Runtime evidence for #255 and #256 is Contra-only (the CHR RAM game); Mega
Man 3's §3 evidence stands as recorded. No `Core/` change, no new tool in
the product: the introspection the first pass asked for turned out to be
unnecessary once the decision was made visible from the pack side.
