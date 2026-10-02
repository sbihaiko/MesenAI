# ADR-0243: A Remaster project is the ROM's enhancement folder, and only the bootstrap recorder feeds it

- Status: accepted (2026-10-02). The user picked *"Aceito"* after answering Q1–Q3 (below). The ADR was requested verbatim: *"sim, aplique o grupo 1 e escreva a ADR do projeto"*, after the Fable audit of PRD Part B §13 found that W-R0–W-R5 assume a "project" and a recorder that do not exist as one unit. Listed as slice **F12.20** (PRD Part A, Phase 12). **Implemented 2026-10-02** under the user's go-ahead, quoted verbatim: *"sim, pode seguir. depois que tudo estiver no main, pode implementar usando paralelismo de tudo que puder"* and *"pode implementar em paralelo tudo que puder"* (2026-10-02). Unit tests cover the decision: `scripts/core_unit_tests.cpp` (recording ids, the loader playing the newest `rec-NNN`, the decline rule refusing a foreign pack and exempting the project's own `mep/` and recordings, the `project.json` round trip), `scripts/test_mep_project.py` (bare `auto/textures` as `rec-001`, the derived manifest, `mep_build.py` keying from `auto/rec-NNN/`, the kit plan) and `UI.Tests/Config/BootstrapRecordingDefaultTests.cs` (Q3's defaults and the one upgrade notice). Stop rule measured on Castlevania with two recordings: the project kit equals the per-recording kits plus the union pattern pages, 486 files byte for byte (PRD Part A §3, F12.20). Two choices the text left open: when several recordings hold a section, the loader plays and `mep_build.py` keys from the newest one; a human layer at the sibling root (the pre-ADR-0147 layout) still counts as someone else's pack and declines. The GUI (W-R0–W-R2) is not part of F12.20.
- Date: 2026-10-02
- Related: ADR-0194 (figures, scenery and maps stay per recording; the union is the pattern pages), ADR-0241 (Play / Remaster / Share, `proposed`), PRD Part B §13 (W-R0, W-R1, W-R2, W-R6), ADR-0049 (the sibling folder is the pack; discovery order), ADR-0050 (bootstrap screens), ADR-0147 (`auto/` machine layer, `mep/` human layer), ADR-0169 §4 (in-app live recorder slot), ADR-0183 §1 (kits in `kit/`, never inside the recording), ADR-0198 (import a legacy pack as a MEP project), ADR-0165 (pack tools are external scripts), ADR-0185 (TAS movie as input), ADR-0201 (data folder), ADR-0239 (coverage as a union of recordings), MEP-v1 §2.1
- Supersedes / amends: amends ADR-0049's bootstrap default (Q3: off; Play stops recording by itself) and ADR-0169 §4's statement that the live recorder slot "feeds the artist kit" (it does not; see Context), and narrows the bootstrap's decline rule for the project's own `mep/` (Decision 3). ADR-0049's discovery order and ADR-0147's layering are unchanged.

## Context

The redesign's Remaster profile (W-R1) is built on a project: recordings in
zone ①, the artist kit in zone ②, a build in zone ③. Today the app has
three output folders and three recorders, and none of them is called a
project:

| Recorder | Where it writes | What it produces | Feeds the artist kit? |
|---|---|---|---|
| HD Pack Builder window (`HdPackBuilderViewModel`) | `<Home>/HdPacks/<RomName>/` | flat `hires.txt` and `chr/` PNGs | **No.** `sheets/`, `poses.json` and `adjacency.json` are gated on `_captureScreens`, which only the bootstrap sets (`NesConsole::EnableBootstrapScreenCapture`, called only from `MepPackManager::StartBootstrapIfNeeded`) |
| Live recorder (ADR-0169 §4, `LiveRecordingSession`) | the single slot `<Home>/LiveRecording`, cleared on every start | `frame.ppm`, `sprites.json`, CHR and nametable dumps, `status.json` | **No.** Only `record_viewer.py` reads it. No kit, compose or `mep_*` script does, despite ADR-0169's and `MainMenuViewModel`'s comment |
| Bootstrap (ADR-0049/0050, in-app on ROM load, or `headless_record bootstrap`) | `<dir>/<Game>/auto/`, or `EnhancementPacks/<Game>/auto/` when the ROM folder is read-only | `auto/textures/` (`hires.txt`, `chr/`, `backgrounds/`, `sheets/` with `spr*`/`obj*`/`map-*`, `poses.json`, `adjacency.json`, sidecars), `auto/audio/fingerprints.json`, a `.bootstrap` stamp | **Yes, and it is the only one.** `artist_kit.py`, `compose_engine.Pack` and `mep_figure.py` need `textures/sheets/` with `poses.json`/`adjacency.json`. Sheets are NES only |

The human side already has a home. `<dir>/<Game>/mep/` is the authored MEP
pack, and it wins over `auto/` per entry (ADR-0147). `mep_build.py` already
calls it a "project folder". Kits go to `kit/` beside the pack (ADR-0183 §1).

Two facts shape what a project can be:

- **The bootstrap records only what is missing, into fixed folders.**
  Today `StartBootstrapIfNeeded` declines only when neither section is
  needed: no textures (a textures pack, or an existing
  `HdPacks/<rom>/hires.txt`, covers them) and no audio (NES only). It then
  logs "delete that pack (and the sibling `.bootstrap` stamp) or record
  into an empty directory". Otherwise it records the missing sections
  under `auto/textures/` and `auto/audio/`, even when another pack covers
  the other section. A second recording of the same section has nowhere
  to go: there is no `auto/rec-NNN/` yet (that is this ADR's Decision). `scripts/record_stages.sh` gives every stage its own
  directory for that reason. A project with "2 recordings" (W-R1) is
  therefore two folders today, measured as a union (ADR-0239).
- **It runs by itself.** With `EnableMepPacks` and
  `BootstrapEnhancementFolder` on (both default `true`), any NES/GB/SMS
  load with nothing dressing it starts recording. Nobody presses Record.

Non-goals:

- No new pack format. A project is folders that already exist plus, at
  most, one small manifest (Q2).
- No change to discovery order (ADR-0049) or to the `auto/`-versus-`mep/`
  precedence (ADR-0147).
- The HD Pack Builder window is not removed. It stays in Tools ⋯ as the
  legacy authoring path.

## Decision

1. **A Remaster project is the ROM's enhancement folder** — the ADR-0049
   sibling `<dir>/<Game>/`, or `EnhancementPacks/<Game>/` when the ROM
   folder is read-only:
   - `auto/`: the machine layer, every recording (Q1 says how several are
     kept);
   - `mep/`: the human layer, the pack the artist edits and builds;
   - `kit/`: the ADR-0183 projections the PAINT zone shows;
   - `.bootstrap`: the stamp that ties the folder to one ROM (SHA-1).

   W-R0's *Open a project folder…* accepts exactly this shape. A legacy
   `HdPacks/<RomName>/` pack or a finished pack goes through W-R6, ADR-0198's
   import, which writes a project next to the user's copy.
2. **Zone ①'s *Record While I Play* is the bootstrap recorder, started and
   stopped by the user** — `StartRecordHdPack` with
   `EnableBootstrapScreenCapture`, the same path `headless_record bootstrap`
   drives. It is not the HD Pack Builder window and not the live recorder.
   *Record from a TAS Movie…* is `headless_record bootstrap movie=…`
   writing into the same project. *Let the AI Play…* (ADR-0242) replays its
   script the same way.
3. **Recording into a project is allowed while the project's own `mep/`
   dresses the ROM.** The bootstrap keeps declining for any *other* pack
   (a community or loose pack), which protects someone else's art from
   being shadowed (issue #142). Its own human layer does not count as "a
   pack someone already has". Without this, the second recording of any
   project would be silently refused.
4. **The live recorder stays a viewer feed.** ADR-0169 §4's "feeds the
   artist kit" is corrected to "feeds `record_viewer.py`". Its Record/Stop
   stays in Tools ⋯ for diagnosis. Remaster does not show it.
5. **Console scope follows the data.** Sheets are NES only, so on GB/GBC/SMS
   Remaster shows *Record* enabled (tiles are recorded) but the PAINT zone
   disabled with "Figures and pages are NES only for now" (rule 4). GBA has
   no HD path: Remaster is disabled with its reason. This answers §13.8 Q6
   for the first slice.
6. **Feasibility gates, one each, shown as W-R0b banners:**
   - Python 3 for the kit and the build (already drawn);
   - `headless_record` for TAS and AI recording. It ships only in the
     macOS arm64 zip today (`release_macos.sh`), so elsewhere those buttons
     are disabled with "Not in this build".

## Answers to the open questions (user, 2026-10-02)

The user picked the recommended option of each question in a three-way
choice, verbatim labels: *"Uma pasta por gravação"*, *"Sim, project.json"*,
*"Só no Remaster"*.

- **Q1 — one folder per recording.** Each recording is `auto/rec-NNN/`, a
  complete bootstrap output (`textures/`, `audio/`).
  - Pattern pages are the union across recordings; figures, scenery and
    maps stay per recording (ADR-0194, unchanged).
  - Coverage is measured as a union (ADR-0239).
  - A recording can be deleted alone.
  - `mep_build.py` and the kit generators read `auto/rec-*/` instead of
    `auto/textures/`. A project with a bare `auto/textures/` (every
    folder recorded before this ADR) reads as one recording, `rec-001`,
    without being moved.
- **Q2 — `project.json` at the project root.**
  - It holds a display name and one entry per recording: `id`
    (`rec-NNN`), `recordedAt`, `source` (`play`, `tas`, `ai`, `script`),
    `durationSeconds` and an optional `note`.
  - The ROM identity stays in `.bootstrap`, which is never duplicated.
  - The file is machine-written. Hosts ignore it, like every authoring
    file under MEP-v1 §2.1.
  - A project without one is valid, and its list is derived from the
    folder names.
- **Q3 — recording happens only in Remaster.** Play no longer bootstraps
  a ROM on load. This **amends ADR-0049**, whose setting *"Bootstrap
  enhancement folder for played games"* defaults to on:
  - In Play the bootstrap stops running by itself; the setting stays, in
    Tools ⋯, defaulting to **off** for new installs.
  - An install that has it on keeps it on, and its upgrade says so once.
  - What Play loses is the automatic xBRZ 4× first draft. The same look is
    one choice in Look › Pixels (W-P10), without writing anything to disk.

## Consequences

- W-R0–W-R5 get a single source: the project folder. The W-R0 note
  "nothing new on disk" holds for Decision 1, but not for Q1(a) or Q2's
  manifest. The PRD notes are corrected to say so.
- The bootstrap's decline rule gains an exception (Decision 3). The test
  that guards #142 must keep failing for a foreign pack and pass for the
  project's own `mep/`.
- The HD Pack Builder window and the project now produce different things
  under different names. Tools ⋯ labels the window "HD Pack Builder
  (classic)" so a user does not mistake its output for a project.
- ADR-0169 and the `MainMenuViewModel` comment carry a false sentence
  until this is accepted and they are amended.
