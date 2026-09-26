# Deep re-measurement of Excitebike, Castlevania and Punch-Out!! on `main` `ceab80a9` (2026-09-25)

**Date:** 2026-09-25
**Baselines:** `docs/validation/excitebike-deep-measurement-2026-09-24.md`,
`docs/validation/castlevania-deep-measurement-2026-09-24.md`,
`docs/validation/punchout-deep-measurement-2026-09-24.md`, all measured on
`main` `46b9136a`.
**Binary under test:** `main` `ceab80a9` (the merge of #485).
**This is an addendum, not a revision** of the three baseline logs. Apart
from a one-line pointer under each title and one corrected figure in the
Punch-Out!! log (below), nothing in them was edited.

## What ran and why

The three published measurements predate the work numbered #449–#488
(issues and PRs), which changed the recorder's sprite-half naming, blank-tile
handling, CHR bank ids, sheet folds/variants (F14.9, ADR-0230) and the
rebuild of untouched cells (ADR-0231). 74 commits separate `46b9136a` from
`ceab80a9`.

Each game was re-run with its baseline doc's own protocol and commands,
steps 1–4: mint, two recording passes with a determinism check, the F14.4
palette gap, the artist kit with every `--verify`, and the painted round
trip with an in-game screenshot. The baseline sessions' scratch scripts and
packs were still on disk and were reused, with paths changed, so the
baseline column below is the published log and, where the source says so,
also re-derived from the baseline's own files.

Verification:

- All three results were checked independently by a second (Sonnet) pass
  and matched, except a few sub-metrics the verifier could not reproduce;
  they are marked *not independently reproduced* below. One Castlevania
  typo (a truncated sha256) was corrected.
- Earlier Haiku attempts (`../excitebike`, `../castlevania`, and Punch-Out!!'s
  `pass-a/`, `pass-b/`) were wrong or unproven and are not used anywhere here.

## Binary and provenance

| Item | Value |
|---|---|
| Worktree | `/Users/bihaiko/remeasure-ceab`, detached at `ceab80a92b5a566b058e4e27a188e2b3756f4608` |
| `InteropDLL/obj.osx-arm64/MesenCore.dylib` | sha256 `a3f1018caed39269e09beff889635b2bce03addb17cfbacda40394f279aad80a` (all three games) |
| `scripts/headless_record` | sha256 `1bc62ecb76f16d185ae952f038fee038a1136f19b2ecf9f31ac7965a6fba1422`; `otool -L` resolves the worktree dylib |
| Castlevania private copies | relinked with `install_name_tool` and ad-hoc signed: dylib `a6e76735…727097`, `headless_record` `beec6e0200e6fc0e6b1a0d8c5a6a453030b48a7f6918397553930132aca87b5b` |
| #474 (`2f8507344`) | `nm` + `c++filt` finds 4 `HdShapeKey` hash-table instantiations |
| #488 (`61aa9c3e8`, ADR-0232) | `nm` finds `MesenSheets::ChrBankHashes::BankIdOf<…HdBuilderPpu::DrawPixel…>`; `strings` finds the ADR-0232 builder message |
| Absent at baseline | `git grep` finds 0 hits for either identifier in `Core/` at `46b9136a` |
| #479 (`05ab41f70`) | `OamFetchLatch::SpriteRowIsPlaced` is header-inline and has no symbol. Behaviour is consistent with it; its presence is not proven by `nm` |

ROMs are the same dumps as the baselines: Excitebike sha1
`2e9897846e54a4a9865e87de7517c6710bdec255`, Castlevania sha1
`7a20c44f302fb2f1b7adffa6b619e3e1cae7b546`, Punch-Out!! file sha1
`0F0C2C2E294FF8CE255A618964728C935FD8963B` (No-Intro
`B6F6BD9D78CDD264A117D4B647AEE3309993E9A9`). The mints of Castlevania
(`stage1.mss`) and Punch-Out!! (`fight1.mss`) are byte-identical to the
baselines'. Every game's two recording passes are `diff -r` clean.

## Excitebike

| Metric | 46b9136a | ceab80a9 | Delta |
|---|---|---|---|
| Keys / drawn keys | 868 / 356 | 865 / 353 | −3 / −3 |
| Drawn keys on a sheet cell | 337/356 = 94.7 % | 347/353 = 98.3 % | +3.6 pt |
| Drawn keys served by a cell or `folds` | n/a | 353/353 = 100 % (base 335, variant 12, fold 6) | – |
| Drawn keys with no cell | 19 (16 BG / 3 sprite) | 6 (4 / 2), all 6 in `folds` | −13 |
| Variant cells (`variantOf`) | 0 | 10 | +10 |
| `artist_kit --verify` | 868 → 337 → 337 PASS | 865 → 353 → 353 PASS | +16 carried |
| `artist_chr_kit` recorded / ROM fill | 498 (97 %) / 14 | 335 (65 %) / 177 | #449 fixed |
| CHR `seen:true` cells on a never-drawn index | 175 | 0 | −175 |
| Poses / HUD-excluded / laid out | 58 / 2 / 53 | 58 / 4 / 51 | rider excluded (#493) |
| Wheel cycle `cycle000` on a kit sheet | `usr000`, 14 figures | none | regression (#493) |
| Painted vs control `hires.txt` | byte-identical | 1 rule differs, by design (ADR-0231) | changed |
| In game frame 1323: magenta px painted / control | 144 / 0 | 144 / 0 | 0 |
| Px that differ, painted vs control | 144 | 150 (144 magenta + 6 xBRZ fringe) | +6 |

Bugs:

- **#449** (CHR kit marks never-drawn indices as recorded): **fixed** by PR
  #469 (`f177fd826`); 0 `seen:true` cells on a never-drawn index. The issue
  is closed.
- **#493** (new, filed from this run): the rider's main pose pair
  (`pose001`/`pose002`) is classified as HUD and left out of the kit, so no
  sheet carries the wheel cycle. At baseline the blank sprite tile `0xFC`
  (`screenFixed: false`) was a member of both poses and masked the ADR-0173
  pinned-to-screen test; #470 stopped registering blank sprite halves, and
  the remaining tiles are all `screenFixed: true`. Knock-on effects: the
  painted import lands in `sheets/sprites.png` (which ARTIST.md tells artists
  not to paint) and build2/build3 on the painted arm warn "size not a whole
  number of cells". The wheel cycle's 772 → 768 repeats and a new 10-phase
  `cycle007` are likely the same cause, not proven.
  *Correction:* attributing the `sprites.png` landing to #493 is
  incomplete; it recurs without #493 in SMB3 and Ninja Gaiden, filed as
  #498 (see [smb3-ninjagaiden-deep-measurement-2026-09-25.md](smb3-ninjagaiden-deep-measurement-2026-09-25.md)).

## Castlevania

| Metric | 46b9136a | ceab80a9 | Delta |
|---|---|---|---|
| Recording frames | 361 → 5170 (4 809) | 361 → 5171 (4 809) | 0 |
| `<tile>` keys / drawn (gameplay) | 3 033 / 452 | 3 014 / 433 | −19 / −19 |
| Drawn keys on a sheet (gameplay) | 341/452 = 75.4 % | 424/433 = 97.9 % | +22.5 pt |
| Drawn keys on a sheet (idle 60 s) | 532/628 = 84.7 % | 583/607 = 96.0 % | +11.3 pt |
| Missing drawn keys, gameplay / idle | 111 / 96 | 9 / 24, all listed in `folds` | −102 / −72 |
| Colourways among missing, gameplay / idle | 91 / 61 | 0 / 0 | −91 / −61 |
| Variant cells, gameplay / idle | 0 / 0 | 81 / 51 | new |
| Sheet keys absent from `hires.txt` | 7 keys (13 entries) | 0 | −13 |
| Kit parts / files / cells | 4 / 148 / 14 312 | 4 / 145 / 14 583 | −3 / +271 |
| Poses / laid out / figure sheets | 273 / 100 / 18 | 256 / 115 / 14 | – |
| Simon walk `cycle002` (period, repeats) | 28 f ×30 | 28 f ×30 (the 7/7/7/7 hold split not independently reproduced) | = |
| Loose whip-segment figures | 4 | 0 | −4 |
| CHR real banks / completeness | 2 / 93 % | 3 / 89.7 % | +1 / −3.3 pt |
| `artist_map --verify` | FAIL (B2) | PASS, 8 875/8 875 byte-identical | fixed |
| Painted vs control `hires.txt` | byte-identical | 3 rules of 1 key differ, by design (ADR-0231) | changed |
| In game frame 2766: changed / exact `#FF00FF` px | 848 / 672 | 848 / 848 | +176 exact |
| Whole-figure paint | build2 exit 1 (B3) | build2, build3 exit 0, byte-identical | fixed |

Bugs (the baseline's unfiled drafts, plus #447):

- **B1** (recorder sheet cells name sprite keys never drawn): **absent**.
  The doc's 20 s repro gives 241 entries, 0 leftover (80 s: 573/0; idle:
  534/0). Candidate fixes #450 (`a43fa5cd4`) and #468 (`67891cb86`,
  `448f35c5a`), whose messages describe B1's cause; not bisected.
- **B2** (`artist_map.py --verify` always fails): **absent**. Fixed by #451
  (`0ed9d3d63`), the only commit on that path.
- **B3** (`mep_figure import` into blank member tiles): **absent**.
  Candidates #452 (`20a21bf24`) and #464 (`a3424d4e0`); not bisected.
- **#447** (rebuilt layer draws NN crops over xBRZ): **absent**. The
  unpainted control rebuild renders identically to the recorded pack at
  frames 1564, 2766 and 5170, and 1 073/1 073 control rules match `auto/`.
  Fixed by `130d2c38d` (ADR-0231).
- Still present: a whole-figure paint warns that 2 painted keys are already
  claimed by another crop (#343, known; build exits 0).

## Punch-Out!!

| Metric | 46b9136a | ceab80a9 | Delta |
|---|---|---|---|
| Keys in `hires.txt` / drawn keys | 9 847 / 1 655 | 9 815 / 1 623 | −32 / −32 |
| Drawn sprite / BG keys | 516 / 1 139 | 484 / 1 139 | −32 / 0 |
| Drawn shapes | 1 009 | 999 | −10 |
| Drawn keys on any sheet, baseline method | 1 003 = 60.6 % | 1 552 = 95.6 % | +35.0 pt |
| Same, counting `folds` | 60.6 % | 1 623 = 100 % | +39.4 pt |
| Drawn keys on an organised sheet (not `unsorted`) | 501 = 30.3 % | 657 = 40.5 % | +156 |
| Missing drawn keys, baseline method | 652 on 402 shapes | 71 on 37 shapes, all pairwise inert, all in `folds` | −581 |
| Variant cells | 0 | 500 | +500 |
| Sheet keys never drawn (bug C) | 7 | 0 | −7 |
| Drawn keys with no cell | 9 | 0 | −9 |
| CHR kit recorded / ROM fill | 7 562 / 630 | 1 000 (12 %) / 7 192 | consistent with #469, not isolated |
| Poses / figure sheets | 34 / 13 | 34 / 13 | 0 |
| Torn H-mirrored figures (bug B view) | pose005, pose023 | none | fixed |
| Painted vs control `hires.txt` | byte-identical | 2 rules of 1 key differ, by design (ADR-0231) | changed |
| Magenta px in game, loose, no backdrop spike | 128, Glass Joe hidden | 1 024, Glass Joe drawn | +896 |
| Magenta px in game, layered | 0 | 1 024 | +1 024 |

*Not independently reproduced:* the variant cells carrying 553 keys, and the
baseline's `compute_folds` BG/sprite fold-vs-own split (304/243, 11/94). The
totals around them (500 variant cells; 315 fold / 337 own) were reproduced.

Bugs (the baseline's unfiled drafts):

- **A** (rebuilt BG layer bakes the backdrop opaque and hides Glass Joe):
  **fixed**. Control, plain and painted rebuilds are pixel-identical to
  `auto` in the Joe box; a forced arm moving all 59 floor rules to `sheets/`
  still draws Joe (build logs the #456 "keep colour 0 transparent" line).
- **B** (flip not un-baked on index-keyed packs): **fixed**. Forced arms
  repainting 20 H-mirrored cells give 0 mirrored crops; the build logs
  "un-baked 20 mirror crop(s)".
- **C** (MMC2 registry names the wrong bank): **fixed**. Sheet keys never
  drawn 7 → 0, drawn with no cell 9 → 0, `1E80`–`1E82` now `0580`–`0582`.

**Baseline doc correction:** the Punch-Out!! log gave the grid dump as
"592 K lines". Both the baseline's and this run's dumps have 4 046 508
lines (confirmed by the verifier); the baseline log is corrected in place.

## Cross-game findings

1. **F14.9 lifts on-sheet coverage to ~96–98 %, and to 100 % counting
   folds.** Drawn keys on a sheet: Excitebike 94.7 → 98.3 %, Castlevania
   75.4 → 97.9 % (idle 84.7 → 96.0 %), Punch-Out!! 60.6 → 95.6 %. Every
   remaining missing key in all three games is listed in a sidecar `folds`
   entry, which the F14.4 metric does not credit; crediting it gives 100 %.
   Attributed to `909fe0a95` (F14.9, ADR-0230), which introduced `folds` and
   `variantOf`. Colourways among missing keys dropped to 0 in Castlevania
   and Punch-Out!!, and Excitebike's 12 colourway/brighter keys are now
   variant cells.
2. **The protocol's "painted `hires.txt` byte-identical to control" check no
   longer holds, by design.** Under ADR-0231 (#447) untouched cells keep the
   recorded `chr/` rule, and only the painted key's rules move to the sheet:
   1 rule in Excitebike, 3 in Castlevania, 2 in Punch-Out!!, always one key,
   key sets equal. The check should become "only the painted key's rules
   differ". A side effect seen in Excitebike: painting a cell also drops its
   xBRZ fringe (6 px).
3. **Drawn sprite keys fell in every game** (Excitebike −3, Castlevania −19
   gameplay / −21 idle, Punch-Out!! −32); BG keys did not change, and no key
   was added. Attribution only where evidenced:
   - Excitebike: `8B`/`FC` on `FF20160F` are gone because of #470 (their CHR
     is all zero). `FE`/`FF20190F` is not attributed (candidates #450/#468
     or #479; not bisected).
   - Punch-Out!!: #474 is ruled out (its branch alone drops 0 keys). 22 keys
     (blank-slot `xxFD`–`xxFF` plus `05FD`/`05FE`) went in a window holding
     #468/#476 and #483; 10 real-art keys in a window holding #484, #486 and
     #487 (#479). Per-PR pinning was not done, and the window boundaries come
     from file timestamps.
   - Castlevania: every lost key is a sprite palette of a shape still drawn
     in another palette; consistent with #450/#468/#479, not bisected.

## What this does NOT prove

- It does not pin any un-bisected change to one PR: the lost sprite keys,
  B1 and B3 fixes, Castlevania's third CHR bank (consistent with ADR-0232 /
  #488), Punch-Out!!'s CHR-kit "recorded" drop (consistent with #469), and
  the cycle/sequence count changes in all three games.
- It does not prove #479 is in the binary (no symbol).
- It does not show that painted art is good, only that it reaches the
  screen; the magenta paint is a probe.
- Wall clock is not comparable for Castlevania (passes ran concurrently).
- Three games, one route each; nothing here generalises to other mappers or
  CHR RAM games beyond Castlevania.

## Raw material

Unversioned, under `/Users/bihaiko/remeasure-ceab/runs/remeasure/`:
`excitebike-opus/`, `castlevania-opus/` and `punchout/v2/`, each with a
`RESULTS.md` (exact commands, logs, scratch scripts, kits and screenshots).
The Haiku directories next to them (`excitebike/`, `castlevania/`, and
Punch-Out!!'s `pass-a/`, `pass-b/`, `PunchOut/`) are superseded.
