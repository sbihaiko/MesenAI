# ADR-0232: A CHR RAM bank id names the CHR state a tile was drawn from, or stays a layout constant (#467)

- Status: accepted — decided and implemented the same turn, 2026-09-25 (issue #467); the user's pick *"(a) via (c1) (Recommended)"*, go-ahead *"em paralelo corrija os bugs, mande pro main e limpe os WTs"*. The decision lives in `Core/NES/HdPacks/ChrBankHashes.h` and the tests named under `## Record`.
- Date: 2026-09-24 (proposed), 2026-09-25 (accepted)
- Related: issue #467 (root cause of #460), issue #460 / PR #473 (`Core/NES/HdPacks/ChrPageSlots.h`), ADR-0160 §3 (re-record, and the dropped-tile guard), ADR-0230 (palette variants, F14.9), PRD Part A F9.24 (`artist_chr_kit`)
- Measurement: `docs/validation/adr-0232-chr-ram-bank-hash-measurement-2026-09-24.md` (the options, and the re-record case); implementation: `docs/validation/issue-467-adr0232-chr-bank-id-2026-09-25.md`

## Decision

**(a) via (c1), keeping #460's guard.** The CHR RAM bank id follows the CHR state, and the hash is recomputed only when a tile is about to be recorded, never on every write.

1. **The override is fixed.** `HdBuilderPpu::WriteRam` is spelled as the PPU's virtual and marked `override`, so the compiler refuses a repeat of the 2022 misspelling. A `$2007` write below `$2000` marks the bank hashes stale, and so does a state load.
2. **The rehash is lazy.** `MesenSheets::ChrBankHashes` (`Core/NES/HdPacks/ChrBankHashes.h`, host-free) rehashes the 8 KB pattern space only when a CHR RAM tile is next recorded while the hashes are stale, and stays stale while a `$2007` write is still pending (NesPpu commits the byte a few PPU cycles after the CPU write; a tile drawn in that window must not settle the id without it). A CHR ROM game never hashes: its bank is its CHR ROM bank number.
3. **The hash is unchanged.** It is the same rotating sum, so an all-zero bank is still 0. A stronger hash stays out of scope.
4. **#460's guard stays** (`ChrPageSlots.h`), as the backstop for real collisions of that weak hash.
5. **A re-record over a pack recorded before the fix migrates, on redraw.** Bank 0 is now the id of an all-zero bank, and an all-zero bank draws only blank tiles, so a CHR RAM tile on bank 0 with any set pattern bit can only come from a pre-fix pack (`IsPreFixChrRamTile`). Each of its bank-0 tiles, blank ones included, moves to the bank it is drawn from the next time it is drawn (`RehomesOnRedraw`); a tile the session never draws again stays on bank 0; the builder logs both counts at save. A pack recorded since the fix never moves a tile.
6. **`artist_chr_kit` regroups only what predates the fix.** A bank-0 page with a non-blank tile (`recorded_before_bank_fix`) takes the structural "older recorder" regroup, with its identity unknown, even beside pages with real ids; a pack with real ids and no such page leaves that mode entirely. The power-on bank's page of blank tiles is a real bank.

The contract is stated in `Core/AGENTS.md` (the bank-id bullet) and `scripts/AGENTS.md` (`artist_chr_kit.py`).

## Context

`HdBuilderPpu` hashes each CHR RAM bank when a tile is drawn after `_needChrHash` was set; the recorder uses that hash as the tile's `ChrBankId`, decides which `chr/Chr_*.png` page the tile's cell goes on (`HdPackBuilder::AddTile`), and writes it as the trailing bank field of every CHR RAM `<tile>` line. The method meant to set the flag on a CHR write is spelled `WriteRAM`; the PPU's virtual is `WriteRam`, so it overrode nothing since the 2022 port. The flag was thus set only at construction and after a state load: a power-on recording keyed every drawn tile under the hash of an all-zero bank, 0, and a later tile at the same index evicted a drawn key (#460). #460's guard (PR #473) stops the eviction without touching the hash; renaming the method makes the bank id follow the CHR state instead, re-laying out every CHR RAM recording and adding pages — a trade-off, not a fix in passing.

**Measured** (Castlevania 60 s idle, Zelda 85 s and Contra stage 1, each with and without the rename, both on #460's guard; Excitebike CHR ROM as control). The rename adds no key, loses no key, changes no pixel, creates no duplicate: all 3 211 / 2 154 / 1 834 keys have byte-identical crops in both builds, including the 272 and 42 tiles on the new pages, and `sheets/` and `backgrounds/` are byte-identical; 23 frames rendered with each pack differ in **0** pixels. Size: Castlevania 21 → 29 `chr/` pages (+38 496 B, +0.9 % of `auto/`), Zelda 19 → 26 (+30 764 B, +1.8 %); Contra is unchanged (it records from a state, which already rehashes), Excitebike byte-identical. Layout: 3 128 of 3 211 and 2 146 of 2 154 keys move cell. The kit is where the options differ: with every id 0 it recovers banks "for an older recorder", marks them unknown, and `--also` never donates to them; those recovered banks are chimeras (Castlevania's largest agrees with its closest real CHR state on 57 of 184 indices and contradicts it on 116, mixing 5–6 real states), while the rename gives each bank one real state with a known identity. CHR completeness 49 % → 76 % on Castlevania, 75 % → 53 % on Zelda. Drawn states show 0 collisions (7 / 7 / 2 states), though the rotating sum is structurally weak — two tiles swapped with the same index parity never change it; #460's guard fired on 2 + 1 keys without the rename and none with it. Rehashing every pixel after a `$2007` write costs about 4 % wall clock on Castlevania (32 767 rehashes) and is within noise on Zelda.

Non-goals: the loader, the `<tile>` format, and CHR ROM games. #460's guard stays in every option.

## Options considered

- **(a)** Rename to `WriteRam … override`, so a CHR RAM write marks the banks for rehash. One real CHR state per bank; the kit reads the pack as designed; #460's guard becomes a backstop rather than a path taken on every power-on recording. Cost: +7–8 PNG pages and +0.9–1.8 % bytes, one re-layout of every CHR RAM recording, Zelda's headline down (75 % → 53 %, about 66 % without the empty power-on bank), about 4 % slower recording on an upload-heavy game.
- **(b)** Keep today's behaviour plus #460's guard. No pack or layout change, in-game identical to (a) — but `ChrBankId` stays a constant that means nothing, the kit stays on its fallback (mixed-state pages, no `--also` donation), and the guard keeps moving tiles off their CHR index (2 and 1 keys here).
- **(c)** The suggested middle ("fix it but merge identical duplicates") has nothing to act on — the rename creates no duplicates — so it is the same as (a). Supported middles: **(c1)** (a) with the rehash on the draw path, same output without the forced-blank cost (picked); **(c2)** fix only the kit side, which makes the fallback honest. It does not make it right: without a state id the kit cannot recover which tiles shared a pattern table.

The author recommended **(a) as (c1)** at proposal time.

## Record

- 2026-09-24 — proposed; user's instruction verbatim *"Medir antes (Recommended) — Escrevo uma ADR proposed e meço se as páginas extras trazem peças que hoje ficam erradas ou só duplicam arte. Só depois você decide."*
- 2026-09-25 — accepted as (a) via (c1), implemented the same turn. Tests: `scripts/core_unit_tests.cpp` (`TestAChrRamBankIdFollowsTheChrState`, `TestAChrRamBankIdIsRehashedPerDrawnTileNotPerWrite`, `TestTheChrPageGuardStillHoldsOnAChrRamHashCollision`, `TestAPreFixChrRamTileIsRecognisedAndRehomed`) and `scripts/test_artist_chr_kit.py` (the three `ADR-0232` cases).
- 2026-09-25 — (c1) gives (a)'s output minus a stale byte (the prototype rehashed before NesPpu committed the upload's last `$2007` byte). A probe counted 4 rehashes in 60 s of Castlevania against the eager 32 767, and printed CPU time before and during `SaveHdPack`: over four alternating same-binary pairs the recording phase took 19.6–23.8 s with the fix and 22.6–26.8 s without it (means 22.3 / 23.9); Zelda 29.5–31.6 s against 25.2–32.9 s; the save phase differs by about 0.05 s for the extra pages. Rebased on `main` at `8e0a22ab` (#470/#471): Castlevania 20 → 29 pages, Zelda 19 → 25, Contra and Excitebike byte-identical to `main`; seven Castlevania pairs gave `main` 15.8–24.0 s (mean 20.1) against the fix 19.5–22.4 s (mean 20.8).
- 2026-09-25 — the open re-record case, measured with plain `hdpack` recordings: without migration the old layout is frozen (Castlevania's 616 non-blank tiles stayed on bank 0); with migration 630 of 630 bank-0 tiles moved and the `hires.txt` is byte-identical to a fresh fixed-build recording, 630 of 694 on a 60 s / 90 s run, 505 of 575 on Zelda. Migrate rather than refuse because refusing blocks every re-record of an existing CHR RAM pack; ADR-0160 §3 already migrates a pack being re-recorded, never one only read. The kit now regroups leftover pages it read as `unreadable` structurally, instead of pooling them into a bank of known identity 0.

## Consequences

- Every CHR RAM recording from here on is laid out by real CHR states: more `chr/` pages (Castlevania 21 → 29, Zelda 19 → 26 on the bootstrap recipe; 20 → 29 and 19 → 25 on `main` after #470), the same keys, the same pixels in game.
- An existing CHR RAM pack is untouched until it is re-recorded; a re-record migrates what it draws again, and a clean layout needs a recording into an empty folder (the builder's save log, `[HD Pack Builder] ADR-0232: ...`, says so).
- A hand edit to a `chr/Chr_*.png` page of a pre-fix CHR RAM pack no longer lines up after a re-record. Sheets and backgrounds are unaffected, and they are the artist surface (ADR-0153).
- `artist_chr_kit` keeps its structural fallback for parts recorded before the fix and uses real bank identities everywhere else, so `--also` can pair banks across recordings.
- The rotating-sum hash can collide on same-parity tile reorders, so #460's guard stays; a stronger hash would change every bank id again and is out of scope.
- Recording speed: the (c1) lazy rehash is within noise of the unfixed recorder, where (a)'s eager rehash costs about 4 % wall clock.
