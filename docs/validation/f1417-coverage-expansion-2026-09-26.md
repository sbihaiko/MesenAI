# Coverage past the first stage, wave two: eight more games on the ADR-0239 selector sweep (2026-09-26)

**Date:** 2026-09-26
**Binary under test:** the tree at `be9a236db` (`main`, F14.16) for the profiles
and the sweep tool, and the Core dylib the recorder loads by absolute path,
`/Users/bihaiko/VSCodeProjects/MesenCE/InteropDLL/obj.osx-arm64/MesenCore.dylib`.
That dylib was rebuilt at **12:55:45** by a sibling session *while this wave was
recording*, which is a defect of this wave's evidence and gets its own section
below. No Core change ships in this slice: `git diff --name-only` against `main`
is `scripts/`, `docs/` and nothing else, so every session ran on the shipped
recorder.
**ADR:** `docs/adr/0239-coverage-past-the-first-stage-comes-from-a-selector-swept-per-game-and-is-measured-as-a-union.md`
**PRD slice:** F14.17 (Part A).
**Raw material:** unversioned, under `runs/f1417/<game>/` — `RESULT.md` (the
authority for every figure below, with its own reproduction block),
`summary.json`, `before/`, `sweep/` (one directory per session, holding the
bootstrap pack the recorder wrote, the final `.mss` the check was read off and
the recorder's own `rec_stdout.log`), the sweep and rescore logs, and `probe/`
(the exploratory recordings). `runs/f1417/SURVEY.md` is the candidate survey for
the games this wave did **not** take.

## Headline

Every place a selector reaches in these eight games is now in a pack, and the
figures are the ADR-0239 §5 union of that with the same game's stage-1 route.
One command with a per-game profile; the recorder is the shipped one.

| game | sessions | drawn keys (written) before → after | written tile data before → after | ROM CHR, written rules before → after | §5.3 reference |
|---|---|---|---|---|---|
| Mega Man | 6 | 480 → **1125** (+645) | 381 → **906** (+525) | n/a (CHR RAM) | refused — patched-ROM pack |
| Mega Man 2 | 8 | 1134 → **2807** (+1673) | 642 → **1733** (+1091) | n/a (CHR RAM) | — none on disk |
| Metroid | 11 | 800 → **1656** (+856) | 215 → **478** (+263) | n/a (CHR RAM) | refused — CHR RAM |
| Dr. Mario | 21 | 1375 → **1420** (+45) | 1188 → **1218** (+30) | 636/1093 (58.2 %) → **657/1093 (60.1 %)** | — none on disk |
| Ice Climber | 31 | 481 → **668** (+187) | 292 → **349** (+57) | 276/476 (58.0 %) → **322/476 (67.6 %)** | — none on disk |
| Lemmings | 16 | 1733 → **3743** (+2010) | 1522 → **3243** (+1721) | 1422/6729 (21.1 %) → **2956/6729 (43.9 %)** | — none on disk |
| Super Mario Bros. | 8 | 203 → **509** (+306) | 138 → **243** (+105) | 128/487 (26.3 %) → **231/487 (47.4 %)** | — none on disk |
| Tetris | 18 | 536 → **961** (+425) | 344 → **348** (+4) | 325/832 (39.1 %) → **329/832 (39.5 %)** | — none on disk |

119 sessions of 120 emulated seconds each. The every-rule columns that §5.1
keeps beside these (the builder's `defaultTile` placeholders) are in each game's
own `summary.json`; `--rom-chr` prints both.

**The gain is not the point of the table; the shape of it is.** Two rows say
more than the eight: Dr. Mario's 21 virus levels buy +45 keys, of which 23 are
the clipboard's own digits, and Tetris' 18 level/height combinations buy +425
keys and **+4** tile data — what a level select adds to these games is palette
and counters, not tile. Lemmings, whose selector reaches sixteen different
*levels* rather than six settings of one screen, triples its tile data instead.
A sweep that only reported "keys went up" would have made those three look alike.

## What each profile does

Each game's `RESULT.md` is the authority; this is the shape of it, and why the
ladder stopped at the rung it did (ADR-0239 §1).

- **Mega Man** — rung 1, the game's own STAGE SELECT, no cheat. The six portraits
  are a **ring**, not the 3×2 grid they look like: Right from CUT MAN walks
  GUTS MAN → ICE MAN → BOMB MAN → FIRE MAN → ELE MAN → CUT MAN, measured, and
  Down is inert at every position. The screen ignores the pad for its first ~60
  frames, so the entries settle for 240. Six sessions; all six `ramCheck` pass on
  `$0031` (DataCrystal's "Stage ID", 00/05/01/02/03/04). `cutman` is
  `did-not-warp` **by construction** — it is the baseline re-recorded — and is
  kept as a value because the selector reaches it.
- **Mega Man 2** — rung 1, its own stage select, eight stage values, no cheat.
  All eight `ramCheck` pass on `$002A` (3,1,4,2,7,5,6,0 — each value distinct and
  covering 0–7), read back independently with `scripts/mss_ram.py`. `bubbleman`
  is the identity value and its pack is byte-identical to the baseline's on all
  five recorded files, so its `new == 0` is a tautology of the identity and not a
  failed warp. §5.2 is `n/a (CHR RAM)`: the header declares 0 CHR banks
  (MMC1B2, NES-SGROM, 8 KB CHR RAM).
- **Metroid** — rung 1, the game's own **password screen**, so nothing is pinned
  anywhere in the chain: the selector is the string. Eleven codes from published
  sources cover all five areas (Brinstar 2, Norfair 2, Kraid 2, Ridley 2, Tourian
  3). The `ramCheck` is *derived*, not typed: the ROM computes
  `$0074 = (byte 8 & 7) | (byte 8 & 0x10)`, verified against the traces and
  against the published description of eight codes. See "the gate and the proof"
  below — this game is where that distinction was measured.
- **Dr. Mario** — rung 1, the `VIRUS LEVEL` row of a 1 PLAYER menu (the booklet
  and the ROM's own screens agree on the path). Twenty-one values, 00–20; every
  one of the twenty above 00 passes §4's gate with `new` between 8 and 32.
- **Ice Climber** — rung 1, the title menu's `MOUNTAIN 01` row, walked with the
  D-pad. Thirty-one values, MOUNTAIN 02–32 (01 is the baseline). Rung 2 was
  available — DataCrystal publishes `$0059`, the same index the menu writes — and
  was not used, because §1 takes the first rung that reaches the place and rung 1
  reaches all 32 on the game's own routine.
- **Lemmings** — rung 1, the `ACCESS CODE` screen behind `NEW LEVEL`. Sixteen
  codes, each six letters of the screen's 21-letter alphabet, typed by holding Up
  for `8 × index` frames — the ROM's autorepeat counter does not reset on release,
  which is what makes that the arithmetic. The codes are not from a web source:
  the ROM carries its own 100-entry table at PRG `0x1964`, and four independent
  checks agree (the table parses, the in-game preview prints the title and rating
  it holds, the release rate matches the preview on all 16, and the final state's
  `$0054`–`$0059` reads the typed code back byte for byte). The check is the
  reconstruction `$00A1 × 25 + $00A0 + 1`.
- **Super Mario Bros.** — rung 2, a published RAM selector (`$075F` = current
  world − 1, 0-based, DataCrystal via a Wayback snapshot), because this game has
  no stage select at all: only warp zones, which are reached by playing. Eight
  values, worlds 1–8. The `ramCheck` is `$00E7`, the *level layout address the
  game resolved* — not the byte the cheat pins, which is the point of choosing
  it. `world1-1` is the identity value: it pins the value the game boots with and
  scores `new` exactly 0, which makes it both §3's inert control and the proof
  that the baseline is the same run without the pin. **The dump is not retail** —
  see the caveats.
- **Tetris** — rung 1, the A-TYPE level select and the B-TYPE height select.
  Eighteen values. Rung 2 was available (`$0047` starting speed, `$0059` B-type
  height are both published) and was not needed. The copyright notice's
  unskippable window was re-measured with the instrument the claim is about — the
  screen the ROM is drawing, five arms — rather than assumed.

## The gate and the proof are different instruments

ADR-0239 §4's gate is `new`: a session that adds nothing the baseline does not
hold did not warp. This wave measured the **converse failing**. Metroid's profile
started from a twelfth code (`4F---- ------ ------ ------`) that is republished as
a Brinstar password; its 24 characters do not reconcile the format's checksum, so
the ROM's own routine refuses it. The session sat on the password screen for its
whole 120 seconds (`$1D` = 1 at every sample, `$0074` = 0) and still reported
**143 `new` drawn keys** — the characters and the cursor the blind body typed onto
the keyboard, which no baseline pack holds.

So a non-zero `new` is not, by itself, evidence that the session went anywhere:
a selector screen draws art of its own, and the selector's art lands in the
union. What says a session arrived is the `ramCheck` read off its own final
state, and for Metroid the check is the extra instrument that catches this. §4's
own sentence ("`new == 0` means did-not-warp") stays true; what this measures is
that its converse does not hold. The same measurement bounds Metroid's headline
row: of the +856 keys, **138 are the password screen itself** (the same 138 for
six different passwords, each present in the session pack), so at most 718 are
the five areas. The value is gone from the profile and the generator now refuses
any password whose checksum does not reconcile, so the mistake cannot come back
as a session that spends a minute of capture budget being refused inside the
emulator.

## The baseline must come from the same binary as the sessions

ADR-0239 §5 asks for the `before` side to be *re-recorded on the same binary* as
the sessions, because the builder's output is a property of the build. This wave
started before a sibling session rebuilt the Core dylib at **12:55:45**
(sha256 `e1c556f3…`) and finished after, and the build stamps itself into every
pack it writes: packs from the newer build carry `<bgCellRecord>` lines, packs
from the older one do not.

Audited across all nine sweep folders, that boundary splits exactly one game:

| folder | `before/` | sessions | verdict |
|---|---|---|---|
| Mega Man, Metroid, Lemmings, Ice Climber, SMB1 | no `bgCellRecord` | no `bgCellRecord` | both sides on the older build — internally consistent |
| Tetris | present | present | both sides on the newer build; the sweep noticed and re-recorded its own baseline |
| **Mega Man 2** | **no** `bgCellRecord` (12:13:22) | **present** (13:02–13:04) | **the two halves were different builds** |
| Dr. Mario | no `before/` on disk — its baseline is `sweep/level-00` | present | one build |

Mega Man 2's baseline was re-recorded on the current dylib and the game was
re-scored against it. The correction is in the table above and in its `RESULT.md`,
which keeps both readings side by side rather than quietly replacing the first:
the old one read 1139/621 → 2837/1735 and the corrected one reads **1134/642 →
2807/1733**, the difference being exactly the 30 keys and 2 tile data that only
the older baseline held. `bubbleman` — the identity value — comes out byte-identical
to the new baseline on all five recorded files, which is the strongest form of the
check Mega Man's `cutman` passes.

The other six folders are internally consistent, and the cross-checks that exist
inside them support the older build having been stable across their windows:
Mega Man's `cutman` (12:18) is `diff -rq`-identical to its baseline (12:08:36),
and SMB1's `world1-1` (12:16) scores `new` exactly 0 against its baseline
(12:13:53). What cannot be re-verified is the identity of that older build: it
has been overwritten and no hash of it was taken. Their numbers are comparisons
within one build; they are not comparable with a pack recorded on today's build,
and re-recording their baselines now would *introduce* the mismatch this section
is about.

## What the sweep does not reach

Named per game in each `RESULT.md`; the recurring ones:

- **Depth inside a stage.** A session is one 120-second window from the place the
  selector chose. A stage whose art lies past that window is not in the pack.
- **The transition frames.** A pinned or typed selector never shows the frame the
  game draws while moving between stages; bodies do finish stages, and those
  frames are what a pinned selector cannot show (Castlevania's caveat, F14.16).
- **Mega Man's Dr. Wily fortress** — it follows the six stages rather than being
  selected, so it is rung 3 and was not attempted.
- **Dr. Mario's 2 PLAYER mode** — measured and *not* swept: its 116 CHR indices
  are outside the vocabulary of the whole 21-session sweep (against 30 for the
  twenty levels and level 00 combined), and the rendered indices include `LEVEL`,
  the digits, virus faces and pill halves. It is reported as the gap worth more
  than the row it would extend.
- **Metroid's Tourian endings** — three codes cover it; the other endings are
  gameplay outcomes, not selector places.
- **Lemmings' 84 remaining levels** — the ROM's table holds 100; sixteen were
  swept, chosen to span the artifact families the probe identified.
- **Ice Climber's 32nd mountain onwards** — the menu counts 1–32 and 01 is the
  baseline, so the sweep is complete for the selector; what it does not reach is
  the bonus stages and the two-player mode.

## Caveats that belong to the numbers, not to the method

- **Super Mario Bros.' dump is not retail.** The file at
  `Super Mario Bros. (1985) (Nintendo).nes` in the ROM library is the hack
  **"Super Mario Bros. Revisited"** — its title screen reads
  `SUPER MARIO BROS. / REVISITED` with the menu `ONE PLUMBER / TWO PLUMBERS /
  TOP- 000000` where the retail game reads `1 PLAYER GAME / 2 PLAYER GAME`. Every
  SMB1 path on this machine shares one hash (whole-file `155C2E09…`, No-Intro
  `B606D2CF…`); there is no retail dump here to compare with.
  `scripts/stages/smb3/stage-set.json` recorded the same finding from the other
  side. The row above is a real measurement on that file — the hack runs on the
  retail engine and the pinned byte was *measured*, not assumed, to load eight
  distinct worlds — but it must not be compared with a community pack keyed to
  the retail ROM. The profile pins `romSha1` and the stage set says so in its
  `note`.
- **No §5.3 row for seven of the eight.** No artist pack exists on disk for
  these ROMs; the only pack beside them is the recorder's own bootstrap, and
  scoring bootstrap against bootstrap measures the builder, not art. Mega Man is
  the exception the other way: an artist pack *does* exist
  (`HdPacks/Mega Man (1987) (Capcom)/`) and the row is **refused**, not absent —
  its `<patch>Megaman - Super.ips,2F883815…` names this dump's whole-file SHA1 and
  `NesConsole.cpp` applies `<patch>` before the game runs, so the pack belongs to
  a patched ROM. On a CHR RAM game a PRG patch reaches the tiles, which is
  ADR-0184 §1's hazard 1. No percentage is printed.
- **CHR RAM games have no §5.2 denominator.** Mega Man, Mega Man 2 and Metroid
  declare 0 CHR banks: `n/a (CHR RAM)`, per ADR-0239 §5.2, not a 0 %.
- **§3's inert control failed on Ice Climber** (RAM trees not byte-identical
  under the identity pin; 385 of 2048 bytes differ), the third game it has failed
  on. No lives pin rides with any session in this wave.
- **Dr. Mario has no `before/` directory**: its baseline is the `level-00`
  session inside `sweep/`, which is the same recording the profile's first value
  makes. The union is taken against that pack.
- **Four play loops were renamed** (2026-09-26, in review of this slice). A
  route's name is how `library_job.start_plan` finds the state it starts from:
  the half before `-run` must be a mint the folder ships, character for
  character (`scripts/stages/README.md`). Three of the eight sets named their
  loop after the screen instead of after a mint — `bottle-run.txt`,
  `mountain-run.txt` and `stage1-run.txt` — so the job pruned their only route
  and the declared sets recorded nothing. They are now `level-00-run.txt`,
  `mountain-01-run.txt` and `brinstar-suitless-run.txt`, each matching its
  profile's own default entry mint. Metroid is the one that was not a naming
  slip: its `stage1-run.txt` was a working power-on route on `main`, and adding
  the eleven `mint-*.txt` files this wave needs turned it into a pruned one,
  because a set that mints its states no longer records from power-on.
  Lemmings' fourth rename (`body.txt` -> `stage1-run.txt`) is the same rule seen
  from the other side: the job's own filter would have offered a file named
  `body` as a route and recorded the title screen from power-on. The guard is
  now in `scripts/test_library_job.py` — every declared set must have at least
  one route the job can start — which is the check whose absence let three sets
  ship declaring nothing to record.
- **Wall-clock figures are not a property of the games**: up to eight sweeps
  recorded in the same worktree at once, and the per-session times show it
  (Dr. Mario 76–368 s for the same 120 emulated seconds).

## Defects this slice found

- **#548 — `--dry-run` wrote, and destroyed a real sweep's record.** Three write
  sites, not one: the `--out` directory, one directory and one generated input
  script per session, and finally `sweep.json` and `--summary`. It cost Mega Man
  a re-recording, and Dr. Mario, Ice Climber and Lemmings each lost a document to
  it. Fixed in #550, with a second sighting inside the fix: `--rescore` ignored
  `--dry-run` entirely and wrote `--summary` (or `<out>/rescore.json`) — the
  shape that costs a summary its per-session `ramCheck` column. Both are now
  guarded and both print their report.
- **#549 — the sweep log called every game a CHR RAM game.** The closing
  paragraph of §5 was printed unconditionally with Castlevania's case baked in;
  Lemmings, a 16-CHR-bank ROM, was told its frozen tile-data column was "the
  builder's PRG scan". Fixed in #550: the sentence is now a function of the ROM
  in hand.
- **#551 — `--rom` is never validated** (a non-iNES file crashes after the §4
  table has printed, a directory passes `exists()`, and a truncated iNES is read
  as CHR RAM because a declared-but-absent CHR bank and a CHR RAM cartridge both
  come back as `b""`). **Open**, filed from the independent verification of #550.
- **The review of this slice found three sets that would have recorded
  nothing.** Dr. Mario's, Ice Climber's and Metroid's only route was pruned by
  `library_job.start_plan` — two because the name matched no mint, one because
  this wave's own mints turned a power-on route into a pruned one. The sets were
  declared, the profiles ran, and the library job would have skipped all three in
  silence. Fixed in this slice (the four renames above), guarded by a new case in
  `scripts/test_library_job.py`, and verified on the real `scripts/stages/`.

## What the sweep cost

119 sessions of 120 emulated seconds each, plus one baseline per game: about four
emulated hours. Wall clock is dominated by contention with the sibling sweeps in
the same worktree — Mega Man 2's eight sessions took 202 s in four parallel jobs,
Lemmings' sixteen took 318 s in six, Tetris' eighteen took 1297 s, Dr. Mario's
twenty-one summed to 3958.9 s of recorder time. `--rescore` re-derives every
figure from the packs on disk with no ROM, no emulator and no state, which is why
the numbers above were recomputed a second time rather than copied from the first
pass.

## Reproducing

Each `runs/f1417/<game>/RESULT.md` carries its own block, with the ROM path, the
`--dry-run` first, then the sweep, then the `--rescore`. The shape is one command:

```sh
python3 scripts/record_navigation_sweep.py \
  --profile scripts/stages/<game>/navigation.json \
  --rom "<library>/<game>.nes" \
  --out runs/f1417/<game>/sweep --baseline runs/f1417/<game>/before \
  --rom-chr --summary runs/f1417/<game>/summary.json
python3 scripts/record_navigation_sweep.py --rescore --out runs/f1417/<game>/sweep \
  --baseline runs/f1417/<game>/before --profile scripts/stages/<game>/navigation.json \
  --rom "<library>/<game>.nes" --rom-chr --summary runs/f1417/<game>/summary.json
```

The ROM library is the user's own and is never copied into the repository. The
eight profiles, their entry/body scripts and their stage sets are versioned under
`scripts/stages/<game>/`; `runs/f1417/` is not.
