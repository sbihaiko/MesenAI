# #381 — Windows-style paths through import → build → verify (2026-09-23)

Issue [#381](https://github.com/sbihaiko/MesenAI/issues/381): importing the
Mega Man (USA) community pack (#138) with `scripts/mep_import.py import`,
rebuilding with `scripts/mep_build.py build` and checking with
`scripts/mep_import.py verify` reported **1 209 rule-set keys missing** and
**719 carried lines missing** (702 `<background>` + 17 `<bgm>`), the 702
backgrounds dropped with an info-level "retired" log. The issue named the
`<background>` half's cause and left the other two open. This log records
what each of the three numbers was, what was changed, and what the three
packs measure after the change.

Result, in one line: **Mega Man goes from 1 209 → 0 keys missing and 719 → 17
carried lines missing; the 17 are the pack's own dangling `<bgm>` references
(it ships no OGG), not a tool defect; Castlevania and Ninja Gaiden are
unchanged and green.**

## 1. Root causes — three numbers, two causes, one non-bug

| Reported | Cause | Fixed? |
|---|---|---|
| 702 `<background>` lines missing | `mep_build`'s retirement pass read the carried line's raw name. `Backdrops\Backdrop_Bombman_00_00.png` is one literal file name on POSIX; the importer had copied the PNG under `Backdrops/…`, so the name never resolved and the line was retired (207 distinct screens, 702 lines). | Yes — `scripts/mep_carry.py` |
| 1 209 rule-set keys missing | **A different cause.** The pack names the same base name in two folders — `Sprite_Megaman_Base.png` and `Submerged\Sprite_Megaman_Base.png`, ten such pairs. The importer wrote every sheet flat under `textures/sheets/` using the base name as the stem, so the later `<img>` overwrote the earlier sheet, its `.orig.png` twin and its sidecar: 79 sheets came out of 89 `<img>` lines, and every key only the first sheet carried was gone (measured on the source: 1 138 distinct `(tileData, palette)` pairs, 1 217 `(pair, condition)` rules, drawn from the ten overwritten sheets; verify counts a key per condition, and the 1 209 it reports are those rules minus the few a surviving sheet also draws). Not a backslash problem as such — the same collision happens with `Submerged/…` — but it is only reachable through a sub-folder `<img>`, and that is what the backslash packs use. | Yes — `mep_import._sheet_stems` |
| 17 `<bgm>` lines missing | **Not a defect in the tools.** The pack's manifest references `BGM\MUS_*.ogg` but ships no `BGM/` folder at all (its README says the music is a separate download). `mep_import` already warns once per line at import time (`<bgm> 'BGM/…ogg' has no file in …; build would drop the reference`), and `mep_build` drops a seed reference whose OGG is absent so its track id is reclaimed — an existing, documented rule. `verify` then reports the 17 as carried-line missing, which is literally true: the round-trip cannot carry a reference to a file that is not there. The audio seed filter **did** have the same backslash read as the background pass (fixed in the same module, covered by a unit test), so had the OGGs shipped they would have been dropped for the wrong reason too. | Backslash read fixed; the 17 are left as they are |

Where the loader stands: `Core/NES/HdPacks/HdPackLoader.cpp` rewrites every
`\` to `/` on each manifest line **before** it parses a tag (the
`std::replace` just above the condition parse), so `Backdrops\x.png` and
`Backdrops/x.png` are the same rule to the emulator on every OS. `mep_lint`
normalizes the same way. The build was the one reader that did not.

## 2. What changed

- **`scripts/mep_carry.py`** (new, 111 lines) — the carried-line rules,
  extracted from `mep_build` because that file sits on its LOC ceiling
  (2 070; it is at 2 037 after this change). `posix_ref` is the loader's
  normalization; `carry_backgrounds` copies a background up from
  `auto/textures` / retires it (#344) and **emits the surviving line under the
  `/` spelling** — that is where the file actually lives, it is what every
  tool in this tree can `exists()`-check, and the loader reads both spellings
  identically, so the rewrite changes nothing for the run time and stops the
  carried text and the disk from disagreeing. `keep_seed_refs` does the same
  for the `<bgm>`/`<sfx>` seed. A retirement is now a **warning**, with the
  line count and the first three names: it stays a legitimate exit (#344 —
  the artist deleted the PNG on purpose, and the loader itself drops a
  dangling entry, so the build must not fail), but it drops manifest lines,
  and a drop the artist did not intend has to be visible in a log they skim.
- **`scripts/mep_build.py`** — calls the module in the two places it used to
  resolve a carried name itself; `_BGM_RE`/`_SFX_RE` now alias the module's.
  `<img>` names are not resolved by build (it regenerates them from the
  sheets), so there was no third site.
- **`scripts/mep_import.py`** — `_sheet_stems`: one stem per `<img>`, unique
  across the manifest (case-insensitively, since the sheet lands on a file
  system that may be). A base name that is unique keeps the bare stem every
  earlier import wrote, so no existing project renames; one shared by several
  `<img>` lines takes its folder path as a prefix (`Submerged_Sprite_Megaman_Base`);
  a prefixed stem that still collides is numbered. `verify_pack` compares
  carried lines under `posix_ref` on both sides — equality there has to be
  the loader's.
- **`scripts/tools-zip-manifest.txt`** — lists the new module, or the release
  zip would ship a `mep_build` that cannot import it (`make doc-checks` caught
  this).
- Tests: `scripts/test_mep_build.py` +3 checks (104 → 107),
  `scripts/test_mep_import.py` +4 checks (80 → 84). Both suites green.

## 3. Inputs

The three packs already installed in the data home
(`~/Library/Application Support/MesenCE/HdPacks/`), copied to a scratch
folder outside any repository (ADR-0198 Consequences) with their `<patch>`
lines stripped — the F12.17 patched-ROM import is not on `main`, and
`mep_import` refuses a `<patch>` (ADR-0198 §2). Nothing was downloaded.

| Pack | `hires.txt` sha256 (after the strip) | `<ver>` | scale | `<img>` | `<background>` | `<bgm>` |
|---|---|---|---|---|---|---|
| Mega Man (1987) (Capcom) — #138 | `59430659fab70982f05423332fed10d22af9b267fb5fd779a151757534269d4c` | 106 | 1 | 89 (20 with `\`, 10 base-name pairs) | 708 (all `\`) | 17 (all `\`, no OGG shipped) |
| Castlevania (1987) (Konami) — #143 | `540a82e67b74869f1834fcca46f4cddc60b7b5099cb6d854dcea2beed2dffd0a` | 101 | 2 | 22 (0 in sub-folders) | 265 | 15 (OGGs shipped, no `\`) |
| Ninja Gaiden (1989) (Tecmo) | `71fe70b2e079ee62e4a792381f58ec59ece3269f3f679feab53db4df5fbc9cdd` (unchanged since the [F12.7 log](f12.7-legacy-pack-import-2026-09-19.md)) | 100 | 1 | 296 (0 in sub-folders) | 0 | 0 |

## 4. Numbers — before / after

"Before" is the same worktree at `origin/main` `5a5bc84f` before any edit;
"after" is with the change. Same scratch procedure, same inputs.

### Mega Man (USA), #138

| Measure | before | after |
|---|---|---|
| sheets written by import | 79 of 89 `<img>` | **89 of 89** (10 `Submerged_*` stems) |
| build: tile keys carried / dropped | 5 801 / 128 | **5 929 / 0** |
| build: `<background>` retired | 207 screens, 702 lines (info) | **0** |
| built manifest `<background>` lines | 6 (the ones whose name had no `\`) | **708** (`/` throughout; no `\` left in the manifest) |
| background PNGs copied up into `textures/` | 0 | **207** (one per screen retired before) |
| verify: rule set missing / extra / twins | 1 209 / 0 / 9 | **0 / 0 / 9** |
| verify: pixels differ | 0 (of 6 068) | **0 (of 7 277)** |
| verify: carried missing / extra | 719 / 0 | **17 / 0** (all `<bgm>`, §1) |
| verify exit | 1 | 1 (the 17 `<bgm>`) |

The 17 remaining lines are listed by verify as
`<bgm>0,1,BGM/MUS_WilyDefeated.ogg,772389` etc.; the import log has the
matching 17 `has no file … build would drop the reference` warnings.

### Castlevania (1987) (Konami), #143 — regression check

| Measure | before | after |
|---|---|---|
| rule set missing / extra / twins | 0 / 0 / 5 | 0 / 0 / 5 |
| pixels differ | 0 (of 7 400) | 0 (of 7 400) |
| carried missing / extra (1 403 lines) | 0 / 0 | 0 / 0 |
| verify exit | 0 (OK) | 0 (OK) |

Castlevania's 15 `<bgm>` lines name root-level OGGs the pack ships; the
import copies all 15 into `audio/` and the rebuilt `audio/hires.txt` carries
all 15, before and after. Its 265 `<background>` lines use `/`-free root
names, so the #381 path is not exercised here — this pack is the control
that the normalization and the `/` rewrite change nothing for a pack that
never needed them.

### Ninja Gaiden (1989) (Tecmo) — regression check

| Measure | before | after |
|---|---|---|
| rule set missing / extra / twins | 0 / 0 / 0 | 0 / 0 / 0 |
| pixels differ | 0 (of 19 147) | 0 (of 19 147) |
| carried lines | 0 | 0 |
| verify exit | 0 (OK) | 0 (OK) |

## 5. What this does not claim

- The Mega Man verify still exits 1, on the 17 `<bgm>` lines. Making verify
  tolerate a reference whose file the source itself does not ship would be a
  change to ADR-0198 §1's acceptance rule, not a bug fix, and is not made
  here.
- The pack was measured with its two `<patch>` lines stripped. Its `<tile>`
  keys are 16 data bytes, not CHR indices, so the strip changes no key; the
  patched-ROM import path itself (ADR-0198 §3) is not exercised.
- No pack was re-recorded and no ROM was run; every number above is a
  manifest/PNG comparison by the tools named.
