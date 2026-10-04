# ADR-0249: The Player GUI looks like its rendered wireframes, not classic Mesen

- Status: accepted (2026-10-03). Decided by the user after comparing headless Skia renders of the shipped Player GUI with `docs/media/gui-redesign/W-*.png`. The maintainer said the app looked like "algo da década de 80" next to the renders, and the user decided, verbatim: *"quero que siga o render, nao o mesen clássico"*. Implemented in slices: the theme and shared components first, then each workspace's screens. Each slice ships with a render test (Decision 5).
- Date: 2026-10-03
- Related: ADR-0241 (Play / Remaster / Share workspaces), ADR-0150 (headless XAML tests), PRD Part B §13 (wireframes W-S1 … W-X3), `scripts/render_gui_wireframes.py`, `UI/Styles/MesenStyles.xaml`
- Supersedes / amends: amends ADR-0241. The rendered PNGs become the visual spec, and the ASCII wireframes stay the structural spec.

## Context

ADR-0241 and PRD Part B §13 specify the Player GUI twice:
- as ASCII wireframes, which fix the elements, their order and their wording;
- as rendered PNGs (`docs/media/gui-redesign/`, drawn by
  `scripts/render_gui_wireframes.py`).

The implementation followed the ASCII and was tested only for structure: which
controls exist, how many, which one has focus. Nobody compared the result with
the renders before it shipped.

Renders of the shipped views, taken through the real `App.axaml` with Skia,
look nothing like the PNGs. The cause is not in any one view.
`UI/Styles/MesenStyles.xaml` styles the whole application for the classic
debugger-era windows: corner radius 0, "Microsoft Sans Serif" at 11 px,
flat grey bordered buttons. The new workspaces inherit all of it, and they
define no design tokens of their own.

Non-goals:

- The classic windows do not change: the debugger, the tools, Advanced mode's
  dialogs and the classic menus keep `MesenStyles.xaml` exactly as it is.
- No dark variant until one is rendered. The PNGs are drawn in the macOS light
  appearance.
- No pixel-exact match. The renders are a drawing, not a layout engine's
  output.

## Decision

1. **The renders are the visual spec.** Where an ASCII wireframe and its PNG
   agree on structure, the implementation matches the PNG's:
   - typography, colour, radius and spacing;
   - control style (filled primary buttons, grouped rows with tinted icon
     badges, toggles, segmented tabs, cards with soft shadows);
   - the per-workspace tint: Play blue `#007AFF`, Remaster purple `#AF52DE`,
     Share green `#34C759`.
2. **The design tokens come from `scripts/render_gui_wireframes.py`.** That
   includes the palette (`TEXT`, `TEXT2`, `TEXT3`, `SEP`, `WINBG`, `CARD`,
   `FILL`, `RED`, `ORANGE`, `TINT`, `TINT_TEXT`), the type sizes and weights,
   and the radii it draws with.
   - They are transcribed once into `UI/Styles/PlayerTheme.axaml` as resources.
   - A test reads both files and fails if a palette value drifts.
3. **The theme is scoped.** `PlayerTheme.axaml` applies under the Player
   workspace root (a style class on the workspace host), and above it under
   the window's Player chrome: the workspace bar and the status line.
   - Its selectors override `MesenStyles.xaml` only inside that scope.
   - Nothing outside the scope changes, which a test pins: a classic
     dialog's `Button` keeps corner radius 0.
4. **The font is Inter, bundled** (`Avalonia.Fonts.Inter`). It is the closest
   freely redistributable match to the SF Pro the renders use, and it is the
   same on macOS, Windows and Linux. The body is 13–15 px and the titles
   follow the renders' sizes.
5. **A render gate.** The headless suite renders each implemented wireframe's
   screen with Skia (`UseHeadlessDrawing = false`) and writes the PNGs. A
   test then asserts what a structural test cannot:
   - the resolved font family, size and corner radius of the key controls;
   - each workspace's tint on its primary button;
   - that the screen's background is the theme's, not the classic one.

   The PNGs are kept as CI artifacts and attached to UI pull requests, so a
   person compares them with `docs/media/gui-redesign/` before merge.

## Consequences

- Two style systems live side by side, the classic one and the Player one.
  The scope boundary is what keeps them apart, and its test is what keeps it
  from leaking either way.
- Inter adds a few hundred KB to every build.
- Every Player view is touched again. The structural tests stay as they are,
  and the new render tests are added next to them.
- The renderer script and `PlayerTheme.axaml` must change together, and the
  drift test makes the second one fail when only the first moves.
