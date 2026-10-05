# ADR-0221: A capture's gate must separate it from the frames it must not draw on, not only from the other captures

- Status: accepted — decided 2026-09-22 on the user's pick *"B sozinha (Recommended)"* for the option, and *"Não, só a ADR"* for the go-ahead (a decision and a request for work, not a same-turn implementation); implementation go-ahead *"dispara as frentes 1, 2, 3 e 4 em paralelo usando workflows"*, then *"Mergear + abrir ADR-0223 (Recommended)"*. The decision lives in `MesenSheets::SelectScreenAnchors`, PRD Part A **F12.13**.
- Date: 2026-09-22
- Related: issue #339, ADR-0217 and ADR-0218 (the change that closed gate *collisions* and did not close #339), ADR-0159 (anchors chosen at save time from cells variants do not change and rivals do not share), ADR-0156 (a captured screen owns the cells it covers), ADR-0050 (bootstrap `<background>` capture), ADR-0183 §3 (the recorder emits observations, never readings), ADR-0223, ADR-0236, PRD Part A §3
- Amends: ADR-0159 §1 (narrowed — see Decision). ADR-0156 §Decision is **not** amended; ADR-0159 carries the matching "Amended 2026-09-22" note.

## Decision

**A frame is a variant of a captured screen only when every cell it changes is a cell the capture's own art already carries. A frame that adds content the capture does not have is a rival, whatever the agreement ratio.** (option B, decided 2026-09-22.)

At save time in `MesenSheets::SelectScreenAnchors` (ADR-0159 §3), for each retained frame at the same `FineX` that today clears `kAnchorVariantAgree`: walk its changed cells against the captured frame. A cell whose captured content is **empty** — the shape id of a flat tile, the same "single flat colour per 8×8 cell" the acceptance tool already scores — and whose frame content is **non-empty** is an *addition*. One addition makes the frame a rival; a frame whose every changed cell is non-empty on both sides (animation, a score digit, the other half of a blink) stays a variant. The threshold constant and `kAnchorVariantAgree`'s measured meaning are untouched; the kind test runs *after* the ratio, so nothing that was a rival becomes a variant. The flat-shape plane it reads lives in `Core/NES/HdPacks/TileSheetTypes.h`.

Why "empty on the capture side" and not a pixel diff: the capture is a frozen PNG of the whole screen, so it *covers* every cell by construction, and the destructive case is the cell it covers with nothing (#339's white block under `STARRING`). The classification is made once, at record time, from the recorded grid, so a later repaint changes no gate, and the frame with the text — now a rival — is captured on its own when it holds still, so the artist paints both. ADR-0159's "a capture owns its variants" survives, **narrowed** by the definition under this Decision; "a captured screen owns the cells it covers" stands.

## Context

ADR-0217/ADR-0218 shipped 2026-09-20 and closed gate *collisions*: across the 30-ROM bounded library, **193 captures, 0 sharing a gate**, against 113 of 237 before. Issue #339 is nonetheless unchanged, and the measurement says why.

Mike Tyson's Punch-Out!!, rendered from power-on at 1 s steps from 10 s to 45 s, three ways (no pack, a pre-change pack, a post-change pack), renders identically at all 36 timestamps. The two packs are route-matched — same **60 s** recording, same 11 606 `<tile>` keys, differing only in their capture gates. Between 18 s and 27 s the game draws the pre-fight card: `screen003`, frozen earlier in the same card, draws over the live background and the ROM's `STARRING` / `LITTLE MAC` pixels (7 808 in the game's own frame, 0 in the rendered one) are gone. The two frames agree on 919 of 960 tile cells — 0.9573, above `kAnchorVariantAgree` (0.90) — so the later frame is a variant, and ADR-0159 §1 then excludes the 41 cells that hold the text from the anchor pool by construction: the resulting gate is guaranteed to match the frame with the text. *Distinct from the other captures* is not the same as *sufficient to identify the frame*.

Non-goals: no change to what is captured (`CaptureScreen`'s trigger, numbering and ADR-0156 residency stay); no machine reading — a `memoryCheck` would separate the frames trivially and ADR-0183 §3 forbids it (this recorder emits observations, never readings); no re-opening of gate collisions.

## Options weighed (proposed 2026-09-20)

1. **A.** Raise `kAnchorVariantAgree` above 0.9573 — smallest change, but #339 is about kind, not proportion, a two-cell score digit would become a rival, and it guesses: the next game's destructive difference may be 5 cells.
2. **B.** Classify a variant by the *kind* of difference, not its size (picked). Against: needs a per-cell "does the capture cover this" comparison at save time, and a definition of *covers* that survives a repainted pack.
3. **C.** Require one discriminating probe — cheap and local, but revokes "a capture owns its variants" wholesale; the drawing rate cost is unmeasured.
4. **D.** Refuse at draw time in `HdNesPack` — the capture loses those cells to the game's own `<tile>` rules and keeps the rest; the only option that can make a pack an artist already painted look different, but it amends ADR-0156 and puts a per-cell comparison in the render hot path.
5. **E.** Change nothing; record in `ARTIST.md` and `mep_lint` that a capture may draw over frames it was not frozen for. Honest to what this pipeline is — ADR-0183 frames the output as material for an artist, not a shipped renderer — but rejected: #339 is player-visible (ADR-0146 auto-loads every accepted pack, including `docs/community-packs.json`).

## What a human had to pick

1. Which option, or which combination (B and D are not exclusive). 2. Does ADR-0159's "a capture owns its variants" survive? (kept on 2026-09-20 as *"Manter (captura vale p/ variantes)"*, before this measurement existed; B narrows it, C revokes it, D routes around it.) 3. Render-time correctness or visibility to the artist? 4. If D, does it amend ADR-0156? 5. **What is the measurement budget?** Re-recording the 30-ROM library is ~15 minutes of wall clock and is what produced every number above; a synthetic `GridFrame` unit test is seconds but cannot see #339; scoring a sweep is free (`scripts/measure_capture_overdraw.py`, added 2026-09-20), so the cost is the recording, not the reading.

## Record

- 2026-09-20 — proposed, with the five options above and the questions above ([log](../../validation/adr/adr0217-0218-anchor-gate-collisions-2026-09-20.md); `docs/validation/adr/adr0217-0218-anchor-gate-collisions-2026-09-20.md`).
- 2026-09-22 — accepted as option B (PRD Part A F12.13), shipped the same day ([log](../../validation/slices/f12.13-variant-kind-rule-2026-09-22.md)). Stop conditions (2) and (3) met; **(1) not met** — the Punch-Out!! sweep still erases 437 cells, because the kind test files the frames as rivals (228 of them) and the search cannot separate them: the only discriminating cell is flat on the capture side, and ADR-0050 excludes flat cells from the anchor pool. Issue #339 stays open; the follow-up decision is ADR-0223 (`proposed`).
- 2026-09-25 — ADR-0236 picked option D for #499 as an opt-in guard keyed by a new per-cell record; ADR-0156 §Decision remains unamended.

## Consequences

- The acceptance test is now a tool: `scripts/measure_capture_overdraw.py --sweep <dir>` renders Punch-Out!! at 1 s steps from 10 s to 45 s and counts, per 8×8 cell, what the ROM draws and the render does not (`scripts/measure_capture_overdraw.py --sweep`, exit code is the verdict). It reports **12 of 36 frames losing content, 437 erased cells**, identical before and after ADR-0217/ADR-0218. Any fix that does not move that number has not closed #339. See `docs/validation/adr/adr0221-capture-overdraw-harness-2026-09-20.md`.
- A second occurrence, at 41–43 s, the hand measurement missed: 18, 18 and 27 erased cells after the pre-fight card is long gone. A card-only fix has not finished the job; attributing it to a capture still needs the recorder's anchor summary.
- Two measurement traps, now enforced by the tool: a pack installed at `mesen-home/HdPacks/<stem>/` is *not* found by `scripts/headless_record` (the log says `LoadHdPack: 0 ms; no-pack`, screenshot back at native 256×240) — the sibling convention `<romdir>/<stem>/auto` loads it; and comparing a rendered pack frame against a nearest-neighbour upscale of the no-pack frame measures the upscale, not correctness — hence the per-cell metric.
- Doing nothing has a cost that is not zero: ADR-0146 auto-installs every accepted community pack, so a capture that erases live art reaches players with no opt-in. That is the argument against E.
- Option B changes what the recorder writes, so packs recorded before and after differ and neither can be regenerated from the other without the original ROM session.
- A unit test covers the kind test on a synthetic `GridFrame` pair (addition → rival; content-for-content → variant; ratio below threshold → rival as before).
