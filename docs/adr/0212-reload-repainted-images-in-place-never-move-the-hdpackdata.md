# ADR-0212: A pack reload re-decodes the images that changed, in place — the `HdPackData` object never moves

- Status: **accepted 2026-09-19 and implemented the same turn as F12.3** — the PRD's F12.3 decision rule was already accepted and F12.1's measurement resolves it; this ADR records how. User go-ahead, verbatim, answering a recommendation to do F12.3 before the coverage work: *"/goal F12.3"*. CLAUDE.md allows same-turn implementation when the change ships with unit tests covering the decision and the go-ahead is quoted here and in the PR body; both hold.
- Date: 2026-09-19
- Related: PRD Part A F12.3, F12.1 (scale/load measurement, `docs/validation/f12.1-scale-and-load-2026-09-17.md`), F12.4, ADR-0153, ADR-0183, ADR-0209

## Context

ADR-0209's paint loop ends by seeing the repainted file in the game, but that step costs a ROM reopen — the artist saves in Photoshop and must reload to see whether the pixels landed; F12.3 removes it. The PRD's F12.3 rule: *"full reload if it costs under one frame budget times an agreed factor, otherwise per-image invalidation with the strategy named in the log."* F12.1 measured on the installed Metroid pack: `NesConsole::LoadHdPack` (manifest parse) = **412 ms** against a 16.7 ms frame budget, **25 frames**, so it cannot ride a frame boundary; `HdPackData::LoadAsync` (bitmap decode) = **13.2–16.4 s for 271 images**, 30–40× the parse — F12.1 finding 1: *"Any F12.3 reload design that only re-reads `hires.txt` would be fast and wrong; per-image invalidation is the shape this number points at."*

Three structural facts (code, 2026-09-19): (1) three holders keep a raw `HdPackData*` — `HdNesPpu::_hdData`, `HdVideoFilter::_hdData`, `HdNesPack::_hdData` — none refcounted; (2) `HdVideoFilter::ApplyFilter` runs from `VideoDecoder::DecodeThread`, so mutating those pixels without draining it races the render hot path; (3) `HdPackBitmapInfo` is already the unit of decoding — it owns `PngName`, the raw `FileData` (freed after `Init()`), the decoded `PixelData`, and an `_initDone`-guarded `SimpleLock`, and every `HdPackTileInfo` reaches pixels through a `HdPackBitmapInfo*`, so re-decoding in place invalidates no pointer.

Non-goals: no swap of the `HdPackData` object, no re-parse of `hires.txt`, no audio/conditions/`<addition>`/`<fallback>` reload, no install or matcher change; a manifest change still needs a reopen.

## Decision

### 1. The unit of reload is one image file, re-decoded in place

`HdPackData` gains a reload entry point that walks `BackgroundFileData` and `ImageFileData`; for each `HdPackBitmapInfo` whose backing file changed on disk it re-reads the PNG into the **same object** — `FileData` refilled, `PNGHelper::ReadPNG` into `PixelData`, `PremultiplyAlpha`, `FileData` released again, the sequence `Init()` already performs minus the `_initDone` short-circuit. No `HdPackData*`, `HdPackBitmapInfo*` or `HdPackTileInfo::Bitmap` is invalidated, so the three holders of fact 1 need no coordination.

### 2. "Changed" is a stat fingerprint, recorded at load

Each `HdPackBitmapInfo` records the **absolute path** it loaded from and a `(size, mtime)` pair at load time. A reload re-stats each path and re-decodes only where the pair moved, then updates the fingerprint. An unchanged pack costs one `stat` per image (sub-millisecond for 271 files) — F12.1 finding 1's per-image invalidation, turning a 13–16 s operation into a single-image one for the artist's case: repaint one sheet and look.

### 3. The re-decode happens on the emulation thread at frame end, after the decode thread is drained

The reload is requested asynchronously (menu action, headless flag, interop call) and sets a pending flag. At the next frame boundary on the emulation thread the console calls `VideoDecoder::WaitForAsyncFrameDecode()` — after it returns the decode thread is idle and not inside `HdVideoFilter::ApplyFilter` — then re-decodes the changed images, clears the flag and logs one line. That drain is the entire synchronisation; locking `PixelData` on the read side was rejected because reads are the per-pixel render hot path, and a reload is rare and explicit, so paying once on the writer's side is the right trade.

### 4. A repaint that resizes the canvas is refused, per image, and says so

`HdPackTileInfo` caches `X`, `Y`, `Width` and `Height` from the manifest and indexes into `PixelData` with them; a smaller PNG would read out of bounds. So when the re-decoded image's `Width`/`Height` differ, the new pixels are **discarded**, the old image kept, and the Core logs:

```
[MEP] reload: <name> changed size (WxH -> W'xH') - reopen the ROM to pick it up
```

The artist's loop overwrites the same canvas; resizes are off-path.

### 5. A zip-backed pack is not watched

`HdPackLoader::LoadFile` reads a zipped pack through `ZipReader`, so there is no file on disk to stat; a reload against one logs the limitation and does nothing.

### 6. Verification

- Unit tests in `scripts/core_unit_tests.cpp` over the fingerprint and dimension rules, host-free. **The change-detection test must not depend on mtime resolution** — libstdc++ resolves `last_write_time` more coarsely than APFS (CI has flaked); assert on the size change, or set the mtime explicitly.
- Stop rule (PRD): a PNG overwritten on disk renders pixel-exact in a `headless_record` screenshot after the reload, no state loss. **Met 2026-09-19**: a repaint mid-play plus reload lands on the byte-identical final frame (`0xA8693E63`) as one that had the repaint from the start, differing from the control (`0xDBA93B36`). Flags: `reload-at-frame=<n>`, `replace=<dst>=<src>` (repeatable); the trigger counts **emulated frames** (a wall-clock trigger never fires headless).
- Hitch measured, not asserted: `docs/validation/f12.3-reload-repainted-images-2026-09-19.md`.

## Consequences

- **A manifest edit is not picked up.** An artist who changes `hires.txt`, or rebuilds with `mep_build.py`, still reopens the ROM — the cost of immovable `HdPackData`. If it matters in the F12.4 loop, it is a follow-up slice inheriting this one's frame-boundary discipline.
- **The reload stalls emulation briefly** (decode-thread drain + re-decode + tile re-cut). **Measured 2026-09-19** on a 26-image, 2 171-rule Zelda pack: **0 ms** when nothing changed, **2 ms** for one sheet (264 rules re-cut), **25 ms** for all 19 (2 420 re-cut) — the "tens of milliseconds" ~50 ms per image the earlier estimate used was decode under contention, not one warm file. The tile-invalidation walk is linear in `Tiles`, so a 150 199-rule pack will cost more and has not been measured.
- **The fingerprint can miss a same-size, same-mtime overwrite.** A one-second-granularity file system and a paint program rewriting a file byte-for-byte the same size within the same second would not be noticed; the escape hatch is the explicit reload: a follow-up can force every image. Hashing 23.9 MB of PNG on every request is not worth it.
- **`HdPackBitmapInfo` grows two fields** (path, fingerprint) on every image of every pack; at 271 images that is noise against the 2.2–3.0 GB peak RSS F12.1 measured.
