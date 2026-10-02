# HD Pack toolchains: MesenCE (upstream) and MesenAI (this fork), side by side

Written 2026-09-16, from measurement rather than memory: the upstream tree was
read, the format's loader and builder were read here, and the MesenAI numbers
were taken from packs recorded on this machine. **Revised 2026-09-26** against
the code and the accepted ADRs: a row whose slice has since shipped says so and
cites it, and a **Better** cell moves only where the row was re-measured.

## Naming, and why the comparison is lopsided

| Name | What it is | Where it is |
| --- | --- | --- |
| **Mesen** (0.9.x) | The original, .NET/WinForms | `SourMesen/Mesen` — **archived** 2024-12 |
| **Mesen2** | The C++ rewrite | `SourMesen/Mesen2` — **archived**; its README points at `nesdev-org/MesenCE` |
| **MesenCE** | The community continuation; `mesen.ca` distributes it (2.2.1) | `nesdev-org/MesenCE` — **upstream of this repo** |
| **MesenAI** | This fork | `sbihaiko/MesenAI` (renamed from `sbihaiko/MesenCE` on 2026-09-16) |

`git remote -v` in this tree is the ground truth: `origin` is the fork,
`upstream` is `nesdev-org/MesenCE` with push disabled.

**The repository was renamed on 2026-09-16**; the old `sbihaiko/MesenCE` URLs
redirect. Project files, the git tag (`mesence-v0.1.0`), the catalog's
`"MesenCE validation"` strings and older `docs/` prose still read `MesenCE`
and are updated as they are touched — the published release **assets** do not:
they were renamed to `MesenAI-*`/`mesenai-tools-*` the same day (ADR-0202,
which amends ADR-0201 §4's list of what keeps the old name). In this document
"MesenCE" always means the upstream, and "MesenAI" this fork.

The consequence for everything below: **MesenAI did not reimplement the HD
Pack subsystem.** `Core/NES/HdPacks/` is inherited from upstream MesenCE, and
the HD Pack Builder window still ships here
(`UI/Windows/HdPackBuilderWindow.axaml`). So the honest comparison is the
**inherited base versus the layer built on top of it** — every row in the left
column is also in the right column unless the row says otherwise. A row is
marked *inherited* when MesenAI did nothing to it.

Two facts about the base that colour the rest:

- Upstream is in **maintenance**: the last functional change to its
  `HdPackBuilder.cpp` was 2024-07-15, the format has been frozen at version 109
  since 2023-12-29, and the most recent upstream commit touching it is
  `clang-format`.
- Upstream **documentation stopped in 2020** (`mesen.ca/docs/hdpacks.html` is
  stamped version 0.9.9, format v105). `<addition>`, `<fallback>`,
  `sppalette*` and `positionCheck*` exist in the loader and are documented
  nowhere. There is no `docs/` directory in the upstream repository at all.

## Capability table

| | MesenCE (upstream) | MesenAI (this fork) | Better |
| --- | --- | --- | --- |
| **Recording** | HD Pack Builder window: press Start, play the game to the end, press Stop | Same window, plus `headless_record`: no window, deterministic, ~3× real time | **MesenAI** |
| **Route as data** | — | `input=` frame-counted scripts (route sets for ten games ship under `scripts/stages/`), `state=` save states, `movie=` TAS, `cheat=` RAM pokes, `cdl=` code/data log | **MesenAI** |
| **Coverage measurement** | — | `artist_cover.py` (per image, per state, and which tiles only that state shows), `gameplay_probe.py` (gameplay vs menus), live % in the builder window | **MesenAI** |
| **"What did I miss" steering** | Play more and look | Contra measured 53.8 % → 58.9 % → 64.6 % across recordings | **MesenAI** |
| **Tile identity** | `(tileData, palette)`; CHR ROM by bank index, CHR RAM by the 16 bytes | *Inherited*, plus explicit per-console decisions for GB/GBC/SMS/GG (ADR-0036, ADR-0037) and a CHR RAM bank id that names the CHR state the tile was drawn from instead of the all-zero default (ADR-0232) | Upstream defines it; the fork extends it |
| **Picking a tile's key by hand** | Right-click → *Copy tile (HD pack format)* in the Tile, Tilemap and Sprite viewers — one tile per gesture, and no gesture emits a whole nametable in that format | *Inherited*, plus *Copy as MEP sheet cell* in the same three menus (F12.2, ADR-0215/0216): the same pick, emitted as the key a sheet sidecar carries | **Even** — upstream's gesture is the one the fork extends |
| **Vocabulary (which tiles are one thing)** | Recorded in `.NES` file order, deliberately. Sour: *"I tried to make the recorder smarter… it didn't work very well and was generally worse"* | Metatile vocabulary by mutual predictability, sprite figures (`sprNNN`), poses from the OAM stream at per-tile pixel offsets (ADR-0225), cycles/sequences/variants (a track survives one flickered frame, ADR-0226), fusion labels (ADR-0228) | **MesenAI** |
| **Ambiguity of a reused tile** | 13 condition types, all hand-written by the author | `spriteNearby` (spanning tree, ADR-0189) and `tileNearby` (directed co-occurrence, ADR-0190) emitted automatically, each with a mandatory bare twin | **MesenAI** |
| **Conditions deliberately refused** | All 13 available to a human author | Onto a `<tile>` rule, `frameRange`, `tileAtPosition` and `memoryCheckConstant` are not emitted (ADR-0189 §4) | **MesenCE** (a hand author can do what our tool will not) |
| **Sprite composition** | Nothing in the emulator; the community's answer is an external editor (`mkwong98/HDNes-Graphics-Pack-Editor`, CHR ROM only, wxWidgets) | `compose_editor.py`: MVVM tkinter over a host-free engine, poses as the unit, export as legal build input; and `mep_figure.py export`/`import` hands one figure to the artist's own editor as a single PNG and takes the paint back cell by cell (ADR-0209 Q2/Q3) | **MesenAI** |
| **Extra tiles drawn on match** | `<addition>` — composes sprites without spending the 8-per-scanline limit; 1987 uses in one community pack | Emitted from the composition editor's overflow layer (F12.5, ADR-0196): anchored on the pose's root cell, target key proved unmatched against the ROM's CHR, linted | **Even** — upstream's format, authored by tool here |
| **A recorded screen** | A `<background>` capture draws its whole bitmap on every frame its conditions match, and a behind-background sprite over colour-0 canvas disappears under it | Two tags the recorder writes and the loader honours, and a reader that does not know them skips them: a per-cell record (`<bgCellRecord>`, bound to the `<background>` line above it), so a capture draws a cell only where the live key equals the key it was captured with (ADR-0236, F14.11 — the 30-ROM library re-recorded: stale frames 2 995 → 618, 219 of 219 captures kept), and `<bgPreservesBehindBgSprites>`, so a behind-background sprite survives the screen (ADR-0224, F12.15). A pack without either renders as before | **MesenAI** |
| **Writing `hires.txt`** | By hand, or by the author's own generator (the most prolific author ships a 9.9 MB, 34-sheet Excel workbook) | `mep_build.py build` regenerates it from sheets; the guide forbids hand-editing | **MesenAI** |
| **File-level duplicate bitmaps (CHR ROM)** | `automaticFallbackTiles` exists in the format and the builder never set it | Set on every CHR ROM recording (ADR-0195) | **MesenAI** |
| **Validation** | None. No linter, no spec that matches the code | `mep_lint.py`, versioned MEP-v1, canonical `content_id`, sha256 errata, pack CI | **MesenAI** |
| **Interop with community packs** | The packs are written for the format upstream defines | Textures and BPS match optimistically; **IPS does not relax** (ADR-0145). A legacy pack is imported into an editable project (F12.7, ADR-0198 §1) — including one keyed against an IPS-patched ROM, by applying the patch in memory (F12.17, ADR-0198 §3), which puts the project in the patched ROM's namespace | **Even** — upstream defines the namespace; the fork imports into it |
| **Painting, end to end** | Edit the recorded PNGs in place and reload | Kit → paint PNG → `build` → `lint`, with the reload in place too (F12.3) and the build naming the rule each painted cell produced (#511, #524); a layered `.ora` beside every surface (F12.11, ADR-0220), every palette a shape was drawn in reaching a sheet — a fold riding on the cell, a colourway in a variant cell beside it (F14.9, ADR-0230) — and an unpainted cell keeping the recorded rule (ADR-0231) | **MesenAI** — a painted cell was measured reaching the running game pixel-exact (F14.1); the open row is a person's own sitting (F14.8), not the pipeline |
| **Staying inside the emulator** | One window. Start, play, stop, edit, see it | A checkout — or the tools zip, which is the `mesence-v0.1.0` tag's own snapshot (published 2026-09-15): 20 modules, against the longer list `scripts/tools-zip-manifest.txt` holds today, so anything newer (`mep_figure.py` among them) is checkout-only, and no CI leg rebuilds it (`build.yml` publishes binaries) — plus a headless binary driven by flags, four generators, a copy step, and a *see it in the game* step of its own — a screenshot pass, or the in-place reload of F12.3 | **MesenCE** |
| **Vocabulary scale** | An author's shipped Metroid pack, re-measured 2026-09-17: 67 images, 150 199 tile rules, **8 401 keys** (distinct `tileData`+`palette`), 260 146 lines | A 60-second recording: 2211 keys. The tools run at his scale — `mep_build` 2.70 s and `mep_lint` 0.59 s on a 300 000-line project — but the recording vocabulary is still ours to close ([log](validation/f12.1-scale-and-load-2026-09-17.md)) | **MesenCE** |

## Where the numbers came from

| Claim | Measurement |
| --- | --- |
| Palette variants barely inflate the mapping | Contra 1776 `<tile>` lines / 1694 bitmaps = 1.05. Mega Man 3: 8581 / 8192 = 1.05 |
| The vocabulary is shared, not repeated | Contra stage 3 boss: 517 poses, 9868 tile placements, **195 distinct nodes** — 50.6× reuse |
| CHR ROM duplicates bitmaps | Mega Man 3 (USA): 8192 indices, **6663 distinct bitmaps**, 1529 redundant (18.7 %) |
| The fallback option is worth setting | Same pack, 1529 redundant lines deleted: 625 920 vs 599 040 matched bg tiles over ~60 frames, the only difference being the `<options>` line |
| The artist's real competitor is a spreadsheet | The Metroid pack ships `SourceWorkForComplexHires_TXTCoding.xlsx`: 9.9 MB, 34 sheets, 113 855 declared rows, of which 97 399 carry a `hires.txt` directive |
| His bottleneck is not drawing | Every structure in that workbook is a device for emitting rule text at volume: comma columns, concatenation columns, coordinate steppers, frame counters spliced into condition names |

Full method and raw evidence: `docs/validation/metroid-artist-workflow-evidence.md`,
`docs/validation/c5-fable-artist-run-zelda-2026-09-14.md`,
`docs/validation/c5-fable-artist-run-mega-man-3-2026-09-14.md`,
`docs/validation/tilenearby-evidence-study.md`.

## Gaps this table names

Three rows still mark **MesenCE**, and they are not a scoreboard to zero — the
Core, the format and the builder are upstream's, and the competitor the artist
evidence actually measured is a spreadsheet, not an emulator. They are the
places where a hand author is still better served than by the layer built
here. Phase 12 (Part A §4, **Paint loop and hand-authored conditions**, opened
2026-09-16) delivered every slice it named; the row it left open, F12.11's
human stop condition, is carried by Phase 14, the live phase. What each row
stands on now:

| Row | Slice | Where it stands |
| --- | --- | --- |
| Vocabulary scale | F12.1 — delivered 2026-09-17: the pack is measured and every count now carries its definition ([log](validation/f12.1-scale-and-load-2026-09-17.md)) | still **MesenCE**: what remains open is the recording's vocabulary, not the tools' speed |
| Conditions deliberately refused | F12.6a / F12.6b — delivered 2026-09-19: a condition you write by hand is replayed over every retained frame of every route ([log](validation/f12.6a-lint-authored-conditions-2026-09-19.md)), and `memoryCheckConstant` inside internal RAM is a verdict ([log](validation/f12.6b-recorder-retains-internal-ram-2026-09-19.md)) | still **MesenCE**: onto a `<tile>` rule the tool emits none of `frameRange`, `tileAtPosition`, `memoryCheckConstant` (ADR-0189 §4, kept by ADR-0197) — `tileAtPosition` is the one it does write, on the `<background>` gates of its own captures (ADR-0217/0218, `FinalizeScreenAnchors`) |
| Staying inside the emulator | F12.3 (the reload, no ROM reopen) and F12.4 (the asset-name template) — both delivered 2026-09-19 ([log](validation/f12.3-reload-repainted-images-2026-09-19.md), [log](validation/f12.4-asset-name-template-2026-09-19.md)) | still **MesenCE**: the recording, the kit and the build all run outside the game window |
| Picking a tile's key by hand | F12.2 — delivered 2026-09-19: two fresh cold readers pasted a cell and painted it without opening a `hires.txt` ([log](validation/f12.2-fable-panel-2026-09-19.md)), on the rules of ADR-0215/0216 | **Even**, as the capability table says |
| Painting, end to end | F12.11 (the layered `.ora`, ADR-0220) landed 2026-09-22, and F14.1 met its pixel-exact stop condition 2026-09-23 ([log](validation/f14.1-painted-round-trip-2026-09-23.md)) | **MesenAI** — a person's own sitting is the open row (F14.8) |
| Interop with community packs | F12.7 — delivered 2026-09-19: 0 differing keys on three community packs ([log](validation/f12.7-legacy-pack-import-2026-09-19.md)); F12.17 — delivered 2026-09-23: a pack keyed against an IPS-patched ROM imports against the patched ROM ([log](validation/adr0198-s3-patched-rom-import-2026-09-22.md)) | **Even** — the namespace is upstream's (ADR-0198 §3), and ADR-0145's IPS refusal stands |
| Extra tiles drawn on match | F12.5 — delivered 2026-09-19: the overflow layer emits `<addition>`, proved pixel-exact on one pose each of Mega Man 3 (CHR ROM) and Contra (CHR RAM) ([log](validation/f12.5-addition-overflow-layer-2026-09-19.md)) | **Even** — upstream's format, authored by tool here (ADR-0196) |
| A recorded screen | F12.15 / ADR-0224 — delivered 2026-09-22 ([log](validation/f12.15-behind-bg-sprites-2026-09-22.md)); F14.11 / ADR-0236 — delivered 2026-09-25 ([log](validation/f1411-capture-cell-guard-2026-09-25.md)) | **MesenAI** — neither tag exists in the inherited format |
| Tile identity | none — it is the inherited contract | — |

A **Better** cell moved only where the row was re-measured, and the log under
`docs/validation/` is what that re-measurement cites. Two things the phase
deliberately did not do: emit any key a recording did not observe (ADR-0183
§3, with ADR-0196 §3's one confined exception), or claim that recording
coverage is solved — that criterion stays with ADR-0182/0184/0185 and F9.25.

## "Mapping without playing", precisely

Worth stating carefully, because the short version oversells it. MesenAI does
not map without the game running. What it removed is **the human at the
controls and the emulator's window** — and, in four cases, the run itself.

**Driven, not hand-played.** `headless_record` runs the console with no window
and takes its input from data:

- `input=<script>` — a text route of frame-counted button states
  (`<n>f <buttons>`). Hand-written, or generated by `write_play_scripts.py`.
- `state=<file.mss>` — start from a save state, so a recording does not spend
  its budget on the title screen. The guide's `mint` step exists for this: one
  short run produces the state, every later run starts inside the level.
- `movie=<file.bk2>` — a published TAS drives the pad (ADR-0185);
  `fm2_to_bk2.py` converts FCEUX `.fm2`, which the Core cannot read directly.
- `cheat=AAAA:VV` — RAM writes only (ADR-0184), to reach a state no route
  reaches. A cheated run feeds the background surfaces and never the sprites.
- `cdl=<file.cdl>` — a Code/Data Logger map of what the run executed, which
  finds art the PPU fetched but never drew.

The result is deterministic in **emulated frames**, so the same route over the
same state produces the same pack, and coverage becomes something you measure
and steer on rather than something you eyeball. All of it is built on the
inherited builder — upstream's recorder, driven by data instead of by hands —
and what it writes has grown past what upstream's builder ever wrote (the tags
of ADR-0224 and ADR-0236, the labels of ADR-0209 Q1).

**Genuinely play-free.** Four paths reach art with no gameplay at all:

- The **static ROM export** (`romtiles`, ADR-0043): every CHR ROM tile becomes
  a palette-agnostic `defaultTile` entry, with no gameplay at all — but it is
  not run-free. `romtiles` is a `scripts/headless_record` flag, and the export
  reads CHR through the mapper (`NesConsole::ExportRomTilesHdPack`), so the
  console still boots and the run stops on its first frame
  (`scripts/headless_record <rom> 1 <prefix> romtiles`). The path that touches
  no console at all is the kit below. On GB/SMS and on CHR RAM it is a
  heuristic scan of the file and finds uncompressed tiles only — 12 of 26
  on-screen tiles in the F1 test ROM are built at run time and stay invisible
  to it. The UI says so.
- The **CHR kit's ROM fill**, whose degenerate case needs no pack at all:
  `artist_chr_kit.py --static` takes a *missing* pack folder and lays out one
  page per CHR ROM bank from `--rom` alone, every cell `fill` and
  `seen: false` (ADR-0219, F12.9: 512 rules for SMB in 0.51 s, 8 192 for Mega
  Man 3 in 5.69 s, and a CHR RAM game is refused). Filling a *recorded* kit
  from the cartridge measured Metroid 82 % complete.
- **A third-party key index read as facts** (`mep_import.py index`,
  ADR-0210 §3): on a CHR RAM game every key the recording does not hold is
  rendered from its own 16 pattern bytes into a cell marked `seen: false`, and
  no PNG of that pack is ever opened — the only static source of *shape* for
  those games (Contra80s contributed 2 583 cells). The index is refused unless
  its declared dump matches the recording's.
- **Donation across recordings** of the same ROM: one recording's CHR page
  donates cells to another's, refused unless the `supportedRom` SHA1 matches
  **and both runs know the bank** — donors are paired by CHR bank id, and a CHR
  RAM pack recorded before ADR-0232 has none recorded, so it donates nothing
  (ADR-0232 §6; `artist_chr_kit.py`'s `attach_donors`).

Everything else is a recording. The honest one-line version: **MesenAI replaced
"play the game from start to finish" with "write down a route, measure what it
covered, and write a better one" — the play still happened, but it stopped
being the thing that decides whether the map is complete.**

## What not to claim

- **Not "a different emulator".** MesenAI is a fork of MesenCE. The Core, the
  format and the builder are upstream's work, and a MesenCE HD pack loads here
  unchanged. Where this table says a row is inherited, the credit is upstream's.
- **Not "automatic mapping".** Every inference is marked, and anything that
  would change what a rebuilt pack *renders* is only emitted when the key it
  carries was actually observed (ADR-0183 §3). Names come from data or a human,
  never from a generator.
- **Not "beats upstream at its own job".** For one-off surgical work inside a
  running game — pick a tile, get its key, paint that one thing — the inherited
  gesture is still the shortest path; the fork puts a second copy beside it, not
  a faster one.
- **Not "the community author should switch".** He patches the ROM to convert
  CHR RAM into CHR ROM because that buys him a short stable tile id per graphic,
  which is what his spreadsheet manipulates. That bet is the opposite of his,
  and the tools still assume the stock ROM — though the namespace gap behind it
  is no longer silent: `artist_cover.py` refuses the comparison instead of
  printing 0 % (#225, closed 2026-09-14 as a diagnostic fix; ADR-0198 Context),
  and a pack keyed against an IPS-patched ROM imports into the patched ROM's
  namespace (F12.17, ADR-0198 §3).
