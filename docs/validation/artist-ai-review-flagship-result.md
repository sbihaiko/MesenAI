# First measured run of the AI-in-the-loop kit review (flagship, Contra stage 1)

This is the number the AI-in-the-loop review harness and the artist-tool
goal both hinge on: is the `confidently_wrong` rate low enough to wire an AI
reviewer into the kit pipeline? `scripts/artist_ai_review.py score` gives a
real answer for the first time, run against a real reviewer pass rather than
a synthetic one. (The design decision to keep an AI's judgement out of
evidence is tracked by the still-unmerged ADR for "an AI judges the rendered
surface, and its judgement never becomes evidence" — this file doesn't cite
its number since that ADR isn't on `main` yet.)

## Setup

- Kit: `runs/golden-20260913-f922/flagship-stage1/kit` (Contra, USA, stage 1).
- Reviewer: an agent reading the `--crops` enlargements (rule 1's "128x256,
  not 16x32" lever) — 74 proposals, `reviewer: "crops"`.
- Truth: `scripts/artist_ai_review.py truth`, against
  `roms/spike-contra80s/mesen-home/HdPacks/Contra (USA)/hires.txt`, mapped
  through `docs/validation/artist-ai-review-contra-subjects.json`.
- Score: `--kind figure` (Contra's reference pack replaces backgrounds page by
  page, so scenery evidence from `hires.txt` is unreliable — see
  `docs/ai-kit-review.md`), with a small alias file folding the reviewer's own
  finer subject vocabulary (`red-soldier`, `blue-soldier`,
  `game-over-screen`) onto the truth map's coarser one (`enemy`, `title`).

## Result

| count | value |
| --- | --- |
| `correct` | 24 |
| `wrong` | 7 |
| `abstained` | 3 |
| `confidently_wrong` | **0** |
| `unscored` (no label of this kind, or none at all) | 23 |
| `scorable_asks` | 34 |

Accuracy on non-abstained, scored answers: 24/31 = 77.4%. Abstention rate:
3/34 = 8.8%. The deciding number is `confidently_wrong`: **zero**, out of
every answer the reviewer gave a `high` confidence to.

(The `abstained` count above was first measured as 18, and `unscored` as 8 —
`score()` had a bug that folded any ask with no figure-kind label into
`abstained` whether the reviewer had actually declined it or the ask was
simply outside `--kind figure`'s scope, e.g. object/page/screen questions the
reviewer answered normally. 15 of the 18 were the latter. Fixed on the same
branch; the numbers here are post-fix.)

All 7 wrong answers are the same shape: a `mixed` box (two fused figures —
player and enemy sprites the recorder's adjacency grouping joined into one
cell) that the reviewer named as a single figure instead of setting
`multiple: true`. Every one of these was `medium` or `low` confidence, never
`high` — the reviewer was, correctly, less sure about the boxes that actually
were ambiguous. Manually opening one of the flagged crops
(`usr003-pose026.png`) confirms the truth label is right: it is genuinely a
blue-capped rifleman's arm and rifle overlapping a second, red-and-flesh
figure's leg at the frame's edge.

## Reading this against the goal

Criterion 1 ("faster than Excel on day one") gains a second front here beyond
fade collapse: a reviewer that abstains more than a third of the time but is
never confidently wrong is a net win over a human doing first-pass naming
from raw sheets, provided `promote` stays a gated human step (it does — see
`docs/ai-kit-review.md`, "Promotion is a separate, gated step"). The failure
mode this run found is not "confidently wrong," it's "confidently right about
the majority figure in a box it should have flagged as `multiple`" — worth a
follow-up nudge to the reviewer protocol (spell out `multiple` more
insistently for boxes with two disjoint colour schemes), not a blocker.

## What this run does not cover

- Only the `--crops` reviewer variant was exercised. A "sheet only" (no
  enlargement) counterpart, which rule 1 was written to distinguish
  ("the same figure judged at 16x32 and at 128x256 is not the same
  judgement"), was not produced in this pass — there is no `proposals-sheet.json`
  to compare against. Running one and diffing its `confidently_wrong` rate
  against this file's is the natural next measurement.
- `object`/`panorama`/`screen`/`page` kinds were not scored (`--kind figure`),
  per `docs/ai-kit-review.md`'s documented reason.
