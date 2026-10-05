# C.5 — first outside artist, CHR ROM run: Mega Man 3

Phase 11 C.5 (`docs/roadmap/PRD-mesence-enhancement-ecosystem.md`): a session
with no prior context stands in for an external NES pixel artist and runs
`docs/remastering-a-game.md` end to end from the C.4 release binary.

**Run.** 2026-09-14, 22:36–22:47 — 11 minutes of wall clock.

**Game.** `roms/chr-rom/Mega Man 3 (USA).nes` (NES, mapper 4, CHR ROM). The
evaluator arrived at it the hard way: it started on the file the sandbox offered
as the CHR RAM game and found it to be a 40 KB stub (mapper 0, 32 KB PRG of
`JMP $8000` plus NOP filler, 8 KB of zero CHR) carrying no art at all. Four of
the run's six recording minutes went into establishing that, and the pivot to
MM3 is where the CHR ROM half of the criterion is satisfied.

**Binary.** `mesence-v0.1.0`, macOS Apple Silicon, unpacked — never built.

**Sandbox.** The release, the three guides, the ROM library and Pillow. Not the
repository checkout, `docs/adr/`, the PRD, any `scripts/*.py` source (the tools
ship with the release and are run, never read), the memory directory, or any
session transcript. No reads outside the sandbox occurred; the one tool the
guide does not mention (`log`) the evaluator found in `headless_record`'s own
usage text, which is inside the sandbox.

**Prompt.** The verbatim PRD prompt, as reproduced in the CHR RAM log.

**Verdict: pass.** Lint-clean pack, repainted figure on screen, 11 minutes
including the dead end, and the evaluator's own "yes" over the spreadsheet.

## The evaluator's log, verbatim

```
# First-hour log — Contra repaint with MesenCE v0.1.0 (NES pixel artist, first time)

Format: `HH:MM | stage | what I ran / saw`. STOP = had to look for an answer the guide did not give.

22:36 | 0 | Read docs/remastering-a-game.md top to bottom (27K). Table of 6 stages is clear; step 4 is "yours".
22:36 | 1 | STOP #1: guide says `make core && make capture-tool`, I have a release zip, no Makefile. Looked in release/MesenCE-*/README.md -> headless_record is prebuilt, keep MesenCore.dylib beside it; tools README says "run every tool from the tools folder". Resolved in 1 min.
22:37 | 1 | `./headless_record` with no args prints usage; no Gatekeeper block. Pillow 12.0 / numpy 2.4 already present (requirements pins 12.3, did not reinstall).
22:38 | 1 | Read scripts/stages/contra/mint-stage1.txt (901 frames) and stage1-run.txt (60s of R/RB/RA/UB). Copied ROM to a work dir as Contra.nes since the recording lands beside the ROM.
22:37 | 1 | Minted stage1-run.mss: `headless_record Contra.nes 20 out/mint hdpack-off input=mint-stage1.txt save-state=stages/stage1-run.mss` -> 1203 frames in 1.5 s wall. Fast.
22:37 | 1 | Probe per guide: `... 60 out/probe screenshot hdpack-off input=stage1-run.txt state=stage1-run.mss` -> final frame is a SOLID GREEN screen. Guide only tells me what GAME OVER / PASS WORD mean; a blank frame is not in the table.
22:37 | 1 | Bootstrap run with MESEN_SHEET_GRID_DUMP: 4810 frames, 5 s wall. Pack landed at roms/Contra/auto/ but holds only textures/chr/ (6 PNGs, all one flat colour), no sheets/, no backgrounds/, no grid.txt. VGM of the 60 s run is 309 B, same as the 20 s mint.
22:40 | 1 | STOP #2 (big): why is the recording empty? Guide's "If something looks wrong" table has no row for "no sheets at all". Looked at: hires.txt (512 <tile> rows keyed by CHR INDEX, so the emulator thinks CHR ROM, in a folder named chr-ram); .bootstrap stamp; then the ROM itself with xxd/python. Header = mapper 0, 32K PRG + 8K CHR ROM. PRG = `JMP $8000` + NOP filler, CHR = 8192 zero bytes. The file named "Contra (USA).nes" is a stub, not Contra. Nothing the tools did was wrong; the input has no art in it. Cost so far: ~4 min.
22:41 | 1 | Decision: I cannot repaint a Contra figure from this file. Sibling roms/chr-rom/Mega Man 3 (USA).nes is 393,232 B (right size for the real cart, mapper 4) and the tools ship scripts/stages/mm3/. Pivoting to MM3 to still walk stages 1-5 in the hour; will say so plainly in the summary.
22:39 | 1 | MM3: mint (1503 f, 2.7 s wall) -> probe screenshot shows Mega Man at Snake Man's stage start: route is alive. `screenshot` flag is genuinely useful.
22:40 | 1 | MM3 bootstrap run: `MESEN_SHEET_GRID_DUMP=out/grid.txt headless_record MM3.nes 60 out/rec bootstrap hdpack-off input=stage1-run.txt state=stage1-run.mss` -> 5110 frames, 14.8 s wall. Pack at roms/MM3/auto/: 22 spr sheets, 13 obj sheets, 14 backgrounds, 106 chr PNGs, 9985 <tile> rows, grid.txt 39 MB. Final-frame screenshot at 60 s: Mega Man mid-air next to a big snake — still alive, still in the stage.
22:41 | 2 | Stage 2 (measure) SKIPPED: needs a reference hires.txt of a finished MM3 pack; I have none. Guide is explicit that this is what it compares against, so no stop.
22:40 | 3 | Kit: artist_kit.py / artist_bg_kit.py / artist_chr_kit.py --rom / artist_map.py / artist_kit_assemble.py, all from the tools folder. 13 s total. All three --verify PASS (0 lost / 0 added). artist_map refused: "keys its tiles by CHR index" — exactly the row in the guide's table, so no stop. CHR kit says 98% of 8192 tiles recorded from a 60 s run.
22:41 | 3 | Opened kit/ARTIST.md. Rules are short and clear (never paint .orig.png, keep sizes, copy .json with .png, don't paint sprites.png). Figure table lists usr000..usr036 by "cycle/seq, N phases, seen N times" — no names, so I hunted by eye.
22:42 | 3 | Looked at usr000.png: a composed 4-phase figure in skin-tone palette I did not recognise. Small STOP #3: how do I find Mega Man among 37 unnamed sheets? Guide's answer is step 6 (AI reviewer), which I skipped; instead I listed palettes per .json (Mega Man = blue FF0F2C11 + face FF0F3037) and opened the mixed-palette sheets. usr002.png = Mega Man's 8-phase run, right half of a 2488x184 sheet. ~2 min.
22:42 | 4 | Painted: hue-shifted every blue/cyan pixel of kit/sheets/usr002.png 180 deg -> red/orange Mega Man (23,424 px). Did not touch .orig.png. 30 s.
22:42 | 5 | `cp -R auto painted; cp kit sheets+json, chr+json; mep_build.py build painted` -> exit 0, "202 tile(s), 22 sheet(s)"; "8779 keys -> 202 carried, 8577 dropped" with an info saying that is expected. `mep_lint.py` exit 0. 1 s. Guide's acceptance test fully green.
22:43 | 6 | STOP #4: how do I LOAD the painted pack to see it? remastering-a-game.md stage 5 ends at lint; "you can load it in the emulator as-is" (l.229) never says where. grep of all three guides: nothing. ARTIST.md mentions `mep/` "wins over auto/, ADR-0147, the installer's job". Tried `cp -R painted roms/MM3/mep` + `headless_record ... 8 screenshot` (no hdpack-off). 4x xBRZ frame came out, pack on. Then ran with `log` flag (found in usage, not in guide): "[MEP] textures: loaded NES HD pack from .../MM3/mep/textures", "auto layer merged: 8577 tiles added, 1408 overridden". So mep/ sibling works; ~2 min.
22:43 | 6 | Frames at 8 s, 2 s, 1 s: Mega Man is STILL BLUE. At 1 s he is mid-run and his face is a transparent hole, so the human layer is touching his cells but not with my paint.
22:44 | 5 | STOP #5 (blocking): parsed painted/textures/hires.txt with python. 22 <img>, 202 <tile> rows total, only 3 rows point at usr002.png (129 cells, 72 of them Mega Man blue). 0 duplicate keys. The 8577 "dropped" keys are served by auto/ underneath with the ORIGINAL art. So build exit 0 + lint 0 + verify PASS all hold and the figure is not in the pack. The guide's "expected, not breakage" info line hides exactly the case I care about.
22:46 | 5 | Cause found (python over hires.txt + usr002.json, not source code): build emits each (tile,palette) key ONCE, from the first sheet holding it. usr002's 8 run phases reuse the same tile indices, so the whole sheet collapses to 3 keys (1E, 50 blue + one face tile); the rest of Mega Man's body is keyed from usr005/007/021/022/023 where I did not paint. "Figure" surfaces overlap in keys and the guide never says which sheet OWNS a key. ~3 min.
22:47 | 4 | Workaround: repaint by PALETTE not by sheet — hue-shift every cell whose tile wears FF0F2C11 (Mega Man's body) in every usr*.png, using x/y from the .json (logical px * scale 4, 32 px cells).
22:46 | 4-5 | Palette repaint touched 11 sheets / 52,352 px. Rebuild exit 0, lint exit 0, reinstalled to roms/MM3/mep, screenshots at 1 s and 8 s.
22:46 | 6 | SEEN: Mega Man runs and jumps in RED/ORANGE (logs/screens/after-red-1s.png, after-red-8s.png; before: before-blue-1s.png). Residue: helmet top + one forearm still blue (keys owned only by the auto/ chr layer that `build` dropped), and the flying enemy's boots went orange because they share the palette. Visible, unmistakable, imperfect.
22:47 | -- | Done. Wall clock 22:36 -> 22:47 = 11 min including the Contra dead end. Did NOT read any tool source code.

## Summary (five lines)

1. Elapsed per stage of the guide's table — 1 Record: 6 min (4 of them the Contra stub dead end; MM3 mint+probe+bootstrap = 25 s of machine time) · 2 Measure: skipped, no reference pack · 3 Unpack: 1 min (13 s machine) · 4 Paint: 2 min (twice, because of the key-ownership surprise) · 5 Build+verify: 1 s machine, 4 min human to understand why "0 errors" did not mean "my figure is in the pack" · 6 Ship: not attempted · seeing it on screen (not a stage in the table): 3 min.
2. Stops: 5 — (1) `make` vs prebuilt release, (2) "Contra (USA).nes" is a 40 KB stub with an all-zero CHR and no art, (3) finding Mega Man among 37 unnamed sheets, (4) how to load the built pack (answer: `<rom dir>/<rom stem>/mep/`, found via ARTIST.md hint + the recorder's `log` flag, not the guide), (5) build dedupes (tile,palette) keys across sheets so painting one figure sheet repaints ~2 of his tiles.
3. Repainted figure on screen: YES — Mega Man red/orange, running and jumping, in Snake Man's stage; not Contra, because the Contra file provided is not Contra. Residue of a few blue tiles that no sheet owns.
4. Over my spreadsheet: yes, for the recording and kit — 60 s of gameplay to 22 figure sheets, 98% of CHR and a lint-clean hires.txt in under a minute of machine time is a week of my old workflow; but not yet for the painting contract — the acceptance test (build 0 / lint 0 / verify PASS) passed with my figure absent, and the guide needs one paragraph saying which sheet owns which key and where to drop the pack to see it.
5. In one sentence: I would switch because the tool removes the part of the job I hate (recording and untangling) and only mislabels the part I am good at (knowing which pixels I actually changed) — fix the ownership report and the "load it here" line and this is the tool.
```

## Stage times

| Stage of the guide's table | Time | Notes |
| --- | --- | --- |
| Read the guide | included in stage 1 | read end to end before 22:36 |
| 1 Record | 6 min | 4 of them the stub-ROM dead end; MM3 itself was 25 s of machine time |
| 2 Measure | skipped | needs a reference MM3 `hires.txt`; none in the sandbox |
| 3 Unpack (kit) | 1 min | 13 s of machine time; all three `--verify` PASS |
| 4 Paint | 2 min | done twice — the second pass repainted by palette |
| 5 Build + verify | 4 min | 1 s of machine time; the other 4 min to understand why "0 errors" did not mean "my figure is in the pack" |
| 6 Ship | not attempted | out of scope for the hour |
| "See it on screen" | 3 min | **not a row in the table** |
| **Total** | **11 min** | including the dead end, inside the one-hour budget |

## Stops

Five. Two of them are the guide's, two are the tool's, one is the input's.

1. **`make` against a binary release** — answered by the release's own README.
   Fixed in the guide (PR #254).
2. **A ROM that is not the game it is named** — `Contra (USA).nes` in the
   sandbox is a 40 KB stub (mapper 0, `JMP $8000` + NOP filler, 8 192 zero CHR
   bytes), which the guide's "if something looks wrong" table has no row for.
   Not the tools' fault and not a guide gap in the ordinary sense; recorded
   because it cost 4 of the run's 11 minutes and because a first-timer cannot
   tell a stub from a route failure. The sandbox is dispatcher-provided, so the
   dispatcher owes the fix: hand the artist real dumps.
3. **Finding a known figure among 37 unnamed sheets** — `ARTIST.md` captions are
   `cycle000 — a 2-phase loop, seen N time(s)`; the evaluator identified Mega
   Man by listing palettes out of the sidecars and opening the mixed-palette
   sheets, ~2 min. The guide's answer (step 6, the AI reviewer) was skipped as
   out of the hour's scope.
4. **How to load a built pack** — the same stop as the CHR RAM run, hit
   independently: reconstructed `mep/` from an `ARTIST.md` footnote and confirmed
   it with the undocumented `log` flag. Fixed in the guide (PR #254).
5. **`build` dedupes `(tile, palette)` keys across sheets, so painting one
   figure sheet repainted two of the figure's tiles** — the accepted cause of
   the run's only rework, and the reason its final state is "visible,
   unmistakable, imperfect" rather than clean. Filed as #253; `--verify` and
   `mep_lint.py` both pass in this state.

## Phase 9 panel, sections 2 and 3

Filled by the dispatcher from the log above, following the PRD's panel.

**Section 2 — side-by-side with the artist pack.** *Not reached as written* (no
reference MM3 pack in the sandbox). On the part that does not need one — is a
subject the artist treats as one figure reachable as one unit? — the answer is
**partial, and the exceptions are the interesting part.** Every figure *is* a
unit: `usr002.png` alone is Mega Man's whole 8-phase run, which is exactly what
ADR-0171 makes the unit of the sprite layer. What the surface does not provide
is any way to *know* which unit is which: 37 sheets with palette-and-phase
captions only, and the evaluator spent two minutes matching palette hexes by
hand. Named exceptions, per the criterion:

- A known character cannot be found by name — only by opening sheets until one
  is recognised.
- The keys a figure's cells actually own are invisible: the evaluator painted
  the sheet that *shows* Mega Man and repainted two of his tiles, because five
  other sheets own the rest of his body. That is #253, and it belongs to section
  3's contract as much as to section 2.

**Section 3 — find-and-edit.** *Split verdict, and the split is the finding.*
The time criterion passes: paint at 22:42, first screen evidence at 22:43, done
by 22:46 — about five minutes from opening the folder, with Pillow and
`mep_build.py` as the only tools. The criterion "the person never had to open
`hires.txt`" **fails**, and unlike the CHR RAM run it failed for the delivery
path rather than for diagnosis: the evaluator read the built `hires.txt` to
discover that 8 577 of 8 779 keys were dropped, i.e. that the mechanical
acceptance test it had already passed does not mean the painted figure is in the
pack. The figure only appeared after the evaluator stopped repainting the sheet
that shows the figure and repainted by palette across eleven sheets. Filed as
#253.

## Defects filed

| Defect | Where |
| --- | --- |
| `build` emits each `(tile, palette)` key once, from the first sheet holding it, so a repaint of the sheet that shows a figure can leave the figure's own keys owned by other sheets and served by `auto/` — with `build` 0 errors, `mep_lint.py` 0 and every `--verify` PASS | #253 (P1) |
| How to load a built pack, and what `bootstrap`/`hdpack`/`hdpack-off` mean | guide fixes, PR #254 — stops 1 and 4 |
| The sandbox's `Contra (USA).nes` is a 40 KB stub with no art | dispatcher fix, not an issue: real dumps only |

No ROM-derived image is committed with this log; the before/after screenshots
stay in the run's scratch directory.
