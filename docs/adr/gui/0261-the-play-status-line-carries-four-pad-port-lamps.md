# ADR-0261: The Play status line carries four pad port lamps, and a lamp means "connected"

- Status: accepted 2026-10-05 (the feature was asked for by the user, verbatim:
  **"seria legal inclusive ter 4 leds para demonstrar os joysticks conectados"**,
  and it ships with the tests this decision is pinned by,
  `UI.Tests/Shell/PadPortLampsTests` and `UI.HeadlessTests/PadPortLampsRenderTests`).
- Date: 2026-10-05
- Related: ADR-0249 (the W-S1 status line, and W-P2's home), ADR-0250 (the
  Play door), ADR-0255 (the Controller sheet's PLAYERS rows, the surface that
  answers the same question in full), ADR-0256 (the pad as the Play GUI's)

## Context

An arcade cabinet's control panel has one lamp per player port, and the player
reads them without a keyboard or a pointer: lit means "a pad is on this port".
The request was for the same four lamps in the app.

Two things had to be decided rather than assumed, and neither is visible from
the request:

1. **What "connected" is read from.** The app has two independent views of a
   pad. The *gamepad* API (`InputApi.GetConnectedGamepadCount` /
   `GetGamepadInfo`) enumerates devices with their names, which is what
   ADR-0255's Controller sheet reads for its PLAYERS rows. The *key manager*
   names the pads its own device numbering sees, which is what ADR-0256's
   bridge navigates by. The two can disagree: a pad the backend can name for
   key codes is not necessarily enumerated by the gamepad API, and a device the
   gamepad API counts need not have a single key named after it. A lamp that
   silently mixed the two would point at a port the app cannot actually drive.
2. **Where it lives, and therefore when it is on screen.** The status line is
   already the shell's read-only sentence about state, and it is hidden while a
   game runs unpaused in Play (`WorkspaceShell.IsBarVisible`) — the game fills
   the window then. Putting the lamps anywhere else would have been a second
   bar, which the Player layout does not have.

## Decision

**The Play status line carries four lamps, one per port, and a lamp is lit when
that port's pad is connected. "Connected" is the gamepad API's count, the same
source ADR-0255's Controller sheet reads.**

- The rule is `UI/Logic/PadPortLamps` — a count and the backend's name lookup
  in, four lamps and an overflow note out, BCL only — so `UI.Tests` pins it
  without a core and without a window (ADR-0123).
- **A lamp means connected, never in use.** Nothing reads whether the pad has a
  mapping, whether it is the active pad, or which console port it is bound to;
  a pad plugged in and bound to nothing still lights its lamp. The Controller
  sheet is where that question is answered in full, and a lamp that guessed
  would be worse than one that says less.
- **Never a fifth lamp.** More pads than ports is reported, not drawn: the
  strip stays four lamps wide and says how many are beyond them, through two
  localized strings (`ShellPadPortsOverflowOne` / `...Many`). The bar's height
  is unchanged.
- The **port number is the label** and the pad's name rides as that lamp's
  tooltip. A port the backend cannot name still lights its lamp: the count is
  the truth about a connection, the name is a nicety about the device.
- It refreshes on a 1 s poll in the existing Play wiring
  (`PlayEdgeFlowsWiring`), and **a strip that has not changed is left alone** —
  the poll must not rebuild the lamps once a second and drop a hovered tooltip
  with them.
- Lit and dim are the status dot's own pair — the theme's live/dim brushes —
  so the strip inherits whatever the theme says and adds no palette.

## Consequences

- **The lamps follow the status bar's own visibility rule, and "unpaused play"
  is not the whole of it.** `WorkspaceShell.IsBarVisible` keeps the bar while a
  game runs unpaused *with a sheet over it* (`sheetOpen`), which is the shape
  ADR-0256 names: the on-load pack picker is posted over a game that is **not**
  paused, and the bar - and therefore the strip - stays on screen over it. So
  the lamps are up on the home, with the pause overlay, and over a non-pausing
  surface; they are off only while a game fills the window with nothing over it.
  That is the honest consequence of the surface chosen, and it is also what a
  cabinet does: the panel is read while the machine is idle. Making them visible
  in every running-game case means a different surface (W-P4's card), which this
  decision does not do.
- The gamepad API's count and the key manager's named pads can disagree, and
  the lamps follow the former. A pad lit here is one the app enumerates, not
  necessarily one the bridge can navigate with; that gap is the backend's, and
  this decision is what keeps it visible rather than hidden behind a second
  definition of "connected".
- The first read happens while `MainWindow` is being constructed, so the strip
  is populated on the first frame rather than one poll later.
- Nothing here reads the pad while a game runs unpaused: the poll is a count
  and a name lookup, never `GetPressedKeys`, so it cannot become a second
  consumer of the pad's buttons (ADR-0256 Decision 1).
