# ADR-0253: Widescreen reveals more of the playfield, per console, with a content-aware fallback

- Status: proposed (2026-10-03). The user found that the WideScrn toggle only stretches the picture (*"sobre a feature widescreen, está apenas distorcendo, eu quero completar as bordas, na linha do que o https://www.zsnes.com/#features faz"*), and added that it must work across more than one kind of game (*"lembre que o widescreen vai funcionar para mais de um tipo de jogo"*). They then settled the UI: *"o modo deve ser automático. a opção liga/desliga do widescreen já existe no Enhacements. quando o jogo não suporta nenhum modo a chave fica disabled."* — decisions 1 and 4 below. Nothing is implemented. On acceptance, each Decision item below becomes a slice in `docs/roadmap/PRD-mesence-enhancement-ecosystem.md`.
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

   "Cannot fill" is decided per console. On NES it means single-screen or horizontal mirroring with horizontal scroll. A half-built column, the one the game is rewriting this frame, is cut back to the last fully written column so construction never shows.
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
- **The PRD changes on acceptance:** §6.1's WideScrn row becomes the automatic switch (Reveal, then pack art, otherwise disabled; no stretch), the §7 non-goal is deleted, and the slices are added.
