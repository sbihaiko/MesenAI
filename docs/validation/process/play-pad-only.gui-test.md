# play-pad-only

<!-- Rendered from the JSON script by gui_test_render.py. Do not edit; edit the JSON. -->

- Format: `gui-test/1`
- Target: `mesen-gui`
- Requires actions: `pad.press`, `pad.release`, `pad.chord`, `nav.goal`
- Requires checks: `ui.screen`, `ui.focused`, `ui.visible`, `ui.dialogs`
- Variant `window.mode`: `windowed`, `fullscreen`

## Fixtures

```json
{
  "note": "Control ids are provisional until the in-app AutomationIds land (#1182); steps use the HOME-01..06 wording of the manual script.",
  "rom": {
    "path": "<library>/Contra (USA).nes",
    "sha1": "<no-intro sha1 per ADR-0003, filled when the adapter lands>"
  },
  "settings": {
    "profiles": {
      "fresh": "no play history, no favorites",
      "history-one-favorite": "one game in play history (so Continue and Recent exist) and that game favorited (so Favorites exists, ADR-0268 Decision 4)"
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
| `home.settings-by-pad` | under-test | `ui.screen == play.home` | — | — | — | Start (Options on a DualShock) opens Settings from the home with no game loaded; the bar reads Start Settings; Home's layout is unchanged (W-P1 one control, W-P2 two besides the tiles, ADR-0241). Bug #1177. | all | major | manual |

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
