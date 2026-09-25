# Issue #467 — ADR-0232 implemented: the CHR RAM bank id follows the CHR state (2026-09-25)

ADR-0232 was accepted with option **(a) via (c1)**, keeping #460's guard.
The user's words, verbatim: pick *"(a) via (c1) (Recommended)"*, go-ahead
*"em paralelo corrija os bugs, mande pro main e limpe os WTs"*. This log
holds the red output, the fix, the mutations and the end-to-end numbers.
The measurement that led to the decision is
`docs/validation/adr-0232-chr-ram-bank-hash-measurement-2026-09-24.md`.
Raw material (binaries, recordings) is not versioned.

## What changed

- `Core/NES/HdPacks/ChrBankHashes.h` (new, host-free):
  - `HashChrBank`, the recorder's unchanged rotating sum;
  - `ChrBankHashes`, marked stale by `OnVideoMemoryWrite` / `MarkStale`,
    and rehashed lazily in `BankIdOf`. It stays stale while a `$2007`
    write is pending;
  - `IsPreFixChrRamTile` and `RehomesOnRedraw`, the re-record rules.
- `Core/NES/HdPacks/HdBuilderPpu.h`:
  - `WriteRAM` becomes `WriteRam … override`;
  - the eager per-pixel rehash block is gone, and each `ProcessTile` call
    asks `ChrBankHashes` for the tile's bank id. It passes 0 on CHR ROM,
    which `HdPackBuilder` never reads.
- `Core/NES/HdPacks/ChrPageSlots.h`: `RemoveTileFromChrPages`, and a
  comment that no longer says the hash is never refreshed.
- `Core/NES/HdPacks/HdPackBuilder.cpp` / `.h`: the re-record migration in
  `ProcessTile`, the load-time flag and counter, and the save-time log
  line. The file is 2393 lines, under its ADR-0137 ceiling of 2438, so no raise
  is needed.
- `scripts/artist_chr_kit.py`: `recorded_before_bank_fix`. Only those
  pages take the structural regroup when real ids are present.
- `Core/AGENTS.md` and `scripts/AGENTS.md`: the contract.

## Red before the fix

The helper did not exist before, so the new C++ tests could not compile
against the old tree. The red below is the new tests run against the
helper with the pre-fix behaviour put back, a CHR write that never marks
the banks stale. That is what the misspelled override did. Verbatim:

```
FAIL  ADR-0232: a CHR write between two draws gives the second draw another bank id
FAIL  ADR-0232: a write to bank 1 moves bank 1's id and keeps bank 0's
FAIL  ADR-0232: the first tile after the upload rehashes once
FAIL  ADR-0232: tiles drawn with no CHR write in between reuse the hashes
FAIL  ADR-0232: rehashes are bounded by drawn tiles (one per write burst), not by the 500 writes
FAIL  ADR-0232: the lazy id equals an eager hash of the bank as the tile reads it
1127/1133 cases passed (before the rebase onto #470/#471)
```

The kit test against the unfixed `scripts/artist_chr_kit.py`, verbatim:

```
FAIL ADR-0232: the pre-fix bank-0 page beside it is regrouped, never presented as a known bank: []
46/47 cases passed
```

## Mutations

Each one was applied to the fixed tree, followed by `make core-unit-tests`,
then reverted.

| mutation | failing cases |
|---|---|
| M1 `OnVideoMemoryWrite` never marks stale (the pre-fix behaviour) | 6 (the red above) |
| M2 eager: every CHR write counts a rehash (option (a) as prototyped) | 4: *"8 192 CHR writes and no drawn tile rehash nothing"*, *"the first tile after the upload rehashes once"*, *"tiles drawn with no CHR write in between reuse the hashes"*, *"rehashes are bounded by drawn tiles …"* |
| M3 #460 guard removed (a taken slot is overwritten) | 10, including *"ADR-0232: on a colliding bank id the #460 guard keeps both tiles"* |
| M4 `IsPreFixChrRamTile` ignores the tile data | 1: *"an all-zero tile on bank 0 is the power-on bank's, not a pre-fix tile"* |
| M5 a pending `$2007` write is ignored (`_stale = false`) | 1: *"a tile drawn while the CHR write is pending leaves the id stale, so the next tile sees the committed byte"* |
| M6 `RehomesOnRedraw` ignores whether the pack predates the fix | 1: *"in a pack recorded since the fix bank 0 is the real power-on bank and never moves"* |

## End to end

The binaries were built from this branch's tree, on top of #460 at
`e065f3cf`, with the CommandLineTools toolchain, and each `headless_record`
was relinked to its own dylib:

- **A2**: the base, unfixed;
- **F1**: the lazy fix, no migration;
- **F2**: F1 plus the pending-write rule and the non-blank migration;
- **F3**: the shipped tree;
- **B2**: a probe, (a)'s eager rehash plus the pending-write rule.

A2 reproduces the ADR log's unfixed hashes (`e51ca7a2…`, `c665f942…`,
`6f8e6c4e…`, `ced55758…`).

The recipes are the ADR log's: Castlevania 60 s from power-on, Zelda 85 s,
Contra from its stage-1 state, and Excitebike 40 s, all `bootstrap
hdpack-off log`.

| | Castlevania | Zelda | Contra | Excitebike |
|---|---|---|---|---|
| `chr/` pages, unfixed → fixed | 21 → **29** | 19 → **26** | 16 → 16 | 10 → 10 |
| distinct bank ids among drawn rows, fixed | **6** | 5 | 2 | – |
| fixed `hires.txt` | `96ca1dc7…` (F1 = F2 = F3 = B2) | `b616dc73…` (F1 = F2 = F3 = B2) | `6f8e6c4e…` = unfixed | `ced55758…` = unfixed |
| whole `auto/` tree vs unfixed | re-laid out | re-laid out | **byte-identical** | **byte-identical** |
| vs prototype (a) (`b477b53a…` / `ab81b13f…`) | same 29 pages, same rules, same ids except one bank: `6834328` → `6834228` | same 26 pages, same rules, same ids except one bank: `3551148317` → `3551148827` | same | same |

**Rebased onto `main` `8e0a22ab` (#470/#471).** The same recipes, run with
the rebased tree (F4) and that `main` (A4):

| | Castlevania | Zelda | Contra | Excitebike |
|---|---|---|---|---|
| `chr/` pages, `main` → fix | 20 → **29** | 19 → **25** | 16 → 16 | 10 → 10 |
| distinct drawn bank ids, fix | **6** | **5** | 2 | – |
| `hires.txt`, `main` / fix | `2fef854d…` / `7bf0ebb9…` | `b9eec739…` / `5722220f…` | `2821be64…` both | `0e60948a…` both |
| `<tile>` rules vs `main`, ignoring cell/bank/index | equal (3 745 rows) | equal (2 344 rows) | whole `auto/` tree **byte-identical** | whole `auto/` tree **byte-identical** |

#470 no longer records fully transparent sprite tiles, so Zelda has 43
rows and 7 drawn keys fewer than on the older base. Those tiles filled one
page there, which is why it is 25 pages instead of 26. The bank ids of the
568 drawn keys the two bases share are unchanged.

**Why one bank id differs from the prototype.** NesPpu commits a `$2007`
write about 5 PPU cycles after the CPU write (`_ppuMemoryDataWriteStateMachine`).
The prototype rehashed on the next pixel, so the last byte of an upload
could land after the hash, and the id then named the bank one byte short.
The B2 probe keeps the eager rehash but waits out the pending write, and
its `hires.txt` is byte-identical to the lazy build's. So the lazy build is
(a)'s intended output. F1 is also byte-identical to F3: on these runs the
lazy path never drew a tile inside the pending window.

**Rehashes.** A probe build counting `ChrBankHashes::Recomputes()` gives **4**
on Castlevania 60 s. The ADR log counted 32 767 for the eager prototype.

**Speed.** Wall-clock runs across different binaries were unusable here.
Other sessions held the load average at 30–54. The first five passes also
always ran A2, then F3, then B2, and the times rose in that order in every
pass: Castlevania real A2 16.3–19.2 s, F3 18.5–21.8 s, B2 21.4–24.9 s. A
throwaway probe answered the question inside one binary instead:

- `ADR467_NOFIX=1` makes `WriteRam` skip the stale mark, so the hashes are
  taken once and frozen: the unfixed cost profile.
- `getrusage` CPU time is printed before and during `SaveHdPack`.
- The order alternates on each pass.

CPU in seconds for the recording phase, then the save phase:

| pass | Castlevania fix | Castlevania no fix | Zelda fix | Zelda no fix | load |
|---|---|---|---|---|---|
| 1 | 23.29 / 1.45 | 26.75 / 1.34 | 30.16 / 0.49 | 32.92 / 0.56 | 20→10 |
| 2 | 22.31 / 1.34 | 22.57 / 1.32 | 29.49 / 0.60 | 28.55 / 0.50 | 9→6 |
| 3 | 23.82 / 1.55 | 22.85 / 1.32 | 31.64 / 0.68 | 31.31 / 0.50 | 5.5 |
| 4 | 19.61 / 1.11 | 23.48 / 1.33 | 40.33 / 0.84 | 25.24 / 0.75 | 5–6 |

- Rehashes: 4 with the fix, 1 without.
- Castlevania: the recording phase with the fix is at or below the unfixed
  one (means 22.3 vs 23.9).
- Zelda: within noise, except pass 4's fix run at load 6.3.
- The save phase grows by about 0.05 s for the extra pages.

(The probe's no-fix mode hashes once, at the first drawn tile, so its pack
is not A2's. Only its cost profile is.)

**Speed of the rebased build.** Seven Castlevania wall-clock pairs
(`capture finished`), F4 against A4. Three alternated pairs, then four in
ABBA order, at load 6–20:

| | runs (s) | mean |
|---|---|---|
| A4 (`main`) | 21.3, 20.3, 19.0, 15.8, 24.0, 19.2, 20.8 | 20.1 |
| F4 (fix) | 22.1, 22.1, 19.6, 19.5, 20.1, 19.5, 22.4 | 20.8 |

The 0.7 s gap is inside A4's own 8 s spread. The same-binary probe above
remains the controlled measurement.

## Re-record over a pack recorded before the fix

Plain `hdpack` recordings (scale 1, into `rec-hdpack/`). A2 recorded the old
pack, and the named build then recorded into a copy of that folder.

| | bank-0 tiles moved | `chr/` pages | distinct drawn bank ids | non-blank tiles left on bank 0 |
|---|---|---|---|---|
| Castlevania old (A2) | – | 15 | 1 | 616 |
| … re-recorded by F1 (no migration) | 0 | 15 | 1 | 616 |
| … re-recorded by F2 (non-blank only) | 616 of 616 | 39 | 7 | 0 (14 blank tiles left on 0, one page each) |
| … re-recorded by **F3** | **630 of 630** | **25** | **6** | **0** |
| Castlevania fresh (F3, empty folder) | – | 25 | 6 | 0 |
| F3 re-record again over F3's result | nothing logged | 25 | 6 | 0 |
| Castlevania old 90 s, re-recorded 60 s by F3 | 630 of 694 | 31 | 7 | 63 |
| Zelda old (A2) | – | 23 | 1 | 557 |
| … re-recorded by F3 | 505 of 575 | 33 | 7 | 68 |
| Zelda fresh (F3) | – | 28 | 5 | 0 |

- **Castlevania, same route:** the migrated `hires.txt` is **byte-identical**
  to the fresh recording's, and a second re-record over it is
  byte-identical again.
- **The 60 s re-record over a 90 s pack** keeps the 63 non-blank tiles it
  never drew again on bank 0. That is the designed residue.
- **Zelda's re-record** folder carried the first run's `Zelda.sav`, so its
  sync trace departs from the fresh run at the second sample. It drew 2 keys
  the old pack lacked and missed 68 old ones.
- **Without migration nothing moves.** A re-record only bumps the usage of
  a key it already holds, so the fix would never reach an existing pack.

**The kit on these packs.** Only the CHR RAM banks are listed:

| pack | kit before | kit after |
|---|---|---|
| fresh Castlevania / Zelda | 6 / 5 banks, all identity known | same |
| migrated Castlevania | 6 banks, all known | same |
| old Castlevania / Zelda (all bank 0) | 6 / 4 banks, all regrouped, identity unknown | same |
| Zelda re-recorded by F3 | 7 banks, one of them a 9-page bank 0 of **known** identity (a chimera `--also` could pair) | 9 banks: 2 regrouped with identity unknown, 1 bank-0 of blank tiles, 6 real |
| Castlevania 90 s → 60 s | 6 real banks + 6 leftover pages as `unreadable` | 6 real + 1 regrouped bank of unknown identity + 5 pages `unreadable` |

## Tests

- `make core-unit-tests`: 1183/1183 after the rebase onto `8e0a22ab` (1139/1139 before). The new ADR-0232 cases are listed in
  the ADR's Status line.
- `python3 scripts/test_artist_chr_kit.py`: 49/49 after the rebase (47/47 before). The three new ADR-0232
  cases include the real-bank-id pack leaving the older-recorder regroup.
- `make python-tests`: 62 passed, 0 failed, 0 skipped on `main` `90794ceb` (the recorder files are unchanged since `8e0a22ab`). `make doc-checks`: pass.
