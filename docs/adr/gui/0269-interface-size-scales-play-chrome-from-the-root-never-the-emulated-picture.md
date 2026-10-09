# ADR-0269: Interface size scales Play's chrome from the root, never the emulated picture

- Status: proposed (2026-10-09). The decision comes from the spec on issue #1102
  (slice 5, "Interface size"); the owner has not yet picked the three values in a
  question, so it waits for a human accept. Recorded by the agent under
  owner-away autonomy on #1103; nothing is implemented here.
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
3. **The scale is applied once, from the root of the Play chrome** (a single
   layout transform on that root), not per control. No control carries its own
   size multiplier, so a new surface is scaled by being inside the root.
4. **ADR-0249's token drift test stays valid.** The tokens (colors, radii, type
   sizes) remain the values `scripts/render_gui_wireframes.py` declares; the
   factor multiplies the rendered result and never rewrites a token. The
   wireframes are drawn at Standard (1.0); the test compares tokens, not
   scaled pixels.
5. **The row is not a new element budget breach:** Display becomes strip, four
   rows and Done, six elements (seven while full screen) against PRD §13.3 rule
   2.
6. **A surface that no longer fits at 1.5 scrolls**; nothing is clipped, and Back
   always stays reachable.

## Consequences

- One preference (an enum of three values) joins the existing preferences; a
  value written by a later version that this one does not know reads as Standard.
- The factor is applied on the Play chrome root, so a regression shows in the
  render gate only once a case renders at a non-default size; the ticket that
  builds the row adds that case.
