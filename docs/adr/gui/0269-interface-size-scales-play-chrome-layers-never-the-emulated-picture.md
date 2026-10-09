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
  The same day the cap reached the rows: a right-docked control that carries a
  fixed width kept it inside the narrower sheet and drew its label past the room
  left over, which the review of PR #1145 named as blocking against Decision 6's
  "nothing is clipped". The rows became Auto/star grids whose controls stretch
  into a capped star column, and a headless theory walks every
  tab's rows asserting that no child is drawn outside its row or over a sibling
  — the label-width theory that came with the rows cannot see it, because a
  label in an Auto column is always as wide as its own text; the one spot that
  does not meet the guarantee is named in Decision 6 and is #1149. The same
  review found the cap answering "+Infinity" - no cap - on the first measure
  pass, when the host reports a room of 0: the fallback is now the room itself,
  so the sheet does not lay out at 480 and flash at 720 for a frame. A second
  review round found the same cap still on the control: a control clamped by
  its own `MaxWidth` inside a column wider than that cap is *centred* there
  (Avalonia arranges Stretch and Center from one origin), so at 1024x640 - a
  size where nothing moves - the capped controls sat mid-row. The cap is now on
  the column (`1000*` + `MaxWidth`) and the control stretches into it, which
  makes the control's box the column's box; `HorizontalAlignment="Stretch"` is
  written out on each one because Avalonia's ComboBox is Left by default, and a
  Left child is arranged at min(its column, its own DesiredSize). A headless
  theory pins the cap *and* the control's right edge in that window. No new
  panel pick here either: it is the same #1123 criterion, read as covering the
  rows the cap narrows.
  Nothing is implemented by this ADR; PR #1119 implements it and must match
  Decisions 3 and 6. The width cap, the Auto/star rows and the 512x505 guarantee
  for the settings sheet (Decision 6) are in PR #1145 (#1123).
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
   A host with nothing past the cap's own 32 px margin leaves the sheet the room
   it has, 0 included — the first measure pass, before the host has Bounds,
   reports 0 — so the cap is the room itself and never "no cap": +Infinity there
   let the sheet lay out at its own 480 and flash at 720 drawn for a frame.
   **The cap does not squeeze the rows it narrows.** A setting row is a Grid
   whose first column is Auto and whose last is the capped star (`1000*` +
   `MaxWidth` at the width the wireframes draw it — 150 for a slider, 200 for
   the settings and Look popups): the label holds the width its own text needs
   and the control stretches into that column, so the control's box is the
   column's box. It is drawn at the cap wherever the column has room for it
   (200 stays 200), flush at the row's right edge because no leftover is left
   inside the column to centre it in, and shrinks with the column where the
   row has not the room. Each control carries an explicit
   `HorizontalAlignment="Stretch"` for that: Avalonia's ComboBox is Left by
   default, and a Left child is arranged at min(its column, its own
   DesiredSize), which would shrink the closed popup to its own text. A fixed `Width` was arranged at 150 or 200 whatever cell the control
   was given, and a Grid does not clip, so the control was drawn over the
   label's own column: 70 px of "Output device" under a 200 px popup, 12 px of
   "Volume" under a 150 px slider beside its 34 px readout, 18 px of "Smoothing"
   under a 200 px popup before the rows became grids. A headless theory walks
   all five tabs at 1.5 in 512x505 and asserts, for every row Grid, that each
   visible child's box is inside its row's box and that no two visible children
   intersect — which is what "nothing is clipped" means where a label's own
   Bounds cannot witness it, a label in an Auto column being always as wide as
   its text. The label-width theory that came first still runs over the same
   rows and keeps the weaker half: no label's text needs more room than the
   layout gave it. Display's rows keep their own 120 px popups, narrower than
   the cap has to act on. A control that cannot show a value as long as its box
   is the control's own business and not this guarantee: where the value is host
   data of any length it wraps or is ellipsized as the view says.
   **One spot on this sheet does not meet it**: the Look footer's Hold to Compare
   note, which shares Done's row (W-P10) and is left about 13 px of the 254 px
   page there. It stays on one line and is ellipsized — bounded, not whole — and
   reflowing that footer is a W-P10 decision, filed as #1149.
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
