# ADR-0256: The Play GUI is fully operable from a controller alone

- Status: accepted (2026-10-04). **All four questions were answered by the user
  on 2026-10-04** and are recorded under Decision, quoted verbatim; the user
  accepted the ADR and asked for the work later the same day, quoted verbatim:
  **"espera a review e mergeia os três. depois que estiver no main pode
  implementar tudo em paralelo usando workflows"**. **Decision 9 amended a
  second time on 2026-10-05** (a discovered library is named by the console it
  holds, and there is one row per console), under the user's own ask, quoted
  verbatim there. So this is a request for the
  work listed under Decision - the decision is made, and at the time of writing
  **nothing implemented it**; the first landing is the Controller sheet
  (ADR-0255), which this ADR's Consequences already names as the cheaper order.
  Ids are never reused (ADR-0035), which is why this is 0256 and not 0255.
  **Partly implemented 2026-10-04**, the same day: Decision 5's chord rule on
  any pad (#802, `Core/Shared/ShortcutKeyRules.h`), Decisions 2-4's host-free
  rules (`UI/Logic/PlayPadNavigation.cs`, `PadNavControls.cs`, `PadInHand.cs`,
  pinned by `UI.Tests/Play/PadNavigationTests.cs`) and Decision 6's footer
  vocabulary (`PlayMenuHint.ResumeHint`). Those rules are **rules only** - the
  bridge that hands them the app's pad state is not wired, so the pad still does
  not move the GUI's focus. **The bridge landed later the same day** (#827, with
  its review finding fixed in the same PR): every rule above is wired to the
  window, Decision 3's single focus owner is in place (a claim for each surface
  the bridge registered), and Decision 7's repeat rides the same tick. The
  corrections the slices needed followed in #834 — a claim for ADR-0255's
  Controller sheet, which the Consequences name, and the tool sheet's claim
  widened from the barcode kind to every kind it shows. **Decision 8 is answered
  on 2026-10-04, later the same day**, by the user's pick *"Tirar o wizard do
  caminho"*: the pre-core `SetupWizardWindow` leaves the startup path and the two
  choices it asked - storage and keyboard preset - move into Settings, which the
  bridge already drives. Decision 8's own paragraph carries the pick, its
  reasoning and what it costs. **Implemented 2026-10-04** (#846): nothing
  stands before the main window any more, Settings › System is a pad-drivable
  surface like the rest of Play, and the ROM picker is the one first-run surface
  still needing a keyboard - `PlayHomeView.OnOpenRom` calls
  `EmuApi.ExecuteShortcut(EmulatorShortcut.OpenFile)`, which reaches
  `FileDialogHelper.OpenFile` and a native OS dialog the focus engine cannot
  drive. That is #845, still open, and it is why the stop rule is not signed off
  yet. **Decision 9 closes it, decided and implemented 2026-10-05** (#845):
  the ROM picker is an in-app Play sheet, so the last first-run surface that
  needed a keyboard is gone and every Play surface is drivable from a pad.
  **Amended 2026-10-04**, the same day, after the work started: four more
  questions were put to the user and answered — the focus mechanism (Decision
  7's paragraph: the focus engine, not synthetic key events), what "one
  focusable control at a time" means concretely (Decision 3), auto-repeat
  (Decision 7) and the first run's scope (Decision 8). Their picks are recorded
  in the decisions themselves, quoted verbatim, and they changed the Decision
  text rather than being noted beside it.
  **Decision 9 amended 2026-10-05**, under the standing grant the user gave for
  the hours he was away that day (*"vou ficar off por algumas horas, use o grok
  no meu lugar se precisar. nao pare, decida"*), from his own requirement, quoted
  verbatim: **"poder mudar o diretorio onde estao as ROMs nao e opcional"**, his
  pick that the control lives in the ROM picker sheet itself, his pick of a root
  at `/`, and **"acho que sobre a roms, que tal buscar e sugerir, na pasta do
  mesen, na pasta do usuario, essas com padrao? e permitir indicar/alterar o
  diretorio de roms?"**. The sheet keeps its refusal of every generic folder
  picker and gains exactly one folder to set: the games folder it already lists.
  What that changes is set out under Decision 9, which also says which of the
  proxy's quoted clauses it narrows.
- Date: 2026-10-04
- Related: ADR-0241 (Play's home and the W-P4 pause overlay), ADR-0249 (the
  rendered wireframes as the visual spec), ADR-0250 (every menu entry has one
  place per door; Classic is a fourth), ADR-0251 (the pad's way *into* W-P4),
  ADR-0255 (the Controller sheet, one of the surfaces this has to drive),
  ADR-0123 (host-free rules).
- Supersedes / amends: none.

## Context

The user's requirement, 2026-10-04: *"a GUI precisa poder ser tmb operada
tolatamente pelo joystick, lembre que isso pode ser um arcade sem teclado ou
mesmo mouse"*. An arcade cabinet, a TV with a pad and nothing else, an HTPC:
every path through the app has to exist without a keyboard.

Only the **way in** exists today. ADR-0251 §3 gave `ToggleOverlay` a default
second binding — the pad's Home/Guide button where the platform reports one,
otherwise Select and Start pressed together — so a pad can open W-P4. That chord
goes through `ShortcutHandler` into the same `PlayEsc` router the Esc key uses: a
pad already opens and closes W-P4, closes a sheet back to W-P4, dismisses the
pack picker, answers Quit's "Keep playing", and backs out of the BIOS, load and
tool sheets. **Back, from a pad, already works everywhere in Play.** What does
not exist is everything else: W-P4's rows, the sheets behind them and Play's home
are reached by pointer or keyboard focus, and nothing moves a pad's D-pad into
either.

**The constraint that makes this a decision and not a chore: the pad is also
Player 1's controller.** Every button is spoken for while a game runs, and a
D-pad press that moves a menu cursor is a D-pad press that moves Mario. The
question is not "how do we navigate with a pad" but "**when** is the pad the
GUI's and not the console's". The good answer, and the reason this is cheap:
**W-P4 pauses the game** — while the overlay is up the pad is not read as
gameplay, so navigation is safe there; and the one gesture that must work while
the game *is* running, opening W-P4, is already a chord (ADR-0251) chosen so it
cannot collide with play.

**Non-goals:**
- Driving the classic `ConfigWindow` or the debugger windows by pad. Those are
  the mouse-shaped deep end; the Play door is the one an arcade cabinet uses.
- Emulating a mouse with a stick. Focus traversal is the model; a pointer is not.
- Any per-view navigation code. If every Play surface has to learn about the
  pad, this will rot the first time a sheet is added.

## Decision

**Decided, in six rules. The pad's authority is a function of the pause state,
and each rule below is binding - the four questions that used to sit under this
heading are answered by them and by the section after.**

1. **While a game runs unpaused, the pad is the console's and nothing else** -
   with one exception that already ships: W-P15's `UnknownControllerDetector`
   reads every connected pad while a game runs unpaused, to notice one whose
   keys no mapping uses and show the "press Start on it" pill (ADR-0249's setup
   sheet, `PlayControllerSetupViewModel`). So the pad has no *menu* authority
   while a game runs - the existing gestures are the chord and the detector, and
   anything new must justify itself against both.
2. **While W-P4 is up, or with no game loaded, the pad drives the GUI**: focus
   moves, something activates, something goes back, following ADR-0249's Esc
   order so `game → W-P4 → resume` and `sheet → W-P4` read the same from a pad
   as from the keyboard.
3. **One focusable control at a time**, the focus visible — an arcade cabinet
   has no cursor to fall back on, so "where am I" has to be drawn. Avalonia
   already focuses one element, so this is **not** a new roving-focus container
   — it is (a) one place that decides who receives focus when a surface opens,
   replacing the four-plus sites that decide it today and fight each other
   (`MainWindow.axaml.cs`, `PlayEdgeFlowsWiring`, the Play sheets wiring,
   `PlayHomeView`, `StateGrid`), and (b) that path entering focus with a
   `NavigationMethod`, so `:focus-visible` paints the `PlayerFocusRing` the theme
   carries. Suppressing the tab stops of a hidden surface is a separate question
   this rule does not settle.
4. **Navigation is not rebindable** (the user's answer, 2026-10-04:
   *"Não reconfigurável"*). Confirm, back and focus movement follow the pad's own
   preset - `DefaultKeyMappingType.Xbox` or `Ps4`, which the first run already
   models - and no surface may unbind them; Esc stays fixed. A player who binds
   "confirm" to a control their pad does not have is stuck, with no keyboard and
   no pointer to recover with. It also settles what ADR-0255's extra-buttons
   slice may offer: the navigation controls are **excluded**.

5. **The gesture that opens W-P4 belongs to a button, not to device 0** (the
   user's answer, 2026-10-04: *"Qualquer controle"*). Today it does not:
   `PlayMenuHint.ControllerCandidates` hardcodes every candidate to `Pad1`
   (`"Pad1 Home"`, `"Pad1 Guide"`, `"Pad1 Select" + "Pad1 Start"`,
   `"Pad1 Back" + "Pad1 Start"`), and no backend exposes a Home or Guide pad
   button - `Core/Shared/KeyDefinitions.h` has `"Home"` only as keyboard
   scancode 22, and the per-platform pad button lists have neither - so the
   seeded binding is always a `Pad1` chord: `Pad1 Select` + `Pad1 Start` where
   the backend names a Select (macOS, Linux) or `Pad1 Back` + `Pad1 Start` on
   XInput/Windows. With two pads connected, the one in the player's hand has no
   way into the overlay at all. Filed as issue #800 and fixed in #802; this rule
   is what that fix had to satisfy.

   **The rule cannot seed a default chord for a Windows DirectInput joystick** —
   filed as #804 and decided with the user on 2026-10-04. DirectInput exposes no
   semantic button names (axis directions and `But1..But128`), so the
   Select+Start gesture has no spelling in that family, and guessing two
   high-numbered buttons would put a default chord on top of the player's own
   controls. What that pad gets instead is Decision 2 plus ADR-0255 slice 4:
   Play › Settings › Controls has an **Extra buttons** section, a filtered view
   of the one shortcut list, so the pad binds its own menu button there by hand.
   #804 is closed by that surface. **The route to it has a gap, measured
   2026-10-04 after #844 landed:** `OpenControllerSheet()`
   (`UI/ViewModels/MainWindowViewModel.ControllerSheet.cs`) returns false unless
   `IsGameLoaded && Shell.IsPlay` - the sheet reads a paused game and both its
   Done and its Esc go back to W-P4 - so with no game loaded the sheet a pad
   could reach does not exist, and the first bind needs W-P4 open once. Opening
   it is exactly what a DirectInput pad cannot do, so a keyboard-less cabinet
   needs Esc or ADR-0254's focus-loss auto-pause once before the pad can finish
   the job itself. And before that, it cannot load a game at all: the ROM picker
   is a native OS dialog (#845). Making the Controller sheet reachable with no
   game loaded is the option that closes the asymmetry, and it is not decided
   here.
6. **On-screen text names the control in the player's hand**, not the keyboard
   (the user's answer, 2026-10-04: *"Segue o controle na mão"*). W-P4's footer
   reads "Esc to resume" today, a lie on the cabinet this ADR is about: the
   player has no Esc key. Confirm and back follow the pad's own preset
   (`DefaultKeyMappingType.Xbox` or `Ps4`), so "back" is B on one desk and ○ on
   another, and the footer has to say which.

   **What the bridge that feeds this line has to satisfy** (found in review,
   2026-10-04, before it was wired - the rule is landed as
   `PlayMenuHint.ResumeHint` and the seam that answers which device is in hand is
   not):
   - **The line follows the resolution, not the family.** `PadNavControls.Resolve`
     answers `null` for a device index it cannot resolve and for a backend that
     spells no such button - a Windows DirectInput joystick, #804's case - and a
     footer reading "B to resume" over a dead D-pad is the same lie as "Esc to
     resume" on a cabinet. If the mapping did not resolve, the line is the
     neutral one.
   - **It is recomputed when the device in hand changes, not only when the
     overlay opens.** Today the line is read on open; wiring the seam without
     refreshing on the change leaves the old control named after the player
     picks up the other pad.
   - **The device tracker must not be wired before back actually works.** With
     the tracker live and Decision 2's back unshipped, the footer would name the
     pad's circle while the only working way out is still ADR-0251's chord - a
     correct-looking instruction pointing at an inert button. The bridge lands
     both together.
   - **The control is named in words, not as a glyph.** W-P4's Resume button
     draws its play mark rather than putting one in the string, because the
     Player theme's font is the bundled Inter and a symbol depends on fallback.
     The footer says "Circle", not U+25CB.

7. **A held D-pad repeats** (decided with the user, 2026-10-04). The Core's
   shortcut thread only re-emits a key when the set of pressed keys *changes*,
   so a held direction produces one event and nothing else. A menu cursor needs
   its own timing: the first step on the press, then a repeat after a short
   delay. The exact numbers are an implementation detail; what is decided is that
   holding a direction **does** repeat, rather than stepping once per press.
8. **The first run is in scope** (decided with the user, 2026-10-04). Storage
   choice, keyboard preset and the ROM picker happen before any game and before
   any pad binding exists, exactly the state a cabinet boots into, and the PRD's
   stop rule cannot be signed off while they need a keyboard. They are driven by
   the same rules above — the surface the ADR's own Consequences already called
   "the hard part", and a slice of this work rather than a later ADR.

   **Answered 2026-10-04, later the same day**: the user's pick is *"Tirar o
   wizard do caminho"* - the app assumes the default folder and goes straight to
   the MainWindow (already drivable from a pad); the storage choice and the
   keyboard preset move into Settings. It changes a contract already in the PRD
   (W-P12) and deletes a screen that exists today. Concretely:
   - **`SetupWizardWindow` is retired.** `Program.cs` no longer branches to it
     when no settings file exists, and `App.ShowConfigWindow` goes with it. A
     fresh install boots into the main window with the default home folder,
     lands on the first-run home (W-P1) and is driven from there by the same
     bridge every other surface uses - no second process, no restart, no pad
     read that the core cannot make yet.
   - **The storage choice becomes a Settings surface.** It writes the same
     setting the wizard wrote; a switch the running process cannot absorb offers
     the restart the wizard's flow performed (write, then relaunch) rather than
     pretending the move happened.
   - **The keyboard preset lives there too**: the surface sets
     `DefaultKeyMappings` and writes the keys through the seeding path the first
     run and the Controller sheet's restore both use
     (`Configuration.SeedConsoleKeyDefaults`, extracted from `KeyPresets`'
     writers, one call site). The Controller sheet's
     `RestoreKeyboardPresetIfNothingIsBound` is **not** reused: its guard is
     `DefaultKeyMappings == None && NothingIsBound()`, a no-op on a fresh config
     whose default is already `Xbox | ArrowKeys`.
   - **`DependencyHelper.ExtractNativeDependencies(ConfigManager.HomeFolder)` is
     the wizard path's other job**, already on the normal startup path - the
     wizard's call existed only because that branch returned before reaching it.
     The implementation deleted the call with the branch, pinned by `UI.Tests`'
     `FirstRunStartupTests`, which reads `Program.cs` and refuses any `return`
     between the working directory being fixed and the extract call.
   - **Two things go with the screen, a real if small loss**: the Windows/Linux
     *Desktop shortcut* checkbox, which no surface offers any more, and *Check
     for updates*, already inert (`UpdateChannel.HasFeed` is false while the fork
     has no feed, #672). The Advanced door's Preferences tab keeps both.
   - **`App.ShowConfigWindow` did more than pick a first window**: it skipped
     `EmuApi.TestDll()`, so retiring the flag also puts the core-load error path
     back on first boot - a machine whose core cannot load gets the message
     instead of a wizard that starts and then fails.
   - **The PRD's drawn W-P12 sheet is retired with it**: the sheet kept the
     question and moved it inside the window, this pick drops it from the first
     run and answers it in Settings, which W-P12's own caption already promised
     ("Both can be changed later in Settings").
   - **Why this rather than the alternatives.** The wizard shows *before* the
     core exists, and a pad is read through the core's key manager
     (`InitializeEmu` registers it only when the window *and* the viewer handles
     are present, `InteropDLL/EmuApiWrapper.cpp`). A pad in the wizard would mean
     either a second, platform-specific reading of the pad - against this ADR's
     one-place rule - or initialising the core to read a device on the screen
     whose job is to say where the core's files go, the wrong order, creating
     files before the player chose where they live.

There is no second gesture into W-P4: the chord on any pad, and nothing else
(the user's pick, 2026-10-04, over adding a long-press). A pad whose Select or
Start is broken has no way in, which is accepted rather than overlooked.

The cheap implementation: do **not** teach each Play view about the pad. Every
pad press is reduced to one navigation intent in a single place next to
`ShortcutHandler`, and that place moves the focus.

**How it moves the focus is decided with the user, 2026-10-04**: not the wording
this ADR first carried. Translating pad events into synthetic keyboard events
was rejected on measurement: no code in the app has ever set a
`NavigationMethod`, and there is no evidence a synthesised `KeyEventArgs` drives
Avalonia 12's focus navigation — a mechanism that cannot be proven from a
headless test is the wrong foundation for the one path a keyboard-less cabinet
depends on. The bridge calls the focus engine directly
(`KeyboardNavigationHandler` / `FocusManager` with
`NavigationMethod.Directional`), deterministic, testable without a pad, still one
place.

9. **The ROM picker is an in-app Play sheet** (decided 2026-10-05, while the
   user was away, under the standing instruction he gave for that case:
   *"estarei fora por algumas horas, tome as decisoes sozinho, use o grok 4.6
   como proxy humano se precisar"*). Decision 8 named the ROM picker as part of
   the first run and left it as the one surface still needing a keyboard; this is
   the answer to it, filed as #845.

   **What was actually broken, measured.** `PlayHomeView.OnOpenRom` called
   `EmuApi.ExecuteShortcut(EmulatorShortcut.OpenFile)`, which reaches
   `ShortcutHandler.OpenFile` and then `FileDialogHelper.OpenFile` - Avalonia's
   `StorageProvider.OpenFilePickerAsync`, i.e. NSOpenPanel on macOS, the Win32
   common dialog on Windows, GTK on Linux. The bridge drives the app's own focus
   engine; a foreign dialog owns the screen once it opens, and Esc does not route
   through `PlayEsc` there either. The *button* was reachable from the pad and
   activated; the choice it opened was not. Every other file and folder choice in
   the app goes through the same helper (`FirmwareSelect`, `FirmwareHelper`, the
   W-P16 pack-file sheet, save-state import/export, movie and wave export,
   shaders, palettes, `PathSelector`), and the app has no in-app file browser at
   all.

   **The pick, quoted verbatim from the proxy** (Grok 4.6, 2026-10-05, asked with
   the measurements above and with the alternatives - a full Finder clone, a
   known-locations scan, and "make the ROM folder the answer"):

   > **Pick:** D - a Play-only ROM picker sheet that walks folders (A's surface)
   > from a short list of roots (B's places), used only to load a game.
   >
   > **Why the others lose.** A as written is a Finder clone plus five call
   > sites. That pretends BIOS, packs, shaders and movie export are this bug.
   > They are not. B cannot reach a stick you just plugged in, or a library one
   > folder below the scan. An HTPC guess at Downloads is not a cabinet answer.
   > C leaves *Open a ROM…* a lie. If the default folder is empty, the machine
   > still cannot load a game. That does not close #845.
   >
   > **The stop rule.** Host-free in `UI/Logic/`: given a fake tree, the model
   > lists the roots (configured `GameFolder` when set, the app ROM folder, each
   > mounted volume) plus recents; a folder row descends; Back ascends; a
   > ROM-extension file is the only pick; non-ROMs are not rows. Headless
   > against the real core: Play home, no game, pad-bridge Confirm on *Open a
   > ROM…* opens this sheet (not `StorageProvider`); the sheet takes the focus
   > claim; Confirm on a nested `.nes` returns that path to the existing open-ROM
   > path and the home shows the loaded game; Back from the root dismisses with
   > no load. Advanced still uses the native dialog. That is what closes #845.
   >
   > **What I refuse to build now.** No BIOS / W-P16 pack / save-state / movie /
   > wave / shader / palette / folder picker in this sheet. Those stay native
   > until each one is a pad-only blocker of its own. No hidden-file policy, no
   > search box, no keyboard path typing. One sheet, ROM extensions, load a
   > game.

   Concretely, and as implemented: `PlayerRomPickerSheet` is a light sheet in
   the main window like Settings and the Controller sheet, with a path line and
   one row per entry - the folders first, then the files whose extension is a
   ROM's (`RomFileKinds`, the table `FolderHelper` carried, moved into the
   dual-compiled `UI/Logic` layer so the rules can be tested without a host),
   each group sorted, never a dot-name. Confirm on a folder descends; Confirm on
   a game hands its path to `LoadRomHelper.LoadFile`, the same call the native
   dialog's own result took - so nothing downstream changes, an archive still
   asks which game it holds, and the pack still resolves. Back ascends one level,
   and on the first list it dismisses: `PlayEsc` gains one state
   (`RomPickerBack`), not a second key handler, and `PlaySheet.RomPicker` is the
   one Play sheet that is *not* opened from W-P4 - it sits over the home, so its
   step back opens no overlay. The sheet's own Back button calls the same method
   the router does. The roots are the configured game folder when
   `Preferences.OverrideGameFolder` is set, the app's own ROM folder
   (`<HomeFolder>/Roms`, created on demand), and every mounted volume
   (`MountedVolumes`, the one host-aware piece - `/Volumes` on macOS, ready
   fixed and removable drives on Windows, `/media`, `/run/media/<user>` and
   `/mnt` on Linux).
   - **Recents are not a root**, though the proxy listed them. A recent game is a
     `.rgd` archive, not a ROM path, and the app has no helper that reads one
     back; and the surface that has them (W-P2) already reaches the newest game
     in one press through Continue.
   - **What it costs.** The sheet is a second, smaller list of the same kind
     `PlaySelectRomSheetView` already is, and the two are deliberately not
     merged: one asks which game an archive holds (a list the loader owns), the
     other walks the filesystem (a list the player owns).
   - **What this deliberately does not decide**: every other file *and folder*
     choice stays native (the proxy's refusal above). A pad-only machine still
     cannot add a BIOS file, a pack dependency, a save state, a shader or a
     palette, **nor set any folder other than the games folder**, and each of
     those is its own bug when someone reports it. Advanced's own Open keeps the
     native dialog too - this sheet belongs to the Play home, the door a cabinet
     boots into.

   **Amendment, 2026-10-05: the sheet also names the folder the games live in.**
   The user, verbatim: *"poder mudar o diretorio onde estao as ROMs nao e
   opcional"*. The proxy's quotation above stands as the record of what was
   picked; what it *binds* is narrowed here, because two of its clauses read
   wider than the decision they carry.

   - **"used only to load a game" is now "load a game, and name the folder they
     live in".** Both clauses of the refusal ("*No BIOS / W-P16 pack / save-state
     / movie / wave / shader / palette / folder picker in this sheet*", and the
     stop rule's "*a ROM-extension file is the only pick*") are about picks and
     choices the sheet has no business making. The games folder is neither: it is
     already one of the sheet's own roots - the stop rule lists "configured
     `GameFolder` when set" - and the sheet's subject is the folder that holds
     the games it lists. A *folder picker* in that refusal means the generic
     `PathSelector` family the sentence enumerates it with (BIOS, packs, save
     states, movie and wave export, shaders, palettes), and that refusal is
     untouched: this sheet gains exactly one folder it may write, the one it
     exists to browse. The stop rule's "non-ROMs are not rows" survives with one
     carve-out, because a *discovered folder* is a place to walk to, not a pick -
     the same exception a folder row already was.
   - **What the sheet gains, three things.** (1) A **bounded scan** of the
     standard places - the user's home and the Mesen home to depth 5, each
     mounted volume to depth 3 - collecting folders that hold at least one ROM
     directly, ranked by how many, deduped against each other (a folder that
     contains a better hit is dropped) and against the specific roots, capped at
     five, and run **off the UI thread with a folder and wall-clock budget** so
     the sheet never waits. Two passes, in this order: a shallow one (home 2,
     volumes 1) published the moment it lands, then the measured one that
     replaces it - and a second answer that offers *fewer* libraries than the
     first is discarded, so "the scan finished" can never read as "your libraries
     are gone". **A network mount is never a base and is never entered** (NFS,
     SMB, AFP, WebDAV, CIFS, autofs): a read on a mount whose server is gone
     blocks in the kernel, where no wall-clock budget can interrupt it. Whether a
     mount is a network one is *told* to the scan by the host, so the rule stays
     host-free and is pinned in `UI.Tests`. It is a *scan of standard locations*,
     not the "no search box" the refusal forbids: no keystroke field, no query,
     no path typing. Measured on the requesting machine: depth 4 costs 0.31 s and
     does **not** find his library, which sits at depth 5; depth 5 costs 0.86 s
     and its top five hits are exactly his five emulator libraries; depth 6 costs
     1.24 s and adds only noise. That is why the depth is 5 and the ranking is by
     count. (2) An **action row**, *Make this my games folder*, first in the list
     inside any folder that is not already the games folder; it writes
     `Preferences.GameFolder` and `Preferences.OverrideGameFolder = true` through
     `ConfigManager.Config.Save()` - the two properties the classic Advanced
     Options row already writes, so no new setting exists - and then re-roots in
     place, turning the path line into *Your games* and putting the new root at
     the head of the list. (3) A root at **`/`**, the user's own pick, so a folder
     the scan does not reach is still reachable by hand. It is supplied by the
     host like the volumes are, and is deliberately **not** part of the exclusion
     set the scan dedupes against, or it would discard every suggestion.
   - **The fence: this sheet writes `GameFolder` and nothing else, ever.** The
     action row is the sheet's only write, and the two properties it sets -
     `Preferences.GameFolder` and `Preferences.OverrideGameFolder = true`, both
     through `ConfigManager.Config.Save()` - are the whole of it. No other
     preference, no other file, no pack, no pad binding, no storage path, no
     window geometry. This is the clause the widening above is bounded by: a
     later change that wants this sheet to write a second thing is a change to
     this ADR, not a patch to the sheet.
   - **The focus rule that makes (2) safe.** The action row leads the list, so the
     focus arbiter must never land the ring on it: the picker's first-row target
     skips Action rows whenever the folder has content, and a folder with none
     sends the ring to Back instead. Without that, a stray Confirm on an empty
     folder would silently repoint *Your games* away from a library that worked.
     A headless case pins it.
   - **Still refused, unchanged**: no BIOS, pack, save-state, movie, wave, shader
     or palette choice in this sheet, no general-purpose folder picker, no
     hidden-file policy, no keyboard path typing. Advanced keeps the native
     dialog, and so does every other door.
   - **The save is two writes, and the second one is the point** (added by the
     review of the first pass, 2026-10-05). `MainWindow` calls
     `EmuApi.AddKnownGameFolder` from the configured folder **at startup only**,
     so a folder designated at runtime would not be in the core's
     known-game-folder list until the next launch - and the whole purpose of the
     action row is a folder the player can use *now*. The sheet therefore makes
     that same call itself, immediately after `ConfigManager.Config.Save()`.
     `Core/Shared/RomFinder.h` is what resolves a ROM by name and CRC when a
     pack, a replay or a movie names one, so without the call a designated folder
     works for this sheet and for nothing else until restart, which is precisely
     the "works only after a restart" the action row exists to remove. The
     classic PathSelector keeps the old gap; that is out of scope here.

   **Amendment, 2026-10-05 (second, same day): a discovered library is named by
   the console it holds, and there is one row per console.** The user, verbatim:
   *"acho precisamos poder ter uma pasta por console. de qq forma da maneira que
   esta nao funciona direito. verifique cuidadosamente como melhoramos isso.
   Quem sabe listar todas as ROMs abaixo do diretorio? agrupando por console? O
   que vc acha?"*; his picks, verbatim from the questions put to him: *"Varredura,
   agrupada"* and *"Tudo (rótulo + agrupar + recusar)"*. What the ranking above
   does was measured before it was changed, and the measurement is the reason
   this amendment exists.

   - **What was wrong, three things, all measured on the requesting machine.**
     (a) Every library on disk is a folder called `roms` under a folder named
     after its console, so the suggestion label `roms  ·  <shortened parent>`
     made the four libraries the scan found share their **first 51 characters** -
     and the console, the only discriminating segment, sat exactly where the
     480 px sheet's `CharacterEllipsis` cuts. Four rows, one reading.
     (b) The scan found **154 hits**, of which the four libraries above were the
     top by count and the rest were stray ROMs in working trees; the ranking's
     "top five hits are exactly his five emulator libraries" (above) no longer
     holds on a machine that has since accumulated work trees.
     (c) The Atari 7800 folder - ten `.zip`/`.7z` files of a console this
     emulator cannot open - was offered as a library, because
     `RomFileKinds.IsOpenable` accepts an archive by extension and cannot see
     what console is inside it. Every pick in it fails to load.
   - **The decision.** `RomConsole` (`UI/Logic/RomConsoleKinds.cs`) is the
     consoles this emulator runs, and the ONE table of which extension is which:
     `RomFileKinds.IsRomFile` asks it rather than keeping a second copy. The
     granularity is the player's, not the core's - the core files Game Gear
     **and SG-1000** under its one `Sms` type and has no Game Boy Color at all,
     but `.gg`, `.sg` and `.gbc` libraries are different machines to the person
     reading the row. The app's own settings already draw that line exactly where
     the ROM files are associated: `chkSmsRomFiles` is "SMS roms: .sms",
     `chkGameGearRomFiles` is "Game Gear roms: .gg" and `chkSgRomFiles` is
     "SG-1000 roms: .sg" - three settings, three platforms.
     The walk **names each hit's console from the file names it has already
     listed**, so it costs no extra read, and the suggestions are **one row per
     console**, the console with the biggest library leading. The label is
     `<console>  ·  <count> games  ·  <shortened parent>`, so the part that tells
     two rows apart is the part that survives the trim. A folder whose console
     cannot be named keeps the older `<name>  ·  <parent>` shape and sorts after
     every console: it is not a console, so it is not collapsed into one, and two
     of them are two places.
   - **One row per console is load-bearing, and this was measured too.** The
     first version of this change ranked by count with the console only
     breaking ties, and on the requesting machine that gave **all five rows to
     five `Nes` folders** - the four stray ones plus the real library - and
     dropped the Master System and Game Boy Advance libraries entirely. The cap
     is shared, so the row that stands for a console must not be able to starve
     another console.
   - **SG-1000 was folded into Master System, and adversarial review caught it.**
     The first version of `RomConsoleKinds` mapped `.sg` to
     `RomConsole.MasterSystem`. That is defensible read off the core alone -
     `Core/SMS/SmsConsole.cpp` builds one `SmsConsole` for both and picks
     `SmsModel::Sg` versus `SmsModel::Sms` from the file's extension - but it is
     wrong against this decision's own rule two paragraphs up, and it was
     inconsistent with `.gg`, which the same code path files identically under
     `Sms` and which this table *does* keep separate. The consequence is the one
     the one-row rule makes severe: a player holding both a `.sms` library and a
     `.sg` one is offered **only the larger**, and the other machine's games are
     unreachable from the picker. Found by the review of PR #889 (grok,
     2026-10-05), which requested changes on exactly this: *"a `roms` folder of
     only `.sg` files is named Master System ... so beside a `.sms` library the
     one-row rule keeps only the larger folder and the other console is not
     offered."* `RomConsole.Sg1000` now exists, `.sg` maps to it, and
     `An_sg1000_library_is_not_collapsed_into_the_master_system_one` pins it.
   - **What this deliberately does not do: refuse the Atari folder.** The report
     asked for it and the extension cannot deliver it. `Asteroids (USA).zip` and
     a zipped NES library are indistinguishable by name, and the ADR's own rule
     above wants zipped libraries offered. Refusing archive-only folders would
     refuse the cabinet's whole library. The honest answer - and the one taken -
     is that such a folder's console is **Unknown** and the row says so instead
     of claiming one; the two are no longer conflated at the point the player
     reads them. Deciding it *exactly* needs the archive's entry list, which the
     core already produces at pick time (`ArchiveHelper.GetArchiveRomList`) and
     which the scan does not have; that is its own change and is not made here.
   - **Unchanged**: the walk itself, its two passes, the budgets, the depth
     measurements, the network-mount rule, the action row, the `GameFolder`
     fence and every refusal above. This amendment changes what a discovered
     library is *called* and *how many rows it may take*, and nothing about
     which folders are found.

   **Amendment, 2026-10-05 (third, same day): the configured games folder is
   used only while it answers something.** The rule for a games folder that
   resolves to a directory holding nothing was put to the user as a choice
   (issue #887; macOS `/home` is an autofs node - `isdir` true, `listdir` empty),
   and he picked **"Degradar no uso"**, verbatim: *accept any folder as the games
   folder, and never open on one that lists nothing*. That is the go-ahead this
   narrowing records, and ADR-0260 is the decision it belongs to.

   Three clauses written above stop being literally true, and this is where they
   are narrowed rather than left to contradict the code. Each was a promise the
   sheet could not keep once the folder it promised to lead with is one the app
   cannot open on - the player is left at a root that lists nothing, with no way
   back to a library that works.

   - **"The roots are the configured game folder when `Preferences.OverrideGameFolder`
     is set"** is now "…when it is set **and that folder holds at least one
     entry**". `PlayerRomPickerViewModel.GamesFolder` asks `GamesFolderChoice.Usable`
     instead of reading the setting, so an empty, missing or unreadable folder is
     not a root at all. The app ROM folder and the mounted volumes are unchanged:
     they are not the player's setting, and nothing here makes them conditional.
   - **The action row "then re-roots in place, turning the path line into *Your
     games* and putting the new root at the head of the list"** is now that,
     **when the folder answers**; when it does not, the setting is saved, the
     roots are rebuilt without it, the path line keeps the shortened path, and
     the row stays where it was with a notice saying the folder is saved but not
     the one in use. The *save* is untouched - the fence below still holds, and
     the two properties written are the same two - and so is the fence's reason:
     the player may put a legitimate empty library there and fill it later.
   - **"The sheet therefore makes that same call itself, immediately after
     `ConfigManager.Config.Save()`"** is now that call **gated on the same
     answer**: `EmuApi.AddKnownGameFolder` is not called for a folder that holds
     nothing, because what that list feeds is `RomFinder`, which resolves a ROM
     by name and CRC and has nothing to resolve in an empty folder. Everything
     the paragraph above says about *why* the second write exists is untouched -
     a designated folder that answers something is still registered in the same
     turn, and the "works only after a restart" gap is still closed.
   - **What this does not change**: the fence (this sheet writes `GameFolder` and
     nothing else, ever), the setting itself - which is never cleared, refused or
     rewritten, so a folder that answers nothing today is used again the moment
     it holds anything - the focus rule that keeps the ring off the action row,
     and every refusal above. The rule is one function in the dual-compiled
     `UI/Logic` (`GamesFolderChoice`), so the next entry point that needs it asks
     the same question instead of an inline `Directory.Exists` of its own.
   - **The cost, stated where the sheet's own rules live**: a legitimately empty
     games folder stops being led with. A player who has just created an empty
     library is not led to their new empty one - `Open ROM` starts at the
     last-opened folder only when that folder answers something too, and
     otherwise at the dialog's own default, and the roots list is usually one
     root shorter but not always, because `PlayRomPicker.Roots` dedupes by path
     and the folder is still a root under another label when it is also the app's
     own ROM folder or a mounted volume. That is deliberate - telling an autofs
     node apart from an empty folder is not knowable from the path - and the
     alternative (refusing at save) would refuse a folder that is full
     tomorrow. ADR-0260 carries the full argument and the three ways out that
     were put to the user.

## The four questions, and how they were answered

All on 2026-10-04, by the user, quoted verbatim from the questions they answered.

1. **Which buttons are confirm and back?** **Answered by the navigation answer**:
   they follow the pad's own preset - `DefaultKeyMappingType.Xbox` or `Ps4`,
   which the first run already models - so "back" is B on one desk and ○ on
   another.
2. **Is navigation rebindable?** **"Não reconfigurável"** - no. See Decision 4.
3. **Can the player get stuck?** **Answered, and the answer found a live bug.**
   Checking the premise turned up issue #800: the chord is hardcoded to `Pad1`,
   so a second pad has no way in. The user's pick was "Qualquer controle" - the
   gesture is fixed per button rather than per device, and there is no second
   gesture. See Decision 5.
4. **Does the app say which pad it means?** **"Segue o controle na mão"** - the
   text follows the device in hand. See Decision 6.

## Consequences

- **The first run is the hard part.** Storage choice, keyboard preset and the
  ROM picker all happen before any game, before any pad binding exists, and
  possibly on a machine with no keyboard at all — so they need pad input from a
  path that has never been configured. This is the case a cabinet actually boots
  into, which is why Decision 8 brings it into this ADR's scope rather than
  leaving it to a later one.
- Focus traversal has to be *drawn*: `PlayerTheme.axaml` has carried a
  `PlayerFocusRing` on `:focus-visible` since the theme landed, now a three-layer
  glow on every button class the theme defines, with list rows glowing inward
  (`UI.HeadlessTests/PlayFocusGlowTests`). What is *not* covered is everything
  outside that theme - the classic `ConfigWindow`, the debugger windows - and
  Play's own surfaces wherever a focusable control is not a Button.
  `:focus-visible` is also the wrong trigger to rely on alone: it answers the
  keyboard, and whether a pad moving focus sets it is exactly one of the things
  this decision has to pin.
- Rule 1 means the pad's navigation authority is a function of the pause state,
  which is a function of ADR-0254's auto-pause as well — an overlay opened by a
  focus loss mid-game hands the pad to the GUI without the player asking.
- ADR-0255's Controller sheet is one of the surfaces this has to drive, so
  landing 0255 first and 0256 second is the cheaper order: the sheet is a
  single, self-contained place to prove the focus model before it is asked to
  carry the whole door.
- This ADR does not cover the pad's *way in* being discoverable; ADR-0251 owns
  that, and the count it keeps (`PlayMenuHintsShown`) is per install, not per pad.
- **Two mechanisms already in the tree move focus without going through this
  ADR's rules, and neither is named in the Decision (found 2026-10-04, before the
  bridge landed).** They are the reason the bridge is not merely additive:
  - `XYFocus.NavigationModes="Enabled"` is set on some nineteen Play surfaces
    (`UI/Windows/MainWindow.axaml`'s overlay and sheets, every `UI/Views/Play*`
    sheet, `PlayHomeView.axaml`, `SetupWizardWindow.axaml`). That is a
    directional-focus mechanism the ADR never decided on and does not mention as
    prior art; the bridge has to coexist with it rather than assume it is absent.
  - `UI/Controls/StateGrid.axaml.cs`'s `TimerInput_Tick` is per-view navigation
    code of exactly the shape the non-goals forbid: it polls
    `InputApi.GetPressedKeys`, walks the recent-games grid using **player 1's own
    console mappings** off `Nes.Port1` and friends, keeps its own de-duplication
    set, and never repeats. Decision 4 says navigation is not rebindable and
    follows the pad's preset; this follows whatever the player bound to the
    console, so a player who moves their D-pad loses grid navigation, and a
    second pad cannot drive it at all. The grid is reachable from W-P4, so the
    stop rule covers it; folding it into the bridge is part of this ADR's work,
    not a follow-up.
