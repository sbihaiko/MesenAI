# AI review of an artist kit

The kit generators cut a recording into surfaces an artist recognises, and
prove the cut is lossless by rebuilding the pack from it. What they cannot do is
**judge**: which tiles form one figure, what that figure is called, which of
1642 recorded shapes matter and which are HUD or fade noise. Mesen's answer to
that is a human pointing at the PPU viewer.

This note is the protocol for asking an AI instead — and, just as importantly,
for measuring whether its answers are worth having. The tool is
`scripts/artist_ai_review.py`; its suite is `scripts/test_artist_ai_review.py`.

## The reviewer

**An agent with vision reading the PNGs.** Not an API call: no key, no endpoint,
no network, nothing to configure. `packet` writes down what to look at, an agent
(or a human) opens those images and writes the answers into
`kit-proposals.json`, and `check` decides whether the answers are usable. The
protocol is a file format and a question order, so any reviewer that can open a
PNG and write JSON can take the job.

## Four rules

1. **The reviewer looks at the rendered surface, never at raw data.** The unit
   of review is a PNG a human would recognise — a figure grid, a scenery
   object, a panorama region, a pattern page — plus a rectangle inside it. Never
   a tile array, never a hex key, never a bare 8x8 crop. This is the first rule
   because it is the one that was learned the hard way: naming figures from 8 px
   thumbnails put three green enemies on the player's sheet, and only the
   full-size grids revealed it. The input mattered more than the reasoning.
2. **Output is a proposal and never becomes evidence.** ADR-0183 §3 is binding:
   a generator never invents a name, and inference is never mixed into what was
   observed. Proposals live in their own `kit-proposals.json` beside the
   `kit-part-*.json` fragments, and nothing in the pack changes because a
   proposal exists.
3. **Every proposal is falsifiable in seconds.** It cites the file it looked at
   and the rectangle inside it, so a reviewer opens one image and knows. A
   proposal that cannot be checked is a defect, and `check` rejects it rather
   than recording it.
4. **Abstention is a first-class answer.** "I do not know what this is" beats a
   confident wrong name. `score` counts abstentions in their own bucket and
   never folds them into an accuracy number, so a reviewer that declines the
   hard cases is not punished for it.

## What the reviewer is shown, in what order

`packet <kit>` walks the kit's fragments in the kit's own reading order —
figures, scenery, stage maps, pattern pages — because a reviewer that has
already seen the player as a whole figure is the one who can recognise its torso
on a pattern page, never the other way round. It writes two files:

- `kit-review.json` — the machine-readable ask list plus the answer schema and
  a `sha256` over the asks, so an answer file can prove which kit it answers.
- `kit-review.md` — the same thing as the page a reviewing agent reads,
  grouped by surface.

One ask per unit of judgement:

| kind | one ask per | the question |
| --- | --- | --- |
| `figure` | figure box on a sprite grid | what figure is this, in what pose; is it more than one figure? |
| `object` | scenery object sheet | what is this piece of scenery? |
| `panorama` | stitched stage region | what place is this, and what is in it? |
| `screen` | whole recorded screen | what is happening here? |
| `page` | CHR pattern page | only where a whole thing is laid out contiguously — abstaining is the expected answer |

A sprite sheet does not record which cell belongs to which pose; the layout
does. `artist_kit.py` pads every figure to its row's box, centres it, and leaves
exactly one empty column between boxes, emitting the cells box by box in reading
order. The segmenter recovers the boxes from that and settles the rows a sheet
could be read several ways by making them add up to the sheet's pose count. A
sheet with no reading at all is listed under `skipped` — never approximated.

`--crops <dir>` additionally writes one enlarged, nearest-neighbour PNG per
figure ask. That is rule 1's lever: the same figure judged at 16x32 and at
128x256 is not the same judgement. Crops are written outside the kit, because a
crop is a reviewing aid and not a painting surface.

## The answer schema

`kit-proposals.json`, in the kit folder beside the fragments:

```json
{
  "version": 1,
  "kit": "<kit folder>",
  "packet": "<sha256 copied from kit-review.json>",
  "reviewer": "<who or what reviewed>",
  "subjects": {"red-soldier": "the red-uniformed enemy rifleman of stage 1"},
  "proposals": [
    {"ask": "usr000#pose002", "saw": "sheets/usr000.png", "rect": [4, 4, 68, 32],
     "subject": "red-soldier", "name": "red soldier running, stride 1",
     "confidence": "high",
     "why": "red cap and red tunic over white trousers, repeated across the row"},

    {"ask": "usr003#pose049", "saw": "sheets/usr003.png", "rect": [4, 184, 44, 76],
     "subject": "player", "name": "player prone, an enemy over him",
     "confidence": "medium", "multiple": true,
     "why": "two colour schemes in one box: blue cap and blue rifle left, red and skin right"},

    {"ask": "usr004#pose001", "saw": "sheets/usr004.png", "rect": [4, 4, 32, 32],
     "abstain": true, "why": "a red capsule shape I cannot place in this game"}
  ]
}
```

`check <kit> --proposals <file>` accepts an answer only when a reviewer could
falsify it in seconds, and reports everything else as a defect: an ask the
packet never made, an answer citing a file it was not shown, a rectangle outside
the box it was asked about, a name with no subject behind it, a subject the file
never declares, a confidence off the scale, a missing `why`, an answer given
twice, or an answer to a stale packet. Asks with no answer at all are listed
separately — a skipped ask is not an abstention.

`multiple: true` is how the reviewer says a box holds more than one figure
overlapping. It is a real case, not an edge case: the recorder groups sprites by
adjacency, so two figures that touched on screen can land in one box.

## Promotion is a separate, gated step

The generators take a human-written `--names` file (see
`runs/golden-20260913-f922/names-contra-stage2-base.json`). `promote` is the
only door from a proposal into one:

```
scripts/artist_ai_review.py promote <kit> --proposals kit-proposals.json \
    --accepted accepted.txt --out names.json
```

`accepted.txt` is one ask id per line — the ones a human ticked. There is no
flag that promotes everything, an abstention promotes to nothing, and an
accepted id the packet never made is refused.

## Measuring it

An AI judgement nobody measures is worse than none, because it looks
authoritative. Where a hand-made HD pack exists for the same ROM, it is a
labelled set produced by a human who knew the game: its file names (`BillRizer`,
`Enemies_Gunner`, `LargeTank1`) say what its tiles are.

```
scripts/artist_ai_review.py truth <kit> \
    --reference-pack "<pack folder>" \
    --subjects docs/validation/artist-ai-review-contra-subjects.json \
    --out truth.json
scripts/artist_ai_review.py score --proposals kit-proposals.json --truth truth.json \
    [--alias alias.json] [--kind figure]
```

Only the reference pack's `hires.txt` is read. Its PNGs are never opened, copied
or sent anywhere — the one thing wanted from it is the names.

Two things make the labels trustworthy rather than merely available:

- **Exact `(pattern, palette)` keys only.** Matching on the pattern alone is a
  guess, and Contra's grey rock faces share patterns with the light tiles of the
  player's sheet — a palette-blind lookup cheerfully labels a mountain
  `player`. A tile that does not match exactly contributes nothing.
- **Exclusive tiles only.** A tile that appears in files of two different
  subjects says nothing about either and is dropped. What labels a box is the
  tiles belonging to one subject and no other.

A box whose exclusive tiles give no subject a 60% majority is labelled `mixed`,
not unknown: the kit really does put two overlapping figures in one box, and a
reviewer who says so is right.

`score` reports four counts and never collapses them:

| count | meaning |
| --- | --- |
| `correct` | the subject matches the label (or `multiple` was set on a `mixed` box) |
| `wrong` | it does not |
| `abstained` | the reviewer declined — never counted as wrong |
| `confidently_wrong` | a subset of `wrong` where the reviewer said `high` |

`confidently_wrong` is the number that decides whether this is worth wiring in.
It is the only bucket that can poison a kit: a low-confidence wrong name gets
caught at promotion, a confident one gets ticked.

`--kind figure` restricts scoring to figures. Scenery is deliberately left out
of the scored set for Contra: a reference pack replaces sprites file by file but
backgrounds page by page, so its named files carry almost no scenery evidence
and the few scenery matches it does produce are unreliable.
