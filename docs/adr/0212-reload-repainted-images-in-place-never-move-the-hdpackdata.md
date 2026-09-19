# ADR-0212: A pack reload re-decodes the images that changed, in place — the `HdPackData` object never moves

- Status: **accepted 2026-09-19 and implemented the same turn as F12.3** — the PRD's F12.3 decision rule was already accepted and F12.1's measurement resolves it; this ADR records how. User go-ahead, verbatim, answering a recommendation to do F12.3 before the coverage work: *"/goal F12.3"*. CLAUDE.md allows same-turn implementation when the change ships with unit tests covering the decision and the go-ahead is quoted here and in the PR body; both hold.
- Date: 2026-09-19
- Related: PRD Part A F12.3 (reload the pack without reopening the ROM), F12.1 (scale and load measurement, `docs/validation/f12.1-scale-and-load-2026-09-17.md`), F12.4 (asset-name template), ADR-0153 (artist-legible sheets), ADR-0183 (the artist kit), ADR-0209 (the four-step artist loop)

## Context

The paint loop ADR-0209 describes has four steps: name a figure, hand it to the
artist's own editor, get the file back, see it in the game. Today the fourth
step costs a ROM reopen — the artist saves in Photoshop and has to close and
reload the game to find out whether the pixels landed. F12.3 is the slice that
removes that.

The PRD's row for F12.3 states the decision rule up front: *"full reload if it
costs under one frame budget times an agreed factor, otherwise per-image
invalidation with the strategy named in the log."* F12.1 measured the inputs on
the installed Metroid pack and the rule resolves cleanly:

- `NesConsole::LoadHdPack` — the manifest parse — takes **412 ms**. A frame
  budget is 16.7 ms. The parse is **25 frames**, so it cannot ride a frame
  boundary under any factor worth agreeing to.
- `HdPackData::LoadAsync` — the bitmap decode — takes **13.2–16.4 s for 271
  images**, 30–40× the parse. F12.1's finding 1 says it outright: *"Any F12.3
  reload design that only re-reads `hires.txt` would be fast and wrong;
  per-image invalidation is the shape this number points at."*

Three structural facts, read off the code on 2026-09-19, decide the rest.

1. **Three holders keep a raw `HdPackData*`**: `HdNesPpu::_hdData`,
   `HdVideoFilter::_hdData` and `HdNesPack::_hdData`. Nothing refcounts them.
   Replacing the object means finding and updating all three at a moment when
   none of them is dereferencing it.
2. **One of those three lives on another thread.** `HdVideoFilter::ApplyFilter`
   runs from `VideoDecoder::DecodeThread`, not from the emulation thread. So
   the pixels a reload wants to overwrite are being read concurrently, and any
   design that mutates them without draining that thread is a data race on the
   render hot path.
3. **`HdPackBitmapInfo` is already the unit of decoding.** It owns `PngName`,
   the raw `FileData` (freed after `Init()`), the decoded `PixelData`, and an
   `_initDone`-guarded `SimpleLock`. Every `HdPackTileInfo` reaches its pixels
   through a `HdPackBitmapInfo*`. Re-decoding one image *in place* therefore
   invalidates no pointer anyone holds.

Fact 3 is the one that shapes the decision: the cheap reload and the safe
reload are the same reload.

Non-goals, stated up front. This does not swap the `HdPackData` object, does
not re-parse `hires.txt`, does not reload audio, conditions, `<addition>` or
`<fallback>`, and does not touch the install or matcher paths. A pack whose
*manifest* changed still needs the ROM reopened, and the reload says so rather
than half-applying.

## Decision

### 1. The unit of reload is one image file, re-decoded in place

`HdPackData` gains a reload entry point that walks `BackgroundFileData` and
`ImageFileData`, and for each `HdPackBitmapInfo` whose backing file changed on
disk, re-reads the PNG into the **same object**: `FileData` refilled,
`PNGHelper::ReadPNG` into `PixelData`, `PremultiplyAlpha`, `FileData` released
again — exactly the sequence `Init()` already performs, minus the `_initDone`
short-circuit.

The object's address never changes. No `HdPackData*`, no `HdPackBitmapInfo*`
and no `HdPackTileInfo::Bitmap` is ever invalidated, so the three holders of
fact 1 need no coordination at all. This is the whole reason the decision is
shaped this way.

### 2. "Changed" is a stat fingerprint, recorded at load

Each `HdPackBitmapInfo` records the **absolute path** it was loaded from and a
`(size, mtime)` pair taken at load time. A reload re-stats each path and
re-decodes only where the pair moved; the fingerprint is then updated. An
unchanged pack costs one `stat` per image — sub-millisecond for 271 files — and
decodes nothing.

This is F12.1 finding 1's per-image invalidation, and it is what turns a
13–16 s operation into a single-image one for the case the artist actually
performs: repaint one sheet, save, look.

### 3. The re-decode happens on the emulation thread at frame end, after the decode thread is drained

The reload is requested asynchronously (menu action, headless flag, interop
call) and sets a pending flag. At the next frame boundary, on the emulation
thread, the console:

1. calls `VideoDecoder::WaitForAsyncFrameDecode()` — after it returns, the
   decode thread is idle and is not inside `HdVideoFilter::ApplyFilter`;
2. re-decodes the changed images;
3. clears the pending flag and logs one line.

That drain is the entire synchronisation. Locking `PixelData` on the read side
was rejected: reads are the per-pixel render hot path, and a reload is a rare,
explicit, human-initiated event. Paying for it on the writer's side, once, is
the right trade.

### 4. A repaint that resizes the canvas is refused, per image, and says so

`HdPackTileInfo` caches `X`, `Y`, `Width` and `Height` from the manifest and
indexes into `PixelData` with them. A PNG that comes back smaller would read out
of bounds. So when the re-decoded image's `Width`/`Height` differ from the ones
it had, the new pixels are **discarded**, the old image is kept, and the Core
logs:

```
[MEP] reload: <name> changed size (WxH -> W'xH') - reopen the ROM to pick it up
```

The artist's loop overwrites the same canvas, so this is the off-path case. A
resize is a manifest change in disguise, and §1's non-goal already says those
need a reopen.

### 5. A zip-backed pack is not watched

`HdPackLoader::LoadFile` reads a zipped pack through `ZipReader`; there is no
file on disk to stat. A reload requested against one logs a single line naming
the limitation and does nothing. Zipped packs are not what an artist paints
into.

### 6. Verification

- Unit tests in `scripts/core_unit_tests.cpp` over the fingerprint and
  dimension rules, host-free. **The change-detection test must not depend on
  mtime resolution** — libstdc++ resolves `last_write_time` more coarsely than
  APFS, which has flaked CI before; assert on the size change, or set the mtime
  explicitly.
- The slice's stop rule, from the PRD: a PNG overwritten on disk renders
  pixel-exact in a `headless_record` screenshot after the reload, with no state
  loss. **Met 2026-09-19**: a run that repaints mid-play and reloads lands on
  the byte-identical final frame (`0xA8693E63`) as a run that had the repaint
  from the start, and both differ from the untouched control (`0xDBA93B36`).
  The two headless flags this needs are `reload-at-frame=<n>` and
  `replace=<dst>=<src>` (repeatable); the trigger counts **emulated frames**,
  because a headless run is far faster than real time and a wall-clock trigger
  never fires.
- The hitch is **measured, not asserted**:
  `docs/validation/f12.3-reload-repainted-images-2026-09-19.md`.

## Consequences

- **A manifest edit is not picked up.** An artist who changes `hires.txt`, or
  rebuilds the project with `mep_build.py`, still reopens the ROM. This is the
  cost of keeping the `HdPackData` object immovable, and it buys the absence of
  a three-holder cross-thread swap. If the manifest case turns out to matter in
  the F12.4 loop, it is a follow-up slice that inherits this one's frame-boundary
  discipline — not a reason to move the object now.
- **The reload stalls emulation briefly**, by the decode-thread drain plus the
  re-decode plus the tile re-cut. **Measured 2026-09-19**
  (`docs/validation/f12.3-reload-repainted-images-2026-09-19.md`) on a 26-image,
  2 171-rule Zelda pack: **0 ms** when nothing changed, **2 ms** for one sheet
  (264 rules re-cut), **25 ms** for all 19 (2 420 re-cut). The estimate this
  bullet used to carry — "tens of milliseconds" for a single sheet, extrapolated
  from F12.1's ~50 ms per image — was pessimistic by an order of magnitude,
  because that average was decode under contention with 271 images in flight,
  not one warm file. The tile-invalidation walk is linear in `Tiles`, so a
  150 199-rule pack will cost more than measured here and has not been measured.
- **The fingerprint can miss a same-size, same-mtime overwrite.** A file system
  with one-second mtime granularity and a paint program that rewrites a file
  byte-for-byte the same size within the same second would not be noticed. The
  escape hatch is that the reload is explicit: a follow-up can force every image
  when asked. Hashing 23.9 MB of PNG on every request to close a window this
  narrow is not worth it.
- **`HdPackBitmapInfo` grows two fields** (path, fingerprint) on every image of
  every pack. At 271 images that is noise against the 2.2–3.0 GB peak RSS F12.1
  measured.
