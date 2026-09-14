# ADR-0185: A published TAS movie is an admissible recording driver when it matches our ROM, and it is converted rather than trusted

- Status: accepted (2026-09-14, at the user's direction to decide without
  asking; implemented the same day as `scripts/fm2_to_bk2.py` and
  `scripts/headless_record`'s `movie=` flag)
- Date: 2026-09-14
- Related: ADR-0184 (the RAM-cheat rule this makes largely unnecessary),
  ADR-0183 (the artist kit these recordings feed), ADR-0182 (recording
  coverage), ADR-0159 (the grid dump a panorama is stitched from), F9.22 (the
  gameplay search this replaces for the games it covers)

## Context

Every surface of the artist kit is capped by the same thing: how far the
recording gets. ADR-0184 chased that cap with cheats and then measured the cap
itself — Contra stage 1 lands on exactly 2512x240 from two unrelated
configurations, one with a cheat and one without, so 2512 is a wall the blind
"hold right" script cannot pass whatever keeps the player alive. The fan pack's
stage 1 is 3348 px. The missing 25% is not a survival problem; it is a *play*
problem, and ADR-0184 said so: "the rest is a gameplay-search problem, as F9.22
was, and not a cheat problem."

F9.22 solved one instance of it by search — RAM inspection plus DFS over inputs
— and it cost days to cross two stages of one game.

A tool-assisted speedrun is that same search, already done, by people who did it
better, published with its input. TASVideos hosts them and licenses the movie
files under **CC BY 2.0** (https://tasvideos.org/SiteLicense), which permits
reproduction and adaptation with attribution. Replaying one locally to capture
graphics is squarely inside that licence. The ROM is not covered by it and is
our own problem, unchanged.

Two things had to be true for this to be usable, and both are:

- **Our Core already plays movies.** `MovieManager` detects the format by
  content — a zip holding `GameSettings.txt` is a Mesen `.mmo`, one holding
  `Input Log.txt` is a BizHawk `.bk2` — and `MoviePlay`/`MovieStop`/
  `MoviePlaying` are already exported. There is **no `.fm2` reader**: Mesen 1
  accepted them, Mesen 2 dropped it.
- **At least one published run targets a ROM we hold, byte for byte.** The
  Zelda 1 "all items" publication (https://tasvideos.org/4767M, chatterbox,
  31:52) declares `romChecksum base64:0/RTkxFG6VsEoxZH3oD9qw==`, which is MD5
  `d3f453931146e95b04a31647de80fdab` — our `roms/Zelda.nes` with its 16-byte
  iNES header stripped. Full-file SHA-1 `3701381a…`, USA Rev 1.

What is *not* true is the convenient case. Every modern Contra publication runs
`Contra (Japan)`, a VRC2 cartridge with different code, different mappers and
uncompressed CHR where the US release uses RLE. It will not sync on our
`Contra (USA)`, and even retargeted it would capture a tile set a `Contra (USA)`
pack cannot key off. The only USA-ROM Contra runs are 2005-era Famtasia `.fmv`
from a much less accurate emulator. **Contra, the game that motivated this, is
the one game it does not help.**

Non-goals: adding a `.fm2` reader to the Core (a converter outside it is
smaller and testable); shipping any movie file in this repository; making a
movie a *source of truth* for anything — it is an input device.

## Decision

### 1. A movie drives a recording; it never becomes evidence itself

A movie is input, exactly like `scripts/stages/*.txt`. Everything ADR-0183 says
about evidence and inference is unchanged, because what a movie changes is only
*how far the game gets* — the tiles, sprites and nametables recorded are the
game's own, drawn by the game's own code. This is the decisive difference from
ADR-0184's hazard 2: a cheat can make the game draw a state it almost never
shows, and a movie cannot. **A movie-driven run is a clean run**, admissible for
all four surfaces, with no two-pass split.

### 2. Conversion happens outside the Core, and the Core keeps its two formats

`.fm2` is converted to a `.bk2`-shaped zip by `scripts/fm2_to_bk2.py`. The Core
gains no format. The conversion is a positional permutation — fm2's gamepad
mnemonic order `RLDUTSBA` to Mesen's `UDLRSsBA` — plus the commands bitfield
mapped onto the leading `RP` console column, and it is derived in code from the
two named orders rather than written as a literal tuple, because a silently
wrong permutation produces a movie that plays and desyncs.

The converter **refuses, does not warn**, and names the cause: a `savestate`
header key (the Core has no `CoreState` handling, so the movie would start from
nowhere), `FDS 1`, `binary 1`, `fourscore 1`, a `romChecksum` that does not
match, a malformed port field. `palFlag 1` is reported, not refused — it is the
caller's job to set the region.

**The ROM check hashes the post-header bytes.** fm2's `romChecksum` is the MD5
of the ROM *without* its 16-byte iNES header. Hashing the whole file compares
the right ROM against the wrong number and rejects it. This is written down
because it cost real time to discover and looks like a ROM mismatch when it is
not.

### 3. The harness must not be able to silently record nothing

`MovieManager` ignores a file it does not recognise: no player, no message,
`MoviePlay` returns void. A run started that way records the title screen for
its whole budget and produces a pack that looks like a pack.

So `scripts/headless_record`'s `movie=` flag polls `MoviePlaying()` immediately
after `MoviePlay` and **fails the run** when it is false, naming the file and
the two container shapes the Core accepts. `movie=` is mutually exclusive with
`input=` and `state=`, refused when combined; a movie carries its own start
state and its own poll counter, and a script driving the same pad would fight
it.

### 4. Sync is not assumed; it is measured against the movie-less run

There is no in-band sync check worth trusting: `MesenMovie` never compares the
recorded ROM SHA-1 it stores, and `BizHawkMovie::ApplySettings` is a stub, so a
`.bk2`'s `SyncSettings.json` — region, power-on RAM pattern, board properties —
is ignored entirely. A desynced movie does not error; it plays a different game.

The acceptance test is therefore behavioural and comparative: **a movie-driven
run must record strictly more `(tileData, palette)` keys than an identical run
of the same length with no movie**, and its panorama must be longer. A run that
matches the movie-less baseline has desynced, whatever the logs say.

The settings the movie does or does not clobber differ by format and must be
treated differently. `MesenMovie::ApplySettings` overwrites the live settings
with the movie's own, restoring them when it stops — but only over
`EmuSettings::Serialize`'s subset: controller types, `RamPowerOnState`, region,
console type and the per-console quirk flags. The palette, channel volumes,
`EmulationSpeed`, the video filter and the HD/MEP-pack flags are outside that
subset and survive. `BizHawkMovie::ApplySettings` is a stub returning true, so
a `.bk2` clobbers nothing.

The subset is small, and it is exactly the wrong small: `RamPowerOnState` and
region are the two settings a desync turns on. A `.mmo` is therefore the more
dangerous of the two containers for a harness that configures itself, which is
the opposite of what its being our own format suggests.

### 5. Attribution travels with the material

CC BY 2.0 requires it, and the kit has a place for it already. A kit fragment
built from a movie-driven run records, in `notes[]`: the publication URL, the
author name as the publication gives it, and the movie file name. The converter
writes the same provenance into the output's `Comments.txt`, together with the
sync-relevant fm2 header values it did **not** carry across — `NewPPU`,
`RAMInitOption`, `RAMInitSeed`, `palFlag` — because those are exactly what a
desync investigation needs and dropping them silently is the defect.

No movie file is committed to this repository. They live under `.cache/tas/`,
unversioned, fetched from their publication page.

### 6. Per game, the movie is used only when it beats the script

Ranked by what the survey measured:

1. **Zelda 1 "all items"** (4767M) — our exact ROM, full game, deliberately
   exhaustive. The best coverage case we have, and the reason this ADR exists.
2. **Castlevania** (4840M) — USA PRG0, already a `.bk2` from BizHawk 2.8, so no
   converter and the closest thing to a drop-in. We do not currently hold that
   ROM.
3. **Mega Man 3** (2439M) — targets `Rockman 3` (Japan). Same class of mismatch
   as Contra; not usable against our USA ROM.
4. **Contra** — no usable path. Keep scripting input.
5. **Excitebike** (1348M) — races the built-in tracks only, never Selection B
   or design mode. A TAS buys little over a script here.

A movie is not automatically better. Zelda's *any%* run (3232M) is tagged heavy
glitch abuse and skips most of the overworld; the FDS "game end glitch" run
finishes in three minutes and records almost nothing. **The branch matters more
than the game does**: prefer "all items", "100%", "warpless" and pacifist/low%
variants, and read what a publication says it skips before using it.

## Consequences

- **The coverage ceiling moves for the games this covers, and not at all for
  the one that motivated it.** Contra keeps needing gameplay search. That is
  worth stating plainly rather than letting the Zelda result imply otherwise.
- **ADR-0184's two-pass split becomes a fallback, not the plan.** Where a movie
  exists, §1 says one clean run serves all four surfaces and no cheat is
  involved. ADR-0184 stays in force for the games without one — and its §4 still
  binds: a movie-driven run of a game a TAS never dies in records no death
  animation, no respawn and no GAME OVER screen, so that material still needs a
  run that dies.
- **Movie mode gives up ADR-0157's in-frame stop.** A run is a number of
  emulated frames, and the frame it stops on is normally decided from inside
  the frame by `HeadlessInputProvider`. While a movie plays that never fires:
  `BaseControlManager::UpdateInputState` stops at the first provider whose
  `SetInput` returns true, `MesenMovie::SetInput` always does, and the movie's
  provider registers on `AfterInitConsole` while `HeadlessInputProvider`
  re-registers on the later `GameLoaded` — so the movie is permanently ahead of
  it. The harness falls back to a host-side `Pause()` from the poll loop, which
  lands a frame or two past the target instead of exactly on it. Measured: a
  300-frame budget parks at 301 with the guard and at 602 without it, 602 being
  the frame the movie's input ran out. The run reports the frame it actually
  reached; a caller that needs frame-exact stops must not use `movie=`.
- **A new silent-failure class enters the harness**, and §3 is the whole
  defence. The ADR-0184 lesson repeats in a different costume: a movie that does
  nothing looks exactly like a movie that does not help, and the only way to
  tell is to compare against the run without it.
- **Sync is a standing risk we cannot detect in-band.** §4's comparative test
  catches a total desync. A *partial* desync — the movie diverging halfway —
  produces a run that is better than the baseline and worse than the movie, and
  nothing here catches that. The honest mitigation is to prefer BizHawk-made
  `.bk2` over converted `.fm2`, since Mesen's importer was written against that
  lineage, and to treat a converted FCEUX movie as the riskier input it is.
- **A converted movie needs its power-on row dropped, and that is a property of
  the pair of emulators, not of the movie.** §4's comparative test is what
  caught it; see "Measured 2026-09-14". Any future converter for another source
  format must establish its own offset the same way, by bisection against a
  visible in-game event, and must not assume this one's answer. Measured
  2026-09-14, below: a `.bk2` needs 0 and an `.fm2` needs -1, both against real
  movies. An earlier reading of the Castlevania desync as a harness defect was
  wrong and is corrected there.
- **We now depend on an external archive for material.** A publication can be
  obsoleted and its file moved. `.cache/tas/` is a cache, not an archive: a kit
  built from a movie records the publication URL in `notes[]`, which is what
  makes the run reproducible later, and is required by the licence anyway.

## Measured 2026-09-14

Zelda 1 "all items" (https://tasvideos.org/4767M, chatterbox), converted by
`scripts/fm2_to_bk2.py` and replayed through `scripts/headless_record`'s
`movie=` against `roms/Zelda.nes`. 1920 emulated seconds — the movie's full
114913 frames — in 313.5 s of wall clock, about 6x real time.

**The movie played perfectly and recorded almost nothing.** The first attempt
consumed every input row and logged a clean `movie ended at frame 114914`,
while the game sat on Zelda's "REGISTER YOUR NAME" screen typing garbage into
the save slots. This is §3's failure mode in its second costume: the harness's
check proves the Core *accepted* the file, and proves nothing about whether the
game is playing. Only §4's comparison caught it — 867 keys against the
movie-less run's 2371.

**The cause was an off-by-one row, and finding it needed bisection, not
reasoning.** Short probes at 5–52 s located the divergence precisely: frames
0–700 replay correctly — title, file select, overworld, the first cave, the old
man — but the HUD's A slot stays empty from 14 s. Link never picks up the
sword, an event decided by a single frame of sprite overlap, and everything
downstream drifts until he dies around 52 s. Shifting the input log by hand:
`-2` reproduces the registration-screen symptom exactly, `0` and `+1` reach the
cave without the sword, `+2` takes the sword and desyncs by 60 s, and only `-1`
survives.

Our Core spends a poll before the first frame runs — after the movie's
`PowerCycle`, `Emulator::LoadRom` calls `UpdateInputState()`, and the NES then
polls again at `InputScanline` 241 every frame — while FCEUX applies its first
log row to the first frame it emulates. Emitting the rows one for one delivers
every input one frame late. The converter therefore **drops the fm2's power-on
row**, and refuses by name if that row holds a button or a command bit, so the
drop can never silently lose input. The offset was established by measurement
and only through the harness; whether the same shift is right when a converted
bk2 is played in the GUI has not been checked.

With the shift, the same 1920 s run is in sync throughout — Link is in Level 4
at 300 s and carrying the white sword with 8 hearts at 600 s:

| run | distinct tile shapes | `(tileData, palette)` keys | CHR pages |
|---|---|---|---|
| TAS, corrected | **614** | **3237** | 56 |
| no input (attract) | 276 | 2371 | 77 |
| TAS, one frame late | — | 867 | 28 |
| union of the first two | 808 | 5557 | — |

**The movie-less baseline is not a null baseline, and the scripted one was
worthless.** Left alone, Zelda runs its attract loop — title, story scroll,
scripted overworld and dungeon demos — which cycles real art on its own.
`scripts/stages/zelda/stage1-run.txt` produced a key set **identical** to the
input-free run over 32 emulated minutes: the script never takes the game out of
attract mode, so every past measurement using it as a control was measuring
attract mode. It is not a usable control and should not be reused as one.

**Neither pass dominates.** The attract loop holds **194 tile shapes the TAS
never draws** — the title screen and the story scroll a speedrun crosses at
speed — against the TAS's 532 exclusive ones. This is ADR-0184 §4's finding in
a second game and a different cause: coverage is a union, not a maximum, which
is exactly what ADR-0183's kit needs `--also` for.

## Measured 2026-09-14, second pass: the offset is not ours, and a partial desync is real

The "sync is a standing risk we cannot detect in-band" consequence above was
written as a prediction. It is now an observation, and the investigation that
produced it also overturned a claim made earlier in this session.

**The claim that was wrong.** A native BizHawk Castlevania `.bk2` desyncs in
our Core, and that was read as proof that the one-frame offset was a defect in
our harness's `LoadRom` -> `MoviePlay` sequence rather than a property of the
`.fm2` format. It is not. Three independent measurements:

- **Poll accounting.** The prime poll inside the movie's own `PowerCycle`
  consumes row 0 before a scanline is drawn; from there it is exactly one poll
  per frame, with no accumulating drift. A 36788-row `.bk2` runs dry at frame
  36790.
- **Harness ordering is not the defect.** Forcing `RecordedRomTest`'s exact
  ordering -- `MoviePlay` immediately after `LoadRom`, before the first frame
  -- yields a **byte-identical** 300 s screenshot. `BizHawkMovie::Play`
  power-cycles the console itself, erasing the frame the harness ran first.
  Nothing in `scripts/headless_record.cpp` needs to change.
- **The `.bk2`'s alignment is already 1:1.** Its first non-blank row is 12
  (`START`) and its first directional row is 583; frame 583 is the first
  controllable frame. At shift -1 the row-12 `START` misses and the console
  never leaves the attract demo. A sweep of -3..+2 fails at every value.

Conversely, regenerating the Zelda movie with `FM2_POWER_ON_ROWS = 0` leaves
the run stuck on "REGISTER YOUR NAME", where the drop reaches the Level 4
dungeon with the sword. So the drop is required and stays. **A `.bk2` needs 0
and an `.fm2` needs -1** -- a converter-level fact, established per format by
measurement, exactly as this ADR's consequence requires.

**What the Castlevania movie actually does** is diverge *during play*: correct
at frame 3607 (full health, P-03, stage 01), a life lost by frame 4807, GAME
OVER by 300 s. A run that starts perfectly aligned and dies a minute in is an
emulation difference between NESHawk and our Core, not a timing offset, and it
is tracked as issue #201.

That is the partial desync this ADR predicted, and Sec. 4's comparative test
does not catch it: such a run records more keys than the movie-less baseline
and fewer than the movie, so it passes the gate while archiving a playthrough
nobody intended. Until #201 is understood, a long movie-driven recording must
be checked against a mid-run screenshot, not against its key count alone.

Also ruled out, each byte-identical or still broken: `RamPowerOnState`
AllZeros vs AllOnes, `InputScanline` 0 and -1, port 2 unplugged. Worth
recording separately: NESHawk's default RAM power-on pattern is FCEUX's
`(i & 4) ? 0xFF : 0x00`, which Mesen's three-value `RamState` cannot express.
It does not change this result, but it is a real representational gap.
