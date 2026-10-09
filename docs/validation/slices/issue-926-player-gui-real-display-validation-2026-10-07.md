# Issue #926 — Player GUI real-display validation (2026-10-07)

Refs #926. Validation only: nothing in the product was changed. Each real
failure is filed as a P2 bug and cited in its row.

Machine: macOS (Darwin 25.6.0, arm64), branch based on `origin/main` at
`2adbdcd9a`. Evidence lives under `docs/validation/evidence/issue-926/`.

## Vision proof

Every visual verdict below comes from reading the PNGs directly; no external
vision model was used. As proof that reading works, a literal string read from
`docs/media/gui-redesign/W-P1.png`: **"Drop a game here"**. The same image also
shows "Open a ROM...", "W-P1  Play — first run, no recent games" and
"1 control at rest".

## Verification outputs (verbatim)

RenderTests, after `make core` (exit 0) and **before** any `make ui` (#786):

```
MESEN_CORE_LIB=$PWD/InteropDLL/obj.osx-arm64/MesenCore.dylib MESEN_PLAYER_RENDERS=/tmp/926-renders \
  dotnet test UI.HeadlessTests/UI.HeadlessTests.csproj -p:RuntimeIdentifier=osx-arm64 \
  --filter "FullyQualifiedName~RenderTests"
Passed!  - Failed:     0, Passed:    61, Skipped:     0, Total:    61, Duration: 41 s - UI.HeadlessTests.dll (net10.0)
```

`python3 scripts/checks/verify_adr_refs.py` (exit 0):

```
PASS verify_adr_refs: every cited ADR-NNNN resolves to docs/adr/<area>/
```

## Verdicts

| Check | Verdict | Evidence | Method | Issue |
|---|---|---|---|---|
| 1 · W-P1 first run | pass | `docs/validation/evidence/issue-926/renders/W-P1.png` | headless render vs `docs/media/gui-redesign/W-P1.png` | — |
| 1 · W-P2 recents | pass | `.../renders/W-P2.png` | render vs wireframe | — |
| 1 · W-P4 pause sheet | pass | `.../renders/W-P4.png`, `.../renders/W-P4-save-states.png` | render vs wireframe | — |
| 1 · W-P5 pack sheet | pass | `.../renders/W-P5.png` | render vs wireframe | — |
| 1 · W-P6 pack details | pass | `.../renders/W-P6.png` | render vs wireframe | — |
| 1 · W-P7 Enhancements | fail | `.../renders/W-P7.png` | render vs wireframe | #1006 |
| 1 · W-P8 Settings | fail | `.../renders/W-P8.png` | render vs wireframe | #1006 |
| 1 · W-P10 Settings › Look | fail | `.../renders/W-P10.png` | render vs wireframe | #1006 |
| 1 · W-P11 Cheats | pass | `.../renders/W-P11.png` | render vs wireframe | — |
| 1 · W-P13 Quit Game | pass | `.../renders/W-P13.png`, `.../renders/W-P13-confirm.png` | render vs wireframe | — |
| 1 · W-P14 error strip | pass | `.../renders/W-P14.png` | render vs wireframe | — |
| 1 · W-P15 new controller | pass | `.../renders/W-P15.png`, `.../renders/W-P15-pill.png` | render vs wireframe | — |
| 1 · W-P16 | pass | `.../renders/W-P16.png` | render vs wireframe | — |
| 1 · W-S2 Tools ⋯ menu | fail | `.../renders/W-S2.png` | render vs wireframe | #1007 |
| 1 · W-S3 doors | pass | `.../renders/W-S3.png` | render vs wireframe | — |
| 1 · W-S1, W-P3, W-P8b, W-P8c, W-P9, W-P12 | not-automatable | — | no RenderTests case renders these screens, so there is nothing to compare | — |
| 2a · macOS title bar screenshot | not-automatable | — | `screencapture -x` exit 1: `could not create image from display` (Screen Recording not granted) | — |
| 2b · macOS app menu | fail | — (System Events text output, quoted below) | `osascript` System Events on the `make ui` app, no prompt | #1008 |
| 3a · Finder hand-off | pass | window title quoted below | `open -a .../Mesen.app "<rom>"` | — |
| 3b · drag-and-drop (headless) | pass | test names below | headless DragDrop/DropRoute tests | — |
| 3c · real OS drag | not-automatable | — | needs a pointer driven through Accessibility plus Screen Recording, neither available without a prompt | — |
| 4 · W.7 on a commercial GBA ROM | not-automatable | `docs/validation/evidence/issue-926/gba/` | temporary headless probe (not committed) on local GBA ROMs | — |
| 5a · real gamepad pass | not-automatable | — | needs a person holding a pad | — |
| 5b · P.13 Hold to Compare on Windows/Linux | not-automatable | — | macOS-only machine | — |
| 5c · P.8 stop condition (3) | not-automatable | — | the owner's 2026-10-05 decision keeps it a human check | — |

`.../` = `docs/validation/evidence/issue-926/`. Every render also has its
automated `<name>.wireframe.md` region report beside it. Those reports are
informational: `PlayerRender.Save` never fails a test over them.

## Check 1 — §13.5 look, divergences per wireframe

These apply to every render and are not counted as divergences:
- The renders are headless, so they lack the macOS traffic lights and the
  title bar.
- The headless focus ring draws as a black rectangle.
- A P1–P4 pad-port lamp strip from a later slice appears.
- Fixture values are seeded: names, counts, thumbnails and "empty"/"none".

- **W-P1:** layout, "Drop a game here", "Open a ROM…" and the 1 control
  match. The helper line reads "Enhanced audio is on.", a shortened variant
  that is chosen from the settings (intentional, `PlayHome.cs`).
- **W-P2:** layout and the 2 controls match. The seeded data shows 3 recents
  instead of 5, black thumbnails and solid continue art, with no
  "· Contra 80s 1.2" suffix.
- **W-P4:** all 7 controls match: the Save States, Pack, Enhancements, Cheats
  and Settings rows, Resume, Quit Game, plus "Esc to resume". The scrim is
  flat gray instead of a blurred game, because the fixture has no game frame,
  and the home's text bleeds through on the left.
- **W-P5:** the same 5 controls. It lacks the 👍 counts and the known-missing
  warning, and shows "1.0" instead of "validated Aug 30" (seeded).
- **W-P6:** near-identical. "Change Pack…" is disabled with "There is no
  other pack for this game." because there is a single seeded pack, which is
  consistent with the rules.
- **W-P7 (FAIL, #1006):** the footer reads "How the picture looks:
  Settings › Video", but the spec says "Settings › Look". "Done" in place of
  "Apply & Reload" is correct, since nothing is pending.
- **W-P8 (FAIL, #1006):** there are 5 tabs, "Window │ Video │ Audio │
  Controls │ System", where the spec has 4: "Display │ Look │ Audio │
  Controls". The footer reads "Everything else: Classic › Settings" instead of
  "Everything else: Tools ⋯ › Options".
- **W-P10 (FAIL, #1006):** the tab names diverge in the same way. The ART,
  PIXELS and SCREEN sections match. "Hold to Compare" is disabled ("Nothing to
  compare: Pixels and Screen are off"), as the seeded state predicts.
- **W-P11:** there are two extra rows: the intent search ("Or say what you
  want…" with Find) and the OpenRouter key row. Both are covered by the
  accepted ADR-0245 and ADR-0247 (P.11–P.12), so they are not a divergence.
- **W-P13:** matches. The backdrop is empty instead of the home.
- **W-P14:** the error strip matches. It sits over the W-P1 home, since no
  recents are seeded.
- **W-P15:** matches. The pill names the pad ("New controller "8BitDo SN30".
  Press Start on it to set it up.").
- **W-P16:** matches.
- **W-S2 (FAIL, #1007):** with no game loaded, Reset, Power Cycle and
  Screenshot render in enabled ink, but the wireframe grays them out. The
  ⌃⌘F shortcut on Fullscreen and the hint "Disk, coin and tape items appear
  when the game uses them." are missing.
- **W-S3:** matches: 4 doors with ⌘1–⌘4 and the footer.

These ids have a wireframe PNG but no render, so they could not be compared:
**W-S1** (its shell does appear around the other renders, but it has no render
of its own), **W-P3**, **W-P8b**, **W-P8c**, **W-P9** and **W-P12**.

Some renders were produced and kept as evidence but not compared, because they
are outside this Player pass: W-R0–W-R7 and W-R0b (Remaster), W-H1–W-H4 and
W-X1–W-X3. The other renders (rom-picker-*, replays, save-states-sheet,
slot-grid) have no wireframe id.

## Check 2 — macOS title bar and app menu

`make ui` was run only after check 1 (exit 0). The app tested was
`bin/osx-arm64/Release/osx-arm64/publish/Mesen.app`.

- **Screenshot:** `screencapture -x` → `could not create image from display`
  (exit 1). The row is not-automatable.
- **System Events:** `osascript` worked with no prompt (exit 0).
  - Menu bar: `Apple, Mesen`.
  - App menu items, in order: `missing value, Services, missing value, Hide
    Mesen, Hide Others, Show All, missing value, Quit, About MesenAI, missing
    value, Settings…`.
  - Command characters: Quit `Q`, Settings… `,`.
  - Window: `MesenAI - 1942 (1985) (Capcom)` (`AXStandardWindow`).
  - **FAIL (#1008):** the menu is named "Mesen" and shows "Hide Mesen", and
    About and Settings… come after Quit instead of first.
- The app was closed through the menu afterwards, and no process remained.

## Check 3 — Finder hand-off and drag-and-drop

- `open -a bin/osx-arm64/Release/osx-arm64/publish/Mesen.app ".../1942 (1985)
  (Capcom).nes"` exited 0. The window title became `MesenAI - 1942 (1985)
  (Capcom)`, which shows the ROM loaded. The title is the evidence: the
  view-model was not read from outside the process.
- Drag-and-drop is covered by the headless tests in
  `PlayDragDropRomTests` and the `DropRouteExtensionsTests` class (they ran
  together with `GbaWidescreenRevealTests` in one filter):
  - `A_rom_dropped_on_the_player_window_opens_it`
  - `A_patch_dropped_on_the_player_window_opens_the_rom_beside_it_patched`
  - `A_pack_archive_dropped_on_the_player_window_is_installed`
  - `A_pack_folder_dropped_on_the_player_window_is_installed`

  The run's result, verbatim:
  `Passed!  - Failed:     0, Passed:    14, Skipped:     0, Total:    14, Duration: 5 s`.
- A real OS drag is not-automatable here.

## Check 4 — W.7 on a GBA ROM

`mdfind -name .gba` found local commercial ROMs. A temporary, uncommitted
headless probe loaded each one through the real core and captured the frame
through `FrameCaptureApi`, first with the Widescreen switch on and then with
it off. The probe used the same set-up as `GbaWidescreenRevealTests`:
`SkipBootScreen`, `VideoFilter=None` and a temporary home folder. Raw output
is in `docs/validation/evidence/issue-926/gba/probe.txt`.

| ROM | Frames | Widescreen frame | Standard frame | What the image shows |
|---|---|---|---|---|
| Golden Sun | 600 | 284×160 | 240×160 | all black |
| Super Mario Advance 4 | 600 / 1800 / 3600 | 284×160 | 240×160 | blank near-white across all 284 px |
| Castlevania: Aria of Sorrow | 1800 / 3600 | 284×160 | 240×160 | blank near-white across all 284 px |

- **Confirmed** on three commercial cartridges:
  - they load as `Gba`;
  - the switch produces the 284×160 extended frame (22 px each side);
  - turning it off returns 240×160.
- **Not confirmed:** map content beside a real game picture. Every capture is
  a blank (black or forced-blank-white) screen, so the sides cannot be judged.
  Reading `Castlevania3600-widescreen.png` and `Super3600-widescreen.png` shows
  a uniform pale frame with no game picture.
- No input was scripted to get past the title or intro, so this is not
  evidence of a defect.

PRD Part B §8, W.7 currently reads: "**On-screen validation: headless,
2026-10-06 (#954).** … Still not seen by a person on a commercial game: none
is committed or available." (The issue brief's "not evaluated" wording is
older.) This run **restates** that line rather than closing it. A commercial
ROM is now known to be available locally, and the extended width is confirmed
on one, but the reveal on a real game scene still needs a person or a scripted
run into gameplay.

## Check 5 — residual human checks

- **Real gamepad pass:** not-automatable. It needs a person with a physical
  pad. Headless input tests do not substitute for it.
- **P.13 Hold to Compare on Windows/Linux:** not-automatable. This machine
  only runs macOS.
- **P.8 stop condition (3):** not-automatable. It stays a human check per the
  owner's 2026-10-05 decision.
