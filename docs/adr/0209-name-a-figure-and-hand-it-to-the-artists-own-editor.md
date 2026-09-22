# ADR-0209: Name a figure, hand it to the artist's own editor, and reload — we own selection and return, not the brush

- Status: **Q4 accepted 2026-09-19 and shipped the same turn as F12.8** — option (k), the `unsorted` remainder sheet. User go-ahead, verbatim: *"vamos fazer a sheet unsorted então"*. Same-turn implementation is allowed by CLAUDE.md only when the change ships with unit tests covering the decision and the go-ahead is quoted here and in the PR body; both hold (`BlocoV` in `scripts/core_unit_tests.cpp`, 6 cases). **Q1, Q2 and Q3 accepted 2026-09-20:** Q1 = (b), Q2 = (e), Q3 = (i), each picked from the options this ADR laid out (not typed free-form) via a structured question the user answered directly; recorded here as the decision, not yet implemented — F12.3 (reload) is the prerequisite this ADR itself names, and F12.4's return-path template (Q3's own dependency) is not shipped either. No same-turn implementation: no go-ahead to build now was sought or given for Q1–Q3. **Q2 and Q3 implemented 2026-09-22** (F12.3 and F12.4 having shipped on 2026-09-19 as ADR-0212 / ADR-0213): `scripts/mep_figure.py export` writes the `sprNNN`/`objNNN` figure as one PNG plus its 1x `*.orig.png` twin and a sidecar mapping each cell rect back to `(sheet, cell index)`, on the F12.4 asset-name contract; `scripts/mep_figure.py import` brings the painted file back onto exactly those cells, only where they differ from the twin, with `--verify` as the ADR-0183 §4 round-trip. Suite `scripts/test_mep_figure.py`; evidence `docs/validation/adr0209-q2-q3-figure-export-2026-09-22.md`. Go-ahead, verbatim, for building Q2/Q3 in parallel with F12.11/F12.13/F12.14: *"dispara as frentes 1, 2, 3 e 4 em paralelo usando workflows"*; the change ships with `scripts/test_mep_figure.py` (35 checks) and an independent test pass reproduced the Contra round-trip on a fresh copy. **Q1 implemented 2026-09-22** as option (b): the Core infers a default `label` at record time from the grouping it already computed and writes it beside a `"labelSource": "inferred"` field — on every sheet cell (`<context> #<metatile> x<count>`), on every `sprNNN`/`objNNN` group sheet (`<kind> group <cols>x<rows>, <n> cells[, <p> poses], x<max count>`), on every `poses.json` pose (`figure <w>x<h>, <t> tiles, <f> frames[, fusion][, variant of poseNNN]`) and run (`<loop|sequence> of <n> phases, <w>x<h>, x<repeats>[, driver portN]`). The scheme lives in `Core/NES/HdPacks/SheetLabels.h`, host-free, and states only what the recording measured — no subject is ever guessed (ADR-0183 §3/§5). Readers (`scripts/compose_engine.py` `caption()`, `scripts/artist_kit.py` row captions, `scripts/mep_figure.py export` `label`/`labelSource`, with `--names`) prefer a human `names.json` name, then the sidecar label, then the id, and say which won; an artist renames by editing the sidecar's `label` and changing its `labelSource`. Go-ahead, verbatim: *"vai com o Q1 da ADR-0209 em paralelo também"*. Tests: four cases in `scripts/core_unit_tests.cpp` (scheme, determinism, no label without grouping data, provenance beside every label) and `scripts/test_sidecar_labels.py` (reader precedence, round-trip through `mep_build.py build` with the key set unchanged — ADR-0183 §4). Evidence: `docs/validation/adr0209-q1-inferred-label-2026-09-22.md`.
- Date: 2026-09-18 (amended 2026-09-19 with the Q4 decision; amended twice on 09-18: sheet coverage measured with Q4; then Q4 option (m), seeding coverage from an existing pack's key index)
- Related: ADR-0210 (where coverage comes from; amends Q4(m)), ADR-0153 (artist-legible sheets), ADR-0164 (adjacency statistics), ADR-0165 (the composition editor), ADR-0168 (the `sprNNN` figure is the sprite unit), PRD Part A F12.2 (code on `main`, human panel row open), F12.3 (reload), F12.4 (asset-name template)

## Context

F12.2 shipped *Copy as MEP sheet cell* on 2026-09-17 and its human panel began
the next day. Before the evaluator reached the paste step, the shape of the
workflow was questioned from the chair — not the implementation, the premise.
The request, verbatim in the session and paraphrased here in four steps:

1. work on the screen at a high resolution;
2. **mark a figure** and open it in the artist's own paint program;
3. edit and save;
4. reload, and see how it looks.

Nowhere in those four steps does a person see a tile key. The shipped F12.2
flow spends steps 7 to 10 of its own panel script on exactly that: copy a key,
paste it into `misc.json`, find pixel `208,276` on `misc.png`, and — on Contra
— resize two canvases by a whole row first. That is not a defect in F12.2; it
is evidence that F12.2 optimised a step the target workflow does not contain.

**What already exists.** Most of the four steps are built or specified:

- *The high-resolution screen.* `map-000.png` in a Zelda pack is 2048x704 —
  two stitched screens at the pack's `<scale>` of 4, the artist surface
  ADR-0153 and ADR-0156 introduced. The scale is a recording parameter feeding
  `MesenSheets::Upscale(image, _hdData.Scale)` in `HdPackBuilder`, not a format
  limit.
- *The figure.* Grouping is the whole point of Phase 9: `metatiles.json` (182
  cells on Zelda, each with a `count` — the first appears 1 004 times),
  `obj000..007.json` with `kind: "object"`, and the `sprNNN` figure ADR-0168
  makes the sprite unit.
- *The name.* Every cell of every sheet already carries a `label` field. It is
  always the empty string: `Core/NES/HdPacks/SheetRender.cpp` emits
  `", \"label\": \"\", \"tiles\": "` literally. Nothing writes it and nothing
  reads it. Zelda's 182 metatile cells have zero non-empty labels.
- *Save and reload.* F12.4 (asset-name template the paint program exports to)
  and F12.3 (reload the pack without reopening the ROM) are both specified and
  unstarted. F12.1 measured what F12.3 must answer for: a **412 ms** parse
  against a **13.2-16.4 s** detached bitmap decode for 271 images.

**The question that forced this ADR.** Asked in the same session: could the
viewer we already built stand in for the external paint program? It cannot,
and the reason is worth recording rather than rediscovering:

- `scripts/compose_editor.py` (ADR-0165) has a tkinter canvas, but it
  *composes* — seed, rank, lock, recompute over adjacency, export the kept
  cells as a `usrNNN` sheet. It places art; it does not draw it.
- `scripts/record_viewer.py` (ADR-0169) watches a live recording. Read-only.
- `scripts/sheet_repaint.py`, despite the name, is a *generated* upscale into
  `auto/repaint/`, labelled `generated` so ADR-0154 can keep it out of the
  catalog. It is not manual editing.

So the honest answer is split: our tool is already the **selector** the second
step asks for, and should not become the **brush**.

**Measured 2026-09-18 — the simple flow already exists, for 12.5% of the art.**
The question that produced this measurement was asked plainly: why are the
editable textures not already sitting in `auto/`, ready to paint? The answer
turns out to be "they are, for one tile in eight".

- **Every `<tile>` rule points at `chr/`, none at `sheets/`.** Zelda: 2 203 of
  2 203. Contra: 2 276 of 2 276. The sheets are a derived surface; what makes a
  painted sheet reach the game is `mep_build.py build`, which asks "was this
  cell painted?" by comparing it against its `*.orig.png` twin (`_EditedProbe`)
  and rewrites `hires.txt` to point the key at the sheet.
- **So for a cell that is already in a sheet, the four-step flow works today**:
  open `sheets/metatiles.png`, paint, `mep_build.py build`, reopen. No key, no
  JSON edit, no computed pixel offset. Steps 6 to 10 of the F12.2 panel script
  do not exist on that path.
- **But the sheets hold 319 keys against the pack's 2 203** — 275 of them
  matching, 12.5%. `metatiles.json` 141, `sprites.json` 121, `misc.json` 62,
  the rest in the tens. **1 928 keys have no sheet at all.**

The F12.2 panel script does not use the working path. It tells the evaluator to
*append* a cell to `misc.json`, which deliberately exercises the uncovered case.
That is why the script reads as complicated: it measures the worst case and
presents it as the normal one.

One candidate explanation was checked and refuted rather than assumed: the
uncovered 1 928 are **not** CHR dump noise. `HdPackBuilder::ProcessTile` is
called from `HdBuilderPpu` per scanline and cycle, so a tile is recorded only
when the PPU actually drew it. Every uncovered key is art that appeared on
screen.

**Non-goals.** Writing a pixel editor. Reading `.psd`. Changing the recorder's
scale (a separate question). Superseding F12.2 — the copy action stays, and the
panel measuring it stays valid as the baseline the new flow is compared against.

## Constraints — stated by the decision-maker, not open for trade

Added 2026-09-18, in the same session, and binding on every option below:

1. **The process must be simple.** An option that is powerful and adds steps
   loses to one that is weaker and removes them. The four-step shape above is
   the budget, not an aspiration.
2. **The feedback must be easy, and it must be *in the game*.** Not a rebuilt
   PNG, not a lint exit code, not a headless screenshot — the artist looks at
   the running game and sees the change. Every criterion the F12.2 panel
   measures short of P14 is a proxy; this says the proxy is not the product.
3. **Returning to the same moment is in scope.** If reload cannot preserve the
   running state, the emulator may save the state, reload, and restore it
   automatically, so the artist is standing in front of the same screen with
   the new texture on it.

Constraint 3 settles something F12.3 had left open. Its row requires the swap
"with no state loss" and F12.1 measured the cost as a 412 ms parse against a
13.2-16.4 s detached bitmap decode, with the decision rule "full reload if it
costs under one frame budget times an agreed factor, otherwise per-image
invalidation". Save-state / reload / restore is a third strategy that satisfies
"no state loss" **without** per-image invalidation, at the price of a visible
pause rather than a seamless swap. Two things make that price smaller than it
looks: the decode is already detached (`HdPackData::LoadAsync`), so play can
resume before it finishes, and a paused artist waiting for their own edit is
not the same user as a player mid-game.

It is not free, and the ADR should not pretend otherwise: a save state carries
console state, not pack state, so the restore path has to be proven — pixel
exactness after restore is the test, and "it looked right" is not it.

## Decision

**Open — three questions, and they need one answer each before any code.**
The constraints above bound the answers; they are not themselves in question.

The proposed shape, for the record: MesenAI owns **selection** (which art, named)
and **return** (the edited file coming back and rendering), and delegates
**painting** to whatever program the artist already uses. Competing with GIMP
and Aseprite on brushes is a fight we would lose, and losing it costs the thing
we are actually good at.

**Q1 — who writes `label`?**

- **(a) The artist, in the composition editor.** A name is an authoring act;
  the editor already opens the pack and already writes sidecars.
- **(b) The Core, inferred at record time** from the grouping it already
  computes (`metatile`, `context`, `count`), as a default the artist renames.
- **(c) Both** — Core seeds, artist overrides, and the sidecar records which.

**Q2 — what does "mark a figure" export?**

- **(d) The cell.** Smallest change; matches what `Copy as MEP sheet cell`
  already does, only by group instead of by 8x8 tile.
- **(e) The figure.** The `sprNNN` unit of ADR-0168, or an `objNNN` group,
  reassembled through its `evidence[]` offsets into one PNG the artist sees as
  a character rather than as fragments.
- **(f) The screen crop.** A rectangle of `map-000.png`, which is what "work on
  the screen" literally asks for — and the option that makes a brush stroke
  cross cell boundaries, which nothing downstream currently handles.

**Q3 — how does the file come back?**

- **(g) Same path, watched.** The export writes a PNG the artist saves over in
  place; a watcher fires the F12.3 reload. Closest to the four steps as stated.
- **(h) Same path, explicit re-import.** The artist saves, then triggers the
  return by hand. Slower, but no watcher and no half-written-file race.
- **(i) Whatever F12.4's template already decides**, with this ADR adding only
  the launch and the reload trigger on top.

**Q4 — how does sheet coverage get to 100%?** Added by the measurement above;
without it the other three questions only serve one tile in eight.

- **(j) Record more.** Coverage comes from what the recorder saw, so longer and
  wider sessions fill the sheets with no code at all — this is what F9.25
  already does for Contra. Cheapest, and unbounded: it never *reaches* 100%,
  it only approaches it.
- **(k) Emit a remainder sheet. — CHOSEN 2026-09-19, shipped as F12.8.** One `unsorted` sheet per pack carrying every
  key no other sheet claimed, with the same `*.orig.png` twin mechanic. Coverage
  of the recorder's shape registry becomes 100% by construction. Reuses the
  mechanism that already works rather than adding one. (The "100%" was written
  here as coverage of the *pack*; the measurement below shows the registry and
  the pack are not the same set, and the honest claim is the narrower one.)
- **(l) Leave it, and keep `Copy as MEP sheet cell`** as the escape hatch for
  whatever the sheets miss — the status quo, stated as a choice.
- **(m) Seed coverage from an existing pack's key index, and render the art
  ourselves.** A `<tile>` key is `(tileData, palette)`: 16 bytes of original CHR
  plus four NES colours. That *is* the original art — so any third-party
  `hires.txt` can be read as an **index of which tiles exist**, and each key
  rendered through the upscale `HdPackBuilder` already applies. No pixel of the
  other pack is copied; nothing but facts about the ROM is taken.

  > **Amended 2026-09-18 by ADR-0210:** the premise above holds only for CHR RAM
  > games. `HdPackLoader::ReadTileData` branches on the field's length — 32 hex
  > characters or more are the literal 16 pattern bytes (CHR RAM), anything
  > shorter is a *tile index* into the ROM's own CHR and carries no art at all.
  > Zelda and Contra, measured below, are both CHR RAM, so their numbers stand;
  > but 23 of the 30 bounded ROMs are CHR ROM, and for those a third-party index
  > contributes palettes only. ADR-0210 decides how each source is used.


### Q4 decided: (k), the remainder sheet

`unsorted.png` / `unsorted.orig.png` / `unsorted.json` carry one 8x8 cell for
every recorded shape no other sheet put on a canvas. Written last in
`HdPackBuilder::BuildSheets`, because it is the complement of everything above
it: `WriteSheetFiles` is the single funnel every sheet passes through, so it
accumulates the shape ids as they are written and the remainder reads what is
left. Within that set, coverage stops being a number to improve and becomes
true by construction.

Three properties are deliberate, and each is pinned by a unit test:

- **Not alias-collapsed.** `CollapseAliases` stops an artist paying twice for
  one subject on a sheet built *around* subjects. The remainder is leftovers,
  its cells are unrelated by construction, and collapsing them would hide a key
  behind a look-alike with no group to explain the substitution.
- **A shape with no drawable art is left off**, not shipped as a transparent
  cell. Coverage means a paintable surface, not a numbered blank — a hole would
  make `mep_build.py` resolve that key to empty pixels.
- **An empty remainder writes no file.** A pack whose sheets already cover
  everything ships no stub `unsorted.png`.

### What (k) actually closed, measured 2026-09-19

Run on two CHR RAM games with a bootstrap recording (Castlevania 60 s idle;
Zelda 85 s played from `scripts/stages/zelda/mint-stage1.txt` +
`stage1-run.txt`), counting distinct `(tileData, palette)` keys in the emitted
`hires.txt` against the keys reachable from a sheet cell:

| | Castlevania | Zelda |
|---|---|---|
| `<tile>` keys in `hires.txt` | 3 209 | 2 171 |
| distinct shapes in `hires.txt` | 2 673 | 1 615 |
| shapes in the recorder's registry (`_shapeTiles`) | 514 | 277 |
| on a sheet **before** `unsorted` | 380 | 187 |
| on a sheet **after** `unsorted` | **514** | **277** |
| registry coverage after | **100%** | **100%** |
| pack coverage after | 19.2% | 17.2% |

**The claim this ADR made — "an artist cannot meet a recorded tile with no
surface to paint" — is false as written, and the table is why.** (k) closes the
gap between the shape registry and the sheets completely, and that gap is now
zero by construction. It does not close the gap between the *pack* and the
registry, which is far larger and was never in this slice's reach.

The cause is upstream of every sheet. `ShapeIdFor` — the only thing that ever
appends to `_shapeTiles` — is reached from exactly two callers,
`RecordGridFrame` and `RecordSprite`, both gated on `_captureScreens` and both
fed by the retained frame stream (`kMaxSheetFrames`, consecutive duplicates
collapsed). `ProcessTile`, which emits the `<tile>` rules, runs on everything
the PPU draws and answers to neither. Zelda's registry tops out at shape id 276
while its `hires.txt` names 1 615 distinct shapes; the missing 1 338 were never
offered to the sheet pipeline in the first place, so no sheet — remainder or
otherwise — could have carried them.

Reaching 100% of the *pack* is therefore a separate decision about what the
recorder retains, not about how sheets are laid out. It is left open here
deliberately rather than folded into this slice.

Nothing else changes: the sheet uses the same `BuildContactSheet` geometry, the
same `*.orig.png` twin, and the same v1 sidecar schema, so `mep_build.py` reads
it with one new line — a `_SHEET_RANK` entry of 0, which never actually decides
anything because the sheet is disjoint from the others by construction.

The other three options stay unchosen rather than refuted: (j) recording more
is still the only thing that adds *observed* pairs, (m) is now governed by
ADR-0210, and (l) is what this supersedes.
Measured 2026-09-18 against the two community packs installed beside the
bounded ROMs:

| | our `auto/` | their pack | union |
|---|---|---|---|
| Zelda | 2 203 pairs | 7 210 | **8 744** |
| Contra | 2 276 pairs | 7 818 | **9 417** |

Roughly 4x coverage on both, and the shape of the gain differs per game. By
`tileData` — distinct art, palette ignored — Zelda is **1 615 ours against 992
theirs**, with only **53** shapes we lack: their advantage there is almost
entirely palette variety (131 palettes against our 24), and a pair whose
palette we never recorded does not match at run time however good our art is.
Contra is the opposite: **3 404 theirs against 2 064 ours**, 2 585 shapes we
never saw.

The split between "art we already have in a PNG" and "art we never recorded"
does **not** bear on feasibility, only on where the pixel comes from: since
`tileData` is in the key, both are generated the same way, at the same quality.

Two constraints on (m), both found while measuring:

- **A pack carrying `<patch>` cannot seed anything.** Its keys are bank indices
  of the *patched* ROM (ADR-0198 §2/§3). Checked: the Zelda community pack has
  one `<patch>` line, the Contra one has none — so the game with the larger art
  gain is the usable one, and the other needs §3's patched-ROM path first.
- **Provenance should be recorded** in `pack.json` even though a key is a fact
  derived from the ROM rather than an authored thing. Cheap now, and it settles
  a question that will otherwise be asked later.

`mep_import.py` is **not** this: ADR-0198 §1 has it "cut every `<tile>` it names
out of the PNG the rule points at" — it imports their art. (m) imports their
index and leaves their art alone.

(k) and (m) compose rather than compete: (m) decides *which keys exist*, (k)
guarantees *every key has a surface*. (k) is the one that makes the original
question stop existing. It is also the
only one that satisfies constraint 1 for the whole library rather than for the
covered fraction.

**Ordering, independent of the above.** F12.3 is the prerequisite for all of
it: without reload, step 4 is "reopen the ROM", which is the friction the whole
request exists to remove. F12.3 is already next in the Phase 12 order and needs
no decision from this ADR to start — but constraint 3 narrows its own decision
rule, so F12.3 should be planned with save-state / reload / restore on the
table as a first-class strategy rather than a fallback.

## Consequences

- Answering Q2 with (f) makes a painted stroke able to span cells, and nothing
  in `mep_build` resolves that today — sheets are painted per cell against an
  `orig` twin. (f) is therefore the expensive answer, not merely the ambitious
  one, and it should not be chosen on the grounds that it sounds closest to the
  request.
- Launching an external program from the emulator is a per-platform affordance
  (`open -a`, `xdg-open`, `ShellExecute`) and a configuration surface we do not
  have yet. It is also the first place MesenAI would execute something the user
  configured, which deserves its own line in whatever ships.
- (k) grows every pack's `sheets/` directory by whatever the vocabulary did not
  claim — on Zelda that is 1 928 keys, against the 319 the sheets hold now. The
  remainder sheet's size, and whether it is one sheet or many, is the part of
  (k) that needs measuring before it is chosen.
- Constraint 1 argues against (f) and against (c) on its own, independently of
  cost: both add a decision the artist has to make before they can paint. If a
  measurement later contradicts that, the measurement wins.
- Filling `label` changes a field that is currently a constant in every pack
  ever recorded. Any reader written against "label is always empty" — there are
  none today, which is why this is cheap now and will not stay cheap.
- F12.2 is not superseded. If this flow ships, *Copy as MEP sheet cell* remains
  the escape hatch for the one-off tile and the baseline the panel measured. A
  future slice may find nobody uses it; that is a measurement, not a
  prediction to write here.
