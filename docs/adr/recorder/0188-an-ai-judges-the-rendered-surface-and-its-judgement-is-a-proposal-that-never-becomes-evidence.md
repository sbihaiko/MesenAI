# ADR-0188: An AI judges the rendered surface, and its judgment is a proposal that never becomes evidence

- Status: accepted (2026-09-14, at the user's direction: "quero uma AI no loop
  para automatizar"; shipped 2026-09-14 in `43eaab04` (#215): the
  `packet`/`check`/`promote`/`truth`/`score` protocol of
  `scripts/artist_ai_review.py`, with `promote` the only door from a proposal
  into the generators' `--names` file. The ADR was written against PRD Part A
  slice F9.28; the commit that landed it is filed under F9.24)
- Date: 2026-09-14
- Related: ADR-0183 (the artist kit — §3 "evidence and inference are never
  confused" and "no generator invents a name" are the constraints this ADR
  must not break), ADR-0153 (artist-legible sheets — the surfaces being
  judged), ADR-0170 (the conversational skin studio, the *generative* half of
  AI in this project), ADR-0182 (coverage), ADR-0186 (what a code/data map may
  and may not claim — the same access-is-not-meaning discipline)

## Context

The pipeline records a game and emits four surfaces, but cannot judge. Three questions come up on every kit with no mechanical answer: which tiles form one figure; what that figure is; which shapes on a page are art and which are HUD, fade steps or noise. Mesen's answer is a human; the community's is a text editor.

`mkwong98`'s external editor is the one serious attempt at automating the judgment, and it failed in the market — across eight years and six forum threads, **six named people ever ran it, two ever completed a pack with it**, 451 release-asset downloads, zero GitHub-wide hits for its project format. On romhacking.net thread 30535 (April 2020) the artist handed it *and* a purpose-written tutorial replied that he "couldn't really manage to do much with it", then shipped five packs by hand-writing `hires.txt` without crediting it. The three most prolific pack authors all knew of it and stayed on text editors — the most prolific, January 2024: "I keep using the old school approach... Over time, I have learned **Excel** can be extremely useful", shipping that spreadsheet in his Metroid pack's public download.

Two constraints follow. **We are not competing with that editor; we compete with Notepad and Excel** — a tool displaces a spreadsheet only by being faster on day one. **And it lost partly because the step before it was never solved:** artists name *recording*, not mapping, as the bottleneck ("they turned into a garbled mess... I don't even know how I would begin unscrambling them"; in 2024, unanswered, "I sometimes have to make several passes over a scene to get it to record all the sprites/tiles") — a competent mapping tool fed garbled input is useless, and that second quote is also the per-stage/per-boss coverage finding of ADR-0182 and ADR-0184 §4.

Our own answer has been an AI as proxy for human vision, ad hoc and unmeasured; an unmeasured judgment written down looks exactly like a measured one afterwards. **The measurement that shapes this decision is a failure of mine:** naming figures from 8x8 thumbnails, I attributed three green enemies to the player — the input was too small to carry the distinction, and the error surfaced only when the figures were rendered as full grids. The lesson is not "check your work" but that **what the judge is shown determines what it can be right about** — the input is a design decision, not an implementation detail.

Non-goals: this ADR does not generate art (that is ADR-0170's half; the reference pack `Contra80s` shows the generative route is real). It adds no API dependency, does not change what a recording records, and gives no automated component write access to a pack.

## Decision

### 1. The unit of review is a rendered surface, never raw data

An AI in this loop is shown a PNG a human would recognize — a figure grid, a scenery object, a panorama region, a pattern page. Never a tile array, never a hex key, never an 8x8 crop. This is the direct consequence of the green-enemies error and it is a requirement, not a preference: a reviewer that cannot see the thing cannot judge it, and a judgment made from an inadequate input is not improved by qualifying it afterwards.

### 2. The output is a proposal, and a proposal never becomes evidence

Proposals live in their own file (`kit-proposals.json`), beside the `kit-part-*.json` fragments and never inside them. Nothing in the pack changes because a proposal exists. ADR-0183 §3 is unchanged and unweakened: what was observed and what was guessed remain separable by a reader who was not here.

### 3. Every proposal is falsifiable in seconds

A proposal cites exactly what was looked at — the file, and the cell coordinates within it — so a reviewer opens one image and knows. A proposal that cannot be checked this way is a defect and the ingest step rejects it.

### 4. Abstention is a first-class answer

"I do not know what this is" is a valid, valued result, and scoring must reward it relative to a confident wrong answer. A kit with unnamed figures is usable; a kit with confidently mislabelled ones is worse than a kit with none, because the label is believed.

### 5. Promotion is separate and gated

The generators already consume a human-written `names.json`. Accepted proposals are promoted into that file by an explicit step. **Nothing is auto-promoted.** The gate is where a human — or a measured confidence threshold, once §6 has produced one — decides.

### 6. The judge is scored, on ground truth, and the score is published

An AI judgment nobody measures is worse than none, because it reads as authoritative.

`Contra80s` is a labeled set produced by a human who knew the game: `BillRizer.png`, `Enemies_Gunner.png`, `Enemies_Terminator.png`, `Stage1BaseDoor1..3.png`, `LargeTank1..3.png`. Proposals are scored against it in **four separate counts** — correct, wrong, abstained, and **confidently wrong** — never collapsed into one accuracy number. Confidently wrong is the only category that can poison a kit, and it is the number this decision lives or dies by.

The reference pack is read locally and never redistributed, never committed, and never sent to any external service (it is unlicensed, all rights reserved, and depicts third-party IP).

## Consequences

- **An unmeasured judge is now a policy violation, not a shortcut.** Including mine: everything I have named by eye this session (the base door trio, the Contra stage-2 names) was produced outside this protocol and should be re-run through it before it is trusted at scale.
- **The confidently-wrong rate decides the wiring, not enthusiasm.** If it is high, the answer is a better input (larger renders, neighboring frames, the palette shown alongside) rather than a better prompt, per §1 — a measurable experiment to run as one.
- **This does not remove the human; it changes what the human is asked.** Reviewing a proposal that cites its own evidence takes seconds; naming 1642 shapes from scratch does not. The gate in §5 is where the human's remaining time is spent.
- **It composes with ADR-0170 but does not depend on it.** The judge is an agent with vision reading PNGs from disk — no key, no network. The generative half can arrive later without renegotiating this contract.
- **Two loops must not be allowed to merge.** A judge that both names a figure and generates its replacement would be grading its own work. If ADR-0170's generator is ever driven from these proposals, the scoring in §6 must be re-established against art the generator did not produce.
