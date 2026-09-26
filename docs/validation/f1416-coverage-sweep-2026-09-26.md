# Coverage past the first stage: the ADR-0239 selector sweep (2026-09-26)

**Date:** 2026-09-26
**Binary under test:** `99c274673` (`main`), plus the F14.16 work in
`feat/adr0239-coverage-sweep`. The Core is unmodified by this slice; only
`scripts/` and `docs/` changed, so every session ran on the shipped recorder.
**ADR:** `docs/adr/0239-coverage-past-the-first-stage-comes-from-a-selector-swept-per-game-and-is-measured-as-a-union.md`
**PRD slice:** F14.16 (Part A).
**Raw material:** unversioned, under `runs/f1416/<game>/` — `RESULT.md`,
`summary.json`, `before/` (the stage-1 baseline the sweep passed with
`--baseline`), `sweep/` (one directory per session, each holding the
bootstrap pack the recorder wrote). The one uniform table with every
per-session row and the exact commands is `runs/f1416/final-table.md`
(machine-readable: `final-table.json`, and one `<game>-rescore.json` per
game).

## Headline

Every stage of five games that a selector reaches is now in a pack, and the
figures below are the ADR-0239 §5 union of that with the game's stage-1
route. The sweep is one command with a per-game profile; nothing in the Core
changed.

| game | sessions ok / did-not-warp | drawn keys (written) before → after | written tile data before → after | ROM CHR, written rules before → after | reference before → after |
|---|---|---|---|---|---|
| Excitebike | 13 / 0 | 353 → **1775** (+1422) | 335 → **481** (+146) | 309/456 (67.8 %) → **437/456 (95.8 %)** | — |
| Mike Tyson's Punch-Out!! | 14 / 0 | 1623 → **17881** (+16258) | 999 → **5114** (+4115) | 976/7291 (13.4 %) → **4894/7291 (67.1 %)** | — |
| Super Mario Bros. 3 | 8 / 0 | 1571 → **6972** (+5401) | 477 → **1227** (+750) | 406/5530 (7.3 %) → **944/5530 (17.1 %)** | — |
| Ninja Gaiden | 21 / 0 | 2569 → **36991** (+34422) | 668 → **3404** (+2736) | 553/6405 (8.6 %) → **2730/6405 (42.6 %)** | 535/6208 (8.6 %) → **2705/6208 (43.6 %)** |
| Castlevania | 17 / 0 | 703 → **1344** (+641) | 588 → **698** (+110) | n/a (CHR RAM) | 541/2006 (27.0 %) → **637/2006 (31.8 %)** |

"Written" counts only the `<tile>` rules the run itself wrote (last field
`N`); the builder's `defaultTile=Y` placeholders are a constant every pack of
a ROM carries and are excluded. The every-rule numbers, for comparison, are
in `final-table.md`.

The ROM CHR denominators were counted a second time, off the ROM headers
alone (`chr_banks × 8192` bytes at `16 + prg_banks × 16384`, distinct
non-blank 16-byte patterns), independently of the tool: 456 / 7291 / 5530 /
6405 — the same figures, and Castlevania's dump declares 0 CHR banks, which
is the CHR RAM the table reports.

**No session of any game is `did-not-warp`** under §4's gate: `new` — the
drawn keys `(tileData, palette)` the baseline packs lack — is non-zero for
every one of the 73 sessions. `unique == 0` (reported, never a gate) happens
for 10 of Excitebike's 13 sessions and 1 of Ninja Gaiden's 21; those places
share a tileset with their siblings, which is why §4 does not gate on it.

## What each profile does

The driver ladder of §1, applied per game. Rung 1 is the game's own selector
by input; rung 2 is a published RAM selector pinned for the run.

| game | rung | selector | values | RAM check | source |
|---|---|---|---|---|---|
| Excitebike | 1 (input) | title menu (`SELECTION A`/`B`, `DESIGN`) then the `CHALLENGE RACE` track select | 13 | `$004F` racing flag | Excitebike instruction booklet (Nintendo of America, 1985) |
| Punch-Out!! | 1 (input) + 2 (RAM) | the title screen's `CONTINUE` → `PASS KEY` screen for five fights; `$0001`+`$0002`+`$0003` pinned for the other nine | 14 | `$000B`, and `$0002`/`$0003` on the pinned ones | the game's own PASS KEY screen; DataCrystal Punch-Out RAM map |
| SMB3 | 2 (RAM) | `$0727`, current world − 1 | 8 | `$0348` | DataCrystal SMB3 RAM map |
| Ninja Gaiden | 2 (RAM) | `$006D`, current stage | 21 | `$006E` | DataCrystal Ninja Gaiden RAM map |
| Castlevania | 2 (RAM) | `$0028`, current stage | 17 (0x02–0x12) | `$0018` | DataCrystal Castlevania RAM map |

No profile carries a lives pin. §3 permits one, but the inert control failed
on both games it was tried on — Castlevania `002A:04` (166 of 529 files
differ) and Ninja Gaiden `0076:02` (226 of 627) — so the ladder's rung 2 runs
without it and the bodies accept death.

Every value's place was measured on this binary against the game's own
status bar or screen before it was recorded, and the value lists that a RAM
map does not publish (Castlevania's stage numbers, Excitebike's track menu
path) are logged in the profile's `notes[]` with the measurement that fixed
them. Rung 2 was only used where no track selector exists: Excitebike's two
published RAM maps have a *lane* byte and no track byte, which is why it is
the one rung-1 profile.

## What the sweep does not reach

- **Stage-clear transitions.** A pinned selector never shows one (§6,
  ADR-0184 amendment), and the blind bodies pace inside each stage. The
  union metric makes this invisible, so it is stated here: no pack in this
  sweep holds the frames that connect one stage to the next.
- **Bosses and mid-stage rooms no selector reaches.** §1's rung 3 (search,
  chain, movie) is per-room work a later measurement has to name. Ninja
  Gaiden's 21 values cover its 21 stage numbers, not its four acts'
  interiors; Punch-Out's nine RAM-pinned fights start at the bell, not at
  the circuit screen.
- **Excitebike's five main races.** The booklet reaches them only by placing
  third or better in the preliminary race, which is a stage-clear
  transition.

## Caveats that belong to the numbers, not to the method

- **SMB3's reference pack is invalid.** The pack beside that ROM on disk is
  the folder's own `auto/` bootstrap (8192 placeholders + 984 written
  rules), not an artist pack, and §5.3's line reads 100 % on both sides for
  that reason. It is reported as a caveat, not as a row.
- **Excitebike and Punch-Out!! have no artist pack on disk** — only the
  folder's `auto/` bootstrap — so they have no §5.3 row at all. Their
  headline evidence is §5.2, ROM CHR coverage, which needs no third-party
  pack.
- **Castlevania's reference pack targets another build.** Its `pack.json`
  names No-Intro SHA1 `3DCB69A8` (a build with `akuogg.ips`) and the dumped
  ROM is `7A20C44F`, so §5.3's line compares two builds. The CHR RAM game
  has no ROM CHR denominator either, so its table row is the only coverage
  figure it gets.
- **Ninja Gaiden's reference pack is `<ver>100`** (decimal, unpadded rule
  indices) while a bootstrapped pack is `<ver>109` (hex, padded). Until
  #545's fix the tool compared the two dialects' raw `tileData` strings and
  printed a constant 13.6 % for the baseline, for every session and for the
  union; the row above is the same comparison at the 16-byte CHR pattern
  identity the two dialects share, and it moves the baseline too (102/7382
  = 1.4 % under the raw string).
- **The reference denominator is the reference's own written rules.**
  Castlevania's pack carries 2 222 distinct `tileData` strings but only
  2 006 patterns over its written (`N`) rules — the other 251 rules are
  builder `defaultTile` placeholders, a constant of every pack of that ROM.
  Counting them would put the placeholders on both sides of the fraction.
  Castlevania's `RESULT.md` still quotes its pre-#545 line
  (588/2222 = 26.5 % → 685/2222 = 30.8 %); this table is the later run.

## What the sweep cost

Five profiles, 73 sessions, 120 emulated seconds each: about 2 h 30 min of
emulated time in total, recorded over one day on one machine. The 120 s
budget and the repeated body are ADR-0239 §6; the recorder's usual budget is
two minutes.

## Three defects this slice found and fixed

All three are in the slice's own tooling and are fixed here, each with a test
that failed first (the RED-to-GREEN outputs are in the PR body). The first two
were filed as bugs; the third was introduced by this slice's own work-in-
progress snapshot and caught by two workers reading the code independently.

- **#545** — `--reference` compared `tileData` strings across `<ver>` bases,
  so §5.3's guard ("a pack keyed for a patched ROM is refused, not printed
  as 0 %") did not catch a constant, non-zero figure. It now compares by
  CHR pattern identity, and notes the `<ver>` mismatch when the two files
  disagree.
- **#546** — the ramCheck caveat was attached only when the checked address
  was the selector's own, which a `kind: "input"` profile never has, so nine
  Punch-Out sessions read a pinned address bare. The caveat now covers every
  address the session pins, and its wording states what is measured: the pin
  substitutes on the CPU **read bus**, so a byte that reads back other than
  the pin is the game's own value, and one that reads back the pin is either
  the pin or a value the game stored after reading it. Verified on the stored
  Punch-Out sessions: the nine rung-2 sessions carry the caveat and the five
  rung-1 ones do not (`runs/f1416/code/verify546.txt`).
- **The run's `notes[]` block** — a work-in-progress snapshot of this branch
  shipped `main()` with `swept` and `picked` used but never assigned, so a real
  sweep aborted with `NameError` at the last thing before `sweep.json`, after
  every capture had been paid for. `--dry-run` reaches the same block without
  spawning the emulator, and that is the regression test added here.

## Reproducing

Each game is one command. From the repository root, with the ROM in the
library and the packs already recorded:

```
python3 scripts/record_navigation_sweep.py --rescore --out runs/f1416/<game>/sweep \
    --profile scripts/stages/<game>/navigation.json --baseline <baseline pack> \
    --rom "<ROM>" --rom-chr [--reference "<artist pack>/hires.txt"] \
    --summary runs/f1416/<game>-rescore.json
```

`--rescore` reads the packs already on disk and re-scores them; drop it to
record. The five exact command lines, with their exit codes (0), are in
`runs/f1416/final-table.md`. Every figure in the headline table was
re-derived from the stored packs by that table's run, not copied from a
game's `RESULT.md`.
