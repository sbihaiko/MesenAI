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
  "note": "Control ids are provisional until the in-app AutomationIds land (#1182); steps use the HOME-01..06 wording of the manual script. Library, Favorites and Game batches (#1193) follow LIB-01..09, FAV-01..02, GAME-01..04; Controller sheet, port lamps and loss batches (#1195) follow CTL-01..05, LAMP-01..03 and LOSS-01..04; ids under play.controller.* are provisional the same way. Ids under play.library.*, play.keyboard, play.select-rom, play.bios and play.game are provisional the same way.",
  "pack": {
    "note": "exactly one installed pack matching the fixture ROM, so A on Pack opens the detail sheet W-P6 (P4-06)",
    "path": "<library>/EnhancementPacks/<one installed pack for the fixture ROM>"
  },
  "pack-multi": {
    "note": "2+ installed packs matching the fixture ROM, so A on Pack opens the picker W-P5 (P4-06 variant)",
    "path": "<library>/EnhancementPacks/<two or more installed packs for the fixture ROM>"
  },
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

## Batch `pause`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `pause.reach-game` | setup | `fixture rom, fixture pack and fixture settings.profiles.fresh` | `nav.goal(goal="ui.screen == play.game")` | `ui.screen == play.game` within 600 ticks | — | A game is running (the P4-01 precondition). | all | major | automated |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `pause.chord-opens` | under-test | `ui.screen == play.game` | `pad.chord(buttons=["Select", "Start"], frames=4)` | `ui.screen == play.pause` within 120 frames | `ui.screen(is="play.pause")` | P4-01: the chord opens W-P4 with the game paused behind it. | all | major | automated |
| `pause.resume-focused` | under-test | `ui.screen == play.pause` | — | `ui.focused == play.pause.resume` within 120 ticks | `ui.focused(is="play.pause.resume")` | P4-01: Resume is the focused control. | all | major | automated |
| `pause.row-resume` | under-test | `ui.screen == play.pause` | — | within 4 ticks | `ui.visible(is="play.pause.resume")` | P4-01: the control resume is present (seven in all: Resume, Save states, Pack, Enhancements, Cheats, Settings, Quit game). | all | major | automated |
| `pause.row-save-states` | under-test | `ui.screen == play.pause` | — | within 4 ticks | `ui.visible(is="play.pause.save-states")` | P4-01: the control save-states is present (seven in all: Resume, Save states, Pack, Enhancements, Cheats, Settings, Quit game). | all | major | automated |
| `pause.row-pack` | under-test | `ui.screen == play.pause` | — | within 4 ticks | `ui.visible(is="play.pause.pack")` | P4-01: the control pack is present (seven in all: Resume, Save states, Pack, Enhancements, Cheats, Settings, Quit game). | all | major | automated |
| `pause.row-enhancements` | under-test | `ui.screen == play.pause` | — | within 4 ticks | `ui.visible(is="play.pause.enhancements")` | P4-01: the control enhancements is present (seven in all: Resume, Save states, Pack, Enhancements, Cheats, Settings, Quit game). | all | major | automated |
| `pause.row-cheats` | under-test | `ui.screen == play.pause` | — | within 4 ticks | `ui.visible(is="play.pause.cheats")` | P4-01: the control cheats is present (seven in all: Resume, Save states, Pack, Enhancements, Cheats, Settings, Quit game). | all | major | automated |
| `pause.row-settings` | under-test | `ui.screen == play.pause` | — | within 4 ticks | `ui.visible(is="play.pause.settings")` | P4-01: the control settings is present (seven in all: Resume, Save states, Pack, Enhancements, Cheats, Settings, Quit game). | all | major | automated |
| `pause.row-quit-game` | under-test | `ui.screen == play.pause` | — | within 4 ticks | `ui.visible(is="play.pause.quit-game")` | P4-01: the control quit-game is present (seven in all: Resume, Save states, Pack, Enhancements, Cheats, Settings, Quit game). | all | major | automated |
| `pause.footer-words` | under-test | `ui.screen == play.pause` | — | — | — | P4-01: the footer names "B to resume" in words for the pad in hand (ADR-0256 Decision 6: "Circle", never a glyph); the hook does not read footer text. | all | minor | manual |
| `pause.frozen-frame-bar-lamps` | under-test | `ui.screen == play.pause` | — | — | — | P4-01: a frozen frame shows behind the overlay and the status bar and lamps are visible; judged by eye. | all | minor | manual |
| `pause.chord-again-resumes` | under-test | `ui.screen == play.pause` | `pad.chord(buttons=["Select", "Start"], ticks=4)` | `ui.screen == play.game` within 120 ticks | `ui.screen(is="play.game")` | P4-02: the chord again closes W-P4 and resumes the game (ADR-0241 Esc order: game, W-P4, resume). | all | major | automated |
| `pause.reopen-for-b` | under-test | `ui.screen == play.game` | `pad.chord(buttons=["Select", "Start"], frames=4)` | `ui.screen == play.pause` within 120 frames | `ui.screen(is="play.pause")` | P4-02: the chord reopens W-P4 (setup for the B half). | all | major | automated |
| `pause.b-resumes` | under-test | `ui.screen == play.pause` | `pad.press(button="B", ticks=4)` | `ui.screen == play.game` within 120 ticks | `ui.screen(is="play.game")` | P4-02: B closes W-P4 and resumes the game. | all | major | automated |
| `pause.reopen-for-rows` | under-test | `ui.screen == play.game` | `pad.chord(buttons=["Select", "Start"], frames=4)` | `ui.screen == play.pause` within 120 frames | `ui.screen(is="play.pause")` | P4-03: the chord reopens W-P4 with Resume focused (P4-01 precondition). | all | major | automated |
| `pause.down-to-save-states` | under-test | `ui.focused == play.pause.resume` | `pad.press(button="Down", ticks=4)` | `ui.focused == play.pause.save-states` within 8 ticks | `ui.focused(is="play.pause.save-states")` | P4-03: D-pad Down from resume reaches save-states; the ring is on it. | all | major | automated |
| `pause.down-to-pack` | under-test | `ui.focused == play.pause.save-states` | `pad.press(button="Down", ticks=4)` | `ui.focused == play.pause.pack` within 8 ticks | `ui.focused(is="play.pause.pack")` | P4-03: D-pad Down from save-states reaches pack; the ring is on it. | all | major | automated |
| `pause.down-to-enhancements` | under-test | `ui.focused == play.pause.pack` | `pad.press(button="Down", ticks=4)` | `ui.focused == play.pause.enhancements` within 8 ticks | `ui.focused(is="play.pause.enhancements")` | P4-03: D-pad Down from pack reaches enhancements; the ring is on it. | all | major | automated |
| `pause.down-to-cheats` | under-test | `ui.focused == play.pause.enhancements` | `pad.press(button="Down", ticks=4)` | `ui.focused == play.pause.cheats` within 8 ticks | `ui.focused(is="play.pause.cheats")` | P4-03: D-pad Down from enhancements reaches cheats; the ring is on it. | all | major | automated |
| `pause.down-to-settings` | under-test | `ui.focused == play.pause.cheats` | `pad.press(button="Down", ticks=4)` | `ui.focused == play.pause.settings` within 8 ticks | `ui.focused(is="play.pause.settings")` | P4-03: D-pad Down from cheats reaches settings; the ring is on it. | all | major | automated |
| `pause.down-to-quit-game` | under-test | `ui.focused == play.pause.settings` | `pad.press(button="Down", ticks=4)` | `ui.focused == play.pause.quit-game` within 8 ticks | `ui.focused(is="play.pause.quit-game")` | P4-03: D-pad Down from settings reaches quit-game; the ring is on it. | all | major | automated |
| `pause.down-no-wrap` | under-test | `ui.focused == play.pause.quit-game` | `pad.press(button="Down", ticks=4)` | `ui.focused == play.pause.quit-game` within 8 ticks | `ui.focused(is="play.pause.quit-game")` | P4-03: Down on the last row does not wrap and is not a dead end that moves the ring away. | all | major | automated |
| `pause.left-contained` | under-test | `ui.focused == play.pause.quit-game` | `pad.press(button="Left", ticks=4)` | `ui.focused == play.pause.quit-game` within 8 ticks | `ui.focused(is="play.pause.quit-game")` | P4-03: Left keeps focus on the last row, no escape from W-P4. | all | major | automated |
| `pause.right-contained` | under-test | `ui.focused == play.pause.quit-game` | `pad.press(button="Right", ticks=4)` | `ui.focused == play.pause.quit-game` within 8 ticks | `ui.focused(is="play.pause.quit-game")` | P4-03: Right keeps focus on the last row, no escape from W-P4. | all | major | automated |
| `pause.up-to-settings` | under-test | `ui.focused == play.pause.quit-game` | `pad.press(button="Up", ticks=4)` | `ui.focused == play.pause.settings` within 8 ticks | `ui.focused(is="play.pause.settings")` | P4-03: D-pad Up from quit-game reaches settings. | all | major | automated |
| `pause.up-to-cheats` | under-test | `ui.focused == play.pause.settings` | `pad.press(button="Up", ticks=4)` | `ui.focused == play.pause.cheats` within 8 ticks | `ui.focused(is="play.pause.cheats")` | P4-03: D-pad Up from settings reaches cheats. | all | major | automated |
| `pause.up-to-enhancements` | under-test | `ui.focused == play.pause.cheats` | `pad.press(button="Up", ticks=4)` | `ui.focused == play.pause.enhancements` within 8 ticks | `ui.focused(is="play.pause.enhancements")` | P4-03: D-pad Up from cheats reaches enhancements. | all | major | automated |
| `pause.up-to-pack` | under-test | `ui.focused == play.pause.enhancements` | `pad.press(button="Up", ticks=4)` | `ui.focused == play.pause.pack` within 8 ticks | `ui.focused(is="play.pause.pack")` | P4-03: D-pad Up from enhancements reaches pack. | all | major | automated |
| `pause.up-to-save-states` | under-test | `ui.focused == play.pause.pack` | `pad.press(button="Up", ticks=4)` | `ui.focused == play.pause.save-states` within 8 ticks | `ui.focused(is="play.pause.save-states")` | P4-03: D-pad Up from pack reaches save-states. | all | major | automated |
| `pause.up-to-resume` | under-test | `ui.focused == play.pause.save-states` | `pad.press(button="Up", ticks=4)` | `ui.focused == play.pause.resume` within 8 ticks | `ui.focused(is="play.pause.resume")` | P4-03: D-pad Up from save-states reaches resume. | all | major | automated |
| `pause.up-no-wrap-header` | under-test | `ui.focused == play.pause.resume` | `pad.press(button="Up", ticks=4)` | `ui.focused == play.pause.resume` within 8 ticks | `ui.focused(is="play.pause.resume")` | P4-03: Up on Resume stays on Resume: no wrap into the header. | all | major | automated |
| `pause.ring-visible-each-row` | under-test | `ui.screen == play.pause` | — | — | — | P4-03: the ring is visible on every row through the walk; judged by eye. | all | major | manual |
| `pause.footer-follows-pad` | under-test | `ui.screen == play.pause and second pad connected` | — | — | — | P4-04 (pass 3): switching to the other pad mid-overlay and pressing any direction makes the footer re-read for the pad now in hand (ADR-0256 Decision 6); needs two pads. | all | major | manual |
| `pause.save-states-focus` | setup | `ui.screen == play.pause` | `nav.goal(goal="ui.focused == play.pause.save-states")` | `ui.focused == play.pause.save-states` within 8 ticks | `ui.focused(is="play.pause.save-states")` | P4-05 (setup): focus Save states. | all | major | automated |
| `pause.save-states-opens` | under-test | `ui.focused == play.pause.save-states` | `pad.press(button="A", ticks=4)` | `ui.screen == play.save-states` within 120 ticks | `ui.screen(is="play.save-states")` | P4-05: A on Save states opens the Save states sheet. | all | major | automated |
| `pause.save-states-slot-roundtrip` | under-test | `ui.screen == play.save-states` | — | — | — | P4-05: walk the slot grid, save to a slot, load it, all with D-pad + A; Shared replays... (if shown) opens and B backs out. | all | major | manual |
| `pause.save-states-b-to-pause` | under-test | `ui.screen == play.save-states` | `pad.press(button="B", ticks=4)` | `ui.screen == play.pause` within 120 ticks | `ui.screen(is="play.pause")` | P4-05: B closes back to W-P4, not to the game. | all | major | automated |
| `pause.pack-focus` | setup | `ui.screen == play.pause` | `nav.goal(goal="ui.focused == play.pause.pack")` | `ui.focused == play.pause.pack` within 8 ticks | `ui.focused(is="play.pause.pack")` | P4-06 (setup): focus Pack. | all | major | automated |
| `pause.pack-opens` | under-test | `ui.focused == play.pause.pack and fixture pack` | `pad.press(button="A", ticks=4)` | `ui.visible == play.pack` within 120 ticks | `ui.visible(is="play.pack")` | P4-06: with one pack, A on Pack opens the pack detail (W-P6). | all | major | automated |
| `pause.pack-detail-controls` | under-test | `ui.visible == play.pack` | — | — | — | P4-06: with one pack the detail (W-P6) shows Done, Show pack folder, Details. | all | minor | manual |
| `pause.pack-show-folder-gap` | under-test | `ui.visible == play.pack` | — | — | — | KNOWN GAP (P4-06): Show pack folder is expected FAIL; Finder takes the foreground and the pad cannot bring MesenAI back. PASS only if the pad still drives Play afterwards with no keyboard or mouse. The hook cannot observe the OS foreground. | all | major | manual |
| `pause.pack-b-to-pause` | under-test | `ui.visible == play.pack` | `pad.press(button="B", ticks=4)` | `ui.focused == play.pause.pack` within 120 ticks | `ui.focused(is="play.pause.pack")` | P4-06: B closes back to W-P4; focus returns to play.pause.pack. | all | major | automated |
| `pause.enh-focus` | setup | `ui.screen == play.pause` | `nav.goal(goal="ui.focused == play.pause.enhancements")` | `ui.focused == play.pause.enhancements` within 8 ticks | `ui.focused(is="play.pause.enhancements")` | P4-07 (setup): focus Enhancements. | all | major | automated |
| `pause.enh-opens` | under-test | `ui.focused == play.pause.enhancements` | `pad.press(button="A", ticks=4)` | `ui.screen == play.enhancements` within 120 ticks | `ui.screen(is="play.enhancements")` | P4-07: A on Enhancements opens the sheet. | all | major | automated |
| `pause.enh-toggle-apply` | under-test | `ui.screen == play.enhancements` | — | — | — | P4-07: a switch toggles by A; the footer label (Done/Apply/Apply & Reload) follows the draft; applying with a reload returns to the game or W-P4 with no mouse. | all | major | manual |
| `pause.enh-b-discards` | under-test | `ui.screen == play.enhancements` | `pad.press(button="B", ticks=4)` | `ui.screen == play.pause` within 120 ticks | `ui.screen(is="play.pause")` | P4-07: B discards back to W-P4. | all | major | automated |
| `pause.cheats-focus` | setup | `ui.screen == play.pause` | `nav.goal(goal="ui.focused == play.pause.cheats")` | `ui.focused == play.pause.cheats` within 8 ticks | `ui.focused(is="play.pause.cheats")` | P4-08 (setup): focus Cheats. | all | major | automated |
| `pause.cheats-opens` | under-test | `ui.focused == play.pause.cheats` | `pad.press(button="A", ticks=4)` | `ui.screen == play.cheats` within 120 ticks | `ui.screen(is="play.cheats")` | P4-08: A on Cheats opens the sheet. | all | major | automated |
| `pause.cheats-add-focus` | setup | `ui.screen == play.cheats` | `nav.goal(goal="ui.focused == play.cheats.add-code")` | `ui.focused == play.cheats.add-code` within 120 ticks | `ui.focused(is="play.cheats.add-code")` | P4-08 (setup): focus Add a Code. | all | major | automated |
| `pause.cheats-add-keyboard` | under-test | `ui.focused == play.cheats.add-code` | `pad.press(button="A", ticks=4)` | `ui.visible == play.keyboard` within 120 ticks | `ui.visible(is="play.keyboard")` | P4-08: A on Add a Code opens the on-screen keyboard (code-shaped field, ADR-0262 Decision 2). | all | major | automated |
| `pause.cheats-code-keyboard-letters` | under-test | `ui.visible == play.keyboard` | — | — | — | P4-08: the code keyboard shows APZLGITYEOXUKSVN (Game Genie letters) first; the description field is free text; commit works. | all | minor | manual |
| `pause.cheats-field-b-cancels` | under-test | `ui.visible == play.keyboard` | `pad.press(button="B", ticks=4)` | `ui.focused == play.cheats.add-code` within 8 ticks | `ui.focused(is="play.cheats.add-code")` | P4-08: B cancels a field without closing the sheet; focus returns to play.cheats.add-code. | all | major | automated |
| `pause.cheats-api-key-mask` | under-test | `ui.screen == play.cheats` | — | — | — | P4-08: the API key field (if visible) masks its draft as the bullet character. | all | minor | manual |
| `pause.cheats-b-to-pause` | under-test | `ui.screen == play.cheats` | `pad.press(button="B", ticks=4)` | `ui.screen == play.pause` within 120 ticks | `ui.screen(is="play.pause")` | P4-08: B closes the sheet back to W-P4. | all | major | automated |
| `pause.quit-focus` | setup | `ui.screen == play.pause` | `nav.goal(goal="ui.focused == play.pause.quit-game")` | `ui.focused == play.pause.quit-game` within 8 ticks | `ui.focused(is="play.pause.quit-game")` | P4-09 (setup): focus Quit game. | all | major | automated |
| `pause.quit-confirm-in-place` | under-test | `ui.focused == play.pause.quit-game` | `pad.press(button="A", ticks=4)` | `ui.visible == play.pause.quit-confirm` within 120 ticks | `ui.visible(is="play.pause.quit-confirm")` | P4-09: A on Quit game shows the confirm in place on W-P4, reachable. | all | major | automated |
| `pause.quit-b-keeps-playing` | under-test | `ui.visible == play.pause.quit-confirm` | `pad.press(button="B", ticks=4)` | `ui.focused == play.pause.quit-game` within 8 ticks | `ui.focused(is="play.pause.quit-game")` | P4-09: B on the confirm dismisses it and stays on W-P4 (keep playing); focus returns to play.pause.quit-game. | all | major | automated |
| `pause.quit-reconfirm` | under-test | `ui.focused == play.pause.quit-game` | `pad.press(button="A", ticks=4)` | `ui.visible == play.pause.quit-confirm` within 120 ticks | `ui.visible(is="play.pause.quit-confirm")` | P4-09: A again shows the confirm. | all | major | automated |
| `pause.quit-confirm-lands-home` | under-test | `ui.visible == play.pause.quit-confirm` | `pad.press(button="A", ticks=4)` | `ui.screen == play.home` within 600 ticks | `ui.screen(is="play.home")` | P4-09: confirming powers the game off and lands on Home. | all | major | automated |
| `pause.quit-home-ring` | under-test | `ui.screen == play.home` | — | `ui.focused == play.home.open-rom` within 120 ticks | `ui.focused(is="play.home.open-rom")` | P4-09: the ring is placed on Home's first control. | all | major | automated |

## Batch `settings`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `settings.reach-pause` | setup | `fixture rom and fixture settings.profiles.fresh` | `nav.goal(goal="ui.focused == play.pause.settings")` | `ui.focused == play.pause.settings` within 600 ticks | — | W-P4 is open with Settings focused (the SET-01 precondition, from P4-01). | all | major | automated |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `settings.a-opens` | under-test | `ui.focused == play.pause.settings` | `pad.press(button="A", ticks=4)` | `ui.screen == play.settings` within 120 ticks | `ui.screen(is="play.settings")` | SET-01: A on Settings opens W-P8. | all | major | automated |
| `settings.tab-display-first` | under-test | `ui.screen == play.settings` | — | `ui.focused == play.settings.tab-display` within 8 ticks | `ui.focused(is="play.settings.tab-display")` | SET-01: the sheet opens on the Display tab. | all | major | automated |
| `settings.display-down-into-page` | under-test | `ui.focused == play.settings.tab-display` | `pad.press(button="Down", ticks=4)` | within 8 ticks | `ui.focused(within="play.settings.page")` | SET-01: Down from the display tab enters its page. | all | major | automated |
| `settings.display-done-reachable` | under-test | `ui.screen == play.settings` | `pad.press(button="Down", ticks=40)` | `ui.focused == play.settings.done` within 8 ticks | `ui.focused(is="play.settings.done")` | SET-01: Done is reachable on the display page. Premise: held Down repeats at 400 ms then every 100 ms (~17 moves in 40 ticks), the Settings vertical chain does not wrap, and Done is its last row. | all | major | automated |
| `settings.display-up-page` | under-test | `ui.focused == play.settings.done` | `pad.press(button="Up", ticks=4)` | within 8 ticks | `ui.focused(within="play.settings.page")` | SET-01: Up from Done on display goes to the page (no More in Options on this tab). | all | major | automated |
| `settings.display-back-to-tab` | setup | `ui.screen == play.settings` | `nav.goal(goal="ui.focused == play.settings.tab-display")` | `ui.focused == play.settings.tab-display` within 120 ticks | `ui.focused(is="play.settings.tab-display")` | SET-01 (setup): back to the display tab for the next step. | all | major | automated |
| `settings.tab-right-look` | under-test | `ui.focused == play.settings.tab-display` | `pad.press(button="Right", ticks=4)` | `ui.focused == play.settings.tab-look` within 8 ticks | `ui.focused(is="play.settings.tab-look")` | SET-01: Right on the tab strip reaches look. | all | major | automated |
| `settings.tab-left-display` | under-test | `ui.focused == play.settings.tab-look` | `pad.press(button="Left", ticks=4)` | `ui.focused == play.settings.tab-display` within 8 ticks | `ui.focused(is="play.settings.tab-display")` | SET-01: Left on the tab strip goes back to display. | all | major | automated |
| `settings.tab-right-look-again` | setup | `ui.focused == play.settings.tab-display` | `pad.press(button="Right", ticks=4)` | `ui.focused == play.settings.tab-look` within 8 ticks | `ui.focused(is="play.settings.tab-look")` | SET-01 (setup): back to the look tab for the next step. | all | major | automated |
| `settings.look-down-into-page` | under-test | `ui.focused == play.settings.tab-look` | `pad.press(button="Down", ticks=4)` | within 8 ticks | `ui.focused(within="play.settings.page")` | SET-01: Down from the look tab enters its page. | all | major | automated |
| `settings.look-done-reachable` | under-test | `ui.screen == play.settings` | `pad.press(button="Down", ticks=40)` | `ui.focused == play.settings.done` within 8 ticks | `ui.focused(is="play.settings.done")` | SET-01: Done is reachable on the look page. Premise: held Down repeats at 400 ms then every 100 ms (~17 moves in 40 ticks), the Settings vertical chain does not wrap, and Done is its last row. | all | major | automated |
| `settings.look-up-page` | under-test | `ui.focused == play.settings.done` | `pad.press(button="Up", ticks=4)` | within 8 ticks | `ui.focused(within="play.settings.page")` | SET-01: Up from Done on look goes to the page (no More in Options on this tab). | all | major | automated |
| `settings.look-back-to-tab` | setup | `ui.screen == play.settings` | `nav.goal(goal="ui.focused == play.settings.tab-look")` | `ui.focused == play.settings.tab-look` within 120 ticks | `ui.focused(is="play.settings.tab-look")` | SET-01 (setup): back to the look tab for the next step. | all | major | automated |
| `settings.tab-right-audio` | under-test | `ui.focused == play.settings.tab-look` | `pad.press(button="Right", ticks=4)` | `ui.focused == play.settings.tab-audio` within 8 ticks | `ui.focused(is="play.settings.tab-audio")` | SET-01: Right on the tab strip reaches audio. | all | major | automated |
| `settings.audio-down-into-page` | under-test | `ui.focused == play.settings.tab-audio` | `pad.press(button="Down", ticks=4)` | within 8 ticks | `ui.focused(within="play.settings.page")` | SET-01: Down from the audio tab enters its page. | all | major | automated |
| `settings.audio-done-reachable` | under-test | `ui.screen == play.settings` | `pad.press(button="Down", ticks=40)` | `ui.focused == play.settings.done` within 8 ticks | `ui.focused(is="play.settings.done")` | SET-01: Done is reachable on the audio page. Premise: held Down repeats at 400 ms then every 100 ms (~17 moves in 40 ticks), the Settings vertical chain does not wrap, and Done is its last row. | all | major | automated |
| `settings.audio-up-more` | under-test | `ui.focused == play.settings.done` | `pad.press(button="Up", ticks=4)` | `ui.focused == play.settings.more-in-options` within 8 ticks | `ui.focused(is="play.settings.more-in-options")` | SET-01: Up from Done on audio reaches More in Options. | all | major | automated |
| `settings.audio-up-page` | under-test | `ui.focused == play.settings.more-in-options` | `pad.press(button="Up", ticks=4)` | within 8 ticks | `ui.focused(within="play.settings.page")` | SET-01: Up again from More in Options reaches the audio page. | all | major | automated |
| `settings.audio-back-to-tab` | setup | `ui.screen == play.settings` | `nav.goal(goal="ui.focused == play.settings.tab-audio")` | `ui.focused == play.settings.tab-audio` within 120 ticks | `ui.focused(is="play.settings.tab-audio")` | SET-01 (setup): back to the audio tab for the next step. | all | major | automated |
| `settings.tab-right-controls` | under-test | `ui.focused == play.settings.tab-audio` | `pad.press(button="Right", ticks=4)` | `ui.focused == play.settings.tab-controls` within 8 ticks | `ui.focused(is="play.settings.tab-controls")` | SET-01: Right on the tab strip reaches controls. | all | major | automated |
| `settings.controls-down-into-page` | under-test | `ui.focused == play.settings.tab-controls` | `pad.press(button="Down", ticks=4)` | within 8 ticks | `ui.focused(within="play.settings.page")` | SET-01: Down from the controls tab enters its page. | all | major | automated |
| `settings.controls-done-reachable` | under-test | `ui.screen == play.settings` | `pad.press(button="Down", ticks=40)` | `ui.focused == play.settings.done` within 8 ticks | `ui.focused(is="play.settings.done")` | SET-01: Done is reachable on the controls page. Premise: held Down repeats at 400 ms then every 100 ms (~17 moves in 40 ticks), the Settings vertical chain does not wrap, and Done is its last row. | all | major | automated |
| `settings.controls-up-more` | under-test | `ui.focused == play.settings.done` | `pad.press(button="Up", ticks=4)` | `ui.focused == play.settings.more-in-options` within 8 ticks | `ui.focused(is="play.settings.more-in-options")` | SET-01: Up from Done on controls reaches More in Options. | all | major | automated |
| `settings.controls-up-page` | under-test | `ui.focused == play.settings.more-in-options` | `pad.press(button="Up", ticks=4)` | within 8 ticks | `ui.focused(within="play.settings.page")` | SET-01: Up again from More in Options reaches the controls page. | all | major | automated |
| `settings.controls-back-to-tab` | setup | `ui.screen == play.settings` | `nav.goal(goal="ui.focused == play.settings.tab-controls")` | `ui.focused == play.settings.tab-controls` within 120 ticks | `ui.focused(is="play.settings.tab-controls")` | SET-01 (setup): back to the controls tab for the next step. | all | major | automated |
| `settings.tab-right-system` | under-test | `ui.focused == play.settings.tab-controls` | `pad.press(button="Right", ticks=4)` | `ui.focused == play.settings.tab-system` within 8 ticks | `ui.focused(is="play.settings.tab-system")` | SET-01: Right on the tab strip reaches system. | all | major | automated |
| `settings.system-down-into-page` | under-test | `ui.focused == play.settings.tab-system` | `pad.press(button="Down", ticks=4)` | within 8 ticks | `ui.focused(within="play.settings.page")` | SET-01: Down from the system tab enters its page. | all | major | automated |
| `settings.system-done-reachable` | under-test | `ui.screen == play.settings` | `pad.press(button="Down", ticks=40)` | `ui.focused == play.settings.done` within 8 ticks | `ui.focused(is="play.settings.done")` | SET-01: Done is reachable on the system page. Premise: held Down repeats at 400 ms then every 100 ms (~17 moves in 40 ticks), the Settings vertical chain does not wrap, and Done is its last row. | all | major | automated |
| `settings.system-up-page` | under-test | `ui.focused == play.settings.done` | `pad.press(button="Up", ticks=4)` | within 8 ticks | `ui.focused(within="play.settings.page")` | SET-01: Up from Done on system goes to the page (no More in Options on this tab). | all | major | automated |
| `settings.system-back-to-tab` | setup | `ui.screen == play.settings` | `nav.goal(goal="ui.focused == play.settings.tab-system")` | `ui.focused == play.settings.tab-system` within 120 ticks | `ui.focused(is="play.settings.tab-system")` | SET-01 (setup): back to the system tab for the next step. | all | major | automated |
| `settings.exit-fullscreen-done-focus` | setup | `ui.screen == play.settings` | `nav.goal(goal="ui.focused == play.settings.done")` | `ui.focused == play.settings.done` within 120 ticks | `ui.focused(is="play.settings.done")` | SET-01 (setup): focus Done. | all | major | automated |
| `settings.exit-fullscreen-left-of-done` | under-test | `ui.focused == play.settings.done` | `pad.press(button="Left", ticks=4)` | `ui.focused == play.settings.exit-fullscreen` within 8 ticks | `ui.focused(is="play.settings.exit-fullscreen")` | SET-01: Exit full screen is Left of Done, not above it (FS-02). | window.mode=fullscreen | major | automated |
| `settings.size-focus` | setup | `ui.screen == play.settings` | `nav.goal(goal="ui.focused == play.settings.interface-size")` | `ui.focused == play.settings.interface-size` within 120 ticks | `ui.focused(is="play.settings.interface-size")` | SET-02 (setup): focus Interface size on the Display tab. | all | major | automated |
| `settings.size-right-large` | under-test | `ui.focused == play.settings.interface-size` | `pad.press(button="Right", ticks=4)` | `ui.focused == play.settings.interface-size` within 8 ticks | `ui.focused(is="play.settings.interface-size")` | SET-02: Right steps the value in place to Large; the focus stays on the row. | all | major | automated |
| `settings.size-right-xlarge` | under-test | `ui.focused == play.settings.interface-size` | `pad.press(button="Right", ticks=4)` | `ui.focused == play.settings.interface-size` within 8 ticks | `ui.focused(is="play.settings.interface-size")` | SET-02: Right steps the value in place to Extra large; the focus stays on the row. | all | major | automated |
| `settings.size-xlarge-done` | under-test | `ui.focused == play.settings.interface-size` | `pad.press(button="Down", ticks=60)` | `ui.focused == play.settings.done` within 8 ticks | `ui.focused(is="play.settings.done")` | SET-02: at Extra large Done stays pinned and reachable (ADR-0269 Decision 6). Premise: held Down repeats at 400 ms then every 100 ms (~17 moves in 40 ticks, so this step holds 60 ticks for the taller Extra large chain), the Settings vertical chain does not wrap, and Done is its last row. | all | major | automated |
| `settings.size-back-to-row` | setup | `ui.focused == play.settings.done` | `nav.goal(goal="ui.focused == play.settings.interface-size")` | `ui.focused == play.settings.interface-size` within 120 ticks | `ui.focused(is="play.settings.interface-size")` | SET-02 (setup): back to Interface size. | all | major | automated |
| `settings.size-left-standard` | under-test | `ui.focused == play.settings.interface-size` | `pad.press(button="Left", ticks=4)` | `ui.focused == play.settings.interface-size` within 8 ticks | `ui.focused(is="play.settings.interface-size")` | SET-02: Left steps the value back toward Standard in place. | all | major | automated |
| `settings.size-left-standard-2` | under-test | `ui.focused == play.settings.interface-size` | `pad.press(button="Left", ticks=4)` | `ui.focused == play.settings.interface-size` within 8 ticks | `ui.focused(is="play.settings.interface-size")` | SET-02: the second Left reaches Standard; the focus stays on the row. | all | major | automated |
| `settings.size-standard-done` | under-test | `ui.focused == play.settings.interface-size` | `pad.press(button="Down", ticks=40)` | `ui.focused == play.settings.done` within 8 ticks | `ui.focused(is="play.settings.done")` | SET-02: at Standard Done is reachable. Premise: held Down repeats at 400 ms then every 100 ms (~17 moves in 40 ticks), the Settings vertical chain does not wrap, and Done is its last row. | all | major | automated |
| `settings.size-applies-scrolls` | under-test | `ui.screen == play.settings` | — | — | — | SET-02: each value applies at once and only Play chrome scales, never the game picture; at Extra large the rows scroll with nothing clipped (guaranteed at 1024x640 and 512x505); record legibility at couch distance per size in the rubric. The hook exposes no values or geometry. | all | major | manual |
| `settings.menu-sounds-row` | under-test | `ui.screen == play.settings` | — | — | — | SET-03: Menu sounds is visible when the host audio path exists (ADR-0270, #1126) and hidden otherwise; record which. | all | minor | manual |
| `settings.rumble-focus` | setup | `ui.screen == play.settings` | `nav.goal(goal="ui.focused == play.settings.rumble")` | `ui.focused == play.settings.rumble` within 120 ticks | `ui.focused(is="play.settings.rumble")` | SET-04 (setup): focus the Rumble slider on the Controls tab. | all | major | automated |
| `settings.rumble-left` | under-test | `ui.focused == play.settings.rumble` | `pad.press(button="Left", ticks=4)` | `ui.focused == play.settings.rumble` within 8 ticks | `ui.focused(is="play.settings.rumble")` | SET-04: Left steps Rumble toward 0 in place. | all | major | automated |
| `settings.rumble-right` | under-test | `ui.focused == play.settings.rumble` | `pad.press(button="Right", ticks=4)` | `ui.focused == play.settings.rumble` within 8 ticks | `ui.focused(is="play.settings.rumble")` | SET-04: Right steps Rumble back up in place. | all | major | automated |
| `settings.menu-tick` | under-test | `ui.focused == play.settings.rumble` | — | — | — | SET-04: Menu tick shows only when the core says the pad is aimable (MenuTickAvailable; macOS with a rumble-capable pad); with Rumble 0 the switch is disabled with the reason text; on, a short tick on each focus move, never while a game runs unpaused (HapticTickRule.ShouldTickOnMove). On a pad without rumble mark N/A, not FAIL. | all | major | manual |
| `settings.deadzone-focus` | setup | `ui.focused == play.settings.rumble` | `nav.goal(goal="ui.focused == play.settings.deadzone")` | `ui.focused == play.settings.deadzone` within 120 ticks | `ui.focused(is="play.settings.deadzone")` | SET-05 (setup): focus the Deadzone slider. | all | major | automated |
| `settings.deadzone-left` | under-test | `ui.focused == play.settings.deadzone` | `pad.press(button="Left", ticks=4)` | `ui.focused == play.settings.deadzone` within 8 ticks | `ui.focused(is="play.settings.deadzone")` | SET-05: Left steps the Deadzone slider in place. | all | major | automated |
| `settings.deadzone-right` | under-test | `ui.focused == play.settings.deadzone` | `pad.press(button="Right", ticks=4)` | `ui.focused == play.settings.deadzone` within 8 ticks | `ui.focused(is="play.settings.deadzone")` | SET-05: Right steps the Deadzone slider in place. | all | major | automated |
| `settings.deadzone-readout-drift` | under-test | `ui.focused == play.settings.deadzone` | — | — | — | SET-05: the value readout follows the slider; stick drift must not move the menu cursor (the bridge reads D-pad names only, see PAD-03). The hook reads no values or stick axes. | all | minor | manual |
| `settings.box-art-focus` | setup | `ui.screen == play.settings` | `nav.goal(goal="ui.focused == play.settings.download-box-art")` | `ui.focused == play.settings.download-box-art` within 120 ticks | `ui.focused(is="play.settings.download-box-art")` | SET-06 (setup): focus Download box art on the System tab. | all | major | automated |
| `settings.box-art-change` | under-test | `ui.focused == play.settings.download-box-art` | `pad.press(button="A", ticks=4)` | `ui.focused == play.settings.download-box-art` within 8 ticks | `ui.focused(is="play.settings.download-box-art")` | SET-06: A changes Download box art by pad; the focus stays on the row. | all | major | automated |
| `settings.box-art-change-back` | under-test | `ui.focused == play.settings.download-box-art` | `pad.press(button="A", ticks=4)` | `ui.focused == play.settings.download-box-art` within 8 ticks | `ui.focused(is="play.settings.download-box-art")` | SET-06: A changes it back; the focus stays on the row. | all | major | automated |
| `settings.system-no-native-dialog` | under-test | `ui.screen == play.settings` | — | — | — | KNOWN GAP: SET-06: no native OS dialog opens on the System tab. A native panel is not an Avalonia control, so the hook's dialogs list (app sheets, ADR-0272 §3) cannot see it; verify by eye or with the vision fallback. | all | major | manual |
| `settings.system-rows-restart` | under-test | `ui.screen == play.settings` | — | — | — | SET-06: every System row (storage, keyboard preset, ...) is a pad-drivable control; a storage change offers a restart rather than pretending (ADR-0256 Decision 8). | all | major | manual |
| `settings.b-closes-to-pause` | under-test | `ui.screen == play.settings` | `pad.press(button="B", ticks=4)` | `ui.screen == play.pause` within 120 ticks | `ui.screen(is="play.pause")` | SET-07: B closes the sheet back to W-P4. | all | major | automated |
| `settings.changes-kept` | under-test | `ui.screen == play.pause` | — | — | — | SET-07: the changes made on the tabs are kept after B; the hook reads no setting values. | all | major | manual |

## Batch `pause-pack-multi`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `pause-pack-multi.reach-game` | setup | `fixture rom, fixture pack-multi and fixture settings.profiles.fresh` | `nav.goal(goal="ui.screen == play.game")` | `ui.screen == play.game` within 600 ticks | — | A game is running (the P4-01 precondition). | all | major | automated |
| `pause-pack-multi.chord-opens` | setup | `ui.screen == play.game` | `pad.chord(buttons=["Select", "Start"], frames=4)` | `ui.screen == play.pause` within 120 frames | `ui.screen(is="play.pause")` | W-P4 is open over the running game (setup, from P4-01). | all | major | automated |
| `pause-pack-multi.pack-focus` | setup | `ui.screen == play.pause and fixture pack-multi` | `nav.goal(goal="ui.focused == play.pause.pack")` | `ui.focused == play.pause.pack` within 8 ticks | `ui.focused(is="play.pause.pack")` | P4-06 (setup): focus Pack with 2+ installed packs. | all | major | automated |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `pause-pack-multi.pack-picker-opens` | under-test | `ui.focused == play.pause.pack and fixture pack-multi` | `pad.press(button="A", ticks=4)` | `ui.visible == play.pack` within 120 ticks | `ui.visible(is="play.pack")` | P4-06: with 2+ packs A on Pack opens the picker W-P5. | all | major | automated |
| `pause-pack-multi.pack-picker-controls` | under-test | `ui.visible == play.pack` | — | — | — | P4-06: the picker (W-P5) has radios by D-pad, Use This Pack and Cancel. | all | minor | manual |
| `pause-pack-multi.pack-picker-b-to-pause` | under-test | `ui.visible == play.pack` | `pad.press(button="B", ticks=4)` | `ui.focused == play.pause.pack` within 120 ticks | `ui.focused(is="play.pause.pack")` | P4-06: B closes the picker back to W-P4; focus returns to play.pause.pack. | all | major | automated |

## Batch `settings-menu-sounds`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `settings-menu-sounds.reach-audio` | setup | `fixture rom and fixture settings.profiles.fresh` | `nav.goal(goal="ui.focused == play.settings.tab-audio")` | `ui.focused == play.settings.tab-audio` within 600 ticks | — | Settings is open on the Audio tab (the SET-03 precondition). | all | major | automated |
| `settings-menu-sounds.menu-sounds-focus` | setup | `ui.screen == play.settings and ui.visible == play.settings.menu-sounds` | `nav.goal(goal="ui.focused == play.settings.menu-sounds")` | `ui.focused == play.settings.menu-sounds` within 120 ticks | `ui.focused(is="play.settings.menu-sounds")` | SET-03: focus Menu sounds on the Audio tab; if the host has no Menu sounds row this step fails and stops only this batch. | all | major | automated |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `settings-menu-sounds.menu-sounds-toggle` | under-test | `ui.focused == play.settings.menu-sounds` | `pad.press(button="A", ticks=4)` | `ui.focused == play.settings.menu-sounds` within 8 ticks | `ui.focused(is="play.settings.menu-sounds")` | SET-03: A toggles Menu sounds on; the focus stays on the row. | all | major | automated |
| `settings-menu-sounds.menu-sounds-blips` | under-test | `ui.focused == play.settings.menu-sounds` | — | — | — | SET-03: a soft blip on move, confirm and back at a fixed low level, never while a game runs unpaused (MenuSounds.ShouldPlay); the game's own audio is unaffected (ADR-0270). Judged by ear. | all | major | manual |

## Batch `controller`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `ctl.reach-more-in-options` | setup | `fixture rom and fixture settings.profiles.fresh` | `nav.goal(goal="ui.focused == play.settings.more-in-options")` | `ui.focused == play.settings.more-in-options` within 600 ticks | — | Settings is open on the Controls tab with More in Options focused (the CTL-01 precondition: SET-01, Controls tab, game loaded). | all | major | automated |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `ctl.open-sheet` | under-test | `ui.focused == play.settings.more-in-options` | `pad.press(button="A", ticks=4)` | `ui.screen == play.controller` within 120 ticks | `ui.screen(is="play.controller")` | CTL-01: A on More in Options opens the Play Controller sheet over the paused game, not the classic ConfigWindow. | all | major | automated |
| `ctl.sheet-holds-focus` | under-test | `ui.screen == play.controller` | — | within 4 ticks | `ui.focused(within="play.controller")` | CTL-01: the sheet holds the pad's focus (Done reachability is the next steps' under-test check, ctl.done-reachable-by-pad). | all | major | automated |
| `ctl.pad-drawing-lights` | under-test | `ui.screen == play.controller` | — | — | — | CTL-01: the live pad drawing lights the buttons you press; the hook does not read the drawing, judged by eye on a real pad. | all | minor | manual |
| `ctl.arm-row` | under-test | `ui.focused == play.controller.row-first` | `pad.press(button="A", ticks=4)` | `ui.visible == play.controller.capture-armed` within 120 ticks | `ui.visible(is="play.controller.capture-armed")` | CTL-02: A on a console-control row arms the capture. | all | major | automated |
| `ctl.arm-waits-release` | under-test | `ui.visible == play.controller.capture-armed` | — | — | — | CTL-02: arming waits for the first button to be released, then the next pad button pressed maps and lights the two lights (pad side / port side); needs a real pad. | all | major | manual |
| `ctl.nav-control-refused` | under-test | `ui.visible == play.controller.capture-armed` | — | — | — | CTL-02: pressing a navigation control (A, B or a D-pad direction) while armed is refused visibly (ADR-0256 Decision 4); the refusal is not exposed by the hook and A/B are the pad's own presses. | all | major | manual |
| `ctl.b-cancels-capture` | under-test | `ui.visible == play.controller.capture-armed` | `pad.press(button="B", ticks=4)` | `ui.visible != play.controller.capture-armed` within 8 ticks | `ui.visible(is="play.controller.capture-armed", visible=false)` | CTL-02: B cancels the capture: the armed prompt is gone (a refused B would leave it up); the pad regains authority (HasAuthority gains !IsControllerCapturing). | all | major | automated |
| `ctl.focus-stays-on-row-after-cancel` | under-test | `ui.screen == play.controller` | — | `ui.focused == play.controller.row-first` within 8 ticks | `ui.focused(is="play.controller.row-first")` | CTL-02: after the cancel the focus stays on the row that was armed. | all | major | automated |
| `ctl.sheet-stays-after-cancel` | under-test | `ui.focused == play.controller.row-first` | — | within 4 ticks | `ui.screen(is="play.controller")` | CTL-02: the sheet is still open and drivable after the cancelled capture. | all | major | automated |
| `ctl.done-reachable-by-pad` | under-test | `ui.screen == play.controller` | `nav.goal(goal="ui.focused == play.controller.done")` | `ui.focused == play.controller.done` within 600 ticks | `ui.focused(is="play.controller.done")` | CTL-01: Done is reachable by pad from the sheet. | all | major | automated |
| `ctl.bind-extra-button` | under-test | `ui.screen == play.controller` | — | — | — | CTL-04: bind a spare button (a paddle or the right stick click) to Rewind, B to the game, press it while playing: the binding takes and the action fires in game (engine third key set, ADR-0255 slice 4); navigation controls are not offered in the list. Needs a real pad with a spare button. | all | major | manual |

## Batch `controller-done`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `controller-done.reach` | setup | `fixture rom and fixture settings.profiles.fresh` | `nav.goal(goal="ui.focused == play.controller.done")` | `ui.focused == play.controller.done` within 600 ticks | — | The Controller sheet is open with Done focused (the CTL-03 precondition: CTL-01). | all | major | automated |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `controller-done.leaves` | under-test | `ui.focused == play.controller.done` | `pad.press(button="A", ticks=4)` | `ui.screen == play.pause` within 120 ticks | `ui.screen(is="play.pause")` | CTL-03: A on Done leaves the sheet to W-P4. | all | major | automated |
| `controller-done.pad-still-moves` | under-test | `ui.screen == play.pause` | `pad.press(button="Down", ticks=4)` | `ui.focused == play.pause.quit-game` within 8 ticks | `ui.focused(is="play.pause.quit-game")` | CTL-03: no capture stays armed after close; the pad still moves focus on W-P4. | all | major | automated |

## Batch `controller-b`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `controller-b.reach` | setup | `fixture rom and fixture settings.profiles.fresh` | `nav.goal(goal="ui.focused == play.controller.row-first")` | `ui.focused == play.controller.row-first` within 600 ticks | — | The Controller sheet is open with a row focused (the CTL-03 precondition: CTL-01). | all | major | automated |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `controller-b.leaves` | under-test | `ui.focused == play.controller.row-first` | `pad.press(button="B", ticks=4)` | `ui.screen == play.pause` within 120 ticks | `ui.screen(is="play.pause")` | CTL-03: B instead of Done leaves the sheet to W-P4. | all | major | automated |
| `controller-b.pad-still-moves` | under-test | `ui.screen == play.pause` | `pad.press(button="Down", ticks=4)` | `ui.focused == play.pause.quit-game` within 8 ticks | `ui.focused(is="play.pause.quit-game")` | CTL-03: no capture stays armed after close; the pad still moves focus on W-P4. | all | major | automated |

## Batch `controller-no-game`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `ctl-no-game.reach-home` | setup | `fixture settings.profiles.fresh` | `nav.goal(goal="ui.screen == play.home")` | `ui.screen == play.home` within 600 ticks | — | Home with no game loaded (the CTL-05 precondition). | all | major | automated |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `ctl.no-game-gap` | under-test | `ui.screen == play.home` | — | — | — | CTL-05: KNOWN GAP, expected FAIL. Settings is not reachable by pad with no game (HOME-06, bug #1177), so neither is the Controller sheet. With Settings reached by keyboard, Controls > More in Options opens the classic Options window's Input page because OpenControllerSheet() returns false with no game loaded (ADR-0256 Decision 5 note). Record what the player sees. | all | major | manual |

## Batch `lamps`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `lamps.reach-home` | setup | `fixture settings.profiles.fresh` | `nav.goal(goal="ui.screen == play.home")` | `ui.screen == play.home` within 600 ticks | — | Home with the pad port lamps on the status line (the LAMP-01 precondition: one pad). | all | major | automated |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `lamp.four-lamps-p1` | under-test | `ui.screen == play.home` | — | — | — | LAMP-01: the status line shows four lamps, P1 lit and the others dim; the number is the label and the pad name is the tooltip; the hook exposes neither lamps nor tooltips, judged by eye. | all | minor | manual |
| `lamp.second-pad-hotplug` | under-test | `ui.screen == play.home` | — | — | — | LAMP-02: plug a second pad in (or power it on over Bluetooth), wait 2 s, power it off: the second lamp lights within about 1 s (1 s poll) and dims again, nothing else moves, the ring stays where it was; the W-P15 pill "New controller …" on its first press is expected. A real plug is physical-only and is not simulated. | all | major | manual |

## Batch `lamps-game`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `lamps-game.reach-game` | setup | `fixture rom and fixture settings.profiles.fresh` | `nav.goal(goal="ui.screen == play.game")` | `ui.screen == play.game` within 600 ticks | — | A game is running unpaused (the LAMP-03 precondition). | all | major | automated |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `lamp.game-unpaused` | under-test | `ui.screen == play.game` | — | within 4 ticks | `ui.screen(is="play.game")` | LAMP-03: the game runs unpaused, the state in which the status line and lamps must be hidden. | all | minor | automated |
| `lamp.bar-hidden-in-game` | under-test | `ui.screen == play.game` | — | — | — | LAMP-03: the status bar and lamps are hidden while the game runs; a pack-install pill over a running game keeps the bar (sheetOpen), record only if observed; the hook does not read the bar. | all | minor | manual |

## Batch `loss`

Setup:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `loss.reach-game` | setup | `fixture rom and fixture settings.profiles.fresh` | `nav.goal(goal="ui.screen == play.game")` | `ui.screen == play.game` within 600 ticks | — | A game is running unpaused with PauseWhenInBackground at its default (on). | all | major | automated |

Steps:

| ID | Role | Precondition | Action | Wait | Check | Expected | Variants | Severity | Mode |
|---|---|---|---|---|---|---|---|---|---|
| `loss.focus-lost-pauses` | under-test | `ui.screen == play.game` | — | — | — | LOSS-01: another app takes focus: W-P4 opens with "Paused — …" naming the lost focus; regaining focus stays paused (ADR-0254 answer 1) and only Resume or the chord resumes. Taking focus by pad is not possible on macOS without a keyboard, so this is BLOCKED unless a second machine or remote switch exists; the keyboard (Cmd-Tab) may be used if noted. | all | major | manual |
| `loss.pad-unplug-pauses` | under-test | `ui.screen == play.game` | — | — | — | LOSS-02: unplug the pad, then replug it: W-P4 opens with "Paused — controller disconnected" (ADR-0254 amendment, always on in Play, not gated by PauseWhenInBackground); replugging rewrites the line to "Controller reconnected" and does not resume; the action bar names the pad still connected, or the keyboard with none. Then record whether the replugged pad resumes (chord or A on Resume). A real unplug is not simulated. | all | major | manual |
| `loss.pad-off-mid-menu` | under-test | `ui.screen == play.pause` | — | — | — | LOSS-03 (pass 3): with W-P4 open and a Bluetooth second pad, power it off, on again, press a direction: no second pause (already paused); on return the pad drives focus again and the footer names the right control. | all | minor | manual |
| `loss.pad-sleep` | under-test | `ui.screen == play.game` | — | — | — | LOSS-04 (pass 3): leave a Bluetooth pad idle until it sleeps (vendor timeout, typically 10-15 min), then wake it: sleep is a disconnect, W-P4 opens as in LOSS-02, and waking reconnects so the pad can navigate W-P4 and resume. Not covered by any repo test; observation only. | all | minor | manual |
