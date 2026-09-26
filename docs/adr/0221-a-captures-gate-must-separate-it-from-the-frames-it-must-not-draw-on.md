# ADR-0221: A capture's gate must separate it from the frames it must not draw on, not only from the other captures

- Status: **accepted 2026-09-22 — option B, shipped the same day as PRD Part A
  F12.13** ([log](../validation/f12.13-variant-kind-rule-2026-09-22.md)),
  go-ahead verbatim *"dispara as frentes 1, 2, 3 e 4 em paralelo usando
  workflows"*, then *"Mergear + abrir ADR-0223 (Recommended)"* once the result
  was in. Stop conditions (2) and (3) met; **(1) not met** — the Punch-Out!!
  sweep still erases 437 cells, because the kind test files the frames as
  rivals (228 of them) and the search cannot separate them: the only
  discriminating cell is flat on the capture side, and ADR-0050 excludes flat
  cells from the anchor pool. Issue #339 stays open; the follow-up decision is
  ADR-0223 (`proposed`). Original acceptance record: the user picked
  it through a structured question whose labels are recorded verbatim: *"B
  sozinha (Recommended)"* for the option, and *"Não, só a ADR"* for the
  go-ahead — so this is a decision and a request for work, not a same-turn
  implementation. The pending slice is PRD Part A **F12.13**. Answers to *What
  a human has to pick*: (1) B alone — no D; (2) ADR-0159's "a capture owns its
  variants" survives, **narrowed** by the definition under "Decided" below;
  (3) correctness at render time — ADR-0146 auto-loads every accepted pack, so
  #339 is player-visible, which is what rules E out; (4) moot, ADR-0156 is not
  amended; (5) the re-record of the 30-ROM bounded library (~15 min) is
  required, since B changes what the recorder writes and a synthetic unit test
  cannot see #339. Proposed 2026-09-20 with the five options below, which stay
  in the file as the record of what was weighed.
- Date: 2026-09-20
- Related: issue #339, ADR-0217 and ADR-0218 (the change that closed gate
  *collisions* and did not close #339), ADR-0159 (anchors chosen at save time
  from cells variants do not change and rivals do not share), ADR-0156 (a
  captured screen owns the cells it covers), ADR-0050 (bootstrap `<background>`
  capture), ADR-0183 §3 (the recorder emits observations, never readings),
  PRD Part A §3, `docs/validation/adr0217-0218-anchor-gate-collisions-2026-09-20.md`,
  `docs/validation/adr0221-capture-overdraw-harness-2026-09-20.md`
- Supersedes / amends: ADR-0159 §1, narrowed — a frame is a *variant* of a
  captured screen only when every cell it changes is a cell whose content the
  capture already carries; a frame that adds content the capture lacks is a
  *rival* regardless of agreement ratio. ADR-0156 §Decision is **not** amended
  (option D was not picked here; ADR-0236 picked it for #499 on 2026-09-25, as an opt-in guard keyed by a new per-cell record). ADR-0159 carries the matching "Amended 2026-09-22"
  note.

## Context

ADR-0217 and ADR-0218 shipped on 2026-09-20 and were measured the same day.
They do what they say: across the 30-ROM bounded library, **193 captures, 0
sharing a gate**, against 113 of 237 before. Issue #339 is nonetheless
unchanged, and the measurement says why.

### The reproduction

Mike Tyson's Punch-Out!! rendered from power-on at 1 s steps from 10 s to 45 s,
three ways — no pack, a pre-change pack, a post-change pack. The two packs are
route-matched: same 60 s recording, same 11 606 `<tile>` keys, differing only in
their capture gates. **They render identically at all 36 timestamps.** Between
18 s and 27 s the game draws the pre-fight card:

| measurement | pixels |
|---|---|
| the game's own frame vs the capture that draws (`screen003`) | 23 664 |
| the rendered frame vs the game's own frame | 12 686 |
| `STARRING` / `LITTLE MAC` pixels the ROM draws | **7 808** |
| the same pixels in the rendered frame | **0** |

`screen003` was frozen at an earlier moment of the same card — Little Mac alone,
Doc Louis's head still an undrawn white block, no text. It draws over the live
background on the later frame and the game's own text is gone. Doc Louis
survives only because he is sprites, which a `<background>` does not cover.

### The root cause is a rule working as written, not a bug

The two frames agree on **919 of 960 tile cells — 0.9573**.
`kAnchorVariantAgree` in `Core/NES/HdPacks/TileSheetTypes.h` is **0.90**, so the
later frame is classified a **variant** of `screen003`, not a rival. ADR-0159 §1
then does exactly what it promises: among the candidate cells, it keeps the ones
**no variant of this screen changes**. The 41 cells that hold the text are
therefore excluded from the anchor pool *by construction*, and the resulting
gate is guaranteed to match the frame with the text.

ADR-0217's answer (1) kept this deliberately — *"a capture keeps owning its
variants — ADR-0159's rule stands"*. That clause is what keeps #339 alive.

So the two ADRs' title is precise and so is their limit: *"separates it from
**every other capture**"*. `screen003`'s gate does separate it from every other
capture. **Distinct from the other captures is not the same as sufficient to
identify the frame.** Those ADRs separate captures from each other; this one is
about separating a capture from *frames*.

### Non-goals

- **Not a change to what is captured.** `CaptureScreen`'s trigger, numbering and
  ADR-0156 residency tie stay as they are under every option below.
- **Not importing anyone's reading of the machine.** A `memoryCheck` would
  separate these frames trivially and ADR-0183 §3 forbids it: this recorder
  emits observations, never readings. Every option here is decided from frames
  the recorder actually saw.
- **Not a re-opening of gate collisions.** ADR-0217/ADR-0218 stand; the
  library-wide 0 is a real result and none of the options below give it back.

## Decision

### Decided 2026-09-22: option B

**A frame is a variant of a captured screen only when every cell it changes is
a cell the capture's own art already carries. A frame that adds content the
capture does not have is a rival, whatever the agreement ratio.**

Concretely, at save time in `MesenSheets::SelectScreenAnchors` (ADR-0159 §3),
for each retained frame at the same `FineX` that today clears
`kAnchorVariantAgree`: walk its changed cells against the captured frame. A
cell whose captured content is **empty** — the shape id of a flat tile, the
same "single flat colour per 8×8 cell" the acceptance tool already scores —
and whose frame content is **non-empty** is an *addition*. One addition makes
the frame a rival; a frame whose every changed cell is non-empty on both sides
(animation, a score digit, the other half of a blink) stays a variant. The
threshold constant is untouched and `kAnchorVariantAgree` keeps its measured
meaning; the kind test runs *after* it, so nothing that was a rival becomes a
variant.

- Why "empty on the capture side" and not a pixel diff: the capture is a
  frozen PNG of the whole screen, so it *covers* every cell by construction;
  the destructive case is the cell it covers with nothing (#339's white block
  under `STARRING`). The repainted-pack objection under B dissolves the same
  way: the classification is made once, at record time, from the recorded
  grid — a later repaint changes no gate, and the frame with the text, now a
  rival, is captured on its own when it holds still, so the artist paints both.
- Stop condition for F12.13: `scripts/measure_capture_overdraw.py --sweep` on
  a re-recorded Punch-Out!! reports **0 erased cells** across the 36 frames,
  including the 41–43 s occurrence; and the library-wide re-record publishes
  capture count **and** per-capture draw rate before/after, so the cost C
  would have hidden is visible here. Unit tests cover the kind test on a
  synthetic `GridFrame` pair (addition → rival; content-for-content → variant;
  ratio below threshold → rival as before).

### The options as proposed 2026-09-20

The question was: *when may a capture own a variant?* Owning a variant
means drawing frozen art over a frame that differs from it. That is harmless
when the difference is art the capture also carries, and destructive when the
difference is background art the capture lacks — which is #339 exactly.

Five options, each stated concretely enough to implement and to verify:

### A. Raise `kAnchorVariantAgree`

One constant. At a threshold above 0.9573 the pre-fight frame becomes a rival
and ADR-0159 §1's existing discrimination pass has to separate it.

- **For:** smallest possible change; no new mechanism; ADR-0159 already
  publishes the sensitivity curve, so the trade is a known one.
- **Against:** it is a proportion, and #339 is not about proportion. 41 changed
  cells is a *large* variant by any reading and it still passed; a screen whose
  only difference is a two-cell score digit would become a rival too, which is
  the false-match direction ADR-0159 measured and rejected. It also guesses: the
  next game's destructive difference may be 5 cells.

### B. Classify a variant by the *kind* of difference, not by its size

A frame is a variant only when every cell it changes is a cell the capture's own
art already covers. A frame that **adds background content the capture does not
have** is a rival regardless of how small it is.

- **For:** targets #339 at its cause. The text is content the capture lacks, so
  the frame becomes a rival and the discrimination pass must find a probe that
  separates them — which it can, since 41 cells differ.
- **Against:** needs a per-cell "does the capture cover this" comparison at save
  time and a definition of *covers* that survives a repainted pack (the artist
  may paint the text in later, at which point the frame **is** a variant). The
  definition is the hard part, not the code.

### C. Require one discriminating probe

Keep variant classification as it is, but require that at least one of the three
anchors sits on a cell that **does** change across the variant set — the exact
opposite of ADR-0159 §1's stability filter, applied to one probe instead of all
three. The capture then draws only on the variant it was frozen for.

- **For:** cheap, local, and it inverts a rule whose failure mode is now
  measured. Fits the existing `SelectScreenAnchors` signature.
- **Against:** it revokes "a capture owns its variants" wholesale rather than
  case by case — every variant becomes a separate frame the capture will not
  draw on, so a pack that used to cover a whole blink cycle with one capture now
  covers one phase of it. The capture count does not drop (nothing is refused)
  but the *drawing* rate does, and nobody has measured by how much.

### D. Refuse at draw time, not at save time

Leave the recorder alone. In `HdNesPack`, a `<background>` never overwrites a
live background cell whose content the capture does not carry — the capture
loses those cells to the game's own `<tile>` rules and keeps the rest.

- **For:** the only option that fixes existing packs, including the community
  ones in `docs/community-packs.json`, without re-recording anything. It is also
  the only one whose guarantee is about what the player sees rather than about
  what the recorder guessed.
- **Against:** it amends ADR-0156's "a captured screen owns the cells it covers"
  — the load-bearing clause of the whole capture design — and it puts a per-cell
  comparison in the render hot path. It also changes the meaning of a pack an
  artist already painted: cells they deliberately blanked would start showing
  the game again.

### E. Accept it, and say so where the artist reads it

Change nothing in the Core. Record in the kit (`ARTIST.md`) and in `mep_lint`
that a capture may draw over frames it was not frozen for, name the capture and
the frames, and leave the fix to the artist's own repaint.

- **For:** honest to what this pipeline is. ADR-0183 frames the output as
  *material for an artist*, not a shipped renderer, and every option above
  spends Core complexity on a surface a human is expected to repaint anyway.
- **Against:** #339 is filed as **player-visible**, not artist-workflow — a
  pack the auto-installer ships (ADR-0146 auto-loads every accepted pack) erases
  text in a game the player is playing. Under that framing E is not an answer.

## What a human has to pick

1. **Which option, or which combination?** B and D are not exclusive — B fixes
   what we record from now on, D fixes what is already out there. A is the
   cheap partial; C is the blunt one; E is the refusal.
2. **Does ADR-0159's "a capture owns its variants" survive?** ADR-0217 answer
   (1) kept it on 2026-09-20 (*"Manter (captura vale p/ variantes)"*), before
   this measurement existed. B narrows it, C revokes it, D routes around it.
   This is the same question, asked again with the number attached.
3. **Is the target correctness at render time, or visibility to the artist?**
   #339 says player-visible; ADR-0183 says material. The answer decides whether
   E is on the table at all.
4. **If D: does it amend ADR-0156?** "A captured screen owns the cells it
   covers" would become "…owns the cells it covers and whose content it
   carries". That is a real narrowing of a rule other decisions lean on.
5. **What is the measurement budget?** Re-recording the 30-ROM library is
   ~15 minutes of wall clock and is what produced every number above; a
   synthetic-`GridFrame` unit test is seconds but cannot see #339. Scoring a
   sweep is now free (`scripts/measure_capture_overdraw.py`, added 2026-09-20),
   so the cost is the recording, not the reading. The stop condition for
   whichever option wins should say which it requires.

## Consequences

- **Whatever is picked, the acceptance test is the same and it is now a
  tool**: `scripts/measure_capture_overdraw.py --sweep <dir>` renders
  Punch-Out!! at 1 s steps from 10 s to 45 s and counts, per 8×8 cell, what the
  ROM draws and the render does not. Today it reports **12 of 36 frames losing
  content, 437 erased cells**, identical before and after ADR-0217/ADR-0218.
  Any option that does not move that number has not closed #339. The tool is
  decision-neutral — it measures, it does not pick an option — and its exit
  code is the verdict, so a sweep can gate whichever fix wins.
- **There is a second occurrence, at 41–43 s**, that the hand measurement
  missed: 18, 18 and 27 erased cells after the pre-fight card is long gone. Any
  option that fixes only the card has not finished the job. See
  `docs/validation/adr0221-capture-overdraw-harness-2026-09-20.md`; attributing
  it to a capture still needs the recorder's anchor summary.
- **Two measurement traps are now known and must be honoured** by anyone
  re-running this; the tool enforces both rather than leaving them to the
  operator. A pack installed at `mesen-home/HdPacks/<stem>/` is *not* found by
  `scripts/headless_record` — the log says `LoadHdPack: 0 ms; no-pack` and the
  screenshot comes back at native 256×240, so a pixel diff reads a misleading
  zero; the sibling convention `<romdir>/<stem>/auto` loads it. And comparing a
  rendered pack frame against a nearest-neighbour upscale of the no-pack frame
  measures the upscale, not correctness — which is why the metric is per cell.
- **Doing nothing has a cost that is not zero.** ADR-0146 auto-installs every
  accepted community pack, so a capture that erases live art reaches players
  without anyone opting in. That is the argument against E and it should be
  weighed as such, not left implicit.
- **The options are not equally reversible.** A is a constant. C is a rule in
  `SelectScreenAnchors`. B changes what the recorder writes, so packs recorded
  before and after differ and neither can be regenerated from the other without
  the original ROM session. D changes what existing packs draw, which is the
  only option that can make a pack an artist already painted look different.
