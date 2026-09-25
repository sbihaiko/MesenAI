# ADR-0232 — CHR RAM bank hash measured, with and without the override fix (2026-09-24)

Measurement for issue #467, asked for before a human picks an option in
ADR-0232 (`proposed`). User's instruction, verbatim: *"Medir antes
(Recommended) — Escrevo uma ADR proposed e meço se as páginas extras trazem
peças que hoje ficam erradas ou só duplicam arte. Só depois você decide."*

**Decided 2026-09-25:** ADR-0232 is accepted as (a) via (c1), and is
implemented. See section 7 below and
`docs/validation/issue-467-adr0232-chr-bank-id-2026-09-25.md`.

This log measures and records. It decides nothing and ships nothing. The
prototype lived only in the build used for arm B. It is reverted in the
tree, and saved as a patch in the session scratchpad. Raw material
(recordings, renders, kits) is not versioned. This file keeps the numbers
and the method.

## Headline

1. **The extra pages hold no new art and no duplicate art.** Every tile the
   fixed build places, including the 272 (Castlevania) and 42 (Zelda) tiles
   on its new pages, is a key the unfixed build already carries, with
   byte-identical cell pixels. No key is added or lost, no key gets two
   cells, and no cell is shared by two keys, in either build. The fix only
   moves cells: 3 128 of 3 211 keys on Castlevania and 2 146 of 2 154 on
   Zelda change `(png, x, y)`.
2. **In game, nothing changes.** Across 23 frames on the three games, with
   the pack as recorded and with its `<background>` screens stripped, the
   fixed and unfixed packs render **0** differing pixels. Each pack differs
   from the no-pack render by 5 289–190 264 of 983 040 pixels, so the pack
   was loaded. The run time keys a CHR RAM tile by its data and palette.
   `ChrBankId` never reaches matching, so today's merged bank id cannot make
   a rule draw the wrong art.
3. **The difference is in the CHR artist kit.** On the unfixed packs,
   `artist_chr_kit` finds bank id 0 everywhere. It then takes its "older
   recorder" fallback (`_regroup_without_hashes`), and the banks it
   recovers are chimeras. Castlevania's largest recovered bank agrees with
   its closest real CHR state on 57 of 184 indices and contradicts it on
   116. Every recovered bank mixes tiles from 5–6 real states. With the
   fix, every bank is one real CHR state with its identity known.
   Castlevania's CHR completeness goes from 49 % to 76 %. Zelda's goes from
   75 % to 53 %: its unfixed number counts cells of those chimeric pages.
4. **Cost.** Castlevania gets +8 `chr/` pages (+38 496 B, +4.2 % of
   `chr/`, +0.9 % of `auto/`) and Zelda +7 (+30 764 B, +5.0 %, +1.8 %).
   Contra is byte-identical. Every CHR RAM recording is re-laid out: the
   `<tile>` rules are equal only when cell, bank and index are ignored.
   Sheets and backgrounds are byte-identical.
5. **Collisions.** With the fix there are 0 hash collisions among the drawn
   CHR states (7, 7 and 2 distinct states). The hash is structurally weak,
   though: swapping any two tiles whose indices have the same parity leaves
   it unchanged (2 000/2 000 random 4 KB banks). #460's slot guard stays
   useful. It fired on the unfixed runs (2 keys on Castlevania, 1 on
   Zelda), and it never fires on the fixed runs.
6. **Excitebike (CHR ROM) is byte-identical**: the whole `auto/` tree, 108
   files.

## Binaries

- Base: `origin/fix/460-chr-page-slots` at `6eda4e6b` (#460's slot guard,
  stacked on F14.9 `909fe0a9`). Fresh worktree, `make capture-tool` from
  clean with the CommandLineTools toolchain.
- **A (unfixed)**: the tree as is. `MesenCore.dylib` sha256
  `1b0b2a46f8a631f5dc9a591af6b20ba2927e9b2174bacd827f722292db8f15a2`.
  After the prototype was reverted, a rebuild of the two affected objects
  gave the same bytes again. So A is exactly the committed source.
- **B (fixed)**: A plus the one-line patch below. `NesConsole.o` and
  `NesPpu.o`, the two objects that include `HdBuilderPpu.h`, were deleted
  and rebuilt. `MesenCore.dylib` sha256
  `1b18b798a624a354d7d3deff5d2baa64c556d39827879adcbba4d03cad79aebd`.

  ```diff
  -	void WriteRAM(uint16_t addr, uint8_t value)
  +	void WriteRam(uint16_t addr, uint8_t value) override
  ```

- **C (fixed + instrumented)**: B plus an env-gated log
  (`MESEN_ADR467_HASH_LOG`), used only for the collision count. It logs
  frame, bank slot, rotating hash and FNV-1a 64 of the bank bytes, each
  time a tile is drawn from a freshly rehashed state. C's packs are
  byte-identical to B's for all three games.
- Private copies of each `headless_record` were relinked with
  `install_name_tool`, and `otool -L` shows each linking its own dylib.
- **Behavioural proof.**
  - `nm | c++filt` finds `HdBuilderPpu::WriteRam(unsigned short, unsigned
    char)` in B and no such symbol in A.
  - A reproduces #473's Castlevania `hires.txt` (`e51ca7a2…`), with 1
    distinct bank id among drawn tiles.
  - B has 6 distinct bank ids on Castlevania and 5 on Zelda.

## Recordings

These use the recipes of the F14.9 and #460 logs:

- **Castlevania**: 60 s from power-on,
  `bootstrap hdpack-off log`, with the grid and OAM dumps.
- **Zelda**: 85 s, with `mint-stage1.txt` + `stage1-run.txt`.
- **Contra**: mint 30 s with `mint-stage1.txt` and `save-state=`, then 61 s
  with `stage1-probe.txt` from the state (5 471 frames).
- **Excitebike**: 40 s from power-on.

The ROM sha1 values are `7a20c44f…`, `3701381a…`, `c9ea66bb…` and
`2e989784…`. Every run ended `result: ok`.

- **Reproducibility.** Castlevania and Zelda were recorded three times per
  arm (one A, B and C pass, then two timing passes each). Each arm's
  `hires.txt` was byte-identical across its passes:
  - A: `e51ca7a2…` and `c665f942…`;
  - B = C: `b477b53a…` and `ab81b13f…`;
  - Contra, all arms: `6f8e6c4e…`.

## 1–2. Size, keys, coverage

| | Castlevania A → B | Zelda A → B | Contra A → B |
|---|---|---|---|
| `chr/Chr_*.png` pages | 21 → **29** | 19 → **26** | 16 → 16 |
| `chr/` files (pages + `.orig` twins) | 42 → 58 | 38 → 52 | 32 → 32 |
| `chr/` bytes | 919 181 → 957 677 (+4.2 %) | 616 938 → 647 702 (+5.0 %) | 670 462 (same) |
| `auto/` files | 354 → 370 | 162 → 176 | 161 (same) |
| `auto/` bytes | 5 409 988 → 5 458 956 (+0.9 %) | 2 074 069 → 2 111 591 (+1.8 %) | 1 733 778 (same) |
| `sheets/` files / bytes | 257 / 2 441 584, byte-identical | 104 / 879 815, byte-identical | 125 / 804 620, byte-identical |
| `backgrounds/` | byte-identical | byte-identical | byte-identical |
| `<tile>` rows / distinct keys | 3 793 / 3 211, both | 2 387 / 2 154, both | 2 423 / 1 834, both |
| drawn keys (`N`) | 630 / 630 | 575 / 575 | 259 / 259 |
| drawn keys on sheets | 630/630 → 630/630 | 575/575 → 575/575 | 259/259 → 259/259 |
| distinct bank ids among drawn rows | 1 → 6 | 1 → 5 | 2 → 2 |
| rules equal, ignoring cell/bank/index | yes | yes | yes (whole `auto/` byte-identical) |

- Contra records from a save state, and the state load already rehashes
  (`Serialize`). The probe never changes CHR RAM after the load (2 rehashes
  in C), so the fix changes nothing there.

## 3. Classification of the tiles the fixed build places

The method: for every key in B, crop its cell from B's page and the same
key's cell from A's page, both at the pack's scale 4, and compare the RGBA
bytes.

| | Castlevania | Zelda | Contra |
|---|---|---|---|
| keys in B | 3 211 | 2 154 | 1 834 |
| identical to A (same key, same pixels) | **3 211** | **2 154** | **1 834** |
| a different shape (today mis-keyed or merged) | 0 | 0 | 0 |
| new (absent from A) | 0 | 0 | 0 |
| ... of which on B's new pages | 272, all identical | 42, all identical | – |
| keys whose cell moved | 3 128 | 2 146 | 0 |
| keys with 2+ cells (A / B) | 0 / 0 | 0 / 0 | 0 / 0 |
| cells shared by 2+ keys (A / B) | 0 / 0 | 0 / 0 | 0 / 0 |
| identical crop under another key (A / B) | 30 / 30 | 152 / 152 | 67 / 67 |

**B cell vs A cell at the same `(png, x, y)`.** This is the "same index"
comparison.

- Castlevania: 83 same key, 2 433 a different key, 423 empty in A, 272 on a
  page A lacks.
- Zelda: 8, 496, 1 608 and 42.

The pages are simply re-dealt.

The "identical crop under another key" count is the same in both builds.
These are palettes that render the same RGB (the folds of ADR-0230), not
something the fix adds or removes. So the answer to "do the extra pages
hold art that is wrong or missing today?" is **no**. They hold the same
tiles, split by the CHR state they were drawn from.

### Slots the #460 guard had to move

A `N` row is filed off its CHR index when its cell slot is not
`TileIndex % 256`.

- **A**: 2 keys on Castlevania (`E4C2A191…`/`FF16250F` from index 95 to
  slot 119, and `FE7F3F3E…`/`FF16250F` from 11 to 155) and 1 on Zelda
  (`00071F77…`/`FF202C08` from 0 to 1). These are exactly #460's keys.
- **B**: 0 on all three games.

## 3b. In game

The method: the recorded `auto/` folder was placed beside a copy of the ROM
(the ROM-sibling layer, `NesConsole::LoadHdPack`), with `audio/` removed.
Each frame was then rendered with the unfixed binary's `headless_record …
screenshot` (the renderer is untouched by the fix). Every run logged
`[MEP] textures: auto layer loaded`.

- **Variants per frame**: no pack; A's pack; B's pack; and A's and B's
  packs with every `<background>` line stripped ("tiles only"), so that
  every pixel comes from `<tile>` rules.
- **Frames**:
  - Castlevania: 5, 10, 15, 20, 30, 40, 50 and 60 s;
  - Zelda: 10, 20, 30, 40, 50, 60, 70 and 85 s, on the same input;
  - Contra: 5, 10, 20, 30, 40, 50 and 61 s from the state.
- **Comparison.** Pixels were counted on 1024×960 PNGs. The no-pack frame
  was upscaled ×4, nearest-neighbour.

| | frames | A vs B | tiles-only A vs B | pack vs no-pack (range) |
|---|---|---|---|---|
| Castlevania | 8 | **0 px** | **0 px** | 13 744 – 190 264 |
| Zelda | 8 | **0 px** | **0 px** | 5 289 – 48 092 |
| Contra | 7 | **0 px** | **0 px** | 64 778 – 67 049 |

Does today's merged bank id make a rule draw the wrong art? **No.**

- The loader reads `ChrBankId` from a CHR RAM `<tile>` line, but
  `HdNesPack` never uses it for matching.
- The builder's `DrawTile` writes each tile's own pixels into its cell.
- #460's guard means no slot is overwritten.

The bank id reaches only two places: the page layout, and a re-record that
reloads an existing `hires.txt` into the builder (ADR-0160 §3). The
re-record case was not measured.

## 4. Bank-hash collisions

The data come from build C's log. A drawn state is the CHR bank content a
tile was drawn from, after a rehash.

| | Castlevania | Zelda | Contra |
|---|---|---|---|
| rehashes in the fixed build | 32 767 | 11 156 | 2 |
| points where a tile was drawn from a new state | 4 | 4 | 1 |
| distinct drawn bank contents (two 4 KB slots) | 7 | 7 | 2 |
| distinct rotating hashes for them | 7 | 7 | 2 |
| **hashes naming 2+ different drawn contents** | **0** | **0** | **0** |
| unfixed: ids those contents share | 1 (`0`) | 1 (`0`) | the 2 taken at the state load |

- **Unfixed.** The id is the hash taken at the first rehash (power-on: all
  zero, so `0`). All later states share it: 7, 8 and 2 contents under one
  id per slot. That is the merge #460 guards.
- **Structural weakness.** `hash = rotl(hash + byte, 1)` over 4 096 bytes
  gives each byte a rotation of `(4096 − offset) mod 32`. Two tiles 32
  bytes apart therefore enter with the same rotation. On 2 000 random
  banks:
  - swapping tile `k` with tile `k+2`: same hash 2 000/2 000;
  - swapping `k` with `k+32`: 2 000/2 000;
  - swapping `k` with `k+1`: 0/2 000.

  A game that reorders same-parity tiles in CHR RAM would collide every
  time. None of the three recordings did. #460's guard is what keeps a
  collision from evicting a drawn key.
- **Cost of the rehash.** Most rehashes happen during forced-blank CHR
  uploads. `_needChrHash` is set on each `$2007` write below `$2000`, and
  `DrawPixel` rehashes 8 KB on the next pixel even with rendering off.
  Wall clock, sequential and uncontended, two passes each:
  - Castlevania: A 17.4/17.7 s, B 18.0/18.9 s (≈ +4 %);
  - Zelda: A 21.5/23.5 s, B 21.8/22.8 s (within noise).

  Moving the rehash to the draw path (rehash only when a tile is about to
  be processed) would give the same output without that cost. This was
  not prototyped.

## 5. Artist impact

The kit recipe is the F14.9/Contra one, run on a copy of each recording:
`artist_kit --verify`, `artist_bg_kit --verify`,
`artist_chr_kit --rom --verify`, `artist_kit_assemble`, `mep_lint`, then
`textures/` built twice with `mep_build build`.

| | Castlevania A → B | Zelda A → B | Contra A → B |
|---|---|---|---|
| `artist_kit --verify` | PASS, 630 → 630, 0 lost / 0 invented (both) | PASS, 575, 0/0 (both) | PASS, 259, 0/0 (both) |
| `artist_bg_kit --verify` | 630 → 630, 0/0 (both) | 575, 0/0 (both) | 259, 0/0 (both) |
| `artist_chr_kit --verify` | 3 211 cells byte-identical, rebuilt `hires.txt` identical (both) | 2 154, same (both) | 1 834, same (both) |
| sprite + background kit parts | identical (paths aside) | identical (paths aside) | identical |
| CHR kit pages / cells | 21 / 5 376 → 29 / 7 424 | 19 / 4 864 → 26 / 6 656 | 16 / 4 096 (same) |
| kit files / size | 448 / 6 256 KB → 488 / 6 700 KB | 214 / 2 860 KB → 249 / 3 188 KB | 185 / 2 264 KB (same) |
| assembled kit cells | 5 650 → 7 698 | 4 919 → 6 711 | 4 178 (same) |
| CHR banks: identity known | no (fallback) → yes | no (fallback) → yes | yes (both) |
| CHR recorded / ROM fill / unrecoverable | 377 / 369 / 790 → 566 / 598 / 372 | 526 / 435 / 319 → 283 / 390 / 607 | 174 / 137 / 201 (same) |
| **CHR completeness** | **49 % → 76 %** | **75 % → 53 %** | 61 % (same) |
| `mep_lint` | 0 errors, 0 warnings (both) | same | same |
| `mep_build` ×2 | 0 errors, 76 warnings, second build byte-identical (both) | 0 errors, 31 warnings, identical (both) | 0 errors, 31 warnings, identical (both) |

**Why the CHR numbers move.** `artist_chr_kit` groups a CHR RAM pack's
pages by the bank id on its `<tile>` lines. When every real page carries 0,
`bank_identity_unknown` is true. The kit then recovers banks from pages
that "never disagree about a tile index" (`_regroup_without_hashes`, whose
note reads *"this pack records no CHR bank hash (an older recorder)"*).
It also marks those banks `identity_known = False`, so they are never
donated to by `--also`.

On a merged bank, `SortByUsageFrequency` deals each index's most-used
tile, from whatever CHR state drew it, onto the rank-0 page. The recovered
"banks" are therefore not pattern tables the game ever had.

The method: each bank the kit recovers from A was compared with the fixed
pack's banks, which are one CHR state each. The table reports each A
bank's closest single B state.

| A bank (indices) | Castlevania: agrees / contradicts | Zelda: agrees / contradicts |
|---|---|---|
| largest | 184: 57 / **116** | 186: 120 / **46** |
| 2nd | 94: 44 / 29 | 134: 110 / 17 |
| 3rd | 52: 32 / 17 | 104: 87 / 16 |
| 4th | 23: 21 / 1 | 91: 82 / 9 |
| 5th | 16: 13 / 2 | 11: 7 / 4 |
| 6th | 8: 6 / 1 | – |

Every A bank draws its indices from 5 (Zelda) or 6 (Castlevania) different
real states.

- **Zelda's drop.** Zelda's higher unfixed completeness counts cells of
  these mixed pages, and ROM fills pinned from them. B's number is lower
  but describes real pattern tables.
- **One of B's five Zelda banks** is the power-on all-zero state (`0x0`,
  1 recorded tile). It adds 254 of B's 607 unrecoverable cells. Without it
  B is about 66 %.

**`--also`.** A second Zelda recording (70 s, `mint-stage1` +
`stage1-probe`) was used as a donor in both arms. It donated 0 cells either
way, because every donor key was already in the main recording. B's 5 donor
bank ids all equal the main recording's 5, so with the fix a bank's
identity is stable across runs of the same route. That is the precondition
for a donation. A's banks are never donated to by construction.

## 6. CHR ROM control

Excitebike, 40 s, A vs B: the whole `auto/` tree (108 files) is
byte-identical, `hires.txt` `ced55758…`. The override only sets
`_needChrHash`, and a CHR ROM game never reads `_bankHashes` for its key or
layout.

## Scripts (not versioned)

These live in the session scratchpad, `adr467/`:

- `analyze.py`: sizes, keys, sheet coverage, the per-key crop comparison
  and the same-cell comparison.
- `render_cmp.py`: the in-game pixel counts.
- `hashlog.py`: the collisions.
- `kitbanks.py`: the kit's recovered banks against B's.
- `sh/rec.sh`, `sh/render.sh` and `sh/kit.sh`: the recipes above.
- `fix-writeram-override.patch`: the B prototype.
- `fix-plus-hashlog.patch`: the C prototype.

## 7. After the decision: the re-record case, and (c1) against (a) (2026-09-25)

The open question of section 3b, re-recording over a pack recorded before
the fix, was measured before shipping. The full tables are in
`docs/validation/issue-467-adr0232-chr-bank-id-2026-09-25.md`.

- **Without handling, the old pack is frozen, not mixed.** A re-record
  finds every key it draws already loaded and only bumps its usage. All 616
  non-blank Castlevania tiles kept bank 0, and the fix never reached the
  pack.
- **Handled by migrating on redraw.** In a pack that holds a non-blank
  bank-0 CHR RAM tile, which only the pre-fix recorder writes, a bank-0 tile
  drawn again moves to the bank it is drawn from.
  - Castlevania, same route: 630 of 630 moved, and the `hires.txt` is
    byte-identical to a fresh recording's.
  - A 60 s re-record over a 90 s pack left 63 tiles on bank 0.
  - A Zelda re-record that took another route (the folder carried a battery
    save) left 68.
- **The leftovers in the kit.** `artist_chr_kit` now regroups the leftover
  bank-0 pages structurally with their identity unknown. Before, beside real
  ids, it had pooled them into one bank of known identity 0 (9 pages on
  Zelda).
- **(c1) against (a).** The lazy build matches the prototype's pages, keys,
  cells and ids, except one bank id per game. The prototype had hashed that
  bank before NesPpu committed an upload's last `$2007` byte. An eager probe
  that waits out the pending write matches the lazy build byte for byte. The
  lazy build rehashes 4 times in 60 s of Castlevania, where the prototype
  rehashed 32 767 times.
