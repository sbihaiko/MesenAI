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

### Recording a whole folder of ROMs, unattended

When you have route sets already, the per-ROM steps above can run as one job:

```sh
scripts/record_library.sh <roms-dir> <out-dir> 60
```

For each ROM it computes the No-Intro SHA1, picks the best driver that matches
it — a declared route set, then a `.bk2` for that exact dump, then a lone entry
script — mints whatever save states the set needs into its own scratch copy,
records, builds the kit, and writes `<out-dir>/library-report.md`: one row per
ROM with the driver, retained frames, `seen` %, surface counts and the kit's
`--verify` result. It never waits for you, and a ROM it cannot record is a row
saying why, not a stop.

A ROM that matches nothing gets driver `static` and no kit — there is nothing to
record. A route set is matched only through its `stage-set.json`
(`scripts/stages/README.md`), never by folder name.

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

The generated `textures/hires.txt` carries, in its header block right after
the `<options>` line, a bare `<bgPreservesBehindBgSprites>` line. It asks
MesenAI to keep a behind-background sprite visible where the ROM's background
is colour 0, even under a recorded `<background>` screen — without it, the
screen paints over the sprite (ADR-0224). It is opt-in per pack: the recorder
writes it on every pack it produces; a hand-written pack opts in by adding
the same line; a pack without the line — every community pack in the
catalog today included — renders exactly as it always did when it loads on
its own. The exception is a stack: when a pack without the line is the human
`mep/` layer over a recorded `auto/` layer that carries it, the flag is ORed
upward and the combined pack is opted in, so that pack's layer-2 screens stop
hiding behind-background sprites too. It is a tag rather
than an `<options>` token because an unknown `<options>` token is a load
error in Mesen 2 and HD Mesen, while an unknown tag is skipped, so the pack
still loads there, only without the effect. One edge to know: the tag undoes
the layer-2 screen only. If you add a **foreground** background (priority
30–39) on top, it covers the restored sprite where it is opaque, the same way
it covers any front sprite (ADR-0224, amended 2026-09-22).

**Clear it before you record the same ROM again.** Anything already dressing
this ROM — the `auto/` folder, but also a `mep/` layer beside it — makes the
next `bootstrap` run decline to record tiles, so a second run over the same
folder leaves the pack you already had. It says so, on its own line, since
issue #229 was fixed on 2026-09-14:

```
[MEP] bootstrap: no tiles were recorded - '<rom>' already dresses this ROM. Only the audio section was written by this run; the textures are unchanged.
```

Read that line as *the pack on disk is the old one*. Clear `auto/`, but **move
a `mep/` layer aside rather than deleting the whole folder**, because since
ADR-0146 that is also where an accepted community pack installs itself:

```sh
ROMDIR="<rom dir>/<rom stem>"
[ -f "$ROMDIR/mep/.mep-install.json" ] && mv "$ROMDIR/mep" "$ROMDIR/mep.community"
rm -rf "$ROMDIR/auto" "<rom dir>/.bootstrap"
```

Or record through `scripts/record_stages.sh`, which gives every run its own
directory with a hard link to the ROM and clears any pack left there.

The decline itself is deliberate — it is what keeps a short session from
erasing a long recording — so the message is the fix, not the behaviour.

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
Passing the pack you named positionally — or the same recording twice — adds
nothing rather than failing: the repeat is ignored, and the kit's `notes[]` says
which argument it was.

---

## 4. Paint

**Your deliverable is the PNGs.** Paint `<kit>/sheets/*.png`, `<kit>/chr/*.png`
and `<kit>/map/*.png` in place, at the scale the sheet was generated at. `<kit>/ARTIST.md`
states the rules that matter — keep every image's size and the position of
everything inside it, keep transparency transparent, and **never paint a
`.orig.png`**: that is the untouched reference the rebuild is checked against,
and painting it is how your work becomes invisible.

### Open, paint, save

**Open the surface, paint it, save back over the same file, then ask the
running game for it with *HD Packs > Reload Repainted Images*** — you do not
reopen the ROM and you do not lose where you are standing (F12.3, ADR-0212).

Each surface's file name is also the name to export to, so after the first save
it is one shortcut (F12.4, ADR-0213). `kit.json` carries that name per surface
as `assetName`:

| program | the one step |
|---|---|
| GIMP | *File > Overwrite `<name>.png`* |
| Aseprite | *File > Export* once, then *Repeat last export* |
| Krita | *File > Export* once, then *File > Export* again over the same path |
| Photoshop | *File > Generate > Image Assets*, with your layer named exactly `<name>.png` |

Photoshop is the one exception, and it is worth knowing before you start: its
generator always writes into a `<document>-assets` folder beside the `.psd`, and
that location cannot be changed. The file it writes has the right name, so
copying it over the kit's copy is the whole difference. The other three
overwrite the kit file directly, and the reload picks it up from there.

A resized canvas is refused rather than half-applied: the reload keeps the old
pixels and logs the two sizes, because the sheet's sidecar names a crop that a
smaller image no longer holds. Repaint at the size you were given.

### Mark a figure and paint it whole

A `sprNNN` sheet cuts a character into the fragments the recorder grouped, and
a soldier painted at the waist is hard to paint well. To paint the character
instead, export the figure as one PNG (ADR-0209 Q2, the `sprNNN` unit of
ADR-0168 reassembled through the offsets the pack already recorded), paint
that, and bring it back onto the cells it came from (ADR-0209 Q3, the same
explicit return path as any other surface):

```sh
python3 scripts/mep_figure.py export out/painted spr000 --out out/kit/figures
#   -> out/kit/figures/spr000-figure.png       paint this one
#      out/kit/figures/spr000-figure.orig.png  never this one (the 1x reference)
#      out/kit/figures/spr000-figure.json      which sheet cell each rect came from
python3 scripts/mep_figure.py import out/painted out/kit/figures/spr000-figure.png --verify
python3 scripts/mep_build.py build out/painted
```

Then *HD Packs > Reload Repainted Images*, as above. `export` takes a
`sprNNN`, an `objNNN` or a `poseNNN` id; the printed `layout from poses` /
`layout from walk` line says whether the layout is the silhouette the recorder
saw in one frame (`sheets/poses.json`) or the ADR-0168 evidence walk a pack
recorded before that sidecar falls back to — the walk is a guess, and any
member it could not place is listed rather than drawn at a guessed spot.
`import` writes only the cells that differ from the `.orig.png` twin, into the
sheet each came from (the `sprNNN` sheet itself, or `sprites.png` for a member
the group does not hold), and leaves everything else alone; `--verify` rebuilds
a throwaway copy and asserts the `(tileData, palette)` key set is unchanged.
The surface is at the pack's scale like every other sheet, and a resized
figure is refused the same way. The figure also gets its `<id>-figure.ora`
beside the pair — four layers, `paint` on top of the untouched figure, the cell
grid and the figure's id hidden in `guides` (see "The layered file" below); the
flat PNG over the F12.4 name is still the only way back.

### The layered file, for GIMP, Krita and MyPaint (F12.11, ADR-0220)

Beside every surface the kit also writes `<name>.ora`, an OpenRaster file the
three programs above open natively — the same picture as layers, bottom to top:

| layer | what it is | flags |
|---|---|---|
| `orig` | the `*.orig.png` twin, pixel for pixel | visible, locked |
| `context` | the stage around the surface at 1x, at 50 % — only on a surface whose every cell has a stage position: today the stage panoramas (five layers); figure, scenery and CHR sheets have four until a recording writes where a sprite was seen on the stage (ADR-0220 §3, amended 2026-09-22) | visible, movable |
| `paint` | empty — **the one layer you paint on**; it opens as the topmost visible layer | visible |
| `guides` | cell grid, captions from the recording's ids or your `names.json` (wrapped to the canvas, at most two lines, cut with `...` when they still do not fit), a hatch over every cell nothing saw in play | **hidden**, locked |
| `palettes` | a swatch strip of the palettes recorded for the sheet, in the order they first appear reading down the sheet, each group labelled with the first cell that wears it (a static page states `defaultTile = Y` instead) | **hidden**, locked |

**Select `paint` in the Layers panel before your first stroke.** Measured on
2026-09-23: GIMP 2.10 and Krita 5.3 both open an OpenRaster file with the
*bottom* layer active — `orig`, the locked reference — whatever the stack
order, and no order fixes it: putting `paint` at the bottom would make it the
active layer but would hide every stroke under `orig`. Krita honours the lock
and refuses the stroke; GIMP ignores `edit-locked` and lets you paint on
`orig`, where the work is discarded on the next kit run (ADR-0220 §3, amended
2026-09-23). One click on `paint` before painting is the whole fix.

**The `.ora` is a starting point; the flat PNG is the deliverable.** Paint on
`paint`, then export a flat PNG over `<name>.png` — the F12.4 name in the table
above — exactly as you would without the layered file. The return path does not
change: `mep_build` keeps only the cells that differ from the twin, the reload
puts them on screen, and **nothing reads the `.ora` back** — not the rebuild,
not the reload, not `mep_lint.py`. That refusal is deliberate and tested
(ADR-0220 §5): the sheet PNG stays the only source of truth.

Keep `guides` and `palettes` hidden when you export. Both are drawn in one
magenta, `#FF00FD`, that no NES palette reaches, and the kit checks the value is
absent from the artwork before writing; a flat export that still carries it in a
cell is refused by `python3 scripts/mep_lint.py`, which names the sheet and the
cell (`index` and `(x, y)`). Photoshop and Aseprite do not open `.ora` and stay
on the per-surface names above — there is no `.psd`, `.aseprite` or `.kra`
writer, on purpose. A surface that carries a `context` layer is taller than its
cell grid, and its `*.orig.png` twin grew with it: paint at the size you were
given, the cells have not moved.

Do not hand-edit `textures/hires.txt`. It is generated from the sheets by
`mep_build.py build`, and an edit there is thrown away on the next build. Copy
each sheet **together with its `.json`** — the sidecar is the slicing contract
that says which cell is which tile.

### What a cell is

Adding a tile by hand means adding a cell to that sidecar, so here is the shape
of one. Nothing else in this guide spells it out, and both 2026-09-19 cold-read
runs had to reverse-engineer it from the cells already there:

```json
"cell": { "w": 16, "h": 16 }, "columns": 6, "gutter": 1,
"cells": [
  { "index": 23, "x": 52, "y": 69, "count": 1, "context": "misc", "label": "tree-12,8",
    "tiles": [ { "tile": "<32 hex>", "palette": "<8 hex>" } ] }
]
```

| Field | What it is |
|---|---|
| `index` | the cell's ordinal in this sheet. Unique; it is what `metatile`, `additions[]` and the condition blocks cite. |
| `x`, `y` | the cell's top-left in the **1× pixel space** of the `.orig.png`, *not* the PNG you paint on. A pack at `scale` 4 puts the same cell at `4x,4y` there. |
| `count` | how many tiles the cell holds. Free — it is not checked against `tiles[]`. |
| `tiles[]` | one entry per tile the cell carries, in the cell's own order. |
| `context`, `label`, `metatile` | free text and an optional back-reference the tools write and carry. Copy them off a neighbouring cell; nothing requires them. Since ADR-0209 Q1 the recorder fills `label` with a default it infers from the grouping (`scene #142 x1004`, and on a `sprNNN`/`objNNN` sheet, a pose or a cycle in `poses.json` a one-line statement of its size and counts) beside `"labelSource": "inferred"`; to rename, edit the `label` and set `labelSource` to anything else, or put the name in a `names.json` handed to `artist_kit.py --names` / `mep_figure.py export --names`, which always wins over the inferred one. |
| `columns`, `cell`, `gutter` | the grid: how many cells fit across, each cell's size, and the transparent margin between them. Written above `cells[]`, not inside it. |
| `kind` | what the surface holds: `unsorted`, `misc`, `metatiles`, `object`, `sprite`, `sprites`, `font`, `hud`, `map`. With `cell` it decides which sheet a new cell belongs on — see *Which sheet a copied key goes on*. |
| `emptySlots` | `{ "col": n, "row": n }` per **blank** slot, written by the generators on the object and sprite sheets. It is not free space: a group sheet's grid is the bounding box of an L-shaped or otherwise non-rectangular figure, so a blank states where the figure is *not*, and the recorder never fills it (ADR-0175). A background key put in one lands in the hole of a named figure. To place a new cell, use a free-form sheet; where there is none, compute the slot from `columns`, `cell` and `gutter` — `metatiles.json` does not carry the list. |

A `tiles[]` entry is `{"tile": "<32 uppercase hex>", "palette": "<8 hex>"}` —
the same two strings *Copy as MEP sheet cell* puts on your clipboard, inside
the `{"count": …, "tiles": […]}` wrapper it copies (see *Which sheet a copied
key goes on*). Two optional fields appear only where they mean something:

- `"index": <n>` — the tile's CHR index (ADR-0172), present only on a CHR ROM
  game, where `hires.txt` keys by index rather than by bitmap data. Paste the
  action's text as-is; it already carries this on those games and omits it
  elsewhere.
- `"source"` and `"mirror"` — written by the recorder for a shape it recorded
  with an OAM flip baked in (ADR-0178), so the rebuild can emit the unflipped
  key the run time looks up. Never author these by hand.

**A cell may hold a single 8×8 tile.** The 16×16 cell grid is a layout
convention, not a constraint: `count: 1` with one entry in `tiles[]` builds,
lints and renders, which is how a one-off repaint of one tile is done.

**Where the pack's `scale` is written: the `<scale>` line at the top of
`textures/hires.txt`.** Every sheet in a pack shares that one number (4 for the
NES bootstrap packs here), and nothing else states it — not the sidecars, and
not `pack.json`, which a folder-convention pack does not have at all. The
`<sheet>.png` ÷ `<sheet>.orig.png` size ratio is the same number if you would
rather not open a generated file, and that ratio is how most of the 2026-09-19
cold reads got it. You need it twice: to paint the cell at `scale × x, scale × y`
on the sheet, and to grow both PNGs consistently when the grid has no free slot.
Get the factor wrong and the paint lands on a different cell: `build` exits 0,
`mep_lint.py` exits 0, and the screen does not change. Reading `hires.txt` is
fine — only *editing* it is thrown away.

### The tilemap is a picture of the nametable, not of the screen

*Copy as MEP sheet cell* lives in the Tilemap Viewer's right-click menu, and the
picture you right-click is **one nametable** — the whole 32×30 map in map order,
labelled `($2000)` in the same menu. The screen is a **scrolled window** into it,
and nothing in the viewer marks where that window is. Three consequences, all
measured on 2026-09-19:

- **A tile can be in the map and off the screen.** Zelda II's cold read picked a
  full-width rock band at row 0 that looked exactly like the rock on the frame,
  added the cell, painted it, built, linted, deployed and rendered — four green
  exit codes — and got a byte-identical frame. The logo sat at map rows 19–25 and
  at screen rows 6–12, so the map was scrolled ~13 rows and row 0 was above the
  top of the screen. Anchor on a landmark that is in both pictures (a word, a HUD
  digit, a platform band) and work out the offset before you trust a coordinate:
  Contra's run read `PLAY` at map col 5 and screen col 27, so frame col = map
  col + 22 and only map cols 0–9 were on screen at all.
- **A nametable can hold art the frame never displays.** Bubble Bobble's attract
  frame keeps the whole "Bubble Bobble" logo in VRAM while the background layer
  draws the backdrop: 59 917 of the frame's 61 440 pixels are one colour and the
  logo is not fetched. A key copied there is well-formed and unreachable.
- **The viewer's own picture is not the frame's picture.** It draws patterns in a
  flat palette, so the shapes read differently from the game's colours, and on a
  CHR-banked game it can draw the tile from a different bank than the PPU used
  for that scanline — Ninja Gaiden's viewer shows glyphs where the frame shows a
  boulder. Issue #341 tracks the copy emitting a key from the wrong bank; until
  it is fixed, check that the tile you copied is the shape you meant by its
  coordinates, not by the thumbnail.

If your repaint builds clean, lints clean and changes nothing, "the tile I picked
is not on this frame" is the first thing to rule out — it is cheaper than
bisecting the pack.

### Which sheet a copied key goes on

**The short path: let `mep_add_cell.py` place it.**

```sh
scripts/mep_add_cell.py out/painted --paste     # or: … cell.json, or pipe it in with -
```

It reads the copied text, picks the sheet by the rule below, finds the free
slot, writes the cell, and grows `<sheet>.png` and `<sheet>.orig.png` together
when the grid is full — the four hand steps this section and the two below it
describe. It also says, before you build, whether another sheet already claims
the key and whether that cell was painted. `--dry-run` reports the placement
without writing anything.

Two things it will not do. It refuses a key whose palette leads with `FF` —
that is how a sprite's transparent color 0 is packed, and a background key
does not belong on a sprite sheet (`--allow-sprite-palette` if you know
better; 131 of the 3 495 cells on the 30 packs' `unsorted` sheets are keyed
that way legitimately). And it refuses to grow a sheet whose `.orig.png` twin
it cannot read, rather than write one of the two files: a sheet grown without
its twin makes the build treat **every** cell of that sheet as painted, which
is silent and changes cells you never touched.

**What is on the clipboard.** *Copy as MEP sheet cell* puts a whole cell
there, **unplaced**:

```json
{"count": 1, "tiles": [{"tile": "<32 hex>", "palette": "<8 hex>", "index": 486}]}
```

No `index`, `x` or `y` of the cell's own — those depend on the sheet, which
the emulator never opens. **The `index` you can see is not the cell's.** It is
inside `tiles[]` and it is the tile's absolute CHR index (ADR-0172); the
cell's own `index` is its ordinal in the sheet, and `mep_add_cell.py` sets
that one. Copying the tile's index into the cell's field gives you a cell at a
slot that already exists. A 16-pixel-tall sprite copies as **two lines, two
cells** — a cell's `tiles[]` is laid out row-major 2×2, so a second entry
would draw to the *right* of the first, not below it.

Pasting the text into a sidecar's `cells[]` by hand still works, and the
`tiles[]` entry inside it is unchanged, so the manual procedure below is still
correct — it is just longer.

**The rule the placer applies, and the one to apply by hand.** Nothing in the
copied text names a sheet, and the `context` field cannot help: a pack that
ships both `misc.json` and `unsorted.json` usually labels every cell of both
`"context": "misc"`. Eleven 2026-09-19 cold reads hit this and every one of
them had to work it out from the sheet headers instead:

- `cell: { "w": 8, "h": 8 }` — the cell holds **one** tile, which is what you
  copied. This is the free-form sheet, `unsorted.json`, on all 27 of the 30
  packs here that ship one.
- `cell: { "w": 16, "h": 16 }` — a metatile, 2×2 tiles: `misc.json` (21 packs)
  and `metatiles.json` (all 30). A single pasted entry here is legal but
  leaves three quadrants of the cell as they were.
- `kind` and `gridUnit` in the same header say what the sheet is for. **`kind`
  alone does not choose the sheet, and neither does the grid alone.**
  `sprite` and `sprites` sheets are 8×8 too — 747 of them across those 30
  packs, against 27 `unsorted` — and a background key put on one never
  reaches the background. The sheet you want is the 8×8 one that is not a
  named figure: not `sprite`, `sprites`, `object`, `font`, `hud` or `map`.
  That lands on `unsorted`, and on `misc` only when the pack ships no
  `unsorted`.

**Some packs ship no free-form sheet at all.** Zelda II and Donkey Kong have
neither `misc.json` nor `unsorted.json`: their `textures/sheets/` holds
`metatiles.json`, one `objNNN.json` per recovered background object, the sprite
sheets, and `adjacency.json` / `poses.json`, which are not paintable surfaces.
Every cell in every one of those sheets reads `"context": "scene"`, so the
`context` field routes nothing there either. The destination rule is `kind`
plus the grid:

| `kind` | Takes a background key? |
|---|---|
| `unsorted` | yes — the free-form one-tile sheet (8×8 cells), the destination whenever the pack has one |
| `misc` | yes — the free-form 16×16 sheet, when there is no `unsorted` |
| `metatiles` | yes — the destination when there is neither. 16×16 cells, four tiles each; a `count: 1` cell with one `tiles[]` entry is legal beside them |
| `object`, `font`, `hud` | yes, but each is one recovered thing and is small; use one only when your key belongs to it |
| `map` | not the free-form destination — a nametable surface (`map-NNN.json`); use it only when you mean to repaint the map itself |
| `sprite`, `sprites` | no — a background key put here never reaches the background |

Finding the free slot, by hand (`mep_add_cell.py` does all of this for you, and
grows the sheet when there is no free slot): on `metatiles.json` you compute it
from the header. The
`emptySlots` list on `objNNN.json` and `sprNNN.json` is **not** a slot you may
take — it is the blank half of a figure's own grid (see the field table above),
and those sheets are not where a background key goes in the first place.
Zelda II's worked example: `columns: 18`, `cell: 16×16`, `gutter: 1`,
`metatiles.orig.png` 307×290, 298 cells. The pitch is 17, so the grid is 18×17 =
306 slots and 8 are free; index 298 lands at `x = 1 + 17×10 = 171`,
`y = 1 + 17×16 = 273`, painted at `(684, 1092)` on the scale-4 PNG, and no
resize is needed. Donkey Kong is the same shape: `columns: 13`, a 222×222
reference, 169 slots, 161 used, 8 free.

Two traps around it, both measured:

- **A key may already be claimed by another sheet.** Paste anyway: the build
  logs which cell won — `sheets/unsorted.png overrides tile <key> from
  sheets/misc.png (painted)` — and a *painted* cell beats an untouched one.
  Check that line; a cell that loses builds and lints clean and changes
  nothing. `mep_add_cell.py` says it at paste time instead
  (`metatiles.json cell 1 already claims … (painted)`), which is two steps
  earlier, and it reads the paint state through the build's own probe, so the
  two never disagree.
- **Find a free slot, do not guess one.** A cell you overwrite silently
  repaints whatever tile already lived there. The grid is `columns` wide and
  `cells[]` is in row-major order, so the free slots are the ones no `index`
  claims.

- **A painted cell that reaches nothing now says so.** `build` names the sheet
  and the key in two cases: a painted key another crop already owns
  (`... painted tile key(s) were already claimed by another crop ... (#343)`),
  and a painted key a capture also draws (`... also drawn by the captured
  screen backgrounds/screenNNN.png ... (#338)`). Both are per sheet and count
  only cells you actually painted, so silence about your sheet means the paint
  did reach the manifest.

Finally: a frame a captured screen owns is drawn from `backgrounds/`, and on
it no cell of any sheet reaches the screen. `build` warns that captures exist;
the recorder log (`auto layer has N captured screen(s) - not used under the
human layer`) is what tells you whether one applies. If your repaint builds
clean, lints clean and changes nothing, move `textures/backgrounds/` aside and
render again — that is the diagnosis, not a rebuild.

**A capture is gated on probes, not on the frame.** The recorder writes three
`<condition>` lines per capture (`screenNNN_A/_B/_C`, `tileAtPosition`), so any
frame that happens to match those few tiles gets the capture drawn over it —
including frames it was never frozen for, whose own art it then erases. Mike
Tyson's Punch-Out!! is the measured case: the pre-fight card renders with the
game's own `STARRING` / `LITTLE MAC` text missing, and no capture in the pack
matches that frame exactly (the closest, `screen003.png`, is 16 047 pixels
away). ADR-0050 and ADR-0156 make a *present* capture's precedence deliberate
and that has not changed; what changed is that the build now says the gate is
approximate, and that you have a way out.

**Retiring a capture.** Delete `textures/backgrounds/screenNNN.png` (and the
`auto/textures/` copy, if the pack still carries the recorder layer) and
rebuild. `build` drops the `<background>` line with it and reports
`info: retired N captured screen(s) ... (#344)`; those frames are drawn from
the sheets again. Deleting the PNG used to fail the build with one
`error: <background> ... does not exist` per file even though the engine
itself drops a dangling entry harmlessly at load, which left keeping the
capture — and living with a no-op repaint — as the only option.

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
cp out/kit/scene/*.png  out/painted/textures/backgrounds/   # whole screens go back where they came from
sh -c 'for f in out/kit/figures/usr*-figure.png; do
  [ -e "$f" ] || continue                                        # no figures: nothing to import
  python3 scripts/mep_figure.py import out/painted "$f" || exit 1  # figures are imported, not copied
done' &&
scripts/mep_build.py build out/painted &&    # regenerates hires.txt, then lints
python3 scripts/mep_lint.py out/painted      # exit 0 = clean
```

`mep_build.py build` regenerates `textures/hires.txt` from `textures/sheets/*.png`
and `audio/hires.txt` from `audio/bgm|sfx/`, then runs the linter — **a lint
failure is a build failure**, so exit 0 means both happened. 0 errors with
warnings about the recorder's own sheet sizes is normal and is called out as
such in the output.

The figure loop runs after the sheet copy and before the build: a composed
figure (`<kit>/figures/usr*-figure.png`, ADR-0225) is a view, and
`mep_figure.py import` writes what was painted on it into the copy's own
sprite sheet. An unpainted figure writes nothing, so importing all of them is
safe, and the `[ -e ]` guard skips the loop when the kit exported no
figures (a background- or CHR-only kit), where the unmatched glob would
otherwise reach `mep_figure.py` as a literal path. A failed import stops the
block: a `for` loop's status is its last iteration's, so the loop exits on the
first failure and `&&` keeps the build from running with a repaint missing.
It runs under `sh -c` so that `exit` leaves only that child — never your
terminal — and so zsh's `no matches found` cannot abort a figure-less kit. A figure and its `sheets/usr*.png` row are the same tiles — paint one,
not both; if both are painted, `build` stops with a `painted tile … lost to`
error naming the tile (#399).

A `scene/` screen is the pack's own whole-screen capture,
`textures/backgrounds/screenNNN.png`, copied into the kit untouched; the
manifest's `<background>` line draws it by that name on the frames it was
frozen for, so a painted screen returns by copying it back under the same name.
An unpainted one goes back unchanged. The block above lists every folder a
kit can have, and a `cp` of a folder your kit lacks fails, so drop that line;
the block in the kit's own `ARTIST.md` already names only the folders that kit
has (#403).

That is the acceptance test: **`build` exit 0, `mep_lint.py` exit 0, and every
generator's `--verify` PASS.** All three are mechanical, they take seconds, and
a failure means the kit is wrong — never that the check is wrong.

### Optional: a condition you wrote by hand

Most packs never need one. If you do want a tile to render differently in some
situation — "this piece, but only where the sky is open to its right" — you can
write the emulator's own condition into the sheet and attach cells to it:

```json
"conditions": [
  { "name": "openToTheRight", "authored": true,
    "line": "<condition>openToTheRight,tileNearby,8,0,<32 hex>,<8 hex>" }
],
"cells": [ { "index": 37, "condition": "openToTheRight", ... } ]
```

`authored: true` is required: the toolchain never writes one of these itself, so
an unmarked block is a mistake rather than a shortcut. `mep_build.py` emits the
definition once, above the rules that cite it, and always writes the
unconditional twin behind each conditional rule — without it, a frame where the
condition does not hold falls through to the ROM's own art.

A condition is a claim about the game, and you can check it against what the
game actually drew before anyone plays it:

```sh
python3 scripts/mep_lint.py out/painted --routes runs/<run>/grid.txt ...
```

Pass the pack first; `--routes` takes every path after it, and a folder is
searched one level deep. Each route is a grid stream from step 1
(`MESEN_SHEET_GRID_DUMP`). For every condition the report says, per route,
how many drawn instances it held on and failed on, **the frame and cell of the
first failure**, and — for `tileNearby` — how many times the pattern also
occurred around a tile you did not attach it to. That last number is the one
that usually surprises people: "with open sky to the right" is true of most of
the sky.

It is a report, not a gate: exit 0 means the report was produced, not that your
conditions were right. Reading it is your job.

A `memoryCheckConstant` is checked like the rest, as long as the address is in
the console's internal RAM (`$0000`–`$07FF`) and the recording was made after
F12.6b: every retained frame carries that 2 KB window, so lint reads the byte
the emulator would have read. All 486 `memoryCheckConstant` lines of the
`Contra80s 1.1` pack are inside it.

What is still reported as **`not evaluable`**, with the reason printed, is an
address outside that window (WRAM `$6000`+, PRG, mapper registers,
`ppuMemoryCheck*`), a `memoryCheck` comparing two watched addresses, a
recording made before F12.6b, and everything that needs the sprite stream
(`spriteNearby`, `positionCheck*`). `not evaluable` is never a pass — if you
ship one, nothing has checked it.

### Look at the painted pack before you ship it

The mechanical checks prove the pack is valid; they do not replace looking at
your edit in the game. Copy the built folder beside the ROM as its `mep/`
override, then run a screenshot pass **without** `hdpack-off`.

`mep/` is a **single slot**, and you are probably not the only thing in it: an
accepted community pack is downloaded and installed into that same path
automatically, with a `.mep-install.json` stamp beside its textures (ADR-0146,
ADR-0147). Delete the folder blindly and you delete somebody else's pack — the
next ROM load downloads it again, over the very edit you are trying to look at.
Look first, then move aside rather than remove:

```sh
MEP="<rom dir>/<rom stem>/mep"
if [ -f "$MEP/.mep-install.json" ]; then
  mv "$MEP" "$MEP.community"   # somebody else's pack: aside, never delete
elif [ -d "$MEP" ]; then
  rm -rf "$MEP"                # your own earlier copy: replace, do not merge
fi
cp -R out/painted "$MEP"
scripts/headless_record <rom> 20 out/painted-check screenshot
```

The stamp is what tells the two apart, and the second branch matters as much as
the first: `cp -R` onto an earlier copy of your own **merges** the two trees
instead of replacing them, so a file you deleted since the last build stays on
disk and keeps painting.

The recorder log must say that it loaded `<rom dir>/<rom stem>/mep/textures`.
Open the resulting screenshot and confirm the exact figure you painted. The
`mep/` layer overrides `auto/` entry by entry, so your original recording stays
available underneath it.

Put the community pack back when you are done comparing — `mv
"$MEP.community" "$MEP"` — or keep your own and leave it moved aside. The
automatic install itself is the `Automatically install matching community
packs` switch in Preferences, if you would rather it stopped happening while
you work.

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
| A second `bootstrap` run changes nothing in the pack | Something already dresses the ROM — `<rom stem>/auto/`, or a `mep/` layer beside it — so the bootstrap declines and keeps it — it logs `no tiles were recorded - '<rom>' already dresses this ROM`. Clear `auto/` and the sibling `.bootstrap` stamp, moving any `mep/` aside first (step 1), or record through `record_stages.sh`. |
| `artist_map.py` refuses: "keys its tiles by CHR index" | A CHR ROM game; there is no panorama for it yet. |
| A stage's tiles are missing from the coverage table | No recording reached them. Record that stage — `artist_cover.py`'s per-state table names which state exhibited what. |
| `artist_cover.py` refuses: "different namespaces" | The reference pack is built for a patched ROM whose board has CHR ROM where the stock one has CHR RAM (or the reverse), so the two sides key tiles differently and no key can match. Record the patched ROM, or use a reference built for the ROM you recorded — see step 2. |
| The sprite sheet's top rows are wrong | HUD runs along a fixed row and is excluded; if your game puts HUD elsewhere, check the band before trusting those cells. |
| A figure is two figures fused together | Sprite grouping is by adjacency, so two bodies that touch become one box. Mark it `multiple` in a review, or split it by hand — the kit's box is a grouping, not a truth. |
| `check-coverage` refuses your baseline as "not sheet-derived" | You pointed it at the recorder's manifest. Its baseline must be a manifest `build` wrote, taken before the repaint — see step 5. |
| A pasted cell builds and lints clean and the frame does not change | Rule out the tile first: the Tilemap Viewer draws the whole nametable and the screen is a scrolled window into it, so the tile you picked may be off screen — see *The tilemap is a picture of the nametable*. Then check the scale: `x,y` are in the `.orig.png`'s 1× space and the PNG you paint is `<scale>`× that. |
| There is no `misc.json` or `unsorted.json` to paste into | That pack has no free-form sheet. Go by the sidecar's `kind`: a background key goes on `metatiles.json`, and the free slot is computed from `columns`, `cell` and `gutter` — see *Which sheet a copied key goes on*. |
| `error: stage1-000.png: painted at 1x while metatiles.png is at 4x` | Every sheet in a pack shares one `<scale>`. Give `artist_map.py --scale N` the same N the recording's `textures/hires.txt` declares under `<scale>` (4 for the NES bootstrap packs here). |

## Related

- [`hd-pack-authoring.md`](hd-pack-authoring.md) — getting a finished pack validated and listed.
- [`ai-kit-review.md`](ai-kit-review.md) — the AI proposer protocol and how a review is scored.
- [`enhancement-ecosystem.md`](enhancement-ecosystem.md) — what MEP is, for newcomers.
- [`../scripts/stages/README.md`](../scripts/stages/README.md) — route script format and how stages are reached headlessly.
- `docs/adr/0182`–`0189` — the decisions behind per-stage coverage, the kit's four surfaces, the RAM-cheat rule, the TAS driver, the CDL map, the AI reviewer and the emitted conditions.
