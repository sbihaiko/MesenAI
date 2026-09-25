# ADR-0232: A CHR RAM bank id names the CHR state a tile was drawn from, or stays a layout constant (#467)

- Status: **accepted 2026-09-25 — implemented the same turn** (issue
  #467), with unit tests covering the decision in
  `scripts/core_unit_tests.cpp` (`TestAChrRamBankIdFollowsTheChrState`,
  `TestAChrRamBankIdIsRehashedPerDrawnTileNotPerWrite`,
  `TestTheChrPageGuardStillHoldsOnAChrRamHashCollision`,
  `TestAPreFixChrRamTileIsRecognisedAndRehomed`) and
  `scripts/test_artist_chr_kit.py` (the three `ADR-0232` cases). The
  user's pick and go-ahead, verbatim:
  - pick: *"(a) via (c1) (Recommended)"*
  - go-ahead: *"em paralelo corrija os bugs, mande pro main e limpe os WTs"*

  Proposed 2026-09-24 with the options below. The re-record case the
  proposal left open was measured before shipping (see "Re-record over a
  pack recorded before the fix").
- Date: 2026-09-24 (proposed), 2026-09-25 (accepted)
- Related: issue #467 (root cause of #460), issue #460 / PR #473 (the slot
  guard, `Core/NES/HdPacks/ChrPageSlots.h`), ADR-0160 §3 (re-record, and
  the dropped-tile guard), ADR-0230 (palette variants, F14.9), PRD
  Part A F9.24 (`artist_chr_kit`)
- Measurement: `docs/validation/adr-0232-chr-ram-bank-hash-measurement-2026-09-24.md`
  (the options, and the re-record case);
  implementation: `docs/validation/issue-467-adr0232-chr-bank-id-2026-09-25.md`

## Context

`HdBuilderPpu` hashes each CHR RAM bank when a tile is drawn after
`_needChrHash` was set. The recorder uses that hash as the tile's
`ChrBankId`. It decides which `chr/Chr_*.png` page the tile's cell goes on
(`HdPackBuilder::AddTile`), and it is written as the trailing bank field of
every CHR RAM `<tile>` line.

The method meant to set the flag on a CHR write is spelled `WriteRAM`. The
PPU's virtual is `WriteRam`, so it overrides nothing, and has not since
the 2022 port. As a result:

- The flag is set only at construction and after a state load.
- A recording from power-on keys every drawn tile under the hash of an
  all-zero bank, `0`.
- A later tile at the same index asked for an occupied slot and evicted a
  drawn key (#460).

#460's guard (PR #473) stops the eviction without touching the hash.
Renaming the method makes the bank id follow the CHR state instead. That
re-lays out every CHR RAM recording and adds pages. So it is a trade-off,
not a fix in passing. The user's instruction, verbatim: *"Medir antes
(Recommended) — Escrevo uma ADR proposed e meço se as páginas extras trazem
peças que hoje ficam erradas ou só duplicam arte. Só depois você decide."*

**What was measured.** Castlevania 60 s idle, Zelda 85 s and Contra
stage 1 were recorded, each with and without the rename, both on top of
#460's guard. Excitebike (CHR ROM) is the control.

- **Keys and art.** The rename adds no key, loses no key, changes no pixel
  of any cell, and creates no duplicate. All 3 211 / 2 154 / 1 834 keys
  have byte-identical crops in both builds, including the 272 and 42 tiles
  on the new pages. The `<tile>` rules are equal once cell, bank and index
  are ignored. `sheets/` and `backgrounds/` are byte-identical. Drawn-key
  coverage on sheets is 100 % either way.
- **In game.** 23 frames were rendered with each pack, as recorded and
  with the captured screens stripped: **0** differing pixels. The run time
  keys a CHR RAM tile by data and palette and never reads `ChrBankId`, so
  today's merged id does not draw wrong art.
- **Size.** Castlevania goes from 21 to 29 pages (+38 496 B, +0.9 % of
  `auto/`) and Zelda from 19 to 26 (+30 764 B, +1.8 %). Contra is
  unchanged: it records from a state, and the state load already rehashes.
  Excitebike is byte-identical.
- **Layout.** 3 128 of 3 211 (Castlevania) and 2 146 of 2 154 (Zelda)
  keys move to another cell.
- **CHR artist kit.** This is where the options differ.
  - `artist_chr_kit` already expects a content hash per bank. When every
    id is 0, it falls back to recovering banks "for an older recorder"
    and marks them unknown, so they are never donated to by `--also`.
  - On today's packs, the recovered banks are chimeras. Castlevania's
    largest agrees with its closest real CHR state on 57 of 184 indices
    and contradicts it on 116, and each recovered bank mixes 5–6 real
    states.
  - With the rename, each bank is one real state with a known identity,
    and the identities matched across two recordings of the same route.
  - CHR completeness goes from 49 % to 76 % on Castlevania, and from 75 %
    to 53 % on Zelda. Zelda's higher number today comes from those mixed
    pages, and one of its fixed banks is the nearly empty power-on state.
- **Collisions.** Among the drawn states there are 0 with the rename
  (7 / 7 / 2 states). The rotating sum is structurally weak, though:
  swapping two tiles whose indices have the same parity never changes it.
  #460's guard fired on 2 + 1 keys without the rename and on none with it.
- **Speed.** Rehashing on every pixel after a `$2007` write, including
  forced-blank uploads, costs about 4 % wall clock on Castlevania (32 767
  rehashes) and is within noise on Zelda.

Non-goals: the loader, the `<tile>` format, and CHR ROM games. #460's guard
stays in every option.

## Decision

**(a) via (c1), keeping #460's guard.** The CHR RAM bank id follows the CHR
state, and the hash is recomputed only when a tile is about to be recorded,
never on every write.

1. **The override is fixed.** `HdBuilderPpu::WriteRam` is spelled as the
   PPU's virtual and marked `override`, so the compiler refuses a repeat of
   the 2022 misspelling. A `$2007` write below `$2000` marks the bank hashes
   stale, and so does a state load.
2. **The rehash is lazy.** `MesenSheets::ChrBankHashes`
   (`Core/NES/HdPacks/ChrBankHashes.h`, host-free) rehashes the 8 KB pattern
   space only when a CHR RAM tile is next recorded while the hashes are
   stale. It also stays stale while a `$2007` write is still pending: NesPpu
   commits the byte a few PPU cycles after the CPU write, and a tile drawn in
   that window must not settle the id without it. A CHR ROM game never
   hashes: its bank is its CHR ROM bank number.
3. **The hash is unchanged.** It is the same rotating sum, so an all-zero
   bank is still 0. A stronger hash stays out of scope.
4. **#460's guard stays** (`ChrPageSlots.h`), as the backstop for real
   collisions of that weak hash.
5. **A re-record over a pack recorded before the fix migrates, on redraw.**
   Bank 0 is now the id of an all-zero bank, and an all-zero bank draws only
   blank tiles. So a CHR RAM tile on bank 0 with any set pattern bit can only
   come from a pre-fix pack (`IsPreFixChrRamTile`). When the loaded pack
   holds one, each of its bank-0 tiles, blank ones included, moves to the
   bank it is drawn from the next time it is drawn (`RehomesOnRedraw`). A
   tile the session never draws again stays on bank 0. The builder logs both
   counts at save. A pack recorded since the fix never moves a tile.
6. **`artist_chr_kit` regroups only what predates the fix.** A bank-0 page
   with a non-blank tile (`recorded_before_bank_fix`) takes the structural
   "older recorder" regroup, with its identity unknown, even beside pages
   with real ids. A pack with real ids and no such page leaves that mode
   entirely. The power-on bank's page of blank tiles is a real bank.

### Options considered

The options as proposed, with what the measurement said about each:

**(a) Fix the override.** Rename to `WriteRam … override`, so a CHR RAM
write marks the banks for rehash.

- Benefits:
  - The bank id and the page are one real CHR state.
  - `artist_chr_kit` reads the pack as designed: identities known,
    `--also` donation possible, no chimeric pattern tables.
  - #460's guard becomes a backstop for real collisions rather than a
    path taken on every power-on recording.
- Costs:
  - +7–8 PNG pages and +0.9–1.8 % bytes on these games.
  - Every CHR RAM recording is re-laid out once. `hires.txt` differs in
    cell/bank/index on nearly every line. Any hand edit to a
    `chr/Chr_*.png` page of an existing CHR RAM pack no longer lines up
    after a re-record.
  - Zelda's CHR completeness headline drops (75 % → 53 %, about 66 %
    without the empty power-on bank).
  - About 4 % slower recording on an upload-heavy game.
- Re-recording over a pack recorded before the fix was open at proposal
  time. It was measured before shipping; see "Re-record over a pack
  recorded before the fix".

**(b) Keep today's behaviour plus #460's guard.**

- Benefits:
  - No change to any existing pack or layout.
  - No extra pages, no rehash cost.
  - In game, identical to (a).
- Costs:
  - `ChrBankId` stays a constant that means nothing.
  - `artist_chr_kit` stays on its fallback for every CHR RAM recording:
    mixed-state pages, fills pinned from them, no `--also` donation.
  - The guard keeps moving tiles off their CHR index (2 and 1 keys here).
    That is invisible in game but visible on the kit's pages.

**(c) A middle option.** The suggested middle, "fix it but merge identical
duplicates", has nothing to act on. The rename creates no duplicates: every
key keeps exactly one cell, and the extra pages hold only moved tiles. So
(c) as suggested is the same as (a).

Two middles the data do support:

- **(c1) (a), with the rehash on the draw path.** Mark the banks dirty on
  the write, and rehash only when a tile is about to be processed. Same
  output as (a), without the forced-blank rehash cost. Not prototyped.
- **(c2) Keep (b)'s recorder, and fix the kit side only.**
  `artist_chr_kit` would stop presenting its recovered banks as pattern
  tables. It would, for example, warn, or group by tile rather than by
  page. Existing packs are untouched. However, without a state id the kit
  cannot recover which tiles shared a pattern table, so this only makes
  the fallback honest. It does not make it right.

### Recommendation (the author's, at proposal time)

**(a), implemented as (c1)**, keeping #460's guard.

- The measured benefit is real for the artist: pattern tables that
  existed, and a known bank identity.
- The costs are one-time layout churn and a few percent of bytes.
- Nothing changes in game and no key moves.

Choose (b) instead if churn in existing CHR RAM packs' `chr/` pages
outweighs the CHR kit. Either way, a CHR RAM re-record over an old pack
should be measured before (a) ships.

### What the implementation measured (2026-09-25)

The full tables are in the two logs named at the top.

- **(c1) gives (a)'s output, minus a stale byte in (a).** On Castlevania
  60 s and Zelda 85 s the lazy build has the same 29 and 26 `chr/` pages, the
  same keys and cells, and the same bank ids as the prototype (a), except
  one bank id per game. The prototype rehashed on the pixel right after the
  last `$2007` write of an upload, before NesPpu committed that byte. A probe
  build that keeps (a)'s eager rehash but waits out the pending write gives
  `hires.txt` byte-identical to the lazy build. Contra and Excitebike are
  byte-identical to the unfixed build (the whole `auto/` tree).
- **Rehashes.** A probe counted 4 rehashes in 60 s of Castlevania: one per
  CHR state a tile was actually drawn from. The eager (a) did 32 767.
- **Speed.** It is within noise of the unfixed recorder. A probe binary
  switched the fix off with an environment variable, and printed the CPU
  time before and during `SaveHdPack`. Over four alternating pairs, the
  recording phase took 19.6–23.8 s with the fix and 22.6–26.8 s without it
  on Castlevania (means 22.3 / 23.9). On Zelda it took 29.5–31.6 s against
  25.2–32.9 s, apart from one outlier under load. The save phase differs by
  about 0.05 s for the extra pages. Runs across different binaries were confounded
  by other sessions on the machine (load average 30–54) and by a fixed run
  order, so the claim rests on the same-binary pairs.
- **Rebased on `main` at `8e0a22ab` (#470/#471).** The shipped tree against
  that `main` gives Castlevania 20 → 29 pages with 6 drawn bank ids and
  Zelda 19 → 25 with 5, the same `<tile>` rules as `main` apart from cell,
  bank and index. Zelda has one page fewer than above because #470 no
  longer records fully transparent sprite tiles. Contra and Excitebike stay
  byte-identical to `main` (the whole `auto/` tree). Seven Castlevania wall
  clock pairs between the two binaries, in alternating and ABBA order at
  load 6–20: `main` 15.8–24.0 s (mean 20.1), the fix 19.5–22.4 s (mean
  20.8). The gap is smaller than `main`'s own spread.

### Re-record over a pack recorded before the fix (measured 2026-09-25)

Plain `hdpack` recordings: first with the unfixed build (the old pack),
then again into the same folder with the fixed one.

- **Without migration, the old layout is frozen, not mixed.** Every key is
  already in the loaded pack, so the builder only bumps its usage and the
  tile keeps bank 0. On Castlevania all 616 non-blank tiles stayed on bank
  0: the fix would never reach an existing pack.
- **With migration (decision item 5):**
  - Castlevania, same route: 630 of 630 bank-0 tiles moved. The `hires.txt`
    is **byte-identical** to a fresh recording with the fixed build (25
    pages, 6 bank ids). A second re-record over it is byte-identical again,
    and logs nothing.
  - Castlevania, 60 s over a 90 s old pack: 630 of 694 moved. The 63
    non-blank tiles drawn only after 60 s stay on bank 0.
  - Zelda: 505 of 575 moved. The copied folder carried the first run's
    battery save, so this run took another route from its second sync
    sample: it drew 2 new keys and missed 68 old non-blank ones.
- **What the kit sees on the leftovers.** The kit before this ADR had no
  rule for a mix. On the Zelda re-record it pooled the 9 leftover bank-0
  pages into one bank of *known* identity 0, a chimera that `--also`
  could pair with a donor. The kit now regroups them structurally, into 2
  banks of unknown identity, and never donates to them. On the Castlevania
  60 s / 90 s re-record, the leftover pages were read as `unreadable` by
  both kits, and one of them now becomes a regrouped bank. Fresh and fully
  migrated packs have no bank of unknown identity.
- **Why migrate rather than refuse.** Refusing would block every re-record
  of an existing CHR RAM pack, and explaining alone leaves the pack frozen,
  as measured above. Migration on redraw needs no guess: a tile moves only
  to the bank it is seen being drawn from. ADR-0160 §3 already migrates a
  pack being re-recorded, and never a pack that is only read.

## Consequences

- Every CHR RAM recording made from here on is laid out by real CHR states:
  more `chr/` pages (Castlevania 21 → 29, Zelda 19 → 26 on the bootstrap
  recipe; 20 → 29 and 19 → 25 on `main` after #470), the same keys, the same pixels in game.
- An existing CHR RAM pack is untouched until it is re-recorded. A
  re-record migrates what it draws again, and a clean layout needs a
  recording into an empty folder; the builder's save log says so.
- A hand edit to a `chr/Chr_*.png` page of a pre-fix CHR RAM pack no longer
  lines up after a re-record. Sheets and backgrounds are unaffected, and they
  are the artist surface (ADR-0153).
- `artist_chr_kit` keeps its structural fallback for packs, or parts of
  packs, recorded before the fix, and uses real bank identities everywhere
  else, so `--also` can pair banks across recordings.
- The rotating-sum hash can collide on same-parity tile reorders, so #460's
  guard stays. A stronger hash would change every bank id again, and is out
  of scope here.
- The contract is stated in `Core/AGENTS.md` (the bank-id bullet) and
  `scripts/AGENTS.md` (`artist_chr_kit.py`).
