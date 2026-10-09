# ADR-0268: X toggles Favorite on a focused cover, and Favorites are a newest-first list of library paths

- Status: accepted (2026-10-09), by the autonomy panel (Opus 5.5 as the human
  proxy, issue #1103 comment) with edits, pick quoted verbatim: **"Accept ADR-0268, ADR-0269 and the ADR-0254 amendment with the listed edits; PR #1119 waits until ADR-0269 is accepted with Decisions 3 and 6 matching its code."**
  The decision comes from the spec on issue #1102 (slice 4, "Favorites on Home,
  X to favorite"). Nothing is implemented by this ADR; the build waits for its
  own ticket.
- Date: 2026-10-09
- Related: ADR-0241 (Home), ADR-0250 Decision 3 (*Open a game…* is the one entry
  to the library), ADR-0256 (pad-only Play), ADR-0262 (Y and the on-screen
  keyboard), ADR-0264 (the flat library and its pad map), wireframe W-P20,
  issues #1102 and #1103.
- Supersedes / amends: amends ADR-0264 Decision 3 and its *Not decided here*
  list (see ADR-0264's 2026-10-09 Amendment). ADR-0264 Decision 3 gave Y to
  search and left X unassigned; this ADR assigns X, takes favorites out of that
  list, and moves nothing else.

## Context

Home shows *Continue playing* and *Recent*. A game the player returns to every
week is reachable only while it happens to be recent, or through the full library.
Every console dashboard answers this with a shelf the player curates.

## Decision

1. **X toggles Favorite on a focused cover, Y keeps meaning search.** X is a
   surface control, not a navigation control: it acts only where a cover has the
   focus (a Home tile, *Continue*, a library tile) and does nothing elsewhere. On
   the *Continue* card X acts on the Continue game (the card is not a cover of
   its own; it favorites the game it resumes). A
   player who binds X to a console button keeps that binding (ADR-0256 Decision
   1), as with Y today; X is not a navigation control (ADR-0256 Decision 4). The action bar's X entry reads *Favorite* on an
   unfavorited cover and *Unfavorite* on a favorited one. Y is unchanged
   (ADR-0264 Decision 3); while the on-screen keyboard is open every press is
   the keyboard's (ADR-0262).
2. **A favorite is identified by its library path.** The same ROM reached by
   another path is another entry; there is no hash lookup, so toggling never
   waits on a scan or a hash.
3. **The Favorites model is a list, newest-first.** Favoriting a game puts it at
   the front; unfavoriting removes it; the order is never otherwise rewritten, so
   playing a game does not move it.
4. **Home draws a Favorites shelf between *Continue* and *Recent*, hidden when
   the list is empty.** With no favorites Home is W-P2 as it is today; with no
   recent game it is W-P1, favorites or not. The picture is W-P20.
5. **A vanished file is kept and drawn dimmed in the library.** If a path no
   longer resolves (drive unplugged, folder moved) its entry stays in the list.
   The Favorites shelf on Home does not draw it, but the library draws that
   favorite dimmed, so X can still unfavorite it (a removal route exists) and
   the favorite returns to normal with the drive. Nothing deletes an entry the
   player did not unfavorite.
6. **Focus order** on Home: Continue, then the Favorites row, then the Recent
   row; the §13.3 rule-2 tally is unchanged from W-P2.

## Consequences

- The list lives in the player's own settings, next to the existing recents; it
  holds paths only, no data beyond the file paths the app already stores in its
  recents.
- A list that keeps vanished entries can grow without bound; it is capped only by
  the player's own favoriting, which is acceptable at the sizes a human curates.
- Tests this implies: a host-free model test (toggle, newest-first, hidden when
  empty, a missing path kept, absent from the shelf and drawn dimmed in the library with X unfavoriting it) and a headless pad test that X on a
  focused tile toggles it and the shelf appears and disappears.
