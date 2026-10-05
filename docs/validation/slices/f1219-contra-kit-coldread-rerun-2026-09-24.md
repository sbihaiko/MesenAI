# Cold read re-run: Contra artist kit after #399, #400, #401 and #413 (2026-09-24)

This re-runs the F12.19 (4) cold-read row, which also closes F12.18 (3). The first run
(`f1219-contra-kit-coldread-2026-09-23.md`) had the verdict "no" and filed
three defects: #399 (painted figures never routed back), #400 (a six-phase
loop in five columns with no phase order) and #401 (composites in the rest
grid). All three and #413 (figure reload) are closed and merged. The kit is
regenerated from a fresh recording on current `main`, with the same route
and protocol as `f1218-f1219-contra-rerecord-2026-09-23.md`. The fixes are
checked mechanically first. Then a fresh evaluator reads the kit blind,
using the same briefing.

**Verdict: "Run cycle identifiable and paintable unaided: yes."**

The raw material is in the session scratchpad and is not versioned: the
state, the recording, the kit, the sandbox and the evaluator's log. This
file keeps the numbers.

## Binary

- Worktree at `89acdc10` (`origin/main`). It carries #404 (#400), #402
  (#399), #405 (#401, ADR-0228, a Core change) and #425 (#413).
- `make capture-tool`, built from clean with CommandLineTools clang.
  `InteropDLL/obj.osx-arm64/MesenCore.dylib` sha256
  `83e2ad178712c4f5ac4f588a647d58d96bf3b7f7f48f0e675468d0cdc9787065`.
  `otool -L scripts/headless_record` resolves the dylib to this worktree.
- Behavioural proof that the dylib carries ADR-0228: `poses.json` marks
  **6** poses fused. The 2026-09-23 binary marked 2. That is the
  `fused 2 -> 6` #405 measured. Every other `poses.json` figure reproduces
  the 2026-09-23 re-record exactly (below).

## Recording

- ROM `Contra (1988) (Konami).nes`, sha1
  `c9ea66bb7cb30ad5343f1721b1d4d3219859319b`. This is the dump
  `scripts/stages/contra/navigation.json` pins, from
  `runs/golden-20260913-f923/mint/`.
- Minted fresh, because `.mss` is never versioned. The command was
  `headless_record Contra.nes 30 <mint> input=scripts/stages/contra/mint-stage1.txt save-state=stage1.mss`.
  It ran 1804 frames.
- The recording command was `headless_record Contra.nes 61 <rec> bootstrap hdpack-off input=scripts/stages/contra/stage1-probe.txt state=stage1.mss`,
  with `MESEN_OAM_STREAM_DUMP` and `MESEN_POSE_TRACK_DUMP` set. It ran
  5471 frames and ended with `result: ok`.

| Metric | 2026-09-23 re-record | This run |
|---|---|---|
| Retained frames | 3667 | 3667 |
| Poses / fused | 27 / 2 | 27 / **6** |
| Cycles (period/repeats/driver) | 6/26/`port1`, 6/26/`port1`, 6/3 | 6/26/`port1`, 6/26/`port1`, 6/3 |
| Sequences | 0 | 0 |
| Tiles / missing `px`,`py` / with `z` | 292 / 0 / 118 | 292 / 0 / 118 |
| Tracks | 9 | 9 |

## Kit

The generators are `artist_kit.py`, `artist_bg_kit.py` and
`artist_chr_kit.py --rom`, each run with `--verify`, followed by
`artist_kit_assemble.py`. This is the same three-part kit as
2026-09-23; `artist_map.py` is not part of it.

- sprites 172 → 172, background 172 → 172 and CHR 1834 → 1834. Every part
  loses 0 and adds 0, and every `--verify` passes.
- `artist_kit_assemble.py` reports 3 parts, 37 files and 4178 cells. The
  2026-09-23 kit had 4182; the 4 cells missing are the rest grid's 4
  composites.
- `mep_lint.py` on the recorded pack reports 0 errors and 0 warnings.
  `mep_build.py build` reports 0 errors. Its 29 warnings are all the tool's
  own ADR-0172 sheet-size note.

### The three fixes, checked on this kit

| Issue | Check | Result |
|---|---|---|
| #400: phase order | The captions of `usr000`, `usr001` and `usr002` in ARTIST.md, the `figures/usr00N-figure.json` `label`, and `kit-part-sprites.json` `files[].playsColumns` | **fixed.** All three cycles read "loop of 6 phases … **plays columns 1 2 3 1 4 5 (column 1 plays twice)**", and `playsColumns` is `[1, 2, 3, 1, 4, 5]`. ARTIST.md explains the notation. |
| #401: no composites in the rest grid | `sheets/usr003` / `figures/usr003-figure`, viewed upscaled, plus ARTIST.md's dropped list | **fixed.** The rest grid holds 6 poses (was 10): Bill standing and firing, facing right and facing left, Bill prone, and three somersault balls. All six are Bill alone. `pose018`, `pose020`, `pose025` and `pose026` are listed as dropped under ADR-0228 ("a figure … touched by another made of tiles never seen on their own"), next to ADR-0177's `pose021` and `pose022`. |
| #399: painted figures route back | Followed ARTIST.md's "When you are done" block exactly on a copy of the pack, with every opaque pixel of `figures/usr000-figure.png` painted magenta (25 872 px) | **fixed.** The block now runs `mep_figure.py import` on every `figures/usr*-figure.png` before the build. The import wrote 26 cells. The build had 0 errors and the key set stayed at 172. In the built pack, 66 `<tile>` rules (26 distinct keys) draw magenta; the unpainted pack has 0. See the note below. |

**Note on #399 (filed as #435, P2, out of scope for this row).** The recipe
runs the imports before any build. At that point `import` resolves each
key's draw source against the recording's `hires.txt`, which still draws
from `sprites.png`. It then prints "21 painted cell(s) will re-point a key
in hires.txt at the next build", and after the build `hires.txt` differs
from an unpainted run of the same recipe by 55 rules. The paint still
reaches the pack. What is lost is *Reload Repainted Images*, which ARTIST.md
promises ("so a rebuild changes no rule … (#413)").

The control adds one `mep_build.py build` between the copy and the import,
with the same painted figure. There the import writes into `usr001.png` and
`usr003.png` ("no rule moves"), the rebuilt `hires.txt` is byte-identical to
the unpainted control, and the same 66 rules and 26 keys draw magenta. The
cold read does not depend on this, because the evaluator never runs the
recipe.

## Cold read

### Protocol

The protocol is the same as the first run (ADR-0214 §1–§2).

- **Briefing.** The briefing in `f1219-contra-kit-coldread-briefing.md` was
  the evaluator's entire prompt. It was reused character for character,
  except for the sandbox path, which is now `…/scratchpad/coldread-contra-kit-rerun/`.
- **Sandbox.** The sandbox held a copy of the regenerated kit plus
  `docs/remastering-a-game.md` and `docs/hd-pack-authoring.md` from
  `89acdc10`.
- **Evaluator.** A fresh agent (`Agent`, model `sonnet`, no fork, no builder
  context, no prior log or hint). The first run used Opus. The model changed
  because of the user's standing rule that test runs go on Sonnet and never
  on Fable. The briefing did not change.

### Result

- Start: 17:28:55 -03. End: about 17:33:00 -03.
- **Stops:** none. No read outside the sandbox and no `hires.txt` opened.
- **Path taken:** `kit/ARTIST.md` "1. Figures" table → `kit-part-sprites.json`
  (`driver port1` on `usr000`/`usr001`) → the four
  `figures/usr00N-figure.png` → the `sheets/` twins → the guides. The
  candidate files were found within the first two minutes, from ARTIST.md's
  own table.
- **Verdict line, verbatim:** "Run cycle identifiable and paintable
  unaided: **yes**". The caveat, verbatim: "an artist must open the images
  to decide between `usr000` and `usr001` (or paint both … ) as 'the' run,
  because neither the kit nor either guide names either sheet 'run'".

The evaluator's table:

| File | What it shows | Frames | Complete figure |
|---|---|---|---|
| `figures/usr000-figure.png` | Bill running, "5 drawn columns standing in for a 6-phase loop (column 1 plays twice: order 1 2 3 1 4 5)" | 5 columns / 6 phases | yes: "no offset limbs" |
| `figures/usr001-figure.png` | Bill again, same column order | 5 / 6 | yes |
| `figures/usr002-figure.png` | "a different figure — red helmet, black uniform (soldier/enemy)" | 5 / 6 | yes |
| `figures/usr003-figure.png` | six single poses of Bill, "not a run animation" | 6, no cycle | yes, each |

The evaluator's friction points, with the builder's reading of each:

1. **No human name for any row** ("run", "Bill"). This is by design: names
   come from an optional `names.json` (ADR-0227, *"Pelo nome, no kit"*), and
   the evaluator resolved it from `driver port1` plus the pictures. It
   repeats the first run's point 1. Not a defect.
2. **`usr000` and `usr001` look alike, with no rule for choosing one or
   both.** The evaluator guessed that `usr001` was a "run-and-aim-diagonally
   variant". It is the left-facing twin, the mirror of `usr000`, with its
   own pose ids, as the first run read it. Nothing in the kit states the
   facing, so this is still a labelling gap (the first run's point 5). It
   did not stop the evaluator, who chose to paint both. Not filed: the kit
   infers no subject or facing by decision (ADR-0183 §3/§5, ADR-0227).
3. **`locked` in every `usrNNN.json` is undocumented for the artist.** It
   is the composition editor's field (ADR-0168) and is not mentioned in
   ARTIST.md or either guide. This is a documentation gap only; the artist
   has nothing to act on.
4. **Scenery PNGs looked black-backed.** The evaluator suspected its own
   viewer. Checked: `usr004` and `usr017` are RGBA with alpha down to 0, so
   it is the viewer. Not a defect.
5. **The raw `sheets/usr*.png` looks broken before the reader reaches the
   paragraph on the composed figure view.** The two surfaces behave as
   designed (ADR-0225): 8 px cells versus pixel precision. This is an
   ordering point in ARTIST.md.

### Against the first run's three defects

| First-run defect | Re-run |
|---|---|
| "Loop of 6 phases" but 5 columns, with no phase order (#400) | The evaluator quoted the order "1 2 3 1 4 5 (column 1 plays twice)" unprompted. Resolved. |
| Fused Bill+soldier figures on `usr003` (#401) | "The six poses are individually clear … nothing duplicated or visibly broken". Resolved. |
| The copy recipe loses figure work (#399) | Not raised. The recipe now imports figures, and the mechanical check above shows the paint reaches the pack. Resolved; the live-reload residue is #435. |

## Stop conditions

| Slice | Condition | Result |
|---|---|---|
| F12.18 | (3) the kit's figure row shows the six phases with no intra-figure gutter and the row baseline intact | **met.** Each row is 5 composed figures on one baseline with no gutter inside a figure (evaluator: "consistent baseline across columns"). The caption and `playsColumns` give the six-phase order 1 2 3 1 4 5, and a cold reader read it. |
| F12.19 | (4) the Contra kit's figure rows are period-6 cycles and the regenerated surface gets its cold-read row | **met.** `usr000`, `usr001` and `usr002` are the three period-6 cycles (26/26/3). The re-run cold read's verdict is "yes". |

## Filed

- #435 (P2): the "When you are done" recipe imports figures before any
  build, so a painted figure re-points 55 rules and *Reload Repainted
  Images* cannot show it. Spiked with a control run; details above.
