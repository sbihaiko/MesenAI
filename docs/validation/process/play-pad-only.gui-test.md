# play-pad-only

<!-- Rendered from the JSON script by gui_test_render.py. Do not edit; edit the JSON. -->

- Format: `gui-test/1`
- Target: `mesen-gui`
- Requires actions: `pad.press`
- Requires checks: `ui.focused`, `ui.screen`
- Variant `window.mode`: `windowed`

## Fixtures

```json
{
  "note": "no `rom`, `rom-second`, `rom-zip`, `rom-fds`, `rom-library-large`, `pack` or `pack-multi` fixture is committed: a `rom` fixture needs an absolute path to a real ROM and its No-Intro sha1 (ADR-0003), which a script in this repository cannot carry, and mesen-gui places no file it was not given. The steps that need one are `manual` - they name the fixture they need in their own text. `settings.profile` is not a scripting convention: `scripts/gui_test/mesen_gui_adapter.py` `resolve_fixtures()` reads the `profile` key of the `settings` fixture against its own `PROFILES` list (default `fresh`), and `fresh` is that list's only entry - the seeded portable settings.json beside the cloned app folder.",
  "settings": {
    "profile": "fresh",
    "profiles": {
      "fresh": "no play history, no favorites",
      "history-no-favorite": "one game in play history (so Continue and Recent exist) and no favorites (FAV-01 starts from an empty Favorites shelf)",
      "history-one-favorite": "one game in play history (so Continue and Recent exist) and that game favorited (so Favorites exists, ADR-0268 Decision 4)",
      "history-two-favorites": "two games in play history (so Continue and Recent exist) and both favorited, with the Continue game (the most recently played, fixture rom) as the newest favorite and so the first Favorites tile (ADR-0268 Decision 3), and the older game (fixture rom-second) as the other favorite; unfavoriting the Continue game via X leaves the Favorites shelf visible (ADR-0268 Decision 4)"
    }
  }
}
```

## Batch `launch`

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `launch.first-run-home` | under-test | `fixture settings.profiles.fresh` | — | `ui.screen == play.home` within 120 ticks | `ui.screen(is="play.home")` | No wizard appears (ADR-0256 Decision 8); Home W-P1 is on screen. | window.mode=windowed | major | automated |
| `launch.open-rom-focused` | under-test | `ui.screen == play.home` | — | `ui.focused == play.home.open-rom` within 120 ticks | `ui.focused(is="play.home.open-rom")` | The one control, Open a ROM, is already focused at launch. | window.mode=windowed | major | automated |
| `launch.port-lamp-p1` | under-test | `by hand: the screen is play.home` | — | — | — | The ring is visible on Open a ROM and the pad port lamps show P1 lit (ADR-0261); judged by eye, the hook exposes neither. | window.mode=windowed | minor | manual |

## Batch `home`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `home.first-focus` | setup | `the application has just launched on a fresh settings folder` | — | `ui.focused == play.home.open-rom` within 120 ticks | `ui.focused(is="play.home.open-rom")` | HOME-01 - no wizard (ADR-0256 Decision 8); Play Home W-P1 is on screen and Open a ROM is already focused | all | major | automated |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `home.focus-holds-up` | under-test | `ui.focused == play.home.open-rom` | `pad.press(button="Up", ticks=4)` | within 4 ticks | `ui.focused(is="play.home.open-rom")` | Focus stays on Open a ROM after D-pad Up. (HOME-02, HOME-04) | all | major | automated |
| `home.focus-holds-down` | under-test | `ui.focused == play.home.open-rom` | `pad.press(button="Down", ticks=4)` | within 4 ticks | `ui.focused(is="play.home.open-rom")` | Focus stays on Open a ROM after D-pad Down. | all | major | automated |
| `home.focus-holds-left` | under-test | `ui.focused == play.home.open-rom` | `pad.press(button="Left", ticks=4)` | within 4 ticks | `ui.focused(is="play.home.open-rom")` | Focus stays on Open a ROM after D-pad Left. | all | major | automated |
| `home.focus-holds-right` | under-test | `ui.focused == play.home.open-rom` | `pad.press(button="Right", ticks=4)` | within 4 ticks | `ui.focused(is="play.home.open-rom")` | Focus stays on Open a ROM after D-pad Right. | all | major | automated |
| `home.ring-visible` | under-test | `by hand: the ring is on play.home.open-rom` | — | — | — | The focus ring never disappears through the four presses. | all | minor | manual |
| `home.open-library` | under-test | `ui.focused == play.home.open-rom` | `pad.press(button="A", ticks=4)` | `ui.screen == play.library` within 120 ticks | `ui.screen(is="play.library")` | A on Open a ROM opens the library sheet (LIB-01). (HOME-02) | all | major | automated |
| `home.back-to-home` | under-test | `ui.screen == play.library` | `pad.press(button="B", ticks=4)` | `ui.screen == play.home` within 120 ticks | `ui.screen(is="play.home")` | B on the sheet returns to Home. | all | major | automated |
| `home.back-focus-restored` | under-test | `by hand: the screen is play.home` | — | — | — | The ring is back on Open a ROM after B. (manual: needs action pad.release, which mesen-gui does not advertise) | all | major | manual |
| `home.chord-opens-nothing` | under-test | `by hand: the screen is play.home` | — | — | — | The chord with no game loaded opens no overlay. (HOME-05) (manual: needs action pad.chord, which mesen-gui does not advertise) | all | major | manual |
| `home.chord-focus-unchanged` | under-test | `by hand: the screen is play.home` | — | — | — | Focus is unchanged after the chord. (manual: needs action pad.release, which mesen-gui does not advertise) | all | minor | manual |
| `home.settings-by-pad-gap` | under-test | `by hand: the screen is play.home` | — | — | — | Y opens the Settings sheet from Home with no game loaded (bug #1177, ADR-0256 W-P8); B returns to Home. | all | major | manual |

## Batch `home-after-game`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `after-game.reach-home` | setup | `fixture settings.profiles.history-one-favorite and fixture rom` | — | — | — | Home W-P2 (Continue / Favorites / Recent) with at least one game played (the HOME-03 precondition). (manual: needs action nav.goal, which mesen-gui does not advertise) | all | major | manual |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `after-game.continue-first` | under-test | `by hand: the screen is play.home` | — | — | — | Continue playing is the first focus stop. (manual: needs action pad.release, which mesen-gui does not advertise) | all | major | manual |
| `after-game.order-favorites` | under-test | `by hand: the ring is on play.home.continue` | — | — | — | Favorites follows Continue when the shelf exists (ADR-0268 Decision 6). (manual: needs control play.home.favorites, which mesen-gui does not advertise) | all | major | manual |
| `after-game.order-recent` | under-test | `by hand: the ring is on play.home.favorites` | — | — | — | Recent follows Favorites (Continue, Favorites, Recent; ADR-0268 Decision 6). (manual: needs control play.home.recent, control play.home.favorites, which mesen-gui does not advertise) | all | major | manual |
| `after-game.order-open-rom` | under-test | `by hand: the ring is on play.home.recent` | — | — | — | Open a ROM is reachable last. (manual: needs control play.home.recent, which mesen-gui does not advertise) | all | major | manual |
| `after-game.one-ring` | under-test | `by hand: the screen is play.home` | — | — | — | The ring is on exactly one control at a time (ADR-0256 Decision 3); with a Favorites shelf present the order is Continue, Favorites, Recent. Both are judged by eye. | all | minor | manual |
| `after-game.contained-up-continue` | under-test | `by hand: the ring is on play.home.continue` | — | — | — | Repeated D-pad Up from Continue never leaves the Home host for the header's Profile or Tools buttons (#1137, #1166). (manual: batch setup after-game.reach-home is manual, so nothing established the state this step asserts) | all | major | manual |
| `after-game.contained-up-recent` | under-test | `by hand: the ring is on play.home.recent` | — | — | — | Repeated D-pad Up from Recent never leaves the Home host for the header's Profile or Tools buttons (#1137, #1166). (manual: needs control play.home.recent, which mesen-gui does not advertise) | all | major | manual |
| `after-game.contained-up-favorites` | under-test | `by hand: the ring is on play.home.favorites` | — | — | — | Repeated D-pad Up from Favorites never leaves the Home host for the header's Profile or Tools buttons (#1137, #1166). (manual: needs control play.home.favorites, which mesen-gui does not advertise) | all | major | manual |
| `after-game.contained-up-open-rom` | under-test | `by hand: the ring is on play.home.open-rom` | — | — | — | Repeated D-pad Up from Open a ROM never leaves the Home host for the header's Profile or Tools buttons (#1137, #1166). (manual: batch setup after-game.reach-home is manual, so nothing established the state this step asserts) | all | major | manual |
| `after-game.contained-sideways-left` | under-test | `by hand: the ring is on play.home.continue` | — | — | — | Left on the top row stays inside the Home host. (manual: batch setup after-game.reach-home is manual, so nothing established the state this step asserts) | all | major | manual |
| `after-game.contained-sideways-right` | under-test | `by hand: the ring is on play.home.continue` | — | — | — | Right on the top row stays inside the Home host. (manual: batch setup after-game.reach-home is manual, so nothing established the state this step asserts) | all | major | manual |
| `after-game.contained-other-controls-gap` | under-test | `by hand: the screen is play.home` | — | — | — | From Favorites, Recent and Open a ROM, D-pad Left and Right keep focus inside Home (the automated steps press them from Continue only) | all | major | manual |

## Batch `library`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `lib.home-ring` | setup | `the application has just launched on a fresh settings folder` | — | `ui.focused == play.home.open-rom` within 120 ticks | `ui.focused(is="play.home.open-rom")` | HOME-01 - the batch opens on a settled home before it presses anything; the ring is not up yet at a fresh launch, so a press sent first is dropped and the step below reads None (#1232). | all | major | automated |
| `lib.reach-library` | setup | `ui.focused == play.home.open-rom` | `pad.press(button="A", ticks=4)` | `ui.screen == play.library` within 120 ticks | `ui.screen(is="play.library")` | LIB-01 - A on the Home's focused Open a ROM opens the Library W-P19 by pad alone, on a fresh settings folder | all | major | automated |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `lib.open-screen` | under-test | `ui.screen == play.library` | — | `ui.screen == play.library` within 120 ticks | `ui.screen(is="play.library")` | LIB-01: the flat library grid is open. | all | major | automated |
| `lib.first-tile-focused` | under-test | `by hand: the screen is play.library` | — | — | — | LIB-01: a tile is focused (ring on the grid's first tile). (manual: needs control play.library.tile, which mesen-gui does not advertise) | all | major | manual |
| `lib.header-footer-scan` | under-test | `by hand: the screen is play.library` | — | — | — | LIB-01: header reads "Your library · N games in M folders", the footer names A/B/Y/X/LB/RB in the pad's own words (ADR-0256 Decision 6), an animated indicator shows while scanning. | all | minor | manual |
| `lib.hold-down-repeats` | under-test | `fixture rom-library-large and a library of 100+ ROMs` | — | — | — | LIB-02: with 100+ ROMs, a held Down repeats after 400 ms then every 100 ms (ADR-0256 Decision 7) and the grid scrolls; judged by timing and eye. Part 2 (same with Right) is the next step. | all | major | manual |
| `lib.hold-right-repeats` | under-test | `fixture rom-library-large and a library of 100+ ROMs` | — | — | — | LIB-02: the same with Right: a held Right repeats after 400 ms then every 100 ms and the focus walks the row, wrapping onto the next row. | all | major | manual |
| `lib.tap-down-steps-once` | under-test | `fixture rom-library-large and a tile focused in the library` | — | — | — | LIB-02: one tap steps the focus once and the focus stays in the grid. (manual: needs control play.library.tile, which mesen-gui does not advertise) | all | major | manual |
| `lib.up-back-to-row-one` | under-test | `a tile focused in the library` | — | — | — | LIB-03 setup: Up returns the focus from row 2 to the top grid row, ready to enter the header. (manual: needs control play.library.tile, which mesen-gui does not advertise) | all | major | manual |
| `lib.up-enters-header` | under-test | `a tile focused in the library` | — | — | — | LIB-03: Up from the top grid row enters the header row (search first, ADR-0264 Decision 3). (manual: needs control play.library.search, control play.library.tile, which mesen-gui does not advertise) | all | major | manual |
| `lib.header-right-folders` | under-test | `by hand: the ring is on play.library.search` | — | — | — | LIB-03: Right moves to Library folders. (manual: needs control play.library.folders, control play.library.search, which mesen-gui does not advertise) | all | major | manual |
| `lib.header-right-browse` | under-test | `by hand: the ring is on play.library.folders` | — | — | — | LIB-03: Right moves to Browse a file. (manual: needs control play.library.browse, control play.library.folders, which mesen-gui does not advertise) | all | major | manual |
| `lib.header-right-back` | under-test | `by hand: the ring is on play.library.browse` | — | — | — | LIB-03: Right moves to Back. (manual: needs control play.library.back, control play.library.browse, which mesen-gui does not advertise) | all | major | manual |
| `lib.header-left-across` | under-test | `by hand: the ring is on play.library.back` | — | — | — | LIB-03: Left walks back across the header row (Back -> Browse), mirroring the Right walk. (manual: needs control play.library.browse, control play.library.back, which mesen-gui does not advertise) | all | major | manual |
| `lib.header-down-grid` | under-test | `by hand: the ring is on play.library.browse` | — | — | — | LIB-03: Down from the header returns to the grid. (manual: needs control play.library.tile, control play.library.browse, which mesen-gui does not advertise) | all | major | manual |
| `lib.filter-rb-wraps` | under-test | `by hand: the screen is play.library` | — | — | — | LIB-04: RB steps All -> each console present -> All, wrapping at both ends, the grid narrows, and the ring lands on the segment the press selected (PlayerRomPickerViewModel.ConsoleFilter.cs; ADR-0264 amendment 2026-10-09, #1108), never an empty filter. Record whether the segment the ring is on is obvious at couch distance. The row is not a named control the hook reads, so the landing is judged by eye. | all | minor | manual |
| `lib.filter-lb-backwards` | under-test | `by hand: the screen is play.library` | — | — | — | LIB-04: LB walks the same ring backwards, wrapping at both ends, and lands the ring on the segment it moved (ADR-0264 amendment 2026-10-09, #1108). | all | minor | manual |
| `lib.filter-segment-focus` | under-test | `by hand: the screen is play.library` | — | — | — | LIB-04 (ADR-0264 amendment 2026-10-09, #1108): after a shoulder press the ring is ON the chip row, on the segment the press selected - the row is one element, so its Left/Right step the same ring one console at a time, its Up leaves for the header and its Down returns to the grid it filters, and the footer names the shoulders and no Play (ADR-0256 Decision 6). The landing is skipped only where the sheet's own claim outranks it: while a restore is in flight (IsRestorePending / IsRestoreLanding) the ring lands on the remembered game, and the search box keeps the ring as it always did. FAIL if the ring does not reach the row outside those two. The row is not a named control the hook reads, so this is judged by eye. Record whether the ring on the row is obvious at couch distance. | all | major | manual |
| `lib.search-y-focuses-field` | under-test | `a tile focused in the library` | — | — | — | LIB-05: Y moves the ring to the search field. (manual: needs control play.library.search, control play.library.tile, which mesen-gui does not advertise) | all | major | manual |
| `lib.search-a-opens-keyboard` | under-test | `by hand: the ring is on play.library.search` | — | — | — | LIB-05: A opens the shared on-screen keyboard (ADR-0262) below the field; the sheet stays open. (manual: needs control play.keyboard, control play.library.search, which mesen-gui does not advertise) | all | major | manual |
| `lib.search-type-filters` | under-test | `by hand: play.keyboard is visible` | — | — | — | LIB-05: typing `zel` (or a prefix of an owned title) with D-pad + A filters the grid per letter; OK commits and focus returns to the field; an empty result shows "No games match" with a way to clear it. (manual: needs control play.keyboard, which mesen-gui does not advertise) | all | minor | manual |
| `lib.search-b-cancels` | under-test | `by hand: play.keyboard is visible` | — | — | — | LIB-05: B on the open keyboard closes it, focus returns to the search field and the sheet does not close. The text revert on a second Y, A, B is judged by eye (next step). (manual: needs control play.library.search, control play.keyboard, which mesen-gui does not advertise) | all | major | manual |
| `lib.search-second-cancel-reverts` | under-test | `by hand: the ring is on play.library.search` | — | — | — | LIB-05: after typing, a second Y, A, B returns the field to its previous text; focus returns to it and the sheet does not close (typing is manual, so the text revert is manual). (manual: needs control play.library.search, which mesen-gui does not advertise) | all | minor | manual |
| `lib.search-sheet-stays` | under-test | `by hand: the ring is on play.library.search` | — | — | — | LIB-05: the library sheet is still open under the cancelled keyboard. (manual: needs control play.library.search, which mesen-gui does not advertise) | all | major | manual |
| `lib.search-right-folders` | under-test | `by hand: the ring is on play.library.search` | — | — | — | LIB-06: Right from the search field reaches Library folders. (manual: needs control play.library.folders, control play.library.search, which mesen-gui does not advertise) | all | major | manual |
| `lib.folders-open` | under-test | `by hand: the ring is on play.library.folders` | — | — | — | LIB-06: A on Library folders opens the pad-reachable folder list (ADR-0264 Decision 8). (manual: needs control play.library.folders, which mesen-gui does not advertise) | all | major | manual |
| `lib.folders-no-native-dialog` | under-test | `by hand: the screen is play.library.folders` | — | — | — | LIB-06: no native OS dialog is open over the folder list. (manual: needs control play.library.folders, which mesen-gui does not advertise) | all | major | manual |
| `lib.folders-add-remove` | under-test | `by hand: the screen is play.library.folders` | — | — | — | LIB-06: add a folder, remove it, add it back by pad; the header count updates. (manual: needs control play.library.folders, which mesen-gui does not advertise) | all | major | manual |
| `lib.folders-b-closes` | under-test | `by hand: the screen is play.library.folders` | — | — | — | LIB-06: B closes the folder list back to the library. (manual: needs control play.library.folders, which mesen-gui does not advertise) | all | major | manual |
| `lib.folders-right-browse` | under-test | `by hand: the ring is on play.library.folders` | — | — | — | LIB-07: Right from Library folders reaches Browse a file. (manual: needs control play.library.browse, control play.library.folders, which mesen-gui does not advertise) | all | major | manual |
| `lib.browse-open` | under-test | `by hand: the ring is on play.library.browse` | — | — | — | LIB-07: A on Browse a file opens the folder browser (ADR-0256 Decision 9). (manual: needs control play.library.browse, which mesen-gui does not advertise) | all | major | manual |
| `lib.browse-walk` | under-test | `by hand: the screen is play.library.browse` | — | — | — | LIB-07: Confirm descends, B ascends; the action row leads the list but the ring never lands on it first (first-row guard); Make this my games folder inside a non-empty folder. (manual: needs control play.library.browse, which mesen-gui does not advertise) | all | minor | manual |
| `lib.browse-b-root-dismisses` | under-test | `by hand: the screen is play.library.browse` | — | — | — | LIB-07: B on the root dismisses with no load, back to the library. (manual: needs control play.library.browse, which mesen-gui does not advertise) | all | major | manual |
| `lib.box-art` | under-test | `by hand: the screen is play.library` | — | — | — | LIB-08: box art for known ROMs (lazy, visible tiles only), a generic console-colored cover carrying the title for unknown ones, nothing waits on the network; a visual check, record only if it blocks focus or scrolling. | all | minor | manual |
| `lib.browse-down-grid` | under-test | `by hand: the ring is on play.library.browse` | — | — | — | LIB-09: Down from the header returns to the grid (LIB-09 starts from the grid). (manual: needs control play.library.tile, control play.library.browse, which mesen-gui does not advertise) | all | major | manual |
| `lib.back-to-home` | under-test | `ui.screen == play.library` | `pad.press(button="B", ticks=4)` | `ui.screen == play.home` within 120 ticks | `ui.screen(is="play.home")` | LIB-09: B from the grid goes back to Home. | all | major | automated |
| `lib.back-ring-on-opener` | under-test | `ui.screen == play.home` | — | `ui.focused == play.home.open-rom` within 8 ticks | `ui.focused(is="play.home.open-rom")` | LIB-09: the ring is on the control that opened the sheet, Open a ROM. | all | major | automated |

## Batch `favorites`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `fav.reach-library` | setup | `fixture rom and fixture settings.profiles.history-no-favorite` | — | — | — | The library sheet is open with a tile focused (the FAV-01 precondition). (manual: needs action nav.goal, which mesen-gui does not advertise) | all | major | manual |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `fav.x-favorites-tile` | under-test | `a tile focused in the library` | — | — | — | FAV-01: X on the focused tile keeps the ring on the grid and toggles Favorite (ADR-0268). (manual: needs control play.library.tile, which mesen-gui does not advertise) | all | major | manual |
| `fav.footer-reads-unfavorite` | under-test | `by hand: the screen is play.library` | — | — | — | FAV-01: the footer's X entry now reads Unfavorite (it read Favorite before). | all | minor | manual |
| `fav.back-to-home` | under-test | `by hand: the screen is play.library` | — | — | — | FAV-01: B returns to Home. (manual: batch setup fav.reach-library is manual, so nothing established the state this step asserts) | all | major | manual |
| `fav.home-shelf-shown` | under-test | `by hand: the screen is play.home` | — | — | — | FAV-01: Home shows a Favorites shelf between Continue and Recent, newest favorite first. (manual: needs control play.home.favorites, which mesen-gui does not advertise) | all | major | manual |
| `fav.library-again` | under-test | `by hand: the ring is on play.home.open-rom` | — | — | — | FAV-01: A on Open a ROM returns to the library. (manual: batch setup fav.reach-library is manual, so nothing established the state this step asserts) | all | major | manual |
| `fav.x-unfavorites-tile` | under-test | `a tile focused in the library` | — | — | — | FAV-01: X on the same tile unfavorites it; the ring stays on the grid. (manual: needs control play.library.tile, which mesen-gui does not advertise) | all | major | manual |
| `fav.back-home-again` | under-test | `by hand: the screen is play.library` | — | — | — | FAV-01: B returns to Home. (manual: batch setup fav.reach-library is manual, so nothing established the state this step asserts) | all | major | manual |
| `fav.shelf-hides-when-empty` | under-test | `by hand: the screen is play.home` | — | — | — | FAV-01: with no favorite left the Favorites shelf is hidden. | all | minor | manual |

## Batch `favorites-home`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `fav2.reach-favorite-tile` | setup | `fixture rom, fixture rom-second and fixture settings.profiles.history-two-favorites` | — | — | — | Home W-P2 with a Favorites shelf and the ring on a favorite tile (the FAV-02 precondition). (manual: needs action nav.goal, control play.home.favorite, which mesen-gui does not advertise) | all | major | manual |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `fav2.x-on-favorite-tile` | under-test | `by hand: the ring is on play.home.favorite` | — | — | — | FAV-02: X acts on the focused cover; on a Home favorite tile it toggles that game, the ring stays on the shelf and the Favorites shelf stays visible (two favorites, so the shelf survives the toggle; ADR-0268 Decision 4). (manual: needs control play.home.favorites, control play.home.favorite, which mesen-gui does not advertise) | all | major | manual |
| `fav2.focus-stays-on-favorite-tile` | under-test | `by hand: play.home.favorites is visible` | — | — | — | FAV-02: after X the ring is still on a Home favorite tile (the shelf remains because a second favorite is left). (manual: needs control play.home.favorite, control play.home.favorites, which mesen-gui does not advertise) | all | major | manual |
| `fav2.up-to-continue` | under-test | `by hand: the ring is on play.home.favorite` | — | — | — | FAV-02: Up reaches the Continue card (focus order Continue -> Favorites -> Recent, ADR-0268 Decision 6). (manual: needs control play.home.favorite, which mesen-gui does not advertise) | all | major | manual |
| `fav2.x-on-continue` | under-test | `by hand: the ring is on play.home.continue` | — | — | — | FAV-02: X on the Continue card favorites the Continue game (ADR-0268 Decision 1); the ring stays on Continue. (manual: batch setup fav2.reach-favorite-tile is manual, so nothing established the state this step asserts) | all | major | manual |
| `fav2.down-favorite` | under-test | `by hand: the ring is on play.home.continue` | — | — | — | FAV-02: Down returns to the Favorites shelf. (manual: needs control play.home.favorite, which mesen-gui does not advertise) | all | major | manual |
| `fav2.down-recent` | under-test | `by hand: the ring is on play.home.favorite` | — | — | — | FAV-02: Down reaches the Recent grid. (manual: needs control play.home.recent, control play.home.favorite, which mesen-gui does not advertise) | all | major | manual |
| `fav2.down-open-rom` | under-test | `by hand: the ring is on play.home.recent` | — | — | — | FAV-02: Down reaches Open a ROM. (manual: needs control play.home.recent, which mesen-gui does not advertise) | all | major | manual |
| `fav2.x-elsewhere-does-nothing` | under-test | `by hand: the ring is on play.home.open-rom` | — | — | — | FAV-02: X on Open a ROM does nothing; the ring stays put. (manual: batch setup fav2.reach-favorite-tile is manual, so nothing established the state this step asserts) | all | major | manual |

## Batch `game`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `game.reach-library` | setup | `fixture rom and fixture settings.profiles.fresh` | — | — | — | The library sheet is open with a tile focused (the GAME-01 precondition). (manual: needs action nav.goal, which mesen-gui does not advertise) | all | major | manual |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `game.a-loads-game` | under-test | `a tile focused in the library` | — | — | — | GAME-01: A on a tile closes the sheet and the game runs. (manual: needs control play.game, control play.library.tile, which mesen-gui does not advertise) | all | major | manual |
| `game.load-card-and-toast` | under-test | `by hand: the screen is play.game` | — | — | — | GAME-01: a load card with a moving indicator shows; for the first three starts the entry toast ends with the menu hint naming the pad's chord, e.g. "· Select+Start for the menu" (ADR-0251 §2), not "Esc". (manual: needs control play.game, which mesen-gui does not advertise) | all | minor | manual |
| `game.pad-is-the-consoles` | under-test | `by hand: the screen is play.game` | — | — | — | GAME-02: playing 10 s with the D-pad leaves the game running; the pad is the console's (ADR-0256 Decision 1). (manual: needs control play.game, which mesen-gui does not advertise) | all | major | manual |
| `game.buttons-are-the-consoles` | under-test | `by hand: the screen is play.game` | — | — | — | GAME-02: the face buttons reach the console too; pressing A and B leaves the game running and nothing in the GUI reacts. (manual: needs control play.game, which mesen-gui does not advertise) | all | major | manual |
| `game.no-menu-feedback` | under-test | `by hand: the screen is play.game` | — | — | — | GAME-02: no menu sound, no focus ring, no haptic tick, nothing in the GUI reacts; the status bar and lamps are hidden (ADR-0261 Consequences). (manual: needs control play.game, which mesen-gui does not advertise) | all | minor | manual |

## Batch `game-archive`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `game-archive.reach-library` | setup | `fixture rom-zip (a library holding only that file) and fixture settings.profiles.fresh` | — | — | — | The library sheet is open with the multi-ROM archive tile focused (fixture rom-zip). (manual: needs action nav.goal, control play.library.tile, which mesen-gui does not advertise) | all | major | manual |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `game-archive.a-opens-select-sheet` | under-test | `a tile focused in the library (fixture rom-zip)` | — | — | — | GAME-03: A on the archive tile opens the "which game" sheet (PlaySelectRomSheet). (manual: needs control play.select-rom, control play.library.tile, which mesen-gui does not advertise) | all | major | manual |
| `game-archive.b-cancels-back` | under-test | `by hand: the screen is play.select-rom` | — | — | — | GAME-03: B cancels back to the library. (manual: needs control play.select-rom, which mesen-gui does not advertise) | all | major | manual |
| `game-archive.a-reopens` | under-test | `a tile focused in the library` | — | — | — | GAME-03: A on the archive tile opens the sheet again. (manual: needs control play.select-rom, control play.library.tile, which mesen-gui does not advertise) | all | major | manual |
| `game-archive.a-picks-a-game` | under-test | `by hand: the screen is play.select-rom` | — | — | — | GAME-03: D-pad + A picks a game and it loads. (manual: needs control play.game, control play.select-rom, which mesen-gui does not advertise) | all | major | manual |

## Batch `game-bios`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `game-bios.reach-library` | setup | `fixture rom-fds (a library holding only that file) and fixture settings.profiles.fresh` | — | — | — | The library sheet is open with the FDS tile focused (fixture rom-fds, disksys.rom not installed). (manual: needs action nav.goal, control play.library.tile, which mesen-gui does not advertise) | all | major | manual |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `game-bios.a-opens-bios-sheet` | under-test | `a tile focused in the library (fixture rom-fds)` | — | — | — | GAME-04: A on the FDS image shows the BIOS sheet (W-P13). (manual: needs control play.bios, control play.library.tile, which mesen-gui does not advertise) | all | major | manual |
| `game-bios.choose-file-native` | under-test | `by hand: the screen is play.bios` | — | — | — | KNOWN GAP: GAME-04: Choose File... on the BIOS sheet is a native dialog (ADR-0256 Decision 9 refusals). Expected FAIL; record that Cancel still backs out. (manual: needs control play.bios, which mesen-gui does not advertise) | all | major | manual |
| `game-bios.b-cancels` | under-test | `by hand: the screen is play.bios` | — | — | — | GAME-04: B/Cancel backs out cleanly to the library. (manual: needs control play.bios, which mesen-gui does not advertise) | all | major | manual |
| `game-bios.status-names-bios` | under-test | `by hand: the screen is play.library` | — | — | — | GAME-04: the status line names the missing BIOS. | all | minor | manual |

## Batch `dialogs`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `dialogs.reach-home` | setup | `fixture settings.profiles.fresh` | — | — | — | Home W-P2 is on screen, the precondition of the dialogs sweep. (manual: needs action nav.goal, which mesen-gui does not advertise) | all | major | manual |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `dialogs.no-trap` | under-test | `by hand: the screen is play.home` | — | — | — | Jev walks Home by pad toward screens it has not visited; every dialog it reaches has a pad move that leaves it (no trap), within 60 moves. (manual: needs action nav.sweep, which mesen-gui does not advertise) | all | major | manual |

## Batch `pause`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `pause.reach-game` | setup | `fixture rom, fixture pack and fixture settings.profiles.fresh` | — | — | — | A game is running (the P4-01 precondition). (manual: needs action nav.goal, control play.game, which mesen-gui does not advertise) | all | major | manual |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `pause.chord-opens` | under-test | `by hand: the screen is play.game` | — | — | — | P4-01: the chord opens W-P4 with the game paused behind it. (manual: needs action pad.chord, control play.pause, control play.game, which mesen-gui does not advertise) | all | major | manual |
| `pause.resume-focused` | under-test | `by hand: the screen is play.pause` | — | — | — | P4-01: Resume is the focused control. (manual: needs control play.pause.resume, control play.pause, which mesen-gui does not advertise) | all | major | manual |
| `pause.row-resume` | under-test | `by hand: the screen is play.pause` | — | — | — | P4-01: the control resume is present (seven in all: Resume, Save states, Pack, Enhancements, Cheats, Settings, Quit game). (manual: needs control play.pause.resume, control play.pause, which mesen-gui does not advertise) | all | major | manual |
| `pause.row-save-states` | under-test | `by hand: the screen is play.pause` | — | — | — | P4-01: the control save-states is present (seven in all: Resume, Save states, Pack, Enhancements, Cheats, Settings, Quit game). (manual: needs control play.pause.save-states, control play.pause, which mesen-gui does not advertise) | all | major | manual |
| `pause.row-pack` | under-test | `by hand: the screen is play.pause` | — | — | — | P4-01: the control pack is present (seven in all: Resume, Save states, Pack, Enhancements, Cheats, Settings, Quit game). (manual: needs control play.pause.pack, control play.pause, which mesen-gui does not advertise) | all | major | manual |
| `pause.row-enhancements` | under-test | `by hand: the screen is play.pause` | — | — | — | P4-01: the control enhancements is present (seven in all: Resume, Save states, Pack, Enhancements, Cheats, Settings, Quit game). (manual: needs control play.pause.enhancements, control play.pause, which mesen-gui does not advertise) | all | major | manual |
| `pause.row-cheats` | under-test | `by hand: the screen is play.pause` | — | — | — | P4-01: the control cheats is present (seven in all: Resume, Save states, Pack, Enhancements, Cheats, Settings, Quit game). (manual: needs control play.pause.cheats, control play.pause, which mesen-gui does not advertise) | all | major | manual |
| `pause.row-settings` | under-test | `by hand: the screen is play.pause` | — | — | — | P4-01: the control settings is present (seven in all: Resume, Save states, Pack, Enhancements, Cheats, Settings, Quit game). (manual: needs control play.pause.settings, control play.pause, which mesen-gui does not advertise) | all | major | manual |
| `pause.row-quit-game` | under-test | `by hand: the screen is play.pause` | — | — | — | P4-01: the control quit-game is present (seven in all: Resume, Save states, Pack, Enhancements, Cheats, Settings, Quit game). (manual: needs control play.pause.quit-game, control play.pause, which mesen-gui does not advertise) | all | major | manual |
| `pause.footer-words` | under-test | `by hand: the screen is play.pause` | — | — | — | P4-01: the footer names "B to resume" in words for the pad in hand (ADR-0256 Decision 6: "Circle", never a glyph); the hook does not read footer text. (manual: needs control play.pause, which mesen-gui does not advertise) | all | minor | manual |
| `pause.frozen-frame-bar-lamps` | under-test | `by hand: the screen is play.pause` | — | — | — | P4-01: a frozen frame shows behind the overlay and the status bar and lamps are visible; judged by eye. (manual: needs control play.pause, which mesen-gui does not advertise) | all | minor | manual |
| `pause.chord-again-resumes` | under-test | `by hand: the screen is play.pause` | — | — | — | P4-02: the chord again closes W-P4 and resumes the game (ADR-0241 Esc order: game, W-P4, resume). (manual: needs action pad.chord, control play.game, control play.pause, which mesen-gui does not advertise) | all | major | manual |
| `pause.reopen-for-b` | under-test | `by hand: the screen is play.game` | — | — | — | P4-02: the chord reopens W-P4 (setup for the B half). (manual: needs action pad.chord, control play.pause, control play.game, which mesen-gui does not advertise) | all | major | manual |
| `pause.b-resumes` | under-test | `by hand: the screen is play.pause` | — | — | — | P4-02: B closes W-P4 and resumes the game. (manual: needs control play.game, control play.pause, which mesen-gui does not advertise) | all | major | manual |
| `pause.reopen-for-rows` | under-test | `by hand: the screen is play.game` | — | — | — | P4-03: the chord reopens W-P4 with Resume focused (P4-01 precondition). (manual: needs action pad.chord, control play.pause, control play.game, which mesen-gui does not advertise) | all | major | manual |
| `pause.down-to-save-states` | under-test | `by hand: the ring is on play.pause.resume` | — | — | — | P4-03: D-pad Down from resume reaches save-states; the ring is on it. (manual: needs control play.pause.save-states, control play.pause.resume, which mesen-gui does not advertise) | all | major | manual |
| `pause.down-to-pack` | under-test | `by hand: the ring is on play.pause.save-states` | — | — | — | P4-03: D-pad Down from save-states reaches pack; the ring is on it. (manual: needs control play.pause.pack, control play.pause.save-states, which mesen-gui does not advertise) | all | major | manual |
| `pause.down-to-enhancements` | under-test | `by hand: the ring is on play.pause.pack` | — | — | — | P4-03: D-pad Down from pack reaches enhancements; the ring is on it. (manual: needs control play.pause.enhancements, control play.pause.pack, which mesen-gui does not advertise) | all | major | manual |
| `pause.down-to-cheats` | under-test | `by hand: the ring is on play.pause.enhancements` | — | — | — | P4-03: D-pad Down from enhancements reaches cheats; the ring is on it. (manual: needs control play.pause.cheats, control play.pause.enhancements, which mesen-gui does not advertise) | all | major | manual |
| `pause.down-to-settings` | under-test | `by hand: the ring is on play.pause.cheats` | — | — | — | P4-03: D-pad Down from cheats reaches settings; the ring is on it. (manual: needs control play.pause.settings, control play.pause.cheats, which mesen-gui does not advertise) | all | major | manual |
| `pause.down-to-quit-game` | under-test | `by hand: the ring is on play.pause.settings` | — | — | — | P4-03: D-pad Down from settings reaches quit-game; the ring is on it. (manual: needs control play.pause.quit-game, control play.pause.settings, which mesen-gui does not advertise) | all | major | manual |
| `pause.down-no-wrap` | under-test | `by hand: the ring is on play.pause.quit-game` | — | — | — | P4-03: Down on the last row does not wrap and is not a dead end that moves the ring away. (manual: needs control play.pause.quit-game, which mesen-gui does not advertise) | all | major | manual |
| `pause.left-contained` | under-test | `by hand: the ring is on play.pause.quit-game` | — | — | — | P4-03: Left keeps focus on the last row, no escape from W-P4. (manual: needs control play.pause.quit-game, which mesen-gui does not advertise) | all | major | manual |
| `pause.right-contained` | under-test | `by hand: the ring is on play.pause.quit-game` | — | — | — | P4-03: Right keeps focus on the last row, no escape from W-P4. (manual: needs control play.pause.quit-game, which mesen-gui does not advertise) | all | major | manual |
| `pause.up-to-settings` | under-test | `by hand: the ring is on play.pause.quit-game` | — | — | — | P4-03: D-pad Up from quit-game reaches settings. (manual: needs control play.pause.settings, control play.pause.quit-game, which mesen-gui does not advertise) | all | major | manual |
| `pause.up-to-cheats` | under-test | `by hand: the ring is on play.pause.settings` | — | — | — | P4-03: D-pad Up from settings reaches cheats. (manual: needs control play.pause.cheats, control play.pause.settings, which mesen-gui does not advertise) | all | major | manual |
| `pause.up-to-enhancements` | under-test | `by hand: the ring is on play.pause.cheats` | — | — | — | P4-03: D-pad Up from cheats reaches enhancements. (manual: needs control play.pause.enhancements, control play.pause.cheats, which mesen-gui does not advertise) | all | major | manual |
| `pause.up-to-pack` | under-test | `by hand: the ring is on play.pause.enhancements` | — | — | — | P4-03: D-pad Up from enhancements reaches pack. (manual: needs control play.pause.pack, control play.pause.enhancements, which mesen-gui does not advertise) | all | major | manual |
| `pause.up-to-save-states` | under-test | `by hand: the ring is on play.pause.pack` | — | — | — | P4-03: D-pad Up from pack reaches save-states. (manual: needs control play.pause.save-states, control play.pause.pack, which mesen-gui does not advertise) | all | major | manual |
| `pause.up-to-resume` | under-test | `by hand: the ring is on play.pause.save-states` | — | — | — | P4-03: D-pad Up from save-states reaches resume. (manual: needs control play.pause.resume, control play.pause.save-states, which mesen-gui does not advertise) | all | major | manual |
| `pause.up-no-wrap-header` | under-test | `by hand: the ring is on play.pause.resume` | — | — | — | P4-03: Up on Resume stays on Resume: no wrap into the header. (manual: needs control play.pause.resume, which mesen-gui does not advertise) | all | major | manual |
| `pause.ring-visible-each-row` | under-test | `by hand: the screen is play.pause` | — | — | — | P4-03: the ring is visible on every row through the walk; judged by eye. (manual: needs control play.pause, which mesen-gui does not advertise) | all | major | manual |
| `pause.footer-follows-pad` | under-test | `by hand: the screen is play.pause` | — | — | — | P4-04 (pass 3): switching to the other pad mid-overlay and pressing any direction makes the footer re-read for the pad now in hand (ADR-0256 Decision 6); needs two pads. (manual: needs control play.pause, which mesen-gui does not advertise) | all | major | manual |
| `pause.save-states-focus` | setup | `by hand: the screen is play.pause` | — | — | — | P4-05 (setup): focus Save states. (manual: needs action nav.goal, control play.pause.save-states, control play.pause, which mesen-gui does not advertise) | all | major | manual |
| `pause.save-states-opens` | under-test | `by hand: the ring is on play.pause.save-states` | — | — | — | P4-05: A on Save states opens the Save states sheet. (manual: needs control play.save-states, control play.pause.save-states, which mesen-gui does not advertise) | all | major | manual |
| `pause.save-states-slot-roundtrip` | under-test | `by hand: the screen is play.save-states` | — | — | — | P4-05: walk the slot grid, save to a slot, load it, all with D-pad + A; Shared replays... (if shown) opens and B backs out. (manual: needs control play.save-states, which mesen-gui does not advertise) | all | major | manual |
| `pause.save-states-b-to-pause` | under-test | `by hand: the screen is play.save-states` | — | — | — | P4-05: B closes back to W-P4, not to the game. (manual: needs control play.pause, control play.save-states, which mesen-gui does not advertise) | all | major | manual |
| `pause.pack-focus` | setup | `by hand: the screen is play.pause` | — | — | — | P4-06 (setup): focus Pack. (manual: needs action nav.goal, control play.pause.pack, control play.pause, which mesen-gui does not advertise) | all | major | manual |
| `pause.pack-opens` | under-test | `the pack row focused in the pause menu (fixture pack)` | — | — | — | P4-06: with one pack, A on Pack opens the pack detail (W-P6). (manual: needs control play.pack, control play.pause.pack, which mesen-gui does not advertise) | all | major | manual |
| `pause.pack-detail-controls` | under-test | `by hand: play.pack is visible` | — | — | — | P4-06: with one pack the detail (W-P6) shows Done, Show pack folder, Details. (manual: needs control play.pack, which mesen-gui does not advertise) | all | minor | manual |
| `pause.pack-show-folder-gap` | under-test | `by hand: play.pack is visible` | — | — | — | KNOWN GAP (P4-06): Show pack folder is expected FAIL; Finder takes the foreground and the pad cannot bring MesenAI back. PASS only if the pad still drives Play afterwards with no keyboard or mouse. The hook cannot observe the OS foreground. (manual: needs control play.pack, which mesen-gui does not advertise) | all | major | manual |
| `pause.pack-b-to-pause` | under-test | `by hand: play.pack is visible` | — | — | — | P4-06: B closes back to W-P4; focus returns to play.pause.pack. (manual: needs control play.pause.pack, control play.pack, which mesen-gui does not advertise) | all | major | manual |
| `pause.enh-focus` | setup | `by hand: the screen is play.pause` | — | — | — | P4-07 (setup): focus Enhancements. (manual: needs action nav.goal, control play.pause.enhancements, control play.pause, which mesen-gui does not advertise) | all | major | manual |
| `pause.enh-opens` | under-test | `by hand: the ring is on play.pause.enhancements` | — | — | — | P4-07: A on Enhancements opens the sheet. (manual: needs control play.enhancements, control play.pause.enhancements, which mesen-gui does not advertise) | all | major | manual |
| `pause.enh-toggle-apply` | under-test | `by hand: the screen is play.enhancements` | — | — | — | P4-07: a switch toggles by A; the footer label (Done/Apply/Apply & Reload) follows the draft; applying with a reload returns to the game or W-P4 with no mouse. (manual: needs control play.enhancements, which mesen-gui does not advertise) | all | major | manual |
| `pause.enh-b-discards` | under-test | `by hand: the screen is play.enhancements` | — | — | — | P4-07: B discards back to W-P4. (manual: needs control play.pause, control play.enhancements, which mesen-gui does not advertise) | all | major | manual |
| `pause.cheats-focus` | setup | `by hand: the screen is play.pause` | — | — | — | P4-08 (setup): focus Cheats. (manual: needs action nav.goal, control play.pause.cheats, control play.pause, which mesen-gui does not advertise) | all | major | manual |
| `pause.cheats-opens` | under-test | `by hand: the ring is on play.pause.cheats` | — | — | — | P4-08: A on Cheats opens the sheet. (manual: needs control play.cheats, control play.pause.cheats, which mesen-gui does not advertise) | all | major | manual |
| `pause.cheats-add-focus` | setup | `by hand: the screen is play.cheats` | — | — | — | P4-08 (setup): focus Add a Code. (manual: needs action nav.goal, control play.cheats.add-code, control play.cheats, which mesen-gui does not advertise) | all | major | manual |
| `pause.cheats-add-keyboard` | under-test | `by hand: the ring is on play.cheats.add-code` | — | — | — | P4-08: A on Add a Code opens the on-screen keyboard (code-shaped field, ADR-0262 Decision 2). (manual: needs control play.keyboard, control play.cheats.add-code, which mesen-gui does not advertise) | all | major | manual |
| `pause.cheats-code-keyboard-letters` | under-test | `by hand: play.keyboard is visible` | — | — | — | P4-08: the code keyboard shows APZLGITYEOXUKSVN (Game Genie letters) first; the description field is free text; commit works. (manual: needs control play.keyboard, which mesen-gui does not advertise) | all | minor | manual |
| `pause.cheats-field-b-cancels` | under-test | `by hand: play.keyboard is visible` | — | — | — | P4-08: B cancels a field without closing the sheet; focus returns to play.cheats.add-code. (manual: needs control play.cheats.add-code, control play.keyboard, which mesen-gui does not advertise) | all | major | manual |
| `pause.cheats-api-key-mask` | under-test | `by hand: the screen is play.cheats` | — | — | — | P4-08: the API key field (if visible) masks its draft as the bullet character. (manual: needs control play.cheats, which mesen-gui does not advertise) | all | minor | manual |
| `pause.cheats-b-to-pause` | under-test | `by hand: the screen is play.cheats` | — | — | — | P4-08: B closes the sheet back to W-P4. (manual: needs control play.pause, control play.cheats, which mesen-gui does not advertise) | all | major | manual |
| `pause.quit-focus` | setup | `by hand: the screen is play.pause` | — | — | — | P4-09 (setup): focus Quit game. (manual: needs action nav.goal, control play.pause.quit-game, control play.pause, which mesen-gui does not advertise) | all | major | manual |
| `pause.quit-confirm-in-place` | under-test | `by hand: the ring is on play.pause.quit-game` | — | — | — | P4-09: A on Quit game shows the confirm in place on W-P4, reachable. (manual: needs control play.pause.quit-confirm, control play.pause.quit-game, which mesen-gui does not advertise) | all | major | manual |
| `pause.quit-b-keeps-playing` | under-test | `by hand: play.pause.quit-confirm is visible` | — | — | — | P4-09: B on the confirm dismisses it and stays on W-P4 (keep playing); focus returns to play.pause.quit-game. (manual: needs control play.pause.quit-game, control play.pause.quit-confirm, which mesen-gui does not advertise) | all | major | manual |
| `pause.quit-reconfirm` | under-test | `by hand: the ring is on play.pause.quit-game` | — | — | — | P4-09: A again shows the confirm. (manual: needs control play.pause.quit-confirm, control play.pause.quit-game, which mesen-gui does not advertise) | all | major | manual |
| `pause.quit-confirm-lands-home` | under-test | `by hand: play.pause.quit-confirm is visible` | — | — | — | P4-09: confirming powers the game off and lands on Home. (manual: needs control play.pause.quit-confirm, which mesen-gui does not advertise) | all | major | manual |
| `pause.quit-home-ring` | under-test | `by hand: the screen is play.home` | — | — | — | P4-09: the ring is placed on Home's first control. (manual: batch setup pause.reach-game is manual, so nothing established the state this step asserts) | all | major | manual |

## Batch `settings`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `settings.reach-pause` | setup | `fixture rom and fixture settings.profiles.fresh` | — | — | — | W-P4 is open with Settings focused (the SET-01 precondition, from P4-01). (manual: needs action nav.goal, control play.pause.settings, which mesen-gui does not advertise) | all | major | manual |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `settings.a-opens` | under-test | `by hand: the ring is on play.pause.settings` | — | — | — | SET-01: A on Settings opens W-P8. (manual: needs control play.settings, control play.pause.settings, which mesen-gui does not advertise) | all | major | manual |
| `settings.tab-display-first` | under-test | `by hand: the screen is play.settings` | — | — | — | SET-01: the sheet opens on the Display tab. (manual: needs control play.settings.tab-display, control play.settings, which mesen-gui does not advertise) | all | major | manual |
| `settings.display-down-into-page` | under-test | `by hand: the ring is on play.settings.tab-display` | — | — | — | SET-01: Down from the display tab enters its page. (manual: needs control play.settings.page, control play.settings.tab-display, which mesen-gui does not advertise) | all | major | manual |
| `settings.display-done-reachable` | under-test | `by hand: the screen is play.settings` | — | — | — | SET-01: Done is reachable on the display page. Premise: held Down repeats at 400 ms then every 100 ms (~17 moves in 40 ticks), the Settings vertical chain does not wrap, and Done is its last row. (manual: needs control play.settings.done, control play.settings, which mesen-gui does not advertise) | all | major | manual |
| `settings.display-up-page` | under-test | `by hand: the ring is on play.settings.done` | — | — | — | SET-01: Up from Done on display goes to the page (no More in Options on this tab). (manual: needs control play.settings.page, control play.settings.done, which mesen-gui does not advertise) | all | major | manual |
| `settings.display-back-to-tab` | setup | `by hand: the screen is play.settings` | — | — | — | SET-01 (setup): back to the display tab for the next step. (manual: needs action nav.goal, control play.settings.tab-display, control play.settings, which mesen-gui does not advertise) | all | major | manual |
| `settings.tab-right-look` | under-test | `by hand: the ring is on play.settings.tab-display` | — | — | — | SET-01: Right on the tab strip reaches look. (manual: needs control play.settings.tab-look, control play.settings.tab-display, which mesen-gui does not advertise) | all | major | manual |
| `settings.tab-left-display` | under-test | `by hand: the ring is on play.settings.tab-look` | — | — | — | SET-01: Left on the tab strip goes back to display. (manual: needs control play.settings.tab-display, control play.settings.tab-look, which mesen-gui does not advertise) | all | major | manual |
| `settings.tab-right-look-again` | setup | `by hand: the ring is on play.settings.tab-display` | — | — | — | SET-01 (setup): back to the look tab for the next step. (manual: needs control play.settings.tab-look, control play.settings.tab-display, which mesen-gui does not advertise) | all | major | manual |
| `settings.look-down-into-page` | under-test | `by hand: the ring is on play.settings.tab-look` | — | — | — | SET-01: Down from the look tab enters its page. (manual: needs control play.settings.page, control play.settings.tab-look, which mesen-gui does not advertise) | all | major | manual |
| `settings.look-done-reachable` | under-test | `by hand: the screen is play.settings` | — | — | — | SET-01: Done is reachable on the look page. Premise: held Down repeats at 400 ms then every 100 ms (~17 moves in 40 ticks), the Settings vertical chain does not wrap, and Done is its last row. (manual: needs control play.settings.done, control play.settings, which mesen-gui does not advertise) | all | major | manual |
| `settings.look-up-page` | under-test | `by hand: the ring is on play.settings.done` | — | — | — | SET-01: Up from Done on look goes to the page (no More in Options on this tab). (manual: needs control play.settings.page, control play.settings.done, which mesen-gui does not advertise) | all | major | manual |
| `settings.look-back-to-tab` | setup | `by hand: the screen is play.settings` | — | — | — | SET-01 (setup): back to the look tab for the next step. (manual: needs action nav.goal, control play.settings.tab-look, control play.settings, which mesen-gui does not advertise) | all | major | manual |
| `settings.tab-right-audio` | under-test | `by hand: the ring is on play.settings.tab-look` | — | — | — | SET-01: Right on the tab strip reaches audio. (manual: needs control play.settings.tab-audio, control play.settings.tab-look, which mesen-gui does not advertise) | all | major | manual |
| `settings.audio-down-into-page` | under-test | `by hand: the ring is on play.settings.tab-audio` | — | — | — | SET-01: Down from the audio tab enters its page. (manual: needs control play.settings.page, control play.settings.tab-audio, which mesen-gui does not advertise) | all | major | manual |
| `settings.audio-done-reachable` | under-test | `by hand: the screen is play.settings` | — | — | — | SET-01: Done is reachable on the audio page. Premise: held Down repeats at 400 ms then every 100 ms (~17 moves in 40 ticks), the Settings vertical chain does not wrap, and Done is its last row. (manual: needs control play.settings.done, control play.settings, which mesen-gui does not advertise) | all | major | manual |
| `settings.audio-up-more` | under-test | `by hand: the ring is on play.settings.done` | — | — | — | SET-01: Up from Done on audio reaches More in Options. (manual: needs control play.settings.more-in-options, control play.settings.done, which mesen-gui does not advertise) | all | major | manual |
| `settings.audio-up-page` | under-test | `by hand: the ring is on play.settings.more-in-options` | — | — | — | SET-01: Up again from More in Options reaches the audio page. (manual: needs control play.settings.page, control play.settings.more-in-options, which mesen-gui does not advertise) | all | major | manual |
| `settings.audio-back-to-tab` | setup | `by hand: the screen is play.settings` | — | — | — | SET-01 (setup): back to the audio tab for the next step. (manual: needs action nav.goal, control play.settings.tab-audio, control play.settings, which mesen-gui does not advertise) | all | major | manual |
| `settings.tab-right-controls` | under-test | `by hand: the ring is on play.settings.tab-audio` | — | — | — | SET-01: Right on the tab strip reaches controls. (manual: needs control play.settings.tab-controls, control play.settings.tab-audio, which mesen-gui does not advertise) | all | major | manual |
| `settings.controls-down-into-page` | under-test | `by hand: the ring is on play.settings.tab-controls` | — | — | — | SET-01: Down from the controls tab enters its page. (manual: needs control play.settings.page, control play.settings.tab-controls, which mesen-gui does not advertise) | all | major | manual |
| `settings.controls-done-reachable` | under-test | `by hand: the screen is play.settings` | — | — | — | SET-01: Done is reachable on the controls page. Premise: held Down repeats at 400 ms then every 100 ms (~17 moves in 40 ticks), the Settings vertical chain does not wrap, and Done is its last row. (manual: needs control play.settings.done, control play.settings, which mesen-gui does not advertise) | all | major | manual |
| `settings.controls-up-more` | under-test | `by hand: the ring is on play.settings.done` | — | — | — | SET-01: Up from Done on controls reaches More in Options. (manual: needs control play.settings.more-in-options, control play.settings.done, which mesen-gui does not advertise) | all | major | manual |
| `settings.controls-up-page` | under-test | `by hand: the ring is on play.settings.more-in-options` | — | — | — | SET-01: Up again from More in Options reaches the controls page. (manual: needs control play.settings.page, control play.settings.more-in-options, which mesen-gui does not advertise) | all | major | manual |
| `settings.controls-back-to-tab` | setup | `by hand: the screen is play.settings` | — | — | — | SET-01 (setup): back to the controls tab for the next step. (manual: needs action nav.goal, control play.settings.tab-controls, control play.settings, which mesen-gui does not advertise) | all | major | manual |
| `settings.tab-right-system` | under-test | `by hand: the ring is on play.settings.tab-controls` | — | — | — | SET-01: Right on the tab strip reaches system. (manual: needs control play.settings.tab-system, control play.settings.tab-controls, which mesen-gui does not advertise) | all | major | manual |
| `settings.system-down-into-page` | under-test | `by hand: the ring is on play.settings.tab-system` | — | — | — | SET-01: Down from the system tab enters its page. (manual: needs control play.settings.page, control play.settings.tab-system, which mesen-gui does not advertise) | all | major | manual |
| `settings.system-done-reachable` | under-test | `by hand: the screen is play.settings` | — | — | — | SET-01: Done is reachable on the system page. Premise: held Down repeats at 400 ms then every 100 ms (~17 moves in 40 ticks), the Settings vertical chain does not wrap, and Done is its last row. (manual: needs control play.settings.done, control play.settings, which mesen-gui does not advertise) | all | major | manual |
| `settings.system-up-page` | under-test | `by hand: the ring is on play.settings.done` | — | — | — | SET-01: Up from Done on system goes to the page (no More in Options on this tab). (manual: needs control play.settings.page, control play.settings.done, which mesen-gui does not advertise) | all | major | manual |
| `settings.system-back-to-tab` | setup | `by hand: the screen is play.settings` | — | — | — | SET-01 (setup): back to the system tab for the next step. (manual: needs action nav.goal, control play.settings.tab-system, control play.settings, which mesen-gui does not advertise) | all | major | manual |
| `settings.exit-fullscreen-done-focus` | setup | `by hand: the screen is play.settings` | — | — | — | SET-01 (setup): focus Done. (manual: needs action nav.goal, control play.settings.done, control play.settings, which mesen-gui does not advertise) | all | major | manual |
| `settings.exit-fullscreen-left-of-done` | under-test | `by hand: the ring is on play.settings.done` | — | — | — | SET-01: Exit full screen is Left of Done, not above it (FS-02). (manual: needs variant window.mode=fullscreen, control play.settings.exit-fullscreen, control play.settings.done, which mesen-gui does not advertise) | all | major | manual |
| `settings.size-focus` | setup | `by hand: the screen is play.settings` | — | — | — | SET-02 (setup): focus Interface size on the Display tab. (manual: needs action nav.goal, control play.settings.interface-size, control play.settings, which mesen-gui does not advertise) | all | major | manual |
| `settings.size-right-large` | under-test | `by hand: the ring is on play.settings.interface-size` | — | — | — | SET-02: Right steps the value in place to Large; the focus stays on the row. (manual: needs control play.settings.interface-size, which mesen-gui does not advertise) | all | major | manual |
| `settings.size-right-xlarge` | under-test | `by hand: the ring is on play.settings.interface-size` | — | — | — | SET-02: Right steps the value in place to Extra large; the focus stays on the row. (manual: needs control play.settings.interface-size, which mesen-gui does not advertise) | all | major | manual |
| `settings.size-xlarge-done` | under-test | `by hand: the ring is on play.settings.interface-size` | — | — | — | SET-02: at Extra large Done stays pinned and reachable (ADR-0269 Decision 6). Premise: held Down repeats at 400 ms then every 100 ms (~17 moves in 40 ticks, so this step holds 60 ticks for the taller Extra large chain), the Settings vertical chain does not wrap, and Done is its last row. (manual: needs control play.settings.done, control play.settings.interface-size, which mesen-gui does not advertise) | all | major | manual |
| `settings.size-back-to-row` | setup | `by hand: the ring is on play.settings.done` | — | — | — | SET-02 (setup): back to Interface size. (manual: needs action nav.goal, control play.settings.interface-size, control play.settings.done, which mesen-gui does not advertise) | all | major | manual |
| `settings.size-left-standard` | under-test | `by hand: the ring is on play.settings.interface-size` | — | — | — | SET-02: Left steps the value back toward Standard in place. (manual: needs control play.settings.interface-size, which mesen-gui does not advertise) | all | major | manual |
| `settings.size-left-standard-2` | under-test | `by hand: the ring is on play.settings.interface-size` | — | — | — | SET-02: the second Left reaches Standard; the focus stays on the row. (manual: needs control play.settings.interface-size, which mesen-gui does not advertise) | all | major | manual |
| `settings.size-standard-done` | under-test | `by hand: the ring is on play.settings.interface-size` | — | — | — | SET-02: at Standard Done is reachable. Premise: held Down repeats at 400 ms then every 100 ms (~17 moves in 40 ticks), the Settings vertical chain does not wrap, and Done is its last row. (manual: needs control play.settings.done, control play.settings.interface-size, which mesen-gui does not advertise) | all | major | manual |
| `settings.size-applies-scrolls` | under-test | `by hand: the screen is play.settings` | — | — | — | SET-02: each value applies at once and only Play chrome scales, never the game picture; at Extra large the rows scroll with nothing clipped (guaranteed at 1024x640 and 512x505); record legibility at couch distance per size in the rubric. The hook exposes no values or geometry. (manual: needs control play.settings, which mesen-gui does not advertise) | all | major | manual |
| `settings.menu-sounds-row` | under-test | `by hand: the screen is play.settings` | — | — | — | SET-03: Menu sounds is visible when the host audio path exists (ADR-0270, #1126) and hidden otherwise; record which. (manual: needs control play.settings, which mesen-gui does not advertise) | all | minor | manual |
| `settings.rumble-focus` | setup | `by hand: the screen is play.settings` | — | — | — | SET-04 (setup): focus the Rumble slider on the Controls tab. (manual: needs action nav.goal, control play.settings.rumble, control play.settings, which mesen-gui does not advertise) | all | major | manual |
| `settings.rumble-left` | under-test | `by hand: the ring is on play.settings.rumble` | — | — | — | SET-04: Left steps Rumble toward 0 in place. (manual: needs control play.settings.rumble, which mesen-gui does not advertise) | all | major | manual |
| `settings.rumble-right` | under-test | `by hand: the ring is on play.settings.rumble` | — | — | — | SET-04: Right steps Rumble back up in place. (manual: needs control play.settings.rumble, which mesen-gui does not advertise) | all | major | manual |
| `settings.menu-tick` | under-test | `by hand: the ring is on play.settings.rumble` | — | — | — | SET-04: Menu tick shows only when the core says the pad is aimable (MenuTickAvailable; macOS with a rumble-capable pad); with Rumble 0 the switch is disabled with the reason text; on, a short tick on each focus move, never while a game runs unpaused (HapticTickRule.ShouldTickOnMove). On a pad without rumble mark N/A, not FAIL. (manual: needs control play.settings.rumble, which mesen-gui does not advertise) | all | major | manual |
| `settings.deadzone-focus` | setup | `by hand: the ring is on play.settings.rumble` | — | — | — | SET-05 (setup): focus the Deadzone slider. (manual: needs action nav.goal, control play.settings.deadzone, control play.settings.rumble, which mesen-gui does not advertise) | all | major | manual |
| `settings.deadzone-left` | under-test | `by hand: the ring is on play.settings.deadzone` | — | — | — | SET-05: Left steps the Deadzone slider in place. (manual: needs control play.settings.deadzone, which mesen-gui does not advertise) | all | major | manual |
| `settings.deadzone-right` | under-test | `by hand: the ring is on play.settings.deadzone` | — | — | — | SET-05: Right steps the Deadzone slider in place. (manual: needs control play.settings.deadzone, which mesen-gui does not advertise) | all | major | manual |
| `settings.deadzone-readout-drift` | under-test | `by hand: the ring is on play.settings.deadzone` | — | — | — | SET-05: the value readout follows the slider; stick drift must not move the menu cursor (the bridge reads D-pad names only, see PAD-03). The hook reads no values or stick axes. (manual: needs control play.settings.deadzone, which mesen-gui does not advertise) | all | minor | manual |
| `settings.box-art-focus` | setup | `by hand: the screen is play.settings` | — | — | — | SET-06 (setup): focus Download box art on the System tab. (manual: needs action nav.goal, control play.settings.download-box-art, control play.settings, which mesen-gui does not advertise) | all | major | manual |
| `settings.box-art-change` | under-test | `by hand: the ring is on play.settings.download-box-art` | — | — | — | SET-06: A changes Download box art by pad; the focus stays on the row. (manual: needs control play.settings.download-box-art, which mesen-gui does not advertise) | all | major | manual |
| `settings.box-art-change-back` | under-test | `by hand: the ring is on play.settings.download-box-art` | — | — | — | SET-06: A changes it back; the focus stays on the row. (manual: needs control play.settings.download-box-art, which mesen-gui does not advertise) | all | major | manual |
| `settings.system-no-native-dialog` | under-test | `by hand: the screen is play.settings` | — | — | — | KNOWN GAP: SET-06: no native OS dialog opens on the System tab. A native panel is not an Avalonia control, so the hook's dialogs list (app sheets, ADR-0272 §3) cannot see it; verify by eye or with the vision fallback. (manual: needs control play.settings, which mesen-gui does not advertise) | all | major | manual |
| `settings.system-rows-restart` | under-test | `by hand: the screen is play.settings` | — | — | — | SET-06: every System row (storage, keyboard preset, ...) is a pad-drivable control; a storage change offers a restart rather than pretending (ADR-0256 Decision 8). (manual: needs control play.settings, which mesen-gui does not advertise) | all | major | manual |
| `settings.b-closes-to-pause` | under-test | `by hand: the screen is play.settings` | — | — | — | SET-07: B closes the sheet back to W-P4. (manual: needs control play.pause, control play.settings, which mesen-gui does not advertise) | all | major | manual |
| `settings.changes-kept` | under-test | `by hand: the screen is play.pause` | — | — | — | SET-07: the changes made on the tabs are kept after B; the hook reads no setting values. (manual: needs control play.pause, which mesen-gui does not advertise) | all | major | manual |

## Batch `pause-pack-multi`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `pause-pack-multi.reach-game` | setup | `fixture rom, fixture pack-multi and fixture settings.profiles.fresh` | — | — | — | A game is running (the P4-01 precondition). (manual: needs action nav.goal, control play.game, which mesen-gui does not advertise) | all | major | manual |
| `pause-pack-multi.chord-opens` | setup | `by hand: the screen is play.game` | — | — | — | W-P4 is open over the running game (setup, from P4-01). (manual: needs action pad.chord, control play.pause, control play.game, which mesen-gui does not advertise) | all | major | manual |
| `pause-pack-multi.pack-focus` | setup | `by hand: the screen is play.pause` | — | — | — | P4-06 (setup): focus Pack with 2+ installed packs. (manual: needs action nav.goal, control play.pause.pack, control play.pause, which mesen-gui does not advertise) | all | major | manual |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `pause-pack-multi.pack-picker-opens` | under-test | `the pack row focused in the pause menu (fixture pack-multi)` | — | — | — | P4-06: with 2+ packs A on Pack opens the picker W-P5. (manual: needs control play.pack, control play.pause.pack, which mesen-gui does not advertise) | all | major | manual |
| `pause-pack-multi.pack-picker-controls` | under-test | `by hand: play.pack is visible` | — | — | — | P4-06: the picker (W-P5) has radios by D-pad, Use This Pack and Cancel. (manual: needs control play.pack, which mesen-gui does not advertise) | all | minor | manual |
| `pause-pack-multi.pack-picker-b-to-pause` | under-test | `by hand: play.pack is visible` | — | — | — | P4-06: B closes the picker back to W-P4; focus returns to play.pause.pack. (manual: needs control play.pause.pack, control play.pack, which mesen-gui does not advertise) | all | major | manual |

## Batch `settings-menu-sounds`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `settings-menu-sounds.reach-audio` | setup | `fixture rom and fixture settings.profiles.fresh` | — | — | — | Settings is open on the Audio tab (the SET-03 precondition). (manual: needs action nav.goal, control play.settings.tab-audio, which mesen-gui does not advertise) | all | major | manual |
| `settings-menu-sounds.menu-sounds-focus` | setup | `by hand: the screen is play.settings` | — | — | — | SET-03: focus Menu sounds on the Audio tab; if the host has no Menu sounds row this step fails and stops only this batch. (manual: needs action nav.goal, control play.settings.menu-sounds, control play.settings, which mesen-gui does not advertise) | all | major | manual |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `settings-menu-sounds.menu-sounds-toggle` | under-test | `by hand: the ring is on play.settings.menu-sounds` | — | — | — | SET-03: A toggles Menu sounds on; the focus stays on the row. (manual: needs control play.settings.menu-sounds, which mesen-gui does not advertise) | all | major | manual |
| `settings-menu-sounds.menu-sounds-blips` | under-test | `by hand: the ring is on play.settings.menu-sounds` | — | — | — | SET-03: a soft blip on move, confirm and back at a fixed low level, never while a game runs unpaused (MenuSounds.ShouldPlay); the game's own audio is unaffected (ADR-0270). Judged by ear. (manual: needs control play.settings.menu-sounds, which mesen-gui does not advertise) | all | major | manual |

## Batch `controller`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `ctl.reach-controls-tab` | setup | `fixture rom and fixture settings.profiles.fresh` | — | — | — | Settings is open with the Controls tab focused; More in Options exists on both Audio and Controls, so the Controls tab is pinned first. (manual: needs action nav.goal, control play.settings.tab-controls, which mesen-gui does not advertise) | all | major | manual |
| `ctl.reach-more-in-options` | setup | `by hand: the ring is on play.settings.tab-controls` | — | — | — | Settings is open on the Controls tab with More in Options focused (the CTL-01 precondition: SET-01, Controls tab, game loaded); reached from the Controls tab so the goal cannot stop on Audio's identical button. (manual: needs action nav.goal, control play.settings.more-in-options, control play.settings.tab-controls, which mesen-gui does not advertise) | all | major | manual |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `ctl.open-sheet` | under-test | `by hand: the ring is on play.settings.more-in-options` | — | — | — | CTL-01: A on More in Options opens the Play Controller sheet over the paused game, not the classic ConfigWindow. (manual: needs control play.controller, control play.settings.more-in-options, which mesen-gui does not advertise) | all | major | manual |
| `ctl.sheet-holds-focus` | under-test | `by hand: the screen is play.controller` | — | — | — | CTL-01: the sheet holds the pad's focus (Done reachability is checked by the controller-done batch setup). (manual: needs control play.controller, which mesen-gui does not advertise) | all | major | manual |
| `ctl.pad-drawing-lights` | under-test | `by hand: the screen is play.controller` | — | — | — | CTL-01: the live pad drawing lights the buttons you press; the hook does not read the drawing, judged by eye on a real pad. (manual: needs control play.controller, which mesen-gui does not advertise) | all | minor | manual |
| `ctl.reach-first-row` | setup | `by hand: the screen is play.controller` | — | — | — | The first console-control row of the Controller sheet is focused (the CTL-02 precondition). (manual: needs action nav.goal, control play.controller.row-first, control play.controller, which mesen-gui does not advertise) | all | major | manual |
| `ctl.arm-row` | under-test | `by hand: the ring is on play.controller.row-first` | — | — | — | CTL-02: A on a console-control row arms the capture. (manual: needs control play.controller.capture-armed, control play.controller.row-first, which mesen-gui does not advertise) | all | major | manual |
| `ctl.arm-waits-release` | under-test | `by hand: play.controller.capture-armed is visible` | — | — | — | CTL-02: arming waits for the first button to be released, then the next pad button pressed maps and lights the two lights (pad side / port side); needs a real pad. (manual: needs control play.controller.capture-armed, which mesen-gui does not advertise) | all | major | manual |
| `ctl.nav-control-refused` | under-test | `by hand: play.controller.capture-armed is visible` | — | — | — | CTL-02: pressing a navigation control (A, B or a D-pad direction) while armed is refused visibly (ADR-0256 Decision 4); the refusal is not exposed by the hook and A/B are the pad's own presses. (manual: needs control play.controller.capture-armed, which mesen-gui does not advertise) | all | major | manual |
| `ctl.b-refused-capture-stays-armed` | under-test | `by hand: play.controller.capture-armed is visible` | — | — | — | CTL-02: B is a navigation control and is refused; the capture stays armed (ADR-0256 Decision 4); the refusal note is not exposed by the hook (manual: needs control play.controller.capture-armed, which mesen-gui does not advertise) | all | major | manual |
| `ctl.focus-stays-on-row-after-cancel` | under-test | `by hand: play.controller.capture-armed is visible` | — | — | — | CTL-02: after Esc cancels the capture (ADR-0255 slice 3), the focus ring stays on the row that was armed. Esc is keyboard-only, outside the pad-only vocabulary (manual: needs control play.controller.capture-armed, which mesen-gui does not advertise) | all | major | manual |
| `ctl.sheet-stays-after-cancel` | under-test | `by hand: play.controller.capture-armed is visible` | — | — | — | CTL-02: after Esc cancels the capture, the Controller sheet stays open and the pad drives it again (HasAuthority gains !IsControllerCapturing). Esc is keyboard-only, outside the pad-only vocabulary (manual: needs control play.controller.capture-armed, which mesen-gui does not advertise) | all | major | manual |
| `ctl.bind-extra-button` | under-test | `by hand: the screen is play.controller` | — | — | — | CTL-04: bind a spare button (a paddle or the right stick click) to Rewind, B to the game, press it while playing: the binding takes and the action fires in game (engine third key set, ADR-0255 slice 4); navigation controls are not offered in the list. Needs a real pad with a spare button. (manual: needs control play.controller, which mesen-gui does not advertise) | all | major | manual |

## Batch `controller-done`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `controller-done.reach` | setup | `fixture rom and fixture settings.profiles.fresh` | — | — | — | The Controller sheet is open with Done focused (the CTL-03 precondition: CTL-01). (manual: needs action nav.goal, control play.controller.done, which mesen-gui does not advertise) | all | major | manual |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `controller-done.leaves` | under-test | `by hand: the ring is on play.controller.done` | — | — | — | CTL-03: A on Done leaves the sheet to W-P4. (manual: needs control play.pause, control play.controller.done, which mesen-gui does not advertise) | all | major | manual |
| `controller-done.reach-settings-row` | setup | `by hand: the screen is play.pause` | — | — | — | W-P4 is open with the Settings row focused (reopen does not restore a row, so the row is pinned before the Down press). (manual: needs action nav.goal, control play.pause.settings, control play.pause, which mesen-gui does not advertise) | all | major | manual |
| `controller-done.pad-still-moves` | under-test | `by hand: the ring is on play.pause.settings` | — | — | — | CTL-03: no capture stays armed after close; the pad still moves focus on W-P4. (manual: needs control play.pause.quit-game, control play.pause.settings, which mesen-gui does not advertise) | all | major | manual |

## Batch `controller-b`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `controller-b.reach` | setup | `fixture rom and fixture settings.profiles.fresh` | — | — | — | The Controller sheet is open with a row focused (the CTL-03 precondition: CTL-01). (manual: needs action nav.goal, control play.controller.row-first, which mesen-gui does not advertise) | all | major | manual |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `controller-b.leaves` | under-test | `by hand: the ring is on play.controller.row-first` | — | — | — | CTL-03: B instead of Done leaves the sheet to W-P4. (manual: needs control play.pause, control play.controller.row-first, which mesen-gui does not advertise) | all | major | manual |
| `controller-b.reach-settings-row` | setup | `by hand: the screen is play.pause` | — | — | — | W-P4 is open with the Settings row focused (reopen does not restore a row, so the row is pinned before the Down press). (manual: needs action nav.goal, control play.pause.settings, control play.pause, which mesen-gui does not advertise) | all | major | manual |
| `controller-b.pad-still-moves` | under-test | `by hand: the ring is on play.pause.settings` | — | — | — | CTL-03: no capture stays armed after close; the pad still moves focus on W-P4. (manual: needs control play.pause.quit-game, control play.pause.settings, which mesen-gui does not advertise) | all | major | manual |

## Batch `controller-no-game`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `ctl-no-game.reach-home` | setup | `fixture settings.profiles.fresh` | — | — | — | Home with no game loaded (the CTL-05 precondition). (manual: needs action nav.goal, which mesen-gui does not advertise) | all | major | manual |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `ctl.no-game-gap` | under-test | `by hand: the screen is play.home` | — | — | — | CTL-05: KNOWN GAP, expected FAIL. Settings is not reachable by pad with no game (HOME-06, bug #1177), so neither is the Controller sheet. With Settings reached by keyboard, Controls > More in Options opens the classic Options window's Input page because OpenControllerSheet() returns false with no game loaded (ADR-0256 Decision 5 note). Record what the player sees. | all | major | manual |

## Batch `lamps`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `lamps.reach-home` | setup | `fixture settings.profiles.fresh` | — | — | — | Home with the pad port lamps on the status line (the LAMP-01 precondition: one pad). (manual: needs action nav.goal, which mesen-gui does not advertise) | all | major | manual |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `lamp.four-lamps-p1` | under-test | `by hand: the screen is play.home` | — | — | — | LAMP-01: the status line shows four lamps, P1 lit and the others dim; the number is the label and the pad name is the tooltip; the hook exposes neither lamps nor tooltips, judged by eye. | all | minor | manual |
| `lamp.second-pad-hotplug` | under-test | `by hand: the screen is play.home` | — | — | — | LAMP-02: plug a second pad in (or power it on over Bluetooth), wait 2 s, power it off: the second lamp lights within about 1 s (1 s poll) and dims again, nothing else moves, the ring stays where it was; the W-P15 pill "New controller …" on its first press is expected. A real plug is physical-only and is not simulated. | all | major | manual |

## Batch `lamps-game`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `lamps-game.reach-game` | setup | `fixture rom and fixture settings.profiles.fresh` | — | — | — | A game is running unpaused (the LAMP-03 precondition). (manual: needs action nav.goal, control play.game, which mesen-gui does not advertise) | all | major | manual |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `lamp.bar-hidden-in-game` | under-test | `by hand: the screen is play.game` | — | — | — | LAMP-03: the status bar and lamps are hidden while the game runs; a pack-install pill over a running game keeps the bar (sheetOpen), record only if observed; the hook does not read the bar. (manual: needs control play.game, which mesen-gui does not advertise) | all | minor | manual |

## Batch `loss`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `loss.reach-game` | setup | `fixture rom and fixture settings.profiles.fresh` | — | — | — | A game is running unpaused with PauseWhenInBackground at its default (on). (manual: needs action nav.goal, control play.game, which mesen-gui does not advertise) | all | major | manual |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `loss.focus-lost-pauses` | under-test | `by hand: the screen is play.game` | — | — | — | LOSS-01: another app takes focus: W-P4 opens with "Paused — …" naming the lost focus; regaining focus stays paused (ADR-0254 answer 1) and only Resume or the chord resumes. Taking focus by pad is not possible on macOS without a keyboard, so this is BLOCKED unless a second machine or remote switch exists; the keyboard (Cmd-Tab) may be used if noted. (manual: needs control play.game, which mesen-gui does not advertise) | all | major | manual |
| `loss.pad-unplug-pauses` | under-test | `by hand: the screen is play.game` | — | — | — | LOSS-02: unplug the pad, then replug it: W-P4 opens with "Paused — controller disconnected" (ADR-0254 amendment, always on in Play, not gated by PauseWhenInBackground); replugging rewrites the line to "Controller reconnected" and does not resume; the action bar names the pad still connected, or the keyboard with none. Then record whether the replugged pad resumes (chord or A on Resume). A real unplug is not simulated. (manual: needs control play.game, which mesen-gui does not advertise) | all | major | manual |
| `loss.pad-off-mid-menu` | under-test | `by hand: the screen is play.game` | — | — | — | LOSS-03 (pass 3): the tester opens W-P4 first (pause the running game); then, with a Bluetooth second pad, power it off, on again, press a direction: no second pause (already paused); on return the pad drives focus again and the footer names the right control. (manual: needs control play.game, which mesen-gui does not advertise) | all | minor | manual |
| `loss.pad-sleep` | under-test | `by hand: the screen is play.game` | — | — | — | LOSS-04 (pass 3): leave a Bluetooth pad idle until it sleeps (vendor timeout, typically 10-15 min), then wake it: sleep is a disconnect, W-P4 opens as in LOSS-02, and waking reconnects so the pad can navigate W-P4 and resume. Not covered by any repo test; observation only. (manual: needs control play.game, which mesen-gui does not advertise) | all | minor | manual |

## Batch `fullscreen`

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `FS-01` | under-test | `W-P4 > Settings > Display, windowed` | — | — | — | A on the Fullscreen switch goes full screen and the ring stays on the switch; the mouse cursor is hidden over the game (needs variant window.mode=fullscreen, which mesen-gui does not advertise) | all | major | manual |
| `FS-02` | under-test | `Full screen, W-P4 > Settings > Display` | — | — | — | Left from Done to Exit full screen (its own row, visible only in full screen, #910), A: back to a window with the ring on Done (needs variant window.mode=fullscreen, which mesen-gui does not advertise) | all | major | manual |
| `FS-03` | under-test | `Full screen, game running` | — | — | — | Chord, walk W-P4, open Settings, B, B: the [F] column of P4-01..SET-07, recorded only where it differs (needs variant window.mode=fullscreen, which mesen-gui does not advertise; needs action pad.chord, which mesen-gui does not advertise) | all | major | manual |
| `FS-04` | under-test | `Windowed` | — | — | — | Shrink the window to its minimum by pad: BLOCKED by design; record only whether the default window is below 1024x640 (ADR-0269 Decision 6). (manual: by design, not automatable - the pad cannot shrink the window, and the case asks a person to read the default window size, which no action or check the adapter advertises expresses) | all | major | manual |
| `FS-05` | under-test | `Full screen, Interface size Extra large` | — | — | — | Open the library, Cheats and the Controller sheet: nothing clipped, every Done/Back reachable (ADR-0269 Decision 6 names them; needs variant window.mode=fullscreen, which mesen-gui does not advertise) | all | major | manual |

## Batch `feel`

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `PAD-01` | under-test | `Any long list (library 100+)` | — | — | — | Hold Down 5 s, release, hold Up 5 s: steady repeat (~10/s after 0.4 s), stops on release, no overshoot | all | major | manual |
| `PAD-02` | under-test | `W-P4` | — | — | — | Tap a direction 10 times quickly: ten steps or as many as rows allow, none dropped, none doubled | all | major | manual |
| `PAD-04` | under-test | `Library` | — | — | — | Diagonal D-pad press (Up+Right): one deterministic step or none, no focus jump off the surface | all | major | manual |

## Batch `trap`

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `TRAP-01` | under-test | `Every Play surface and the three Options exits` | — | — | — | Enter, press B: every surface closes on B to its parent, no surface needs a mouse to leave (needs action pad.chord, which mesen-gui does not advertise to open W-P4 from a game) | all | major | manual |
| `TRAP-02` | under-test | `Any point` | — | — | — | Watch for any native dialog or classic window: none in the Play door except the ones ADR-0256 Decision 9 lists as deliberately native | all | major | manual |
| `TRAP-03` | under-test | `W-P15 pill shown (a never-mapped pad)` | — | — | — | Start on the pill, follow the lit steps, hold to skip, let it time out once (needs action pad.chord, which mesen-gui does not advertise) | all | major | manual |
| `TRAP-04` | under-test | `Home` | — | — | — | Quit the application by pad: expected FAIL, Tools ... is deliberately unpadded (#1137) and there is no pad route to quit | all | major | manual |

## Batch `journey`

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `JOURNEY-01` | under-test | `Cold start: the app is launched by OS means first (HOME-01 - a pad-only launch does not exist), the keyboard and mouse are set aside, and the timed pad-only journey starts at Home` | — | — | — | Launch -> Home -> Open a ROM -> search with Y, A -> A on a tile -> play 30 s -> chord -> Settings > Display > Interface size -> Large -> B -> Save states -> save a slot -> B -> Resume -> chord -> Quit game -> confirm -> Home, under 5 minutes (needs action pad.chord, which mesen-gui does not advertise) | all | major | manual |
| `JOURNEY-02` | under-test | `JOURNEY-01` | — | — | — | The same trip entered in full screen first and left at the end, focus kept across both mode switches (needs variant window.mode=fullscreen, which mesen-gui does not advertise; needs action pad.chord, which mesen-gui does not advertise) | all | major | manual |

## Batch `two-pads`

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `PAD2-01` | under-test | `Both pads connected, game running` | — | — | — | Chord on P-B: W-P4 opens and the footer names P-B's buttons (needs action pad.chord, which mesen-gui does not advertise) | all | major | manual |
| `PAD2-02` | under-test | `W-P4 open` | — | — | — | Navigate with P-A, then with P-B, alternating: both move the focus, one ring, no double-step | all | major | manual |
| `PAD2-03` | under-test | `Controller sheet, both connected` | — | — | — | Walk the PLAYERS rows and move P-B to P2 by pad: the port lamps are unchanged (ADR-0261) | all | major | manual |
| `PAD2-04` | under-test | `PAD2-03` | — | — | — | Unplug P-A, replug: on macOS the reconnect repair never fires and P-A may return on another index (a note for #813's family) | all | major | manual |
