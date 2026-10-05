# Manual shader sweep (macOS)

A maintainer tool that pushes every RetroArch `.slangp` preset of a
slang-shaders library through the macOS Metal presenter
([ADR-0237](adr/0237-macos-gets-shader-support-through-a-native-metal-renderer.md))
and through `GetShaderParams`, and says which ones load, render, fall back or
take the process down.

It is **manual on purpose**. It needs a Metal device, and its numbers depend on
the machine's GPU and driver, so it is not part of `make doc-checks`,
`make python-tests` or any GitHub workflow, and must not become part of one.
Run it after moving the librashader pin, after touching
`MacOS/MetalPresenter.mm` or `Utilities/Video/LibrashaderUtilities.*`, or
before telling users which presets work.

## Prerequisites

- macOS on Apple Silicon with a Metal device. Command Line Tools are enough;
  where `/usr/bin/make` refuses to run without an Xcode licence, use
  `/Library/Developer/CommandLineTools/usr/bin/make` with
  `CXX="/Library/Developer/CommandLineTools/usr/bin/clang++ -isysroot /Library/Developer/CommandLineTools/SDKs/MacOSX.sdk"`.
- `UI/Dependencies/librashader.dylib`, from `scripts/fetch_librashader_macos.sh`
  (the sha256-pinned build; `--from <dylib>` for a local one).
- `InteropDLL/obj.osx-arm64/MesenCore.dylib`. `make shader-sweep` depends on
  it, so a stale or missing core is rebuilt first.
- A slang-shaders library: a checkout of
  `https://github.com/libretro/slang-shaders`, or the `Shaders` folder the app
  uses. That folder is `<data folder>/Shaders`, and the data folder is **not
  always** named `MesenAI`: `HomeFolderChoice.Resolve` adopts a legacy folder
  whenever it already holds a `settings.json`, so an install that predates the
  rename reads from `~/Library/Application Support/MesenCE` (or `Mesen2`).
  Point `SHADERS` at the one this machine actually uses — a sweep aimed at the
  wrong folder silently omits every preset installed under the other. The sweep
  only reads it. Write down which revision you swept; the summary records the
  path, not the revision.
- `python3` 3.9 or later, standard library only.

## Run

```bash
scripts/fetch_librashader_macos.sh
make shader-sweep SHADERS=~/src/slang-shaders
# a quick run: 30 evenly spaced presets plus two named ones
make shader-sweep SHADERS=~/src/slang-shaders \
  SWEEP_ARGS="--sample 28 --preset crt/crt-geom-deluxe.slangp --preset bezel/scanline-classic/presets/fhd-hdr/consumer/aaa-generic-ntsc-composite.slangp"
# the script directly, e.g. only the Mega Bezel presets with 300 frames each
python3 scripts/shader_sweep.py ~/src/slang-shaders --glob 'bezel/Mega_Bezel/*' --nframes 300 --timeout 120
```

`make shader-sweep-tool` builds only the harness (`scripts/shader_sweep_shot`).
`MESEN_SWEEP_INJECT_FRAME_FAILURES=<n>` in the environment makes the harness
fail its first `n` frame calls on purpose, which checks that the sweep still
reports such frames as FRAME_FAIL. No real preset is known to make that call
fail on demand.

Options of `scripts/shader_sweep.py`:

| Option | Default | Meaning |
| --- | --- | --- |
| `--out DIR` | `runs/shader-sweep-<timestamp>` | where `sweep.tsv` and `summary.txt` go |
| `--jobs N` | 4 | presets in flight at once |
| `--timeout S` | 60 | seconds per process; past it the process group is killed |
| `--nframes N` | 60 | frames presented per preset before the readback |
| `--scale N` | 4 | drawable = frame size × N (1024×960 for the built-in frame) |
| `--frame PNG` | `builtin` | reference frame |
| `--glob PAT` | all | fnmatch on the path relative to the library, repeatable |
| `--sample N` | 0 (all) | keep N evenly spaced presets after `--glob` (deterministic) |
| `--preset REL` | - | always include this preset, repeatable |
| `--keep-images` | off | write every presented frame to `<out>/img/` |
| `--no-params` | off | skip the `GetShaderParams` pass |
| `--no-recheck` | off | skip re-running each FRAME_FAIL alone after the sweep |
| `--no-caffeinate` | off | the sweep holds a `caffeinate -ims` assertion for its own lifetime unless told not to, so a long run does not stall when the Mac idles |
| `--core`, `--libdir` | repo paths | another `MesenCore.dylib` / folder holding `librashader.dylib` |

A full library (about 2,700 presets) takes tens of minutes at 4 jobs.
More jobs share one GPU and mostly lengthen each process. A temporal preset
that needs a long `--nframes` also needs a longer `--timeout`.

## What it does

For each preset, in separate processes so one crash or hang costs one row:

1. **Render.** `scripts/shader_sweep_shot` attaches the shipped
   `MetalPresenter` to an offscreen `NSView`, as `make metal-presenter-tests`
   does. It loads the preset, presents the reference frame `--nframes` times
   with readback on, and compares the last drawable with a CPU
   nearest-neighbour scale of the frame. That scale is what the presenter
   shows with no shader.
2. **Params.** The same script, in a child process with `UI/Dependencies/` as
   its working directory (where `./librashader.dylib` resolves), makes the two
   `GetShaderParams` calls `UI/Interop/ConfigApi.cs` makes: the count first,
   then the list.

Before the sweep a **control** runs with no shader. It must come out IDENTICAL,
and `CheckShaderSupport` must be true. Otherwise the comparison or the library
is broken, and the sweep stops with exit 2 rather than report numbers that
mean nothing.

### Why a built-in frame

The default reference is a 256×240 pattern generated inside the harness:
flat NES-palette tiles on black, a 1-pixel checkerboard, a grey ramp and a
grid. It was chosen over a real frame because:

- the ROM library is not in the repo;
- a checked-in emulator screenshot is derivative game content, which
  `CONTRIBUTING.md` keeps out of the repo except for short `docs/media/`
  demonstration excerpts;
- a generated pattern needs no binary fixture and is the same on every
  machine.

It has sharp edges, flat areas, black and a full ramp, so a filter that blurs,
masks, curves or recolours changes a measurable number of pixels. What it
cannot show is how a preset looks on real game art. For that, pass
`--frame <png>` with a `headless_record` screenshot of your own.

## Reading the output

`sweep.tsv` columns: `preset class detail secs rc pclass pdetail psecs`.

Render classes:

| Class | Meaning |
| --- | --- |
| `APPLIED` | the presented frame differs from the unfiltered one |
| `IDENTICAL` | the chain ran and the output equals the unfiltered frame. Either a pass-through preset, or one whose effect has not shown up within `--nframes`, so re-run it with more frames before calling it broken |
| `PARSE_FAIL` | `preset_create_with_options` refused the file |
| `CHAIN_FAIL` | the preset parsed but `mtl_filter_chain_create` failed (e.g. `UnknownSemantics("EnableHDR")`); the app shows it unfiltered |
| `FRAME_FAIL` | a frame failed. Either its command buffer failed on the GPU (a fault or hang the driver gave up on; the presenter drops the chain, #584), or `mtl_filter_chain_frame` returned an error (the harness's `frame=1`, read from `MetalPresenter::TakeFrameError`, #593). The second kind is presented unfiltered while `Present()` still succeeds, so its pixels alone would read as IDENTICAL. The detail is the first error. It failed again when re-run alone |
| `FRAME_FLAKY` | it was FRAME_FAIL in the parallel pass, but rendered when re-run alone after the sweep. The detail shows both results. A GPU fault in one process can also fail the command buffers of the presets running beside it: in a 30-preset run, plain `interpolation/lanczos16-AR` failed next to a faulting `scanline-classic` preset and rendered fine alone. Some presets also fault only on some runs. `--no-recheck` skips the re-run and leaves them FRAME_FAIL |
| `CRASH` | the process died (signal or non-zero exit). This is a bug to file |
| `TIMEOUT` | no result within `--timeout`. Either a real hang, or a preset slower than the budget |

Params classes: `OK` (count = list > 0), `ZERO` (no parameters, or a preset
the export could not parse: it reports both as 0), `MISMATCH` (the count call
and the list call disagree, which overflows the caller's buffer in the app),
`CRASH`, `TIMEOUT`.

`summary.txt` repeats the settings, the librashader sha256 and the counts, and
lists every CRASH/TIMEOUT row. **The exit code is 1 when any render or params
process crashed or timed out**, 2 on a setup error, 0 otherwise. The other
classes are findings about the presets, not failures of the tool.

## Reference numbers

The throwaway sweep this tool replaces (2026-10-02, Apple Silicon, pinned
librashader `01febce6`, 2,659 presets in the app's `Shaders` folder) used a
real NES frame at scale 2 and 3 frames, 4 jobs, and no serial re-check, so
its FRAME_FAIL count includes what this tool would call FRAME_FLAKY:

- render: APPLIED 2316, CHAIN_FAIL 183 (172 of them `EnableHDR`), IDENTICAL 77,
  FRAME_FAIL 71, PARSE_FAIL 11, TIMEOUT 1 (`deinterlacing/nnedi3-nns256-deinterlacing`);
- params: OK 2528, ZERO 131, no crash.

A second pass ran 300 frames over the 1,321 presets that were IDENTICAL, or
APPLIED under `bezel/`, at 3 frames. All 77 IDENTICAL stayed IDENTICAL. 61
presets that were APPLIED at 3 frames became FRAME_FAIL. A short run can
therefore hide a GPU fault, and an APPLIED result at a few frames is not proof
that a preset holds up.
These numbers come from the prototype, not from this tool's defaults (built-in
frame, scale 4, 60 frames). Compare runs that used the same settings.
