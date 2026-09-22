# ADR-0223: A flat cell may be an anchor when it is the only thing that separates a capture from an addition-rival ("emptiness probes")

- Status: **proposed** (2026-09-22). Opened by the F12.13 measurement, which
  found that ADR-0221 option B cannot change a gate as long as ADR-0050's
  candidate rule excludes flat cells. The Decision below is an either/or —
  two options for the candidate clause, with the prototype's numbers attached
  — and stays `proposed` until a human picks; see "What a human has to pick".
  Nothing here is implemented: the prototype lived in a scratch copy and is
  not in any diff.
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
fight screen resists where the card yields is being traced separately; this
section is amended with the answer when it lands.

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

## Consequences

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
- **The fight screen is not closed by this ADR.** If the trace shows the
  difference is under sprites, no background rule closes it and ADR-0221's
  non-goal on sprites applies; the sweep tool will keep reporting it and the
  number to beat is 141, not 0, until that is decided.
