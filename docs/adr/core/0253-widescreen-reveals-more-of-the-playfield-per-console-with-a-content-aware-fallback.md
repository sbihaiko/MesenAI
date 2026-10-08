# ADR-0253: Widescreen reveals more of the playfield, per console, with a content-aware fallback

- Status: accepted 2026-10-03. The user found that the WideScrn toggle only stretches the
  picture (*"sobre a feature widescreen, está apenas distorcendo, eu quero completar as bordas,
  na linha do que o https://www.zsnes.com/#features faz"*), required it to work across more than
  one kind of game (*"lembre que o widescreen vai funcionar para mais de um tipo de jogo"*), and
  settled the UI (*"o modo deve ser automático. a opção liga/desliga do widescreen já existe no
  Enhacements. quando o jogo não suporta nenhum modo a chave fica disabled."*) — decisions 1 and
  4. Go-ahead for slice 1, verbatim: *"sim, aceito a ADR. começa pela fatia 1"* (2026-10-03).
- Date: 2026-10-03
- Related: PRD Part B §6.1 (WideScrn row) and §7 (Non-goals); ADR-0149 (the enhancement pack
  border layer); ADR-0162 (the accuracy suite: standard frames stay identical); ADR-0163
  (fork–upstream coexistence: `NesPpu`, the GB PPU and the SMS VDP are upstream-owned);
  ADR-0236 (recorded captures are keyed to 256-wide cell positions); ADR-0246 (Art, Pixels and
  Screen picture layers).
- Supersedes / amends: amends PRD Part B §7, whose non-goal *"A widescreen mode that reveals
  more of the playfield … would be its own per-console engine ADR"* this ADR is. Amends §6.1's
  WideScrn row, which today is defined as a 16:9 stretch.
- Amended by: ADR-0267 (accepted 2026-10-08, stage 1 = its option B, implemented by a separate PR,
  not by the branch that accepted it) — **§1** ("The stretch to 16:9 is dropped: it is the
  distortion this ADR exists to remove."), **§3** ("border and black are per-frame fill-ins that
  never on their own make a game supported") and **§4** ("SMS/SG-1000 without pack art are known
  unsupported before the game runs, so the switch is disabled at once") are amended: a console with
  no side map keeps the Widescreen switch enabled, and turning it on applies
  `VideoAspectRatio.Widescreen` — a fill, not a Reveal. **§2 is untouched by that amendment**, and
  stays exactly as accepted here; the §2 amendment is owed only when ADR-0267's option C (the
  synthesized edge band) lands.

## Record

- 2026-10-03 — **W.1**: a frame may carry `RenderedFrame::ExtendedColumns`. On NES,
  `DefaultNesPpu` widens the frame to 384×240 (64 px per side, 8 whole tiles, about 16:9 at the
  8:7 pixel aspect), the extra pixels from each row's own scroll state read through
  `DebugReadVram`; a row whose side columns have no content of their own is drawn black. The
  renderer shows an extended frame at the console's pixel aspect instead of stretching it. NTSC
  filters and the border layer still get only the center 256 columns (W.6 lifted the NTSC half;
  the border layer is W.3's). With the switch off, SMB, Contra and SMB3 frames are bit-identical
  to the unmodified build. On the SMB3 title (horizontal mirroring) the sides showed a wrapped
  copy; the user decided *"sim, laterais sempre pretas no SMB3"*, so §3 treats horizontal
  mirroring as "cannot fill" at any scroll.
- 2026-10-03 — **W.6**: both NES NTSC filters take the width from the frame and answer
  `AcceptsExtendedFrame()`, so a Reveal frame is filtered whole instead of cropped to its
  standard center. The shared arithmetic is host-free in `Core/Shared/Video/WidescreenFrameFlow.h`.
  On blargg, `BlitOutputWidth`/`BlitPlanePixels` size the blit plane (896×240 against 602×240 —
  the old fixed `NES_NTSC_OUT_WIDTH(256) * 240` was 294 px short per row, i.e. the blit wrote past its end) and the HUD scale is
  the frame's own (896/384, not 602/256). On Bisqwit, `SignalSamples` gives a row of the frame's
  own width (3072 against 2048), `DecodeFrame` reads `_baseFrameInfo.Width` as the row stride
  instead of `(rowNumber << 8) | x`, and `PhaseAdvanceAfterRow` advances the color phase by
  what is left of the whole 341-cycle scanline. `ApplyPalBorder` takes the width in both
  filters. The recorder needed no change (`VideoRenderer::ProcessAviRecording` already opened the
  AVI/GIF recorder with `frame.Width`/`frame.Height`), but the HUD canvas is now
  `RecorderHudCanvas` (a Reveal recording lays it out on 448×240 against 301×240). `BlitVisibleWidth`
  is what a filter reports as the frame's width. The capture tools measure an extended capture's
  center through `StandardCentre`/`ExtractCentre`, whose caller now names the standard width so
  "there is no center here" is a real answer; `scripts/headless_record.cpp` keeps its explicit
  shape gate. Evidence: `TestW6BlitGeometryFollowsTheFrameWidth`,
  `TestW6BisqwitRowFollowsTheFrameWidth`,
  `TestW6ScanlinePhaseIsIndependentOfTheRevealedColumns`,
  `TestW6CaptureMeasuresAnExtendedFrameOnItsCentre`,
  `TestW6TheRecordedFrameFollowsTheFilteredFramesWidth`, `TestW6TheFiltersAcceptTheExtendedFrame`.
  Reverting the three functions to their 256-px constants turned ten W6 assertions red; the
  wiring mutation pass (deleting `AcceptsExtendedFrame`, pinning `BlitVisibleWidth`, returning
  the standard canvas) turned eight more red. `AcceptsExtendedFrame` is asserted by type, not by
  value: `&Derived::f` has type `bool (Base::*)()` unless the derived class declares the
  override; the two filter headers compile standalone. **Known gap, accepted:** the CUTOBJ set
  links no filter or renderer, so the `.cpp` call sites (`EnsureNtscBuffer`'s reallocation,
  Bisqwit's `rowNumber * width + x` stride and the width to `ApplyPalBorder`) are one-line
  pass-throughs only `make doc-checks` would catch being rewired. The standard path is pinned:
  at 256 px the three functions reproduce the hardcoded constants (a 602×240 plane, a 602/256
  HUD ratio, a 2048-sample row, an 85×8 phase advance), and `AcceptsExtendedFrame() == true`
  cannot change it because the decoder only crops when `_frame.ExtendedColumns > 0`.
- 2026-10-03 — **W.5**: `NesWidescreenSupport::Probe` measures the first 300 frames that
  actually drew the background, sticky on the first frame whose row basis has side content, and
  `NesConsole::GetWidescreenSupportVerdict` publishes the answer through
  `EmuApi.GetWidescreenSupportVerdict`; the app keys it per ROM in
  `PlayerEnhancementsConfig.RomWidescreenSupport`, and the Enhancements sheet's switch comes up
  disabled with the §1 one-line reason plus the §4 toast once per session. Evidence:
  `TestWidescreenSupportProbeIsUndecidedWhileTheWindowIsOpen`,
  `…FindsContentOnTheFirstFrameThatHasIt`,
  `…ConcludesUnsupportedAfterAFullWindowOfNothing`, `…ContentAfterTheWindowStillCounts`,
  `…ResetStartsTheNextRunOver`, `…ReadsTheRevealContentRule` (`scripts/core_unit_tests.cpp`,
  "W253:"); `A_game_measured_with_nothing_beside_the_picture_is_disabled_with_its_reason`,
  `A_console_with_no_side_map_is_disabled_before_the_game_ever_runs`,
  `A_recorded_game_announces_its_reason_once_per_session`,
  `A_terminal_verdict_is_recorded_when_the_window_closes`,
  `A_game_that_cannot_use_widescreen_is_off_without_losing_the_saved_preference`
  (`UI.Tests/Play/WidescreenSupportRuleTests.cs`);
  `Widescreen_switch_is_disabled_with_its_reason_when_the_game_cannot_use_it`,
  `A_game_that_cannot_use_widescreen_shows_the_switch_off_and_keeps_the_saved_preference`
  (`UI.HeadlessTests/PlaySheetsViewTests.cs`). The record is written as the window closes, so it
  does not depend on the sheet being opened; a game settled unsupported is not widened at all
  (`NesWidescreenSupport::Reveals`, `TestWidescreenSupportSettledGameIsNotWidenedAtAll`), and the
  sheet shows and applies `WidescreenSupportRule.EffectiveWidescreen` (§1's "the next game that
  supports it gets it back"); §4's "re-checking" is live in both directions. W.5's toast stacks
  with W-P3's entry toast (`SystemHud::DrawMessages` draws up to four).
- 2026-10-03 — **W.3** ("the fallback chain"): the extended frame travels with a per-row side-fill
  map — `RenderedFrame::ExtendedSideFill`, one byte per row, bit 0 = left / bit 1 = right
  (`Core/Shared/Video/WidescreenFallback.h`) — published by the PPU from the Reveal's own
  `FrameBuffers::LastFill` and carried across `VideoDecoder`'s filter step.
  `VideoRenderer::ApplyWidescreenFallback` fills the sides the Reveal left empty from the MEP
  `widescreen` section's art, and `VideoRenderer::CompositeBorder` (ADR-0149, composited onto an
  extended frame by `BorderLayout::BorderCompositeExtendedFrame`) fills whatever is still empty,
  in Decision 3's order. That order lives in `WidescreenFallback::ApplyChain`, the one host-free
  function `VideoRenderer::UpdateFrame` calls. `WidescreenFallback::Resolve`/`SupportsWidescreen`
  encode §3's rule that border and black are per-frame fill-ins that never on their own make a
  game supported. MEP v1.8 adds §5.5's `widescreen` section (`widescreen/widescreen.json` + the
  side PNGs it names, decoded once per pack change, copied 1:1 only when the decoded size matches
  the console's own extra-column count and frame height). Evidence: the W253C messages —
  `W253C: with nothing to reveal, the pack's widescreen art comes first (ADR-0253 §3)`,
  `W253C: without pack art, the border layer fills the sides`, `W253C: with no art and no border,
  the sides stay black`, `W253C: a border or black alone never makes a game supported (ADR-0253
  §3)`, `W253C: the art marks the rows it filled`, `W253C: the right side skips the rows the game
  filled`, `W253C: a left-filled row draws the left run beside the viewport`, `W253C: an unfilled
  side row keeps the border backdrop`, `W253C: the pack art runs before the border composite
  (ADR-0253 §3)`, `W253C: the art is already on the frame when the border composite runs`,
  `W253C: the pack art reaches the canvas beside the viewport`, `W253C: compositing before the
  art loses it, which is why the order is pinned`, `W253C: with no side map the picture still
  lands in the viewport`, `W253C: dropping back to the standard width drops the side-fill map
  with it`, `W253C: widescreen/widescreen.json is the widescreen section`, `W253C: a valid
  widescreen.json parses`, `W253C: the synth switch does not gate the widescreen art` — and
  `scripts/test_mep_lint_widescreen.py`. The section's own switch is
  `MepPackManager::SectionSwitchEnabled`: `widescreen` answers to EnableMepPacks alone. Three
  notes: (a) this ADR's §5.3 paraphrase listed the border before the pack art, contradicting
  Decision 3 — the paraphrase is reordered to match the Decision, which is normative; (b)
  per-screen art (`screens[]`) parses and is unit-tested, but the renderer draws the default
  `left`/`right` pair on every frame, because no screen id reaches the default PPU/renderer path
  until the NES HD pack path supplies one (W.4); (c) `SupportsWidescreen` had no caller when W.3
  landed — the PRD gives the switch's disabled state and its reason to W.5.
- 2026-10-03 — **W.3 × W.5 seam closed**: `hasWidescreenPackArt` is a constant no longer.
  `MepPackManager::HasWidescreenSection` answers whether the winning pack ships the `widescreen`
  section with a human or auto layer — the same lookup `VideoRenderer::UpdatePackArtAssets`
  makes, section switches and the per-ROM preference included. `NesWidescreenSupport::Reveals(switchOn,
  verdict, packArtAvailable)` now delegates to `WidescreenFallback::SupportsWidescreen`, so §3's
  art overrules a settled `Unsupported`. `EmuApi.HasWidescreenPackArt` feeds
  `WidescreenSupportRule.SwitchForLoadedGame` (switch enabled) and silences §4's "no widescreen
  mode" toast. Evidence: `TestWidescreenSupportPackArtReenablesASettledGame` plus the
  three-argument `TestWidescreenSupportSettledGameIsNotWidenedAtAll` ("W253C:", 1794/1794);
  `A_game_measured_unsupported_is_widened_when_its_pack_ships_widescreen_art`,
  `A_pack_with_widescreen_art_silences_the_unavailable_notice` (1717/1717); and
  `A_pack_shipping_widescreen_art_keeps_the_switch_enabled_for_a_recorded_game`
  (`UI.HeadlessTests/PlaySheetsViewTests.cs`), which drives the VM's `ReadWidescreenPackArt`.
- 2026-10-03 — **W.4** (NES HD pack path): with Reveal on and an HD pack loaded, `HdNesPpu`
  captures each row's basis from `NesPpu`'s own capture point and stores the side tiles in
  `HdScreenInfo::SideTiles`; `HdNesPack::Process` draws them through the pack's per-pixel
  pipeline (`GetPixels`, the `<tile>` rules, fallback tiles, HD conditions and
  grayscale/emphasis) at the pack's scale, so a `<widescreen>` image (W.3) fills the seam; the
  `<background>` layers are deliberately *not* widened — `DrawBackgroundLayer` refuses a side
  pixel rather than index its PNG with a wrapped coordinate. `Process` derives its output width
  and row stride from `ExtendedColumns`; `TestW4SideTilePixelsAreTheLowResRevealPixels`
  cross-checks the widened low-res frame against W.1's `RenderRowSides` for all 64 columns × 2
  sides × 8 fine-X × 8 fine-Y. `ScreenTiles` stay 256×240, so no existing pack rule and no
  ADR-0236 cell mask moves; the side tiles are built from `DebugReadVram` reads only, and with
  the switch off `ComputeHdFrameGeometry(extended=false)` reproduces today's arithmetic element
  for element (ADR-0162). 74 `W4:` cases in `scripts/core_unit_tests.cpp`.
- 2026-10-03 — **W.7** (GBA): N = 22 (284×160; square pixels, so 16:9 sits at 284.4 px).
  `Core/GBA/GbaWidescreenReveal.h` holds the pure half: which map columns hold content the
  240-px window does not already show (a 256-wide map wraps, §3's "cannot fill"), the tilemap
  entry, tile, palette-bank, flip and both-scroll-axis fetch, mosaic blocks anchored at the
  window's own first column, the text-BG priority composite (ties to the lower BG, a transparent
  pixel falling through, the backdrop when none shows) and the row's BLDCNT/BLDALPHA/BLDY effect,
  mirroring `GbaPpu::ProcessColorMath` and `BlendColors`. `GbaPpu` latches the switch once per
  frame, holds the last extended frame while the console skips frames (only while Reveal is on;
  a skip before any extended frame uses the black fallback), and hands the decoder a 284-px frame
  whose center 240 columns are `_currentBuffer`; only text BGs are revealed (`TextBgCount`: BG
  mode 0's four, BG mode 1's BG0/BG1); a row's side columns belong to its first render of the
  frame (`RenderScanline` runs again for every register write inside the row and in a loop while
  VRAM is being accessed). `GbaDefaultVideoFilter` reads the frame's own width and
  accepts extended frames except under an NTSC filter (W.6). VRAM and the palette are read only
  through const pointers. Evidence: `TestGbaRevealWidthContractIsTwentyTwoColumnsPerSide`,
  `TestGbaRevealContentRuleFollowsTheMapWidth`, `TestGbaRevealSamplesTextBgTiles`,
  `TestGbaRevealCompositesTextBgsByPriority`, `TestGbaRevealDrawsBlackWhereItCannotFill`,
  `TestGbaRevealDrawsTheMapColumnsRightBesideTheWindow`, `TestGbaRevealFollowsMosaicBlocks`,
  `TestGbaRevealAppliesTheRowsColorEffect`, `TestGbaRevealOnlyReadsVramAndThePalette`,
  `TestGbaRevealFrameKeepsTheStandardPictureBitIdenticalInTheCentre`,
  `TestGbaRevealHoldsAnExtendedFrameOnlyWhileTheRevealIsOn`,
  `TestGbaRevealSkippedFrameIsExtendedEvenBeforeOneWasDrawn`,
  `TestGbaRevealDrawsARowOnceWhateverTheRegisterWritesDo`,
  `TestGbaRevealDoesNotDrawIntoTheFrameItJustSent`, `TestGbaRevealOnlyRevealsTheModesTextBgs`.
  W.7's on-screen result was first recorded as **"not evaluated"**: no GBA ROM was available in
  the work environment. **Validated headless 2026-10-06 (#954, #963):** a synthetic cartridge
  authored in-repo (`UI.HeadlessTests/SyntheticGbaRom.cs`) runs on the real core and
  `UI.HeadlessTests/GbaWidescreenRevealTests.cs` reads the frame back through `FrameCaptureApi`:
  with the Reveal on the frame is 284×160, a text BG's sides carry the map's hidden columns 30–31
  with the 6 wrapping columns per side black, an affine BG on the row and a bitmap mode turn both
  sides black, a forced-blank row is white across all 284 px, and the switch off returns 240×160
  (color-class and structure assertions, ADR-0249). A commercial GBA game has still not been
  seen by a person: none is committed or available. The Decision is unchanged.
- 2026-10-03 — **W.2** (GB/GBC, Game Gear): GB/GBC N = 48, a 256×144 frame (16:9 exactly at
  square pixels), the side columns read per scanline from the 256×256 BG map around SCX/SCY —
  wrapping, window and its mid-tile takeover included, CGB attributes, flips, palettes and VRAM
  bank honored, a row whose BG layer is off flattened to that one color, black only for rows
  the frame never drew. Fetched with `GbPpu::LcdReadVram` (side-effect free, full 14-bit address
  with the bank bit); a test fails if `ReadVram`, the CPU-visible read that fires the debugger
  hook, is ever used. Game Gear: the Reveal is the player's own horizontal crop being dropped —
  `SmsConfig.GameGearOverscan`, 48 px on each side with the shipped preset — so
  `EmuSettings::GetOverscan` stops cropping exactly what `SmsVdp` reports as extended; a "Full
  Frame" preset (0) or a crop that differs per side is not a Reveal. `Width` stays 256 and only
  `RenderedFrame::ExtendedColumns` is set — `Width - 2 * ExtendedColumns` is the standard 160-px
  picture. Evidence: `TestGbRevealWidthContractIsFortyEightColumnsPerSide`,
  `TestGbRevealDrawsTheBgMapThatWrapsInBesideThePicture`, `TestGbRevealFollowsScrollAndTheMapRow`,
  `TestGbRevealHonoursTheCgbTileAttributes`, `TestGbRevealUsesTheDmgPaletteShades`,
  `TestGbRevealContinuesTheWindowPastThePicturesEdges`,
  `TestGbRevealShowsTheBlankColorWhenTheBgLayerIsOff`,
  `TestGbRevealNeverUsesTheCpuVisibleVramRead`,
  `TestGbRevealFrameKeepsTheStandardPictureBitIdenticalInTheCentre`,
  `TestGbRevealWindowNeedsTheEnableBitNotJustTheLatches` — the Reveal takes the PPU's own window
  latch (`GbPpu::_fetchWindow`, the same `GbWidescreenReveal::WindowVisible` the fetcher decides
  by), never the WY and WX latches alone — and
  `TestGameGearRevealShowsTheLineTheViewportCrops`, whose crop rule
  (`SmsWidescreenReveal::GameGearOverscan`) never asks the decoder which frame it is holding. A
  Game Gear Reveal cannot be exercised through `scripts/headless_record.cpp` today: it pins the
  SMS config to zeros, so `GameGearOverscan` is 0/0 there (1794/1794 `scripts/core_unit_tests`
  cases). With W.2 all seven slices of
  `docs/roadmap/PRD-mesence-enhancement-ecosystem.md` Part B §8 are implemented.
- 2026-10-03 — **HD-pack parity**: with an HD pack loaded, `HdNesPpu` drives the same
  `NesWidescreenPpu::State` as `DefaultNesPpu`, so an HD frame carries the per-row side-fill map
  (§3/W.3) and `NesConsole::GetWidescreenSupportVerdict` answers for whichever PPU is live
  (§4/W.5, through `BaseNesPpu::GetWidescreenSupportVerdict` rather than a `dynamic_cast` to
  `DefaultNesPpu`); the map crosses the filter step through
  `BaseVideoFilter::GetOutputFrameExtension`, whose default reproduces the old width-equality
  check element for element (ADR-0162), while `HdVideoFilter` restates the contract in the
  frame's own coordinates with `HdWidescreenColumns::ScaleSideFill`. MEP-v1 §5.5's canvas is not
  amended: the art stays `extraColumns × frameHeight` on every console (64×240 on the NES), and
  `VideoRenderer::ApplyWidescreenFallback` scales a conforming image up with
  `WidescreenFallback::ScaleSideArt` (nearest-neighbor by the integer factor the two canvases
  share — the pack's scale), so the same pack fills the sides with and without an HD pack; a
  non-conforming image is still refused, exactly as `FillSideFromArt` refuses it on the standard
  path, and the chain moves on to the border. Evidence: `TestW253PpuRevealLatchIsTheSharedSupportRule`,
  `TestW253PpuStateMeasuresWhetherOrNotTheSwitchIsOn`,
  `TestW253PpuStatePublishesTheFrameAndItsSideFillMap`,
  `TestW4HdSideFillMapFollowsTheFramesScale`,
  `TestW253HdVideoFilterRestatesTheFrameContractInItsOwnCoordinates`,
  `TestW253FallbackScalesThePackArtToAnHdFrame` (1914/1914). The override is asserted by type,
  as in W.6 (the CUTOBJ set links no filter — deleting the declaration turns both of that test's
  assertions red, 1900/1902) — and `HdNesPpu`'s three ADR-0253 members are pinned by
  `static_assert` in `HdNesPpu.h` itself, which is where they can be pinned at all: the header
  pulls in `NesPpu.h`, whose `__forceinline` members are defined in `NesPpu.cpp` and trip
  `-Wundefined-inline` under `-Werror`, so the host-free suite cannot include it; dropping any
  of the three declarations resolves the member to `BaseNesPpu` and the build stops on the
  `static_assert` naming the slice (`Verdict (BaseNesPpu::*)() const` against
  `Verdict (HdNesPpu::*)() const`). Merged after the W.3 × W.5 seam, the shared latch takes the
  pack-art answer too (`NesWidescreenPpu::RevealRequested(..., packArtAvailable)`,
  `HdNesPpu::PackHasWidescreenArt`).

## Context

`VideoAspectRatio.Widescreen` makes `AspectRatioMath::ComputeAspectRatio` return a flat 16:9.
`VideoRenderer` then draws the same 256×240 (NES) or 160×144 (GB) frame wider. No new pixel is
produced, so every game looks stretched.

The user wants the sides filled with the game itself, the way bsnes-hd and ZSNES-era widescreen
hacks do it. The hardware draws a window onto a larger background map, and widening the window
shows more of that map. Today nothing in Core can do this:

- **NES:** `NesConstants::ScreenWidth` is 256, and the PPU buffers, `HdNesPack::Process`,
  `ScreenTiles` and the HD conditions all assume 256.
- **GB:** `GbConstants::ScreenWidth` is 160.
- **HD packs:** `<background>` only pans inside the 256-px window, and the border layer
  (ADR-0149) is static art.

How much of the map is real content depends on the console and on the game:

| Console | Visible | Background map | Real content beside the picture |
|---|---|---|---|
| NES | 256×240 | 2 nametables of 256×240, arranged by the mapper's mirroring | Horizontal scrollers with vertical mirroring show the next screen (Super Mario Bros., Contra). Horizontal mirroring, single-screen and four-screen games mirror or show stale data. Games rewrite the off-screen column while scrolling. |
| GB / GBC | 160×144 | 256×256 BG map, plus the window layer | Almost always. 96 px of map exist beside a 160-px picture, and the map wraps. |
| Game Gear | 160×144 | the SMS 256×224 map | Like the GB: 96 px of map beside the picture. |
| SMS / SG-1000 | 256×192 | 256-wide name table | None horizontally: the map is exactly as wide as the screen and wraps onto itself. Only the 8-px left-column blank can be filled. |
| GBA | 240×160 | text BGs up to 512×512, affine BGs larger | Usually, per BG. The GBA is already 3:2, so the gain is smaller. |

The kind of game matters as much as the console:

- **Horizontal scrollers** have real content on the side they scroll into, and a half-built
  column on the other side.
- **Vertical scrollers and single-screen games** (Zelda's overworld rooms, Tetris, puzzle games)
  have nothing meaningful beside the picture.
- **Split screens** (a status bar under an MMC3 IRQ split, the GB window used as a HUD) change
  scroll mid-frame, so a widened picture must follow the per-scanline state.
- **Sprites** past the original edge do not exist. Enemies appear at the old edge and only the
  scenery is widened.

**Non-goals:**
- Patching the game's logic so enemies spawn earlier, as some ROM hacks do.
- Changing what the game computes. Widescreen is presentation only, and save states, movies and
  netplay stay identical to the 4:3 game.
- SNES. It is not a product console on `main`.

## Decision

1. **One switch, automatic mode.** The existing WideScrn on/off switch in Enhancements stays the
   only control; there is no mode picker. When it is on, the core picks the mode by itself, in
   this order:
   - **Reveal**: the console draws N extra columns on each side from the hardware's own
     background map, so the picture is 16:9 at the original pixel aspect;
   - **Pack art**: when the game has no real side content but its pack ships widescreen art
     (decision 3);
   - otherwise the game supports **no** widescreen mode, and the switch is **disabled** for it,
     with a one-line reason (for example "This game has nothing to show beside the picture"). The
     switch keeps its saved value, so the next game that supports it gets it back.

   The stretch to 16:9 is dropped: it is the distortion this ADR exists to remove. The §6.1
   restore rule for the aspect ratio still holds when the switch is turned off.
2. **Reveal is produced where the pixels are made, per console, behind one contract.**
   - **The contract:** a console that supports Reveal emits a `RenderedFrame` that is `2N` pixels
     wider and says which columns are extended. In the standard mode its output is bit-identical
     to today, so ADR-0162's accuracy suite compares the standard frames unchanged and adds
     Reveal cases on top.
   - **No side effects:** extra columns are fetched with side-effect-free VRAM reads, the path
     the debugger viewers already use. A mapper or bus hook never fires twice.
   - **Stays on the original 256/160 px:** sprite evaluation, sprite-0 hit, the left-8-px mask
     and every CPU-visible register.
   - **Per-scanline state:** scroll, nametable and CHR bank are taken per scanline, so split
     screens widen correctly.
   - **Pipeline:** `VideoDecoder`, `BaseVideoFilter` and the renderer are already width-agnostic.
     The NTSC filters, the HD path (`HdNesPack::Process`, `ScreenTiles`, HD conditions), the
     recorder, save-state thumbnails and screenshots get the width from the frame instead of a
     constant.
   - **Per-console scope:**
     - NES: the neighboring nametable through the mirroring.
     - GB/GBC: the BG map and window, wrapped.
     - Game Gear: no new pixels — the 96 columns its 160-px viewport crops
       (`SmsConfig.GameGearOverscan`) are map columns the VDP already draws, so the Reveal is
       that crop being dropped: the frame keeps the width of the line it already renders and
       reports those columns as its extended ones — a deliberate reading of §2's "2N px wider"
       for this console (W.2's Status line).
     - GBA: text BGs only in the first slice.
     - SMS/SG-1000: Reveal is offered but has no map columns to show, so it always uses the
       fallback (decision 3).
3. **Content-aware fallback.** While widescreen is active, a column the console cannot fill with
   real content on a given frame is drawn by the first source that applies:
   1. **Pack art.** An HD pack may ship wider `<background>` art or a per-screen side image. A
      `<widescreen>` section is added to the MEP spec, with a version bump in `docs/specs/`.
   2. **The pack's border layer** (ADR-0149).
   3. **Black.**

   The border and black are per-frame fill-ins for a game that does support a mode. On their own
   they never make a game "supported", so they never keep the switch enabled.

   "Cannot fill" is decided per console. On NES it means single-screen or horizontal mirroring,
   at any scroll (amended 2026-10-03: horizontal mirroring with no horizontal scroll used to keep
   the side columns, which showed a wrapped copy of the picture's own edge on the Super Mario
   Bros. 3 title). A half-built column, the one the game is rewriting this frame, is cut back to
   the last fully written column so construction never shows.
4. **Support is decided per game, and remembered.**
   - **Consoles with no side map:** SMS/SG-1000 without pack art are known unsupported before the
     game runs, so the switch is disabled at once.
   - **Everything else:** the first time a game runs, the core measures over the first gameplay
     seconds whether the side columns ever held real, stable content. During that time the
     switch stays enabled.
   - **No content found:** the game is recorded as unsupported per ROM, the same way the W-P5
     per-ROM pack choice is stored. The switch then shows disabled from the next load, and a
     toast says so once.
   - **Re-checking:** installing a pack with widescreen art re-enables the switch, and so does a
     later run that finds content, such as a level that scrolls.
5. **Slices, in order:**
   1. The frame-width contract, plus the NES Reveal with the black fallback and its accuracy and
      unit tests.
   2. GB/GBC/GG Reveal.
   3. The fallback chain in Decision 3's order — the MEP `<widescreen>` pack art, then the border
      layer — as a spec bump.
   4. The HD pack path on NES (extra columns drawn with the pack's tiles).
   5. The per-game support measurement and memory, and the switch's enabled/disabled state with
      its reason.
   6. The NTSC filters, recorder and capture tools.
   7. GBA text BGs.

## Consequences

- **Upstream-owned files change.** `NesPpu`, the GB PPU and the SMS VDP are edited (ADR-0163), so
  upstream merges get harder. The edits stay behind the frame-width contract, and the standard
  path stays the code that is there now.
- **More widths to support.** Tools that key on the screen position (the HD Pack Builder,
  recorded captures under ADR-0236, the composition editor) keep working on the original 256/160
  px. The extra columns are presentation and are never recorded as tiles.
- **The fallback adds work for pack authors.** It is optional: without pack art, Reveal still
  works, with black or the border.
- **The PRD changes with this ADR:** §6.1's WideScrn row becomes the automatic switch (Reveal,
  then pack art, otherwise disabled; no stretch), the §7 non-goal is deleted, and the slices are
  W.1–W.7 in §8.
