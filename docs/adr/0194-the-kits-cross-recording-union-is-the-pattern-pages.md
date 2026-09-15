# ADR-0194: The kit's cross-recording union is the pattern pages — "judged as a union" is a coverage metric, and figures, scenery and maps stay per recording

- Status: proposed (2026-09-15) — awaiting the pick. Accepting this closes the
  cross-recording union question and changes nothing in `scripts/`: no
  generator gains a merge, and the alternative in "Alternatives" stays
  unbuilt. The only thing that reopens it is the observation named in
  "Consequences".
- Date: 2026-09-15
- Related: ADR-0183 (§1 a kit is a projection, §2 the four surfaces, §3
  evidence vs inference, §4 the round-trip acceptance test), ADR-0184 (§2 the
  clean/coverage surface split; the amendment's coverage union; the "judged as
  a union" wording in `scripts/record_stages.sh` and
  `docs/remastering-a-game.md`, "Driver A — a scripted route"), ADR-0179
  (cycles, phases, `repeats` — the identity a figure merge would need),
  ADR-0164 (adjacency co-occurrence — the identity a scenery merge would
  need), ADR-0153 §3 (`sprites.png` is a vocabulary dump, not a surface),
  ADR-0182 (a game is recorded as many short runs), F9.25
  ([log](../validation/f925-contra-matrix-2026-09-15.md)), PRD Part A §4 (the
  F9.18 panel protocol)
- Supersedes / amends: nothing. On acceptance it amends no ADR: ADR-0183 §2
  already describes the surfaces of *a* recording, and this says that reading
  was the right one.

## Context

A game is not recorded once — the retained OAM stream is capped at 4096
frames, so "several short runs, one per save state, each into its own pack
folder, judged as a union" is the documented shape
(`scripts/record_stages.sh`), and F9.25's evidence for Contra is 22
recordings. Only one of ADR-0183's four surfaces merges across them today:

| surface | cross-recording behaviour |
|---|---|
| pattern pages | **unions** — `artist_chr_kit.py --also <pack>`, repeatable (#199: "a second recording of the same ROM donates CHR cells"); F9.25 measured 21 `--also` packs donating 142 cells into a 94 %-complete bank, 0 lost, 0 invented |
| stage maps | **separate regions** — `artist_map.py` zips `--stage`, `--dump` and `--pack`, one pack per stage; two recordings of one stage give two panoramas |
| figures | **one pack** — the vocabulary is that pack's `sheets/poses.json` |
| scenery | **one pack** — `adjacency.json` is that pack's |

Three measurements taken 2026-09-15, on the F9.25 evidence, say the missing
merges would buy nothing — and one of them says they would cost.

1. **A map merge buys no ground.** Three 300 s stage-1 runs — clean,
   `0032:99`, and `0032:99` + `00B0:FE` — died at different points and each
   stitched the *identical* 2512×240 plus a 464×936 region. The wall is the
   input script, and no merge moves it.
2. **A figure or scenery merge makes the surface the artist opens first
   worse.** The recording *is* the context: Phase 9's own principle splits
   sheets by context "so a rupee counter never sits between two trees", and a
   merge across a water state and a boss room puts rows of different contexts
   on one grid while turning `repeats` into a sum that reads as one run's.
   ADR-0183 §3's rule — evidence and inference never confused — then demands
   provenance in every fragment to keep the number honest.
3. **The artist's unit is the stage, not the game.** The guide records per
   stage and titles the kit for one ("Contra, stage 1"), and the reference
   pack this project measures against (Contra80s) is organized the same way
   (`Stage1a.png`, `Contra-Stage6-Ground-5.png`).

The wording that suggests otherwise is **not a defect**. `record_stages.sh`'s
"judged as a union" and the guide's "Nothing is merged — you judge them as a
union, and a stage that came out thin is the stage you record again" both mean
the coverage metric (ADR-0184's union of distinct `tileData`), not a kit. The
inference that the kit should merge was made once, in the first draft of this
ADR, from those phrases alone.

Non-goals: this does not change the recorded pack, the kit folder shape or the
`--verify` contract; it does not union across different ROMs (a different ROM
is a different kit); it does not touch the raw `sprites.png` vocabulary dump
(ADR-0153 §3); and it does not decide how many recordings a game gets
(ADR-0182).

## Decision

1. **A kit is the projection of exactly one recorded pack** (ADR-0183 §1), and
   its four surfaces are that pack's. No generator gains a cross-recording
   merge.
2. **The one cross-recording union is the pattern pages**, because their
   identity is the ROM's: a `(tileData, palette)` cell is the same cell in any
   recording of that ROM. `artist_chr_kit.py --also` stays exactly as shipped
   in #199.
3. **Stage maps are regions per recording.** `artist_map.py` keeps one pack and
   one grid dump per `--stage`; two recordings of one stage produce two
   panoramas, and no rule for choosing between two variants of one world
   position is invented.
4. **"Judged as a union" and "the union of the recordings" name the coverage
   metric** — the distinct `tileData` a set of recordings exhibits against a
   reference, `artist_cover.parse`'s unit, as ADR-0184's amendment reports it.
   A comment or document that uses "union" about a kit artifact must say
   "coverage" or be corrected.
5. **Nothing else changes.** `--verify` keeps its 0-lost/0-invented meaning per
   pack, the kit contract's folder shape is untouched, and a surface is still
   delivered only when it round-trips (ADR-0183 §4).

## Alternatives

1. **Merge across recordings per surface, under a declared identity rule** —
   figures by the ADR-0179 cycle signature (not pose id), scenery by the
   ADR-0164 co-occurrence key, maps by world position; every generator gains
   the repeatable `--also` the CHR kit has. Rejected on measurements 1–3 above:
   it spends a shared merge module and four call sites to make the primary
   painting surface context-mixed, and it obliges every fragment to carry
   summed counts plus provenance so a reader is not misled.
2. **Merge the packs before the kit** (one `pack_union.py`; generators
   unchanged). Rejected because it recreates the same identity problem one
   layer down — colliding `sheets/usrNNN.png` ids, `poses.json` counts,
   per-pack palettes — and produces a pack whose acceptance test is no longer
   ADR-0183 §4's round-trip.

Keeping the surfaces per recording and saying so is the Decision, not a third
alternative.

## Consequences

- **No implementation and no new semantics.** No merge module, no changed
  `repeats`, no new flag; the recorded pack, the kit contract and the coverage
  metric are as they already are.
- **The phrase stops being ambiguous.** A reader who meets "judged as a union"
  now has a decision that says which union it is, which is the failure this ADR
  exists to prevent — the inference already happened once.
- **An artist with several recordings of one stage opens several kits**, and
  within each kit the surfaces belong to that recording only. That is the same
  provenance rule ADR-0184 §2 already imposes from the other direction: a
  cheated run may not feed figures.
- **Trigger to reopen, named**: an F9.18 panel run in which the artist loses
  time choosing or combining kits and cannot say, without reading a manifest,
  which recording feeds which surface. The observation is part of the panel's
  test 2 (PRD Part A §4). Reopening means building the merge against the case
  the artist actually hit, with the identity rule that case needs — not the
  cycle-signature rule rejected here.
- **Deferring has a cost worth stating**: the trigger depends on the panel
  running, and the panel depends on a person who did not build the feature. If
  it never runs, this decision stands by default — which is the option that
  builds nothing, but it is a default, not a measurement.
