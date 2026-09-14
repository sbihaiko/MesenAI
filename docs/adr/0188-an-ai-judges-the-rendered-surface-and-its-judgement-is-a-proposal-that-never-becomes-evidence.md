# ADR-0188: An AI judges the rendered surface, and its judgement is a proposal that never becomes evidence

- Status: accepted (2026-09-14, at the user's direction: "quero uma AI no loop
  para automatizar"; being implemented as slice F9.28 of
  `docs/roadmap/PRD-mesence-enhancement-ecosystem.md`)
- Date: 2026-09-14
- Related: ADR-0183 (the artist kit — §3 "evidence and inference are never
  confused" and "no generator invents a name" are the constraints this ADR
  must not break), ADR-0153 (artist-legible sheets — the surfaces being
  judged), ADR-0170 (the conversational skin studio, the *generative* half of
  AI in this project), ADR-0182 (coverage), ADR-0186 (what a code/data map may
  and may not claim — the same access-is-not-meaning discipline)

## Context

The pipeline records a game and emits four surfaces. What it cannot do is
judge. Three questions come up on every kit and none of them has a mechanical
answer:

- which tiles form one figure;
- what that figure is;
- which of the shapes on a page are art and which are HUD, fade steps or noise.

Mesen's answer is a human. Its PPU viewer lets a person shift+right-click and
copy every on-screen tile with its position, and `mkwong98`'s external editor
turns that selection into an object and then into `hires.txt` rules. That works
and it does not scale: on romhacking.net thread 30535 (April 2020) the artist
given that tool and a purpose-written tutorial replied that he "couldn't really
manage to do much with it" and that it "seems a bit overwhelming for starters".
A capable tool nobody can drive is the failure mode this project is closest to
repeating.

Our own answer so far has been an AI acting as proxy for human vision — which
is what the session's standing goal asks for — but ad hoc, unmeasured and
unreproducible. That is worse than it sounds, because an unmeasured judgement
that is written down looks exactly like a measured one afterwards.

**The measurement that shapes this decision is a failure of mine.** Naming
figures from 8x8 thumbnails, I attributed three green enemies to the player.
Nothing about the reasoning was unusual; the input was too small to carry the
distinction, and the error surfaced only when the same figures were rendered
as full grids. The lesson is not "check your work". It is that **what the judge
is shown determines what it can be right about**, and that the input is a
design decision, not an implementation detail.

Non-goals: this ADR does not generate art (that is ADR-0170's half, and the
reference pack `Contra80s` shows the generative route is real — its author
states most of its HD graphics came from ChatGPT and Gemini). It does not add
an API dependency, does not change what a recording records, and does not give
any automated component write access to a pack.

## Decision

### 1. The unit of review is a rendered surface, never raw data

An AI in this loop is shown a PNG a human would recognise — a figure grid, a
scenery object, a panorama region, a pattern page. Never a tile array, never a
hex key, never an 8x8 crop.

This is the direct consequence of the green-enemies error and it is a
requirement, not a preference. A reviewer that cannot see the thing cannot
judge it, and a judgement made from an inadequate input is not improved by
qualifying it afterwards.

### 2. The output is a proposal, and a proposal never becomes evidence

Proposals live in their own file (`kit-proposals.json`), beside the
`kit-part-*.json` fragments and never inside them. Nothing in the pack changes
because a proposal exists. ADR-0183 §3 is unchanged and unweakened: what was
observed and what was guessed remain separable by a reader who was not here.

### 3. Every proposal is falsifiable in seconds

A proposal cites exactly what was looked at — the file, and the cell
coordinates within it — so a reviewer opens one image and knows. A proposal
that cannot be checked this way is a defect and the ingest step rejects it.

### 4. Abstention is a first-class answer

"I do not know what this is" is a valid, valued result, and scoring must
reward it relative to a confident wrong answer. A kit with unnamed figures is
usable; a kit with confidently mislabelled ones is worse than a kit with none,
because the label is believed.

### 5. Promotion is separate and gated

The generators already consume a human-written `names.json`. Accepted
proposals are promoted into that file by an explicit step. **Nothing is
auto-promoted.** The gate is where a human — or a measured confidence
threshold, once §6 has produced one — decides.

### 6. The judge is scored, on ground truth, and the score is published

An AI judgement nobody measures is worse than none, because it reads as
authoritative.

`Contra80s` is a labelled set produced by a human who knew the game:
`BillRizer.png`, `Enemies_Gunner.png`, `Enemies_Terminator.png`,
`Stage1BaseDoor1..3.png`, `LargeTank1..3.png`. Proposals are scored against it
in **four separate counts** — correct, wrong, abstained, and **confidently
wrong** — and the four are never collapsed into one accuracy number.
Confidently wrong is the only category that can poison a kit, and it is the
number this decision lives or dies by.

The reference pack is read locally and never redistributed, never committed,
and never sent to any external service (it is unlicensed, all rights reserved,
and depicts third-party IP).

## Consequences

- **An unmeasured judge is now a policy violation, not a shortcut.** Including
  mine. Everything I have named by eye this session — the base door trio, the
  Contra stage-2 names — was produced outside this protocol and should be
  re-run through it before it is trusted at scale.
- **The confidently-wrong rate decides the wiring, not enthusiasm.** If it is
  high, the answer is a better input (larger renders, neighbouring frames, the
  palette shown alongside) rather than a better prompt, per §1. That is a
  measurable experiment and it should be run as one.
- **This does not remove the human; it changes what the human is asked.**
  Reviewing a proposal that cites its own evidence takes seconds. Naming 1642
  shapes from scratch does not. The gate in §5 is where the human's remaining
  time is spent.
- **It composes with ADR-0170 but does not depend on it.** The judge is an
  agent with vision reading PNGs from disk — no key, no network. The
  generative half can arrive later without renegotiating this contract.
- **Two loops must not be allowed to merge.** A judge that both names a figure
  and generates its replacement would be grading its own work. If ADR-0170's
  generator is ever driven from these proposals, the scoring in §6 must be
  re-established against art the generator did not produce.
