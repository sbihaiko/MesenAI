# ADR-0209: Name a figure, hand it to the artist's own editor, and reload — we own selection and return, not the brush

- Status: proposed
- Date: 2026-09-18
- Related: ADR-0153 (artist-legible sheets), ADR-0164 (adjacency statistics), ADR-0165 (the composition editor), ADR-0168 (the `sprNNN` figure is the sprite unit), PRD Part A F12.2 (shipped), F12.3 (reload), F12.4 (asset-name template)

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
