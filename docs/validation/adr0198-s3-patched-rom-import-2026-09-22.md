# ADR-0198 §3 — importing a `<patch>` pack against the patched ROM (2026-09-22)

Decision under test: ADR-0198 §3, option (a) — a legacy pack that ships an
IPS is imported into a MEP project whose `<supportedRom>` is the **patched**
ROM's hash, with the IPS carried as a project asset and the fork's exact
matching (ADR-0145 (3)) left alone. Decided 2026-09-16 ("confirmo"); build
go-ahead 2026-09-22, quoted verbatim: "Construir já, em paralelo
(Recommended)".

Companion of `f12.7-legacy-pack-import-2026-09-19.md` (§1, plain packs).
Same acceptance test (ADR-0183 §4 / ADR-0198 §1): `mep_build.py build` on
the imported project must regenerate a `hires.txt` whose rule set, keyed by
`(kind, tileData, palette, condition)`, equals the input's, with identical
pixels per key; the carried lines (`<condition>`, `<background>`,
`<addition>`, `<fallback>`, `<bgm>`, `<sfx>`, and now `<patch>`) must come
back as a set.

## What changed

- `scripts/mep_import.py` — `import` takes `--rom <stock dump>`. A pack with
  `<patch>` lines is refused without it (the message names ADR-0198 §3 and
  lists the sha1s the pack's `<patch>` lines target); with it, the tool
  applies the matching IPS in memory, writes the **patched** ROM's whole-file
  sha1 as the project's `<supportedRom>` (replacing the pack's own line, or
  inserting one right after `<scale>`), copies the IPS beside both manifests
  (`auto/textures/` and `textures/`), carries the `<patch>` lines verbatim,
  and prints "what this does not buy" on every such import. `verify` reports
  the `<patch>` lines, the written `<supportedRom>` and whether the IPS sits
  beside the built manifest, and fails when it does not. `read_index`
  refuses a `<patch>` pack explicitly (its keys are the patched ROM's). No
  Core change, no runtime dual-namespace lookup, stdlib only.
- `scripts/mep_patch.py` (new) — the stdlib half: `<patch>file,sha1` parsing,
  an IPS applier that mirrors `Core/Utilities/IpsPatcher.cpp` (records in
  stream order, RLE, growth to the furthest write, truncate offset), the two
  hash forms (whole-file and No-Intro, ADR-0003/0044), `resolve()` which
  picks the `<patch>` line in `NesConsole`'s order (whole-file sha1 first,
  then No-Intro) and checks a declared `<supportedRom>` against ADR-0211
  rules 4–6, and `namespace_note()` with the three wordings of the §3 limit
  (CHR RAM → CHR ROM and index-keyed: "the two namespaces never meet";
  index-keyed without a CHR change; data-keyed: "16 pattern bytes" the stock
  ROM also produces, but keyed to another hash).
- `scripts/test_mep_import.py` — `test_apply_ips` and `test_patched_rom`
  (hash selection by whole-file and by No-Intro sha1, IPS beside both
  manifests, IMPORT.md wording, build + `verify --strict` OK, verify fails
  once `textures/fix.ips` is deleted, refusals: no `--rom`, IPS naming no
  hash of the given dump (ADR-0145 (3)), `<patch>` file missing, not an IPS,
  truncated IPS, `<supportedRom>` in no admissible form (ADR-0211), a
  `<patch>` line without a sha1; accepted `<supportedRom>` equal to the
  patched hash or to another `<patch>` target; `--rom` on a plain pack is a
  note, not an error; the CLI prints the limit). Plus one regression case in
  `test_index_keyed` for the `<ver>` fix below.
- One defect found on the way and fixed, because it gated this measurement:
  the index-keyed key source **lowered** `<ver>` to 103 instead of raising
  it (`_raise_ver`). A `<ver>108` pack came back as `<ver>103`, and the
  loader refuses `<addition>` below 107 and reads `<background>` field 5 in
  the pre-106 form — 1 987 build errors and 50 891 warnings on Metroid. It
  now writes `max(ver, 103)`. The F12.7 inputs were all at or below 103, so
  the 2026-09-19 log did not see it.

| tool | lines | sha256 |
| --- | --- | --- |
| `scripts/mep_import.py` | 1 655 | `ed659428e20ee26ef226610c541473948e7e34cf847309e3633809858174412b` |
| `scripts/mep_patch.py` | 281 | `47b2766d799ebf2486a4c7ab8467f000f260b7df6eb95c2acd5801e82ccc97f6` |
| `scripts/test_mep_import.py` | 1 360 | `946ef5cd5754fce90bfbf0f1b85a9f54ea74fd2f3b5c46ce50f35e5447b2ffbe` |

`scripts/mep_build.py`, `scripts/mep_lint.py` and the Core are untouched.

## Inputs

Accepted IPS packs (`docs/community-packs.json`), the installed copy at
`<roms>/<Game>/mep/textures/` (the `hires.txt` folder is what the importer
takes), and the stock dumps on this machine. A `<patch>` pack imports only
when one of its `<patch>` lines names the dump's whole-file or No-Intro sha1
— exactly the lookup `NesConsole` does at load — so the first measurement is
which dumps match at all:

| pack | `<patch>` lines | dump tried | whole-file sha1 | result |
| --- | --- | --- | --- | --- |
| Castlevania #143 | 3 (`akuogg.ips`) | `Castlevania (U) (PRG0) [!].nes` | `A31B8BD5…9341` | matches line 8517 |
| Castlevania #143 | 3 | library `Castlevania (USA) (Rev A)` | `7A20C44F…` (No-Intro `3DCB69A8…`) | **refused**, no line names it |
| Metroid #148 | 5 (`mmm.ips`) | library `Metroid (USA).nes` | `ECF39EC5…F90B` | matches line 257537 (also the pack's own `<supportedRom>`) |
| Mega Man #138 | 2 (`Megaman - Super.ips`) | library `Mega Man (1987) (Capcom).nes` | `2F883815…2C99` | matches line 3 |
| Zelda #139 | 1 (`ZeldaHD.ips`; the two `HdPacks/` copies target `DAB79C84…` and `3CDFA4F2…`) | every local dump | — | **refused**, no line names any of them |
| Zelda II #141 | 1 (`Revamp.ips`, `08FA60F2…`) | library copy | — | **refused** |
| TwinBee #211 | — | no local dump | — | not tried |

The refusals are the tool doing what ADR-0145 (3) says: the IPS does not
relax the match, so a dump the pack's author never targeted is not imported
against a guess. Each refusal exits 2, names the sha1s the pack does target,
and writes nothing.

Patched hashes computed by `mep_patch.apply_ips` (and printed by the import):

| pack | IPS records | size | CHR units | patched whole-file | patched No-Intro |
| --- | --- | --- | --- | --- | --- |
| Castlevania #143 | 4 | 131 088 → 131 088 | 0 → 0 | `5D012C73D1BE84588AA0989182B3ECD66B12A1E9` | `51B6DE089FCF1E4D6B8AF32BFDCBB7BC47B6F28A` |
| Metroid #148 | 1 367 | 131 088 → 393 232 | 0 → 16 (128 KiB CHR ROM) | `FEDFE5BD6E9F27B8B4E955B4405CED37C5B38468` | `190E2BB58FC9EA9D2D11D1709932CDDC5DBDF050` |
| Mega Man #138 | 375 | 131 088 → 131 088 | 0 → 0 | `8967A1A15F6F45B52A97C114B12FDE26F301F2DD` | `0245EB1E611D5196259CE8AF8FF076D923000C12` |

Metroid is the hard case ADR-0198 names: the IPS turns CHR RAM into CHR ROM
and the pack's 150 199 `<tile>` rules are bank indices of that CHR ROM.

## How "equal" is measured

`python3 scripts/mep_import.py verify <pack> <project>` — unchanged from the
F12.7 log, plus the `patch:` line. `--strict` also counts the ADR-0189 §3
bare twins `mep_build` adds on the first build as a difference; the plain
mode does not (they are build's own addition, not a lost rule).

## Results

Pipeline per pack (`scratchpad/run_pack.sh`): import → `mep_build.py build`
→ `verify --strict` → `verify` → `mep_lint.py --quiet` → second build and
sha256 of `textures/hires.txt`.

### Castlevania #143 (data-keyed, PRG0 dump) — meets the stop rule

- import 2.97 s: 7 582 rules over 6 216 cells in 22 sheets, 264 backgrounds,
  15 audio files, 485 patterns at more than one crop (2 945 rules), 5 twins;
  `<supportedRom>` written `5D012C73…A1E9` (the pack declared none), 3
  `<patch>` lines carried, 1 IPS beside both manifests.
- build rc 0.
- verify: 7 400 distinct keys, **0 missing, 0 unexpected extra, 0 differ**;
  carried 1 406 lines, 0 missing; `patch: 3 <patch> line(s) carried;
  <supportedRom> 5D012C73…; IPS beside the built manifest: yes` → **OK**.
  `--strict` fails only on the 5 twins, as on every F12.7 pack.
- lint rc 0: 0 errors, 22 warnings (182 duplicate `<tile>`s the source
  already had; the IPS reported "present, wired — applied on load" in both
  folders).
- deterministic: second build sha256 `90d9c239…e63a`, identical.

### Metroid #148 (index-keyed, CHR RAM → CHR ROM) — meets the stop rule

First pass (before the `<ver>` fix): import 177 s (1 GB pack, 150 199 rules
over 145 610 cells in 67 sheets, 50 891 backgrounds, 45 audio files, 4 229
patterns at more than one crop, 480 twins). Rule set **0 missing, 0 extra,
0 differ** over 148 715 distinct keys; carried 56 049 lines, 12 missing — all
twelve are `<background>LavaAirGlow0.png` lines whose PNG the source pack
does not ship (the importer warns, build retires them; the loader would too).
But build rc 1: the key source had come back as `<ver>103` and the lint
inside build refused the 1 987 `<addition>` lines — the defect fixed above.

Second pass (with the fix, fresh project):

- import 157.88 s: same counts as above; key source now `<ver>108`;
  `<supportedRom>` written `FEDFE5BD…8468` (the pack declared the stock
  whole-file hash `ecf39ec5…f90b`, which is also its first `<patch>` target),
  5 `<patch>` lines carried, `mmm.ips` beside both manifests. The CLI prints
  the CHR RAM → CHR ROM wording of the limit ("the two namespaces never
  meet").
- verify: 148 715 distinct keys, **0 missing, 0 unexpected extra, 0 differ**
  (274 keys name more than one crop in the source — all resolved to the first
  matching rule's art); carried 56 049 lines, 12 missing — the same twelve
  `LavaAirGlow0.png` lines, whose PNG is absent from the pack; `patch: 5
  <patch> line(s) carried; <supportedRom> FEDFE5BD…; IPS beside the built
  manifest: yes`. Plain verify exits 0 on the rule set and pixels and lists
  the 12 lines; `--strict` adds the 480 twins.
- deterministic: second build sha256 `c00d7185…76d8`, identical.
- build rc 1 and lint rc 1 (14 778 errors, 19 649 warnings) — **not** a §3
  effect, and not attainable on this pack: the source pack itself, linted as
  installed, exits 1 with 3 403 errors, all on its 1 987 `<addition>` lines
  (1 987 "target index is not past the pack's own CHR" ADR-0196 §3, 954
  `ignorePalette` §3, 460 anchors "keyed by no `<tile>` rule" §4, 1 target
  unkeyed, 1 missing `<background>` PNG). The project's count is higher for
  two reasons that are also not §3: (i) it ships sheet sidecars, so the
  1 987 "not marked synthetic in any sheet sidecar" checks fire as errors
  where the source only gets a warning; (ii) `mep_lint` compares CHR index
  tokens as **text**: build writes `<tile>` indices in `index_token`'s width
  (`00`, `0217`) while the carried `<addition>` lines keep the pack's
  three-digit `000`/`217`, so 1 987 targets and 1 007 more anchors are
  reported "keyed by no `<tile>` rule" although the loader reads both as the
  same integer. `verify` parses the index, which is why it sees 0 missing.
  Listed as an out-of-scope finding for the bug board (lint token width),
  not fixed here.

So for Metroid the F12.7 criterion holds (rule set, pixels, carried lines
except the pack's own missing PNG) and the `<patch>`/`<supportedRom>`/IPS
contract holds; the "lint exits 0" half of the stop rule does not, and
cannot until the pack's `<addition>` lines pass ADR-0196 — which is the
pack's state, not the import's.

### Mega Man #138 (data-keyed) — imports, does not round-trip, for a reason that predates §3

- import 3.40 s: 7 349 rules over 6 415 cells in 89 sheets, 702 backgrounds,
  1 197 patterns at more than one crop (2 545 rules), 9 twins;
  `<supportedRom>` written `8967A1A1…F2DD`, 2 `<patch>` lines carried, IPS
  beside both manifests. build rc 0. lint rc 0 (0 errors, 22 warnings).
  Deterministic (`ae65bff6…40d2`).
- verify **FAIL**: 1 209 keys missing (all from the `Sprite_Megaman_*.png`
  sheets), 719 carried lines missing (17 `<bgm>` whose OGGs the pack does not
  ship, 702 `<background>` lines whose `Backdrops\...png` paths use a
  backslash the build does not resolve under `textures/`); pixels 0 differ on
  the 6 068 keys that did come back; `patch:` line correct.
- Control: the same pack with its two `<patch>` lines stripped, imported
  without `--rom`, gives the **same** 1 209 / 719 / 0 — so this is the plain
  F12.7 import path, not §3. Not fixed here; listed for the bug board.

## Refusals measured

| case | exit | message names |
| --- | --- | --- |
| Metroid #148 without `--rom` | 2 | ADR-0198 §3, the 5 target sha1s, `pass --rom <dump>` |
| Castlevania #143 with the Rev A dump | 2 | both hashes of the dump, "ADR-0145 (3): IPS does not relax" |
| Zelda #139, Zelda II #141 with the local copies | 2 | same |
| `--rom` on a plain pack | 0 | stderr note: "was not needed and was not read" |

## Automated suites

- `python3 scripts/test_mep_import.py` — **105 PASS, 0 FAIL**, "all checks
  passed" (80 before this slice).
- `make doc-checks` — `make: ok` (per-file ceilings untouched; `mep_import.py`
  has none, `mep_build.py` was not edited).

## Honest limits

- Nothing was rendered in the emulator. The round-trip proves the project
  regenerates the pack's manifest; whether the fork loads the built project
  against the patched ROM is the same `HdPackLoader` path the original pack
  already uses, but it was not exercised here.
- The §3 limit stands as written: the Castlevania and Metroid projects live
  in the patched ROM's hash namespace (`<supportedRom>` = patched whole-file
  sha1). A recording this fork makes on the stock ROM is keyed to the stock
  hash — and, for Metroid, to 16-byte patterns the CHR ROM indices never
  meet — so the recording → kit → sheet loop does not connect to them. The
  tool prints this on every patched import; `IMPORT.md` repeats it.
- `mep_build.py pack . --rom <stock dump>` writes `pack.json targets` from the
  dump it is given; the MEP matcher runs before the IPS (Emulator.cpp load
  order), so the stock dump is the right one there. Not measured.
- The ADR-0211 installer guard (`InstallHdLegacy`) compares `<supportedRom>`
  against the loaded ROM's hashes; a project whose `<supportedRom>` is the
  patched hash is admissible under rules 4–6 because that hash is one the
  pack's own `<patch>` produces. Not exercised against the Core here.
- Zelda #139, Zelda II #141 and TwinBee #211 were not imported: no local dump
  matches a `<patch>` line, and the tool refuses to guess.
- Mega Man #138 fails the round-trip on the plain path (above).
- Metroid #148 does not lint clean, before or after import (above); the
  `mep_lint` index-token width finding is a separate defect.
- The `<ver>` fix is the one change to the §1 path in this slice; the 105
  unit checks cover it and the three F12.7 packs are unaffected (all at
  `<ver>` ≤ 103), but they were not re-run here.
