# play-pad-only

<!-- Rendered from the JSON script by gui_test_render.py. Do not edit; edit the JSON. -->

- Format: `gui-test/1`
- Target: `mesen-gui`
- Requires actions: `pad.press`, `pad.release`, `pad.chord`, `nav.goal`, `nav.sweep`
- Requires checks: `ui.screen`, `ui.focused`, `ui.visible`, `ui.dialogs`
- Variant `window.mode`: `windowed`, `fullscreen`

## Fixtures

```json
{
  "note": "Control ids are provisional until the in-app AutomationIds land (#1182); steps use the HOME-01..06 wording of the manual script. Library, Favorites and Game batches (#1193) follow LIB-01..09, FAV-01..02, GAME-01..04; ids under play.library.*, play.keyboard, play.select-rom, play.bios and play.game are provisional the same way.",
  "rom": {
    "path": "<library>/Contra (USA).nes",
    "sha1": "<no-intro sha1 per ADR-0003, filled when the adapter lands>"
  },
  "rom-fds": {
    "note": "an FDS image with disksys.rom not installed (GAME-04)",
    "path": "<library>/Disk System Game.fds",
    "sha1": "<no-intro sha1 per ADR-0003, filled when the adapter lands>"
  },
  "rom-library-large": {
    "note": "a folder with 100+ ROMs (LIB-02)",
    "path": "<library-large>",
    "sha1": "<no-intro sha1 per ADR-0003, filled when the adapter lands>"
  },
  "rom-second": {
    "note": "the second game in the history-two-favorites profile, played before fixture rom (FAV-02)",
    "path": "<library>/Mega Man 2 (USA).nes",
    "sha1": "<no-intro sha1 per ADR-0003, filled when the adapter lands>"
  },
  "rom-zip": {
    "note": "an archive holding several ROMs (GAME-03)",
    "path": "<library>/Multi ROM Pack.zip",
    "sha1": "<no-intro sha1 per ADR-0003, filled when the adapter lands>"
  },
  "settings": {
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
| `launch.port-lamp-p1` | under-test | `ui.screen == play.home` | — | — | — | The ring is visible on Open a ROM and the pad port lamps show P1 lit (ADR-0261); judged by eye, the hook exposes neither. | window.mode=windowed | minor | manual |

## Batch `home`

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `home.focus-holds-up` | under-test | `ui.focused == play.home.open-rom` | `pad.press(button="Up", ticks=4)` | within 4 ticks | `ui.focused(is="play.home.open-rom")` | Focus stays on Open a ROM after D-pad Up. | all | major | automated |
| `home.focus-holds-down` | under-test | `ui.focused == play.home.open-rom` | `pad.press(button="Down", ticks=4)` | within 4 ticks | `ui.focused(is="play.home.open-rom")` | Focus stays on Open a ROM after D-pad Down. | all | major | automated |
| `home.focus-holds-left` | under-test | `ui.focused == play.home.open-rom` | `pad.press(button="Left", ticks=4)` | within 4 ticks | `ui.focused(is="play.home.open-rom")` | Focus stays on Open a ROM after D-pad Left. | all | major | automated |
| `home.focus-holds-right` | under-test | `ui.focused == play.home.open-rom` | `pad.press(button="Right", ticks=4)` | within 4 ticks | `ui.focused(is="play.home.open-rom")` | Focus stays on Open a ROM after D-pad Right. | all | major | automated |
| `home.ring-visible` | under-test | `ui.focused == play.home.open-rom` | — | — | — | The focus ring never disappears through the four presses. | all | minor | manual |
| `home.open-library` | under-test | `ui.focused == play.home.open-rom` | `pad.press(button="A", ticks=4)` | `ui.screen == play.library` within 120 ticks | `ui.screen(is="play.library")` | A on Open a ROM opens the library sheet (LIB-01). | all | major | automated |
| `home.back-to-home` | under-test | `ui.screen == play.library` | `pad.press(button="B", ticks=4)` | `ui.screen == play.home` within 120 ticks | `ui.screen(is="play.home")` | B on the sheet returns to Home. | all | major | automated |
| `home.back-focus-restored` | under-test | `ui.screen == play.home` | `pad.release(ticks=1)` | `ui.focused == play.home.open-rom` within 4 ticks | `ui.focused(is="play.home.open-rom")` | The ring is back on Open a ROM after B. | all | major | automated |
| `home.chord-opens-nothing` | under-test | `ui.screen == play.home` | `pad.chord(buttons=["Select", "Start"], ticks=4)` | within 4 ticks | `ui.dialogs(is=[])` | The chord with no game loaded opens no overlay. | all | major | automated |
| `home.chord-focus-unchanged` | under-test | `ui.screen == play.home` | `pad.release(ticks=1)` | within 1 ticks | `ui.focused(is="play.home.open-rom")` | Focus is unchanged after the chord. | all | minor | automated |
| `home.settings-by-pad-gap` | under-test | `ui.screen == play.home` | — | — | — | Y opens the Settings sheet from Home with no game loaded (bug #1177, ADR-0256 W-P8); B returns to Home. | all | major | manual |

## Batch `home-after-game`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `after-game.reach-home` | setup | `fixture settings.profiles.history-one-favorite and fixture rom` | `nav.goal(goal="ui.screen == play.home")` | `ui.screen == play.home` within 600 ticks | — | Home W-P2 (Continue / Favorites / Recent) with at least one game played (the HOME-03 precondition). | all | major | automated |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `after-game.continue-first` | under-test | `ui.screen == play.home` | `pad.release(ticks=1)` | `ui.focused == play.home.continue` within 4 ticks | `ui.focused(is="play.home.continue")` | Continue playing is the first focus stop. | all | major | automated |
| `after-game.order-favorites` | under-test | `ui.focused == play.home.continue` | `pad.press(button="Down", ticks=4)` | `ui.focused == play.home.favorites` within 4 ticks | `ui.focused(is="play.home.favorites")` | Favorites follows Continue when the shelf exists (ADR-0268 Decision 6). | all | major | automated |
| `after-game.order-recent` | under-test | `ui.focused == play.home.favorites` | `pad.press(button="Down", ticks=4)` | `ui.focused == play.home.recent` within 4 ticks | `ui.focused(is="play.home.recent")` | Recent follows Favorites (Continue, Favorites, Recent; ADR-0268 Decision 6). | all | major | automated |
| `after-game.order-open-rom` | under-test | `ui.focused == play.home.recent` | `pad.press(button="Down", ticks=4)` | `ui.focused == play.home.open-rom` within 4 ticks | `ui.focused(is="play.home.open-rom")` | Open a ROM is reachable last. | all | major | automated |
| `after-game.one-ring` | under-test | `ui.screen == play.home` | — | — | — | The ring is on exactly one control at a time (ADR-0256 Decision 3); with a Favorites shelf present the order is Continue, Favorites, Recent. Both are judged by eye. | all | minor | manual |
| `after-game.contained-up-continue` | under-test | `ui.focused == play.home.continue` | `pad.press(button="Up", ticks=12)` | within 12 ticks | `ui.focused(within="play.home")` | Repeated D-pad Up from Continue never leaves the Home host for the header's Profile or Tools buttons (#1137, #1166). | all | major | automated |
| `after-game.contained-up-recent` | under-test | `ui.focused == play.home.recent` | `pad.press(button="Up", ticks=12)` | within 12 ticks | `ui.focused(within="play.home")` | Repeated D-pad Up from Recent never leaves the Home host for the header's Profile or Tools buttons (#1137, #1166). | all | major | automated |
| `after-game.contained-up-favorites` | under-test | `ui.focused == play.home.favorites` | `pad.press(button="Up", ticks=12)` | within 12 ticks | `ui.focused(within="play.home")` | Repeated D-pad Up from Favorites never leaves the Home host for the header's Profile or Tools buttons (#1137, #1166). | all | major | automated |
| `after-game.contained-up-open-rom` | under-test | `ui.focused == play.home.open-rom` | `pad.press(button="Up", ticks=12)` | within 12 ticks | `ui.focused(within="play.home")` | Repeated D-pad Up from Open a ROM never leaves the Home host for the header's Profile or Tools buttons (#1137, #1166). | all | major | automated |
| `after-game.contained-sideways-left` | under-test | `ui.focused == play.home.continue` | `pad.press(button="Left", ticks=12)` | within 12 ticks | `ui.focused(within="play.home")` | Left on the top row stays inside the Home host. | all | major | automated |
| `after-game.contained-sideways-right` | under-test | `ui.focused == play.home.continue` | `pad.press(button="Right", ticks=12)` | within 12 ticks | `ui.focused(within="play.home")` | Right on the top row stays inside the Home host. | all | major | automated |
| `after-game.contained-other-controls-gap` | under-test | `ui.screen == play.home` | — | — | — | From Favorites, Recent and Open a ROM, D-pad Left and Right keep focus inside Home (the automated steps press them from Continue only) | all | major | manual |

## Batch `library`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `lib.reach-library` | setup | `fixture rom-library-large and fixture settings.profiles.fresh` | `nav.goal(goal="ui.screen == play.library")` | `ui.screen == play.library` within 600 ticks | — | The library sheet (W-P19) is open with a focused tile (the LIB-01 precondition). | all | major | automated |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `lib.open-screen` | under-test | `ui.screen == play.library` | — | `ui.screen == play.library` within 120 ticks | `ui.screen(is="play.library")` | LIB-01: the flat library grid is open. | all | major | automated |
| `lib.first-tile-focused` | under-test | `ui.screen == play.library` | — | `ui.focused == play.library.tile` within 120 ticks | `ui.focused(is="play.library.tile")` | LIB-01: a tile is focused (ring on the grid's first tile). | all | major | automated |
| `lib.header-footer-scan` | under-test | `ui.screen == play.library` | — | — | — | LIB-01: header reads "Your library · N games in M folders", the footer names A/B/Y/X/LB/RB in the pad's own words (ADR-0256 Decision 6), an animated indicator shows while scanning. | all | minor | manual |
| `lib.hold-down-repeats` | under-test | `fixture rom-library-large; ui.screen == play.library` | — | — | — | LIB-02: with 100+ ROMs, a held Down repeats after 400 ms then every 100 ms (ADR-0256 Decision 7) and the grid scrolls; judged by timing and eye. Part 2 (same with Right) is the next step. | all | major | manual |
| `lib.hold-right-repeats` | under-test | `fixture rom-library-large; ui.screen == play.library` | — | — | — | LIB-02: the same with Right: a held Right repeats after 400 ms then every 100 ms and the focus walks the row, wrapping onto the next row. | all | major | manual |
| `lib.tap-down-steps-once` | under-test | `fixture rom-library-large; ui.focused == play.library.tile` | `pad.press(button="Down", ticks=4)` | `ui.focused == play.library.tile` within 4 ticks | `ui.focused(is="play.library.tile")` | LIB-02: one tap steps the focus once and the focus stays in the grid. | all | major | automated |
| `lib.up-back-to-row-one` | under-test | `ui.focused == play.library.tile` | `pad.press(button="Up", ticks=4)` | `ui.focused == play.library.tile` within 120 ticks | `ui.focused(is="play.library.tile")` | LIB-03 setup: Up returns the focus from row 2 to the top grid row, ready to enter the header. | all | major | automated |
| `lib.up-enters-header` | under-test | `ui.focused == play.library.tile` | `pad.press(button="Up", ticks=4)` | `ui.focused == play.library.search` within 120 ticks | `ui.focused(is="play.library.search")` | LIB-03: Up from the top grid row enters the header row (search first, ADR-0264 Decision 3). | all | major | automated |
| `lib.header-right-folders` | under-test | `ui.focused == play.library.search` | `pad.press(button="Right", ticks=4)` | `ui.focused == play.library.folders` within 120 ticks | `ui.focused(is="play.library.folders")` | LIB-03: Right moves to Library folders. | all | major | automated |
| `lib.header-right-browse` | under-test | `ui.focused == play.library.folders` | `pad.press(button="Right", ticks=4)` | `ui.focused == play.library.browse` within 120 ticks | `ui.focused(is="play.library.browse")` | LIB-03: Right moves to Browse a file. | all | major | automated |
| `lib.header-right-back` | under-test | `ui.focused == play.library.browse` | `pad.press(button="Right", ticks=4)` | `ui.focused == play.library.back` within 120 ticks | `ui.focused(is="play.library.back")` | LIB-03: Right moves to Back. | all | major | automated |
| `lib.header-left-across` | under-test | `ui.focused == play.library.back` | `pad.press(button="Left", ticks=4)` | `ui.focused == play.library.browse` within 120 ticks | `ui.focused(is="play.library.browse")` | LIB-03: Left walks back across the header row (Back -> Browse), mirroring the Right walk. | all | major | automated |
| `lib.header-down-grid` | under-test | `ui.focused == play.library.browse` | `pad.press(button="Down", ticks=4)` | `ui.focused == play.library.tile` within 120 ticks | `ui.focused(is="play.library.tile")` | LIB-03: Down from the header returns to the grid. | all | major | automated |
| `lib.filter-rb-wraps` | under-test | `ui.screen == play.library and two consoles in the library` | — | — | — | LIB-04: RB steps All -> each console present -> All, wrapping; the grid narrows; never an empty filter (PlayerRomPickerViewModel.ConsoleFilter.cs). The filter is not a named control the hook reads. | all | minor | manual |
| `lib.filter-lb-backwards` | under-test | `ui.screen == play.library and two consoles in the library` | — | — | — | LIB-04: LB walks the same ring backwards, wrapping at both ends. | all | minor | manual |
| `lib.filter-chips-not-focusable` | under-test | `ui.screen == play.library` | — | — | — | KNOWN GAP: LIB-04 (#1134, KnownChipGaps = "Library"): the console chips cannot take focus; LB/RB is the only pad route. Record whether the current chip is obvious at couch distance. | all | major | manual |
| `lib.search-y-focuses-field` | under-test | `ui.focused == play.library.tile` | `pad.press(button="Y", ticks=4)` | `ui.focused == play.library.search` within 120 ticks | `ui.focused(is="play.library.search")` | LIB-05: Y moves the ring to the search field. | all | major | automated |
| `lib.search-a-opens-keyboard` | under-test | `ui.focused == play.library.search` | `pad.press(button="A", ticks=4)` | `ui.visible == play.keyboard` within 120 ticks | `ui.visible(is="play.keyboard")` | LIB-05: A opens the shared on-screen keyboard (ADR-0262) below the field; the sheet stays open. | all | major | automated |
| `lib.search-type-filters` | under-test | `ui.visible == play.keyboard` | — | — | — | LIB-05: typing `zel` (or a prefix of an owned title) with D-pad + A filters the grid per letter; OK commits and focus returns to the field; an empty result shows "No games match" with a way to clear it. | all | minor | manual |
| `lib.search-b-cancels` | under-test | `ui.visible == play.keyboard` | `pad.press(button="B", ticks=4)` | `ui.focused == play.library.search` within 8 ticks | `ui.focused(is="play.library.search")` | LIB-05: B on the open keyboard closes it, focus returns to the search field and the sheet does not close. The text revert on a second Y, A, B is judged by eye (next step). | all | major | automated |
| `lib.search-second-cancel-reverts` | under-test | `ui.focused == play.library.search` | — | — | — | LIB-05: after typing, a second Y, A, B returns the field to its previous text; focus returns to it and the sheet does not close (typing is manual, so the text revert is manual). | all | minor | manual |
| `lib.search-sheet-stays` | under-test | `ui.focused == play.library.search` | — | `ui.screen == play.library` within 4 ticks | `ui.screen(is="play.library")` | LIB-05: the library sheet is still open under the cancelled keyboard. | all | major | automated |
| `lib.search-right-folders` | under-test | `ui.focused == play.library.search` | `pad.press(button="Right", ticks=4)` | `ui.focused == play.library.folders` within 8 ticks | `ui.focused(is="play.library.folders")` | LIB-06: Right from the search field reaches Library folders. | all | major | automated |
| `lib.folders-open` | under-test | `ui.focused == play.library.folders` | `pad.press(button="A", ticks=4)` | `ui.screen == play.library.folders` within 120 ticks | `ui.screen(is="play.library.folders")` | LIB-06: A on Library folders opens the pad-reachable folder list (ADR-0264 Decision 8). | all | major | automated |
| `lib.folders-no-native-dialog` | under-test | `ui.screen == play.library.folders` | — | within 4 ticks | `ui.dialogs(is=[])` | LIB-06: no native OS dialog is open over the folder list. | all | major | automated |
| `lib.folders-add-remove` | under-test | `ui.screen == play.library.folders` | — | — | — | LIB-06: add a folder, remove it, add it back by pad; the header count updates. | all | major | manual |
| `lib.folders-b-closes` | under-test | `ui.screen == play.library.folders` | `pad.press(button="B", ticks=4)` | `ui.screen == play.library` within 120 ticks | `ui.screen(is="play.library")` | LIB-06: B closes the folder list back to the library. | all | major | automated |
| `lib.folders-right-browse` | under-test | `ui.focused == play.library.folders` | `pad.press(button="Right", ticks=4)` | `ui.focused == play.library.browse` within 8 ticks | `ui.focused(is="play.library.browse")` | LIB-07: Right from Library folders reaches Browse a file. | all | major | automated |
| `lib.browse-open` | under-test | `ui.focused == play.library.browse` | `pad.press(button="A", ticks=4)` | `ui.screen == play.library.browse` within 120 ticks | `ui.screen(is="play.library.browse")` | LIB-07: A on Browse a file opens the folder browser (ADR-0256 Decision 9). | all | major | automated |
| `lib.browse-walk` | under-test | `ui.screen == play.library.browse` | — | — | — | LIB-07: Confirm descends, B ascends; the action row leads the list but the ring never lands on it first (first-row guard); Make this my games folder inside a non-empty folder. | all | minor | manual |
| `lib.browse-b-root-dismisses` | under-test | `ui.screen == play.library.browse` | `pad.press(button="B", ticks=4)` | `ui.screen == play.library` within 120 ticks | `ui.screen(is="play.library")` | LIB-07: B on the root dismisses with no load, back to the library. | all | major | automated |
| `lib.box-art` | under-test | `ui.screen == play.library and online` | — | — | — | LIB-08: box art for known ROMs (lazy, visible tiles only), a generic console-colored cover carrying the title for unknown ones, nothing waits on the network; a visual check, record only if it blocks focus or scrolling. | all | minor | manual |
| `lib.browse-down-grid` | under-test | `ui.focused == play.library.browse` | `pad.press(button="Down", ticks=4)` | `ui.focused == play.library.tile` within 8 ticks | `ui.focused(is="play.library.tile")` | LIB-09: Down from the header returns to the grid (LIB-09 starts from the grid). | all | major | automated |
| `lib.back-to-home` | under-test | `ui.screen == play.library` | `pad.press(button="B", ticks=4)` | `ui.screen == play.home` within 120 ticks | `ui.screen(is="play.home")` | LIB-09: B from the grid goes back to Home. | all | major | automated |
| `lib.back-ring-on-opener` | under-test | `ui.screen == play.home` | — | `ui.focused == play.home.open-rom` within 8 ticks | `ui.focused(is="play.home.open-rom")` | LIB-09: the ring is on the control that opened the sheet, Open a ROM. | all | major | automated |

## Batch `favorites`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `fav.reach-library` | setup | `fixture rom and fixture settings.profiles.history-no-favorite` | `nav.goal(goal="ui.screen == play.library")` | `ui.screen == play.library` within 600 ticks | — | The library sheet is open with a tile focused (the FAV-01 precondition). | all | major | automated |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `fav.x-favorites-tile` | under-test | `ui.focused == play.library.tile` | `pad.press(button="X", ticks=4)` | `ui.focused == play.library.tile` within 4 ticks | `ui.focused(is="play.library.tile")` | FAV-01: X on the focused tile keeps the ring on the grid and toggles Favorite (ADR-0268). | all | major | automated |
| `fav.footer-reads-unfavorite` | under-test | `ui.screen == play.library` | — | — | — | FAV-01: the footer's X entry now reads Unfavorite (it read Favorite before). | all | minor | manual |
| `fav.back-to-home` | under-test | `ui.screen == play.library` | `pad.press(button="B", ticks=4)` | `ui.screen == play.home` within 120 ticks | `ui.screen(is="play.home")` | FAV-01: B returns to Home. | all | major | automated |
| `fav.home-shelf-shown` | under-test | `ui.screen == play.home` | — | `ui.visible == play.home.favorites` within 8 ticks | `ui.visible(is="play.home.favorites")` | FAV-01: Home shows a Favorites shelf between Continue and Recent, newest favorite first. | all | major | automated |
| `fav.library-again` | under-test | `ui.focused == play.home.open-rom` | `pad.press(button="A", ticks=4)` | `ui.screen == play.library` within 120 ticks | `ui.screen(is="play.library")` | FAV-01: A on Open a ROM returns to the library. | all | major | automated |
| `fav.x-unfavorites-tile` | under-test | `ui.focused == play.library.tile` | `pad.press(button="X", ticks=4)` | `ui.focused == play.library.tile` within 4 ticks | `ui.focused(is="play.library.tile")` | FAV-01: X on the same tile unfavorites it; the ring stays on the grid. | all | major | automated |
| `fav.back-home-again` | under-test | `ui.screen == play.library` | `pad.press(button="B", ticks=4)` | `ui.screen == play.home` within 120 ticks | `ui.screen(is="play.home")` | FAV-01: B returns to Home. | all | major | automated |
| `fav.shelf-hides-when-empty` | under-test | `ui.screen == play.home` | — | — | — | FAV-01: with no favorite left the Favorites shelf is hidden. | all | minor | manual |

## Batch `favorites-home`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `fav2.reach-favorite-tile` | setup | `fixture rom, fixture rom-second and fixture settings.profiles.history-two-favorites` | `nav.goal(goal="ui.focused == play.home.favorite")` | `ui.focused == play.home.favorite` within 600 ticks | — | Home W-P2 with a Favorites shelf and the ring on a favorite tile (the FAV-02 precondition). | all | major | automated |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `fav2.x-on-favorite-tile` | under-test | `ui.focused == play.home.favorite` | `pad.press(button="X", ticks=4)` | `ui.visible == play.home.favorites` within 4 ticks | `ui.visible(is="play.home.favorites")` | FAV-02: X acts on the focused cover; on a Home favorite tile it toggles that game, the ring stays on the shelf and the Favorites shelf stays visible (two favorites, so the shelf survives the toggle; ADR-0268 Decision 4). | all | major | automated |
| `fav2.focus-stays-on-favorite-tile` | under-test | `ui.visible == play.home.favorites` | — | `ui.focused == play.home.favorite` within 120 ticks | `ui.focused(is="play.home.favorite")` | FAV-02: after X the ring is still on a Home favorite tile (the shelf remains because a second favorite is left). | all | major | automated |
| `fav2.up-to-continue` | under-test | `ui.focused == play.home.favorite` | `pad.press(button="Up", ticks=4)` | `ui.focused == play.home.continue` within 8 ticks | `ui.focused(is="play.home.continue")` | FAV-02: Up reaches the Continue card (focus order Continue -> Favorites -> Recent, ADR-0268 Decision 6). | all | major | automated |
| `fav2.x-on-continue` | under-test | `ui.focused == play.home.continue` | `pad.press(button="X", ticks=4)` | `ui.focused == play.home.continue` within 4 ticks | `ui.focused(is="play.home.continue")` | FAV-02: X on the Continue card favorites the Continue game (ADR-0268 Decision 1); the ring stays on Continue. | all | major | automated |
| `fav2.down-favorite` | under-test | `ui.focused == play.home.continue` | `pad.press(button="Down", ticks=4)` | `ui.focused == play.home.favorite` within 8 ticks | `ui.focused(is="play.home.favorite")` | FAV-02: Down returns to the Favorites shelf. | all | major | automated |
| `fav2.down-recent` | under-test | `ui.focused == play.home.favorite` | `pad.press(button="Down", ticks=4)` | `ui.focused == play.home.recent` within 8 ticks | `ui.focused(is="play.home.recent")` | FAV-02: Down reaches the Recent grid. | all | major | automated |
| `fav2.down-open-rom` | under-test | `ui.focused == play.home.recent` | `pad.press(button="Down", ticks=4)` | `ui.focused == play.home.open-rom` within 8 ticks | `ui.focused(is="play.home.open-rom")` | FAV-02: Down reaches Open a ROM. | all | major | automated |
| `fav2.x-elsewhere-does-nothing` | under-test | `ui.focused == play.home.open-rom` | `pad.press(button="X", ticks=4)` | `ui.focused == play.home.open-rom` within 4 ticks | `ui.focused(is="play.home.open-rom")` | FAV-02: X on Open a ROM does nothing; the ring stays put. | all | major | automated |

## Batch `game`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `game.reach-library` | setup | `fixture rom and fixture settings.profiles.fresh` | `nav.goal(goal="ui.screen == play.library")` | `ui.screen == play.library` within 600 ticks | — | The library sheet is open with a tile focused (the GAME-01 precondition). | all | major | automated |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `game.a-loads-game` | under-test | `ui.focused == play.library.tile` | `pad.press(button="A", ticks=4)` | `ui.screen == play.game` within 600 ticks | `ui.screen(is="play.game")` | GAME-01: A on a tile closes the sheet and the game runs. | all | major | automated |
| `game.load-card-and-toast` | under-test | `ui.screen == play.game` | — | — | — | GAME-01: a load card with a moving indicator shows; for the first three starts the entry toast ends with the menu hint naming the pad's chord, e.g. "· Select+Start for the menu" (ADR-0251 §2), not "Esc". | all | minor | manual |
| `game.pad-is-the-consoles` | under-test | `ui.screen == play.game` | `pad.press(button="Right", ticks=600)` | `ui.screen == play.game` within 600 ticks | `ui.screen(is="play.game")` | GAME-02: playing 10 s with the D-pad leaves the game running; the pad is the console's (ADR-0256 Decision 1). | all | major | automated |
| `game.buttons-are-the-consoles` | under-test | `ui.screen == play.game` | — | — | — | GAME-02: the face buttons reach the console too; pressing A and B leaves the game running and nothing in the GUI reacts. | all | major | manual |
| `game.no-menu-feedback` | under-test | `ui.screen == play.game` | — | — | — | GAME-02: no menu sound, no focus ring, no haptic tick, nothing in the GUI reacts; the status bar and lamps are hidden (ADR-0261 Consequences). | all | minor | manual |

## Batch `game-archive`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `game-archive.reach-library` | setup | `fixture rom-zip (a library holding only that file) and fixture settings.profiles.fresh` | `nav.goal(goal="ui.screen == play.library and ui.focused == play.library.tile")` | `ui.screen == play.library and ui.focused == play.library.tile` within 600 ticks | — | The library sheet is open with the multi-ROM archive tile focused (fixture rom-zip). | all | major | automated |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `game-archive.a-opens-select-sheet` | under-test | `ui.focused == play.library.tile and fixture rom-zip` | `pad.press(button="A", ticks=4)` | `ui.screen == play.select-rom` within 120 ticks | `ui.screen(is="play.select-rom")` | GAME-03: A on the archive tile opens the "which game" sheet (PlaySelectRomSheet). | all | major | automated |
| `game-archive.b-cancels-back` | under-test | `ui.screen == play.select-rom` | `pad.press(button="B", ticks=4)` | `ui.screen == play.library` within 120 ticks | `ui.screen(is="play.library")` | GAME-03: B cancels back to the library. | all | major | automated |
| `game-archive.a-reopens` | under-test | `ui.focused == play.library.tile` | `pad.press(button="A", ticks=4)` | `ui.screen == play.select-rom` within 120 ticks | `ui.screen(is="play.select-rom")` | GAME-03: A on the archive tile opens the sheet again. | all | major | automated |
| `game-archive.a-picks-a-game` | under-test | `ui.screen == play.select-rom` | `pad.press(button="A", ticks=4)` | `ui.screen == play.game` within 600 ticks | `ui.screen(is="play.game")` | GAME-03: D-pad + A picks a game and it loads. | all | major | automated |

## Batch `game-bios`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `game-bios.reach-library` | setup | `fixture rom-fds (a library holding only that file) and fixture settings.profiles.fresh` | `nav.goal(goal="ui.screen == play.library and ui.focused == play.library.tile")` | `ui.screen == play.library and ui.focused == play.library.tile` within 600 ticks | — | The library sheet is open with the FDS tile focused (fixture rom-fds, disksys.rom not installed). | all | major | automated |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `game-bios.a-opens-bios-sheet` | under-test | `ui.focused == play.library.tile and fixture rom-fds` | `pad.press(button="A", ticks=4)` | `ui.screen == play.bios` within 120 ticks | `ui.screen(is="play.bios")` | GAME-04: A on the FDS image shows the BIOS sheet (W-P13). | all | major | automated |
| `game-bios.choose-file-native` | under-test | `ui.screen == play.bios` | — | — | — | KNOWN GAP: GAME-04: Choose File... on the BIOS sheet is a native dialog (ADR-0256 Decision 9 refusals). Expected FAIL; record that Cancel still backs out. | all | major | manual |
| `game-bios.b-cancels` | under-test | `ui.screen == play.bios` | `pad.press(button="B", ticks=4)` | `ui.screen == play.library` within 120 ticks | `ui.screen(is="play.library")` | GAME-04: B/Cancel backs out cleanly to the library. | all | major | automated |
| `game-bios.status-names-bios` | under-test | `ui.screen == play.library` | — | — | — | GAME-04: the status line names the missing BIOS. | all | minor | manual |

## Batch `dialogs`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `dialogs.reach-home` | setup | `fixture settings.profiles.fresh` | `nav.goal(goal="ui.screen == play.home")` | `ui.screen == play.home` within 600 ticks | — | Home W-P2 is on screen, the precondition of the dialogs sweep. | all | major | automated |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `dialogs.no-trap` | under-test | `ui.screen == play.home` | `nav.sweep(input="pad", scope="dialogs", bound=60)` | within 600 ticks | — | Jev walks Home by pad toward screens it has not visited; every dialog it reaches has a pad move that leaves it (no trap), within 60 moves. | all | major | automated |
