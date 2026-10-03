# ADR-0253: Widescreen reveals more of the playfield, per console, with a content-aware fallback

- Status: accepted (2026-10-03). The user found that the WideScrn toggle only stretches the picture (*"sobre a feature widescreen, está apenas distorcendo, eu quero completar as bordas, na linha do que o https://www.zsnes.com/#features faz"*), and added that it must work across more than one kind of game (*"lembre que o widescreen vai funcionar para mais de um tipo de jogo"*). They then settled the UI: *"o modo deve ser automático. a opção liga/desliga do widescreen já existe no Enhacements. quando o jogo não suporta nenhum modo a chave fica disabled."* — decisions 1 and 4 below. The user accepted it and asked for slice 1 in the same turn: *"sim, aceito a ADR. começa pela fatia 1"* (2026-10-03). **W.1 implemented 2026-10-03** under that go-ahead. A frame may carry `RenderedFrame::ExtendedColumns`. On NES, `DefaultNesPpu` widens the frame to 384×240: 64 px per side, 8 whole tiles, about 16:9 at the 8:7 pixel aspect. The extra pixels come from each row's own scroll state, read through `DebugReadVram`. A row whose side columns have no content of their own is drawn black. The renderer shows an extended frame at the console's pixel aspect instead of stretching it. NTSC filters and the border layer still get only the centre 256 columns (W.6 lifted the NTSC half of that; the border layer is W.3's). With the switch off, SMB, Contra and SMB3 frames are bit-identical to the unmodified build. On the SMB3 title (horizontal mirroring, no scroll) the sides showed a wrapped copy of the picture; the user decided *"sim, laterais sempre pretas no SMB3"* (2026-10-03), so §3 now treats horizontal mirroring as "cannot fill" at any scroll. **W.6 implemented 2026-10-03**: both NES NTSC filters take the width from the frame and answer `AcceptsExtendedFrame()`, so a Reveal frame is filtered whole instead of being cropped to its standard centre. The shared arithmetic is host-free in `Core/Shared/Video/WidescreenFrameFlow.h`. On the blargg filter, `BlitOutputWidth`/`BlitPlanePixels` size the blit plane (896×240 against 602×240 - the old fixed `NES_NTSC_OUT_WIDTH(256) * 240` was 294 px short per row, i.e. the blit wrote past its end) and the HUD scale is the frame's own (896/384, not 602/256). On the Bisqwit filter, `SignalSamples` gives a row of the frame's own width (3072 against 2048 samples), `DecodeFrame` reads `_baseFrameInfo.Width` as the row stride instead of `(rowNumber << 8) | x`, and `PhaseAdvanceAfterRow` advances the colour phase by what is left of the whole 341-cycle scanline, so a Reveal row is no longer 128 subcarrier samples (10.7 colour cycles) short - a hue that crawls down the picture. `ApplyPalBorder` takes the width in both filters, so a PAL border covers the extended frame. The recorder's behaviour needed no change - `VideoRenderer::ProcessAviRecording` already opened the AVI/GIF recorder with `frame.Width`/`frame.Height` - but the canvas it lays the input/system HUD out on was arithmetic nothing asserted, so it is now `RecorderHudCanvas` in the same header and the recorder calls it: a Reveal recording lays its HUD out on 448×240 against a standard recording's 301×240, which is where the standard canvas would have crowded or clipped it. `BlitVisibleWidth` is what a filter reports as the frame's width, i.e. what the recorder is opened at, and is total because a width that follows the frame - unlike the old 256-px constant - can be no wider than the overscan wants to cut. The capture tools measure an extended capture's centre through `StandardCentre`/`ExtractCentre`, whose caller now names the standard width so "there is no centre here" is a real answer - a standard capture is refused, and `scripts/headless_record.cpp` keeps its explicit shape gate because an NTSC-filtered frame is wider than 256 without being a Reveal frame. Evidence: Bloco W6 in `scripts/core_unit_tests.cpp` - `TestW6BlitGeometryFollowsTheFrameWidth`, `TestW6BisqwitRowFollowsTheFrameWidth`, `TestW6ScanlinePhaseIsIndependentOfTheRevealedColumns`, `TestW6CaptureMeasuresAnExtendedFrameOnItsCentre`, `TestW6TheRecordedFrameFollowsTheFilteredFramesWidth`, `TestW6TheFiltersAcceptTheExtendedFrame`. Reverting the three functions to their 256-px constants turned ten W6 assertions red, spread over all five arithmetic tests - the arithmetic is asserted, not merely exercised. A second mutation pass went after the *wiring* rather than the arithmetic, because arithmetic tests alone stay green when the call site is rewired: deleting `AcceptsExtendedFrame` from both filters (which is what makes the decoder crop a Reveal frame back to its centre before the filter ever sees it), pinning `BlitVisibleWidth` to the 256-px constant, and returning the standard canvas from `RecorderHudCanvas` together turned eight further assertions red across `TestW6TheFiltersAcceptTheExtendedFrame` and `TestW6TheRecordedFrameFollowsTheFilteredFramesWidth`. That first one is asserted by type, not by value: `&Derived::f` has type `bool (Base::*)()` unless the derived class declares the override itself, so the declaration is the thing under test, and it is testable at all only because the two filter headers compile standalone. **Known gap, accepted:** the CUTOBJ set links no filter and no renderer (they pull in the PPU, the console and an Emulator), so the `.cpp` call sites themselves - `EnsureNtscBuffer`'s reallocation, Bisqwit's `rowNumber * width + x` stride and the width passed to `ApplyPalBorder` - are one-line pass-throughs of the asserted functions that only `make doc-checks` (which does compile them) would catch being rewired. The arithmetic is asserted; the passing-through is not. The standard path is pinned there too, which is ADR-0162's claim at the unit level: at 256 px the three functions reproduce the constants the filters hardcoded (a 602×240 plane, a 602/256 HUD ratio, a 2048-sample row, an 85×8 phase advance), and `AcceptsExtendedFrame() == true` cannot change the standard path because the decoder only crops when `_frame.ExtendedColumns > 0`. The remaining slices are W.2, W.3, W.4, W.5 and W.7 in `docs/roadmap/PRD-mesence-enhancement-ecosystem.md` Part B §8.
- Date: 2026-10-03
- Related: PRD Part B §6.1 (WideScrn row) and §7 (Non-goals); ADR-0149 (the enhancement pack border layer); ADR-0162 (the accuracy suite: standard frames stay identical); ADR-0163 (fork–upstream coexistence: `NesPpu`, the GB PPU and the SMS VDP are upstream-owned); ADR-0236 (recorded captures are keyed to 256-wide cell positions); ADR-0246 (Art, Pixels and Screen picture layers).
- Supersedes / amends: amends PRD Part B §7, whose non-goal *"A widescreen mode that reveals more of the playfield … would be its own per-console engine ADR"* this ADR is. Amends §6.1's WideScrn row, which today is defined as a 16:9 stretch.

## Context

`VideoAspectRatio.Widescreen` makes `AspectRatioMath::ComputeAspectRatio` return a flat 16:9. `VideoRenderer` then draws the same 256×240 (NES) or 160×144 (GB) frame wider. No new pixel is produced, so every game looks stretched.

The user wants the sides filled with the game itself, the way bsnes-hd and ZSNES-era widescreen hacks do it. The hardware draws a window onto a larger background map, and widening the window shows more of that map. Today nothing in Core can do this:

- **NES:** `NesConstants::ScreenWidth` is 256, and the PPU buffers, `HdNesPack::Process`, `ScreenTiles` and the HD conditions all assume 256.
- **GB:** `GbConstants::ScreenWidth` is 160.
- **HD packs:** `<background>` only pans inside the 256-px window, and the border layer (ADR-0149) is static art.

How much of the map is real content depends on the console and on the game:

| Console | Visible | Background map | Real content beside the picture |
|---|---|---|---|
| NES | 256×240 | 2 nametables of 256×240, arranged by the mapper's mirroring | Horizontal scrollers with vertical mirroring show the next screen (Super Mario Bros., Contra). Horizontal mirroring, single-screen and four-screen games mirror or show stale data. Games rewrite the off-screen column while scrolling. |
| GB / GBC | 160×144 | 256×256 BG map, plus the window layer | Almost always. 96 px of map exist beside a 160-px picture, and the map wraps. |
| Game Gear | 160×144 | the SMS 256×224 map | Like the GB: 96 px of map beside the picture. |
| SMS / SG-1000 | 256×192 | 256-wide name table | None horizontally: the map is exactly as wide as the screen and wraps onto itself. Only the 8-px left-column blank can be filled. |
| GBA | 240×160 | text BGs up to 512×512, affine BGs larger | Usually, per BG. The GBA is already 3:2, so the gain is smaller. |

The kind of game matters as much as the console:

- **Horizontal scrollers** have real content on the side they scroll into, and a half-built column on the other side.
- **Vertical scrollers and single-screen games** (Zelda's overworld rooms, Tetris, puzzle games) have nothing meaningful beside the picture. They show a mirror or the next room's data.
- **Split screens** (a status bar under an MMC3 IRQ split, the GB window used as a HUD) change scroll mid-frame, so a widened picture must follow the per-scanline state.
- **Sprites** past the original edge do not exist. Enemies appear at the old edge and only the scenery is widened.

**Non-goals:**
- Patching the game's logic so enemies spawn earlier, as some ROM hacks do.
- Changing what the game computes. Widescreen is presentation only, and save states, movies and netplay stay identical to the 4:3 game.
- SNES. It is not a product console on `main`.

## Decision

1. **One switch, automatic mode.** The existing WideScrn on/off switch in Enhancements stays the only control; there is no mode picker. When it is on, the core picks the mode by itself, in this order:
   - **Reveal**: the console draws N extra columns on each side from the hardware's own background map, so the picture is 16:9 at the original pixel aspect;
   - **Pack art**: when the game has no real side content but its pack ships widescreen art (decision 3);
   - otherwise the game supports **no** widescreen mode, and the switch is **disabled** for it, with a one-line reason (for example "This game has nothing to show beside the picture"). The switch keeps its saved value, so the next game that supports it gets it back.

   The stretch to 16:9 is dropped: it is the distortion this ADR exists to remove. The §6.1 restore rule for the aspect ratio still holds when the switch is turned off.
2. **Reveal is produced where the pixels are made, per console, behind one contract.**
   - **The contract:** a console that supports Reveal emits a `RenderedFrame` that is `2N` pixels wider and says which columns are extended. In the standard mode its output is bit-identical to today, so ADR-0162's accuracy suite compares the standard frames unchanged and adds Reveal cases on top.
   - **No side effects:** extra columns are fetched with side-effect-free VRAM reads, the path the debugger viewers already use. A mapper or bus hook never fires twice.
   - **Stays on the original 256/160 px:** sprite evaluation, sprite-0 hit, the left-8-px mask and every CPU-visible register.
   - **Per-scanline state:** scroll, nametable and CHR bank are taken per scanline, so split screens widen correctly.
   - **Pipeline:** `VideoDecoder`, `BaseVideoFilter` and the renderer are already width-agnostic. The NTSC filters, the HD path (`HdNesPack::Process`, `ScreenTiles`, HD conditions), the recorder, save-state thumbnails and screenshots get the width from the frame instead of a constant.
   - **Per-console scope:**
     - NES: the neighbouring nametable through the mirroring.
     - GB/GBC/GG: the BG map and window, wrapped.
     - GBA: text BGs only in the first slice.
     - SMS/SG-1000: Reveal is offered but has no map columns to show, so it always uses the fallback (decision 3).
3. **Content-aware fallback.** While widescreen is active, a column the console cannot fill with real content on a given frame is drawn by the first source that applies:
   1. **Pack art.** An HD pack may ship wider `<background>` art or a per-screen side image. A `<widescreen>` section is added to the MEP spec, with a version bump in `docs/specs/`.
   2. **The pack's border layer** (ADR-0149).
   3. **Black.**

   The border and black are per-frame fill-ins for a game that does support a mode. On their own they never make a game "supported", so they never keep the switch enabled.

   "Cannot fill" is decided per console. On NES it means single-screen or horizontal mirroring, at any scroll (amended 2026-10-03: horizontal mirroring with no horizontal scroll used to keep the side columns, which showed a wrapped copy of the picture's own edge on the Super Mario Bros. 3 title). A half-built column, the one the game is rewriting this frame, is cut back to the last fully written column so construction never shows.
4. **Support is decided per game, and remembered.**
   - **Consoles with no side map:** SMS/SG-1000 without pack art are known unsupported before the game runs, so the switch is disabled at once.
   - **Everything else:** the first time a game runs, the core measures over the first gameplay seconds whether the side columns ever held real, stable content. During that time the switch stays enabled.
   - **No content found:** the game is recorded as unsupported per ROM, the same way the W-P5 per-ROM pack choice is stored. The switch then shows disabled from the next load, and a toast says so once.
   - **Re-checking:** installing a pack with widescreen art re-enables the switch, and so does a later run that finds content, such as a level that scrolls.
5. **Slices, in order:**
   1. The frame-width contract, plus the NES Reveal with the black fallback and its accuracy and unit tests.
   2. GB/GBC/GG Reveal.
   3. The fallback chain: the border layer, then the MEP `<widescreen>` pack art, as a spec bump.
   4. The HD pack path on NES (extra columns drawn with the pack's tiles).
   5. The per-game support measurement and memory, and the switch's enabled/disabled state with its reason.
   6. The NTSC filters, recorder and capture tools.
   7. GBA text BGs.

## Consequences

- **Upstream-owned files change.** `NesPpu`, the GB PPU and the SMS VDP are edited (ADR-0163), so upstream merges get harder. The edits stay behind the frame-width contract, and the standard path stays the code that is there now.
- **More widths to support.** Tools that key on the screen position (the HD Pack Builder, recorded captures under ADR-0236, the composition editor) keep working on the original 256/160 px. The extra columns are presentation and are never recorded as tiles.
- **The fallback adds work for pack authors.** It is optional: without pack art, Reveal still works, with black or the border.
- **The PRD changes with this ADR:** §6.1's WideScrn row becomes the automatic switch (Reveal, then pack art, otherwise disabled; no stretch), the §7 non-goal is deleted, and the slices are W.1–W.7 in §8.
