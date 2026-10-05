# ADR-0185: A published TAS movie is an admissible recording driver when it matches our ROM, and it is converted rather than trusted

- Status: accepted (2026-09-14, at the user's direction to decide without asking; implemented the same day as `scripts/fm2_to_bk2.py` and `scripts/headless_record`'s `movie=` flag)
- Date: 2026-09-14
- Related: ADR-0184, ADR-0183, ADR-0182, ADR-0159, ADR-0157, F9.22

## Decision

### 1. A movie drives a recording; it never becomes evidence itself

A movie is input, exactly like `scripts/stages/*.txt`. Everything ADR-0183 says about evidence and inference is unchanged, because a movie changes only *how far the game gets* — the tiles, sprites and nametables recorded are the game's own. This is the decisive difference from ADR-0184's hazard 2: a cheat can make the game draw a state it almost never shows, a movie cannot. **A movie-driven run is a clean run**, admissible for all four surfaces, with no two-pass split.

### 2. Conversion happens outside the Core, and the Core keeps its two formats

`.fm2` is converted to a `.bk2`-shaped zip by `scripts/fm2_to_bk2.py`. The Core gains no format. The conversion is a positional permutation — fm2's gamepad mnemonic order `RLDUTSBA` to Mesen's `UDLRSsBA` — plus the commands bitfield mapped onto the leading `RP` console column, derived in code from the two named orders rather than written as a literal tuple, because a silently wrong permutation produces a movie that plays and desyncs.

The converter **refuses, does not warn**, and names the cause: a `savestate` header key (the Core has no `CoreState` handling, so the movie would start from nowhere), `FDS 1`, `binary 1`, `fourscore 1`, a `romChecksum` that does not match, a malformed port field. `palFlag 1` is reported, not refused.

**The ROM check hashes the post-header bytes.** fm2's `romChecksum` is the MD5 of the ROM *without* its 16-byte iNES header; hashing the whole file compares the right ROM against the wrong number and rejects it.

### 3. The harness must not be able to silently record nothing

`MovieManager` ignores a file it does not recognise: no player, no message, `MoviePlay` returns void, and a run started that way records the title screen for its whole budget. So `scripts/headless_record`'s `movie=` flag polls `MoviePlaying()` immediately after `MoviePlay` and **fails the run** when it is false, naming the file and the two container shapes the Core accepts — a zip holding `GameSettings.txt` is a Mesen `.mmo`, one holding `Input Log.txt` is a BizHawk `.bk2`. `movie=` is mutually exclusive with `input=` and `state=`, refused when combined: a movie carries its own start state and poll counter. `MovieManager` detects the format by content, and `MoviePlay`/`MovieStop`/`MoviePlaying` are already exported; there is **no `.fm2` reader** (Mesen 1 accepted them, Mesen 2 dropped it).

### 4. Synchronization is measured against the movie-less run and gated (amended 2026-09-14, issue #201)

#### 4.1 No in-band check is trusted; settings clobbering differs by format

`MesenMovie` never compares the recorded ROM SHA-1 it stores, and `BizHawkMovie::ApplySettings` is a stub, so a `.bk2`'s `SyncSettings.json` (region, power-on RAM pattern, board properties) is ignored entirely — a desynced movie does not error, it plays a different game.

`MesenMovie::ApplySettings` overwrites the live settings with the movie's own over `EmuSettings::Serialize`'s subset only — controller types, `RamPowerOnState`, region, console type and the per-console quirk flags — restoring them when it stops; the palette, channel volumes, `EmulationSpeed`, the video filter and the HD/MEP flags survive. `BizHawkMovie::ApplySettings` is a stub returning true, so a `.bk2` clobbers nothing. The subset is the wrong small: `RamPowerOnState` and region are the two settings a desync turns on, so a `.mmo` is the more dangerous container for a harness that configures itself.

#### 4.2 The gate: three rules over a synctrace

`Core/Shared/MovieSyncGate.{h,cpp}` — host-free, no Emulator, no filesystem, unit-tested as Bloco T of `scripts/core_unit_tests.cpp`. Every `hdpack` run of `scripts/headless_record.cpp` writes `<prefix>-synctrace.csv`: one row per emulated second holding the builder's own coverage counters (`GetHdPackCoverageReport`, F5.4d), whether the movie player was still running, and one byte per declared watch. A movie-less run mints the baseline the next movie-driven run is judged against, sampled one per 60 frames. Three rules read those traces:

1. **A declared RAM invariant** — `sync-watch=AAAA:<rule>[=<n>][:<label>]`, repeatable, rules `never-decreases`, `never-increases`, `never-below=<n>`, `never-equals=<n>`, checked only while the movie is driving the pad. Address below `$0800`, read through a new `HeadlessReadNesRam` export off `NesMemoryManager::GetInternalRam()` — not `DebugRead`, which overlays the CheatManager and would report a cheated value. **This is the only rule that fails a run**, and it is the one that catches issue #201: "The emulator diverged" is not observable from inside the emulator, its consequence in the game is. Falsifiable by construction: an address is only trustworthy once a positive control has been run against it.
2. **Movie exhaustion** — `sync-movie-frames=<n>`, the frame the movie's own row count says its input runs dry on (rows + 2 for a `.bk2`). Exact and free; blind to issue #201, which plays every row.
3. **The comparison against the movie-less baseline, asked at every sampled frame instead of only the last** — `sync-baseline=<trace.csv>`, the same two runs and counter read 600 times. Reported as `prefix-stall` (windows in which the run drew nothing new) and `lead-peaked-early` (not read before frame 3600, because a two-frame start lag reads as a lead). **Never fatal**; rule 1 decides.

**The counter is distinct tile shapes, not `(tileData, palette)` keys** — a correction to the rule as first written: on both games the movie-less attract loop holds MORE keys than a good 600 s movie-driven run, so "strictly more keys than the movie-less run" fails a run that is in sync throughout.

#### 4.3 Movie mode gives up ADR-0157's in-frame stop

A run is a number of emulated frames, normally stopped from inside the frame by `HeadlessInputProvider`. While a movie plays that never fires: `BaseControlManager::UpdateInputState` stops at the first provider whose `SetInput` returns true, `MesenMovie::SetInput` always does, and the movie's provider registers on `AfterInitConsole` while `HeadlessInputProvider` re-registers on the later `GameLoaded`, so the movie is permanently ahead. The harness falls back to a host-side `Pause()` from the poll loop. Measured: a 300-frame budget parks at 301 with the guard and at 602 without it (602 is the frame the movie's input ran out). A caller that needs frame-exact stops must not use `movie=`.

#### 4.4 A converted movie needs its power-on row dropped

`.bk2` needs 0 and `.fm2` needs -1 — a property of the pair of emulators, not the movie. Our Core spends a poll before the first frame runs (after the movie's `PowerCycle`, `Emulator::LoadRom` calls `UpdateInputState()`, and the NES then polls again at `InputScanline` 241 every frame) while FCEUX applies its first log row to the first frame it emulates, so emitting the rows one for one delivers every input one frame late. The converter therefore drops the fm2's power-on row and refuses by name if that row holds a button or a command bit. `FM2_POWER_ON_ROWS = 0` regenerates the Zelda movie stuck on "REGISTER YOUR NAME", where the drop reaches the dungeon. Any future converter must establish its own offset by bisection against a visible in-game event.

### 5. Attribution travels with the material

CC BY 2.0 requires it. A kit fragment built from a movie-driven run records, in `notes[]`: the publication URL, the author name as the publication gives it, and the movie file name. The converter writes the same provenance into the output's `Comments.txt`, together with the sync-relevant fm2 header values it did **not** carry across — `NewPPU`, `RAMInitOption`, `RAMInitSeed`, `palFlag` — because those are what a desync investigation needs. No movie file is committed; they live under `.cache/tas/`, unversioned, fetched from their publication page.

### 6. Per game, the movie is used only when it beats the script

1. **Zelda 1 "all items"** (4767M) — our exact ROM, full game, deliberately exhaustive; the best coverage case and the reason this ADR exists.
2. **Castlevania** (4840M) — USA PRG0, already a `.bk2` from BizHawk 2.8, so no converter; we do not currently hold that ROM.
3. **Mega Man 3** (2439M) — targets `Rockman 3` (Japan); same mismatch class as Contra.
4. **Contra** — no usable path: every modern publication runs `Contra (Japan)`, a VRC2 cartridge that will not sync on `Contra (USA)`; the only USA-ROM runs are 2005-era Famtasia `.fmv` from a much less accurate emulator. Keep scripting input.
5. **Excitebike** (1348M) — races the built-in tracks only, never design mode; a TAS buys little.

**The branch matters more than the game**: prefer "all items", "100%", "warpless" and pacifist/low% variants; a "game end glitch" run finishes in three minutes and records almost nothing.

## Context

Every surface of the artist kit is capped by how far the recording gets. ADR-0184 chased that cap with cheats and then measured it: Contra stage 1 lands on exactly 2512x240 from two unrelated configurations, so 2512 is a wall the blind "hold right" script cannot pass whatever keeps the player alive. The fan pack's stage 1 is 3348 px. The missing 25% is a *play* problem, not a survival one — F9.22 solved one instance by search (RAM inspection plus DFS over inputs) and cost days to cross two stages of one game. A tool-assisted speedrun is that same search, already done, published with its input; TASVideos licenses the movie files under **CC BY 2.0** (https://tasvideos.org/SiteLicense), which permits reproduction and adaptation with attribution. The ROM is not covered by it and is our own problem.

Two things had to be true, and both are: our Core already plays movies (`MovieManager`), and at least one published run targets a ROM we hold byte for byte — the Zelda 1 "all items" publication (https://tasvideos.org/4767M, chatterbox, 31:52) declares `romChecksum base64:0/RTkxFG6VsEoxZH3oD9qw==`, which is MD5 `d3f453931146e95b04a31647de80fdab` — our `roms/Zelda.nes` with its 16-byte iNES header stripped; full-file SHA-1 `3701381a…`, USA Rev 1. What is not true is the convenient case: **Contra, the game that motivated this, is the one game it does not help.**

Non-goals: adding a `.fm2` reader to the Core (a converter outside it is smaller and testable); shipping any movie file in this repository; making a movie a *source of truth* for anything — it is an input device.

## Consequences

- **The coverage ceiling moves for the games this covers, and not at all for the one that motivated it.** Contra keeps needing gameplay search.
- **ADR-0184's two-pass split becomes a fallback, not the plan.** Where a movie exists, §1 says one clean run serves all four surfaces; ADR-0184 stays in force for the games without one, and its §4 still binds (a run of a game a TAS never dies in records no death material).
- **Movie mode gives up ADR-0157's in-frame stop** (§4.3).
- **A new silent-failure class enters the harness**, and §3 is the whole defence: a movie that does nothing looks exactly like a movie that does not help, and the only way to tell is to compare against the run without it.
- **Sync is a standing risk we cannot detect in-band.** §4.2's total catches a total desync; a *partial* desync — the movie diverging halfway — produces a run better than the baseline and worse than the movie, and only a mid-run screenshot catches it (issue #201). Prefer BizHawk-made `.bk2` over converted `.fm2`.
- **A converted movie needs its power-on row dropped** (§4.4), a property of the pair of emulators, not the movie.
- **We now depend on an external archive for material.** `.cache/tas/` is a cache, not an archive; the publication URL in `notes[]` is what makes the run reproducible later.
- **Coverage is a union, not a maximum**: the attract loop holds shapes the TAS never draws, which is what ADR-0183's kit needs `--also` for.

## Record

- 2026-09-14 — decided and implemented; the gate (§4.2) was amended the same day.
- 2026-09-14 (**"Measured 2026-09-14"**) — Zelda 1 "all items" (4767M) converted and replayed against `roms/Zelda.nes`: 1920 s of emulation (114913 frames) in 313.5 s of wall clock, about 6x real time. The movie played and recorded almost nothing — it consumed every row and logged a clean `movie ended at frame 114914` while the game sat on "REGISTER YOUR NAME", proving the Core *accepted* the file, not that the game is playing — and only the comparison caught it (867 keys against 2371). The cause was an off-by-one row, found by bisection: short 5–52 s probes located the divergence (the HUD's A slot stays empty from 14 s; Link dies around 52 s; `-2` reproduces the registration-screen symptom, `0` and `+1` reach the cave without the sword, `+2` takes the sword and desyncs by 60 s, only `-1` survives). With the shift: 614 shapes / 3237 keys / 56 CHR pages against the movie-less 276 / 2371 / 77 and the one-frame-late 867 / 28. `scripts/stages/zelda/stage1-run.txt` produced a key set identical to the input-free run over 32 emulated minutes — it never leaves attract mode and is not a usable control.
- 2026-09-14 (second pass) — a native BizHawk Castlevania `.bk2` (`challanger,eien86`, 36788 rows, dry at frame 36790) diverges *during play*: correct at frame 3607, GAME OVER by 300 s. That is an emulation difference between NESHawk and our Core, tracked as issue #201, not a timing offset. Forcing `RecordedRomTest`'s exact ordering (`MoviePlay` immediately after `LoadRom`) yields a byte-identical 300 s screenshot, because `BizHawkMovie::Play` power-cycles the console itself. A sweep of -3..+2 fails; the `.bk2`'s first non-blank row is 12 (`START`) and the first directional row is 583. This is the "sync is a standing risk we cannot detect in-band" prediction made real.
- 2026-09-14 (gate, issue #201) — the gate is three rules (§4.2), and only a declared invariant fails a run. The Castlevania TAS fails `watch-violated` at frame 4141 (`$002A` 4 -> 3), 32650 frames before the run ends; the same movie stopped at 3607 and the Zelda run are clean (false positives 0 of 2). `TestSyncGateTotalComparisonMissesThePartialDesync` holds a run that passes the old total. The counter was corrected to distinct tile shapes; `lead-peaked-early` needed the frame-3600 warm-up guard. `cheat=` in the same run, and any console other than the NES, are unverified.
- 2026-09-14 (RAM) — NESHawk's default power-on pattern is FCEUX's `(i & 4) ? 0xFF : 0x00`, which Mesen's three-value `RamState` cannot express: a real representational gap that does not change the result.
