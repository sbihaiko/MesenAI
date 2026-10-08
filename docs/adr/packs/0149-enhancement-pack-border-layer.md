# ADR-0149: Enhancement pack border layer (border/overlay frame) format and Core rendering architecture

- Status: accepted
- Date: 2026-09-02
- Related: MEP-v1 (§3.1, §5), PRD Part A §4 (Phase 8), PRD Part B §6.1 (Enhancements quick-toggle panel), ADR-0005 (MEP envelope), ADR-0040 (storage/precedence), ADR-0050 (auto screen backgrounds).
- Supersedes / amends: MEP-v1 §3.1 & §5 (adds optional `border` section and layout specification in v1.5).

## Context

Retro communities and emulators (Super Game Boy/SGB borders, RetroArch overlay bezels, custom artwork packs) frame the game viewport with decorative borders. Phase 7 established the "faithful, then enhanced, on by default" thesis and the Player Shell's "Enhancements" quick-toggle panel (Part B §6.1); the roadmap sets Phase 8 as the Enhancement pack border layer: a declarative asset format, a Core compositing pipeline that does not distort game rendering or HUD overlays, and an explicit `EnhancementPackConfig.EnableBorder` gate available in both the quick-toggle panel and Advanced preferences.

Non-goals:
- Automatic heuristic generation of borders (borders are authored pack assets, not engine-generated).
- Emulation of Super Game Boy SNES core hardware (SGB SNES core was removed on 2026-08-26; only static/semi-static border overlay assets are supported).
- Distorting the game view: game aspect ratio is preserved within the designated viewport.

## Decision

### 1. MEP Specification Extension: `border` Section (MEP v1.5)

MEP-v1 is amended to v1.5 to introduce an optional root section under `sections`:
```json
{
  "mep": "1.5.0",
  "name": "Super Mario Bros. Deluxe Bezel",
  "version": "1.0.0",
  "id": "smb-deluxe-bezel",
  "targets": [
    { "system": "nes", "sha1": "3337A01683DE92F05103E994B6982F2494E80D21" }
  ],
  "sections": {
    "textures": { "path": "textures/" },
    "border": { "path": "border/" }
  }
}
```

#### Border Section Structure
Under `sections.border.path` (or `<Game>/border/` or `<Game>/mep/border/` in folder convention):
- `border.png`: the primary 32-bit RGBA border frame image.
- `border.json` (optional): viewport layout and configuration. When omitted, default layout heuristics apply (16:9 canvas with centered original aspect ratio viewport).

#### `border.json` Specification
```json
{
  "version": 1,
  "width": 1920,
  "height": 1080,
  "viewport": {
    "x": 240,
    "y": 0,
    "width": 1440,
    "height": 1080
  },
  "scale_mode": "fit",
  "underlay": false
}
```

Fields:
- `width`, `height` (MUST, integer > 0): intended design resolution of `border.png` (typically 1920x1080 or 16:9 / 16:10).
- `viewport` (MUST, object): target rectangle for the game frame within `width` × `height` — `x`, `y`, `width`, `height` (integers >= 0).
- `scale_mode` (optional, default `"fit"`): `"fit"` scales the composite canvas to fill the window preserving canvas aspect ratio (neutral outer letterboxing if needed); `"stretch"` stretches the border canvas to the output surface.
- `underlay` (optional, boolean, default `false`): `false` composites the border as an overlay on top of the game frame (alpha bezel edges may softly overlap the game border); `true` composites the border behind the game frame (game drawn strictly over viewport).

#### Convention Layout Probing
`MepPack::DetectConventionLayout` probes `border/border.png` (human layer) and `auto/border/border.png` (if generated); as with other sections, the human layer in `mep/` or root takes precedence.

### 2. Core Rendering and Compositing Architecture

#### Rendering Pipeline Integration
The border compositing path is integrated into `VideoRenderer` and `BaseVideoFilter`:
1. **Separation of Concerns:** emulation video decoding, PPU rendering and filters (`BaseVideoFilter`, `ScaleFilter`, `ScanlineFilter`) continue to output the raw emulated frame at native or upscaled game resolution; the game aspect ratio calculation (`EmuSettings::GetAspectRatio`) remains faithful.
2. **Compositing in VideoRenderer:** in `VideoRenderer::RenderThread`, when `EnhancementPackConfig.EnableBorder` is active and the active MEP pack provides a valid border, the game frame is rendered into the viewport defined by `border.json`; the `border.png` surface is blended with standard source-over alpha blending onto the output frame; HUD layers (`SystemHud`, `DebugHud`, `ScriptHud`) render on top of the final composited frame or map to the game viewport per HUD settings.
3. **Thread Safety & Asset Caching:** border image decoding happens once at pack load (`MepPackManager` / `BorderManager`) and is cached as a 32-bit ARGB surface (`RenderSurfaceInfo` or raw buffer); if `border.png` fails to decode or has invalid dimensions, the border is skipped gracefully without interrupting gameplay.

### 3. Configuration & UI Exposure

1. **`EnhancementPackConfig` (Core C++ & UI C#):** new field `bool EnableBorder = true;`, added to `Core/Shared/SettingTypes.h` and `UI/Config/EnhancementPackConfig.cs` (struct `InteropEnhancementPackConfig` kept in exact ABI lockstep).
2. **Player Shell Quick-Toggle Panel (Part B §6.1):** the 7th toggle "Border" is added to `PlayerEnhancementsPanel` in `MainWindow.axaml` and `MainWindowViewModel.cs`; it directly toggles `EnhancementPackConfig.EnableBorder` and is disabled/grayed out when the active game/pack provides no border.
3. **Advanced Mode:** checkbox "Enable pack border" added to `EnhancementPacksWindow`.

### 4. Validation and Tooling (`scripts/`)

1. **`scripts/mep_lint.py`:** validates `sections.border` — `path` exists and contains `border.png`; PNG header, resolution and optional `border.json` schema; warns if `viewport` exceeds canvas bounds or has negative coordinates.
2. **`scripts/validate-specs.py`:** `validate_mep` recognizes `"border"` alongside `"textures"`, `"audio"`, `"synth"`.

## Consequences

- Authors ship high-fidelity borders/bezels without emulator-specific overlay hacks.
- `EnableBorder` lets players toggle borders off instantly (pure black bars or stretch modes).
- ABI safety: `InteropEnhancementPackConfig` is updated symmetrically in C++ and C#.
- Zero performance impact when no border is present or when `EnableBorder == false`.

## Amendments (2026-09-06, code-review pass)

- The border asset is resolved once, not per frame. `VideoRenderer` holds a notification listener that marks the border dirty on `GameLoaded`, `BeforeGameUnload` and `EmulationStopped`; the interop setters `SetPreferredMepPack`/`SetMepPackEnabled` call `VideoRenderer::InvalidateBorderAsset()` so a UI-side pack change takes effect on the next frame. The decode thread no longer reads `MepPackManager` state per frame (that read was unsynchronised against the emu and UI threads).
- Border PNGs above 8192 px per side or above `FrameCaptureMath::MaxCapturePixels` are refused with a log line. The overlay blend precomputes the border over black at load and blends only the clamped viewport per frame; rounding is round-to-nearest (±1 LSB versus the previous truncation).
- Open points, deliberately unchanged: `scale_mode` is parsed but not applied (Stretch has no effect); AVI/GIF recording captures the pre-border frame. Both need a decision before the next border-related slice.

## Amendment (2026-10-05) — the border viewport: MEP-v1 §5.4 governs, and two of the three blocked parts were not blocked

An implementation pass (PRD Part A F8.4, squad run-20261005-210625) stopped on what it read as a
contradiction between this ADR and the published spec, and asked which text wins. The ruling:
**MEP-v1 §5.4 governs**, because it is the contract pack authors are handed (ADR-0004, ADR-0005)
while this ADR is an internal decision — and the internal document is the one that yields. What the
pass reported as three blocked parts is one blocked, one already expressible, and one that only
needed the decision it asked for:

- **Letterboxing inside the viewport — blocked, and it stays blocked.** MEP-v1 §5.4's `viewport`
  row is a MUST: the host "scales the game frame to fill the rectangle exactly (nearest-neighbor),
  it does **not** letterbox inside it". The Non-goals line above — "game aspect ratio is preserved
  within the designated viewport" — is the clause that conflicts, and the one that yields.
  Implementing the PRD's "letterbox inside the viewport" means amending the published spec, which is
  a question about MEP v1.6, not a border slice.
- **The console's aspect in the default viewport — not blocked.** The 4:3 default is scoped: it
  applies "when `border.json` is absent, or its `viewport` is missing/invalid", and the same row
  says "any other layout needs an explicit `viewport`". A pack that wants the console's aspect
  already says so, per pack, in the format; the default exists for the "4:3 game inside a 16:9
  bezel" case. Changing the *default* to the loaded console's aspect would be a spec change —
  wanting the console's aspect is not.
- **`scale_mode` — decided, and the decision is "stays unapplied".** The 2026-09-06 amendment above
  recorded it as an open point needing "a decision before the next border-related slice". This is
  that decision. It stays unapplied because MEP-v1 §5.4's `scale_mode` row tells authors they MUST
  NOT rely on `"stretch"` yet and documents that the reference hands the canvas to the regular video
  scaler: applying it would make the emulator honor a mode the published spec tells authors not to
  use. `BorderLayout::ParseScaleMode` and `CanvasRectOnOutput` stay as they are — parsed, pinned by
  tests, and consumed by no production caller, which is the state the spec describes.
- **Untouched and unblocked:** the lint of a bare root `border.png`, the fourth part of F8.4.

F8.4 therefore stays unscheduled, and the reason is now the spec rather than an open question: only
its letterbox part is blocked, and only by a MUST in a published document.
