# ADR-0224: A recorded screen does not hide a behind-background sprite over empty canvas — opt-in per pack

- Status: **accepted 2026-09-22 — shipped the same day as PRD Part A F12.15**
  ([log](../validation/f12.15-behind-bg-sprites-2026-09-22.md)). User's
  decisions through structured questions, labels verbatim: *"Opt-in por pack
  (Recommended)"* for the scope, *"Sim, na mesma fatia (Recommended)"* for
  splitting the overdraw tool's count, *"So a ADR agora (Recommended)"* for
  the timing, and *"Tag nova em hires.txt (Recommended)"* for where the opt-in
  lives. Build go-ahead verbatim: *"libera a F12.15, dispara as três partes em
  paralelo. mergea o PR assim que puder e garante que t  tudo na main."*
  Three parallel worktrees (Core, tool, docs), Sonnet verification before the
  PR. Stop conditions (1), (3), (4) met; **(2) partially met** — the
  predicate, parse and writer have unit cases, `HdNesPack::GetPixels` has no
  direct unit test (outside the `core-unit-tests` link set), so the tag-on
  render path rests on the headless renders (0 `erased sprite` on 41–45 s,
  36/36 + 9/9 byte-identical without the tag). §2's "equivalently" form is
  what shipped (`DrawBehindBgSprites` re-applied after layer 2 when
  `HdBehindBgSpriteRule::KeepsBehindBgSprite` holds). **Amended
  2026-09-22** (same day, user's choice verbatim: *"Tudo junto com a F12.16
  (Recommended)"*): the layer-3 edge the Sonnet verification left open is
  now a declared edge (§Amended below) pinned by a BlocoP4 model case;
  Decision 5's docs are written; the "measure on the 30-ROM library" clause
  under Consequences is assigned to F12.16's re-recording, not to F12.15.
- Date: 2026-09-22
- Related: issue #339 (its second cause), ADR-0223 (the first cause — the
  card's addition-rivals; this ADR closes what ADR-0223 says it cannot),
  ADR-0221 (whose render-path non-goal this ADR takes up), ADR-0050
  (bootstrap `<background>` at priority 20, "the screen replaces the tiles and
  sprites still draw on top"), ADR-0156 (a captured screen owns the cells it
  covers), ADR-0004 (the hires.txt extension is a draft, community-reviewed),
  ADR-0005 (MEP `textures/` delegates to `HdPackLoader`), ADR-0146 (every
  accepted community pack auto-loads, so a global render change reaches every
  player), issue #328 (tile-suppression tracking in the same function),
  `docs/validation/f12.13-variant-kind-rule-2026-09-22.md` (the trace, under
  ADR-0223 "The fight screen").
- Supersedes / amends: amends ADR-0050 §Decision's sentence "sprites still
  draw on top" — true for front sprites, false for behind-background sprites
  until this ADR's tag is present. Amends ADR-0221's non-goal "the render path
  is untouched" for F12.15 only.

## Context

The F12.13 trace (ADR-0223, "The fight screen") read the shadow OAM off the
retained frames of the Punch-Out!! route and found that **every** cell
`scripts/measure_capture_overdraw.py` counts as erased between 41 s and 45 s
is a sprite with OAM attribute bit 5 set — a behind-background sprite, Glass
Joe's shorts and legs — and none sits on ROM background detail. The captured
screen's gate is right on those frames (genuine variants, zero additions, the
capture's own source frame shows the same loss), so no recorder-side rule can
reach it.

The cause is draw order. `HdNesPack::GetPixels` paints, in this order: the
backdrop colour; layer 0 (priority 0–9); **behind-background sprites**; layer 1
(10–19); the tile (`<tile>` rule or original pixels); layer 2 (priority 20–29,
`BehindFgSpritesPriority`); **front sprites**; layer 3 (30–39). A recorded
`<background>` sits in layer 2 by ADR-0050's decision, so it lands on top of
every behind-background sprite. On hardware a behind-background sprite is
hidden only where the background pixel is opaque (colour index ≠ 0) and shows
wherever the background is transparent. A captured screen is rebuilt from the
background tiles alone and never contains a sprite of either priority; where
the ROM's background is colour 0 it carries the backdrop colour, and painting
that over a sprite the hardware would show is a loss the artist cannot repair
from inside the pack.

This is upstream Mesen's contract, not a MesenAI regression: the layer is
*named* "behind foreground sprites", i.e. above background-priority sprites,
and HD Mesen packs have been painted against that order for years. Changing
it for every pack would alter the look of community packs whose authors never
asked (ADR-0146 loads them all, so the change would be player-visible
everywhere). That is what rules out a global fix and an opt-out default.

The overdraw tool compounds the problem: it scores a cell "erased" when the
render is flat where the ROM had detail, without asking whether the detail
was background or sprite, and never counts content-over-content overpaint
(the capture's frozen pose over the live one). Its total therefore cannot
reach 0 on this route under any gate, and it has already misled once —
ADR-0221's stop condition "0 erased cells" was written against it.

Non-goals: not a change to the recorder's capture or gate rules (ADR-0050,
ADR-0159, ADR-0221, ADR-0223 stay where they are); not a change to layers 0,
1 or 3, to `<tile>` rules, or to front-sprite drawing; not a change for GB/SMS
(`HdNesPack` is NES-only; the GB/SMS renderers have their own priority
model, ADR-0036/ADR-0037); not a change to any existing pack's rendering
unless it opts in.

## Decision

1. **A new hires.txt tag, MesenAI extension, opt-in.** A pack that carries
   the line

   ```
   <bgPreservesBehindBgSprites>
   ```

   asks the renderer to keep a behind-background sprite visible where the
   ROM's background pixel is colour 0, even when a layer-2 (priority 20–29)
   background covers that pixel. The tag takes no arguments; it is on or off
   for the whole pack. It is a tag, not an `<options>` token, because
   `HdPackLoader::ProcessOptionTag` — in MesenAI and in upstream Mesen alike
   — logs an unknown token as `Invalid option` and counts a load error, while
   the tag dispatch has no final `else` and an unknown tag is skipped in
   silence. A pack recorded here therefore still opens in Mesen 2 and HD
   Mesen, without the effect. `<options>` tokens are not gained, lost or
   reordered (`HdPackOptionsToString` unchanged). MEP packs get it through
   their `textures/hires.txt` by ADR-0005; `pack.json` is not touched.

2. **Renderer semantics, `HdNesPack::GetPixels`.** With the tag set, in the
   layer-2 pass, a pixel is left as drawn by the behind-background sprite
   pass when all of: the pixel has a behind-background sprite with
   `SpriteColorIndex != 0` (`lowestBgSprite != 999` already records this);
   the ROM's background pixel has `BgColorIndex == 0`; and the layer-2
   background would otherwise paint it. Everything else — opaque background
   under the sprite, no sprite, front sprites, layers 0/1/3 — draws exactly
   as today. Equivalently: with the tag, the behind-background sprite pass is
   re-applied after layer 2 on colour-0 background pixels. The issue-#328
   tile-suppression counters are scoped per draw and are not affected.
   Without the tag, byte-identical output to today's renderer — a unit test
   asserts it. Layer 3 (priority 30–39) is not part of this rule: see
   "Amended 2026-09-22" below.

3. **The recorder writes the tag on every pack it produces**
   (`HdPackBuilder`, next to `<options>`), bootstrap `auto/` included, since a
   recorded `<background>` is the case the tag exists for. Hand-written and
   community packs are untouched: their authors add the line if they want the
   behaviour. `mep_lint.py` accepts the tag and says nothing about its
   absence.

4. **The overdraw tool splits its count.** `scripts/measure_capture_overdraw.py`
   reads the OAM `K`/`P`/entry lines (ADR-0222) or the `M` RAM line of the
   retained frame and reports **two** columns per frame — `erased background`
   (ROM background detail under a flat render) and `erased sprite` (the flat
   render sits only under sprite detail) — plus the total for continuity with
   the ADR-0221 and F12.13 logs. Overpaint of content by content is out of
   scope here (ADR-0223 records it as unmeasured). The F12.15 stop condition
   is stated on the split columns, never on the total.

5. **Spec and docs.** The tag is added to the hires.txt extension draft under
   ADR-0004's community-review status, to `docs/remastering-a-game.md`, and
   the kit's `ARTIST.md` gains one sentence saying what the line is so nobody
   deletes it as noise.

## Consequences

- **Closes #339's second cause for packs recorded here**, and only those;
  the first cause (the card) stays with ADR-0223. The issue stays open until
  both land or the second is declined.
- **The tag is a third place a MesenAI pack diverges from HD Mesen** (after
  `<ver>` handling and the GB/SMS draft). It is safe by construction — ignored
  where unknown — but a pack author reading the file sees a line no other
  emulator documents; the spec entry is the answer.
- **Render cost:** one extra branch per pixel per layer-2 background, only on
  packs with the tag and only on pixels with a behind-background sprite.
  Measure on the 30-ROM library before and after, not assume. *Assigned
  2026-09-22 to F12.16*: the F12.15 log measured Punch-Out!! alone
  (whole-run wall clock, no difference inside the spread); the 30-ROM
  before/after is folded into F12.16's re-recording of the library, which
  produces the tagged packs anyway, rather than re-recording twice.
- **Re-record is not required.** Existing bootstrap packs on disk lack the
  tag and keep today's look until re-recorded; a user can add the line by
  hand. The catalog's accepted packs are unaffected until their authors opt
  in.
- **The measured number for ADR-0223 changes.** With the split tool, option
  A there is judged on `erased background` alone (the card's 0), and the
  fight window's sprite loss stops being counted against a gate rule.
- **Trap:** `lowestBgSprite` is set only when the behind-background sprite's
  colour index is non-zero, which is the right test; do not read
  `BackgroundPriority` alone, or a transparent sprite pixel would block the
  background. And do not gate on the *HD* tile's alpha — the rule is the
  hardware's, on the ROM's background colour index.

## Amended 2026-09-22: layer 3 (priority 30–39) over a restored behind-background pixel

The Sonnet verification of the F12.15 log left one edge open: §2 says
nothing about what happens when a **layer-3** `<background>` also covers a
pixel the tag just restored. Read off `HdNesPack::GetPixels` as shipped, the
answer is fixed by pass order, not by any branch:

1. backdrop → layer 0 → behind-background sprites → layer 1 → tile →
   **layer 2** → the ADR-0224 re-apply (only when
   `KeepsBehindBgSprite` holds, i.e. layer 2 actually painted the pixel) →
   **front sprites** → **layer 3** → the issue-#328 suppression bookkeeping.
2. Layer 3 runs **unconditionally, last**, through the same
   `DrawBackgroundLayer` as every other layer. Where its image pixel is
   opaque (`Alpha` blend) it replaces whatever is underneath — the restored
   behind-background sprite pixel *and* any front sprite pixel alike; with
   `Add`/`Subtract` it blends onto both alike. Where its image pixel is
   transparent, the restored sprite shows through.
3. With no layer-2 paint on the pixel (`layer2Painted == false`) the
   predicate is false, nothing is re-applied, and layer 3 hides the sprite
   exactly as it does today without the tag. The tag never fights layer 3.

**Declared edge, proposed as intended.** The tag's contract is the layer a
recorded screen lives in — layer 2, ADR-0050's priority 20, "behind
foreground sprites" — and it restores the hardware's rule *for that layer
only*. Layer 3 is the format's "foreground" layer, defined as above every
sprite of either priority; letting the tag punch through it would show a
behind-background sprite where a front sprite is hidden, a priority
inversion no hardware rule supports. So: with the tag, a behind-background
sprite over colour-0 canvas is treated by layer 3 exactly like a front sprite
is, no better and no worse. This is what the code does and what this
amendment declares it should do. The recorder writes no layer-3 background
(a recorded `<background>` is priority 20), so no recorded pack meets the
edge; only a hand-written pack that stacks a priority-30 image over a
recorded screen can, and that author has asked for a foreground.

**Alternative considered and not taken:** extend the predicate to "any
layer ≥ 2 painted" and re-apply after layer 3 as well. Rejected for the
inversion above; if a user wants it, that is a new decision, not this
amendment. No open question remains for a human on this edge unless that
preference is voiced.

**Evidence.** `scripts/core_unit_tests.cpp` BlocoP4,
`TestBehindBgSpriteRuleYieldsToALayer3Background`: the pass-order model
gains a layer-3 step after the front sprites and pins (a) opaque layer 3 over
a restored pixel → layer 3, with or without the tag; (b) the same over a
front sprite → layer 3, so the restored sprite is not treated worse than a
front one; (c) layer 3 with no layer-2 paint → no re-apply, layer 3; (d)
transparent layer 3 → the restored sprite shows. `HdNesPack` itself stays
outside the `make core-unit-tests` link set (needs `NesConsole`), as
recorded in the Status line; the model is a model of the pass order, with
the real predicate deciding. Stop condition (2) stays "partially met" for
that reason — this amendment closes the *edge*, not the link-set gap.

Docs carrying this edge: `docs/specs/hires-gbsms-v1-draft.md` §7 (draft
revision 3), `docs/remastering-a-game.md`, and the F12.15 validation log's
"What this log does not deliver".
