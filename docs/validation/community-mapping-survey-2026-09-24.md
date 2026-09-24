# Community mapping survey — what the 15 accepted packs could add to the automatic mapping (2026-09-24)

The question: *can we read the community's mappings and expand the automatic
mapping?* This is a **measurement only**. No product code changed, no ADR was
written, and no community condition, PNG or colour choice was imported
anywhere. Every pack was read as data, never as instruction.

The binding frame is ADR-0210 §3: a third-party `hires.txt` is an index of
facts about the ROM (palettes always, pattern bytes only for CHR RAM games,
never its PNGs, never its `<condition>` lines), with ADR-0183 §3
(observations, never readings) and ADR-0198 §2/§3 (a `<patch>` pack keys a
different binary) behind it. The survey therefore splits into what is
**allowed today** (§1), what is **forbidden today and measured only as a
yardstick** (§2), and how authors **structure** packs (§3).

## Inputs

- **Catalog.** All 15 rows of `docs/community-packs.json`, downloaded with
  `scripts/fetch_pack.py` (the workflow's allow-list,
  `scripts/pack_host_allowlist.json`, which includes Dropbox per ADR-0187;
  300 MB cap) into a scratch folder outside the repository. **15 of 15 zip
  sha256 match the catalog row.** Zelda's pack is a zip inside the zip; the
  inner `hires.txt` was read.
- **Local installs.** The ten community packs in the MesenCE home `HdPacks/`
  are byte-identical (`hires.txt` sha256) to the catalog downloads. One extra
  local pack, `1942 (1985) (Capcom)`, is not in the catalog and is audio-only
  (16 `<bgm>`, no `<tile>`); it appears in §3 only.
- **Our automatic output**, per game: the bootstrap `auto/` pack of the F12.2
  sweep (`runs/f12.2-sweep/roms/<game>/<game>/auto/textures/hires.txt`,
  `<ver>`109): one recording per game, taken the same way for every game, plus
  the static CHR fill for CHR ROM games. The fill rules are the
  `defaultTile = Y` rules with no condition (ADR-0210 §2); every other rule is
  counted as recorded. For CHR RAM games there is no fill and every rule
  counts as recorded. These are short recordings, so the "new versus ours"
  columns depend on how much play they saw. That is the same caveat the
  F12.12 log gives.
- **Dumps.** The local 30-ROM library. It holds the dump for 12 of the 15
  games. Little Nemo, TwinBee and Donkey Kong Jr. have none, so they appear
  in §2/§3 only.
- **Index-token base.** `<tile>` tokens are decimal at `<ver>` ≤ 102;
  condition tile tokens are decimal at `<ver>` < 104 (`HdPackLoader`,
  two different thresholds). Both are parsed the loader's way. Keys are
  compared as index-keyed or 32-hex separately, never across the two.

### Per-pack identity (licensing and author as the catalog states them)

| # | game | host | zip | `<ver>` | keyed | CHR | `<patch>` lines | dump held | their `<supportedRom>` is our dump | license / author |
|---|---|---|---|---|---|---|---|---|---|---|
| 143 | Castlevania | mediafire | 60.0 MB | 101 | 32-hex | RAM | 3 | yes | none declared | unknown / ? |
| 145 | Ninja Gaiden | drive.google | 0.6 MB | 100 | index | ROM | 0 | yes | yes | unknown / ? |
| 144 | Donkey Kong | mediafire | 5.1 MB | 101 | index | ROM | 0 | yes | yes | unknown / ? |
| 147 | SMB (Paper reskin) | mediafire | 2.3 MB | 104 | index | ROM | 0 | yes | none declared | unknown / ? |
| 137 | Contra (Contra80s) | github | 95.2 MB | 106 | 32-hex | RAM | 0 | yes | yes | unknown / ? |
| 148 | Metroid | mediafire | 294.5 MB | 108 | index | RAM | 5 | yes | yes | unknown / ? |
| 139 | The Legend of Zelda | drive.google | 187.3 MB | 106 | 32-hex | RAM | 1 | yes | no (`DAB79C84…`) | unknown / ? |
| 138 | Mega Man (Megaman-Super) | github | 15.2 MB | 106 | 32-hex | RAM | 2 | yes | none declared | unknown / AxlRocks |
| 140 | Pac-Man | github | 0.4 MB | 100 | index | ROM | 0 | yes | no (`E7D818E1…`) | unknown / ? |
| 141 | Zelda II | github | 10.4 MB | 108 | index | ROM | 1 | yes | none declared | unknown / ? |
| 210 | Little Nemo | dropbox | 0.1 MB | 103 | index | — | 0 | no | — | unknown / ? |
| 207 | Bomberman | dropbox | 0.01 MB | 105 | index | ROM | 0 | yes | yes | unknown / ? |
| 209 | Ice Climber | dropbox | 0.04 MB | 105 | index | ROM | 0 | yes | yes | unknown / ? |
| 211 | TwinBee | dropbox | 6.2 MB | 105 | index | — | 1 | no | — | unknown / ? |
| 208 | Donkey Kong Jr. | dropbox | 0.02 MB | 105 | index | — | 0 | no | — | unknown / ? |

All 15 rows have `license: unknown`. Only Mega Man names an author (the
`docs/community-packs.md` Author column, from mep-meta). **6 of 15 packs carry
`<patch>`.** Every one of the six catalog packs that replaces audio is among
them. That count decides most of §1.

## 1. Allowed today — ADR-0210 §3 as shipped

### Is it wired into the automatic flow?

**No, it is manual only.** Nothing in `UI/`, `Core/`, the makefile, `.github/`
or any other script calls `scripts/mep_import.py index`. The only reference
outside its own file and tests is the refusal message of
`artist_chr_kit.py --static` for a CHR RAM game, which points the user at the
command. Community auto-install (ADR-0146) installs the pack as a playable HD
pack and never feeds the recording, kit or sheets.

### What the shipped tool yields, per pack

`mep_import.py index <their hires.txt> --pack <copy of our auto pack> --rom <dump> --report …`,
run once per pack. The copy is needed because a CHR RAM run writes
`sheets/index.*` beside our manifest.

| # | game | CHR | tool outcome | shapes added | keys once built | palettes taken | palettes new vs our recording | our recording: rules / shapes / palettes |
|---|---|---|---|---|---|---|---|---|
| 143 | Castlevania | RAM | refused — `<patch>` (filter 2) | 0 | 0 | 0 | 0 | 3 783 / 2 673 / 16 |
| 145 | Ninja Gaiden | ROM | report only | 0 (by design) | 0 | 401 | 336 | 4 546 / 794 / 69 |
| 144 | Donkey Kong | ROM | report only | 0 (by design) | 0 | 20 | 8 | 1 064 / 334 / 14 |
| 147 | SMB (Paper reskin) | ROM | report only | 0 (by design) | 0 | 46 | 46 | 357 / 127 / 9 |
| 137 | Contra (Contra80s) | RAM | `sheets/index.*` written | **2 277** | 4 946 | 125 | — | 3 802 / 2 337 / 34 |
| 148 | Metroid | RAM | refused — `<patch>` (filter 2) | 0 | 0 | 0 | 0 | 3 076 / 1 466 / 49 |
| 139 | The Legend of Zelda | RAM | refused — `<patch>` (filter 2) | 0 | 0 | 0 | 0 | 4 796 / 1 612 / 78 |
| 138 | Mega Man | RAM | refused — `<patch>` (filter 2) | 0 | 0 | 0 | 0 | 3 498 / 3 350 / 6 |
| 140 | Pac-Man | ROM | report only | 0 (by design) | 0 | 25 | 11 | 509 / 208 / 15 |
| 141 | Zelda II | ROM | refused — `<patch>` (filter 2) | 0 | 0 | 0 | 0 | 638 / 174 / 8 |
| 207 | Bomberman | ROM | report only | 0 (by design) | 0 | 12 | 2 | 434 / 266 / 10 |
| 209 | Ice Climber | ROM | report only | 0 (by design) | 0 | 25 | 10 | 908 / 272 / 16 |
| 210, 211, 208 | Little Nemo, TwinBee, DK Jr. | — | not run — no dump | — | — | — | — | — |

Totals of the allowed read:

- **Shapes: +2 277, all of them from one pack (Contra80s).** Contra is the only
  CHR RAM game in the catalog whose pack has no `<patch>`. The F12.12 log
  measured +2 583 against a different Contra recording (the F12.2 panel pack,
  2 138 shapes). The order of magnitude holds.
- **Palettes (CHR ROM, 6 packs): 529 taken, 413 of them new versus our
  recordings.** Two facts make this number worth less than it looks:
  - The tool writes no sheet for a CHR ROM game and produces nothing but the
    JSON report. **The palette set reaches no surface.**
  - At run time it is inert. Our fill rules are `defaultTile = Y`, and
    ADR-0210 established that `Y` is the per-rule palette wildcard. A fill cell
    already matches every palette the game uses, so a larger palette set turns
    no miss into a hit.
- **Filter 1 (index range): 0 rules dropped** across the six allowed CHR ROM
  packs. Every index is inside the dump.
- **Filter 2 (`<patch>`): 5 of the 12 packs we can test are refused
  outright.** That includes 4 of the 5 CHR RAM packs, and CHR RAM games are
  the only ones where the index adds shapes.

### Per-index facts for CHR ROM games (a stronger reading, not what the tool does)

The tool keeps only the palette *set*. The pack also states which palette each
index is drawn under. ADR-0210's framing, "which palettes the game puts them
under", covers that pairing. Its Decision text says only "the palette set", so
whether the pairing is in scope needs a sentence of clarification.

| # | game | their in-range indices | our recorded indices | their indices we never recorded | their `(index, palette)` keys not in our recording |
|---|---|---|---|---|---|
| 145 | Ninja Gaiden | 7 382 | 794 | 6 647 | 17 180 |
| 144 | Donkey Kong | 454 | 334 | 120 | 258 |
| 147 | SMB (Paper reskin) | 490 | 127 | 363 | 2 076 |
| 140 | Pac-Man | 372 | 208 | 168 | 404 |
| 207 | Bomberman | 445 | 266 | 179 | 214 |
| 209 | Ice Climber | 446 | 272 | 174 | 535 |
| | **total** | 9 589 | 2 001 | **7 651** | 20 667 |

**7 651 fill cells** (seen: false, painted today under the guessed
`fill_palette()`) have at least one palette the community pack attests for
that exact index. Rendering those cells in an attested palette would make the
sheet legible to an artist. Coverage would not change, because the wildcard
already matches. No tool consumes this today.

Two cautions. SMB shares **0** palettes with our three SMB recordings (46
against 9; the recordings agree with each other). The cause was not
investigated, and every key-level SMB number here is 0 because of it. Pac-Man's
pack declares a different dump (`E7D818E1…`, "Namco, US, 1993") from ours. Its
indices all fall inside our CHR, but they are facts about another revision. The
tool never compares *their* `<supportedRom>` with the dump. It checks only
ours.

### If it ran automatically for every catalog pack matching the loaded ROM

With ADR-0210 unchanged and today's library and recordings:

- **+2 277 shapes / 4 946 keys on one game (Contra)**, drawn onto
  `sheets/index.png` with `source: index, seen: false`. That is the whole shape
  gain.
- **0 change on screen for every CHR ROM game**, because of the wildcard, and
  **0 change on any sheet**, because nothing is written. The per-index reading
  above would give the 7 651 fill cells real preview colours.
- **0 for Castlevania, Metroid, Zelda, Mega Man and Zelda II**, because filter
  2 refuses them.

## 1b. Filter 2 relaxed — measured only (would need an ADR amendment)

The same tool was run on a scratch copy of each `<patch>` pack's `hires.txt`
with the `<patch>` lines removed. To tell the game's own art from the patch's,
each "new" 16-byte shape was then searched for verbatim in the stock dump. The
calibration is that **100% of our recorded shapes are found verbatim in the
stock ROM** for Castlevania (2 673 / 2 673), Mega Man (3 350 / 3 350) and Zelda
(1 612 / 1 612). For these games, a stock shape that is absent from the ROM
bytes is therefore not a stock shape.

| # | game | patch | shapes (keys) if filter 2 were lifted | verbatim in stock ROM | not in stock ROM | note |
|---|---|---|---|---|---|---|
| 143 | Castlevania | `akuogg.ips` ×3 (none targets our dump) | 485 (1 652) | 249 | 236 | — |
| 138 | Mega Man | `Megaman - Super.ips` (targets **our** dump) | 496 (882) | 257 | 239 | all 239 are found in the **patched** ROM: the patch author's art |
| 139 | The Legend of Zelda | `ZeldaHD.ips` (targets `DAB79C84…`, not ours) | 62 (537) | 44 | 18 | — |
| 141 | Zelda II | `Revamp.ips` | 0 shapes; 1 376 palettes (1 374 new) | — | — | 756 rules / 46 indices **out of range**: a patched CHR layout |
| 148 | Metroid | `mmm.ips` ×5 | refused anyway | — | — | index-keyed against a CHR RAM dump. The patch converts the game to CHR ROM, so the namespaces never meet |

- ADR-0210's Decision quotes "Castlevania +485, Mega Man +483, Zelda +53" as
  the CHR RAM gain. The shipped tool reproduces 485 / 496 / 62 **only with
  filter 2 lifted**. Under ADR-0210's own filter 2 all three are **0**, and for
  Mega Man about half of that gain is the patch's art, not the game's. The ADR
  quotes gains that its own mandatory filter blocks.
- With a "verbatim in the stock ROM" guard in place of the wholesale refusal,
  the safe gain is **+550 shapes** (249 + 257 + 44). That guard only works for
  games that store tiles uncompressed. Contra fails the calibration (1 834 of
  2 337 recorded shapes verbatim, 78.5%; its CHR is decompressed from PRG), so
  the guard cannot vet Contra's shapes. Contra needs no vetting anyway, since
  its pack has no patch. For reference, 929 of Contra80s's 2 277 new shapes are
  verbatim in the ROM.

## 2. Forbidden today — conditions as a yardstick only

Nothing below was imported. The counts are what the packs define and
reference. "refs" counts each appearance of a condition in a rule's `[…]`
prefix, negated ones included.

| # | game | defined | memoryCheck* | frameRange | tileAtPosition | spriteAtPosition | tileNearby | spriteNearby | h/vmirror | position* | negated refs | conditioned `<tile>` rules / all |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 143 | Castlevania | 1 122 | 19 / 2 912 | 83 / 1 360 | 67 / 618 | 62 / 114 | 717 / 1 040 | 174 / 450 | — / 336 | — | 59 | 2 481 / 7 582 |
| 144 | Donkey Kong | 1 047 | — | 12 / 38 | 5 / 8 | — | 1 024 / 1 018 | 6 / 12 | — | — | 0 | 437 / 951 |
| 147 | SMB (Paper reskin) | 2 880 | — | 18 / 18 | 14 / 14 | — | 1 436 / 1 436 | 1 412 / 1 412 | — | — | 2 | 1 823 / 3 966 |
| 137 | Contra (Contra80s) | 749 | 376 / 14 919 | 17 / 420 | 46 / 156 | — | 32 / 104 | 278 / 286 | — / 18 | — | 307 | 1 364 / 13 218 |
| 148 | Metroid | 1 238 | 330 / 621 813 | 524 / 136 704 | 42 / 32 858 | 20 / 32 515 | 99 / 4 792 | 219 / 52 404 | — / 86 074 | 4 / 96 | 267 748 | 146 087 / 150 199 |
| 139 | The Legend of Zelda | 3 002 | 389 / 135 436 | 211 / 32 418 | 117 / 37 811 | 458 / 134 | 1 064 / 1 513 | 763 / 833 | — / 6 256 | — | 39 392 | 17 336 / 25 074 |
| 138 | Mega Man | 301 | 198 / 6 704 | 55 / 482 | 46 / 392 | 2 / 517 | — | — | — | — | 384 | 1 373 / 7 349 |
| 141 | Zelda II | 4 679 | 3 564 / 192 827 | 193 / 9 785 | 7 / 1 692 | 2 / 54 | — | 913 / 10 464 | — / 27 490 | — | 59 697 | 26 713 / 26 975 |
| 207 | Bomberman | 1 | — | — | 1 / 2 | — | — | — | — | — | 1 | 0 / 500 |
| 209 | Ice Climber | 183 | — | 179 / 190 | — | — | 4 / 4 | — | — | — | 4 | 196 / 1 137 |
| | **total** | **15 202** | 4 876 / **974 611** | 1 292 / 181 415 | 345 / 73 551 | 544 / 33 334 | 4 376 / 9 907 | 3 765 / 65 861 | — / 120 174 | 4 / 96 | 367 594 | **197 810 / 236 951** |

Cells read *defined / referenced*. `memoryCheck*` counts `memoryCheck`,
`memoryCheckConstant` and the `ppu…` forms. `hmirror`/`vmirror` are built-in,
so nothing is defined. Ninja Gaiden, Pac-Man, Little Nemo, TwinBee and
Donkey Kong Jr. have no conditions at all.

- **83% of all community `<tile>` rules are conditioned**, and Metroid alone
  accounts for 146 087 of them.
- **memoryCheck is 67% of all condition references** (974 611 of 1 458 949). It
  is the reading ADR-0183 §3 forbids. The three types our builder emits
  (tileNearby, spriteNearby, tileAtPosition) are 10% (149 319).
- The packs also reference 3 305 (Metroid), 25 (Zelda), 2 (Ice Climber) and
  1 (Castlevania) condition names that they never define. These are pack data
  defects, noted here and not filed.

### Against our automatic output

This comparison is meaningful only where the key namespaces meet. That holds
for the same dump with no patch, and for 32-hex keys, which are stock art in
the proportions §1b measured. Metroid and Zelda II key a patched CHR layout
and are marked n/c. Nearby facts are compared at shape level (palette
ignored). "Reachable" restricts the reference to facts whose shapes both occur
in our recording, so a short recording is not scored for what it never saw.

| # | game | namespace | their conditioned keys | we hold the key | **we condition it too** | our conditioned keys | nearby-conditioned subjects: theirs reachable / also ours | tileNearby ±8 px facts, direction-normalised: theirs reachable / agree / ours | exact nearby facts (type, offset, target): theirs reachable / agree / ours |
|---|---|---|---|---|---|---|---|---|---|
| 137 | Contra | same dump | 613 | 192 | **139** (72% of held) | 384 | 87 / 66 | 10 / 0 / 235 | 174 / 5 / 522 |
| 144 | Donkey Kong | same dump | 108 | 57 | **33** (58%) | 245 | 53 / 29 | 64 / **44** / 313 | 543 / 24 / 406 |
| 143 | Castlevania | `<patch>`, 32-hex | 490 | 61 | **40** (66%) | 296 | 159 / 25 | 107 / 0 / 197 | 688 / 0 / 368 |
| 209 | Ice Climber | same dump | 52 | 18 | **7** (39%) | 243 | — (they use no nearby) | 0 / 0 / 155 | 0 / 0 / 227 |
| 139 | The Legend of Zelda | `<patch>`, 32-hex | 2 074 | 111 | 2 (2%) | 587 | 68 / 2 | 60 / 0 / 29 | 289 / 0 / 102 |
| 147 | SMB (Paper reskin) | same dump | 549 | 0 | 0 (palettes disjoint, see §1) | 50 | 66 / 8 | 19 / 0 / 52 | 371 / 3 / 65 |
| 138 | Mega Man | `<patch>`, 32-hex | 1 206 | 0 | 0 | 0 (none emitted) | — | — | — |
| 148, 141 | Metroid, Zelda II | patched CHR index | 4 712 / 3 049 | 0 / 14 | 0 / 0 | 85 / 165 | n/c | n/c | n/c |

- **Subject selection agrees; the facts do not.** Where we hold a key that an
  author conditioned, we condition it too 58–72% of the time on Contra,
  Donkey Kong and Castlevania. At subject level, 66 of the 87 reachable
  Contra shapes with a nearby condition also carry one of ours (76%). The
  *facts* barely overlap. Scored against theirs, our exact nearby facts have
  a recall of 5/174 (Contra), 24/543 (Donkey Kong) and 0/688 (Castlevania),
  and a precision of 5/522, 24/406 and 0/368.
- **Only Donkey Kong agrees on adjacency.** Normalised for direction (a fact
  seen from either side counts once), our `tileNearby` recovers **44 of the 64**
  reachable ±8 px adjacencies the author wrote (69% recall, 14% precision). No
  other pack agrees on a single one.
- **The two sides use nearby for different jobs.** Authors mostly place the
  neighbour far away. The share of their nearby facts that are *not* a ±8 px
  adjacency is Contra 255/298, Donkey Kong 588/671, Castlevania 766/992 and
  SMB 529/647. Those are disambiguators ("this shared tile belongs to *that*
  object"). Ours are co-occurrence adjacencies by construction (ADR-0190:
  363 of Contra's 522 facts are ±8 px; the far ones are ADR-0189's
  `spriteNearby` spanning tree). Low fact-level agreement is expected, and it
  is not a quality verdict on either side.
- **Most community condition types have no counterpart in our output.**
  frameRange, spriteAtPosition, the position checks, h/vmirror, negation and
  multi-condition `<tile>` rules are absent from all 30 sweep `auto/` packs. So
  is memoryCheck, which the recorder cannot observe and ADR-0183 §3 forbids.

## 3. Structure — how authors organise, and what we never emit

| # | game | `<img>` sheets (recorder-named) | example names | `<background>` (parallax / behind-BG) | `<addition>` | `<fallback>` | brightness ≠ 1 | multi-condition rules | bgm / sfx | options |
|---|---|---|---|---|---|---|---|---|---|---|
| 143 | Castlevania | 22 (21) | `newskin2.png` | 264 (4 / 0) | 0 | 0 | 159 | 1 812 | 15 / 0 | disableContours, disableSpriteLimit |
| 145 | Ninja Gaiden | 296 (0) | `00_00.png`, `01_00.png` … | 0 | 0 | 0 | 7 178 | 0 | 0 / 0 | — |
| 144 | Donkey Kong | 20 (0) | `donkeykong01.png`, `barrel.png`, `latticegrid.png` | 4 (0 / 0) | 0 | 0 | 0 | 247 | 0 / 0 | — |
| 147 | SMB (Paper reskin) | 11 (0) | `mario.png`, `luigi.png`, `firemario.png` | 9 (0 / 9) | 0 | 0 | 0 | 793 | 0 / 0 | disableContours |
| 137 | Contra (Contra80s) | 163 (22) | `BillRizer.png`, `Enemies.png`, `LargeTank1.png` | 2 971 (154 / 0) | 0 | 0 | 0 | 3 184 | 0 / 0 | — |
| 148 | Metroid | 67 (0) | `BrinstarEnvironmentTiles.png`, `SamusPower.png`, `BrinstarEnemies.png` | 50 903 (151 / 0) | 1 987 | 747 | 0 | 188 758 | 14 / 31 | automaticFallbackTiles, disableContours, disableSpriteLimit |
| 139 | The Legend of Zelda | 114 (47) | `Tornado.png`, `PriorityGraphics1.png`, `CustomEnemies1.png` | 19 905 (0 / 6) | 0 | 0 | 2 336 | 33 784 | 10 / 42 | disableContours, disableOriginalTiles |
| 138 | Mega Man | 89 (0) | `Sprite_Megaman_Base.png`, `Sprite_RM_Cutman_Base.png` | 702 (84 / 0) | 0 | 0 | 93 | 1 950 | 17 / 0 | — |
| 140 | Pac-Man | 21 (0) | `Chr_00_0.png` … | 0 | 0 | 0 | 0 | 0 | 0 / 0 | — |
| 141 | Zelda II | 93 (0) | `Characters/hero_Normal.png`, `Characters/enemy_Slime.png`, `Font/OldFont.png` | 30 597 (32 / 30) | 2 267 | 20 | 0 | 55 835 | 14 / 0 | automaticFallbackTiles, disableOriginalTiles, disableSpriteLimit |
| 210 | Little Nemo | 47 (0) | `Chr_00_0.png` … | 0 | 0 | 0 | 0 | 0 | 0 / 0 | — |
| 207 | Bomberman | 5 (0) | `Chr_00_0.png` … | 2 (0 / 0) | 0 | 0 | 0 | 0 | 0 / 0 | — |
| 209 | Ice Climber | 16 (0) | `Chr_00_0.png` … | 1 (0 / 1) | 0 | 0 | 0 | 0 | 0 / 0 | — |
| 211 | TwinBee | 47 (0) | `Chr_00_0.png` … | 0 | 0 | 0 | 0 | 0 | 101 / 100 | — |
| 208 | Donkey Kong Jr. | 6 (0) | `Chr_00_0.png` … | 0 | 0 | 0 | 0 | 0 | 0 / 0 | — |
| — | 1942 (local only, not in catalog) | 0 | — | 0 | 0 | 0 | 0 | 0 | 16 / 0 | — |

"Recorder-named" means Mesen's own `Chr_<n>.png` pages. The `Chr_XX_N.png`
names are Mesen's other recorder layout and are also untouched recorder pages.
Parallax counts backgrounds whose horizontal or vertical scroll ratio is
neither 0 nor 1.

- **Two organisations, about half each.** 7 of 15 packs repaint onto sheets
  organised by **subject**: Donkey Kong, SMB, Contra, Metroid, Zelda, Mega Man
  and Zelda II, the last with `Characters/` and `Font/` sub-folders and
  `hero_`/`enemy_`/`boss_` prefixes. The other 8 repaint the recorder's own
  pages in place and never reorganise them. The subject naming is ADR-0209's
  sheet coverage problem solved by hand: an author moves each figure onto a
  sheet named after it.
- **Full-screen backgrounds are the dominant structure in the large packs.**
  105 358 `<background>` rules sit in 10 packs, **every one of them
  conditioned** (screen fingerprints plus memoryCheck state). 425 in 5 packs
  scroll at a ratio other than 0/1 (parallax), and 46 in 4 packs draw behind
  BG-priority sprites.
- **Layering.** `<addition>` appears in 2 packs (4 254 rules, Metroid and
  Zelda II), `<fallback>` in 2 (767), and `disableOriginalTiles` in 2 (Zelda,
  Zelda II; those packs replace the whole screen).
- **Fades are authored as brightness variants.** 9 766 `<tile>` rules in 4
  packs have brightness ≠ 1 (Ninja Gaiden alone has 7 178).
- **Audio travels with an IPS.** 6 catalog packs replace music: Castlevania,
  Metroid, Zelda, Mega Man, Zelda II and TwinBee (plus the local-only 1942).
  **All six catalog ones also carry `<patch>`**. The IPS presumably adds
  the hook that makes the game request the `<bgm>` tracks; this survey did
  not confirm that. Either way, audio packs and filter 2 collide.

**Features our automatic output never emits**, checked against all 30 sweep
`auto/` packs. Those packs contain only `<tile>`, `<background>` (241, all with
fields `1,0,0,20`), tileNearby, spriteNearby, tileAtPosition, and the
`automaticFallbackTiles` option:

- the condition types memoryCheck* (forbidden by ADR-0183 §3), frameRange,
  spriteAtPosition, the position checks and h/vmirror; `!` negation; and
  multi-condition `<tile>` rules (0 of 13 561 conditioned tile rules; only
  backgrounds use `&`);
- `<addition>` (the composition editor emits it, F12.5, but the automatic
  path does not), `<fallback>`, brightness variants, parallax or priority
  backgrounds, `disableOriginalTiles`;
- `<bgm>`/`<sfx>` in `hires.txt`. Our audio goes through the MEP audio
  section, not the legacy tags.

## What this implies (not a decision)

### (a) Expansions allowed under ADR-0210 as written — a candidate slice

1. **Run the index read automatically.** When a catalog pack matches the
   loaded ROM and has no `<patch>`, run `mep_import.py index` as part of the
   kit/sheet flow instead of leaving it as a manual command. Measured gain
   today: **+2 277 shapes / 4 946 keys, all on Contra**, and **0** on screen
   for the CHR ROM packs. Small, but it is the only shape gain the current
   rules allow, and nobody reaches it today.
2. **Consult their `<supportedRom>` too.** The read checks only ours. Pac-Man
   and Zelda declare dumps that are not ours, and four packs declare none.
   Refusing or warning on a declared mismatch is a cheap guard that the ADR's
   "catches a pack aimed at another ROM" intent already implies.
3. **Per-index preview palettes for fill cells** (after one clarifying
   sentence in ADR-0210 §3 on whether "palette set" includes the
   index→palette pairing). **7 651 fill cells** across 6 CHR ROM games would
   render in a palette the game really uses instead of `fill_palette()`'s
   guess. That is sheet legibility for the artist. Coverage does not move,
   because the `Y` wildcard already matches.

Nothing else in the 15 packs is reachable without an amendment.

### (b) Expansions that would need an ADR amendment

1. **Replace filter 2's wholesale refusal with a per-shape guard for 32-hex
   packs** (amend ADR-0210 §3 and touch ADR-0198 §2/§3). Keep a shape only if
   its 16 bytes occur verbatim in the stock dump. Measured: **+550 shapes**
   (Castlevania 249, Mega Man 257, Zelda 44). Lifting filter 2 with no guard
   would give 1 043, but 239 of Mega Man's are proven patch art and 254 more
   are absent from the stock ROM. The guard works only for games that store
   tiles uncompressed, and the Contra calibration shows it would misfire
   there. It does nothing for Metroid or Zelda II, whose patches re-key the
   CHR. Independently of any change, ADR-0210's Decision quotes
   Castlevania/Mega Man/Zelda gains that its own filter 2 blocks. That text
   needs correcting either way.
2. **Use community conditions as a validation yardstick, not an import**
   (amend ADR-0210 §3, which today says conditions are "never imported" and
   is silent on reading them for measurement). This survey did exactly that
   and found: subject selection agrees 58–72% where we hold the key; exact
   nearby facts agree at 0–4.4% recall; direction-normalised adjacency agrees
   69% on Donkey Kong and 0% elsewhere; and 90% of their references are types
   we never emit. A standing yardstick would score ADR-0189/0190 changes
   against 197 810 human-conditioned rules instead of the five-game
   `tileNearby` study. ADR-0197's `mep_lint.py --routes` (F12.6a/F12.6b)
   already evaluates *authored* conditions, memoryCheckConstant included,
   against recorded routes, so the evaluator exists. What is missing is the
   permission to point it at third-party conditions.
3. **Importing conditions** (memoryCheck, frameRange, far-offset nearby) would
   reverse ADR-0183 §3 and ADR-0210 §3. Up to 197 810 conditioned rules are at
   stake, but none of them is an observation. This survey measures that size
   and argues nothing further.

## What this does not establish

- **No run-time measurement.** Nothing was installed or rendered. Every gain
  above is what the read yields on a sheet or in a report, not what changes
  on screen.
- **Recording-dependent columns.** Every "new versus ours" number is against
  one short sweep recording per game. A longer recording shrinks them.
- **No investigation of the SMB palette disjointness**, and no check of what
  the 236 non-verbatim Castlevania shapes are: a different revision or patch
  art. None of Castlevania's three patch targets is our dump.
- **No bug filed.** Nothing reproducible and out of scope turned up in our
  code. The undefined condition names are defects in the community packs.

## Reproduce

Downloads use `scripts/fetch_pack.py <url> <out> --max-bytes 314572800` per
catalog row, and the sha256 is checked against the row. The allowed read is
the shipped command, run on a scratch copy of the sweep `auto/textures` folder:

```
python3 scripts/mep_import.py index "<pack>/hires.txt" \
    --pack <copy>/textures/hires.txt --rom "<library>/<game>.nes" --report index.json
```

The relaxed read is the same command on a copy of the pack's `hires.txt` with
its `<patch>` lines removed. The condition, nearby-fact and structure counts
come from a throwaway parser. It is not versioned, because it decides nothing.
It follows `HdPackLoader`'s token rules (`<tile>` index decimal at `<ver>` ≤ 102,
condition tile token decimal at `<ver>` < 104, and ≥ 32 hex characters meaning
pattern bytes).

## Suites

- `make doc-checks` — exit 0.
