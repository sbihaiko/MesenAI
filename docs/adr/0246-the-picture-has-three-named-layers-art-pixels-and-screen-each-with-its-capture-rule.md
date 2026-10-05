# ADR-0246: The picture has three named layers — Art, Pixels and Screen — each with its capture rule

- Status: accepted (2026-10-02). Requested by the user, verbatim: *"Sim"* (2026-10-02), to the question whether to write a separate ADR for the three Look layers; accepted the same day (*"Aceitar"*). It writes down PRD Part B §13 W-P10 together with the user's answers of 2026-10-02: named looks bundled, Hold to Compare, and the Pixels override only in Options. Listed as slice **P.13** in PRD Part A §4, Phase 7. Implemented 2026-10-02 on the user's go-ahead, verbatim: *"sim, pode seguir. depois que tudo estiver no main, pode implementar usando paralelismo de tudo que puder"*, *"pode implementar em paralelo tudo que puder"* and *"pode seguir com a segunda leva em paralelo"* (2026-10-02). Rules in `UI/Logic/LookLayers` and `NamedLookManifest` (tested in `UI.Tests/Look/`); Look is a tab of today's ConfigWindow, which the overlay's Settings opens, since G.1 shipped only the shell. The display check by a person (marks read right, compare shows no stutter) is not evaluated. §5's bypass was macOS Metal only until 2026-10-05, when `Windows/Renderer.cpp` and `Linux/LinuxOglRenderer.cpp` were taught to read the same flag and present the held frames unfiltered, chain kept — the CPU-filter half already dropped everywhere, because `VideoDecoder::DecodeFrame` reads the flag in shared code. **Neither of those two renderers is run anywhere, and a normal pull request does not even compile them**: `.github/workflows/build.yml` runs on a pull request whose base is `prod` and on manual dispatch, never on one into `main`. What holds their pairing is `scripts/checks/verify_hold_compare_bypass.py`, a presence guard that says so in its own docstring - it sees that the flag is read, not what is done with it, nor whether the read is on the right branch. macOS is the only platform with a renderer harness (`make metal-presenter-tests`).
- Date: 2026-10-02
- Related: ADR-0237 (shaders on macOS; non-goal amended 2026-10-02 for 2–3 named looks), ADR-0241 / PRD Part B §13 (W-P7, W-P8, W-P10), PRD Part B §6.1 (restore-not-clobber for the quick panel)
- Supersedes / amends: amends ADR-0237's non-goals ("no bundled preset catalogue" is lifted for a short named list; §4 below). It also moves *Hi-res filter* out of the quick panel (PRD §6.1), and the shader selector out of Video settings into Look.

## Context

Three mechanisms change how the game looks, in three places with overlapping
names:

| | What it is in the code | Where it runs | Captured? |
|---|---|---|---|
| Pack art | `HdVideoFilter` (NES), `HdTileVideoFilter`/`SmsHdTileVideoFilter` (GB/SMS) | Core, CPU | yes, it *is* the frame |
| Scale filter | `VideoConfig.VideoFilter` (HQx, xBRZ, Scale2x, 2xSaI, Prescale) | `VideoDecoder`, CPU, after the console filter | yes: `TakeScreenshot` and AVI/GIF read the filtered buffer |
| Shader | `.slangp` through librashader (ADR-0237 on macOS) | renderer, GPU | no (ADR-0237 non-goal) |
| NTSC / LcdGrid | console video filters | Core, CPU | yes |

Measured facts that make the mix-up costly:

- **`_scaleFilter` runs on top of pack art**: `VideoDecoder.cpp` applies it after
  the console filter, so HQ4× over a 4× pack blurs the artist's work and
  multiplies the frame size.
- **NTSC is silently ignored under a pack**: `NesConsole::GetVideoFilter`
  returns `HdVideoFilter` whenever the pack has video content.
- **Screenshots never show the shader**, so "my screenshot doesn't look like my
  screen" is a recurring surprise.

## Decision

1. **One Settings tab, *Look*, names three layers by what they change, in the
   order they apply:**
   - **Art:** the pack, read-only here, with a link to the pack sheet;
   - **Pixels:** the scale-filter family, one popup: *Sharp (original pixels)*,
     *Smooth — HQ4×*, *Smooth — xBRZ 4×*, *More in Options…*;
   - **Screen:** one popup, *Effect*: *None*, *TV signal (NTSC)* (NES only), the
     named looks, recent `.slangp` files, *Choose a shader file…*.

   *Hi-res filter* leaves the quick panel and the shader selector leaves Video
   settings, so each setting lives in one place.
2. **Every choice shows where its result goes:** ◉ "shows in screenshots and
   videos" (Pixels, NTSC, LcdGrid) or ◌ "only on your display" (any shader). The
   mark follows the selection.
3. **Rules from the measurements:**
   - **Pixels is disabled over pack art** on every console with HD art, with
     "Off while a pack draws the art". Look never overrides it; Tools ⋯ ›
     Options still can, for an advanced user.
   - **NTSC is labelled** "Not applied while a pack draws the art" rather than
     looking active.
   - **Shaders over pack art are allowed**, and are what Look recommends for
     "the TV look" on a remastered game.
4. **Named looks.** Two or three bundled `.slangp` presets, *CRT TV* and
   *Handheld LCD* first, each with a GPL-3.0-compatible license recorded with its
   source and sha256 (ADR-0237 as amended). Adding one is a code change; there is
   no catalogue browser.
5. **Hold to Compare.** While held, Pixels and Screen drop and the original
   pixels show; Art stays. No split view. The slice measures the shader swap
   first, and if it stutters, bypasses the chain for the held frames instead of
   rebuilding it.
6. **Unavailable shaders are shown, not hidden.** When `CheckShaderSupport()` is
   false the shader items are disabled with "Shaders are not available in this
   build"; on macOS with the software renderer they read "Needs the Metal
   renderer — restart after changing it", because support is decided at startup.

## Consequences

- PRD §6.1's quick panel loses *Hi-res filter*. A value set elsewhere that is not
  in Look's short list shows as the current item and is never overwritten
  (restore-not-clobber, kept).
- A user who had HQx on with a pack sees it disabled after the upgrade; the
  reason line has to carry that change, and the first slice checks how it reads
  to someone who never knew it was blurring the art.
- Bundling presets brings license review into the release; the slice records it
  per file.
