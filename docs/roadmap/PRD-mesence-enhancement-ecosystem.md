# PRD — MesenCE roadmap

Consolidated roadmap of the MesenCE fork. This single document unifies the
former two PRDs — `PRD-mesence-enhancement-ecosystem.md` (pack/core) and
`PRD-player-shell.md` (default GUI / chrome) — into two Parts of one file
(2026-08-30, per the project owner's decision). Each Part keeps its own
internal `§N` numbering verbatim; a `§N` reference always resolves within
the Part that uses it. The former ownership split still holds — pack/core
work lives in Part A, player-shell/chrome work lives in Part B — it is now
expressed as parts of one file instead of two files.

Part A is the pack/core roadmap: vision and legal principles, standards,
the shipped record, and the pending slices: Phase 14 (proof at scale),
Phase 12's open F12.11 row, the ADR-0205 replay slices (Phase 13),
Phase 9's F9.18 human panel, the Phase 10 spike S10.b, and the
manual/hardware-gated residue of the shipped phases. Phase 11
consolidation is complete. Part B is the
default-GUI roadmap: player
chrome, Advanced GUI, pack identity (`pack_id`/`content_id`/version),
duplicates, the pack picker, and the quick-enhancements panel. The two
Parts intentionally do not duplicate each other's prose: each holds its own
header block, slice table, and ADR map.

---

## Part A — Enhancement ecosystem (pack/core)

**Status:** active (2026-09-23). Live work: **Phase 14** (proof at scale, opened 2026-09-23 with F14.1–F14.3 under a go-ahead; F14.4 waits on ADR-0229, `proposed`), Phase 12's **F12.11** (stop condition (2) needs a person; (3) moves into F14.1), and **Phase 13** (ADR-0205, R.1/R.2, after F14.1–F14.3). Phase 12's F12.1, F12.3–F12.10 and F12.12–F12.17 are delivered (§3), F12.2 is closed (evaluator row, 2026-09-19), and F12.18/F12.19 are merged with their owed rows in flight. Chronology lives in §3 and in each ADR's Status line, not here) — pack/core roadmap of this
fork. Player
chrome, pack identity (`pack_id`/`content_id`/version) and the in-GUI
picker live in Part B of this document (Phase 7).
Earlier plans (`PRD-ecossistema-enhancement-comunitario.md`,
`PRD-community-pack-mep-conversion.md`, `plano-execucao-F3.md`,
`plano-execucao-F5.md`, `plano-reducao-consoles.md`,
`plano-host-input-tester.md`) were consolidated here and deleted on
2026-08-27; their full text lives in git history. ·
**Author:** sbihaiko ·
**Scope:** MesenCE fork (`main`); nothing goes upstream ·
**Specs:** [MEP-v1](../specs/MEP-v1.md) · [MEI-v1](../specs/MEI-v1.md) · [ESP-v1](../specs/ESP-v1.md) · [MEP-recipe-v1](../specs/MEP-recipe-v1.md) · [hires-gbsms-v1 (draft)](../specs/hires-gbsms-v1-draft.md) ·
**Decisions:** `docs/adr/` — `accepted` ADRs are binding; §6 lists the ones this roadmap depends on ·
**Process:** one implementation task per **slice**, with its ADRs settled first. A slice is done when all applicable acceptance checks have evidence of the required class (structural, runtime or human), its live row is removed, and the delivery record and header are updated. A documentation review may reconcile the whole PRD without implementing its slices.

---

### 1. Vision and legal principles

MesenCE turns the emulator into a **platform for extracting, authoring and
consuming enhancement packs** (textures, replacement audio, synth presets),
with the community producing the content and the project staying legally
clean. The reference for the thesis is SUPER ZSNES's per-game curated
enhancements; the difference is that everything here is open (CC0 specs,
GitHub as backend, no server).

Principles that every phase below obeys:

1. **Distribute the tool, never the files.** Extractors and installers are
   ours; extracted MIDIs, tiles and third-party redrawn textures stay on the
   user's machine or in the hubs that already host them.
2. **The official channel carries only clean data:** specs, hash mappings,
   presets, catalogs (URLs + hashes + licenses), tools. Derivative content
   is *referenced*, never hosted or committed.
3. **The emulator is content-dumb:** no bundled derivative material, no P2P,
   no monetisation (*MGM v. Grokster*, Yuzu 2024).
4. **Hosts never execute pack content as code** (MEP-v1 §6). Patches and
   recipes are declarative data interpreted by a fixed vocabulary.
5. **No LLM in the client.** LLMs run only in CI (the community-pack classify
   step); whatever they emit is validated by deterministic scripts before a
   human or the client sees it. `Core/`, `UI/` and the installer never call
   a model, hold a key or carry a prompt. An external tool under `scripts/`
   is not the client, but what it may send off the machine is governed by
   ADR-0154, not by this principle (see Phase 10).

Product consoles on `main`: **NES, GB/GBC/GBS, SMS/GG/SG-1000, GBA**. SNES
(incl. Super Game Boy), PC Engine, WonderSwan and ColecoVision were removed
on 2026-08-26 (`master` is the frozen full-console snapshot; never merge
`upstream/master` into `main`). SNES **gamepads** (`SnesController`) stay as
host/console input devices. MSU-1 left with the SNES core.

### 2. Standards

Rule: adopt an existing standard when one exists; write an open spec (CC0,
RFC 2119, semver, golden file, `scripts/validate-specs.py`) only for what
does not exist.

| Area | Standard | Status |
|---|---|---|
| ROM identification | No-Intro sha1 (iNES header-size normalization, ADR-0039/0044) | shipped |
| Textures | HDNes `hires.txt` (Mesen is the reference implementation) | shipped, NES/GB/SMS |
| NES replacement audio | OGG via HD pack `<bgm>/<sfx>` + APU fingerprint trigger (ADR-0047) | shipped |
| Patches | IPS/BPS in `patches[]` by sha1 (ADR-0044) | shipped |
| Audio log / score | VGM 1.71 + GD3, SMF type 1 + GM | shipped (F1) |
| **ESP v1** — Enhanced Synth Preset | `docs/specs/ESP-v1.md` | v1 |
| **MEP v1** — pack container | `docs/specs/MEP-v1.md` (§2.1 folder-form, sibling folder, `auto/` layer; §3 `pack.json` optional; §4 hash; §5 sections; §6 security) | v1.6 (v1.1 `patches[]` + folder-form/`auto/`; v1.2 `targets[].md5`; v1.3 rule-9/§6 as-code wording, ADR-0121/0138; v1.4 root `id`, ADR-0140; v1.5 `border`, ADR-0149; v1.6 root `generated`, ADR-0154; all additive) |
| **MEI v1** — discovery index | `docs/specs/MEI-v1.md` (federated `manifest.json`) | v1.4 (Phase 6 made it real as v1.1; D3 v1.2 `rom.sha1s`, D13 v1.3 `pack_id`/`content_id`/`votes`, F6.8 v1.4 `errata` §2.6, all additive) |
| hires.txt extension GB/SMS (OGG on GB/SMS) | `docs/specs/hires-gbsms-v1-draft.md` | draft, frozen until a second implementer appears |
| **MEP Recipe v1** — re-packaging of split-distribution packs | `docs/specs/MEP-recipe-v1.md` | v1 |

### 3. Delivery record

Implementation records do not certify unperformed product acceptance. Detailed
chronology remains in git history and the cited ADRs; current debts belong in §4
or Part B §8. Dates below describe delivery, not a new validation run.

- **F1–F3** — MIDI/VGM export, GB/SMS HD builder and MEP host (ADR-0036–0042).
- **Console reduction** — NES, GB/GBC/GBS, SMS/GG/SG-1000 and GBA on `main` (2026-08-26).
- **F5.1–F5.3** — sibling/auto discovery, image and audio bootstrap, fingerprint replacement (ADR-0043/0044/0047/0049).
- **F5.4a–f** — backgrounds, palette cap, build/preview and sound-driver tooling; the original spatial grouper was replaced by Phase 9 (ADR-0050/0051/0132).
- **F5.4g / F5.5** — level-2 audio, loops, SFX masks, crossfade, UI and regression integration (ADR-0052/0133/0134/0135/0142); listening and installer SoundFont remain in §4.
- **F6.0–F6.3b** — community intake, MEP Recipe, catalog and deterministic gates (ADR-0121/0138).
- **F6.4a–c** — offline recipe interpreter, client download/install and shared discovery fixtures (ADR-0138).
- **F6.5–F6.8** — rollout, headless smoke, automatic loading and known-missing errata (ADR-0146/0151/0152); native picker and live CI validation remain in §4.
- **H1–H7 / D1–D13** — tests, doc gates, identity/spec reconciliation and ADR reference checks (ADR-0122–0131/0136/0137); explicit residual debts remain in §4.
- **H8** — `NES_ONLY`/`LessUI` declined after measurement; per-translation-unit test compilation retained (ADR-0158).
- **H9 / H10** — headless input tests and four-arm accuracy comparison (ADR-0127/0162); accuracy CI remains deferred.
- **I.0–I.3** — input tester and mapping feedback; physical-device checks remain hardware-gated.
- **P.0–P.7** — player shell, catalog identity and preference resolver (ADR-0139/0140/0141, 2026-08-28–09-01).
- **P.1-local** — the local-container `content_id` cache (ADR-0206, 2026-09-17):
  a stamp-less drop gets its ADR-0139 identity from
  `EnhancementPacks/.cache/content-ids.json`, read at load with a one-`stat`
  staleness check and maintained by a fingerprint-validated background refresh;
  a drop equal to a stamped container adopts that `pack_id`, so Part B §5's
  local/catalog merge collapses the pair. Bloco G in `scripts/core_unit_tests.cpp`
  plus `scripts/p1_local_identity_check.py` (cold/warm, nested edit, pruning,
  adoption, zip-with-prefix).
- **F8.1–F8.3** — pack border layer (ADR-0149); optional rendering/lint residue is F8.4.
- **F9.0–F9.5** — legible vocabulary, maps, sheets and sprite grouping (ADR-0153); delivered on spot checks, not a completed human panel.
- **F9.6** — external repaint scaffold and classical output (ADR-0154/0161); ADR-0192 retires the unmeasured generative commitment.
- **F9.7–F9.13** — aliases, stitching evidence, static-screen routing, CHR separation and gameplay probe (ADR-0153/0156/0160).
- **F9.14–F9.17** — frame-counted input, capture, sprite vocabulary and adjacency (ADR-0157/0164/0166/0167).
- **Live recorder/viewer** — one-way, nonblocking publication (ADR-0169).
- **F9.18 implementation** — composition engine and GUI, then pose-based sprite selection (ADR-0165/0166/0171); human acceptance remains open.
- **F9.19** — pose sidecar (ADR-0170); membership is evidence, not proof of editor addressability.
- **S10.a** — original walk failed at 6.7%/10.5%; sidecar rerun found 25/25 Mega Man poses (2026-09-11); no claim of complete subject editing follows.
- **S10.c/S10.d** — `generated` survives packing; studio data stays outside the pack; sheet-key coverage check delivered and narrowed after #218. Visual correctness is a separate gate.
- **Recorder/build fixes** — CHR indices, screen-fixed sprites, sheet/pose joins, layout gaps, per-frame denominators, fusion and mirror provenance (ADR-0172–0178).
- **F9.20 / F9.21** — pose succession/cycles/variants delivered; rigid parts withdrawn on measurement (ADR-0179/0180).
- **F9.22 / F9.23** — per-state recording, two-port input and interruption-based driver attribution (ADR-0181/0182); Contra mechanisms covered through stage4-boss, not all stages.
- **F9.24** — four artist-kit surfaces, assembler and coverage tools (ADR-0183); portability exercised on Contra, Castlevania, Metroid and Zelda II.
- **F9.25 tooling and evidence** — RAM-cheat and navigation drivers (ADR-0184);
  the bounded second-pass matrix was recorded 2026-09-15: nine rows, each with a
  hash-identified clean control, four measured second passes, five named
  reasons, and a union rebuild that passes structural validation.
  [Log](../validation/f925-contra-matrix-2026-09-15.md).
- **F9.26** — FM2 conversion and movie recording (ADR-0185); increased coverage measured, synchronization not established by key count (#201).
- **F9.27** — CDL analysis and recording (ADR-0186); measured cost approximately 1.9× on the logged Zelda run.
- **F9.28 / F9.29** — AI review as human-promoted proposal and emitted nearby conditions with fallback (ADR-0188/0189/0190).
- **Pack hosts** — Dropbox/MEGA support and cross-implementation allow-list drift check (ADR-0187).
- **C.1–C.3** — Linux compilation and all suites in the PR gate, register reconciliation, catalog/board hygiene (ADR-0191; 2026-09-14).
- **C.4** — `mesence-v0.1.0`, macOS Apple Silicon binary and guides (2026-09-14).
- **C.5 experiment completed** — two independent agent runs took 12/11 minutes and found visible edits, but required `hires.txt` diagnosis and exposed incomplete painting; product acceptance remains unproven. [Zelda log](../validation/c5-fable-artist-run-zelda-2026-09-14.md), [Mega Man 3 log](../validation/c5-fable-artist-run-mega-man-3-2026-09-14.md). Findings #253/#255/#256 require current-binary verification under F9.18-V, regardless of issue closure.
- **C.6** — second reference measured: Zelda II 165/874 tile shapes, 28/3301 exact keys, 670 emitted versus 4679 authored conditions, 3.78× palette inflation; lint success was not a runtime round-trip proof.
- **C.7/C.8** — file ceilings, dependency pins and ADR debts closed (ADR-0137/0192; 2026-09-15).
- **F9.18-V** (2026-09-15) — with a binary rebuilt at `main`
  `04d7fc63`, the structural gate and the paint-application gate pass on both
  golden games: Mega Man 3 (CHR ROM) diffs only inside the edit's bounding box,
  Contra (CHR RAM) shows every differing pixel magenta. #253 passes structurally
  and visually, #256 and #255 structurally; the runtime condition-miss and live
  mirrored-instance checks left open by the first re-run were closed the same day
  with a negative-control pack replayed on the same binary (no `Core/` change).
  [Log](../validation/f918v-current-binary-painting-2026-09-15.md).
- **ADR-0193** — PR and main-push CI triggers retained; workflow details live in `.github/` and the ADR.
- **F12.1** (2026-09-17) — the Phase 12 scale reference is measured instead of
  assumed. `NesConsole::LoadHdPack` takes **412 ms** on the installed Metroid
  pack (431 ms on the loose `HdPacks/` twin, 295 ms on a synthetic 300 000-line
  file); 60 s headless runs at 200–260 fps with the pack against 508 without,
  peak RSS 2.2–3.0 GB against 24 MB; `mep_build.py build` 2.70 s and
  `mep_lint.py` 0.59 s on 300 000 lines. The pack's counts are stated with their
  definitions: 67 images, 150 199 tile rules (`Tiles.size()`), 8 401 keys as
  distinct `(tileData, palette)` — the artist-evidence definition — against
  9 197 in the loader's `TileByKey`, whose key carries two fields the pair does
  not. Numbers only, no optimization; the Core gains two `[MEP]` timing lines so
  the numbers are reproducible from a plain run. The finding that shapes F12.3:
  the 412 ms is the parse, while the bitmaps decode in a detached
  `HdPackData::LoadAsync` at **13.2–16.4 s for 271 images**.
  [Log](../validation/f12.1-scale-and-load-2026-09-17.md).
- **F12.8** (2026-09-19) — the `unsorted` remainder sheet. `HdPackBuilder::BuildSheets`
  writes `unsorted.png`/`.orig.png`/`.json` last, carrying one 8x8 cell for every
  shape in the recorder's registry that no other sheet put on a canvas. Coverage
  of that registry is by construction: the builder
  accumulates the shapes each sheet claims as it writes them, and the remainder is
  what is left. Not alias-collapsed (the leftovers are unrelated by construction);
  a shape with no drawable art is left off rather than shipped as a hole; an empty
  remainder writes no file. ADR-0209 Q4 option (k). Measured on a 60 s Castlevania
  bootstrap recording: 457 shapes claimed by the existing sheets, **135 more on
  `unsorted`, overlap zero** — 12x12 grid, `unsorted.png` 436x436 against a 109x109
  `.orig.png` twin, so the `_EditedProbe` 1x contract holds. Zelda, 85 s played:
  187 -> **277**, also overlap zero. Donkey Kong's sheets cover everything and
  correctly write no file. **Scope, stated honestly:** this makes sheet coverage
  of the shape registry 100%, not of the pack — Zelda's `hires.txt` names 1 615
  distinct shapes against the registry's 277, because `ShapeIdFor` is only
  reached from the retained frame stream while `ProcessTile` emits rules for
  everything the PPU draws. Closing *that* gap is a decision about what the
  recorder retains; see ADR-0209, "What (k) actually closed".
- **F12.3** (2026-09-19) — the pack's repainted images come back without
  reopening the ROM. ADR-0212: the reload re-decodes, **in place**, only the
  images whose `(size, mtime)` fingerprint moved, and the `HdPackData` object
  never moves — so none of the three raw `HdPackData*` holders (`HdNesPpu`,
  `HdVideoFilter`, `HdNesPack`) needs coordinating, and the one that lives on
  `VideoDecoder`'s decode thread is handled by draining it with
  `WaitForAsyncFrameDecode()` at the frame boundary rather than by locking the
  per-pixel read path. Surfaces: the **Reload Repainted Images** menu action
  under HD Packs, the `RequestMepImageReload` interop entry point, and the
  headless `reload-at-frame=<n>` + `replace=<dst>=<src>` flags. Stop rule met:
  a run that repaints mid-play and reloads lands on the byte-identical final
  frame as a run that had the repaint from the start (`0xA8693E63`), both
  differing from the untouched control (`0xDBA93B36`). Cost **0 ms** for a no-op,
  **2 ms** for one sheet, **25 ms** for all 19 images of the test pack. A
  resized canvas is refused per image and the old pixels survive; a manifest
  edit still needs a reopen (ADR-0212 non-goal). The implementation found a
  second cache the ADR had missed — `HdPackTileInfo` memcpys its crop out of the
  bitmap — so the sweep also re-cuts the affected tile rules; ADR-0212 §1 is
  amended to say so.
  [Log](../validation/f12.3-reload-repainted-images-2026-09-19.md).
- **F12.4** (2026-09-19) — the name a painting surface is written under is now
  a contract with the artist's paint program, not a convention. ADR-0213:
  `scripts/asset_names.py` holds the rules all three readers need — Photoshop's
  *Generate Image Assets* layer-name grammar (a comma splits one layer into two
  assets, a leading `2x ` resizes, `.png24` drops the alpha), a file system that
  may be Windows, and the kit's own manifest — and every generator checks
  against it where it writes. A name we compose raises; a name derived from
  outside input (`<stage>-NNN.png`, `pano-<map stem>.png`) is sanitized with the
  original recorded. Each `kit-part-*.json` entry gains `assetName`, the string
  an artist pastes as a layer name, and the assembler is the last gate — it also
  catches the rule that only exists between names, two surfaces in one folder
  differing by case. `ARTIST.md` and `docs/remastering-a-game.md` gain the
  **open, paint, save** step: one line per program, then *HD Packs > Reload
  Repainted Images*. Measured on 429 real surfaces across the Contra and Zelda
  kits — all valid, no case clashes — and end to end: a painted kit surface
  copied onto the pack **by `assetName` alone** renders byte-identically
  (`0xC4D2F4DD`) to the same paint applied before load, against a control of
  `0x55645B9C`. Photoshop's `-assets` output folder is not configurable, so that
  one path costs a copy; the docs say so rather than implying an in-place
  overwrite that does not happen. ADR-0209's Q3 was decided 2026-09-20 as option
  (i) — the return is whatever F12.4's template decides, with ADR-0209 adding
  only the launch and the reload trigger. Read against what this row shipped,
  (i) resolves to (h): the explicit re-import F12.3 delivered.
  [Log](../validation/f12.4-asset-name-template-2026-09-19.md).
- **ADR-0209 Q1** (2026-09-22) — the Core infers a default `label` at record
  time and writes it beside `"labelSource": "inferred"` on every sheet cell,
  `sprNNN`/`objNNN` group sheet, pose and run (`Core/NES/HdPacks/SheetLabels.h`,
  host-free; counts and geometry only, never a subject — ADR-0183 §3/§5).
  Readers resolve `names.json` > sidecar label > id in one function
  (`compose_engine.caption`) and say which won; `mep_figure.py export`
  writes the label, `artist_kit.py` titles the grid with it. Go-ahead
  verbatim *"vai com o Q1 da ADR-0209 em paralelo também"*. 966/966 core
  unit tests, `scripts/test_sidecar_labels.py` (31 checks, in doc-checks),
  Contra 60 s route: 49/49 sidecars labelled, `mep_build` round-trip
  byte-identical. Sonnet verification pass: PASS, no subject noun in the
  label code, round-trip reproduced by the suite itself.
  [Log](../validation/adr0209-q1-inferred-label-2026-09-22.md).
- **ADR-0209 Q2/Q3** (2026-09-22) — a figure is exported whole and comes back
  cell by cell. `scripts/mep_figure.py export <pack> sprNNN|objNNN|poseNNN`
  reassembles the figure through its pose/`evidence[]` offsets into one PNG at
  pack scale, a 1x `*.orig.png` twin and a sidecar mapping each cell rect back
  to `(sheet, cell index)`, on the F12.4 asset-name contract; `import` writes
  only the cells that differ from the twin into the source sheet PNG, refuses a
  resized canvas, and `--verify` is the ADR-0183 §4 round-trip. Measured on
  Contra `spr000`: 10 cells (8 on `spr000.json`, 2 on `sprites.json` — the
  ADR-0168 under-grouping made visible), unpainted import writes 0 cells, one
  painted cell writes exactly 1, keys 116 → 116, `mep_build` and `mep_lint`
  exit 0. New module because `mep_build.py` and `sheet_repaint.py` sit at their
  line ceilings. Not verified: the in-game F12.3 reload and a real paint
  program (the paint step was a programmatic fill). Q1 (Core-inferred `label`)
  shipped the same day (entry above).
  [Log](../validation/adr0209-q2-q3-figure-export-2026-09-22.md).
- **F12.17** (2026-09-23) — a legacy pack keyed against an IPS-patched ROM is
  imported against the patched ROM (ADR-0198 §3, option (a); go-ahead
  *"Construir já, em paralelo (Recommended)"*). `mep_import.py import --rom
  <stock dump>` applies the IPS in memory (`scripts/mep_patch.py`, a mirror of
  `IpsPatcher.cpp`), writes the patched whole-file sha1 as `<supportedRom>`,
  carries the IPS and the `<patch>` lines, and prints what this does not buy:
  the project lives in the patched ROM's key namespace, and stock-ROM
  recordings do not land there. Castlevania #143: 7 400 keys, 0 missing /
  extra / differ, lint rc 0; Metroid #148: 148 715 keys, 0 differ (lint rc 1
  from the source pack's own 3 403 errors, partly the false positives of
  issue #382). Mega Man #138 fails `verify` on the plain path too (issue
  #381); Zelda, Zelda II and a Rev A Castlevania dump are refused per
  ADR-0145 (3). Side fix: `<ver>108` is no longer lowered to 103. 105 import
  tests; Sonnet verification PASS. Not exercised: a render in the emulator
  and the ADR-0211 installer path.
  [Log](../validation/adr0198-s3-patched-rom-import-2026-09-22.md).
- **F12.13** (2026-09-22) — a variant may not add content the capture lacks
  (ADR-0221, option B), in `MesenSheets::SelectScreenAnchors` after the
  `kAnchorVariantAgree` test; go-ahead verbatim *"dispara as frentes 1, 2, 3 e
  4 em paralelo usando workflows"*. Stop condition (2) met — six unit cases on
  a synthetic `GridFrame` pair, 927/927. Stop condition (3) met — the 30-ROM
  bounded library re-recorded on both binaries, `tileAtPosition` gates and
  `<background>` lines byte-identical 30 of 30, so capture count and draw rate
  did not move; the kind test reclassified 5 091 frames as rivals across 15
  ROMs. Stop condition (1) **not met**: the Punch-Out!! sweep still reports
  437 erased cells on 12 of 36 frames, before and after, with the 30
  `<condition>` lines byte-identical — the recorder filed 228 frames as
  addition-rivals and could not act on any, because the only cell that
  separates the capture from a one-letter-later frame is flat on the capture
  side and ADR-0050 excludes flat cells from the anchor pool. The rule was not
  loosened to chase the number; the follow-up is ADR-0223 (`proposed`). Ships
  `scripts/measure_capture_draw_rate.py`. Sonnet verification pass: numbers
  reproduced from the artefacts, prototype code absent from the diff.
  [Log](../validation/f12.13-variant-kind-rule-2026-09-22.md).
- **F12.16** (2026-09-22) — emptiness probes as a last anchor pass (ADR-0223,
  option A): flat runs stay in `CaptureScreen`'s candidate list with
  `Usage = UINT32_MAX` (`AppendFlatAnchorCells`, inline in `HdPackBuilder.h`
  because the `.cpp` sits on its ceiling), `SelectScreenAnchors` excludes them
  from the stable/wide pool and, only when rivals survive, runs one more
  greedy pass over the stable pool plus the flat cells no variant changes,
  kept only if it separates strictly more rivals; new counter "N screen(s)
  gated on an emptiness probe". Go-ahead verbatim *"faz o 2 usando o
  sonnet"*. Punch-Out!!: `screen003`–`screen006` each gained a probe on flat
  tile `FD`, the card's `erased background` went **374 -> 0** on all 36 sweep
  points, 10 -> 9 captures, draw rate 0.1200 -> 0.0865 — the ADR's prototype
  numbers, reproduced. Library (30 ROMs): captures 193 -> 219 (Ice Climber
  4 -> 23, Pac-Man 17 -> 25, verified as distinct screens), mean draw rate
  0.2454 -> 0.2316 (more captures, each firing on fewer frames), never-firing
  0 -> 0, 59 probes in 9 ROMs, 14 packs with a changed `<condition>` line.
  10 unit cases, 992/992. Codex review: one predicate for "empty"
  (`IsFlatTileData`) at both call sites; a capture still needs a non-flat
  anchor, recorded in ADR-0223's Consequences. Sonnet verification pass: every number reproduced.
  Closes issue #339's first cause; with F12.15 the issue's two causes are
  both addressed for packs recorded here.
  [Log](../validation/f12.16-emptiness-probes-2026-09-22.md).
- **F12.15** (2026-09-22) — a recorded screen no longer hides a
  behind-background sprite over colour-0 canvas, opt-in per pack through the
  new hires.txt tag `<bgPreservesBehindBgSprites>` (ADR-0224), which the
  recorder writes right after `<options>`; `HdNesPack::GetPixels` re-applies
  the behind-background sprite pass after layer 2 where the rule holds
  (`HdBehindBgSpriteRule.h`, host-free); `scripts/measure_capture_overdraw.py
  --grid` splits `erased` into background / sprite (behind-bg from the `M`
  line's shadow OAM — the ADR-0222 dump carries no priority bit); spec draft
  rev. 2 §7, `remastering-a-game.md`, `ARTIST.md`, `mep_lint` accepts the tag.
  Go-ahead verbatim *"libera a F12.15, dispara as três partes em paralelo.
  mergea o PR assim que puder e garante que t  tudo na main."* Punch-Out!!
  re-recorded: with the tag the fight window reads **0 `erased sprite`**
  (18/18/27 without, 59 of 63 behind-bg), the card stays at 374 background
  (F12.16's job); without the tag 36/36 sweep frames and 9/9 library renders
  are byte-identical to the old dylib; cost 2.632 vs 2.639 ms/frame, no
  measurable difference. Stop conditions (1), (3), (4) met; **(2) partially**
  — no direct unit test of `GetPixels` (outside the unit-test link set), 16
  cases on the predicate, parse and writer. Sonnet verification pass: every
  number reproduced. The layer-3 edge is closed as a declared edge (ADR-0224 amendment, BlocoP4 model case), and the 30-ROM tag on/off sweep is measured — erased sprite 1 vs 369, erased background 6 vs 6, no measurable cost ([log](../validation/adr0224-30rom-tag-sweep-2026-09-22.md)).
  [Log](../validation/f12.15-behind-bg-sprites-2026-09-22.md).
- **F12.14** (2026-09-22) — the OAM stream dump is self-describing, so lint
  gives a verdict on every sprite and position condition. ADR-0222 option A:
  `MESEN_OAM_STREAM_DUMP` interns shapes and palettes with `K`/`P` lines like
  the grid dump and writes each sprite as `<shape>,<x>,<y>,<pal>`; `OamEntry`
  gains a palette id compared by `SameEntries`, so a recolour-only frame no
  longer collapses. `mep_conditions.py` gains `parse_oam_dump`; `spriteNearby`,
  `spriteAtPosition`, `positionCheckX/Y`, `originPositionCheckX/Y` and the
  two-address `memoryCheck` are evaluable; a pre-F12.14 dump reports `not
  evaluable: OAM stream carries no tile data`. Measured on the F12.6a Contra
  route: OAM 2 217 retained / 3 597 played, 166 `K` / 4 `P`, `oam.txt` 375 KB
  against 43.8 MB of grid; nine authored conditions all report verdicts —
  `nearThePlayer` never held 0/749, `playerBelow` always held 1221/1221 and
  its wrong-colour twin never held, `livesMatch`/`stageIsScreen` mixed.
  `core-unit-tests` 924/924, `test_mep_conditions` 43/43; dylib provenance
  proven by `nm` before recording. ADR-0169's wire was not changed (the raw
  attribute byte is already on it). Limits: mirrored offset sign not
  modelled; the grid↔OAM join is refused when played totals disagree; only
  Contra measured. Go-ahead verbatim: *"dispara as frentes 1, 2, 3 e 4 em
  paralelo usando workflows"*.
  [Log](../validation/f12.14-oam-dump-self-describing-2026-09-22.md).
- **F12.6a** (2026-09-19) — a condition an artist writes by hand is now checked
  against what the game actually drew. ADR-0197 §1–§2: a sheet may carry a
  `conditions` block (`authored: true`, the emulator's own `<condition>` syntax),
  `mep_build.py` emits each definition once above the rules that cite it and
  gives every conditioned cell the ADR-0189 §3 bare twin behind it, and
  `mep_lint.py --routes` replays every authored condition over every retained
  frame of every recording. The shared rules live in `scripts/mep_conditions.py`,
  written from `HdPackConditions.h` so the report cannot quietly disagree with
  the emulator. Measured on the six F9.25 Contra routes — 22 772 retained frames
  standing for 85 429 played — with three hand-written conditions: **none held
  everywhere**, and `openToTheRight` fired 714 006 times on keys it was never
  attached to against 2 263 where it was, which is the `tileNearby` failure mode
  ADR-0197 §2 asks for by name. Two things the row did not anticipate are in the
  log: `spriteNearby` reports **`not evaluable`** (the OAM dump carries
  vocabulary indexes, not tile data — the log pointed at F12.6b, but that
  slice shipped ADR-0197 §3's *memory* plane only and `spriteNearby` is still
  `not evaluable`, so the OAM change waited on ADR-0222, accepted 2026-09-22 as F12.14), and
  the six routes share their opening, 249 of the first 300 retained frames
  identical cell for cell, so they are less independent evidence than six
  recordings sound. `not evaluable` is never counted as a pass.
  [Log](../validation/f12.6a-lint-authored-conditions-2026-09-19.md).
- **F12.7** (2026-09-17, completed 2026-09-19) — an existing community pack
  becomes something this fork can edit. `scripts/mep_import.py` reads a plain
  legacy `hires.txt` pack — no `<patch>` — and writes a MEP project: sheets cut
  at the rules' own coordinates, the manifest carried verbatim as the `auto/`
  key source, `<background>`/audio along, unknown tags refused with the line
  cited rather than dropped (ADR-0198 §1). The 09-17 build round-tripped the
  rule set everywhere and the **pixels** only on packs that key each tile
  pattern at one crop; a legacy pack animates by keying one pattern at several
  crops, one per condition, which `mep_build.py` could not express — 914 keys
  differed on Contra80s and 1 467 on Super Mario Bros., and the log said so
  instead of claiming the row. F12.6a's per-cell condition (ADR-0197 §1) made
  it expressible, and on 2026-09-19 each of those rules got a cell of its own,
  pinned with `exactCondition` to the condition it carried, so no key is
  emitted twice and no precedence rule picks the crop. **0 differing keys** on
  all three packs measured (Ninja Gaiden 19 147, Contra80s 8 950, SMB 3 601),
  with `verify` comparing loader keys as sets and every crop as a sha256 over
  its RGBA block; the invented ADR-0189 §3 twins fell from 298 to 119 on SMB
  because a split pattern now emits the input's own rules and nothing else.
  Bomberman, the row's third bounded input, is not on this machine and was not
  downloaded — its 09-17 pass stands and is not restated as a new measurement.
  Five of the ten installed packs are refused for shipping a `<patch>`, naming
  ADR-0198 §2; their import shipped as F12.17 (2026-09-23, §3).
  [Log](../validation/f12.7-legacy-pack-import-2026-09-19.md).
- **F12.6b** (2026-09-19) — the recorder now keeps the console's internal RAM,
  so a `memoryCheckConstant` is a verdict instead of a shrug. ADR-0197 §3
  option (b): every retained grid frame carries `$0000`–`$07FF` as an `M` line
  (4096 upper-case hex characters, the byte at address A at characters
  2A/2A+1), and `mep_lint.py --routes` reads it. Four **real** `Contra80s 1.1`
  conditions replayed over a 60 s Contra stage-1 route — 607 retained frames
  standing for 3 597 played — came back `always held` (`BaseDoorFix2`, `$30`
  == 0), `mixed` (`screen01`, `$64` == 1: 6 081 held / 13 697 failed) and
  `never held` (`stage5`, `deathstarblinkstart`); twelve hand checks over
  frames 0, 314 and 606 all matched, and frame 0's bytes match the minted save
  state read by `mss_ram.py`. The cost was **measured before it was written
  down**: +2 488 093 B of grid dump (+6.42 %, exactly 4 099 B × 607 frames) and
  wall clock inside run-to-run noise. All 486 `memoryCheckConstant` lines of
  `Contra80s 1.1` are inside the window. `spriteNearby` is **still** `not
  evaluable`, and so is `memoryCheck` — the slice widened the memory plane
  only.
  [Log](../validation/f12.6b-recorder-retains-internal-ram-2026-09-19.md).
- **ADR-0211** (2026-09-19) — a pack that names a different ROM no longer gets
  stamped with the ROM in hand. The fix for issue #314, where
  `Bomberman/mep/` held the Contra 80s pack and rendered Contra's art for ten
  days: `BuildLegacyPackJson` wrote the **loaded** ROM's hash over the
  artifact's own `<supportedRom>`, manufacturing a pack that matched cleanly
  forever after. `InstallHdLegacy` now reads the extracted `hires.txt` before
  writing `pack.json` — a contradicting declaration refuses the install, leaves
  no `mep/` behind and logs both hashes; a matching one is written into
  `targets[0].sha1` in place of the loaded ROM's, so the stamp records the
  pack's own claim. Absent or malformed declarations still install unchanged:
  ADR-0145's optimism is about the *absence* of evidence, and this is about
  contrary evidence. Two amendments the measurement forced, both in the ADR:
  the No-Intro body hash counts as a match (the loader already accepts both
  forms for `<patch>`, and an installer stricter than the loader would refuse
  packs the loader then applies), and a declaration equal to the pack's own
  `<patch>` target is the **patched** ROM (ADR-0198 §2) — without it, Zelda
  Remastered, a pack issue #314 had cleared, would have been refused. The
  decision is host-free in `LegacyHdPackInstall` (16 tests), the whole-file
  hash reaches the installer through a new `GetMepRomFileSha1` export kept
  deliberately separate from the No-Intro one, and
  `verify_community_install_from_zero.py` mirrors the guard so the two cannot
  drift. Replayed against the six packs on disk: #314 refused, the same pack
  under Contra accepted, Zelda accepted as a patch target, Pac-Man refused as
  the intended trade.
  [Log](../validation/adr-0211-supported-rom-guard-2026-09-19.md).
- **F12.10** (2026-09-19) — recording a folder of ROMs is a job, not an
  afternoon. `scripts/record_library.sh <roms-dir> <out-dir> [seconds=60]`
  resolves a driver per ROM — a declared route set, then a `.bk2` for that exact
  dump, then a lone entry script, then `static` — records, builds the kit with
  `artist_kit` / `artist_bg_kit` / `artist_chr_kit --also` / `artist_kit_assemble`,
  and writes `library-report.md`. `scripts/library_job.py` holds everything that
  decides what the job does, so it is unit-tested (22/22) rather than buried in
  shell. Measured on the row's three-ROM bounded input in **83 s**: Mega Man 3
  and Zelda `routes`, SMB `static`, all six kit `--verify` parts 0 errors, and
  Zelda's row showing precedence *observed* — a movie also matched and (a) won.
  Two assumptions in the row did not survive contact with a checkout, both in
  the log: **no `.mss` is versioned**, so the job now mints the states its
  routes need into its own scratch copy (before minting, Mega Man 3 kept **2**
  retained frames; after, **6 891**), and **a route set did not say which ROM it
  was authored against**, so a set now declares its dump in `stage-set.json` and
  an undeclared set never matches — folder-name matching was available and
  refused, because that is issue #314's shape. Only `mm3` and `zelda` are
  declared: the two this run verified. Path (d) resolves but produces nothing
  until F12.9 ships, and the SMB row says so.
  [Log](../validation/f12.10-unattended-recording-job-2026-09-19.md).

- **F12.5** (2026-09-19) — the composition editor now emits `<addition>`, the
  one thing the format could do that this toolchain never wrote. An overflow
  layer on a pose reserves a blank sheet cell per cell the artist wants drawn
  outside the hardware silhouette, keys it with ADR-0196 §3's synthetic target
  and exports an `additions[]` record that `mep_build.py` turns into the tag,
  anchored on the pose's **root** cell (ADR-0189 §1's most-seen member, the
  opposite of `pose_anchor`). Every decision about that key lives in
  `scripts/mep_addition.py`, so the editor, the build and `mep_lint.py` read one
  rule. Measured on the row's bounded input: **Mega Man 3** (CHR ROM) target
  `2000` — the first index past its 8192 CHR tiles, asserted against the iNES
  header rather than the sidecar — and **Contra** (CHR RAM) target
  `00000000000000010000000000000000` / `0D0D0D0D`, the reserved pattern and the
  `$0D` palette, with §3's evidence check run over the recording's own
  vocabulary (`[]` on both). Both build and lint at exit 0. Pixel-exact on
  frame 1924 of each: against a control that differs only by the tag, the Mega
  Man 3 frame changes in exactly 800 pixels, one 32×32 block, all of them the
  synthetic cell's paint — and moving the offset by one cell translates that
  block by exactly 32 px and nothing else. Two things the row did not
  anticipate are in the log: the pose's most-seen member is usually a
  **transparent** OAM cell, so `pose_root` skips blanks or the overflow would
  fire off every blank sprite cell on screen; and `HdNesPack::ProcessAdditionalSprites`
  corrupts the screen's first pixel through a C++ reference assignment whenever
  a pack declares any `<addition>` — an upstream defect, visible in the Contra
  frame, reported rather than patched here.
  [Log](../validation/f12.5-addition-overflow-layer-2026-09-19.md).

- **F12.9** (2026-09-20) — a kit now projects over the ROM alone, with no play
  session at all (ADR-0219, accepted and implemented in the same change).
  `artist_chr_kit.py --static` takes a **missing** pack folder and derives every
  page from `--rom`: one page per 4 KB CHR ROM bank, every cell `fill` /
  `seen: false`, and a manifest of its own that `mep_build.py build` reads as a
  **pages-only pack** — no `textures/sheets/`, nothing to slice, because the
  manifest already names the page each key is painted on and the crop inside it.
  Measured on the row's bounded input: **SMB** 2 pages / 512 rules in **0.51 s**
  and **Mega Man 3** 32 pages / 8 192 rules in **5.69 s**, every rule
  `defaultTile=Y`, `mep_build` 0 errors and `mep_lint` 0 errors / 0 warnings; one
  cell painted on SMB's bank 0 changes **exactly 1 024 pixels** of the title
  screen — one 32×32 block, all of them the painted colour. Two things the row
  did not anticipate are in the log: the first Mega Man 3 run took **10.13 s**
  and would have failed the row by 1.3 % on the blank canvas alone (16.7 M
  per-pixel writes that the ROM fill then overwrites; one buffer instead took it
  to 5.69 s), and a static pack matches **100 % of background tiles** on both
  games — ADR-0210's `defaultTile = Y` wildcard observed at run time rather than
  argued. What it does not give is organisation: no figure, no scenery, no map,
  and `ARTIST.md` says so in its first line. A CHR RAM game is refused, naming
  F12.12 as the only static source of shape for those 7. **F12.10's path (d)
  closes with it**: a ROM matching no route set, no `.bk2` and no entry script
  resolved to `static` and produced nothing; `record_library.sh` now projects the
  static kit there, and the SMB row of a one-ROM job reads `static kit from the
  ROM alone — nothing was seen in play`, `seen % 0.0`, `--verify 0`.
  [Log](../validation/f12.9-static-kit-from-the-rom-2026-09-20.md).

- **F12.12 — shipped** (2026-09-20, PR #361; measured 2026-09-20).
  `mep_import.py index <their hires.txt> --pack <ours> --rom <dump>` implements
  ADR-0210 §3: a community pack's `hires.txt` read as facts about the ROM, never
  opening a PNG of it. On a CHR RAM game every 32-hex key the recording does not
  hold is rendered from its own 16 pattern bytes into `sheets/index.png` with
  provenance `index`, `seen: false`; on a CHR ROM game only palettes are taken,
  and only in range. The three mandatory filters each have a test, as do the two
  refusals (dump sha1 ≠ the recording's `<supportedRom>`; data-keyed and
  index-keyed packs never meet), and a trapped decoder proves no PNG of the input
  is opened. The bounded input ran on 2026-09-20, on the two catalog packs at
  the sha256 their rows declare, against the dumps their `<supportedRom>` names:
  **Contra80s → 2 583 cells rendered** from the pack's own pattern bytes
  (13 218 of their rules, 3 404 of their shapes, 3 735 rules we already had,
  6 166 keys, 131 palettes, 1 364 conditioned rules contributing a bare key and
  no condition, 0.93 s) — against ADR-0210's prospective +2 585, a difference of
  two shapes. **Ninja Gaiden → 0 shapes and 401 palettes**, 19 153 of 19 153
  rules in range, no sheet written, 0.16 s. The run also **retracted ADR-0210's
  Context item 2**: its "5 532 keys out of range" is a base-16 reading of a
  `<ver>`100 pack's decimal tokens, and read the loader's way — which is what
  the shipped code does — the pack's highest index is 8 190 against 8 192 tiles
  and nothing is dropped. The filter, the decision and the wrong-ROM case all
  stand; the example did not. ADR-0210 amended the same day.
  [Log](../validation/f12.12-third-party-index-read-2026-09-20.md).

- **ADR-0217 / ADR-0218 — shipped** (2026-09-20, commit `186077d0`; measured
  2026-09-20). Both were accepted on 2026-09-20 and the recorder change landed
  the same day: the forced-rival set shared by ADR-0217 Option C and ADR-0218
  Option A, the write-time exact-key check against every already-committed
  capture (ADR-0217 Option A), and the post-hoc same-priority collision scan with
  its drop-and-log and build-time warning (ADR-0218 Option B). The key comparison
  is host-free in `ScreenStitcher.cpp` and unit-tested there. Re-recorded on
  2026-09-20 over the four worst games of the F12.2 sweep, same 60 s power-on
  route before and after, with the "before" run reproducing the sweep's gate
  definitions byte for byte as a control: **95 co-gated captures of 108 became
  0 of 61**. Extended the same day to the whole 30-ROM library: **193 captures,
  0 co-gated** — the absolute form of the claim, which needs no route matching.
  Five of the eight offending games lose no capture at all (Bomberman,
  Punch-Out!!, Tennis, Mario Bros., The Flintstones); the cost concentrates in
  three. **It does not close issue #339**: re-rendered on the post-change pack,
  the pre-fight card still loses the game's own `STARRING` / `LITTLE MAC` —
  7 808 text pixels drawn by the ROM, 0 in the render — because `screen003`,
  frozen at an earlier moment of the same card, draws over it. Its gate is
  unique in the pack (that is what these ADRs bought); it is simply not
  sufficient to identify the frame, since none of its three probes samples a
  cell that changed. Separating a capture from **frames** is a different
  decision; it was taken on 2026-09-22 — ADR-0221, option B, shipped the
  same day as **F12.13** (entry above). Punch-Out!! (issue #339's game) is the clean case — all ten
  captures kept, none skipped, and the five credits screens that shared one
  probe triple now carry five distinct ones, which is ADR-0217 Option C
  separating rather than discarding. The cost is Option A's refusals, and it is
  large on games whose screens barely differ: Ice Climber goes from 25 captures
  to 4. ADR-0218 Option B never fired (0 post-hoc drops on all four). The first
  attempt at this measurement read the stale dylib on disk and nearly reported
  "the change does nothing"; the binary must be proven to contain the change
  before it measures anything, and the F12.9 log's binary-provenance sentence is
  corrected for the same reason. The ceiling raise it cost is recorded as
  ADR-0137's tenth amendment.
  [Log](../validation/adr0217-0218-anchor-gate-collisions-2026-09-20.md).


### 4. Roadmap — pending work, by slice

#### Phase 6 — Community pack auto-install (MEP Recipe v1)

**Shipped** — F6.0–F6.8, 2026-08-28 → 2026-09-04 (ADR-0138, 0143, 0144,
0146, 0148, 0151, 0152); record in §3. It realized the former Phase 4 (pack
browser + official index): the catalog JSON is the MEI, the Issue Form is the
contribution path, install/update happens in the client.

| Pending | State |
|---|---|
| F6.5 native OS file-picker step of the user-supplied-audio install | manual; no live row can raise the prompt today (all rows `hd-legacy`); every other step of that pass is unit-tested |
| CI live validation (`LIVE_VALIDATION_ENABLED` → `'true'`, also arms the autofix-PR step) | deferred by user decision 2026-08-29 |

Non-goals (unchanged): hosting or committing third-party content; scraping
Google Drive/MEGA confirm flows (the user supplies those files); fabricating
missing assets; adjudicating patch licenses. Edge cases the pipeline must
keep handling, all covered by `mep_lint.py`: nested zip-in-zip, whole-repo
archive wrapper, bare root, named subfolder ≠ ROM, several `hires.txt` after
acceptance (fail closed, list candidates), Google Drive large-file
interstitial (out of automatic scope).

#### Phase 5 — bootstrap

**Shipped** — F5.1–F5.5, 2026-08-25 → 2026-08-29; record in §3. Success
criterion unchanged: *playing for 5 minutes generates, next to the ROM, an
enhanced game (image level 2, sound level 2/3) with no configuration; from
it an artist reaches a publishable pack in < 1 h editing only PNG/OGG*.
Phase 9's validation protocol is where that criterion is now measured.

Pending: the audible end-to-end of Blocks B–D (real game, real ears —
subjective, stays manual); the bundled SoundFont waits on the installer's
first release.

#### Repo hygiene and tests

**Shipped or closed** — H1–H10; record in §3. Open: ADR-0162 (accuracy
suite) is accepted and not in CI by decision; the `CheatTypeDetector`
GB/SMS product decision (H7) stays deferred.

#### Documentation and normative integrity

**Shipped** — D1–D13, audit of 2026-09-01; record in §3. Open: ADR-0120 §3
(optional ROM-name parameter in `MepZipValidator`), deferred with a dated
note in the ADR — pick it up with a per-ROM install caller.

#### Host input tester (host UX, not a pack feature)

**Shipped** — I.0–I.3, 2026-08-29; record in §3. Pending, hardware-gated:
the physical-pad pass (live highlight, ring, rumble), MBC7/GBA tilt UI,
Linux `UpdateDevices()`, macOS pads without `extendedGamepad`. Out: preset
redesign, HUD overlay, special devices (Zapper, Power Pad, Phaser),
automatic remapping, browser Gamepad API, stats collection.

#### Deferred / optional

- OGG replacement audio on GB/SMS (`hires-gbsms-v1-draft`) — frozen until a
  second implementer exists (ADR-0041).
- ML-model upscale as an alternative to xBRZ in `scripts/` — later.
- Automatic IPS relocation across ROM revisions — no.
- Offline AI tools (ESRGAN batch upscale, LLM-assisted preset tuning) —
  optional external tools on top of the bootstrap, never in the emulator.
  The LLM-assisted *skin* tool is under feasibility spikes in Phase 10 and
  returns here if they fail.
- Pack browser UI beyond auto-install (search, ranking by GitHub signals,
  user-configurable extra MEI URLs with explicit confirmation, MEI §3.4) —
  after Phase 6, if the catalog grows past what a list can show. The
  player-shell picker (one ROM, 2+ `pack_id`s) is **not** that browser;
  it lives in Part B §5.

#### Phase 7 — Player shell (minimal GUI)

**Delivered** — P.0–P.7, 2026-08-28 → 2026-09-01, and P.1-local on 2026-09-17
(ADR-0206); no slice of Part B remains open. Record in §3, normative
text and slice list in Part B (do not duplicate that prose here). Pack
identity is the pair `pack_id` (product) + `content_id` (revision); the
catalog keeps one live slot per `pack_id`. The letterbox fit, once the last
manual item, was closed 2026-09-05 (`0f8535c4`); the only manual residue is
the native file picker (F6.5).

#### Phase 8 — Enhancement pack border layer

**Shipped** — F8.1–F8.3, ADR-0149, 2026-09-02; record in §3. Optional and
unscheduled **F8.4**: apply `scale_mode`, honor the console aspect in the
default viewport, letterbox inside the viewport, lint the bare root
`border.png`. Core/pack-format work, so it stays in Part A.

#### Phase 9 — Artist-legible texture sheets (bootstrap output redesign)

**Status.** Implementation through F9.29 is recorded in §3, with F9.21
withdrawn. Open work: F9.18 (independent human panel). F9.18-V landed
2026-09-15 on both golden games — structural and paint-application gates pass
with pixel-exact runtime evidence, and the two runtime checks of its first
re-run were closed the same day (§3) — and F9.25's bounded second-pass matrix
was recorded the same day (§3). C.5 was
an independent agent experiment; it neither satisfies the human panel nor
proves the promise of a publishable pack in under one hour.

**Original problem (2026-09-04 baseline).** The bootstrap `auto/` pack emitted `Chr_N.png` sheets in CHR
order: thousands of 8×8 fragments with no neighbourhood — half a logo,
one corner of a rock, a run of font glyphs. An artist opening `mep/` of a
hand-made pack (Zelda 1 reference) sees whole bushes, trees, a stitched
overworld; opening `auto/` sees noise. F5.4e was meant to bridge this
(objects from spatial co-occurrence) but its global union-find over
"≥2 sightings" edges collapses any contiguous scene into one component,
so `textures/sheets/object*.png` was **never** emitted on real games.
Spike 2026-09-04 (`scripts/spike_tile_sheets.py`, env-gated grid dump in
`HdPackBuilder::OnFrameEnd`, evidence under `runs/spike-sheets/`, not
versioned): Zelda 1 — 59/59 shapes in one component (F5.4e), versus 62
aligned 16×16 metatiles (bush, tree, rock, sand, forest edge) and 5
screens stitched into one map with the metatile/PMI approach; Excitebike —
132/132 in one component, versus a 23 712 px continuous strip with ramps.

**Goal.** The bootstrap writes, next to the ROM, sheets an artist can read
cold and paint over: a **metatile vocabulary** (one cell per in-game
building block, aligned to the game's grid), **stitched maps** (screens
assembled as the player sees them) and **object sheets** (multi-block
figures that always appear together), each round-tripping through
`mep_build.py` back into `hires.txt`. Success criterion is Phase 5's
("publishable pack in < 1 h editing only PNG/OGG") made concrete by the
validation protocol below.

**Principles.**
- Presentation is for humans: transparent background, 1-cell gutters,
  labels in a sidecar JSON (never baked into the PNG), sheets split by
  context (HUD / font / scene) so a rupee counter never sits between two
  trees.
- Grouping is by **mutual predictability**, not raw counts: A and B join
  when P(B east of A) and P(A west of B) both clear a threshold with a
  minimum count — sand next to everything is not an object; a 2×2 boss
  door is.
- The unit is the game's grid: 16×16 aligned to the attribute grid when
  the game uses one, 8×8 only when the recording is too thin in aligned
  placements to justify 16. Detection is automatic and reported; the artist
  never picks. *Measured 2026-09-05: both golden games select unit 16 — the
  spike's "Excitebike falls back to 8×8" claim did not survive the amended
  criterion; what separates them is `hasGrid` (Zelda 0.34, Excitebike 0.03).*
- Nothing inferred can break rendering (F5.4e rule kept): sheets add art
  for tiles already keyed by `hires.txt`; wrong grouping only costs
  legibility, never a missing tile.
- Vanilla-looking output stays in `auto/`; community art is never masked
  (ADR-0049/0050/0147).

**Non-goals.** A tile-map editor; a game-specific level format; changing
`hires.txt` semantics (MEP textures stay an envelope over HD Pack per
ADR-0005); AI generation inside the emulator (stays an external script —
F9.6 and Phase 10).

| Slice | Deliverable | Decision |
|---|---|---|
| F9.18 | Human acceptance of the composition editor over ADR-0170/0171 pose data, with ADR-0165/0166 background sources and exports. | Engine/GUI implemented; waiting for F9.18-V, then a person who did not build the feature. Log tests 1–7 where applicable on the golden set; missing evidence is not a pass. Include native window interaction. Test 2 also records ADR-0194's kit-selection observation (below). |

**F9.25 scope and stop rule.** Inventory the nine existing Contra states:
`stage1-run`, `stage1-water`, `stage1-2p`, `stage1-boss`, `stage2-base`,
`stage3-waterfall`, `stage3-boss`, `stage4-base`, `stage4-boss`. Use
`scripts/stages/contra/` and its `navigation.json`; stages 5–8 are not new
save-state objectives (ADR-0182). Navigation entries already in that profile
may be measured without extending the stage-playing objective.

For each state, log the clean control and either a second pass or a justified
not-applicable result (the clean pass reached its route boundary, or no
admissible cheat is known). For each second pass record ROM/state/script and
binary hashes, duration, exact cheat, mode, output hashes, gained/lost keys,
map extent and contaminated surfaces. Coverage cheats donate only scenery/map
and pattern pages; navigation may donate all four surfaces only with the
ADR-0184 clean-control evidence. Reuse existing evidence only when these inputs
are identifiable. Missing archived states are blocked rows, never successes.
Stop when every row has evidence or an explicit not-applicable reason and the
union rebuild passes structural validation. A zero-gain run is a valid measured
result; do not keep searching for a higher percentage without a new scoped task.

**Recorded 2026-09-15** ([log](../validation/f925-contra-matrix-2026-09-15.md)):
the inventory, the four measured second passes, the five named reasons, the
panorama extents per session and the union validation. Two results there are
worth reading before re-deriving anything: at 300 s of effective input the
coverage cheat buys no map extent (all three stage-1 variants stitch the
identical 2512×240), and the navigation passes are one screen deep because the
sweep's body script is the stage-1 route. One question the run raised and did
not settle is ADR-0194 (accepted 2026-09-22): a kit's cross-recording union is the
pattern pages, so this row's figures and scenery were verified nine times per
recording rather than merged once.

**Validation — qualitative and intuitive.** The deliverable is legibility,
which no pixel metric captures, so each slice is judged by a fixed panel
of tasks run by a person who did **not** build the feature (the artist
persona — a developer may stand in but must not have seen the sheets
before). Golden games: Zelda 1 (16×16 grid, screen scrolling), Excitebike
(no grid, continuous scrolling), Mega Man 3 (CHR ROM), Contra (CHR RAM);
GB/SMS follow once NES passes. Each run records the `auto/` folder, the
answers and elapsed times in a text summary under `docs/validation/`, with
local artifact hashes and one delivery-record line after acceptance passes.

1. **Cold-read test** (F9.1, F9.3, F9.5). Open `sheets/*.png` for the first
   time, 60 s per sheet, name aloud what each cell is. Pass: ≥ 80 % of
   scene metatiles / objects named correctly ("bush", "tree", "Link"); HUD
   and font sheets recognised as such at a glance; no cell described as
   "half of something".
2. **Side-by-side with the artist pack** (F9.1–F9.3). A golden game's
   `auto/` sheets next to a community `mep/` pack's: every subject the
   artist drew as one figure is **addressable as one unit** in `auto/`.
   Freeze the reference subjects and recorded route before evaluating.
   Pass: every reference subject observed on that route is reachable as a unit;
   list failures and separately list subjects the recording never reached.
   If no compatible reference is available, mark this test not evaluated.

   **The unit is the pose, not the sheet cell** (amended 2026-09-12, on
   ADR-0171, which made the pose the unit of the sprite layer and the
   `sprNNN` figure the fallback). The criterion as first written judged
   sheet cells, and by 2026-09-12 it had become impossible to pass by
   construction: the grouper deliberately cuts a shared sub-figure out to
   its own sheet — a pair of legs worn by two torsos is stored once — so a
   whole character is *always* split across `sprNNN` cells, on every pack,
   for a reason the architecture is not going to give up. A sprite subject
   passes only when its pose is present, selectable, exportable and paintable
   as a complete unit. Record a count and named exceptions for each step;
   a `poses[]` link alone does not prove selection. The historical 62/223
   addressable poses versus 25/25 recorded main-character poses measured
   different populations and cannot substitute for this check. A background subject still
   passes on the cell/object surface, where nothing forces a split, and a
   screen-resident cell passes on its `backgrounds/screenNNN.png`
   (ADR-0156, ADR-0166) — a captured screen **is** a painting surface, and
   the 2026-09-12 audit failed to count it as one.

   Zelda 1 was the nominated game and is not usable: its artist pack is
   distributed only via Google Drive, which this project does not fetch
   (Phase 6 non-goals). Contra is the substitute — the artist pack is on
   an allow-listed host.

   **One observation this test also records** (ADR-0194, accepted 2026-09-22; this is its reopen trigger): kit
   selection across recordings. When a subject the reference pack shows exists
   only in a second recording of the same stage — the panorama a coverage pass
   extends, a pose only the boss state holds — note whether the artist found
   it, how long it took, and whether they could say which recording feeds which
   surface without reading a manifest. This is an observation, not a pass
   condition: it is the evidence ADR-0194 names as the trigger for reopening
   the cross-recording merge it rejected.
3. **Find-and-edit test** (F9.4). Task card: "make every bush purple",
   "put a face on the rock", "draw a road marking on the ramp". From
   opening the folder to seeing the change in the emulator: pass when
   < 10 min with no editor other than an image editor and
   `mep_build.py`, and the person never had to open `hires.txt`.
4. **Seam test** (F9.2, F9.4). Paint a continuous diagonal stripe and a
   checkerboard across `map-NNN.png`, rebuild, play the stitched region.
   Pass: the stripe is continuous across every metatile boundary and
   every screen transition; no doubled or missing column at the seams
   (headless screenshots along the route, eyeballed, plus a diff against
   the untouched-sheet run to prove only the paint changed).
5. **Map recognizability** (F9.2). Show `map-NNN.png` alone. Pass: the
   person points to where the game starts and traces the route they
   would take; for Excitebike, identifies the ramps and the finish line.
6. **Three independent correctness gates** (all).
   - **Structural:** `mep_lint.py`, kit `--verify`, and `check-coverage`
     compare supported sheet-derived keys against a copy of the post-build,
     pre-paint manifest. Report the key counts and excluded CHR/background
     coverage; this does not prove visual correctness.
   - **Untouched identity:** replay the same ROM/state/input/frame sequence
     with the original pack and an unpainted rebuild, on the same binary and
     rendering settings. Compare every sampled game frame pixel-exactly;
     record frame numbers and hashes. A missing baseline is not a pass.
   - **Paint application:** apply a known asymmetric edit, rebuild and replay.
     Check every tile of the selected figure, including shared-key ownership,
     mirrored orientations and a nonmatching condition's fallback. Compare
     expected edited regions and require pixels outside the declared affected
     key instances to remain unchanged. No source or `hires.txt` diagnosis is
     allowed in the successful user path; a workaround is a failed trial.
   Existing key checks cover only the first gate. Runtime evidence for the
   other two was required by F9.18-V and is recorded in §3; this PRD does not
   claim that an automated implementation of the complete protocol already
   exists.

7. **Noise budget** (F9.1, F9.3). Count cells with count = 1 or flagged
   "unaligned"; pass when they sit in a separate `misc` sheet and make
   up < 15 % of scene cells (Zelda spike: count-1 cells were GAME OVER
   text — correct to isolate, wrong to interleave).
8. **Optional classical A/B** (F9.6, ADR-0192). Compare `classical` with
   `passthrough` on five identical source screens at the same display size,
   with labels hidden from three reviewers. Record each vote; a screen is
   preferred/tied when at least two reviewers rate it that way. Positive
   result: classical preferred/tied on at least two screens, no seam failure
   and no alpha loss. A negative result is reported, not a release blocker.
   This does not evaluate AI generation. Reopening diffusion requires the
   measured run and new/amended ADR specified by ADR-0192.

**Evidence and completion.** Structural suites, independent agent workflow
runs and human usability panels are three different evidence classes. Every
result names its class, binary commit/hash, input hashes, commands, sampled
frames, duration, exceptions and pass/fail/not-evaluated verdict per criterion.
Text summaries belong in `docs/validation/`; ROM-derived assets stay local in
`runs/`, with hashes in the summary. Tests 3/4/6 have automatable portions;
a green suite is not a claim that the cold-read ran. C.5 remains a completed
proxy experiment of the one-hour record→kit→paint path. **Phase 12 cold-reads
are a fresh Opus session** (ADR-0214, amended 2026-09-19), scored on the
briefing, with criterion 4 (`hires.txt` never opened) as a gate that C.5 lacked. F9.18 still
requires a human who did not build the feature; ADR-0214 does not amend it.
A real external-user repeat of C.5 remains out of scope. The under-one-hour
publishable-pack promise remains unvalidated until the complete documented
path, including packaging/lint and on-screen paint verification, succeeds
without undocumented repairs.

#### Phase 10 — LLM-assisted skin studio (feasibility spikes first)

**Status:** drafted 2026-09-09 as a nine-slice product plan; **rewritten
the same day after review** into the feasibility spikes below. **No product
slice exists**; only the spikes ran. Only one thing in this section is a decision:
the entry point, i.e. how the player chooses a subject, which ADR-0227 fixed on
2026-09-23 (named from the artist kit). No module layout, sidecar format, tool
contract, storage location or provider is fixed here. **S10.c/S10.d shipped 2026-09-09; S10.a ran the same day and
failed** — its premise ("every pose the recorder saw") was not reachable from
the sidecars available then. The user took that decision on 2026-09-11: **ADR-0170 is
accepted and shipped as F9.19** (§3) — the recorder now writes pose
membership — and **S10.a was re-measured the same day and passes at 100 %**
(25 of 25 of a character's poses, against >= 80 %; §3). ADR-0170 was
accepted on its Phase 9 value rather than as a commitment to this phase, and
one link is still unmeasured: **S10.b**, the layout fidelity of a hosted
image model, which needs the user's key and hand. It does not depend on
poses — Contra80s' `BillRizer.png` is already a contact sheet of one
character's poses, and it is public third-party art, so running the spike on
it sends no ROM-derived art anywhere and leaves ADR-0154 §2 untouched. Every
decision the spikes feed (module layout, sidecar, provider, egress) is an
ADR, written by hand after the spike that tests its premise (`docs/roadmap/AGENTS.md`: decisions are not made in a
PRD). The first draft had it backwards — it specified the architecture and
reserved "ADR-A/B/C" to ratify it; that draft is in git history, not here.

**Problem.** A hand-made restyle is an artist's year. The reference pack,
Contra80s (`tastichacks/contra80s`, `docs/community-packs.json`), is 233
files and a 21 179-line `hires.txt`: 11 854 `<tile>` entries, 864
`<condition>` lines, one PNG per subject — `BillRizer.png` is a 512×432
sheet at `<scale>2` holding every pose of one character. Phase 9 made the
machine's output legible (metatile vocabulary, `sprites.png`, `sprNNN`
figures, stitched maps); F9.18 lets a human *compose* a scene; F9.6
repaints a sheet at a higher resolution but keeps the drawing. None of them
lets a player who cannot draw say "make Bill look like a chrome knight,
keep the gun" and play the result.

**Idea under test.** The player names a subject from the artist kit
(ADR-0227), describes a restyle, and an external tool driven by a hosted
image model under the player's own key produces a candidate skin for the
**whole subject** (every pose the player named, ADR-0227 §3) that the unchanged
`mep_build.py` slices into a pack. Whether any link of that chain holds is
what the spikes measure.

*Entry point amended 2026-09-23 by ADR-0227 (user's decision, verbatim:
"Pelo nome, no kit"); it used to start from the live viewer.* The live
viewer is a developer tool since ADR-0169 §4's 2026-09-23 amendment, so the
player does not point at the subject there: they **name** it from the artist kit, and the subject is the set of
kit ids they name: pose ids from a grid's `kit.json` record, with a `usrNNN`
grid as a shorthand for its poses. Whole-grid selection waits until the kit
records the grid's kind (cycle, sequence or rest; ADR-0227 §2). The kit has no notion of a character — one grid per
cycle, and "rest" grids binned by box size — so "the whole subject" above
means every pose the player named; the toolchain infers no membership, and
automatic grouping is left open (ADR-0227 §4). The
kit's figures are pixel-faithful since F12.18 (ADR-0225) and its cycle rows
survive sprite flicker since F12.19 (ADR-0226). S10.b is unaffected: it
measures layout fidelity on a contact sheet and does not depend on how the
subject is chosen.

**Constraints that hold regardless of outcome.**
- Part A §1 principle 5 as written: no model call, key or prompt in
  `Core/`, `UI/` or the installer. If a studio exists it is an external
  script in `scripts/`, like the viewer and the composition editor
  (ADR-0165, ADR-0169).
- ADR-0192 supersedes ADR-0154 §2 Option A: generative repaint is not a
  project commitment until a measured run reopens it. ADR-0154 §4 and the
  remaining contract still stand: no tool in this repo sends ROM-derived art
  off the machine (`sheet_repaint.py` refuses a non-loopback endpoint), and
  generated output lands in `auto/`, never `mep/`. Sending crops to a hosted
  model remains a decision that needs spike results and an ADR — not a premise
  of this phase.
- The model never writes the format. Deterministic code validates every
  byte that reaches a pack; Phase 9's rule — nothing generated can break
  rendering — is kept verbatim.
- Provenance is disclosure, not a gate (ADR-0154 §3, MEP v1.6 `generated`).
- GB/SMS only after NES passes.

**Remaining feasibility questions.** Pose membership exists (S10.a), but
complete subject selection/export/painting must be measured separately under
Phase 9. S10.c preserves `generated`; studio data stays outside the pack tree.
S10.d provides structural sheet-key coverage, not visual acceptance. Hosted
layout fidelity is S10.b. A reverse channel is still a new decision under
ADR-0169, justified only if headless preview is insufficient.

Provider/model identifiers, prices, authentication and data handling must be
verified against the provider's current official documentation immediately
before S10.b and recorded with the experiment. They are not durable PRD
requirements.


| Spike | Question | Pass / fail | Feeds |
|---|---|---|---|
| S10.b | **Does a hosted image model preserve a contact sheet?** One subject sheet on a chroma backdrop, 1K and 2K, three prompts; measure per-cell displacement, gutter ink, whether alpha comes back, silhouette growth, cost, latency. **Run by hand, by the user, from their own account**, with the files to be sent listed before sending; nothing in the repo automates it | cells within ±1 px at 1x and gutters clean on ≥ 2 of 3 runs — else per-cell or per-row generation is the only path and the cost model changes | the BYOK/egress ADR (amends ADR-0154 §2/§4, or declines to) |

**After the spikes.** S10.a is complete. If S10.b passes, record Phase 9
selection/export/paint evidence before promising a whole-subject studio, then write the ADRs — one
decision each, by hand, via `/adr`: (i) whether and how a player's own key
may send ROM-derived crops to a hosted model (amending ADR-0154 §2/§4, or
not); (ii) where studio data lives (outside `mep/`) — how a subject is
chosen is already ADR-0227, and only automatic grouping (ADR-0227 §4) would
be a new subject-model decision;
(iii) the tool contract and the validator as the gate; (iv) a reverse
channel amending ADR-0169, only if a live preview is worth more than
headless screenshots. Then slice the product work, one slice per task. If
either spike fails, the phase closes with the measured reason in §3 and the
LLM-assisted skin tool returns to "Deferred / optional".

**Risks (this phase).**

| Risk | Mitigation |
|---|---|
| ROM-derived art leaves the machine in S10.b | run by hand by the user from their own account; the sent files are listed first; nothing in the repo automates a hosted call until an ADR allows it; `sheet_repaint.py` stays loopback-only |
| Spike results read as a plan | this section names no modules, formats or slices beyond the four spikes; the ADRs come after the numbers |
| Model ids and pricing churn | recorded above as configuration with a read date; re-read before S10.b |
| Safety filter refuses franchise art | prompts describe shape and style, never franchise names; a refusal is a measured outcome, not retried automatically |

#### Prior art from the fork network (survey 2026-09-05)

**Method and headline.** All 70 forks of `nesdev-org/MesenCE` were compared
against upstream `master`; every branch that was genuinely ahead had its
changed-file list read. Twenty-one are empty mirrors, and a large share of the
remaining "ahead" branches are mirrors of upstream's own `Sour*` topic
branches rather than fork work. The load-bearing finding is a negative one:
**nobody in the fork network works on HD packs, MEP, or audio replacement** —
every `Core/NES/HdPacks/` hit traces back to a mirrored upstream branch. That
ground is ours alone, and this table exists so the survey is not repeated.

What the network *does* have is emulator automation: six people independently
built MCP / REST / JSON-RPC control surfaces over Mesen. That is our headless
harness problem, solved several ways, in readable code.

| Source | What it is | Value, and why | Where it lands |
|---|---|---|---|
| `zerkz/MesenCE` · `master` · `Core/Shared/InputOverrideProvider.{h,cpp}` (108 lines) | An `IInputProvider` that resolves buttons by name (`GetKeyNameAssociations()`), expires each override after N frames (`EndFrame = GetFrameCount() + durationFrames`), overlays instead of replacing physical input (`SetInput` returns `false`), walks `IControllerHub` sub-ports, and re-registers on `ConsoleNotificationType::GameLoaded` | **High.** It already implements two of the three properties ADR-0157 decided, in one self-contained file. Its `GameLoaded` re-registration — "a new console (and control manager) is created on every game load" — is the structural explanation of our own documented `input=` no-op trap | **F9.14** (read before designing) |
| `lusid/MesenCE` · `feature/mcp-*` (37 / 32 / 17 ahead, nothing merged upstream) · `Core/Shared/Video/BaseVideoFilter.cpp`, `Core/Shared/Emulator.h`, `UI.Tests/Mcp/` | In-memory frame capture; a packed `atomic<uint64_t>` carrying a **boundary epoch** plus per-owner-thread debug-request accounting (`873730e5`, `03f99a40`), so an external caller can tell "the emulator stopped for *my* request" from "it stopped for someone else's"; and ~7k lines of xUnit against a fake core | **High.** The capture and the test model are direct slices below. The epoch scheme addresses an ambiguity our harness has but has never named — worth reading before we extend stop/resume handling | **F9.15**, **H9**; epoch scheme = reference |
| `ky12138/MesenCE` · `master` (36 ahead) · `Core/Debugger/MappingTracker.{cpp,h}` (`fdc6b156`), `AddressPage.h` | Tracks NES PRG/CHR bank mapping **over time**, with cache persistence | **Medium, reference only.** ADR-0153 / F9.7 already names "CHR bank swaps, CHR-RAM re-uploads" as the source of the duplicate vocabulary entries the alias pass collapses *by pixels*. Bank-aware identity would attack that at the source. No slice opened: we do not yet know whether the ink-share alias budget leaves residual error this would fix | F9.7 follow-up (no slice) |
| `ky12138/MesenCE` · `adc1a6a2`, `42e0ee27` | `NES_ONLY` / `LessUI` compile-time build modes | **None, on measurement.** Both commits are C#-only — they exclude `UI/` files from the csproj and never touch `Core/`, so no core was ever compiled out. Against this tree the removable surface is 4 Netplay window files; the rest is product (GB/GBA/SMS UI, the HD Pack builder, the recorder) or already deleted by the console reduction | **H8** — declined and closed, **ADR-0158** (accepted 2026-09-05) |
| `mmg-media/MesenCE-debug` · `master` (43 ahead) · `Core/SNES/Debugger/SnesDebugLog.{h,cpp}`, `c8cc701a`, `76af74d5` | An always-on ring buffer of ROM reads plus DMA/transfer capture, then a reverse search for which ROM addresses produced the tiles/palette currently on screen | **Medium, reference only.** Tile provenance is adjacent to ADR-0043 (static ROM tile export) and to metatile identity, but this is SNES-implemented: a technique to port, not code to lift. The fork also deletes all CI workflows and mixes in a trainer — quarry, not a branch to merge | Reference |
| `Hoshiruna/MesenGM` · `develop` · `MCPServer/`, `Core/Shared/Video/TrueTypeFont.{cpp,h}` | An *out-of-process* MCP server (the alternative shape to lusid's in-process one), and TTF/embedded-bitmap fonts wired into `DebugHud` | **Low.** Recorded for the architectural contrast; the TTF work only matters if we ever want legible labels burned into captures or sheets | Reference |
| `michaelcmartin/MesenCE` · `tms-magshift` · `2cc3ea1b` | One line in `SmsVdp::ShiftSpriteSg` clipping magnified sprites under Early Clock | **Low.** Verified absent from our tree, but `ShiftSpriteSg` is the SG-1000 / ColecoVision TMS9918 path, not the SMS path our product consoles use | Not planned |
| `Schaltfehler/MesenCE` · `feature/lua-debugger-surfaces` (16 ahead) | 1382 lines exposing access counters, CDL, trace, callstack and profiler to Lua | **Low.** A scripting alternative to a socket/automation surface; Lua-in-emulator is a worse fit for our harness than an in-process provider | Not planned |
| `NovaSquirrel/Mesen2` · `gb-link-cable`; `eclectic-sh/MesenCE` · `SourTestUpdate`; `HeeminTV` · `mmc5_pcm_irq` | GB dual-console plumbing; `RecordedRomTest` MD5→SHA1 / `MRT`→`MT2`; MMC5 PCM IRQ | **None — already ours.** Each verified present in our tree (e.g. `MT2` and `SHA1::GetHash` in `Core/Shared/RecordedRomTest.cpp`). Listed so they are not re-reported as new | Already merged upstream |
| 21 empty mirrors; ~6 mutually redundant localisation forks; WonderSwan / GBA / SNES / Mega Drive / packaging forks | — | **None.** Off-product consoles or no content | Not planned |


#### Phase 11 — Consolidation

**Status:** C.1–C.8 completed 2026-09-14/15; results in §3. C.5 completion
means the independent-agent experiment ran and its defects were recorded,
not that human usability or the full painting promise passed.

The two preserved C.5 logs are the experiment's evidence. Both skipped reference
coverage measurement; both found a visible edit within one hour, but the
painting workflow required manifest diagnosis and exposed incomplete output.
Their original verdicts remain historical observations. F9.18-V owned current-
binary verification of those findings and closed 2026-09-15 (§3); F9.18 owns
human acceptance.
No completed C.* execution plan remains here; its briefing and history are
available in git and the logs.

#### Phase 12 — Paint loop and hand-authored conditions

**Status:** opened 2026-09-16 from `docs/hd-pack-toolchain-comparison.md`
("Gaps this table names"). Delivered (§3): F12.1, F12.3–F12.10 and
F12.12–F12.17 (2026-09-17 to 2026-09-23), plus ADR-0209 Q1–Q3; F12.2's
evaluator row closed 2026-09-19. F12.18 and F12.19 are merged (#395, #394)
with their owed rows in flight. **F12.11 is the one open row:** ADR-0220 was
accepted and its code landed on 2026-09-22; stop condition (3) (the paint
round trip through F12.3) moves into Phase 14's F14.1, and (2) (GIMP and
Krita, logged by a person) into F14.8. F12.1's scale reference moved F12.3's
premise — the load an artist waits for is a 13–16 s decode, not the 0.4 s
parse — and F12.3 answered it with ADR-0212's per-image, in-place reload: a
repainted sheet is back in the running game in 2 ms, without reopening the ROM.

**Why this phase.** The comparison table names seven rows where the
inherited upstream toolchain still serves an author better than the layer
built here. Read as a scoreboard it points at the wrong target: the Core,
the format and the builder are upstream's, and the competitor the artist
evidence measured is a spreadsheet, not another emulator
(`docs/validation/metroid-artist-workflow-evidence.md` §3). This phase takes
the rows that map onto two of the three criteria of the project's goal —
**faster on day one** and **discovery** — and originally left the third,
**recording coverage**, where it already lives (ADR-0182/0184/0185, F9.25).
The day-one block added on 2026-09-19 changes that in one respect only:
F12.10 turns the shipped drivers into an unattended job, so coverage becomes a
property of the pipeline rather than of the artist's session. It does not
claim recording is solved: Contra's clean routes cover 64.6 % and the F9.25
matrix records that more input buys no map extent at 300 s; F12.10 keeps that
budget.

**Goal.** An artist opens the kit in the paint program they already use,
paints on layers, saves, and sees the change in the running game without
reopening the ROM; picks a single tile's key from the emulator's own viewers
as a sheet cell; expands a pose beyond its hardware box from the composition
editor; writes a condition by hand and learns from lint where the recorded
routes agree with it; and brings an existing plain pack into the same
toolchain. Success is measured per row of the comparison table, re-measured
in `docs/validation/` when a slice closes.

**Principles.**
- Measure before optimizing: the pack Metroid (USA) installed on this
  machine is the scale reference — 67 images, 150 199 tile rules
  (`Tiles.size()`), 8 401 keys as distinct `(tileData, palette)` — and no Core
  or generator optimization lands before its number is recorded. F12.1's log
  carries every definition beside its value
  ([2026-09-17](../validation/f12.1-scale-and-load-2026-09-17.md)); quote the
  definition with the number, they are not interchangeable.
- Nothing here emits a key the recording did not observe (ADR-0183 §3),
  with two confined exceptions: the `<addition>` target key, synthetic and
  marked by ADR-0196 §3; and the **static fill** — a key whose shape comes
  from the ROM's own CHR or from a third-party index read as facts
  (ADR-0210), always `seen: false` with its provenance recorded per cell and
  never the source of a pose, a scenery group or a map. The recorder already
  does the CHR ROM half of this: `HdPackBuilder::AddRomTiles` emits every
  CHR ROM tile with `Y`, which is why ADR-0210 counts 88 576 such rules in
  the library. F12.9 and F12.12 extend that exception, they do not open a
  new one.
- The toolchain stays external and stdlib (ADR-0165): no `psd-tools`, no
  C# rewrite of the generators. The paint program exports PNGs; we name
  them and reload them.
- The sheets stay the source of truth; a `.psd`, `.aseprite` or `.kra` is
  the artist's input, never the pack's.
- A slice that changes what the artist sees is not shipped until a **fresh
  Opus session** (ADR-0214) logs the cold-read rows from the slice's
  briefing, not the dispatcher script; the F9.18 panel is a separate
  decision and is not covered by this phase. Pointer-level discoverability
  stays not evaluated until a pointer harness exists.

**Non-goals.** Runtime dual-namespace lookup in the Core; relaxing IPS
matching (ADR-0145); automatic emission of `frameRange`,
`tileAtPosition` or `memoryCheckConstant` (ADR-0189 §4); any tool that picks
a memory address for the author; automatic anti-flicker via `<addition>`;
tile normalization by similarity; embedding the Python toolchain in the UI.

| Slice | Deliverable | Decision |
|---|---|---|
| F12.2 | **Copy as MEP sheet cell.** The Tile/Tilemap/Sprite viewers' right-click menu gains *Copy as MEP sheet cell*, emitting the `(tileData, palette)` key in the exact form `mep_build.py` reads from a sheet sidecar, beside the inherited *Copy tile (HD pack format)*. | No prerequisite; UI only, no Core change. Bounded input: Zelda 1 and Contra paused in the viewers. Stop when the pasted text round-trips through `mep_build.py build` on both: the pasted key is emitted as a `<tile>` whose `x,y` is the painted cell's crop, and `mep_lint.py` exits 0. (Reworded 2026-09-17 — the rule named `mep_build.py --verify`, which does not exist; `verify` is a subcommand of `mep_import.py` and checks a different subject. A machine-readable `verify-cell` subcommand stays a possible follow-up slice.) **Evaluator (ADR-0214, 2026-09-19):** a fresh Fable session pastes one cell and paints it without reading `hires.txt`, from `docs/validation/f12.2-fable-evaluator-briefing.md`. The dispatcher script is `docs/validation/f12.2-copy-sheet-cell-panel-script.md` (S1 re-records both packs — the installed `auto/` recordings predate ADR-0178 and `build` refuses them); handing it to Fable invalidates the run. Re-measures "Picking a tile's key by hand". A **mechanical** replay of setup S1–S4 and of P9–P14 exists (`scripts/replay_f122_panel.py` plus `UI.HeadlessTests/CopyAsMepSheetCellTests.cs`) and is green on both games — log: `docs/validation/f12.2-mechanical-replay-2026-09-19.md`. The remaining row is the Fable dispatch, not a calendar wait for a person. P15 and pointer-level discoverability are observations / not evaluated. **Closed 2026-09-19:** two fresh Fable sessions, one game each, both PASS — 0 hard stops, neither opened a `hires.txt`, magenta on screen in ~2 min (Zelda 1) and ~4 min (Contra). Criteria 3/5/6 re-derived mechanically from the packs they left behind, 0 magenta on an unpainted baseline at the same frame. Log: `docs/validation/f12.2-fable-panel-2026-09-19.md`. The same log is the argument for the protocol: round 1 stopped **both** sessions on the same two dispatcher defects (a copy table keyed in pixels while the tilemap is labelled in tiles, and a screenshot path that did not match the tool), neither reachable by any green suite. |
| F12.18 | **A pose keeps its pixel offsets (ADR-0225).** The recorder writes per-tile `px`/`py` (native pixels from the pose origin; `z` only when tiles overlap) beside the unchanged `dx`/`dy`, `unit: 8` and `size`; readers fall back to `px = dx * unit` on an older sidecar. Sheets keep 8 px cells and ADR-0153's gutter; the **composed** views — `scripts/mep_figure.py export`/`import`, the kit's Figures rows (`scripts/artist_kit.py`), the composition editor's pose rendering — place tiles at pixel precision with no intra-figure gutter, and `import` returns an overlapped pixel to the frontmost cell whose `orig` pixel is opaque there. `PoseTile` gains `Px`/`Py`/rank outside the identity ordering; `mep_lint.py` warns when `dx != ToCells(px)` or `dy != ToCells(py)`, and `core_unit_tests` covers both axes. Bounded input: the Contra stage-1 recording (`scripts/stages/contra/stage1-probe.txt` driver) and the six phases of its run cycle (`docs/validation/contra-pose-offsets-and-flicker-2026-09-23.md` §1: torso +2..+4 px, legs at y 14). Stop when (1) every written tile carries `px`/`py`, `dx == ToCells(px)` holds in a unit test, and a sidecar without them still reads through the fallback; (2) `mep_figure.py export` of each run phase matches the recording's OAM frame pixel for pixel (today: up to 4 px off); (3) the kit's figure row shows the six phases with no intra-figure gutter and the row baseline intact; (4) a painted export round-trips through `import` and `mep_build.py build` with 0 errors and the key set unchanged (ADR-0183 §4), overlap handled as decided; (5) the Contra kit is regenerated from a fresh recording (shared with F12.19). One task. | **ADR-0225 accepted 2026-09-23** (*"px/py por tile"*); go-ahead verbatim *"pode implementar as duas ADRs em paralelo"* (2026-09-23), implemented: stop conditions (1)–(4) **met** — 290/290 tiles carry `px`/`py` with 0 invariant violations on both axes; 10/10 poses of the two player run cycles export pixel-identical to the OAM frame (2–4 px off before); kit rows with no intra-figure gutter on one baseline; a painted overlapping pose round-trips (172 → 172 keys). (5) owed: one Contra re-record with F12.18 and F12.19 on the same binary. 1018/1018 core tests. Log: `docs/validation/f1218-pose-pixel-offsets-2026-09-23.md`. |
| F12.19 | **The pose-track linker tolerates one missing retained frame (ADR-0226).** `LinkPoseTracks` keeps a partnerless cluster pending for one frame and links it in a second nearest-first pass after the adjacent-frame pass, within the unchanged 16 px; `kPoseTrackMaxGap = 1`, the skipped frame's `RepeatCount` (<= `kPoseTrackGapMaxRepeats = 2`) is added to the run it interrupts so `hold` keeps the game's cadence; a two-frame gap still breaks the track. Nothing after the linker changes. The slice re-records Contra stage 1 with the new binary and regenerates the kit (the same recording serves F12.18). Bounded input: the Contra `full` stream of 2026-09-23 — 3 667 retained frames, 69 tracks of which 61 are single-frame tracks between retained frames 383 and 507 (respawn flicker), 3 period-6 cycles with `repeats` 26/24/3 (`docs/validation/contra-pose-offsets-and-flicker-2026-09-23.md` §2–§3). Stop when (1) the ADR-0226 §4 unit tests pass (one track across alternate frames, two tracks across a two-frame gap, `RepeatCount` 3 not bridged, cadence kept, no track swap across the gap); (2) on the re-recorded stream the flicker stretch yields 0 single-frame tracks (61 today) and the player's cycle `repeats` count rises by the turns run under invincibility; (3) the regenerated `poses.json` writes no `sequences[]` entry whose holds reproduce the driver's period (`[…, 30, …]` from `104f R / 30f - / 104f L`) — the old kit's `seq000`; (4) the Contra kit's figure rows are period-6 cycles and the regenerated surface gets its cold-read row. One task. | **ADR-0226 accepted 2026-09-23** (*"Tolerar 1 frame"*); go-ahead verbatim *"pode implementar as duas ADRs em paralelo"* (2026-09-23), implemented (`LinkPoseTracks`, `kPoseTrackMaxGap = 1`): stop conditions (1)–(3) **met** — 1007/1007 core tests; Contra stage 1 single-frame tracks in the flicker window 61 → 0, cycle `repeats` 26/24/3 → 26/26/3, `sequences[]` empty; (4) half met — the kit's figure rows are the three period-6 cycles, the cold-read row is still owed. Log: `docs/validation/f12.19-flicker-tolerant-tracks-2026-09-23.md`. |

**Added 2026-09-23 (F12.18, F12.19).** Two decisions from the Contra pose
investigation (`docs/validation/contra-pose-offsets-and-flicker-2026-09-23.md`):
ADR-0225 keeps a pose's pixel offsets and ADR-0226 lets the track linker
survive one missing frame. Both are accepted with the go-ahead *"pode
implementar as duas ADRs em paralelo"* (2026-09-23) and dispatched in
parallel, not yet on `main`; F12.17 (the patched-ROM import) shipped separately. Order: F12.19 first (it changes what
the recorder links, and its re-record is the input F12.18 measures on), then
F12.18; one Contra re-record serves both, and the regenerated kit is a
surface change under this phase's cold-read rule.

**Day-one material without a human at the controller (added 2026-09-19).**
The slices above all assume a recorded `auto/` exists. The artist evidence
says the bottleneck is the recording itself
(`docs/validation/metroid-artist-workflow-evidence.md`), and ADR-0210's
measurement splits the bounded library in two: for the **23 CHR ROM games**
every shape is in the file and `defaultTile=Y` already wildcards the
palette, so the *shape* half of the kit needs no play at all; for the **7 CHR
RAM games** the only static source of shape is a third-party key index read
as facts (ADR-0210 §3). What no static source gives, for either kind, is
**organisation** — figures, cycles, named scenery and stage maps come from
OAM co-occurrence, adjacency and scroll that were *observed*. So "no
recording" has two honest readings, and the four slices below take both:
material that exists before any recording (F12.9, F12.12), and a recording
that happens without the artist pressing a button (F12.10). F12.11 then
puts each surface into one layered file the artist's own program opens with
the reference, the guides and the paint layer already stacked.

Constraints carried over: ADR-0209's three ("simple", "feedback in the
game", "return to the same moment"), ADR-0183 §3 (inference is marked, never
confused with evidence), the stdlib-only toolchain (ADR-0165), and "the
sheets stay the source of truth" above — a `.ora` is written by us and read
by the paint program; **nothing in the pack is ever read out of it**.

| Slice | Deliverable | Decision |
|---|---|---|
| F12.11 | **Layered surface for the paint program (OpenRaster).** Beside every surface PNG the kit writes `<name>.ora` — a zip with `stack.xml`, `mergedimage.png`, `Thumbnails/thumbnail.png` and one PNG per layer, written with `zipfile` + `xml.etree` and the PNG writer the generators already have. Layers, bottom to top — **five on a recorded surface, four on an F12.9 static page**: `orig` (the `*.orig.png` twin, `edit-locked`), `context` (the 1x stitched-map crop around a figure at 50 % opacity — only when a recording exists, absent on F12.9 pages), `paint` (fully transparent, the **selected** layer, the only one the artist touches), `guides` (cell grid, pose / cycle captions from `names.json` or the sidecar ids, hatch over `seen: false` cells — drawn in one sentinel colour outside every NES palette, `visibility="hidden"` for export), `palettes` (a swatch strip of the palettes recorded for that sheet, hidden). GIMP, Krita and MyPaint open `.ora` natively; Photoshop and Aseprite do not and stay on F12.4's per-layer asset names — **no `.psd` or `.aseprite` writer**, stated in `docs/remastering-a-game.md`. F12.11 is a second path beside F12.4, not its replacement: the artist evidence measured so far (Metroid, a spreadsheet user) does not show a GIMP/Krita population, so F12.4 stays the default path and this one is measured against it. **The return path does not change:** the artist exports a flat PNG over the F12.4 name; `sheet_repaint` keeps only cells that differ from `orig`, and `mep_lint.py` fails a cell that contains the sentinel colour (the guides layer was left visible) naming the cell. | **ADR-0220 accepted 2026-09-22 (*"aceito o F12.11. nao implemente ainda."*); code landed 2026-09-22 (`scripts/ora_writer.py`, `scripts/mep_sentinel.py`, 16 unit tests; build go-ahead *"dispara as frentes 1, 2, 3 e 4 em paralelo usando workflows"*), stop conditions (1) and (4) met by the automated pass; **(3) — a stroke on `paint`, exported flat, reaching the game through F12.3 — has no recorded run** (no test or log exercises that path yet), and **(2) — GIMP and Krita, logged by a person — is open**, so the row stays live. **2026-09-23 follow-up:** a person opened one four-layer sheet in GIMP 2.10 and Krita 5.3.4 — every layer named, but both readers open with `orig` active and no stack order fixes both, so by the user's option (b) the order stays and `ARTIST.md` / `docs/remastering-a-game.md` say "select `paint` before painting" (ADR-0220 amended); captions are fitted to the canvas and the `palettes` band follows first use; (2) still needs a person's log on the regenerated files including a five-layer surface, and (3) is still unevaluated (`docs/validation/f12.11-stop3-and-gimp-findings-2026-09-23.md`).** The ADR was needed before start because it adds a fifth file kind to ADR-0183 §2's surfaces and fixes the layer contract; it must also state that `.ora` is **write-only** for the toolchain (reading `paint` out of it is stdlib-trivial and is refused on purpose, or the sheet stops being the source of truth). Prerequisite chain, in full: F12.3 (the reload that shows it) → F12.4 (the name the flat export lands on) → F12.11; the SMB bounded input additionally needs F12.9. Bounded input: one Contra figure sheet (recorded, five layers) and one SMB static page from F12.9 (four layers). Stop when (1) `stack.xml` validates against the OpenRaster 0.0.5 schema shape the three programs read and each `.ora` round-trips through `zipfile` unchanged; (2) GIMP and Krita open both files with every layer named (five and four respectively) and `paint` selected — this row is logged by a person, per this phase's cold-read rule; (3) a stroke on `paint`, exported flat, reaches the game pixel-exact via F12.3 with the unchanged cells dropped; (4) the same export with `guides` left visible is refused by lint with the offending cell named. What we measure is ours: file validity, layer order, refusal, pixel-exact result. Re-measures "Painting, end to end" and the **"simple"** constraint: open one file, paint, export, look at the game. |

**Order.** Of this block, F12.9, F12.10 and F12.12 are delivered (§3);
F12.10 shipped first, and F12.9 completed its path (d), so a ROM matching no
route set yields a static kit. F12.11's chain F12.3 → F12.4 → ADR-0220 →
F12.11 is complete up to its two open stop conditions, now carried by F14.1
and F14.8. F12.6b made `memoryCheckConstant` a verdict, and F12.14
(ADR-0222, shipped 2026-09-22) made `spriteNearby` and `memoryCheck`
evaluable. A slice that changes what the artist sees is not shipped until its
cold-read row is logged (principles above).

#### Phase 13 — Shared replays (ADR-0205)

**Status:** ADR-0205 accepted 2026-09-17, nothing implemented. Added to this
roadmap 2026-09-19 — the ADR names the two slices and the PRD had none, which
is the "accepted and invisible" state the Phase 11 C.2 check was built to
refuse. Scope, format and trust model are the ADR's; the rows below only
sequence and bound the work.

| Slice | Deliverable | Decision |
|---|---|---|
| R.1 | **Publish.** The single *Record and share* action (ADR-0205 §2) producing a `.mmo` from power-on with the settings the ADR fixes; a `scripts/` lint that re-checks what the action guarantees (§3); the `[Replay]` Issue Form and title rule (§5); the author attaches the file to the issue in their own browser (§6); the workflow that validates the attachment and labels it. | ADR-0205 §1–§6 decided. Core/UI change for the action; stdlib for the lint; no new host — the attachment lives on the issue (§10). Bounded input: one Contra replay recorded by the action on the dev machine. Stop when the lint accepts the action's own output and refuses a `.mmo` recorded any other way with the §3 reason named, and the issue round-trips through the workflow into the label the ADR names. |
| R.2 | **Consume.** The recordings catalog generated from accepted replay issues, listed in the client by loaded ROM and ranked by 👍 (§7); the structural gate that validates before listing (§8); removal by the author closing the issue, mirrored by `replay:removed` (§9). | ADR-0205 §7–§9 decided. Prerequisite: R.1 (there is nothing to list before something is published). Catalog script stdlib, client overlay in the UI project. Bounded input: the R.1 replay plus one closed issue. Stop when the client lists the open one for the matching ROM, hides it for any other ROM, and drops it within one catalog regeneration after the issue closes. |

#### Phase 14 — Proof at scale

**Status:** opened 2026-09-23 from the Phase 12 review of the same day. The
user's decision on that review, verbatim: *"Sim, como recomendado
(Recommended)"* — the numbering (Phase 13 is replays), the go-ahead for
F14.1–F14.3, ADR-0229 opened `proposed` for F14.4, F14.5 as a measurement
only, and the order in §5 item 6. Nothing in this phase is implemented yet.
Two questions the review raised are already decided on open PR #397 and are
not slices here: lint keeps not weighing `<tile>` conditions when it decides
an `<addition>` anchor is keyed (*"Manter como está"*, recorded as an
ADR-0196 §4 refinement), and Phase 10 names its subject from the kit (*"Pelo
nome, no kit"*, the Phase 10 entry-point ADR on PR #397). The review
numbered those two F14.6 and F14.7, so F14.8 keeps its number.

**Why this phase.** Phase 12 built the artist surfaces one cause at a time,
and each fix was measured against its own cause. Three things were never
measured end to end: a painted cell reaching the running game through a
paint-program export or a figure import, the 28-ROM cold read after every
capture fix (criterion 3 is still 13/28, from
`docs/validation/f12.2-opus-sweep-2026-09-19.md`), and how much of a pack the
organised sheets can reach at all — 19.2 % (Castlevania) and 17.2 % (Zelda)
of pack keys, because `ShapeIdFor` sees only the retained stream while
`ProcessTile` emits a rule for everything the PPU draws (ADR-0209, "What (k)
actually closed"). This phase closes the first two with bounded runs and
measures the third before deciding it.

**Prerequisite for the whole phase.** Issues #399, #400 and #401 — the three
kit defects the 2026-09-23 Contra kit cold read found (painted figures never
routed back into the pack, no phase order on a folded cycle, composites in
the rest grid; open PR #398 carries the log) — merge first. The Contra
re-record and that cold read are done (F12.18 (5) met, F12.19 (4) logged,
verdict "no"), so F14.1 no longer re-records.

| Slice | Deliverable | Decision |
|---|---|---|
| F14.1 | **The painted round trip in the running game.** One painted cell of the regenerated Contra kit reaches the running game pixel-exact through the F12.3 reload, twice: (a) a stroke on the `paint` layer of an F12.11 `.ora`, exported flat over the F12.4 name, and (b) a stroke on a `scripts/mep_figure.py export` figure, returned with `mep_figure.py import`. The paint program itself is not what this row measures — that is F12.11 (2), in F14.8. | Go-ahead verbatim *"Sim, como recomendado (Recommended)"* (2026-09-23); follows ADR-0220 (F12.11 (3)), ADR-0209 Q2/Q3 and ADR-0212. Prerequisite: #399 merged. May run in parallel with F14.3. Bounded input: the Contra stage-1 recording recipe of the 2026-09-23 re-record (mint with `scripts/stages/contra/mint-stage1.txt`, record with `stage1-probe.txt`, dylib provenance proven before recording; `.mss` is never versioned, so mint first), one unconditional cell, or a cell whose pose is known to be on screen at the compared frame — the 2026-09-23 attempt picked a `spriteNearby`-gated cell and compared only the final frame (`docs/validation/f12.11-stop3-and-gimp-findings-2026-09-23.md` §3.2). Use `headless_record`'s `replace=` and `reload-at-frame=` (emulated frames, not seconds). Stop when (1) path (a) renders pixel-exact at the compared frame after the reload, against an unpainted control of the same run that differs, with `sheet_repaint` dropping the unchanged cells — closes F12.11 (3); (2) path (b) does the same, and `mep_figure.py import --verify` reports 0 lost / 0 added keys — closes ADR-0209 Q2/Q3's "in-game F12.3 reload not verified"; (3) both runs are logged in `docs/validation/` with the binary hash and the frame compared. |
| F14.2 | **Re-score the 28-ROM cold read on current `main`.** The F12.2 sweep's criterion 3 ("the paste works, the build works, the screen changes") predates ADR-0217/0218, F12.13, F12.15, F12.16 and #344's closure, each measured against its own cause only. | Go-ahead verbatim *"Sim, como recomendado (Recommended)"* (2026-09-23); follows ADR-0214. Runs only after #399, #400 and #401 merge. Bounded input: the same 28 ROMs and `docs/validation/f12.2-sweep-evaluator-briefing.md`, packs re-recorded on one proven binary, scored by `scripts/f122_score_panel.py`; the evaluator is a fresh Opus session per ROM. Stop when criterion 3 is re-scored on all 28 and every remaining failure has a named cause and either a bug on the board (`scripts/report-bug.sh`) or a stated non-goal. |
| F14.3 | **Declare route sets for the four stage dirs that lack one.** `record_library.sh` falls back to `static` for games whose routes already exist: only `scripts/stages/mm3` and `scripts/stages/zelda` carry a `stage-set.json`. | Go-ahead verbatim *"Sim, como recomendado (Recommended)"* (2026-09-23); follows F12.10 and ADR-0182/0184. May run in parallel with F14.1. Bounded input: `contra`, `metroid`, `zelda2` and `excitebike`, each pinned to the dump its routes were authored on, never matched by folder name (#314). Stop when `scripts/record_library.sh` resolves `routes` for all six, every kit `--verify` reports 0 lost / 0 added, and `library-report.md` shows `seen %` per game. |
| F14.4 | **Every drawn shape reaches the shape registry** (option (i) of ADR-0229), so the organised and `unsorted` sheets can reach the pack's keys and not only the retained stream's. | **ADR-0229 `proposed`**, options (i)–(iii); user leans (i), 2026-09-23, pending measurement. Not started until ADR-0229 is accepted. Bounded input: the Castlevania 60 s and Zelda 85 s runs ADR-0209 measured. The measurement that gates acceptance: pack coverage, grid-dump size and wall clock on both runs under option (i). Stop, once accepted, when the coverage figure is re-measured on both games with its cost and `mep_build` round-trips byte-identically. |
| F14.5 | **Measure counter-locked scenery cycles** — the share of cycles (`PoseStats.Cycles` and background animation cycles) whose phase is constant against the global frame counter, over two recordings of the same route. Measurement only; nothing emits `frameRange`. | Scoped as a measurement only by the same decision (*"Sim, como recomendado (Recommended)"*, 2026-09-23); no ADR yet. The ADR on emitting `frameRange` (amending ADR-0189 §4) is written after these numbers. Bounded input: Metroid Norfair (`docs/validation/metroid-artist-workflow-evidence.md` §4) plus one Contra background animation. Stop when the share is logged per game and per axis in `docs/validation/`. |
| F14.8 | **Human session bundle.** One scripted sitting: F12.11 (2) (GIMP and Krita on the regenerated four- and five-layer files, every layer named, `paint` selected), F12.5's hand-added overflow cell, and a timed attempt at Phase 5's "< 1 h to a publishable pack" on one game. F9.18 stays its own panel. | Needs a person; no agent can close it. Stop when each of the three rows has a person's log in `docs/validation/`. |

### 5. Order of execution

1. **F9.18:** F9.18-V closed on 2026-09-15 — current-binary correctness of the
   documented painting path is established on CHR RAM and CHR ROM, with
   pixel-exact evidence on both, including the runtime condition-miss and
   mirrored-instance checks (§3). The independent human panel now runs and
   records all applicable criteria. C.5's agent runs cannot satisfy this gate.
   Should a defect reproduce instead, fix it in a separately scoped task before
   rerunning the affected criterion, and do not reopen fixed issues on the
   strength of the old C.5 logs alone.
2. **Part B P.1-local:** shipped 2026-09-17 (ADR-0206) — the local identity
   cache, its nested-file invalidation and the local/catalog deduplication
   acceptance are in §3 and Part B §8.
3. **S10.b:** the user runs the scoped hosted-model experiment; its results feed
   an egress/provider ADR or a recorded decision to defer. Whole-subject product
   work additionally depends on Phase 9 selection/export/paint evidence.
4. **Manual/hardware residue:** native picker, audio listening, physical input
   and optional classical A/B when their prerequisites are available.
5. **Phase 12:** F12.1, F12.3–F12.10 and F12.12–F12.17 are delivered (§3).
   F12.2's evaluator row is **closed** — two fresh Fable sessions, both PASS
   (`docs/validation/f12.2-fable-panel-2026-09-19.md`), then the same protocol
   on all 28 ROMs with Opus as the standing evaluator
   (`docs/validation/f12.2-opus-sweep-2026-09-19.md`: criterion 1 28/28,
   criterion 4 27/28, criterion 3 13/28 on the path as dispatched; F14.2
   re-scores it). F12.5 still owes a hand-added overflow cell and F12.11 its
   stop conditions (2) and (3); both are carried by Phase 14 (F14.8, F14.1).
6. **Phase 14, then Phase 13** (user's decision, verbatim: *"Sim, como
   recomendado (Recommended)"*, 2026-09-23): #399–#401 → F14.1 ∥ F14.3 →
   F14.2 → F14.4/F14.5 (measure before deciding: ADR-0229's figures for
   F14.4, the phase-constancy share for F14.5, and only then the ADRs) →
   Phase 13 R.1/R.2. F14.8 runs whenever a person is available.

One implementation slice per task; architecture changes still require their
ADR. This documentation update records work and acceptance, not completed runs.

### 6. ADR map

One line per decision. Chronology, amendments and evidence live in the ADR
files and in §3.

| ADR | Status | Meaning for this roadmap |
|---|---|---|
| 0040/0044/0047/0049/0050/0052/0120/0121 | accepted | shipped foundations — storage, permissive targets, fingerprints, sibling convention, `<background>` capture, level-2 audio, zip discovery fallbacks; do not diverge without amending |
| 0122/0126/0127/0129/0130 | accepted | unit-test and CI wiring (`UI.Tests`, `core_unit_tests`, extracted-helper pattern) |
| 0123/0124/0125/0128/0131/0136/0137 | accepted | H-series hygiene: firewall parity, fixture format, test helpers, ThrowsAny, CI contract, `mep_compare` dispatch, `make doc-checks` |
| 0051/0132/0133/0134/0135/0142 | accepted (0134 = Option A) | Phase 5 audio: sound-driver discovery, variant cap, mute mask, loop point, Extract Audio contract, crossfade |
| 0138 | accepted, amended in place (D5) | Phase 6 design; F6.0–F6.8 shipped |
| 0139/0140/0141 | accepted | Part B identity: `content_id`, `pack_id`, one slot per `pack_id` with the `content_id` update trigger (amends 0138 §37) |
| 0143/0144/0145/0146/0147/0148 | accepted | one slot per game; audio via bundled patch; optimistic matching; auto-load every accepted pack (supersedes 0138's consent clauses); `auto/` + `mep/` siblings; self-contained catalog rows |
| 0149 | accepted | Phase 8 border layer (MEP v1.5) |
| 0150 | accepted | Avalonia.Headless XAML-wiring tests (`UI.HeadlessTests/`) |
| 0151/0152 | accepted | unresolvable `<background>` is a lint error; known-missing errata (F6.8) |
| 0153/0156/0159/0160/0164/0166 | accepted (0153 amended by F9.12/F9.16) | Phase 9 sheets: vocabulary + grouping + maps; screen residency; save-time anchors; `textures/chr/`; adjacency sidecar; screen ownership of nodes |
| 0154 | accepted; §2 Option A superseded by ADR-0192 | F9.6 external repaint remains loopback-only and `generated` remains disclosure, not a gate; generative diffusion is retired until the measured run required by ADR-0192 |
| 0155/0157/0158/0163/0167 | accepted | `-MMD -MP`; frame-counted headless input; no `NES_ONLY`/`LessUI`; fork–upstream coexistence; HUD-only capture |
| 0161 | accepted (2026-09-06) | positional palette-variant correspondence (F9.6 §5) |
| 0162 | accepted (2026-09-06) | accuracy suite as a regression gate (H10); not in CI by decision |
| 0165 | accepted | F9.18 composition editor: external stdlib tkinter tool over a host-free engine |
| 0176 | accepted (2026-09-12) | sprite grouping counts both sides of its ratio per frame (`SpriteGrouping.cpp`); the denominator fix behind F9.20's pose tracks |
| 0168 | **superseded** (2026-09-11) by ADR-0171 | figure (`sprNNN` group) as the unit — S10.a measured the walk at 6.7 % / 10.5 %, so the answer was retired and the principle kept; §2/§3 stay readable as the specification of the fallback path for a pack recorded before ADR-0170 |
| 0171 | accepted (2026-09-11) | the sprite layer's unit is the **pose** (ADR-0170's `poses.json`), the `sprNNN` figure is the fallback and the bare node the degenerate case; fixes the ranking denominator ADR-0168 left open and accepts contact-merged poses. Implementing slice: F9.18's sprite layer |
| 0169 | accepted | recorder publishes frames one way; the live viewer never blocks the run |
| 0170 | accepted (2026-09-11) | the recorder writes `sheets/poses.json` from the OAM stream it already holds; shipped as F9.19, and the prerequisite S10.a named |
| 0179 | accepted (2026-09-12) | `poses.json` gains succession (`next[]`/`hold`), `cycles[]`/`sequences[]` found on the track sequence, and `variantOf` for figure + projectile; the editor lays poses out by cycle. Shipped as F9.20 (2026-09-12) |
| 0180 | superseded (2026-09-12) by ADR-0179 §4 | a pose decomposes into rigid `parts[]`; the kit measurement (`runs/golden-20260912/spike-pose-parts.md`) found the cover to be the whole figure inside its variant on 21–43 % of poses and genuine limb parts on Contra only, so `variantOf` is the part story the data supports. F9.21 withdrawn; reopens as a measurement only |
| 0181 | accepted (2026-09-12); §1–§3 shipped | the retained frame keeps the controller state of both ports and `poses.json` reports what the run exercised (`input.held`, `input.never`); §3 attributes a cycle's `driver` by interruption (a release on the port stops it within 12 f on >= 2/3 of >= 4 windows), shipped as F9.23 (2026-09-13) with per-game probe scripts |
| 0182 | accepted (2026-09-13) | per-stage recording coverage is judged by the mechanisms the states exercise, not by stages played; Contra stops at `stage4-boss`, F9.22 closed, a further stage needs a measurement that names it |
| 0183 | accepted (2026-09-13) | a recording produces an **artist kit** of four surfaces (figure grids, named scenery, stage maps, completed pattern pages), generated as a projection over the recorded pack, reading order included; evidence and inference are never confused (`seen: false`), and a surface counts as delivered only when the pack rebuilds with the same `(tileData, palette)` key set |
| 0184 | accepted (2026-09-13) | a recording may carry a cheat only as a **RAM-address** code (`NesCustom`, address below `$0800`), never a PRG patch — a Game Genie code is PRG-space by construction and a CHR RAM game unpacks its tiles out of PRG; coverage cheats feed only stage maps/pattern pages; the amended navigation mode may feed all four surfaces when its clean-control evidence passes |
| 0185 | accepted (2026-09-14); shipped | a **published TAS movie** is an admissible recording driver when it matches our ROM byte for byte: it is input, never evidence, so a movie-driven run is a *clean* run for all four kit surfaces. `.fm2` is converted outside the Core by `scripts/fm2_to_bk2.py` (the Core keeps `.bk2`/`.mmo` and has no `.fm2` reader); the harness refuses a movie the Core silently dropped; more keys demonstrate coverage gain only. Synchronization remains unverified without independent route checkpoints (frame plus expected scene/state) through the claimed segment; a divergent checkpoint invalidates that segment even if coverage grows. Contra is the one game it does not help — every modern publication runs the Japanese VRC2 cartridge. |
| 0186 | accepted (2026-09-14) | a recording also yields a **code/data map**, and the only ROM we disassemble is the part we executed. The CPU performs the code/data separation and the offset is absolute, so two of static analysis's three walls fall by construction; the third, naming, stays human. Coverage accumulates by union and a run that logs nothing fails loudly. §4 is the load-bearing clause: access is not meaning, so a large untouched-by-code data run is reported as a *candidate* with offset and bank and never with a name. Amended the same day: the art-coverage justification is withdrawn; this is program analysis. |
| 0187 | accepted (2026-09-14); shipped | Dropbox and MEGA are allow-listed pack hosts, each with its own fetch kind (amends 0138 §41); five-way mirror drift check shipped as Phase 11 C.8 (`verify_pack_host_allowlist_drift.py`) |
| 0192 | accepted (2026-09-15); shipped | Generative repaint backend retired until measured (supersedes 0154 §2 Option A; Phase 11 C.8) |
| 0188 | accepted (2026-09-14); shipped as F9.28 | an AI judges a rendered surface; its judgement is a **proposal** that becomes evidence only through a human `promote` — the judging half of AI in this project; ADR-0154/0192 govern the repaint half |
| 0189 | accepted (2026-09-14); implemented in the same change | a sprite-group edge is serialized as a `spriteNearby` condition and a conditioned tile always keeps a bare twin; defers `frameRange`, `tileAtPosition`, `memoryCheckConstant` |
| 0190 | accepted (2026-09-14); implemented in the same change | `tileNearby` auto-attached from a directed co-occurrence table gated on both-ways frame support; removes `tileNearby` from 0189 §4's deferrals |
| 0191 | accepted (2026-09-14); implemented in the same change | CI compiles **Linux only**: `tests.yml` (Windows MSBuild + `PGOHelper citests`, not reproducible on Linux) deleted, `unit-tests.yml` folded into `checks.yml` as `ui-tests`/`headless-ui-tests` and deleted, `build.yml` trimmed to its Linux/AppImage legs. The macOS Apple Silicon binary is built locally by `make release-macos` (C.4) and Windows is retired from CI, so MSVC-only breakage is caught only when it returns. Amends ADR-0131 (the unit-test contract moves to `checks.yml`) and drops C.1's "Windows `tests.yml` job" from the required checks. **Amended by 0203/0204 (2026-09-16/17): Windows and macOS Apple Silicon are built again on the existing triggers, and the download channel is the rolling `ci-latest` pre-release — the "Linux only" clause above no longer holds** |
| 0194 | **accepted 2026-09-22** (*"Aceitar (Recommended)"*); nothing to implement — proposed 2026-09-15 | the kit's cross-recording union is the pattern pages, "judged as a union" means the `--also` donation and no generator gains a merge; F9.25's text cites it |
| 0195 | accepted (2026-09-16); implemented in the same change | the recorder always asks for `automaticFallbackTiles` on a CHR ROM game |
| 0199 | accepted (2026-09-16); implemented in the same change | the community-pack classify step is a direct, tool-free Gemini API call, not the Claude Code action |
| 0200/0203/0204 | accepted (2026-09-16/17); shipped | a PR against `prod` builds the binaries; CI builds Windows and macOS Apple Silicon again on the existing triggers; the download channel is a rolling `ci-latest` pre-release with a link check — together they supersede 0191's "Linux only" |
| 0201/0202 | accepted (2026-09-16); shipped | the runtime surface (window title, data folder, adopt-never-move) and the release artifacts are named MesenAI; tag, tools, catalog and env vars keep `mesence` on purpose — this file's title is deliberate for the same reason |
| 0205 | accepted (2026-09-17), **pending slices R.1/R.2** | a shared replay is a `.mmo` from a single *Record and share* action, attached to its submission issue, listed by ROM and ranked by votes; the git tree carries no replay bytes. Phase 13 above |
| 0206 | accepted (2026-09-17); shipped as Part B P.1-local | the local-container `content_id` cache is a stat-manifest fingerprint validated off the ROM load path |
| 0207/0208 | accepted (2026-09-17); implemented | `core_unit_tests.cpp` loses its line ceiling (the ratchet guards the rest); the core log keeps a 1 000-entry ring plus an uncapped `mesen.log` with truncation marked |
| 0211 | accepted (2026-09-19); shipped the same day | a declared `<supportedRom>` that contradicts the loaded ROM refuses the install — the guard for #314 (Bomberman rendered with Contra's art). Amended on acceptance: the loaded ROM's No-Intro body hash also counts as a match (the loader already accepts both forms for `<patch>`), and a declaration equal to the pack's own `<patch>` target is the patched ROM (ADR-0198 §2), not a contradiction |
| 0193 | accepted (2026-09-15); documented in the same change | `checks.yml` keeps **both** triggers, and the `push` on `main` is not an optimization to be cut: `pull_request` reports the five required checks before merge, and `push` is the only gate for the paths that bypass the ruleset — a direct push (admin `bypass_actors`, which is how `community-pack-catalog.yml` and a hand fix land) and a merge-commit/rebase tree the PR never tested (`strict_required_status_checks_policy: false`). Measured over the last 60 commits on `main`: 49 squash-merges, 7 merge-commit/rebase PRs, 4 with no PR at all. Reopening conditions in §5; the verifier asserts the `pull_request` + dispatch half and deliberately not the `push` one |
| 0196 | accepted (2026-09-16), shipped as F12.5 (2026-09-19) | `<addition>` is a compose-editor export anchored on a pose's observed root cell; its target key is synthetic by construction and provably unmatched (CHR ROM: index past CHR; CHR RAM: reserved pattern + `$0D` palette, evidence check on the palette). Slice F12.5 |
| 0197 | accepted (2026-09-16), §1–§2 shipped as F12.6a (2026-09-19), §3 shipped as F12.6b (2026-09-19); amends 0189 §4's scope to emission only | hand-authored conditions are admitted in sheets and `mep_lint.py --routes` evaluates them on every retained frame of every recording; the three refusals of 0189 §4 stand; the recorder retains `$0000`–`$07FF` per retained frame so `memoryCheckConstant` in that window is evaluable (§3). `spriteNearby` is still `not evaluable` — F12.6b widened the memory plane, not the sprite stream |
| 0198 | accepted (2026-09-16), §1 shipped as F12.7 (2026-09-17, completed 2026-09-19); §3 shipped as F12.17 (2026-09-23) | a legacy plain `hires.txt` pack is imported into a MEP project by an external stdlib tool in the stock-ROM namespace — round-trip proven with 0 differing keys on Ninja Gaiden, Contra80s and Super Mario Bros.; a pack keyed against an IPS-patched ROM imports against the patched ROM as a second namespace that the recording loop does not reach (§3, F12.17) |
| 0209 | Q4 accepted and shipped as F12.8 (2026-09-19); Q1–Q3 accepted 2026-09-20 — (b), (e), (i); **Q2/Q3 shipped 2026-09-22** as `scripts/mep_figure.py export`/`import` ([log](../validation/adr0209-q2-q3-figure-export-2026-09-22.md): Contra `spr000`, 10 cells, keys 116 → 116, 0 lost / 0 added); **Q1 shipped 2026-09-22** as option (b) ([log](../validation/adr0209-q1-inferred-label-2026-09-22.md): `Core/NES/HdPacks/SheetLabels.h`, Contra 49/49 sidecars labelled, `mep_build` round-trip byte-identical), go-ahead verbatim *"vai com o Q1 da ADR-0209 em paralelo também"* | MesenAI owns **selection** and **return**, painting is delegated to the artist's own program; the `unsorted` remainder sheet gives every recorded shape a cell. Q1–Q3 answered the label author (the Core infers it at record time, the artist renames), the export unit (the `sprNNN` figure reassembled through its `evidence[]` offsets, not the cell) and the return path (F12.4's template, with this ADR adding only the launch and the reload trigger). Slices F12.9–F12.12 are bounded by its three constraints |
| 0229 | **proposed 2026-09-23** — user leans (i), 2026-09-23, pending measurement (pack coverage, grid-dump size and wall clock on the Castlevania 60 s and Zelda 85 s runs); slice F14.4 | what the recorder hands the shape registry, so the organised and `unsorted` sheets can reach the pack's keys: (i) every shape `ProcessTile` draws, (ii) retune the retained-frame cap, (iii) leave it and document that the pattern pages carry every key. Answers the question ADR-0209 "What (k) actually closed" left open; placed beside 0209 for that reason |
| 0210 | accepted (2026-09-20); §3 shipped as **F12.12** (2026-09-20, PR #361), bounded input measured 2026-09-20 ([log](../validation/f12.12-third-party-index-read-2026-09-20.md)); §2 is what F12.9 stands on. **Amended 2026-09-20** — Context item 2 retracted against that measurement (its "5 532 keys out of range" is a base-16 reading of a `<ver>`100 pack's decimal tokens) and the Consequences bullet that prescribed that reading corrected; the Decision is unchanged | coverage has three sources in order — recording (`seen: true`), the ROM's own CHR (23 CHR ROM games, shape complete by construction, `defaultTile=Y` is the palette wildcard), a third-party key index as facts (palettes always, art only for the 7 CHR RAM games, conditions never). Acceptance unblocked F12.12 and, with an ADR-0183 §1 amendment, F12.9 |
| 0214 | accepted (2026-09-19), amended the same day (Opus replaces Fable); protocol in use — the two-game Fable panel and the 28-ROM Opus sweep both ran 2026-09-19 | a fresh Opus session is the evaluator for a Phase 12 artist-surface cold read; the briefing is the goal, not the path; the menu's identity is the visible label; `hires.txt` is a fail gate; P15 is an observation. Does not amend F9.18 or ADR-0188 §5 |
| 0217 | accepted (2026-09-20), implemented the same day (`186077d0`), re-recorded and **measured 2026-09-20** — 95 co-gated captures of 108 became 0 of 61 across four games, and the sweep extended the same day to the whole bounded library — **30 packs, 193 captures, 0 co-gated**, with the "before" run reproducing the F12.2 sweep's gate definitions byte for byte as a control; Punch-Out!! (issue #339's game) keeps all ten captures and skips none — Option C separates rather than discards ([log](../validation/adr0217-0218-anchor-gate-collisions-2026-09-20.md)) | a captured screen draws only where its gate separates it from every other capture of the recording; Options A (refuse a capture whose gate an earlier one already satisfies) and C (every other capture is a rival) ship, D/E do not. Issues #339, #344
| 0218 | accepted (2026-09-20), implemented the same day (`186077d0`), re-recorded and **measured 2026-09-20** — 95 co-gated captures of 108 became 0 of 61 across four games, and the sweep extended the same day to the whole bounded library — **30 packs, 193 captures, 0 co-gated**, with the "before" run reproducing the F12.2 sweep's gate definitions byte for byte as a control; Option B never fired (0 post-hoc drops on all four); the cost lands as Option A refusals, and it is large — Ice Climber goes from 25 captures to 4 ([log](../validation/adr0217-0218-anchor-gate-collisions-2026-09-20.md)) | a capture's anchors are chosen against every other capture already decided in the same recording; Option A is the avoidance pass, Option B the post-hoc drop with a build-time warning. Coordinated with ADR-0217, one change. Issue #349
| 0219 | accepted (2026-09-20), shipped as F12.9 the same day | a kit may project over the ROM alone — every cell `fill`, `seen: false`, no play session — amending ADR-0183 §1 by reference. CHR ROM only; CHR RAM refused, pointing at ADR-0210 §3. Recording always wins, the recorder is untouched, and the ROM-SHA-1 pin is disabled by construction on that path. Slice F12.9
| 0220 | **accepted 2026-09-22 — code landed the same day as F12.11; stop conditions (2) (GIMP/Krita, a person's log) and (3) (paint round-trip through F12.3) not evaluated.** Proposed 2026-09-20 and left so by user decision the same day; accepted with *"aceito o F12.11. nao implemente ainda."* | a layered `.ora` beside every surface, write-only for the toolchain, five layers with `paint` topmost visible, the flat PNG over F12.4's name the only return path. Blocks F12.11, which does not start until this is accepted; the deferral's reason is that no GIMP/Krita artist population is measured and its stop condition (2) needs a person with both programs
| 0221 | **accepted 2026-09-22, option B — shipped the same day as F12.13** ([log](../validation/f12.13-variant-kind-rule-2026-09-22.md)), stop condition (1) not met, follow-up ADR-0223. Opened `proposed` 2026-09-20 from the ADR-0217/0218 measurement, which reproduced issue #339 on the post-change pack, with five options (A raise `kAnchorVariantAgree`, B classify a variant by the kind of difference, C require one discriminating probe, D refuse at draw time, E accept and surface it to the artist). B alone was picked: ADR-0159 §1 narrowed (a variant may not add content the capture lacks), ADR-0156 not amended, E ruled out because ADR-0146 auto-loads every accepted pack | a capture's gate must separate it from the **frames** it must not draw on, not only from the other captures. Root cause measured: the pre-fight frame agrees with `screen003` on 919 of 960 cells (0.9573) against `kAnchorVariantAgree` 0.90, so it is a *variant*, and ADR-0159 §1 deliberately excludes the cells a variant changes from the anchor pool — the gate is built to match it. Acceptance test exists: the ROM draws 7 808 `STARRING`/`LITTLE MAC` pixels, the render has 0 |
| 0222 | **accepted 2026-09-22, option A — shipped the same day as F12.14** ([log](../validation/f12.14-oam-dump-self-describing-2026-09-22.md)); go-ahead verbatim *"dispara as frentes 1, 2, 3 e 4 em paralelo usando workflows"*. Opened `proposed` the same day from the F12.6a/F12.6b logs' open item; three options for the sprite side (A self-describing OAM dump with `K`/`P` lines and a palette id per entry, B write the `ShapeId` and resolve against the grid dump of the same recording, C leave sprite conditions `not evaluable` and say so to the artist) plus `memoryCheck` from the existing `M` plane under any of them; four questions for a human | the retained OAM stream must let lint resolve a sprite to its `(tileData, palette)`, so `spriteNearby`, `spriteAtPosition`, `positionCheck*` and `memoryCheck` stop reporting `not evaluable` |
| 0223 | **accepted 2026-09-22, option A — shipped the same day as F12.16** ([log](../validation/f12.16-emptiness-probes-2026-09-22.md)), after F12.15. Picks verbatim *"A: probes como ultimo passo (Recommended)"*, *"Depois da F12.15 (Recommended)"*; the fight-screen half went to ADR-0224. Opened `proposed` the same day. Opened from the F12.13 log, which measured that ADR-0221's option B fires (228 Punch-Out!! frames, 5 091 across the library) and changes no gate, because ADR-0050 excludes flat cells from the anchor pool and a one-letter-later frame differs from its capture only on a flat cell. Three options: A last-pass "emptiness probes" (prototype: 437 → 141 erased cells), B rank flat cells with everything else, C leave it. The fight-screen 41–45 s residue was traced the same day: every erased cell there is a behind-background sprite overpainted by the priority-20 layer in `HdNesPack::GetPixels`, so no gate rule reaches it and #339 has two causes; the render-path question is recorded as open | a flat cell may be an anchor when it is the only thing that separates a capture from an addition-rival — the human picks which option, and whether a flat probe may be emitted at all |
| 0224 | **accepted 2026-09-22 — shipped the same day as F12.15** ([log](../validation/f12.15-behind-bg-sprites-2026-09-22.md)); stop condition (2) partially met. Opened and accepted from the F12.13 fight-screen trace through four structured questions (scope opt-in per pack, tool split in the same slice, ADR only at first, opt-in as a new hires.txt tag rather than an `<options>` token, which upstream loaders reject); build go-ahead *"libera a F12.15, dispara as três partes em paralelo. mergea o PR assim que puder e garante que t  tudo na main."* | a recorded screen must not hide a behind-background sprite where the ROM's background is colour 0; `<bgPreservesBehindBgSprites>` opts a pack in, the recorder writes it, community packs keep today's render; the overdraw tool must split background loss from sprite loss before pricing any gate rule |
| 0225 | **accepted 2026-09-23 — implemented by F12.18 (dispatched, not yet on `main`)**; pick verbatim *"px/py por tile"*, go-ahead verbatim *"pode implementar as duas ADRs em paralelo"* | a pose keeps its pixel offsets: per-tile `px`/`py` (and `z` on overlap) beside the tile-unit `dx`/`dy`, which stay; sheets keep ADR-0153's cells and gutter, composed views (`mep_figure.py`, kit figure rows, compose editor) place at pixel precision with no intra-figure gutter; amends 0170 §1 ([measurement](../validation/contra-pose-offsets-and-flicker-2026-09-23.md)) |
| 0226 | **accepted 2026-09-23 — implemented by F12.19 (dispatched, not yet on `main`)**; pick verbatim *"Tolerar 1 frame"*, go-ahead verbatim *"pode implementar as duas ADRs em paralelo"* | the pose-track linker bridges one missing retained frame (`kPoseTrackMaxGap = 1`, skipped frame `RepeatCount <= 2`), so respawn flicker does not shatter a track into single-frame tracks and the sequence fallback stops promoting the recording driver's period; the Contra kit is regenerated from a fresh recording; amends 0179 §1 |
| 0227 | **accepted 2026-09-23 — reflected in Phase 10 "Idea under test" (no slice yet)**; decision verbatim *"Pelo nome, no kit"* | a Phase 10 subject is named from the artist kit as the set of kit ids the player names (`usrNNN` grids or the pose ids their `kit.json` records list); a rest grid counts only as the poses named from it; the toolchain infers no character membership, automatic grouping left open; amends Phase 10's viewer entry point |

### 7. Risks

| Risk | Mitigation |
|---|---|
| Project framed as a distributor of derivative content | catalog holds URLs + hashes + licenses only; client never scrapes third-party hosts; user supplies unlicensed audio |
| Recipe vocabulary grows into a scripting language | new op = new `recipe` major + new ADR; clients skip unknown versions |
| Two agents implementing the same ADR in parallel | one task per slice; accepting an ADR is a request for work, so say which agent owns it before implementing — no background runner claims `accepted` ADRs any more (the dev-squad plugin was removed on 2026-09-03) |
| Upstream pack drift after acceptance | sha256 in the catalog + drift check; client reinstalls when the slot's `content_id` changes (ADR-0141) — a wrapper-only sha256 change does not reinstall |
| Catalog-shaping decisions recorded only in issue comments or commit messages (the 2026-08-31 audio-only NEA removal) | every such decision gets an ADR or a PRD line the same day (ADR-0148 backfilled the one already made) |
| ADR files deleted by an unrelated commit go unnoticed (0130/0131/0136/0137, 2026-08-28) | restored (D1); `scripts/checks/verify_adr_refs.py` in `make doc-checks` fails on a dangling `ADR-NNNN` reference |
| Scope explosion | phases independent; GitHub is the only backend; no telemetry |
| Phase 10 sends ROM-derived art to a hosted model | only S10.b does, by hand, by the user, from their own account, with the files listed first; no tool in the repo automates a hosted call until an ADR reopens ADR-0192 and amends ADR-0154's remaining local-only contract |
| Phase 10 spikes read as a product plan | the section names no modules, formats or product slices; ADRs that depend on spike results (egress, studio storage, tool contract, reverse channel) are written after S10.a/S10.b report numbers. ADR-0227 is the deliberate exception: it fixes only how a subject is chosen, which no spike measures |
| Phase 9 judged by pixel metrics instead of legibility (F5.4e "shipped" green while emitting no sheet on any real game) | the human validation panel in Phase 9 is the acceptance gate. *Honest record:* F9.0–F9.17 shipped on spot checks, and every panel since has been a proxy or a builder — the "two golden games logged" rule has never been met once. For F9.18 it is still a person who did not build the feature. Phase 12 cold-reads are a fresh Fable session (ADR-0214), with `hires.txt` as a fail gate; that is not F9.18 and does not certify taste or return |
| The PR gate regresses and stops compiling the Core or running the Python suite | Phase 11 C.1 shipped both as jobs in `.github/workflows/checks.yml`; its verifier protects the workflow contract, while local runs remain required when CI is unavailable |
| The roadmap and the ADR Status lines drift behind `main` (three shipped rows in a live table, four "not yet in code" ADRs for shipped code, ADR ids missing from §6 — all found 2026-09-14) | Phase 11 C.2: a `doc-checks` script fails on a `shipped` row in a live table; ADR Status-line edits listed per PR; this file's header date is part of "done" (§ Process) |
| An ADR is accepted and implemented in the same turn (ADR-0189, ADR-0190) | Rule relaxed by the user on 2026-09-14 and written into `CLAUDE.md`: same-turn implementation is allowed when the change ships with unit tests covering the decision and the go-ahead is quoted in the ADR Status line **and** the PR body; otherwise accepting stays a request for work |
| The project has no external user (1 star, 0 forks, 100 % of issues and PRs by the maintainer; every panel a proxy) so "the best tool for the artist" is unmeasured | Phase 11 C.4 shipped a binary and C.5 ran the one-hour protocol on two games. **Trade-off taken 2026-09-14 and named 2026-09-19 (ADR-0214):** Phase 12 cold-reads are a fresh Fable session, not a person — faster, repeatable, and still a proxy for taste, fatigue, and whether a human would return. P15 is an observation. A real external-user C.5 rerun remains out of scope until reopened; this does not waive F9.18's separately required human panel |
| Everything is tuned to one reference pack (Contra80s: 864 conditions, one author's habits) | Phase 11 C.6: a second hand-made pack measured with the same four numbers before any grouping or condition rule is tightened again |
| A layered file (`.ora`, later `.psd`) quietly becomes a second source of truth: a tool reads `paint` out of it and the sheet PNG stops being what `mep_build` sees | F12.11's ADR states the file is write-only for the toolchain; the return path is a flat PNG diffed against `*.orig.png`; lint refuses the guides sentinel colour so a wrong export fails loudly instead of shipping grid lines |
| A static kit (F12.9) or an index import (F12.12) is read as evidence that a tile was seen | every such cell is `seen: false` with provenance `fill` / `index` (ADR-0183 §3, ADR-0210 "Provenance is recorded per cell"); `ARTIST.md` says so in its first line; no `poses.json`, scenery or map is ever synthesised from a static source |
| Parallel sessions on one machine: a checkout falls behind `origin/main` and re-does merged work (this checkout was 22 commits behind with a stale duplicate of three merged PRs on 2026-09-14) | check `origin/main` before dispatching or editing; the memory note `feedback_check_main_before_dispatch` is the standing rule; a stale dirty tree is stashed, never committed |

### 8. References

- SUPER ZSNES — https://www.zsnes.com/ · VGMusic · romhack.ing · Zeldix (MSU-1, other hosts)
- No-Intro DATs — https://no-intro.org/ · rcheevos `rhash` · vgmrips (VGM/GD3) · beat/BPS spec
- Precedents: *MGM v. Grokster* (2005); Yuzu/Nintendo settlement (2024)

---

## Part B — Player shell (default GUI)

**Status:** **Phase 7 delivered, P.1-local included** (2026-08-28 → 2026-09-01;
P.1-local 2026-09-17, ADR-0206; record in Part A §3). Product text of §3–§6
accepted by the user 2026-08-28. No implementation debt remains (§8). Manual
residue: the native file picker (F6.5) — the letterbox fit was closed
2026-09-05 (`RendererViewportFit`, `UI.HeadlessTests/RendererLetterboxTests.cs`);
the cards, the Player Settings tabs and the picker's arrow navigation are
asserted by `UI.HeadlessTests/` (ADR-0150) and the aspect-ratio math by
`core_unit_tests` Bloco N ·
**Author:** sbihaiko ·
**Scope:** MesenCE fork (`main`); nothing goes upstream ·
**Parent roadmap:** Part A of this document (Phase 7 entry). Pack/core work
stays there; this Part owns chrome, pack identity, duplicates, and the
player-facing choice between packs ·
**Specs:** [MEP-v1](../specs/MEP-v1.md) · [MEI-v1](../specs/MEI-v1.md) ·
[MEP-recipe-v1](../specs/MEP-recipe-v1.md) ·
**Decisions:** identity model (§3) and one-slot rule (§3.6) are accepted
product requirements, specified by ADR-0139 (`content_id`), ADR-0140
(`pack_id`, catalog uniqueness) and ADR-0141 (one slot, client update
trigger — amends ADR-0138 §37). Chrome (§6) is a product requirement;
`UiMode` has no ADR and needs one only if a trade-off beyond §6 appears ·
**Process:** one task per **slice** (P.1, P.2, …). Settle the slice's ADRs
first. A slice is done when its acceptance checks pass and this header plus
Part A's Phase 7 entry are updated.

---

### 1. Vision

The fork's thesis is *faithful, then enhanced, on by default*. The current
GUI is still classic Mesen: File / Game / Options / Tools / Debug / Help,
plus debugger, HD Pack Builder, netplay, movies, Lua. That chrome is
correct for authors and for anyone who already lives in Mesen. It is the
wrong first screen for a player who should drop a ROM and already hear and
see the enhanced game.

Default chrome becomes a **player shell**: recent games, drop a ROM, the
game fills the window, packs apply themselves, a thin overlay for pause /
save / pack / settings. **Advanced GUI** restores the classic Mesen menus
and tools unchanged.

This is one Avalonia process and one window, not a second binary. Player
and Advanced are chrome modes over the same ViewModels.

The legal principles of Part A §1 still apply: the official
channel carries URLs + hashes + licenses, never third-party assets; hosts
never execute pack content as code; no LLM in the client (a Phase 10 skin
tool, if its spikes pass, would be an external tool like the live viewer;
the shell contributes nothing until an ADR says otherwise).

Product consoles stay NES, GB/GBC/GBS, SMS/GG/SG-1000, GBA
(`docs/roadmap/AGENTS.md`). SNES gamepads stay as input.

### 2. Problem

Three pack-identity problems and one chrome problem.

**Identity**

1. **The zip the catalog hashes is not the pack.** Community submissions
   are GitHub `/archive/` trees, release zips, nested folders, whole
   repos. After ADR-0120/0121 discovery (and a MEP Recipe, when there is
   one) the host loads a *subset* of that zip. Two wrappers of the same
   tree look like two packs if identity is the source sha256. One primary
   zip plus two different recipes is two packs even when the source
   sha256 matches. Today's "Pack Hash" (mep-meta `source_sha256`, MEI
   `sha256`) is the download, not the pack.
2. **A content hash alone cannot version a pack.** Contra80s 1.0 and 1.2
   are the same product and two artifacts. If the unique id is the
   resolved-tree hash, they look like two competing packs for the same
   ROM and the player is asked to choose. Updates need a stable lineage
   id; integrity and duplicate-bytes detection need the content hash.
3. **Several real packs can target the same ROM.** That is not a
   duplicate. The player has to pick one, and the choice has to stick
   per ROM. Today the host applies the first lexicographic container
   (ADR-0040) and hides the rest behind Tools → Enhancement Packs (MEP)….

**Chrome**

4. **The GUI fights the product.** Enhanced Audio is already on by
   default (`AudioConfig.EnableEnhancedAudio = true`); bootstrap already
   writes `<Game>/auto/` beside the ROM; F6.4b will auto-install from the
   catalog. None of that reads as a player product while Debug and HD
   Pack Builder sit in the menu bar.

### 3. Pack identity — two ids, not one

A pack is a *product* that has *revisions*. Treating the content hash as
"the" unique id makes versions look like different packs. Treating the
source-zip sha256 as "the" unique id makes wrappers look like different
packs. Neither is sufficient alone.

#### 3.1 Four names, four jobs

| Name | What it identifies | Changes when | Already exists? |
|---|---|---|---|
| **`pack_id`** | the product (lineage). "This is Contra80s by Tastic." Shared by every revision | never, unless it is a different product | no — new (§3.3) |
| **`content_id`** | one revision: the canonical resolved pack tree the host will load | any loaded file changes | no — new (§3.2) |
| **`version`** | human/semver label of that revision | the author bumps it (can lie; `content_id` is the truth) | yes — `pack.json` `version` (MEP-v1 §3.1, MUST); absent on `hd-legacy` |
| **`source_sha256`** | the downloaded bytes (the wrapper) | the zip wrapper changes, even if the inner tree does not | yes — board "Pack Hash", mep-meta `source_sha256`, MEI `sha256`, `.mep-install.json` `source.sha256` |

Also **not** a pack id:

- **ROM No-Intro sha1** — the game. Many packs share one; one pack may
  list several `targets[]`.
- **GitHub issue number** — the submission. A second issue can be the
  same `pack_id` (duplicate submit) or a different one (competing pack).
  Useful as catalog provenance (`issue`, already in MEI v1.1 §2.2), not
  as the product id.
- **Container file name** — the local discovery key (ADR-0040/0049). It
  is the fallback `pack_id` for a folder the user dropped (§3.3), never a
  catalog id.

#### 3.2 `content_id` — identity of a revision

Computed **on the tree the host would actually load**, not on the zip
bytes: discovery first (MEP-v1 §2.1 rules 5–9, ADR-0120/0121), then the
recipe when one exists.

- **No recipe:** unzip → find the pack root → hash that tree. A GitHub
  archive whose pack lives in `HdPacks/Contra (U) [!]/` hashes only that
  subfolder. `__MACOSX/`, `.DS_Store`, README, screenshots outside the
  root do not enter the id.
- **With a recipe:** `content_id` is a function of (hash of the resolved
  *primary* tree, `recipe_hash`, the declared dep sha256s). CI can compute
  it without fetching `user_supplied` deps (it has the primary zip and the
  recipe's declared digests). Two recipes on the same primary zip are two
  revisions. The same recipe plus the same deps is the same revision even
  if CI never saw the dep bytes. The client computes the same function
  **at install time**, when `MepRecipeInstaller` still holds the primary
  bytes, and stores the result in `.mep-install.json` (§4); it does not
  re-derive it from the installed output tree.

Exact canonicalization (path order, which files, newline folding, zip
entry metadata ignored, whether `pack.json` `version` is part of the
payload) is the P.0 ADR. The product constraint is: **for tree-form identity, equal canonical payloads produce equal ids and
wrapper-only changes do not change the id. Recipe identity also includes the
recipe and declared dependency digests; it is not byte equality of installed
output (ADR-0139).** Recommendation for the ADR:
hash payload files, not the `version` string, so a label-only bump is not
a new revision.

`content_id` answers whether two canonical trees, or two recipe-input
composites, are equal under ADR-0139. It does **not** answer: *is this Contra80s 1.2 or a
different Contra pack?*

The algorithm has **two implementations, one normative reference**, like
the recipe interpreter (ADR-0138 §39): `scripts/` (CI, normative) and the
Core (client). A parity fixture keeps them equal.

#### 3.3 `pack_id` — identity of the product

Stable across revisions. Source, first match wins:

1. An explicit `id` field in `pack.json` (slug, lowercase, unique in the
   official catalog). This is a MEP minor bump and part of the P.0 ADR.
   Best long-term id; authors already have `name`/`version`/`author`.
2. Else, for a `github.com` / `codeload.github.com` pack URL:
   `owner/repo` (the origin, not the tag or release filename).
   `/archive/v1.2.zip` and `/releases/download/v1.2/pack.zip` of the same
   repo are the same product.
   **Amended by ADR-0143 (2026-08-29):** the `pack_id` is `owner/repo:<game-slug>`
   — origin × game, the slug taken from `targets[].name` in `pack.json`,
   else from the legacy HD pack's game subfolder — so one origin hosting
   N games yields N slots, and a multi-game zip is expanded by the
   pipeline into N sibling issues carrying `pack:split`. This is what the
   catalog emits today (e.g. `liquidzgit/hdnes:ice-climber`).
3. Else, catalog fallback: `issue-{n}` of the accepted submission. This
   is the only option for gists, `raw.githubusercontent.com` and Google
   Drive links (`scripts/pack_host_allowlist.json`) when the pack has no
   `id` — so for those hosts **product-level deduplication does not
   exist**; only byte-level (`content_id`) does.
4. **Local drops** (a folder or zip the user put in `EnhancementPacks/`,
   `HdPacks/<Game>/` or beside the ROM) with no `id`: `pack_id` is
   `local:<container-name>` (the ADR-0040/0049 discovery key). Two local
   containers with the same `content_id` are one pack (§5). A local
   container whose `content_id` equals a catalog entry's is that catalog
   `pack_id`, not a second choice. The required local `content_id` cache (**shipped 2026-09-17; P.1-local,
   ADR-0206**) computes it **once** and caches it under
   `EnhancementPacks/.cache/content-ids.json` keyed by the container's
   path plus a stat-manifest fingerprint of its tree — the container's own
   mtime alone cannot see a nested file change (§8) — and it is never
   computed on the synchronous ROM-load path: the load only reads the file,
   and a background refresh re-hashes what moved. Until the cache is warm
   the container is treated as `local:<container-name>`; the catalog merge
   happens on the next load. HD trees run to hundreds of MB — hashing them
   at every boot is not acceptable.

**Catalog uniqueness** (product requirement; enforcement is the P.0 ADR).
The catalog holds **one live row per `pack_id`** (§3.6) — never two
revisions of the same product.

**Origin binding (anti-hijack).** A `pack_id` is bound to the **origin**
of its first accepted submission: the `owner/repo` of the pack URL, or,
for hosts without one (gist, raw, Drive), the GitHub login that opened the
issue. A later submission that claims an existing `pack_id` (via `id` in
`pack.json` or via the same `owner/repo`) but comes from a **different
origin** is *not* a revision: it does not compete for the slot, is not
listed, and gets a comment + the `pack:needs-review` label for human
triage — a maintainer may re-bind the origin (author moved repos) or
treat it as a competing pack. Without this rule anyone could publish
`id: contra80s`, `version: 99.0.0` and have §3.6 push it to every
client. The catalog stores the bound origin in mep-meta
(`pack_origin`). Amends ADR-0140/0141 (recorded in both, 2026-08-28).

Actions when the incoming submission is from the **same** origin:

| Incoming vs existing | Meaning | Action |
|---|---|---|
| same `content_id` | byte-duplicate, even if `pack_id`/`version`/`source_sha256` differ | not a second pack; comment "duplicate of #N"; do not list twice |
| same `pack_id`, new `content_id` | new revision of that product | occupies the single slot if it wins §3.6's order; never a picker choice. Triage warns when `version` did not bump |
| different `pack_id`, different `content_id`, same ROM sha1 | competing packs | both listed; the player chooses (§5) |
| different `pack_id`, same `content_id` | same files under two names | byte-duplicate; the existing row wins |

`/revalidate` on the same issue rewrites that issue's **provenance**
(mep-meta: `source_sha256`, recomputed `content_id`, `version`,
`validated_at`) in place. Whether the revalidated revision **occupies the
slot** follows §3.6 — a revalidation that republishes a lower semver does
not displace a higher one already in the slot.

#### 3.4 `version`

Keep `pack.json` `version` (semver, MUST for MEP). It is a **label**, not
an id. On its own it is not sufficient to know "newest" (authors forget to
bump, or bump without changing files) — but it is the best available
*ordering* signal, which is why §3.6 uses it first and `content_id`
(unordered) never.

- `hd-legacy` has no `version`; the catalog and picker show the
  validation date and a short `content_id` prefix instead.
- `version` bumps, `content_id` does not → the revision did not change
  (wrapper-only, or a label bump). The client does not re-download.
- `content_id` changes, `version` does not → still a new revision of that
  `pack_id`. Triage warns; §3.6 still applies.

Do not order competing *products* by `version`.

#### 3.5 Why not one id

| Candidate as "the" unique id | Breaks |
|---|---|
| `source_sha256` (Pack Hash) | wrappers; pack is a subset; two recipes on one zip |
| `content_id` alone | every revision is a new pack; the player is asked to choose between 1.0 and 1.2 |
| `pack_id` alone | cannot tell duplicate bytes from an update; cannot verify an install |
| `version` alone | not unique; authors forget to bump; two products can both be "1.0" |
| ROM sha1 | many packs per game |
| issue number | second submit of the same product; local drops have no issue |

The pair **`pack_id` + `content_id`** is the split npm (`name` + integrity
hash), git (ref + commit) and Docker (`name:tag` + digest) already use. A
single-id scheme is not proposed.

#### 3.6 Current revision — one catalog slot

**`content_id` is equality/integrity only.** The official catalog has
**one live slot per `pack_id`**; whatever occupies that slot *is* current.
The player never sees 1.0 vs 1.2 of the same pack.

When two candidates compete for the same slot, the first rule that
decides wins:

1. **semver** of `pack.json` `version`, when both have a comparable
   version — higher wins. The catalog knowingly accepts that an inflated
   `version` can win **from the same origin** (§3.3 origin binding);
   triage warns, it does not block. `mep_lint` already rejects any
   non-`x.y.z` `version` (error), so "comparable" only fails for
   `hd-legacy`, which has none.
2. Else **`validated_at`** — later wins.
3. Else **issue number** — higher wins (later submission).

History may live in mep-meta / git; it is not a second catalog row and
not a player choice.

**Client**

- Compare the installed `content_id` (from `.mep-install.json`) to the
  catalog slot of the chosen `pack_id`. Different → reinstall, power
  cycle, toast ("Updated …"). Wrapper-only change (`source_sha256`
  changed, `content_id` did not) → do not reinstall. **This amends
  ADR-0138 §37**, whose trigger is `source.sha256`; the P.0 ADR records
  the amendment.
- **No automatic downgrade.** If the installed revision's semver is
  *greater* than the slot's (yank, rollback, author republished an older
  label), keep the install; Advanced may offer "use catalog revision" with
  confirmation. `hd-legacy` (no semver): a `content_id` difference against
  the slot still updates — there is no version number to protect.
- **Pack removed from the catalog** (no slot for that `pack_id` any
  more): keep the install, keep the per-ROM choice, no toast. It stays
  visible in Advanced; the player is not interrupted by a catalog
  decision.
- Reinstall preserves the user's per-container state (`DisabledPacks`,
  per-section flags — both keyed by container name today), since the
  container name does not change on an update.
- Sibling folder still always wins. No catalog write, no update, no
  picker while it is present.

Old trees may remain under `EnhancementPacks/.cache/`; they are not
listed in the picker and are not applied.

### 4. Applying a pack to a ROM

Already shipped, and this GUI must not bypass it:

1. Load ROM → No-Intro sha1 (ADR-0039).
2. `MepPackManager::LoadForRom` scans **sibling folder →
   `HdPacks/<Game>/` → `EnhancementPacks/`** (ADR-0049/0040/0120/0121).
3. A container matches when any `targets[].sha1` equals the ROM, or when
   it is a convention pack named like the ROM (MEP-v1 §2.1 rule 5).
4. Per section, the first pack in lexicographic container order wins,
   unless the user disabled that container. The sibling folder beats
   everything, in every section.
5. `patches[]` apply in place before the console reads the ROM
   (ADR-0044). Missing patch for this sha1 → skip the patch with a log
   line and a UI notice, still load the other sections.
6. Per-section toggles and enable/disable apply on the **next load /
   power cycle**, not live. Pack switch in the player stays a power
   cycle. Do not invent live texture/patch swap in this phase.

F6.4b (Part A, Phase 6) adds: fetch official MEI, match ROM
sha1, download within the host allow-list, sha256-verify the *source*, run
`MepRecipeInstaller`, write into the `<sibling>/mep` folder with a central
fallback (ADR-0147), then the scan above applies it. The
`AutoInstallCommunityPacks` toggle stays in F6.4b; the first-run consent it
once carried was removed by F6.7 (ADR-0146).

This PRD adds, on top of that scan:

- At install time, record `pack_id` + `content_id` in `.mep-install.json`
  next to `recipe_hash`, `source.sha256`, `deps`, `installed_at` (all
  already written by `MepRecipeInstaller::WriteInstallStamp`).
- On the next load of that ROM sha1, follow §3.6: new `content_id` on the
  chosen `pack_id`'s catalog slot → update (unless it would be a semver
  downgrade).
- Sibling folder still always wins. No catalog auto-install, no picker,
  while a sibling pack is present (artist at work).

### 5. Choosing among packs for the same ROM

Not a duplicate. Two `pack_id`s with the same ROM sha1 and different
`content_id`s are competing products (Contra80s vs another Contra HD
pack).

**Player mode**

- 0 catalog/local matches → play with Enhanced Audio + bootstrap only.
  If F6.4b is on and the catalog later gains a match, offer install as a
  toast; never stall the first frame.
- 1 `pack_id` (any number of revisions on disk or in history) → apply
  the catalog slot (§3.6). No picker. Never ask 1.0 vs 1.2.
- 2+ `pack_id`s and no stored choice for this ROM sha1 → the game starts
  **un-enhanced** (Enhanced Audio + bootstrap only) and the picker opens
  over it, once. Picking applies on the power cycle the picker triggers;
  dismissing plays un-enhanced this session and asks again next launch.
  The picker shows name, `author` (from `pack.json`; `hd-legacy` shows
  the submission title), `version` (or validation date + short
  `content_id` for `hd-legacy`), layers (textures / audio / synth /
  patch), license (or "not declared"), and catalog 👍 as **sort key**, not
  as auto-pick. The choice is remembered **per ROM sha1** — the No-Intro
  sha1 of the ROM as loaded, **before** any `patches[]` apply (§4 step 1
  precedes step 5) — a pack with three `targets[]` is chosen up to three
  times, once per ROM.
- Changing the choice later: overlay → current pack chip → picker.
  Applies on power cycle.
- Mixing section A from pack 1 with section B from pack 2 is **Advanced
  only** (today's Enhancement Packs window and per-section toggles).
  Player picks a whole pack.

**Advanced mode** keeps Tools → Enhancement Packs (MEP)… as it is: list
of matching containers, per-pack enable, per-section flags, lexicographic
default when nothing is chosen. When a per-ROM choice exists (P.3), it
overrides the lexicographic default in Advanced too, and the window shows
which container is the chosen one.

**Local + catalog.** A user-dropped container in `EnhancementPacks/`
whose `content_id` equals the pack already chosen for this ROM is the same
pack, not a second choice. A local container with a different
`content_id` and no `id` joins the picker as `local:<container-name>`
(§3.3 rule 4). The merge requires a populated identity: since P.1-local
(ADR-0206, 2026-09-17) a stamp-less local drop gets one from the identity
cache, and a drop that matches a stamped catalog container adopts its
`pack_id` — so the pair collapses into one choice instead of two. The merge only works for packs whose `content_id` is a
tree hash: the *output* folder of a recipe install copied elsewhere
without its `.mep-install.json` cannot be re-associated with the catalog
row (§3.2 — the recipe composite is never derived from the output tree);
it shows up as a `local:` entry. Documented non-goal (§7).

**Where 👍 comes from.** The client has no GitHub access. P.2 adds an
additive MEI field (`votes`, integer, MAY, non-normative like `issue`)
written by the catalog generator from the submission issue's 👍 count.
Clients ignore it for install decisions; the picker uses it only to sort.

### 6. Player chrome and Advanced GUI

One process. `PreferencesConfig.UiMode`: `Player` | `Advanced`.

| | Player (default on a fresh install) | Advanced |
|---|---|---|
| Menu bar | hidden | classic File / Game / Options / Tools / Debug / Help |
| Home (no ROM) | the existing recent-games grid (`RecentGamesViewModel`), always shown; drop a ROM anywhere; **P.7** adds a first-run welcome card (Load ROM CTA, shown once — recents are necessarily empty on a true first run) and, independently, a persistent "Continue: \<last game\>" entry whenever `GameEntries` is non-empty (not gated on first-run — see §8 P.7) | same grid, as today (`GameSelectionScreenMode` keeps its current meaning: what happens when a recent game is clicked; `Disabled` still hides the grid) |
| Playing | game fills the window; the overlay shortcut opens a thin overlay: Resume, Save/Load slot, Pack (if 2+ `pack_id`s, or to inspect the current one), Settings (video / audio / input essentials), Advanced GUI, Quit. **P.7** adds an "Enhancements" panel (quick toggles for Texture/Audio/WideScrn/HiRes/Overclock — no new Save/Load buttons, it reuses the overlay's existing Save/Load slot row) | current menus and windows |
| Overlay shortcut | a new configurable `EmulatorShortcut` (default Esc on keyboard; `KeyCombination` already accepts controller buttons, so a gamepad binding is a config choice, no new code). Default rule in Player: while a ROM runs, Esc opens the overlay and never leaves fullscreen; "Exit fullscreen" is an overlay item. P.4 implements that precedence inside the shortcut config, not by hard-coding | n/a |
| Gamepad navigation | the overlay and the pack picker are fully operable with D-pad/A/B (Avalonia focus navigation; no pointer required). Acceptance of P.4/P.5 includes a keyboard-arrows pass as proxy | n/a |
| Pack feedback | OSD toast on apply/update ("Applied Contra 80s — textures"); pack name on the overlay chip | Enhancement Packs window |
| Debugger, HD Pack Builder, Lua, netplay, movies, cheats, Record Music | not in the overlay; reachable only after switching to Advanced | unchanged |
| Existing `AutoHideMenu` | ignored in Player (no menu bar); left in Advanced preferences | unchanged |

Switching modes is instant and persisted. **Default rule:** when the
settings file already exists at startup and has no `UiMode` key, the
value is `Advanced`, so a current Mesen user is not stripped of Debug on
upgrade. When no settings file exists (fresh unzip), `UiMode` is `Player`.
The key is always written on first save, so the rule only ever runs once.

Do not fork ViewModels. Player hides chrome and routes a small overlay at
windows that already exist (open-ROM dialog, save slots, a reduced
settings page, the pack picker). Advanced is the current `MainMenuView`.

#### 6.1 Enhancements quick-toggle panel (P.7)

A new "Enhancements" entry sits next to Pack/Settings in the overlay,
opening a checkbox grid over existing config — same D-pad/A/B
accessibility bar as P.4/P.5. It does not add its own Save/Load buttons;
the overlay's existing Save/Load slot row already covers that.

| Toggle | Underlying config | Console coverage | Applies |
|---|---|---|---|
| Texture | `EnhancementPackConfig.EnableTextures` | all | needs ROM reload |
| Audio | `EnhancementPackConfig.EnableAudio` | all | needs ROM reload |
| WideScrn | `VideoConfig.AspectRatio` toggled between `Widescreen` (16:9 stretch, `Core/Shared/EmuSettings.cpp:521`) and the value it had before the toggle was turned on (restored, not hardcoded to `NoStretching`/`Auto`, so an Advanced-configured custom ratio survives) | all | immediate (renderer-only) |
| HiRes | `VideoConfig.VideoFilterType` toggled between one curated hi-res preset (candidate `HQ4x`) and the value it had before — same restore-not-clobber rule as WideScrn, so a filter already chosen in Advanced is never silently discarded | all | immediate (renderer-only) |
| Overclock | NES: `NesConfig.PpuExtraScanlinesBeforeNmi`/`PpuExtraScanlinesAfterNmi` (extra vblank scanlines, `Core/NES/NesPpu.cpp:188-190`); GB/GBA: `GameboyConfig`/`GbaConfig.OverclockScanlineCount`; all three toggled between `0` and one curated preset value. **SMS has no overclock knob today** — the toggle stays visible but disabled on SMS so the panel layout doesn't shift per console | NES, GB, GBA (not SMS) | needs reset |

Both enum-backed toggles (WideScrn, HiRes) store the pre-toggle value the
first time they are switched on, so switching off restores exactly what
the player (or Advanced GUI) had configured — never a hardcoded default.
This keeps the panel from drifting out of sync with Advanced's own
settings pages (§6 non-goal: do not fork settings state).

A **Border** toggle (a pack-declared decorative frame around the game
area) is the seventh entry here (implemented in Phase 8 F8.2, commit `6dc13f9e`,
gated by `EnhancementPackConfig.EnableBorder` and backed by `border.png` + optional
`border.json`). It lives beside the other enhancement toggles in `PlayerEnhancementsPanel`.

#### 6.2 Welcome card and "Continue" (P.7)

Two distinct, independent affordances — not one dialog wearing two hats:

- **Welcome card**: shown once, on the very first Player-mode boot (the
  same "settings file missing the `UiMode` key" signal `UiModeDefaultRule`
  already uses, §6). At that point recents are necessarily empty, so its
  only CTA is **"Load ROM"** plus one short line of orientation text. It
  never reappears once dismissed.
- **Continue card**: a persistent, always-shown-when-applicable entry on
  the Player home whenever `RecentGamesViewModel.GameEntries` is
  non-empty — **"Continue: \<most recent game's title\>"**, resuming that
  game. This is not gated on first-run; it is simply what the home shows
  once there is a game to return to, exactly like the rest of the recent-
  games grid it sits alongside.

### 7. Non-goals

- A second executable or a rewrite off Avalonia.
- Live swap of textures/patches without power cycle.
- Auto-picking the 👍 leader when two `pack_id`s match; 👍 only sorts
  the picker.
- A full pack browser (search, extra MEI URLs). Part A defers
  that until the catalog outgrows a list.
- Replacing F6.4b. This PRD consumes it.
- Hosting or committing pack bytes.
- Changing discovery precedence (sibling still wins).
- Product-level deduplication for packs without `id` hosted outside
  GitHub (§3.3 rule 3).
- Re-associating a recipe *output* folder copied without its
  `.mep-install.json` with its catalog row (§5).
- SNES / PCE / WonderSwan / ColecoVision chrome.
- A widescreen mode that reveals more of the playfield (extra per-console
  PPU/VDP decode) — the WideScrn toggle only stretches the existing 4:3
  frame to 16:9 (§6.1); "see more of the game" would be its own
  per-console engine ADR.
- The welcome card reappearing on every boot, or blocking the recent-
  games grid underneath it.

### 8. Slices

No slice is pending. P.0–P.7 implementation history is in Part A §3, and
P.1-local (the local-container identity requirement of §3.3 and ADR-0139/0140)
shipped 2026-09-17 with ADR-0206:

`EnhancementPacks/.cache/content-ids.json` holds one ADR-0139 `content_id` per
local container. The load reads it with a one-`stat` staleness check and never
walks or hashes a tree; a background refresh, off the load path, re-hashes only
the containers whose stat-manifest fingerprint moved. Acceptance met: identical
stamp-less folders/zips — including a zip whose pack root sits in a subfolder —
collapse onto one `content_id`; a container equal to a stamped one adopts its
`pack_id`; a changed nested payload invalidates that container alone and it
stays distinct. Evidence: Bloco G in `scripts/core_unit_tests.cpp` (cold/warm,
missing/corrupt cache, pruning, adoption) and
`scripts/p1_local_identity_check.py` against the built library. The
recipe-output-without-stamp non-goal remains (§7).

The nested-file question this section used to raise is answered in ADR-0206 §2
(a stat-manifest fingerprint, not the container's mtime), and its cache
trade-off is settled there rather than here. The per-ROM persisted choice
needed no change: it keys off `pack_id`, which the adoption step now supplies
for a local drop.

### 9. ADR map

| Topic | Status | Meaning |
|---|---|---|
| ADR-0139 — `content_id` algorithm (tree canonicalization, recipe composite, excluded files, `version` string excluded, two implementations + parity) | **accepted** (2026-08-28) | P.1 was built on it |
| ADR-0140 — `pack_id` (MEP `id` field; `owner/repo`; `issue-n`; `local:<container>`) + catalog uniqueness + origin binding (amended 2026-08-28) | **accepted** (2026-08-28) | P.2/P.3 were built on it. §3.6 is accepted product text — the ADR specifies enforcement |
| ADR-0141 — one live slot per `pack_id`; amends ADR-0138 §37 (client update trigger `source.sha256` → `content_id`); no auto-downgrade; removed slot keeps install | **accepted** (2026-08-28) | P.6 shipped 2026-08-29 on this trigger; ADR-0138 §37 now carries the in-place note pointing here (Part A §3, D5) |
| ADR-0143 — one slot per **game**: `pack_id` = origin × game; multi-game zip → N packs + N `pack:split` sibling issues | **accepted** (2026-08-29) | amends §3.3 rule 2 above and ADR-0140 source 2; eight of the nine LiQuiDz siblings were later removed from the catalog as audio-only NEA (Part A §3, D4 / ADR-0148) |
| Player chrome (`UiMode`, overlay contents, overlay shortcut, upgrade default Advanced) | not needed — P.4 shipped within what §6 states | P.4 |
| Enhancements quick-toggle panel + welcome/Continue cards (§6.1, §6.2) | not needed — UI over config that already exists | P.7 |
| ADR-0039/0040/0044/0049/0120/0121 | accepted | precedence and ROM hash-matching do not change |
| ADR-0138 (except §37 as above) | accepted | F6.4b is the network installer this shell consumes |

### 10. Risks

| Risk | Mitigation |
|---|---|
| `content_id` treated as the pack id | §3.5–§3.6; picker and preference key off `pack_id`; `content_id` is equality/integrity only |
| Catalog yank / republished older semver | no auto-downgrade (§3.6); Advanced confirms |
| Authors omit `id` / `version` (`hd-legacy`) | fallbacks in §3.3/§3.4; keyed by origin repo or issue; picker shows date + hash prefix |
| Two issues, same product, different `pack_id` fallbacks (non-GitHub hosts) | `content_id` still collapses byte-duplicates; remaining cases open the picker (safe default); documented non-goal until `id` is common |
| Inflated `version` wins the slot | accepted trade-off (§3.6 rule 1) **within one origin**; triage warns; no auto-downgrade protects installs |
| Third party claims an existing `pack_id` (`id` or `owner/repo` spoof) with a high `version` | origin binding (§3.3): different origin never occupies the slot; `pack:needs-review` for a human |
| Hashing local HD trees stalls the ROM load | Delivered by P.1-local (ADR-0206): the load reads `EnhancementPacks/.cache/content-ids.json` and does one `stat` per local container, never a walk or a hash; the background refresh pays the byte-reading cost and invalidates on a nested-file change (§3.3 rule 4) |
| Overlay unusable from the couch | overlay shortcut bindable to a controller button; overlay/picker navigable by D-pad (§6) |
| Recipe identity without dep bytes | composite in §3.2; computed at install time from the primary bytes, stored, not re-derived |
| `scripts/` and Core hashers drift | parity fixture in P.1, same pattern as ADR-0138 §39 |
| Local-pack identity ambiguous | `local:<container>` rule (§3.3 rule 4); `content_id` merges local ↔ catalog |
| Player chrome accidentally ships a second UI stack | P.4 acceptance: no new debugger/settings rewrite; hide and overlay only |
| Esc collides with existing shortcuts | configurable `EmulatorShortcut`; P.4 resolves in shortcut config |
| Scope collision with F6.4b | P.6 waits; P.3–P.5 work on local packs |

### 11. Open questions

None for P.0 — the four questions this section held (tree-hash
canonicalization; MEP `id` field now; duplicate-submit policy; silent
`local:` → catalog `pack_id` migration) were closed by ADR-0139/0140/0141
on 2026-08-28 (hash: ADR-0139; `id` as MEP v1.4 SHOULD, comment + close
the newer duplicate issue, silent migration: ADR-0140). New questions go
here only when a slice surfaces a trade-off §3–§6 do not settle.

### 12. References

- Parent roadmap: Part A (this document)
- Discovery / precedence: ADR-0040, ADR-0049, ADR-0120, ADR-0121
- ROM hash: ADR-0039, MEP-v1 §4
- Catalog / recipe / auto-install: ADR-0138 (§37–§39), MEI-v1 §2.2,
  MEP-recipe-v1
- Host allow-list: `scripts/pack_host_allowlist.json`
- Install stamp: `Core/Shared/EnhancementPacks/MepRecipeInstaller.cpp`
  (`WriteInstallStamp`)
- Current pack UI: `UI/ViewModels/EnhancementPacksViewModel.cs`,
  `UI/Config/EnhancementPackConfig.cs`
- Current chrome: `UI/Views/MainMenuView.axaml`,
  `UI/Windows/MainWindow.axaml`, `UI/ViewModels/RecentGamesViewModel.cs`
