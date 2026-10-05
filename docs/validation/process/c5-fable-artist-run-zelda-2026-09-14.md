# C.5 — first outside artist, CHR RAM run: The Legend of Zelda

Phase 11 C.5 (`docs/roadmap/PRD-mesence-enhancement-ecosystem.md`): a session
with no prior context stands in for an external NES pixel artist and runs
`docs/remastering-a-game.md` end to end from the C.4 release binary.

**Run.** 2026-09-14, 23:03–23:15 — 12 minutes of wall clock.

**Game.** `roms/Zelda.nes` (NES, CHR RAM). The evaluator picked it from the
local library; the PRD names Contra, Zelda and Metroid as the CHR RAM choices.
A third run the same evening picked Contra and turned out to be a CHR ROM cart
(`Contra (Japan)`, VRC2), which is recorded as a defect of the guide's framing
rather than as the CHR RAM run.

**Binary.** `mesence-v0.1.0`, macOS Apple Silicon, unpacked — never built.

**Sandbox.** The release, the three guides, the ROM library and Pillow. Not the
repository checkout, `docs/adr/`, the PRD, any `scripts/*.py` source (the tools
ship with the release and are run, never read), the memory directory, or any
session transcript. Reading anything outside the sandbox is logged as a stop;
none was needed here.

**Prompt** (verbatim from the PRD, with the four placeholders substituted):

> You are a NES pixel artist. You have shipped two HD packs by hand-editing
> `hires.txt` with a spreadsheet beside you, and you are trying MesenCE for
> the first time. In the folder `<release>` is the emulator and its tools;
> `<docs>` holds three guides — start with `remastering-a-game.md`. Your
> ROMs are in `<roms>`. Goal: within one hour, record `<game>`, find one
> figure you recognize, repaint it in a visible way, build the pack, and
> see it on screen. Keep a log as you go, one line per step with the clock
> time: what you ran, what you saw, every time you had to stop and look for
> an answer the guide did not give (say where you looked). Do not read the
> tools' source code; if you feel you must, log it as a stop and then do it.
> At the end write, in five lines: elapsed time per stage of the guide's
> table, the number of stops, whether the repainted figure appeared, and
> whether you would use this over your spreadsheet — and why, in one
> sentence.

**Verdict: pass.** Lint-clean pack, repainted figure on screen, 12 minutes,
and the evaluator's own "yes" over the spreadsheet.

## The evaluator's log, verbatim

```
# Zelda repaint — first-run log (artist POV)
23:03 read docs/remastering-a-game.md top to bottom (~5 min). Table of 6 stages; guide says `make core && make capture-tool` and `scripts/headless_record`.
23:03 STOP 1 — release has no Makefile and no scripts/headless_record. Looked in release/*/README.md: emulator zip README says the binary ships beside Mesen.app and the guide's path is for a repo checkout. Resolved.
23:03 read scripts/stages/zelda/*.txt: mint-stage1 (~22s of title/name entry), stage1-run (walk D/R/U/L + sword). Both shipped.
23:04 mint: headless_record Zelda.nes 25 out/mint screenshot hdpack-off input=mint-stage1.txt save-state=out/stages/stage1-run.mss — 5.8s wall; screenshot shows Link on the first overworld screen. State OK.
23:04 record: headless_record Zelda.nes 60 out/rec bootstrap hdpack-off screenshot input=stage1-run.txt state=out/stages/stage1-run.mss — 14s wall. Pack landed at roms/chr-ram-zelda/Zelda/auto/ (hires.txt 1819 lines, <scale>4, spr000..N sheets, 4 backgrounds). Note: the run's own output never says "pack written to …" — I had to ls beside the ROM to know it worked (guide warned about this, issue #229).
23:05 stage 2 (measure) SKIPPED — artist_cover.py wants a reference hires.txt to intersect with; I have no Zelda reference pack on this machine, so there is nothing to measure against.
23:05 kit: artist_kit.py / artist_bg_kit.py / artist_chr_kit.py --verify, then artist_kit_assemble.py — 3s total, all three verify PASS. Kit = 6 figure sheets (usr000-005), 2 scenery sheets, 12 CHR pages, ARTIST.md.
23:06 opened ARTIST.md + a contact sheet of usr*.png (made my own with Pillow — the kit ships no overview image, so I could not see all figures side by side without one). Recognised at once: usr000-003 = Link walking down/right/up/left (2 phases each), usr004 = Octorok, usr005 = death puff, usr006/007 = HUD.
23:06 ARTIST.md captions are "cycle000 — a 2-phase loop, seen 80 time(s)": no names. Fine for 8 sheets, would hurt at 80. Picked usr000 (Link facing down) to repaint: green tunic -> red.
23:06 paint: recoloured 1296 green px -> red in kit/sheets/usr000.png (Pillow; a real session would be Aseprite). Kept .orig.png untouched.
23:06 build: cp -R auto -> work/out/painted; cp kit sheets+json and chr+json in; mep_build.py build -> 0 errors (7 warnings flagged as the tool's own); mep_lint.py -> exit 0. hires.txt shrank 1819 -> 141 lines ("1598 dropped … expected, not breakage") — scary line for a first-timer even with the explanation.
23:07 STOP 2 — guide stage 5 ends at "lint exit 0"; nothing says how to make the emulator LOAD out/painted instead of the auto/ it discovered beside the ROM. Grepped all three guides for mep/, install, hdpack: nothing. Only lead: ARTIST.md footnote "installing … as the human layer (mep/, which wins over auto/ entry by entry, ADR-0147) is the installer's job". Trying Zelda/mep/ by hand.
23:07 tried `headless_record … 2 out/view hdpack screenshot state= input=down.txt` with painted copied to Zelda/mep/. Result: Link still green, screenshot 256x240. `hdpack` is the RECORD flag (it wrote out/view-hdpack/), not "load packs" — usage line does not say which way each flag goes.
23:08 STOP 3 — how do I make headless_record apply the pack? Guide only ever shows `hdpack-off`. Guessing: no flag at all = packs on by default.
23:08 no-flag run: 1024x960 frame, packs ARE applied (xBRZ auto layer) but Link still green. Zelda/mep/ is either not discovered or not matching.
23:09 `log` flag shows "[MEP] textures: loaded NES HD pack from …/Zelda/mep/textures … 136 overridden by the human layer" — mep/ IS discovered. So the layer works and the guide just never says so.
23:10 measured Link's box in the frame: red only on the top-left tile (his hat). The other 3 tiles of the pose are green. mep/hires.txt line 78 is unconditional, lines 79-81 carry [spr002_n0..n2] spriteNearby conditions the build generated; those do not fire, so the tiles fall back to the auto layer's green art. Same result with painted swapped in as auto/ (romtest/), so it is the conditions, not the layer. --verify PASSED on this — verify checks keys, not what renders.
23:11 root cause (from diffing the two hires.txt, not from source): the recorder's auto/hires.txt writes each conditional rule twice — `[spr002_n1]<tile>…` then the identical `<tile>…` unconditional fallback. mep_build.py build writes only the conditional half for 3 of usr000's 4 cells, so a condition miss falls through to the auto layer's Chr_0.png (green). build --help has no option for it.
23:11 STOP 4 — no supported way forward without editing hires.txt, which the guide forbids ("thrown away on the next build"). Doing it anyway on the installed mep/ copy only, to prove the figure, and logging it as a bug to report.
23:12 ablation (one fallback line at a time, 6 runs x 0.5s): the phase-0 lines change nothing in this frame; only the two lines for sheet cells 5 and 7 paint the right column, and they paint it wrong. usr000.json says those cells are `"source": <left tile>, "mirror": "H"` — the game draws that column as the left tiles H-flipped; the kit shows me the mirrored picture, build slices it back onto the source key un-mirrored, the PPU flips it again on screen. Round-trip --verify cannot see this: it compares keys, not pixels.
23:13 sweep of walk length 84..98 frames: Link in phase 0 (84, 86, 94-98f) = 2832/2832 pixels identical to the kit, 0 green; phase 2 (88-92f) = left column only, right column garbled.
23:14 re-ran phase 0 on the UNTOUCHED build output (no hand edit): Link fully red, 0 green. The supported path works for phase 0; my first frame just happened to land on phase 2. The hand edit in STOP 4 was a wrong turn — kept only as evidence, not needed.
23:15 done. Files: work/out/kit (kit), work/out/painted (built pack, lint-clean), roms/chr-ram-zelda/Zelda/mep (installed copy, hand-edited), work/final-link-red.png (left: phase 0 with fallbacks; middle: phase 2 garbled; right: phase 0 on untouched build), work/ablation.png.

## Summary (5 lines)
1. Elapsed per stage: read guide 3 min · 1 Record 1.5 min (mint 6s + record 14s wall) · 2 Measure skipped (no Zelda reference pack) · 3 Unpack 1 min · 4 Paint 1 min · 5 Build+verify <1 min · "see it on screen" 8 min (not a row in the table; 7 of those 8 were diagnosis) — 12 min total, 23:03 to 23:15.
2. Stops: 4 — (1) guide says `scripts/headless_record`/`make`, release ships it beside Mesen.app (answered by the zip README); (2) guide never says how the emulator loads the painted pack — found `mep/` only in an ARTIST.md footnote, confirmed with the `log` flag; (3) `hdpack` flag records, it does not load — usage text does not say; (4) partial repaint led me to hand-edit hires.txt, which turned out unnecessary for phase 0.
3. Repainted figure appeared: YES — red-tunic Link, pixel-identical to the kit, in walk phase 0 on the untouched build; in phase 2 the right column (two "mirror: H" cells) renders garbled, a tool bug the verify pass cannot catch.
4. Two things to report: (a) build emits mirrored cells without un-mirroring them (usr000 cells 5/7); (b) build drops the unconditional fallback the recorder emits beside each conditional rule, so a condition miss falls through to the auto layer instead of the same art.
5. Would I use this over my spreadsheet: yes — the recording plus kit gave me a whole, named-by-pose, paint-ready Link in under 5 minutes instead of a day of cross-referencing tile hashes, and the round trip to a lint-clean pack is one command; the missing piece is one paragraph in the guide saying "copy painted/ to <rom>/<stem>/mep/ and run with no flags", and a pixel-level verify.
```

## Stage times

| Stage of the guide's table | Time | Notes |
| --- | --- | --- |
| Read the guide | 3 min | 585 lines, read end to end |
| 1 Record | 1.5 min | mint 5.8 s + record 14 s of machine time |
| 2 Measure | skipped | `artist_cover.py` needs a reference `hires.txt`; none is inside the sandbox |
| 3 Unpack (kit) | 1 min | 3 s of machine time, all three `--verify` PASS |
| 4 Paint | 1 min | 1 296 green px → red on `usr000` |
| 5 Build + verify | < 1 min | `build` 0 errors, `mep_lint.py` exit 0 |
| "See it on screen" | 8 min | **not a row in the table**; 7 of the 8 were diagnosis, not looking |
| **Total** | **12 min** | well inside the one-hour budget |

## Stops

Four, and only the fourth cost real time.

1. **`make` against a binary release** — answered by the release's own README.
   Fixed in the guide (PR #254).
2. **How the emulator loads a painted pack** — the guide ended at "lint exit 0"
   and never said. The evaluator reconstructed `mep/` from an `ARTIST.md`
   footnote and confirmed it with the undocumented `log` flag. Fixed in the
   guide (PR #254).
3. **`hdpack` records, it does not load** — the usage line does not say which
   way each flag goes, and the guide only ever showed `hdpack-off`. Fixed in the
   guide (PR #254: the six flags are now a table).
4. **A lint-clean, verify-passing build rendered 2 of the 4 tiles of the
   repainted pose from the layer underneath** — the evaluator opened
   `hires.txt` to diagnose it, hand-edited an installed copy to prove the
   figure, and then found the untouched build renders phase 0 correctly. The
   stop is real even though the workaround turned out to be unnecessary: the
   acceptance test was green while the figure was half-unpainted, and nothing
   on the artist surface said so. Filed as #255 and #256; not a guide gap.

## Phase 9 panel, sections 2 and 3

Filled by the dispatcher from the log above, following the PRD's panel
(`docs/roadmap/PRD-mesence-enhancement-ecosystem.md` §"Validation — qualitative
and intuitive").

**Section 2 — side-by-side with the artist pack.** *Not reached as written:* the
criterion compares our `auto/` sheets against a community `mep/` pack, and the
sandbox holds no Zelda reference pack (stage 2 was skipped for the same reason).
What the log does answer is the part that does not need a reference — is a
subject the artist treats as one figure reachable as one unit? **Pass**, with
seven of eight sheets named correctly on sight: `usr000`–`usr003` = Link walking
down/right/up/left at two phases each, `usr004` = Octorok, `usr005` = death
puff, `usr006`/`usr007` = HUD. The two exceptions are both about *naming*, not
about unit boundaries: `ARTIST.md` captions are `cycle000 — a 2-phase loop, seen
80 time(s)`, which the evaluator judged "fine for 8 sheets, would hurt at 80",
and it built its own contact sheet with Pillow because the kit ships no overview
image. Neither is a defect; both are the C.6/reference-pack question.

**Section 3 — find-and-edit.** *Split verdict.* The time criterion passes
comfortably and the reach-the-screen criterion passes on the supported path: the
pack was built at 23:06 and Link rendered red, pixel-identical to the kit
(2 832/2 832 px), at 23:14 — four minutes from copying the kit into the pack,
with no editor other than Pillow and `mep_build.py`. The criterion "the person
never had to open `hires.txt`" **fails**: the evaluator read it at 23:10–23:11
to find out why two tiles of the pose were still green, and hand-edited an
installed copy at 23:11. That failure is the tool's, not the guide's — the
mechanical acceptance test (`build` 0 errors, `mep_lint.py` exit 0, every
`--verify` PASS) reported success while the figure was half-unpainted, and no
surface in the sandbox could have said otherwise. It is filed as #255 and #256.

## Defects filed

| Defect | Where |
| --- | --- |
| `build` keys a `mirror: H` sheet cell at the unflipped `source` without un-flipping the pixels, so the art lands on screen mirrored; `--verify` compares keys, not pixels | #255 (P1) |
| `build` drops the unconditional fallback rule the recorder emits beside every `[condition]` rule, so a condition miss renders the `auto/` layer's unpainted art | #256 (P1) |
| How to load a built pack, what `bootstrap`/`hdpack`/`hdpack-off` mean, and `make` against a binary release | guide fixes, PR #254 — stops 1–3 |

No ROM-derived image is committed with this log; the screenshots and the
ablation strip stay in the run's scratch directory (`runs/` when a run is kept).
