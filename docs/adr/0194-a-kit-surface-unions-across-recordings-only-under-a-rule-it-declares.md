# ADR-0194: A kit surface unions across recordings only under a rule it declares, and figures and scenery gain the union the pattern pages already have

- Status: proposed (2026-09-15) — awaiting the project owner's pick between the
  two options in "Alternatives". Nothing below is implemented; today's
  behaviour is "primary pack only" for figures and scenery.
- Date: 2026-09-15
- Related: ADR-0183 (§1 a kit is a projection, §2 the four surfaces, §4 the
  round-trip acceptance test), ADR-0184 (the amendment's union language;
  `record_stages.sh`'s own "judged as a union"), ADR-0179 (cycles, phases and
  `repeats` — the identity a figure union needs), ADR-0164 (adjacency
  co-occurrence — the identity a scenery union needs), ADR-0153 §3
  (`sprites.png` is a vocabulary dump, not a surface), ADR-0182 (why a game is
  recorded as many short runs), F9.25 ([log](../validation/f925-contra-matrix-2026-09-15.md))
- Supersedes / amends: nothing today. On acceptance it amends ADR-0183 §2 with
  a per-surface union clause.

## Context

A game is not recorded once. Contra's evidence for the F9.25 matrix is **22
recordings** — nine per-state clean controls of 60 s, eleven navigation
sessions of 300 s, two coverage passes — because the retained OAM stream is
capped at 4096 frames and one long run loses its tail: "the right shape is
several short runs, one per save state, each into its own pack folder,
**judged as a union**" (`scripts/record_stages.sh`, header). ADR-0184's
amendment states the same thing about coverage ("the union of the two reaches
64.6%").

The tooling implements that union in exactly two places:

| surface | union today | evidence |
|---|---|---|
| pattern pages | **yes** — `artist_chr_kit.py --also <pack>`, repeatable (#199: "a second recording of the same ROM donates CHR cells") | F9.25: 21 `--also` packs donated **142** cells, 0 lost, 0 invented |
| stage maps | **yes, as separate regions** — `artist_map.py --pack` and `--dump` are per stage | F9.25: one panorama per session, never merged across sessions |
| figures | **no** — `artist_kit.py` takes one pack; the figure vocabulary is that pack's `sheets/poses.json` | F9.25 verified the nine clean packs **nine times**, one kit each |
| scenery | **no** — `artist_bg_kit.py` takes one pack; `adjacency.json` is that pack's | same |

So the promise is half-kept, and the half that is missing is the half the
artist touches first. An artist repainting Contra's figures from the F9.25
evidence opens nine kits and reconciles by eye, or picks one state and loses
the others' animation cycles — which is the C.5 failure mode (manifest work
landing on the artist's path) that Phase 9 exists to remove.

What forced the decision now: F9.25's stop rule requires "the union rebuild
passes structural validation", and the only honest reading of that gate for
figures and scenery was nine independent `--verify` runs, since a merged
artifact is not something the generators can express. That gate is met, but it
met a weaker claim than the header comment implies.

Non-goals, stated because they bound the decision: this does not change the
recorded pack or `hires.txt` (ADR-0183 §1 — the kit stays a projection); it
does not union across different ROMs (a different ROM is a different kit); it
does not union the raw `sprites.png` vocabulary dump (ADR-0153 §3 — not a
painting surface); and it does not decide *how many* recordings a game should
have (ADR-0182 owns that).

## Decision

**Every kit surface declares, in its manifest fragment, the rule under which
several recordings of one ROM are one surface — and a surface with no such
rule says so in `ARTIST.md` rather than quietly showing the first pack's
content.** All four generators take the same repeatable `--also <pack>` the CHR
kit already has, and the identity rule is named per surface, taken from data
that already exists:

1. **Pattern pages** — identity is the `(tileData, palette)` key. Unchanged
   from #199: a cell present in any recording fills a cell of the bank.
2. **Stage maps** — identity is the world position (screen, then 8×8 cell). Two
   recordings that walked the same stretch merge into one region; where they
   disagree, the variant the most recordings showed at that cell wins, and the
   disagreement count travels in the fragment. Stage maps from *different*
   stages stay separate regions, which is what `--stage` already means.
3. **Figures** — identity is the **cycle signature** (ADR-0179: the ordered
   pose sequence and its period), not the pose id and not the sheet index. A
   cycle two recordings both saw becomes one row whose `repeats` is the sum and
   whose `ids[]` names every source recording; a cycle only one recording saw
   keeps its own row; a pose that belongs to no cycle is grouped by its
   silhouette and matches only on exact equality. The grid's `notes[]` MUST
   name the recordings it merges and MUST NOT present a summed `repeats` as if
   one run had produced it.
4. **Scenery** — identity is the co-occurrence key ADR-0164 already computes
   for an object. Counts are summed; a group that is nameable in one recording
   and scattered in another follows the nameable one, and the drop list
   (ADR-0183 §2) reports the scatter.

`--verify` is unchanged and stays the acceptance test: a union surface must
rebuild with 0 errors and **0 keys lost, 0 invented** against the primary
pack. The union changes which recording each row came from, never what a key
renders. A generator that cannot state a rule for a surface prints that in
`ARTIST.md`; it never silently drops the other recordings.

## Alternatives

1. **Keep the surfaces per recording and document it.** Cheapest by far: no
   code change, one paragraph in `docs/remastering-a-game.md` and a correction
   to `record_stages.sh`'s "judged as a union" (meaning the coverage metric,
   not the kit). It forecloses the single-artifact promise for the two surfaces
   an artist opens first, and leaves nine-kit reconciliation as the artist's
   work — the exact cost Phase 9 was chartered to remove. Rejected as a
   *default*, but it is the honest fallback if the merge rules below cannot be
   made to verify.
2. **Union at the pack level, before the kit** (one `pack_union.py` merging
   `hires.txt`, `sheets/` and `poses.json`, then the existing generators run
   unchanged on the merged pack). One place to own the merge, one place to
   test, and the four generators stay projections of a pack (ADR-0183 §1).
   Costs: it must resolve `sheets/usrNNN.png` id collisions, per-pack palettes
   and poses.json counts *before* any surface can say what it merged, so the
   provenance the decision above requires has to be carried through the merged
   pack anyway — and a merged pack is a new artifact whose round-trip is not
   ADR-0183 §4's check.
3. **Union only where it is already cheap** (CHR + maps, as today), and give
   figures and scenery a *cross-kit index* instead of a merge — a page that
   says "cycle X of the recording Y is the same cycle as cycle Z of recording
   W", leaving the paintings separate. Less to build, but it answers the
   question with a pointer rather than a surface, and the artist still opens
   nine kits.

## Consequences

- **A shared merge module, and one new failure mode to test.** Four call sites,
  one implementation; the danger is a merge that silently changes rendered
  pixels, which §4's 0-lost/0-invented check catches only if the union run is
  verified, not just the primary pack.
- **Counts become sums, and sums need provenance.** `repeats`, "seen N times"
  and object counts stop meaning "one run saw this N times". A reader who does
  not know that will over-trust a figure row, so the fragment's `notes[]`
  obligation is load-bearing, not decoration.
- **Figures from different states may not be comparable.** A row merged from a
  menu state and a boss state can hold poses that never coexisted in play; the
  cycle signature makes them the *same animation*, not the same context, and the
  grid's ordering rule (ADR-0179) is per cycle, so a merged grid stays readable
  — but `scripts/stages/README.md`'s "one row is that animation" claim now
  describes a union, and the guide must say so.
- **It does not touch the recording.** The expensive artefact is unchanged, and
  every kit stays regenerable and throwaway (ADR-0183 §1): a wrong merge costs
  a re-run of the generators, never a re-recording.
- **F9.25's gate would tighten on re-run.** Its four measured rows were
  verified per recording; under this ADR the same rows rebuild as one figure
  surface and one scenery surface per game, and the union's fragment — not the
  nine separate ones — becomes the artifact a reviewer reads.
