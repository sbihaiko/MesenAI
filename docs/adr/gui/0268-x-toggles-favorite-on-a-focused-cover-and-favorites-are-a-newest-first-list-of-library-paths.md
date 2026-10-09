# ADR-0268: X toggles Favorite on a focused cover, and Favorites are a newest-first list of library paths

- Status: proposed (2026-10-09). The decision comes from the spec on issue #1102
  (slice 4, "Favorites on Home, X to favorite"); the owner has not yet picked it
  in a question, so it waits for a human accept. Recorded by the agent under
  owner-away autonomy on #1103; nothing is implemented here.
- Date: 2026-10-09
- Related: ADR-0241 (Home), ADR-0250 Decision 3 (*Open a game…* is the one entry
  to the library), ADR-0256 (pad-only Play), ADR-0262 (Y and the on-screen
  keyboard), ADR-0264 (the flat library and its pad map), wireframe W-P20,
  issues #1102 and #1103.
- Supersedes / amends: nothing. ADR-0264 Decision 3 gave Y to search and left X
  unassigned; this ADR assigns X and moves nothing else.

## Context

Home shows *Continue playing* and *Recent*. A game the player returns to every
week is reachable only while it happens to be recent, or through the full library.
Every console dashboard answers this with a shelf the player curates.

## Decision

1. **X toggles Favorite on a focused cover, Y keeps meaning search.** X is a
   surface control, not a navigation control: it acts only where a cover has the
   focus (a Home tile, *Continue*, a library tile) and does nothing elsewhere. A
   player who binds X to a console button keeps that binding (ADR-0256 Decision
   4), as with Y today. The action bar's X entry reads *Favorite* on an
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
5. **A vanished file is kept but not drawn.** If a path no longer resolves (drive
   unplugged, folder moved) its entry stays in the list and is simply not
   drawn, so the favorite returns with the drive. Nothing deletes an entry the
   player did not unfavorite.
6. **Focus order** on Home: Continue, then the Favorites row, then the Recent
   row; the §13.3 rule-2 tally is unchanged from W-P2.

## Consequences

- The list lives in the player's own settings, next to the existing recents; it
  holds paths only, no resident or personal data beyond the file paths the app
  already stores.
- A list that keeps vanished entries can grow without bound; it is capped only by
  the player's own favoriting, which is acceptable at the sizes a human curates.
- Tests this implies: a host-free model test (toggle, newest-first, hidden when
  empty, a missing path kept and not drawn) and a headless pad test that X on a
  focused tile toggles it and the shelf appears and disappears.
