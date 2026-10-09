# ADR-0269: Interface size scales Play's chrome layers (one transform on each of the four in Decision 3), never the emulated picture

- Status: accepted (2026-10-09), by the autonomy panel (Opus 5.5 as the human
  proxy, issue #1103 comment) with edits, pick quoted verbatim: **"Accept ADR-0268, ADR-0269 and the ADR-0254 amendment with the listed edits; PR #1119 waits until ADR-0269 is accepted with Decisions 3 and 6 matching its code."**
  The decision comes from the spec on issue #1102 (slice 5, "Interface size").
  Amended 2026-10-09 (Decisions 2 and 6) by the same panel after the #1119
  circuit breaker, pick quoted verbatim: **"Narrow PR #1119 on 1024x640: the settings-sheet and four-layer mechanism ship, the minimum-width cap and the pill scale are follow-ups #1123 and #1124."**
  Decision 6's guarantee was widened 2026-10-09 by the width cap of issue #1123
  (the follow-up the panel's pick above names), whose acceptance criteria are
  the owner's own: the cap goes against the transformed room the way the height
  already did, a headless test pins it in the 512x505 starting window, and this
  Decision names the guaranteed size. No new panel pick: the work is what #1123
  asked for.
  Nothing is implemented by this ADR; PR #1119 implements it and must match
  Decisions 3 and 6. The width cap and the 512x505 guarantee for the settings
  sheet (Decision 6) are in PR #1145 (#1123).
- Date: 2026-10-09
- Related: ADR-0249 (tokens come from the renderer; the drift test), ADR-0241,
  ADR-0256, wireframe W-P8d, issues #1102 and #1103.
- Supersedes / amends: nothing. Settings › Display's *Scale* row keeps its
  meaning.

## Context

Nothing in Play is sized for a TV three meters away. Settings › Display has a
*Scale* row, but it scales the emulated picture, not the interface.

## Decision

1. **One new row, *Interface size*, in Settings › Display**, directly below
   *Scale*: **Standard**, **Large**, **Extra large**, which are factors **1.0,
   1.25 and 1.5**. Default Standard. Left / Right on the focused row steps the
   value in place and applies at once.
2. **It scales Play's chrome only** — the Play shell's own surfaces (Home, the
   library sheet, W-P4, the settings sheet, the action bar and toasts). It never
   scales the emulated picture (that is *Scale*), and Remaster and Share do not
   read it.
   The pack-install pill is a separate top-level Popup
   (ShouldUseOverlayLayer=False) outside the four layers, so it stays at 1.0 for
   now; scaling it is deferred to #1124 and does not block the first delivery.
3. **The scale is applied once per Play chrome layer, never per control.** There
   is one layout transform on each of four layers: the Play chrome root, the
   settings sheet layer, the load-wait host and the BIOS/ROM/tool sheet layer.
   Each applies the factor only when the active workspace is Play, so Remaster
   and Share never read it. No control carries its own size multiplier, so a new
   surface is scaled by being inside one of those layers. Toasts live under the
   Play chrome root and scale with it; the one exception is the pack-install
   pill, deferred to #1124.
4. **ADR-0249's token drift test stays valid.** The tokens (colors, radii, type
   sizes) remain the values `scripts/render_gui_wireframes.py` declares; the
   factor multiplies the rendered result and never rewrites a token. The
   wireframes are drawn at Standard (1.0); the test compares tokens, not
   scaled pixels.
5. **The row is not a new element budget breach:** Display becomes strip, four
   rows and Done, six elements (seven while full screen) against PRD §13.3 rule
   2.
6. **A surface that no longer fits at 1.5 scrolls**; nothing is clipped. The
   ScrollViewer sits around the page rows of the surface; the Done row (and Back)
   stays pinned outside it, so it is always reachable. Both sides of the sheet's
   box are capped against the room its host gives it — the height was, the width
   is since #1123 — so the sheet shrinks with the window instead of hanging off
   it at a larger size; 480 stays the width wherever it fits.
   **The settings sheet's guaranteed size is the window's own starting size,
   512x505, at every size including Extra large (1.5)**, and still the PRD's
   drawn size, ~1024x640 (spec #1102; PRD Part B wireframes). A headless test
   pins both ends at factor 1.5: at 1024x640 the rows scroll and the Done row is
   fully on screen and reachable; at 512x505 the sheet is capped to the room
   (310 px of the 342 the host gives it, 465 px drawn) and Done is inside the
   window on every tab. Windows below 512x505, down to the 160x144 floor — the
   width the window's own `MinWidth`/`MinHeight` allow — are not guaranteed.
   The other Play sheets (Cheats, Replays, PackPicker, PackDetail, Tool,
   Controller, Enhancements, Shader) and the pause card are not covered by this
   guarantee yet: they are still guaranteed only at about 1024x640, because the
   width cap so far reaches the settings sheet alone. The same cap for those
   surfaces is the remainder of #1123, which stays open as their follow-up.

## Consequences

- One preference (an enum of three values) joins the existing preferences; a
  value written by a later version that this one does not know reads as Standard.
- The factor is applied on the four Play chrome layers, so a regression shows in the
  render gate only once a case renders at a non-default size; the ticket that
  builds the row adds that case.
