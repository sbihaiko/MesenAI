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

### Two flags worth knowing before you record

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

Measured on Contra against the Contra80s reference: blind route recording
reached **53.8%**, adding eleven per-stage and per-boss sessions took it to
**58.9%**, and the union of everything reached **64.6%** — 367 tiles the archive
had never held. That is the shape of the payoff: the last few percent is exactly
the part no single playthrough contains.

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
| `artist_map.py` | `<kit>/map/` | the stage stitched into one long panorama, addressable per 8x8 cell — the shape of Contra80s `Stage1a.png` (6696x480 at scale 2, i.e. 3348x240 logical). A panorama is only as long as the camera actually travelled, so a short recording gives a short strip |
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

Do **not** reach for `mep_build.py check-coverage` here. Its `--baseline` wants
the manifest the keys came from, and on a pack that came out of the recorder
that comparison is not meaningful yet: the recorder keys 2102 `<tile>` rules
covering `textures/chr/` and `textures/backgrounds/` as well, while `build`
re-derives only the keys its sheets claim — 228 on the same pack. Pointing
`--baseline` at the recorder's manifest therefore reports a 381 → 228 drop on an
**unmodified** copy, and pointing it at the post-build manifest resolves 0 keys
and passes vacuously. Use the `--verify` round trip instead; it compares like
with like.

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
| A stage's tiles are missing from the coverage table | No recording reached them. Record that stage — `artist_cover.py`'s per-state table names which state exhibited what. |
| The sprite sheet's top rows are wrong | HUD runs along a fixed row and is excluded; if your game puts HUD elsewhere, check the band before trusting those cells. |
| A figure is two figures fused together | Sprite grouping is by adjacency, so two bodies that touch become one box. Mark it `multiple` in a review, or split it by hand — the kit's box is a grouping, not a truth. |
| `check-coverage` says your repaint dropped art | On a bootstrap-originated pack it says that about an untouched rebuild too — see step 5. Use the generators' `--verify`. |
| `error: stage1-000.png: painted at 1x while metatiles.png is at 4x` | Every sheet in a pack shares one `<scale>`. Give `artist_map.py --scale N` the same N the recording's `textures/hires.txt` declares under `<scale>` (4 for the NES bootstrap packs here). |

## Related

- [`hd-pack-authoring.md`](hd-pack-authoring.md) — getting a finished pack validated and listed.
- [`ai-kit-review.md`](ai-kit-review.md) — the AI proposer protocol and how a review is scored.
- [`enhancement-ecosystem.md`](enhancement-ecosystem.md) — what MEP is, for newcomers.
- [`../scripts/stages/README.md`](../scripts/stages/README.md) — route script format and how stages are reached headlessly.
- `docs/adr/0182`–`0189` — the decisions behind per-stage coverage, the kit's four surfaces, the RAM-cheat rule, the TAS driver, the CDL map, the AI reviewer and the emitted conditions.
