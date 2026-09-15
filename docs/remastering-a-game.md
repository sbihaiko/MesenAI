# Remastering a game: from a recording to a pack you can paint

This is the entry point for someone who wants to **redraw a game's art** —
a Contra80s-style HD remaster of a NES/GB/SMS title. It is the task guide, not
the submission guide: when your pack is painted, [`hd-pack-authoring.md`](hd-pack-authoring.md)
covers getting it validated and listed in the community catalog.

Everything below runs on your machine, from a ROM you own. Nothing is uploaded
and no game art ships with this project.

If you have never seen the pack format, [`enhancement-ecosystem.md`](enhancement-ecosystem.md)
is the two-minute orientation. If you would rather drive the GUI, **Tools → HD
Packs → HD Pack Builder** records tiles while you play, and the recordings it
leaves behind feed the same tools described here.

---

## The job, in order

| # | Stage | Tool | What you get |
|---|---|---|---|
| 1 | **Record** the game doing everything it can do | `scripts/headless_record` | a *recorded pack*: what the game drew, frame by frame |
| 2 | **Measure** what the recording reached | `scripts/artist_cover.py` | coverage tables — how much of the art you now have |
| 3 | **Unpack** into a kit | `scripts/artist_kit*.py`, `artist_map.py` | PNG surfaces to paint in, plus a sidecar per surface |
| 4 | **Paint** | your editor | painted PNGs |
| 5 | **Build and verify** | `scripts/mep_build.py`, `mep_lint.py` | `textures/hires.txt`, a lint-clean pack |
| 6 | **Ship** | `mep_build.py pack` | a zip, then the [submission form](hd-pack-authoring.md) |

Steps 1 and 3 are the ones this project exists to make cheap. Step 4 is yours.

---

## 1. Record — the step that usually costs a week

A recording is the game running with a pack builder attached to it, writing out
every tile it drew and where. The reason this is a whole stage rather than a
checkbox is that **one pass never sees everything**: a playthrough skips the
deaths, the alternate routes, the boss's second phase and the frames behind a
scrolling camera. Artists describe the result exactly this way — *"it turned
into a jumbled mess, I don't even know where to start untangling it"*, and
*"sometimes I need several passes over a scene to record all the sprites/tiles"*.

So the recorder has four drivers. Use them together; each reaches art the others
cannot.

### Build the tool first

```sh
make core
make capture-tool        # writes scripts/headless_record
```

**Using a binary release?** The release already includes `headless_record` and
`MesenCore.dylib`; do not run `make`. Run the prebuilt executable from the
unpacked release directory, keeping the dylib beside it, and run the bundled
Python tools from that same release.

### The three positional arguments

```sh
scripts/headless_record <rom> <seconds> <output-prefix> [flags...]
```

`<seconds>` is **emulated** seconds (converted to a frame count for the region's
rate), not wall clock. `bootstrap` costs roughly 3x the plain emulator, so budget
~400 emulated seconds per run when you are recording — several short runs beat
one long one, because the retained frame stream is capped and one long run loses
its tail.

### Driver A — a gameplay script (`input=`)

A route written as text, one line per input change:

```
<count>f <buttons>      # <count> frames of <buttons>
<count>s <buttons>      # <count> seconds
```

Buttons are `U D L R A B S T`, `-` means nothing held, and a second player is
`<count>f <port1>|<port2>`. A bare count with no buttons is a parse error. The
full contract, including the *probe* scripts that let the recorder read a
sprite's animation cycle, is [`scripts/stages/README.md`](../scripts/stages/README.md),
and `scripts/stages/` ships working sets for Contra, Zelda, Mega Man 3 and
Excitebike.

```sh
# `scripts/stages/` ships the routes (`.txt`) and never the states (`.mss`):
# a state carries the game's graphics, so it is not versioned. Mint one into
# your own working directory first (Driver D below), then replay it.
scripts/headless_record roms/Contra.nes 60 out/mint bootstrap hdpack-off \
  input=scripts/stages/contra/mint-stage1.txt save-state=out/stages/stage1-run.mss
scripts/headless_record roms/Contra.nes 60 out/rec bootstrap hdpack-off \
  input=scripts/stages/contra/stage1-run.txt state=out/stages/stage1-run.mss
```

To record every stage of a game in one command, put `<stage>.mss` +
`<stage>.txt` pairs in one folder and batch them. Copy the shipped routes into
your working directory and mint the states beside them:

```sh
cp -R scripts/stages/contra out/stages
# mint into out/stages/<stage>.mss the stages you have a mint-*.txt for
scripts/record_stages.sh roms/Contra.nes out/stages out/by-stage 60
```

That writes one recorded pack per stage under `out/by-stage/<stage>/<rom name>/auto/`.
Nothing is merged — you judge them as a union, and a stage that came out thin is
the stage you record again. A `<stage>.txt` whose `<stage>.mss` is not there
yet is **replayed from power-on instead**, which is not the same recording:
check that the state exists before you batch.

### Driver B — a published TAS movie (`movie=`)

A tool-assisted speedrun reaches places a hand-written route will not. The
harness plays one directly:

```sh
scripts/headless_record roms/Contra.nes 400 out/tas bootstrap hdpack-off \
  movie=/path/to/run.bk2
```

Two things to know:

- The Core identifies the container **by content, not by extension**. It reads
  BizHawk `.bk2` (a zip holding `Input Log.txt`) and Mesen `.mmo` (a zip holding
  `GameSettings.txt`). **It does not read `.fm2`** — an unrecognised file is
  dropped silently and the run aborts, which is deliberate: a movie that failed
  to load must not be mistaken for a recording of nothing.
- Convert first if your source is FCEUX:

```sh
scripts/fm2_to_bk2.py movie.fm2 --rom roms/Contra.nes -o /tmp/contra.bk2
```

A movie is only admissible when it was recorded against **your** ROM: a movie
made for a different revision desyncs, and a desynced recording is worse than no
recording because it looks like one. Declare an invariant the movie guarantees
and the run fails itself instead of archiving a half-diverged playthrough:

```sh
scripts/headless_record roms/Castlevania.nes 400 out/tas bootstrap hdpack-off \
  movie=/path/to/run.bk2 sync-watch=002A:never-decreases:lives
```

`sync-watch=AAAA:<rule>[=<n>][:<label>]` is repeatable; rules are
`never-decreases`, `never-increases`, `never-below=<n>`, `never-equals=<n>`;
repeatable and checked only while the movie drives the pad. Add
`sync-movie-frames=<n>` when you know how many frames the movie's input covers.
Both are RAM **reads** — see the cheat rules below. The design and its measured
false-positive behaviour are in [ADR-0185](adr/0185-a-published-tas-movie-is-an-admissible-recording-driver-when-it-matches-our-rom.md).

### Driver C — a RAM cheat (`cheat=`)

Repeatable `cheat=AAAA:VV[:CC]` sets a byte for the whole run — invincibility, a
stage select, infinite lives. Two rules, both load-bearing:

- **RAM only: addresses `$0000`–`$07FF`.** Game Genie codes are refused. On a
  game whose CHR is decompressed from PRG at runtime, patching PRG corrupts the
  art you are trying to record, which is the opposite of the point. If you find
  the RAM byte that means "current stage", you can warps the game through every
  stage without drawing anything foreign.
- **A cheated run feeds only the background surfaces of the kit — never the
  sprites.** A tile the game drew only because a cheat was on is not a tile the
  game draws. [ADR-0184](adr/0184-a-recording-may-use-a-ram-only-cheat-and-a-cheated-run-feeds-only-the-background-surfaces.md)
  has the measurement behind this.

### Driver D — start from a save state (`state=`, `save-state=`)

`state=<file.mss>` starts a run from a state; `save-state=<file.mss>` writes one
when the run reaches its frame target. That is how stages are reached headlessly:
mint a state from power-on, then record from it. `movie=` cannot be combined
with `input=` or `state=` (each combination is refused, in either order).

```sh
scripts/headless_record roms/Contra.nes 60 out/mint \
  input=scripts/stages/contra/mint-stage1.txt save-state=out/stages/stage1-run.mss
```

`.mss` files are **never versioned** — a CHR-RAM state carries the game's
graphics. Keep them in your working directory.

### Check the route before you trust the recording

A route is a blind script, and a blind script dies. When it does, the run keeps
going and records the death animation, the game-over card and the title screen
instead of the stage — a recording that looks healthy by file count and holds
almost none of the art you wanted. Verify with the `screenshot` flag before you
spend a `bootstrap` run on the route:

```sh
scripts/headless_record roms/Metroid.nes 60 out/probe screenshot hdpack-off \
  input=scripts/stages/metroid/stage1-run.txt
```

It runs the route and saves the **final** frame to
`<dir of output prefix>/mesen-home/Screenshots/<rom stem>_NNN.png`. Look at it.
A frame reading `GAME OVER` or `PASS WORD` means the route died before its
budget; shorten the run to the part that survives, or fix the route. Repeat at a
few different `<seconds>` values to find where it dies.

### The flags in those command lines

Every `headless_record` line in this guide uses the same six arguments. They are
positional-then-flags: `<rom> <seconds> <output prefix>` come first, everything
after is a flag, and the order of the flags does not matter.

| Flag | What it does |
| --- | --- |
| `bootstrap` | Runs the pack builder as the game plays, so a pack is written beside the ROM. **This is what makes a recording**, and it costs about 3x the emulation time. Without it the run is only audio export or a screenshot. |
| `hdpack-off` | Disables HD pack / MEP texture substitution for the run, so you record the game's own art. Omit it on a run whose purpose is to *look at* a pack you installed — that is the difference between recording and reviewing. |
| `hdpack` | The opposite switch: record a pack skeleton to `<prefix>-hdpack/` from the first `<seconds>` with no input fed. Used by the tooling's own tests; you do not need it to remaster a game. |
| `screenshot` | Saves the final frame to `<output prefix>/mesen-home/Screenshots/<rom stem>_NNN.png`. The route check above and the pack review later both depend on it. |
| `input=<file>` | Plays an input script (`.txt`, `<frames>f <buttons>` per line) instead of idling. This is the route. |
| `state=<file>` / `save-state=<file>` | `state=` starts the run from a saved state; `save-state=` writes one at the end. Minting a stage state and then recording from it is two runs, as above. |

- `MESEN_SHEET_GRID_DUMP=<file>` (environment variable) writes the grid stream
  that `artist_map.py` needs to rebuild a whole stage as one scrolling image.
  Set it on the runs you will build panoramas from:

  ```sh
  MESEN_SHEET_GRID_DUMP=$PWD/grid.txt scripts/headless_record roms/Contra.nes 60 out/rec \
    bootstrap hdpack-off input=scripts/stages/contra/stage1-run.txt
  ```

- `cdl=<file.cdl>` emits the Code/Data Logger state for the run — which ROM
  bytes the CPU executed and which it read as data, at absolute ROM offsets.
  `scripts/cdl_tool.py regions|report|union|strip` reads it. This is the source
  that can tell you about tiles the game never drew on screen. It requires the
  debugger to be attached, so a `cdl=` run stops rather than continues under
  some conditions — read the block at the top of `scripts/headless_record.cpp`
  before scripting it.

### Where the recording lands

With `bootstrap`, the pack builder writes **beside the ROM**:

```
<rom dir>/<rom stem>/auto/
  textures/hires.txt          # the keys; generated, never hand-edited
  textures/sheets/spr*.png    # sprite sheets as recorded
  textures/chr/Chr_*.png
  textures/backgrounds/screenNNN.png
  audio/fingerprints.json
```

That folder is what every step below consumes. It is also a working pack — you
can load it in the emulator as-is and see what you have.

**Delete it before you record the same ROM again.** Once `<rom stem>/auto/`
exists it is discovered as a pack that already dresses this ROM, so the next
`bootstrap` run declines to record and leaves the old pack untouched — the run
still exits 0 and says nothing, so a second recording that overwrote nothing
looks exactly like one that worked. Either `rm -rf <rom dir>/<rom stem> <rom
dir>/.bootstrap` first, or record through `scripts/record_stages.sh`, which
gives every run its own directory with a hard link to the ROM and clears any
pack left there.

Silence is the defect, not the decline — a run that recorded nothing should
say so. Tracked as issue #229; the workaround above stays valid until it
lands.

---

## 2. Measure before you paint

Do not start painting without this. It tells you which of an artist's finished
tiles your recordings have actually put on screen, so you know what to record
more of instead of guessing.

```sh
scripts/artist_cover.py <reference hires.txt> out/by-stage/stage1/<rom stem>/auto ...
```

It prints three markdown tables: totals, per reference image, and per recorded
state with the tiles **only that state** exhibited. An artist image counts as
*sprite* when at least half its seen tiles land on one of the recorded packs'
sprite sheets, *background* otherwise, and *unseen* when nothing of it was on
screen. The per-state table is the one that tells you which stage to play again.

**A reference pack that patches the ROM is not comparable to a recording of the
stock ROM.** `artist_cover.py` matches on the tileData string, and a pack
shipping a `<patch>` directive keys its art against the *patched* game. The
community Metroid pack is the worked example: it declares the stock
`<supportedRom>` SHA1, then applies `mmm.ips`. Stock Metroid (USA) is mapper 1
with **CHR RAM**, so a recording keys every tile by its 32-hex-character pattern
(`<tile>0,3E7FFF7007FFFC1E00061F000007D01E,...`), while the patched ROM has CHR
ROM and the artist keys by index (`<tile>0,00,...`). The two namespaces cannot
intersect, and today the tool reports that as a flat `0/1465` with no warning —
see issue #225. Check the reference for `<patch>` lines and compare a couple of
`<tile>` rows from each file before you believe a coverage number, in either
direction.

Measured on Contra against the Contra80s reference: blind route recording
reached **53.8%**, adding eleven per-stage and per-boss sessions took it to
**58.9%**, and the union of everything reached **64.6%** — 367 tiles the archive
had never held. That is the shape of the payoff: the last few percent is exactly
the part no single playthrough contains.

### When it refuses instead of measuring

The number is a set intersection, so it only means anything when both sides name
their tiles the same way. A `hires.txt` keys a tile either by its CHR ROM bank
index (`A55`) or by the 32 hex characters of its CHR RAM pattern, and the
emulator tells the two apart by width alone. A reference pack built for a ROM
that a `<patch>` turned from a CHR RAM board into a CHR ROM one keys its tiles in
a namespace your recording of the stock ROM can never contain — every key differs,
and a plain intersection would report that as a confident **0%** you would read as
"record more".

It does not. It exits non-zero and names the cause, the patch and the iNES header
bytes that patch writes. Your options are to record the *patched* ROM the pack
targets and measure against that, or to pick a reference pack built for the ROM
you actually recorded. A partial mismatch still measures, with a warning saying
how much of the reference the recording could never have held.

### And when it does not refuse, and the number is still not yours

The refusal above catches one shape: the two sides keying tiles in *different*
namespaces. A `<patch>` can also leave both sides in the same namespace and
still make the comparison meaningless — a CHR ROM game whose patch rewrites the
PRG/CHR body rather than the board type keys its tiles by index on both sides,
so every key is the right shape and the intersection is a real one, just between
two different builds.

Nothing in the output says so today. Measured on the Zelda II "Revamp" pack,
whose `hires.txt` line 4 is a `<patch>`: the table looks plausible and is not —
`Characters/hero_Normal.png` classified as *background*, `blank.png` with 30
cells seen. **Open the reference's `hires.txt` and look for a `<patch>` line
before steering by the percentages.** It is not automatically fatal (a patch
that only touches audio leaves the tiles alone), but it is never visible, and it
is tracked as issue #231.

---

## 3. Unpack the recording into a kit

A kit is a set of PNG surfaces you can paint, plus a sidecar per surface that
says what each region of the PNG means and which `hires.txt` key it will become.
Run the generators into one shared `--out` folder, then assemble:

```sh
PACK=out/by-stage/stage1/Contra/auto
KIT=out/kit

scripts/artist_kit.py     "$PACK" --out "$KIT" --verify   # sprite figures
scripts/artist_bg_kit.py  "$PACK" --out "$KIT" --verify   # background objects
scripts/artist_chr_kit.py "$PACK" --rom roms/Contra.nes --out "$KIT" --verify  # pattern pages
scripts/artist_map.py --out "$KIT" --stage stage1 --dump grid.txt --pack "$PACK" \
  --scale 4 --verify   # the <scale> the recording's own hires.txt declares

scripts/artist_kit_assemble.py "$KIT" --title "Contra, stage 1"
```

The last line writes **`<kit>/ARTIST.md` — the page you open first** — plus
`kit.json`, which merges every generator's `kit-part-*.json` manifest.

| Generator | Surface | What it is for |
|---|---|---|
| `artist_kit.py` | `<kit>/sheets/` | sprite figures on grids, animation cycles in phase order, variants beside their base |
| `artist_bg_kit.py` | `<kit>/` object sheets | background elements recovered across their animation phases |
| `artist_map.py` | `<kit>/map/` | the stage stitched into one long panorama, addressable per 8x8 cell — the shape of Contra80s `Stage1a.png` (6696x480 at scale 2, i.e. 3348x240 logical). A panorama is only as long as the camera actually travelled, so a short recording gives a short strip. **CHR RAM games only** — see below |
| `artist_chr_kit.py` | `<kit>/chr/Chr_*.png` | complete pattern pages — every tile of a CHR bank, in ROM order |

A measured 60-second stage-1 recording yields, for scale: 4 parts, 43 files,
6922 cells; 18 CHR pages over 9 banks from a 2-bank ROM (`--fill-rules none`:
246 recorded, 109 filled from ROM, 157 unrecoverable — 69% complete); and a
592x240 panorama covering 5.6% of the pack's tile keys. Painting a *whole* stage
means recording the whole stage.

Each writes a `.legend.png` / sidecar next to the PNG naming what it holds.
`--verify` is the round trip: it rebuilds a throwaway pack with your kit dropped
in and asserts no `(tileData, palette)` key changed. It is the acceptance test —
run it every time, and treat a failure as "the kit is wrong", never as "the
verify is wrong".

### The panorama is a CHR RAM surface

On a CHR ROM game (Zelda II, Mega Man 3) the recorder keys every tile by its
CHR index rather than by its 32-hex bitmap (ADR-0043, ADR-0172), and the grid
dump carries no index, so nothing in the panorama could be matched back to a
key. `artist_map.py` says so and stops:

```
error: <stage>: <pack>: this pack keys its tiles by CHR index (ADR-0172) and the
grid dump carries no index — a panorama built from it would match nothing.
```

The ROM you feed it decides this, not the game's name. Contra (USA) is a UNROM
board with CHR RAM and gives a panorama; Contra (Japan) is a VRC2 board with CHR
ROM and `artist_map.py` refuses it, even though the panorama section above uses
Contra as its worked example. If a tool's answer does not match the game you
think you loaded, check the dump before you re-read the guide.

That is the tool refusing to write a surface it cannot verify, not a broken
run. On such a game the kit is the other three surfaces; the stage's
backgrounds are still in `textures/backgrounds/screenNNN.png`.

### Filling a bank from evidence rather than from nothing

`artist_chr_kit.py` completes each CHR bank so a page is paintable even where
the recording never reached. Precedence, per cell of a bank's rank-0 page:

1. **evidence** — this recording's own tile on this page;
2. **borrowed** — this bank's tile from a lower-ranked page;
3. **donated** — a tile **another recording of the same ROM** drew, via
   `--also <other pack>`, repeatable. A donor is refused unless its
   `<supportedRom>` SHA1 matches yours;
4. **fill** — read statically from the ROM, marked `seen: false` in the legend;
5. **empty**.

Two more things the CHR kit does that matter for how much work you have:

- **Fade and inert variants are folded.** A recording keys a cell by
  `(pattern, palette)`, so a screen fade turns every on-screen tile into one key
  per brightness step. Folding maps those onto a single cell; the renderer
  rebuilds the rest from the `tile` row's Brightness column. On the flagship kit
  this took 4712 cells to 3439. Folding is **colour-only**: a palette that
  changes the hue of anything you painted is never folded, and no pixel is ever
  altered by it.
- `--fill-rules observed|all` controls whether the ROM fill is limited to tiles
  the recording's rules actually reach, or every cell of the bank.

### A second recording is evidence

Recording the same ROM twice and passing the second pack as `--also` is the
cheapest coverage increase available: it converts "empty" cells into donated
ones without you replaying anything. Use it whenever a stage came out thin.

---

## 4. Paint

**Your deliverable is the PNGs.** Paint `<kit>/sheets/*.png`, `<kit>/chr/*.png`
and `<kit>/map/*.png` in place, at the scale the sheet was generated at. `<kit>/ARTIST.md`
states the rules that matter — keep every image's size and the position of
everything inside it, keep transparency transparent, and **never paint a
`.orig.png`**: that is the untouched reference the rebuild is checked against,
and painting it is how your work becomes invisible.

Do not hand-edit `textures/hires.txt`. It is generated from the sheets by
`mep_build.py build`, and an edit there is thrown away on the next build. Copy
each sheet **together with its `.json`** — the sidecar is the slicing contract
that says which cell is which tile.

For a panorama, `artist_map.py --slice` is the way back:

```sh
scripts/artist_map.py --slice out/kit/map/stage1-000.painted.png \
  --map out/kit/map/stage1-000.json --out out/kit
```

It cuts a painted panorama into ordinary pack sheets using the sidecar.

---

## 5. Build and verify

The kit is a folder beside the recording, not the pack. Copy the recording,
drop the painted files in, and build the copy:

```sh
cp -R out/by-stage/stage1/Contra/auto out/painted
cp out/kit/sheets/*.png out/kit/sheets/*.json out/painted/textures/sheets/
cp out/kit/chr/*.png    out/kit/chr/*.json    out/painted/textures/chr/
cp out/kit/map/*.png    out/kit/map/*.json    out/painted/textures/sheets/

scripts/mep_build.py build out/painted       # regenerates hires.txt, then lints
python3 scripts/mep_lint.py out/painted      # exit 0 = clean
```

`mep_build.py build` regenerates `textures/hires.txt` from `textures/sheets/*.png`
and `audio/hires.txt` from `audio/bgm|sfx/`, then runs the linter — **a lint
failure is a build failure**, so exit 0 means both happened. 0 errors with
warnings about the recorder's own sheet sizes is normal and is called out as
such in the output.

That is the acceptance test: **`build` exit 0, `mep_lint.py` exit 0, and every
generator's `--verify` PASS.** All three are mechanical, they take seconds, and
a failure means the kit is wrong — never that the check is wrong.

### Look at the painted pack before you ship it

The mechanical checks prove the pack is valid; they do not replace looking at
your edit in the game. Copy the built folder beside the ROM as its `mep/`
override, then run a screenshot pass **without** `hdpack-off`:

```sh
rm -rf <rom dir>/<rom stem>/mep
cp -R out/painted <rom dir>/<rom stem>/mep
scripts/headless_record <rom> 20 out/painted-check screenshot
```

The recorder log must say that it loaded `<rom dir>/<rom stem>/mep/textures`.
Open the resulting screenshot and confirm the exact figure you painted. The
`mep/` layer overrides `auto/` entry by entry, so your original recording stays
available underneath it.

If `build` reports dropped duplicate keys, do this screenshot check before you
call the edit done. A sheet may share `(tile, palette)` keys with other sheets;
a lint-clean build can still leave part of a figure supplied by `auto/`. Track
key ownership manually for now; issue #253 covers an artist-facing ownership
report.

`mep_build.py check-coverage` answers a different question — "did this repaint
lose a tile the previous build carried?" — and its `--baseline` has to be a
manifest **`build` itself wrote**, never the recorder's: the recorder keys every
CHR tile it saw out of `textures/chr/` and `textures/backgrounds/`, which `build`
was never asked to re-derive. Pointed at the recorder's manifest it now refuses
("this baseline is not sheet-derived") instead of reporting a loss that never
happened (#218). To use it, keep the manifest of a build taken *before* you
paint:

```sh
scripts/mep_build.py build out/painted
cp out/painted/textures/hires.txt out/baseline-hires.txt   # before painting
# ... drop the painted files in, rebuild ...
scripts/mep_build.py check-coverage out/painted --baseline out/baseline-hires.txt
```

If you want the audio layer too, the recording's own fingerprints seed it:

```sh
scripts/mep_render_audio.py out/pack           # fingerprints → OGG, via SF2 or the internal synth
scripts/audio_cleanup_suggest.py out/pack      # report-only: flags seeds not worth keeping
```

---

## 6. Optional: let a model propose the tedious names

Naming what each figure *is* is the part of a kit that is pure drudgery.
`scripts/artist_ai_review.py` renders a review packet the agent can look at and
returns proposals. It is a **proposer, never evidence** — nothing it says enters
a pack until a human promotes it, and the tool has no promote-everything flag.

```sh
scripts/artist_ai_review.py packet out/kit --out out/review --crops out/review/crops
# an agent with vision reads out/review/kit-review.md and writes kit-proposals.json
scripts/artist_ai_review.py promote out/kit --proposals kit-proposals.json \
  --accepted accepted.txt --out names.json
```

`--crops` takes a **folder**: it writes each figure enlarged. Show the model the
enlarged crops, not the 16x32 thumbnail — the same figure judged at 16x32 and at
128x256 is not the same judgement, and the measured comparison is in the
protocol: [`ai-kit-review.md`](ai-kit-review.md). Run against the Contra flagship
kit, the reviewer was never *confidently* wrong: 0 of 34 scorable asks, with the
errors concentrated on ambiguous `mixed` boxes and the abstention rate rising
when only the native-resolution sheet was shown.

---

## 7. Ship it

```sh
scripts/mep_build.py pack out/pack --out contra-remaster.zip --rom roms/Contra.nes \
  --name contra-remaster --version 1.0.0 --author "<your name>" --license CC-BY-4.0
```

Then follow [`hd-pack-authoring.md`](hd-pack-authoring.md): open the pack
submission issue with the link, and the workflow lints it, hashes it, labels it
with what is inside and lists it in the catalog if it passes. That guide is also
where the split-distribution flow lives, if your pack is too large for one zip.

---

## If something looks wrong

| Symptom | Cause |
|---|---|
| A movie-driven run aborts immediately | `.fm2` (unsupported) or an unrecognised container. Convert with `fm2_to_bk2.py`; the Core reads `.bk2`/`.mmo` by content, not extension. |
| A movie-driven run fails with a `sync-watch` finding | The movie desynced from your ROM revision. That is the gate working; get a movie for your revision. |
| A recording has almost no sprites | Frames after the retained-stream cap are dropped. Use several shorter runs (`record_stages.sh`) instead of one long one. |
| A recording holds the title screen and little else | The route died partway and the run recorded the game-over and password screens. Re-run it with the `screenshot` flag and look at the final frame. |
| `artist_cover.py` reports every reference image `unseen` and `0/N` | The reference keys tiles in a different namespace — most often because it ships a `<patch>` and is authored against the patched ROM. Compare a `<tile>` row from each file: a 32-hex-character pattern never matches a short CHR ROM index. Issue #225. |
| A second `bootstrap` run changes nothing in the pack | `<rom stem>/auto/` already exists, so the bootstrap declines and keeps it. Delete the folder (and the sibling `.bootstrap` stamp) or record through `record_stages.sh`. The run should say so and does not — issue #229. |
| `artist_map.py` refuses: "keys its tiles by CHR index" | A CHR ROM game; there is no panorama for it yet. |
| A stage's tiles are missing from the coverage table | No recording reached them. Record that stage — `artist_cover.py`'s per-state table names which state exhibited what. |
| `artist_cover.py` refuses: "different namespaces" | The reference pack is built for a patched ROM whose board has CHR ROM where the stock one has CHR RAM (or the reverse), so the two sides key tiles differently and no key can match. Record the patched ROM, or use a reference built for the ROM you recorded — see step 2. |
| The sprite sheet's top rows are wrong | HUD runs along a fixed row and is excluded; if your game puts HUD elsewhere, check the band before trusting those cells. |
| A figure is two figures fused together | Sprite grouping is by adjacency, so two bodies that touch become one box. Mark it `multiple` in a review, or split it by hand — the kit's box is a grouping, not a truth. |
| `check-coverage` refuses your baseline as "not sheet-derived" | You pointed it at the recorder's manifest. Its baseline must be a manifest `build` wrote, taken before the repaint — see step 5. |
| `error: stage1-000.png: painted at 1x while metatiles.png is at 4x` | Every sheet in a pack shares one `<scale>`. Give `artist_map.py --scale N` the same N the recording's `textures/hires.txt` declares under `<scale>` (4 for the NES bootstrap packs here). |

## Related

- [`hd-pack-authoring.md`](hd-pack-authoring.md) — getting a finished pack validated and listed.
- [`ai-kit-review.md`](ai-kit-review.md) — the AI proposer protocol and how a review is scored.
- [`enhancement-ecosystem.md`](enhancement-ecosystem.md) — what MEP is, for newcomers.
- [`../scripts/stages/README.md`](../scripts/stages/README.md) — route script format and how stages are reached headlessly.
- `docs/adr/0182`–`0189` — the decisions behind per-stage coverage, the kit's four surfaces, the RAM-cheat rule, the TAS driver, the CDL map, the AI reviewer and the emitted conditions.
