# ADR-0223: A flat cell may be an anchor when it is the only thing that separates a capture from an addition-rival ("emptiness probes")

- Status: **accepted 2026-09-22 — option A, shipped the same day as PRD Part A
  F12.16** ([log](../validation/f12.16-emptiness-probes-2026-09-22.md)),
  after F12.15 as sequenced. Build go-ahead verbatim: *"faz o 2 usando o
  sonnet"* (implemented and independently verified by Sonnet, by the user's
  choice for this slice). All three stop conditions met: the Punch-Out!! card
  reads `erased background` = 0 on all 36 sweep points (374 before), the
  synthetic-pair unit cases pass (990/990), and the 30-ROM library was
  re-recorded — captures 193 -> 219, mean draw rate 0.2454 -> 0.2316, never-
  firing 0 -> 0, 59 screens gated on an emptiness probe in 9 ROMs, 14 packs
  with a changed `<condition>` line (after the Codex-review predicate fix;
  58/13 before it). One placement note: the flat-cell
  expansion (`AppendFlatAnchorCells`) lives inline in `HdPackBuilder.h`
  because `HdPackBuilder.cpp` sits on its ADR-0137 ceiling. Original record: User's picks through a structured question,
  labels verbatim: *"A: probes como ultimo passo (Recommended)"* and
  *"Depois da F12.15 (Recommended)"*. Answers to *What a human has to pick*:
  (1) A; (2) yes — the draw-rate fall is the price of a gate that stops
  drawing on frames it must not, and the slice publishes the library-wide
  number; (3) no — the fight-screen residue is a different mechanism, decided
  in ADR-0224 (render path, opt-in tag), so it does not block this one;
  (4) the stop condition is restated on the split overdraw tool ADR-0224 §4
  delivers: **`erased background` = 0 on the card (19–27 s)**, the fight
  window's `erased sprite` column not counted against this slice, library
  re-recorded with capture count, draw rate and never-firing count published
  before/after, and unit tests on a synthetic pair where only a flat cell
  separates capture from rival; (5) the render-path ADR is ADR-0224. No
  go-ahead to build was sought or given. Opened `proposed` the same day by
  the F12.13 measurement, which found that ADR-0221 option B cannot change a
  gate as long as ADR-0050's candidate rule excludes flat cells; the
  prototype lived in a scratch copy and is not in any diff.
- Date: 2026-09-22
- Related: ADR-0221 (accepted 2026-09-22, option B — the kind test this ADR
  gives teeth to), ADR-0050 (bootstrap `<background>` capture; the "rarest
  **non-flat** tiles" candidate clause), ADR-0159 (anchors chosen at save
  time; §1 stability filter, §4 constants), ADR-0217/ADR-0218 (gate
  collisions, upstream of this and unaffected), ADR-0156 (a captured screen
  owns the cells it covers — not amended here), issue #339, PRD Part A slice
  F12.13 and its log `docs/validation/f12.13-variant-kind-rule-2026-09-22.md`,
  `docs/validation/adr0221-capture-overdraw-harness-2026-09-20.md`,
  `scripts/measure_capture_overdraw.py`, `scripts/measure_capture_draw_rate.py`.
- Supersedes / amends: nothing yet. Option A below amends ADR-0050 §Decision
  ("rarest non-flat tiles on screen") and adds a fourth pass to ADR-0159 §1;
  option B amends ADR-0050 the same way and ADR-0159 §1 more deeply. The
  amendment is written when an option is picked.

## Context

ADR-0221 decided (option B) that a frame which draws content into a cell the
capture holds empty is a *rival*, whatever its agreement ratio, so that the
discrimination pass separates it. F12.13 implemented that exactly, and the
measurement in `docs/validation/f12.13-variant-kind-rule-2026-09-22.md` says
what happened:

- the kind test **fired** — on Punch-Out!! the recorder's summary reads
  `228 frame(s) filed as rivals for adding content the capture lacks`, and
  5 091 frames across the 30-ROM library, in 15 ROMs;
- **not one gate changed** — the 30 `<condition>` lines of the Punch-Out!!
  pack are byte-identical before and after, and so are the gates of all 30
  library packs (193 captures → 193, mean draw rate 0.2454 → 0.2454);
- the sweep therefore still reads **437 erased cells in 12 of 36 frames**,
  exactly ADR-0221's starting number.

### Why the kind test cannot act

Read off the grid dump: `screen003`'s gate fires on 80 consecutive retained
frames while the game types the pre-fight card one letter every four frames.
Against the captured frame each later frame differs on one more cell than the
one before, and the diff sets are nested. Every one of those cells is shape 97
in the capture, `00…00`, the flat backdrop. To separate the capture from the
frame with one letter, a probe must sit on **the cell the letter lands on** —
no other cell differs. That cell is flat on the capture side.

ADR-0050 picks anchors among the "rarest **non-flat** tiles on screen", and
`HdPackBuilder::CaptureScreen` drops every flat run before ranking, so the
cell never reaches the candidate list, never reaches
`SelectScreenAnchors`, and no fallback inside the stitcher can reach for it.

This is structural. **The capture side of an addition is empty by
definition, and an empty cell is exactly what ADR-0050 excludes.** An
addition-only rival can never be separated under ADR-0050's clause, whatever
ADR-0221 says about its kind. ADR-0221's own sentence — "*which it can, since
41 cells differ*" — assumed the differing cells were usable as probes; on this
card none is. ADR-0221's decision is not wrong, it is inert without this one.

### What the prototype measured

A scratch-only prototype kept the flat runs as candidates, expanded to every
cell they cover and marked `Usage = UINT32_MAX` so ADR-0050's rarity pools
ignore them; `SelectScreenAnchors`, only when rivals survive the stable and
wide passes, ran one more greedy pass over the stable pool plus the flat cells
no variant changes, and kept that result only if it separated more rivals.
Same route, same 36 renders:

| | frames losing content | erased cells | 19–27 s (card) | 41–43 s | 44–45 s | captures | mean draw rate |
|---|---|---|---|---|---|---|---|
| F12.13 as shipped (B) | 12 of 36 | 437 | 10 → 71 | 18, 18, 27 | 0, 0 | 10 | 0.1200 |
| B + emptiness probes | 5 of 36 | **141** | **0** | 18, 18, 27 | 34, 44 | 9 | 0.0865 |

`screen003`–`screen006` each gained one probe on the card's flat backdrop
(tile `FD` at rows 120, 136, 176, 200) and the 79 nested addition-rivals were
all separated: the card is exactly what the diagnosis says. The **fight
screen** (41–45 s) is not: `screen009` now fires across the window with a
different gate that still does not separate the frames where the fight fills
in, and `screen010` was refused as sharing an earlier capture's gate. Why the
fight screen resists where the card yields was traced the same day; the
answer is below, and it is not a gate problem.

### The fight screen (41–45 s), traced 2026-09-22

Per frame, read off the grid dump, the shadow OAM at `$0200` in the retained
frames' `M` RAM line, and overlays of the render against the no-pack
screenshot:

| s | gate firing (B / B+probes) | cells differing from `screen009` | additions | kind | erased (B / B+probes) | erased on ROM background detail | erased on sprite-only detail |
|---|---|---|---|---|---|---|---|
| 41 | screen009 / screen009 | 15 (agree 0.984) | 0 | variant | 18 / 18 | 0 | 18 |
| 42 | screen009 / screen009 | 12 (0.988) | 0 | variant | 18 / 18 | 0 | 18 |
| 43 | screen009 / screen009 | 2 (0.998) | 0 | variant | 27 / 27 | 0 | 27 |
| 44 | none / screen009 | 15 (0.984) | 0 | variant | 0 / 34 | 0 | 34 |
| 45 | none / screen009 | 28 (0.971) | 0 | variant | 0 / 44 | 0 | 44 |

**Every "erased" cell in the window is a behind-background sprite** — Glass
Joe's shorts and legs, OAM attribute bit 5 set (attr `0x21`/`0x22`, 15 of 20
sprites in the erased boxes at 41–43 s, 26 of 26 at 45 s). The five front
sprites (attr `0x02`, the waistband row) sit in the one row the tool does not
flag. `HdNesPack::GetPixels` draws the priority-20 `<background>` layer
*after* the behind-background sprite pass and *before* the front-sprite pass,
so a captured screen — which never contains sprites of either priority —
paints colour-0 canvas over those sprites wherever the ROM's background is
empty behind them. The frame `screen009` was frozen from (retained 213, ≈43 s)
shows the same erasure, so no gate can separate the capture from it: the
frames are genuine content-for-content variants, zero additions, and the
kind test is right to keep them.

Two consequences for the numbers above:

- The 44–45 s increase under the prototype is the **same** capture with a
  more faithful gate. B's shipped gate for `screen009` (`105,72,E5 / 9,16,9C
  / 73,16,A1`) anchors on two of Joe's pose tiles, so it stops firing after
  43 s by accident and fires on 230 ratio-rivals elsewhere in the fight; the
  prototype's gate (two flat probes plus `9,16,9C`) fires on all 64 of its
  variants and 24 rivals. The tool rewards the gate that misses its own
  variants. **The number to beat for A is 141 minus the fight window, i.e.
  the card's 0, not 141.**
- `scripts/measure_capture_overdraw.py` counts a cell "erased" when the
  render is flat where the ROM had detail, without asking whether the detail
  was background or sprite; on 44–45 s the capture also overpaints 15 and 28
  content-for-content cells (Joe's frozen pose over his live pose) that the
  tool never counts, because the render is not flat there. "0 erased" is
  therefore neither reachable on this route with any gate nor equal to a
  correct background.

The smallest change that removes the residue is in the **renderer**, not the
recorder: draw the priority-20 layer before the behind-background sprite
pass, gated on the underlying background pixel being colour 0 (what the
hardware does), so a behind-background sprite over empty canvas is never
hidden by a captured screen. That is outside this ADR's non-goals (it
touches the render path ADR-0221 left untouched and ADR-0156's layer
ordering) and outside ADR-0050's "sprites still draw on top" only in the
sense that ADR-0050 assumed they did. It is recorded here as the open
question, not decided: it changes rendering for every existing pack that
uses priority 20–29 layers, community packs included.

### Non-goals

- Not a change to `kAnchorVariantAgree`, to ADR-0159's stability filter as
  the *first* pass, or to ADR-0217/ADR-0218's collision guards.
- Not a change to what is captured (ADR-0050's 15-frame stillness rule,
  numbering, ADR-0156 residency).
- Not about sprites: a `<background>` never covers them, and a frame that
  differs from its capture only under sprites is a different problem.
- Not a render-time rule (ADR-0221 option D stays unpicked).

## Decision

**Open.** The question is: *may a probe sit on an empty cell, and when?*

### A. Emptiness probes as a last pass (the prototype, as measured)

Flat cells enter the candidate list with a usage that keeps them out of every
rarity pool. `SelectScreenAnchors` keeps its passes as they are; only when
rivals still survive does it run one more greedy pass over the stable pool
**plus the flat cells no variant changes**, and adopts the result only if it
separates strictly more rivals than before. The `<condition>` it emits is an
ordinary `tileAtPosition` on the flat tile's data and palette — the loader
needs no new construct.

- **For:** measured — 437 → 141, the card to 0, nine of ten captures kept,
  no change for any capture whose rivals were already separated (the pass
  never runs for them). Smallest possible amendment to ADR-0050: "non-flat"
  becomes "non-flat, or flat when nothing else separates".
- **Against:** a probe on a flat cell is a probe on *absence*, and absence is
  common: the same flat tile sits on hundreds of cells, so a flat probe is
  only as discriminating as its position. The draw rate on Punch-Out!! fell
  0.1200 → 0.0865 — a capture now refuses to draw on more frames — which is
  the trade ADR-0159 §1 already accepts ("an anchor that sometimes fails to
  draw beats one that draws the wrong screen"), but it has to be said.

### B. Emptiness probes ranked with everything else

Flat cells enter the candidate list at their real rarity (a flat tile that
covers half the screen ranks last; a flat cell in a row of glyphs ranks
higher) and every pass of ADR-0159 §1 may pick them.

- **For:** one rule instead of a special last pass; a flat cell that is
  genuinely rare on a screen is treated as the evidence it is.
- **Against:** unmeasured. It changes gates for captures that are already
  separated today, so it needs the full library re-record to say what it
  costs; and ADR-0050's "rarest" was chosen so that a probe is unlikely to
  match another screen by chance — a flat probe is the opposite of unlikely.

### C. Leave ADR-0050 as it is

The kind test stays a counter in the log; #339's class of overdraw stays.

- **For:** nothing to build; the draw rate does not fall.
- **Against:** it makes ADR-0221's accepted decision permanently inert, and
  ADR-0146 still auto-loads every accepted pack, so the erased text still
  reaches players.

## What a human has to pick

1. **A, B or C.** A is measured, B is not, C undoes ADR-0221 in effect.
2. **Is a fall in draw rate acceptable for a rise in correctness?** On
   Punch-Out!! A costs 0.1200 → 0.0865. The library-wide number is the
   slice's job to publish.
3. **Does the fight-screen residue (41–45 s) block acceptance?** It is a
   different mechanism and may need its own ADR; the trace decides.
4. **Stop condition for the slice.** Natural: the Punch-Out!! sweep at
   **141 or fewer** erased cells with the card at 0 (A's measured floor), the
   library re-recorded with capture count, draw rate and never-firing count
   published before/after, and unit tests on a synthetic pair where only a
   flat cell separates capture from rival.
5. Whether to open an ADR for the render-path ordering (priority-20 layer
   before the behind-background sprite pass, gated on colour 0) that the fight
   screen trace points at — the only thing that closes #339's second half.
   **Answered 2026-09-22: ADR-0224.**

## Consequences

- **A capture still needs at least one non-flat anchor** (decided at the
  PR #379 Codex review, 2026-09-23): the probes are a last pass *on top of*
  ADR-0050's non-flat pool, never a substitute for it. A screen whose only
  candidates are flat is not captured, because a gate made only of emptiness
  probes would fire on every blank screen — worse than not drawing.
  `CaptureScreen` keeps its early return and says so. "Empty" is one
  predicate everywhere, `MesenSheets::IsFlatTileData` (each plane all 0x00 or
  all 0xFF), which at the margin moves a solid colour-1/2 tile into the probe
  pool and lets a 0x55-striped tile back into the rarity ranking; measured:
  nothing moved on Punch-Out!!, the library gained one probe (58 -> 59).
- **Whatever is picked, ADR-0221's rule stays as shipped**: the kind test is
  what makes the addition frames rivals in the first place. F12.13's code is
  the prerequisite of A and B, not a competitor.
- **A changes what recorded packs look like**: packs recorded before and
  after differ in their `<condition>` lines, as after ADR-0217/ADR-0218; the
  library re-record is the cost, and `scripts/measure_capture_draw_rate.py`
  (F12.13) is the tool that prices it without a re-render.
- **A probe on a flat cell reads as odd to a human** who opens `hires.txt`:
  a condition naming an all-zero tile. The kit's `ARTIST.md` should say what
  it is, or the artist will delete it as noise.
- **The fight screen is not closed by this ADR, and cannot be.** The trace
  showed the 41–45 s residue is behind-background sprites overpainted by the
  priority-20 layer in `HdNesPack::GetPixels`; no recorder-side rule reaches
  it, ADR-0221's non-goal on sprites applies, and the fix — if wanted — is a
  render-path ordering change that needs its own ADR. Until then
  `scripts/measure_capture_overdraw.py` keeps counting sprite loss as
  overdraw, so its total cannot reach 0 on this route with any gate; the
  card's 0 is the figure A is judged on, and the tool should learn to split
  background loss from sprite loss before it prices another gate rule.
- **Issue #339 has two causes, not one**: the card is the gate problem
  ADR-0221 named and A/B address; the fight is the sprite-pass ordering
  above. Closing the first does not close the issue.
