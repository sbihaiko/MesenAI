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

## Round 2 — the sheet-only variant (rule 1's own test)

Rule 1 exists because "naming figures from 8px thumbnails put three green
enemies on the player's sheet" — the claim is that resolution changes the
verdict. The first round only exercised `--crops` (nearest-neighbour
enlargements, 2x the sheet's own scale). This round answers the same 36
figure asks from the kit's own sheet PNGs, cropped to the ask's `rect` at
**native scale, no enlargement** — half the linear resolution of a crop, the
actual "16x32 vs 128x256" comparison the rule names. `reviewer: "sheet"`.

| count | crops (round 1) | sheet, native (round 2) |
| --- | --- | --- |
| `correct` | 24 | 22 |
| `wrong` | 7 | 7 |
| `abstained` | 3 | 5 |
| `confidently_wrong` | **0** | **0** |
| `unscored` | 23 | 2 |
| `scorable_asks` | 34 | 34 |

(`unscored` differs because the sheet-only pass only ever answered the 36
figure asks, so 34 of them land on a figure-kind label and the other 2 are
the `usr004` capsule shape neither pass could place — no scenery/page/screen
asks were answered at all in this pass, unlike `--crops`'s 74.)

Accuracy on non-abstained answers: 22/29 = 75.9% (crops: 77.4%). Abstention:
5/34 = 14.7% (crops: 8.8%).

**Confidently wrong stayed at zero in both.** For this kit, resolution changed
caution and marginal accuracy, not safety: without enlargement, two more
boxes (`usr003#pose007`, `usr003#pose019`, `usr003#pose032` — three, not two)
were honestly declined instead of guessed, because their fragments could not
be told apart at native scale. The failure mode that did survive resolution
is exactly round 1's: a `mixed` box (two fused figures) read as one coherent
silhouette and named as a single figure instead of flagged `multiple` —
`usr002#pose029`/`pose021` and `usr003#pose026`/`pose049`/`pose039`/`pose050`
were called this way in *both* passes, at *both* resolutions. Contra's sprite
art is chunky and low-detail enough that the palette blocks read the same
whether upscaled or not; the ambiguity in a fused box is structural (the two
figures really do share a silhouette), not a detail an enlargement recovers.

**One high-confidence pair moved with resolution**, and it is informative
about what the ceiling of confidence is measuring. `usr003#pose003` and
`pose013` (a clean, unambiguous red soldier, arms and legs fully separated
from the background) were called `high` confidence in *both* rounds and
scored correct in both, once the `red-soldier`→`enemy` alias is applied —
they did not need enlargement because nothing about them was ambiguous at
either scale. No figure flipped from `high`-confident-correct at one
resolution to `high`-confident-wrong at the other in this run; the two
passes disagree on `medium` calls and on what to abstain from, never on what
to be certain about. That is a single data point, not a general result — a
kit with finer detail (a game with anti-aliased or higher-resolution art)
is the harder test rule 1 was really written for.

Second `score()` bug found while running this: an abstention on an ask whose
truth label has no subject (the dominance threshold was never reached, e.g.
the `usr004` capsule) was still counted in `abstained` rather than
`unscored`, for the same reason as the first bug — the abstain branch ran
before the subject-is-None check. Fixed on the same branch
(`f1a04c96`); this table is post-fix.

## What this run does not cover

- `object`/`panorama`/`screen`/`page` kinds were not scored (`--kind figure`),
  per `docs/ai-kit-review.md`'s documented reason.
- Only one kit (Contra stage 1) and one game's art density. The
  resolution-independence found here is a property of this kit's chunky,
  low-detail sprites, not a general claim about the protocol.
