# ADR-0223: A flat cell may be an anchor when it is the only thing that separates a capture from an addition-rival ("emptiness probes")

- Status: accepted (2026-09-22 — option A, shipped the same day as PRD Part A F12.16, after F12.15 as sequenced; build go-ahead verbatim: "faz o 2 usando o sonnet", implemented and independently verified by Sonnet). The decision lives in `scripts/core_unit_tests.cpp` (BlocoP5) and in `AppendFlatAnchorCells`, inline in `HdPackBuilder.h` because `HdPackBuilder.cpp` sits on its ADR-0137 ceiling.
- Date: 2026-09-22
- Related: ADR-0221 (accepted 2026-09-22, option B — the kind test this ADR gives teeth to), ADR-0050 (bootstrap `<background>` capture; the "rarest **non-flat** tiles" candidate clause), ADR-0159 (anchors chosen at save time; §1 stability filter, §4 constants), ADR-0217/ADR-0218 (gate collisions, upstream of this and unaffected), ADR-0156 (a captured screen owns the cells it covers — not amended here), ADR-0224 (the render path the fight screen points at), issue #339, PRD Part A slice F12.13 and its log `docs/validation/slices/f12.13-variant-kind-rule-2026-09-22.md`, `docs/validation/adr/adr0221-capture-overdraw-harness-2026-09-20.md`, `scripts/measure_capture_overdraw.py`, `scripts/measure_capture_draw_rate.py`

## Context

ADR-0221 (option B) made a frame that draws content into a cell the capture holds empty a *rival*, so the discrimination pass separates it. F12.13 implemented that, and `docs/validation/slices/f12.13-variant-kind-rule-2026-09-22.md` says the kind test **fired** — Punch-Out!! reads `228 frame(s) filed as rivals for adding content the capture lacks`, 5 091 frames across the 30-ROM library in 15 ROMs — but **not one gate changed** (193 captures → 193, mean draw rate 0.2454 → 0.2454), so the sweep still reads **437 erased cells in 12 of 36 frames**.

### Why the kind test cannot act

`screen003`'s gate fires on 80 consecutive retained frames while the game types the pre-fight card one letter every four frames; the later frames' diff sets are nested, and every differing cell is shape 97 in the capture, `00…00`, the flat backdrop. To separate the capture from the frame with one letter, a probe must sit on the cell the letter lands on — flat on the capture side. ADR-0050 picks anchors among the "rarest **non-flat** tiles on screen", and `HdPackBuilder::CaptureScreen` drops every flat run before ranking, so the cell never reaches `SelectScreenAnchors` and no fallback reaches for it. This is structural: **the capture side of an addition is empty by definition, and an empty cell is exactly what ADR-0050 excludes.** ADR-0221's "*which it can, since 41 cells differ*" assumed the differing cells were usable as probes; on this card none is. ADR-0221's decision is not wrong, it is inert without this one.

### What the prototype measured

A scratch-only prototype kept the flat runs as candidates, expanded to every cell they cover and marked `Usage = UINT32_MAX` so ADR-0050's rarity pools ignore them; `SelectScreenAnchors`, only when rivals survive the stable and wide passes, ran one more greedy pass over the stable pool plus the flat cells no variant changes, keeping it only if it separated more rivals. Same route, same 36 renders:

| | frames losing content | erased cells | captures | mean draw rate |
|---|---|---|---|---|
| F12.13 as shipped (B) | 12 of 36 | 437 | 10 | 0.1200 |
| B + emptiness probes | 5 of 36 | **141** | 9 | 0.0865 |

`screen003`–`screen006` each gained one probe on the card's flat backdrop (tile `FD` at rows 120, 136, 176, 200) and the 79 nested addition-rivals were separated. The **fight screen** (41–45 s) is not: `screen009` fires with a different gate that still does not separate the frames where the fight fills in, and `screen010` was refused as sharing an earlier capture's gate.

### The fight screen (41–45 s), traced 2026-09-22

Read off the grid dump, the shadow OAM at `$0200` in the retained frames' `M` RAM line, and no-pack overlays: **every "erased" cell in the window is a behind-background sprite** — Glass Joe's shorts and legs, attr bit 5 set (`0x21`/`0x22`; 15 of 20 sprites at 41–43 s, 26 of 26 at 45 s). The five front sprites (attr `0x02`) sit in the one row the tool does not flag. `HdNesPack::GetPixels` draws the priority-20 `<background>` layer *after* the behind-background sprite pass and *before* the front-sprite pass, so a captured screen paints color-0 canvas over those sprites wherever the ROM's background is empty behind them. The frame `screen009` was frozen from (retained 213, ≈43 s) shows the same erasure, so no gate can separate it — the frames are content-for-content variants, zero additions.

- The 44–45 s increase under the prototype is the **same** capture with a more faithful gate. B's shipped gate for `screen009` (`105,72,E5 / 9,16,9C
  / 73,16,A1`) anchors on two of Joe's pose tiles, so it stops firing after 43 s by accident and fires on 230 ratio-rivals elsewhere; the prototype's gate (two flat probes plus `9,16,9C`) fires on all 64 of its variants and 24 rivals. **The number to beat for A is 141 minus the fight window, i.e. the card's 0, not 141.**
- `scripts/measure_capture_overdraw.py` counts a cell "erased" when the render is flat where the ROM had detail, without asking whether the detail was background or sprite; on 44–45 s the capture also overpaints 15 and 28 content-for-content cells the tool never counts. "0 erased" is therefore neither reachable on this route with any gate nor equal to a correct background.

The smallest change that removes the residue is in the **renderer**, not the recorder: draw the priority-20 layer before the behind-background sprite pass, gated on the underlying background pixel being color 0. That is outside ADR-0050's "sprites still draw on top" only in that ADR-0050 assumed they did. Recorded here as the open question, not decided — it changes rendering for every existing pack that uses priority 20–29 layers, community packs included. It became ADR-0224.

Non-goals: no change to `kAnchorVariantAgree`, to ADR-0159's stability filter as the *first* pass, or to ADR-0217/ADR-0218's collision guards; no change to what is captured (ADR-0050's 15-frame stillness rule, numbering, ADR-0156 residency); not about sprites; not a render-time rule (ADR-0221 option D stays unpicked).

## Decision

**A — emptiness probes as a last pass (the prototype, as measured).** Flat cells enter the candidate list with a usage that keeps them out of every rarity pool. `SelectScreenAnchors` keeps its passes as they are; only when rivals still survive does it run one more greedy pass over the stable pool **plus the flat cells no variant changes**, adopting the result only if it separates strictly more rivals than before. The `<condition>` it emits is an ordinary `tileAtPosition` on the flat tile's data and palette — the loader needs no new construct. This amends ADR-0050 ("rarest non-flat tiles on screen" becomes "non-flat, or flat when nothing else separates"; "non-flat" becomes "non-flat, or flat when nothing else separates") and adds a fourth pass to ADR-0159 §1.

A capture still needs **at least one non-flat anchor** (decided at the PR #379 Codex review, 2026-09-23): the probes are a last pass *on top of* ADR-0050's non-flat pool, never a substitute. A screen whose only candidates are flat is not captured, because a gate made only of emptiness probes would fire on every blank screen — worse than not drawing. `CaptureScreen` keeps its early return. "Empty" is one predicate everywhere, `MesenSheets::IsFlatTileData` (each plane all 0x00 or all 0xFF), which at the margin moves a solid color-1/2 tile into the probe pool and lets a 0x55-striped tile back into the rarity ranking; measured: nothing moved on Punch-Out!!, the library gained one probe (58 -> 59).

**ADR-0221's rule stays as shipped**: the kind test is what makes the addition frames rivals in the first place; F12.13's code is the prerequisite of A and B, not a competitor.

Alternatives weighed, not adopted:

- **B — flat cells ranked with everything else.** Flat cells enter at their real rarity and every pass of ADR-0159 §1 may pick them. For: one rule instead of a special last pass. Against: unmeasured; it changes gates for captures already separated today, and ADR-0050's "rarest" was chosen so a probe is unlikely to match another screen by chance — a flat probe is the opposite.
- **C — leave ADR-0050 as it is.** The kind test stays a counter in the log; #339's class of overdraw stays. Against: it makes ADR-0221's accepted decision permanently inert, and ADR-0146 still auto-loads every accepted pack, so the erased text still reaches players.

## Record

- 2026-09-22 — opened `proposed` by the F12.13 measurement, which found ADR-0221 option B cannot change a gate as long as ADR-0050's candidate rule excludes flat cells; the prototype lived in a scratch copy and is not in any diff.
- 2026-09-22 — accepted, option A. User's picks through a structured question, labels verbatim: "A: probes como ultimo passo (Recommended)" and "Depois da F12.15 (Recommended)". Build go-ahead verbatim: "faz o 2 usando o sonnet". Answers to *What a human has to pick*: (1) A; (2) yes — the draw-rate fall is the price of a gate that stops drawing on frames it must not, and the slice publishes the library-wide number; (3) no — the fight-screen residue is a different mechanism, decided in ADR-0224, so it does not block this one; (4) the stop condition is restated on ADR-0224 §4's split overdraw tool: `erased background` = 0 on the card (19–27 s), the fight window's `erased sprite` column not counted against this slice, library re-recorded with capture count, draw rate and never-firing count published before/after, and unit tests on a synthetic pair where only a flat cell separates capture from rival; (5) the render-path ADR is ADR-0224.
- 2026-09-22 — shipped as F12.16 (log `../../validation/slices/f12.16-emptiness-probes-2026-09-22.md`). All three stop conditions met: the Punch-Out!! card reads `erased background` = 0 on all 36 sweep points (374 before), the synthetic-pair unit cases pass (990/990), and the 30-ROM library was re-recorded — captures 193 -> 219, mean draw rate 0.2454 -> 0.2316, never-firing 0 -> 0, 59 screens gated on an emptiness probe in 9 ROMs, 14 packs with a changed `<condition>` line (after the Codex-review predicate fix; 58/13 before it).

## Consequences

- **A changes what recorded packs look like:** packs before and after differ in their `<condition>` lines, as after ADR-0217/ADR-0218; the library re-record is the cost, and `scripts/measure_capture_draw_rate.py` (F12.13) prices it without a re-render. The draw rate on Punch-Out!! fell 0.1200 → 0.0865 — a capture now refuses to draw on more frames — the trade ADR-0159 §1 already accepts ("an anchor that sometimes fails to draw beats one that draws the wrong screen").
- **A probe on a flat cell reads as odd to a human** who opens `hires.txt`: a condition naming an all-zero tile. The kit's `ARTIST.md` should say what it is, or the artist will delete it as noise.
- **The fight screen is not closed by this ADR, and cannot be.** No recorder-side rule reaches the behind-background-sprite overpaint; ADR-0221's non-goal on sprites applies, and the fix is the render-path ordering change (ADR-0224). `scripts/measure_capture_overdraw.py` keeps counting sprite loss as overdraw, so its total cannot reach 0 on this route with any gate; the card's 0 is the figure A is judged on, and the tool should learn to split background loss from sprite loss before it prices another gate rule.
- **Issue #339 has two causes, not one:** the card is the gate problem ADR-0221 named and A addresses; the fight is the sprite-pass ordering above. Closing the first does not close the second.
