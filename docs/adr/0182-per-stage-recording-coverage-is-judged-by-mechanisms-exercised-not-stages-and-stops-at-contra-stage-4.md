# ADR-0182: Per-stage recording coverage is judged by the mechanisms it exercises, not by stages played, and stops at Contra's fourth stage

- Status: accepted (2026-09-13, by the user: "encerra na fase 4 e faz o
  PR") — already reflected in `scripts/stages/README.md` and in the PRD's
  Part A §3 record; F9.22 is closed and its slice row deleted.
- Date: 2026-09-13
- Related: PRD Part A §3 (F9.22 record entries of 2026-09-12 and
  2026-09-13), ADR-0179 (cycles and variants the recordings feed),
  ADR-0181 (controller state and `driver`, measured on these states),
  ADR-0157 (headless input counted in frames — what the chain files rely
  on), `scripts/stages/README.md`
- Supersedes / amends: the F9.22 deliverable as worded on 2026-09-12 in
  the PRD's Part A slice table ("every subject the artist pack covers —
  Contra: 8 stages, water, base interior, bosses, player 2 — is on screen
  at least once"). The stage count is dropped from the requirement; the
  rest stands.

## Context

F9.22 was written when one 120 s Contra run kept 4151 of 7213 frames and
the pose sidecar had nothing to say about a base soldier, a boss or the
second player. Its deliverable asked for a save state per stage and
distinct area so that "every subject the artist pack covers" is on screen
once, and listed Contra's eight stages as the yardstick.

By 2026-09-13 the tooling exists (`headless_record state= save-state=
input=`, `scripts/record_stages.sh`, `scripts/mss_ram.py`,
`scripts/replay_chain.sh`, chain files under `scripts/stages/contra/`)
and nine Contra states are recorded with a probe each: stage 1 (run, 30
lives, water, second player), the stage-1 boss, the base, the waterfall,
its boss, the second base and the second base's boss room. Stages 2–3
were beaten headlessly by RAM-steered search; stage 4 was played to its
boss room. What the recordings changed is on record: ADR-0179's cycles and
variants, ADR-0181's `driver` and its 4-window floor, the fusion rule's
cost on a base soldier, a probe that attributes nothing, the retraction of
"a chain reproduces only as a chain".

What a further stage costs is also on record: each one is hours of
depth-first search over emulator runs (the second base took an evening
and its boss room ran the search out of depth), plus chain tracing,
documentation and review — for enemies whose sprites the pack format
already handles the same way it handles the ones recorded. No format
decision in Phase 9 or Phase 10 waits on a snowfield, an energy zone, a
hangar or the alien lair.

Non-goals: this does not withdraw the tooling, the states or the chain
files; it does not forbid playing further stages when a measurement needs
one; it does not change what F9.22 recorded or how.

## Decision

1. **Coverage is judged by mechanisms, not stages.** A per-stage recording
   set is complete for a game when the recorded states together show every
   mechanism the sidecar has to carry for that game: the player's cycles
   with and without a driver, an enemy cycle, a fusion, a projectile
   variant, a boss, a distinct area (water, interior), the second player
   where the game has one, and one state where the probe rule attributes
   nothing. For Contra that set is the nine states above.
2. **Contra stops at the fourth stage's boss room.** `stage4-boss.mss` is
   the last Contra state; its boss and stages 5–8 are not played, not
   scheduled, and not listed as open work. The README says so where it
   describes the second base.
3. **The door stays a measurement.** A later stage is played only when a
   concrete measurement names the subject it needs (an ADR or a PRD slice
   citing this one), with the same tooling and the same rule that every
   new state ships with its `.chain.txt`.
4. **F9.22 is closed.** The PRD keeps the slice's outcome as §3 record
   entries and deletes its row from the pending table; open item 3b names
   this ADR.

**Measured 2026-09-13** with `scripts/artist_cover.py <artist hires.txt>
<recorded pack auto/ dir>...` (versioned, stdlib only; run over Contra80s
1.1 and the fifteen Contra packs of the 120 s golden run and the per-stage
recordings, with probes; the full tables are its output, the prose reading
is `runs/golden-20260913-f922/contra/artist-cover.md`, unversioned like the
earlier spikes): of the 3 404 distinct tiles the Contra80s 1.1 pack paints,
1 597 (47 %) were on screen in some recorded state; Bill 126 / 235, Lance
110 / 137, the soldier and enemy pages 80–85 %. The unseen half is at least
one whole later-stage tileset (PRG page `$15xxx`), the Snow Field tanks,
the Energy Zone boss's own tiles, two later-stage enemy pages and the
ending. Those are the subjects §3 would name. The last two states recorded
(second base, its boss room) added 10 and 0 artist tiles beyond the
earlier ones, and no probe added any beyond its stage script — the
marginal yield that motivated §2, now measured.

## Consequences

- The artist pack's per-stage comparison (ADR-0179 §4's measurement, ADR-0180's
  cover) is bounded by these nine states. If a later comparison shows a
  pose class that none of them exhibit, that is the measurement §3 asks
  for, and it reopens one stage, not the slice.
- `stage3-waterfall.mss` remains the one archived-only state (its
  hand-stitched chain is not on disk); everything after it replays from
  `scripts/stages/contra/*.chain.txt`. Nothing in this ADR changes that.
- The stage-4 boss's head slot and type on the second base stay unread;
  anyone who does play it starts from `stage4-boss.mss` and the search
  notes in the README (pods type 10 and eye type 8 fall to the same lanes
  as on the first base).
