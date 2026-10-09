# ADR-0264: The Play *Open a game* sheet is a flat library of the ROMs under the library folders

- Status: accepted (2026-10-07) — requested by the owner as issue #1031, the
  decision-and-wireframes slice of the parent spec #1030, labeled
  `ready-for-agent`. The issue states the decision itself: the sheet becomes a
  flat library, the folder browser survives only as *Browse a file…*, the pad
  map and the cover priority are fixed, and ADR-0256 Decision 9's folder
  navigation is superseded. **Implemented**: the code this line once deferred to
  arrived with the sibling tickets #1032–#1039, one row each in Part B §8, and
  #1108 (PR #1168) lands the 2026-10-09 amendment below.
  **Amended 2026-10-09 (#1108)**: on the library sheet LB / RB cycle the console
  filter **and land the ring on the chip row** — owner decision via panel pick,
  2026-10-09, option A, quoted verbatim:
  **"Sheets keep their current pad behavior, except the library sheet, where the ring now reaches the console-filter chip (AC2)."**
  The id is 0264 and not
  the 0262 the issue text assumed: 0262 and 0263 landed on `origin/main` after
  that text was written, and ids are never reused (ADR-0035).
- Date: 2026-10-07
- Amended by: **its own 2026-10-09 amendment for #1108** (see the section at the
  end of this file): on the library sheet LB / RB cycle the console filter
  **and land the ring on the chip row**, which supersedes the *#1034 review
  finding 1* rule "the ring lands on a tile of the narrowed grid". Owner
  decision, verbatim: **"OWNER DECISION via panel pick 2026-10-09: option A.
  #1108 AC3 now reads "Sheets keep their current pad behavior, except the
  library sheet, where the ring now reaches the console-filter chip (AC2).""**
  Implemented by #1108 (PR #1168).
- Related: ADR-0256 (the Play GUI is fully operable from a controller alone —
  Decision 9 is the folder browser this replaces, and its stop rule is what the
  pad map below has to satisfy), ADR-0262 (the shared on-screen pad keyboard
  search types through), ADR-0249 (the rendered wireframes the render gate
  reads), ADR-0241 (the GUI redesign this is a slice of), ADR-0003 (the ROM hash
  contract the box-art match uses), ADR-0138 §41 (the host allow-list that
  already carries the box-art host), PRD Part B §8 and §13, issues #1030 and
  #1031.
- Supersedes / amends: **supersedes ADR-0256 Decision 9's folder navigation**
  (the folder-walking list as the primary Play open path). Decision 9's sheet,
  its standoff with the native dialog, its roots and its *Make this my games
  folder* action row all stand; what changes is that the sheet no longer opens
  as a list of folders to descend. The folder walk survives behind
  *Browse a file…*, which the new ADR forwards to exactly as it was — and
  Decision 9's first-row focus guard **lives on** inside *Browse a file…* too,
  where it still protects the *Make this my games folder* action row, the row
  staying where it is.

## Context

ADR-0256 Decision 9 turned the Play home's *Open a ROM…* button into an
in-app sheet, so that the last first-run surface needing a keyboard was gone
and every Play path was drivable from a pad. The sheet it decided is a
**folder browser**: a path line and one row per entry, folders first, then the
files whose extension is a ROM's; Confirm descends or loads, Back ascends.

That shape answers the question Decision 9 was asked — *how does a player with
no keyboard open a file?* — and it is the wrong shape for the question a
player actually has. A player who points the sheet at a games folder sees
folders first and files after, so the games are hidden one level down; to find
a title they must already know where it lives and walk the tree with the pad. A
collection of a few hundred ROMs across `NES/`, `GB/`, `Hacks/` and
`Translations/` is not browsable that way from a couch.

What the sheet has no way to do, measured against what a player wants:

- see *all* the games at once, rather than one folder at a time;
- search them, so one title in a long list is reachable without walking;
- narrow them by console, so the Game Boy half of a collection can be set
  aside;
- recognize a game by its cover rather than by a file name.

The information needed for all four is already in the app: every ROM under a
root can be classified by console, a file name can be cleaned into a title, a
ROM's identity can be computed through ADR-0003's hash contract, and the Recent
list already holds a screenshot of every game the player has actually run.

**Non-goals.** This does not touch the Classic door's file dialogs, and it
re-opens no clause of Decision 9's refusal: this sheet still offers no BIOS,
pack, save-state, movie, wave, shader or palette choice, and no general-purpose
`PathSelector`. It adds no metadata beyond a title and a console. It does not
redistribute artwork — see the last Decision below.

## Decision

**The Play *Open a game* sheet is a flat library.** Every openable ROM under
the player's library folders is listed at once, as a grid of vertical cover
tiles. The folder walk is no longer how the sheet opens; it survives as a
second entry point, *Browse a file…*, for the one ROM that is not in the
library.

1. **One flat list, one grid.** The sheet shows every openable ROM reachable
   under the library folders, regardless of how deeply it is nested. A folder
   row is never a row: the folders shape the scan, not the list. The grid is
   ordered by title, and the entry the player focused last time is focused
   again when the sheet reopens.

2. **Cover tiles are vertical, ≈3:4.** Each tile is a cover — the tallest
   rectangle a box art is — with the clean title and the console tag under it.
   Wireframe **W-P19** fixes the picture: the grid fills the sheet, the focused
   tile carries the focus ring, and nothing else on the sheet competes with it.

3. **The pad map is fixed, and every control on it is reversible.**
   - **D-pad / left stick** moves focus across the grid, row-major. **Up** from
     the top grid row moves focus into the header row — the search field,
     *Library folders…*, *Browse a file…* and Back — **left / right** moves
     between those controls there, and **down** returns to the grid. That is
     how a pad reaches *Library folders…* (Decision 8's pad-reachable folder
     list) and *Browse a file…* (Decision 11), so no control on the sheet is
     mouse-only.
   - **A** plays the focused game.
   - **B** leaves the sheet from anywhere (ADR-0256's stop rule: a sheet is
     reversible).
   - **Y** opens search.
   - **LB / RB** cycle the console filter.
   - Focus is drawn on exactly one tile at a time (ADR-0256 Decision 3), and
     the footer names the control in the player's hand, not the keyboard
     (ADR-0256 Decision 6).

4. **Search matches words in the clean title.** Search is a case- and
   accent-insensitive substring match over the clean titles, so `zel` finds
   *The Legend of Zelda* and `mario` finds *Super Mario Bros. 3*. The cleaner
   (Decision 7) strips the region, revision and dump tags from **both** title
   sources — the file name and the No-Intro canonical title — and it does so
   **before display and before search**, so the canonical `Castlevania (USA)`
   is shown and matched as *Castlevania*, and `usa` matches nothing on its
   own. It follows that a region, revision or dump tag cannot be matched,
   whichever of the two sources the title came from. On a pad, **Y** opens the shared on-screen pad keyboard
   ADR-0262 already owns and the grid narrows as the query is typed; on a
   keyboard, typing in the search field does the same thing. An empty result
   is a named state — *No games match* — with the query shown and a way to
   clear it, never an empty grid.

5. **The console filter lists only the consoles actually present**, so the
   player can never land on a filter that holds nothing. Search and the
   console filter compose: `mario` under the NES filter finds *Super Mario
   Bros. 3* and not the Game Boy one.

6. **Cover priority, in this order.** For each entry the library module
   answers which source applies, and the view only draws it:
   1. **downloaded box art** (Decision 10);
   2. **downloaded title-screen image** (Decision 10);
   3. **the player's own screenshot from the Recent list**, matched by the
      ROM's full path, for a game already played;
   4. **a generic console-colored cover carrying the title**, for a hack, a
      translation or any ROM no database knows.

   A ROM that falls to case 4 is not re-queried for art on every visit.

7. **A library entry is a path, a console, a clean title and a cover
   source.** There are two title sources and **one** cleaner over both. A
   generated SHA1 → No-Intro table supplies the canonical title when it knows
   the ROM; the file name is the fallback when it does not. The cleaner strips
   the extension and the region / revision / dump tags carried in parentheses
   and brackets, and it runs over **the canonical title as well as the file
   name**, before either is displayed or searched — so the canonical
   `Castlevania (USA)` is shown as *Castlevania* and matched by `cast`, not by
   `usa` (Decision 4). The **raw, uncleaned No-Intro name is kept for exactly
   one purpose**: the box-art URL of Decision 10. A leading article is moved
   for sort order, not for display.

8. **Library folders are a list, and the header counts them.** The header
   reads **"Your library · N games in M folders"**. *Library folders…* adds and
   removes the folders the scan reads: the native folder picker with a mouse
   (the mouse-reachability clause ADR-0256 Decision 6 leans on), the sheet's
   own pad-reachable folder list with a pad. The single `Preferences.GameFolder`
   the app already has **seeds the list on first run**, so no player loses the
   folder they had set; removing a folder from the list never deletes a file.
   A player who has set no folder sees an empty state that names the next step
   rather than an empty grid.

9. **The scan is bounded, backgrounded and visible.** It runs off the UI
   thread, streams entries into the grid as they are found, and shows an
   animated progress indicator while it runs — every visible wait has an
   animation. The grid fills as results arrive, so a player can start playing
   before the scan ends. Two constants bound it, and they are named here so
   that they are the decision rather than a detail of one implementation:
   - **`LibraryScan.MaxDepth = 6`** — at most six levels below a library
     folder. Two levels are needed for the ordinary `Console/Game/` layout and
     six covers collection folders that group by decade, region or origin; an
     unbounded walk over a whole mounted disk is what this caps.
   - **`LibraryScan.MaxEntries = 20000`** — the scan stops collecting after
     twenty thousand openable entries, and says so in the header rather than
     silently truncating.

   An archive (`.zip`, `.7z`) whose name the console classifier recognizes
   appears as one game; the archive path is the entry, and the existing
   load-time question about which ROM it holds is unchanged.

10. **Box art is downloaded once per game, through the host already on the
    pack allow-list, and cached only on the player's machine.** With the
    master switch **Download box art** (Settings › System, default **on**,
    consistent with `AutoInstallCommunityPacks`) each *visible* tile fetches
    its art once from the libretro-thumbnails collection under
    `Named_Boxarts`, falling back to `Named_Titles`. **The match is the ROM's
    No-Intro SHA1, resolved to a name** (ADR-0003): the hash — never the ROM's
    file name — is looked up in the **L.7 SHA1 → No-Intro name table (#1038)**.
    The name taken from that table is the **raw, uncleaned No-Intro name**,
    tags and all — the one consumer Decision 7 keeps it for; the displayed and
    searched title is the cleaned one, the URL path is built from the raw one.
    The collection's URL path is that raw name sanitised the way
    libretro-thumbnails names its own files, the characters
    `` & * / : ` < > ? \ | " `` each replaced by `_`. A ROM the table does not
    know gets **no fetch at all**: there is no name to ask for, and the tile
    falls straight to Decision 6's cover. The host is the GitHub raw
    host the pack allow-list already carries (ADR-0138 §41); this ADR is the
    record of that host being used for this second, outbound purpose. Fetches
    are lazy (visible tiles only), bounded in concurrency, HTTPS only, with a
    size cap and image validation before anything is cached, and the cache
    lives in the app-support folder keyed by console and SHA1; a miss is cached
    negatively so it is not re-queried on every visit. **With the switch off,
    no request is made at all** — the switch means what it says. Offline, the
    library opens and plays exactly as fast as online: no request ever waits,
    every cover the machine already holds is still used in Decision 6's order
    — the cached box art, the cached title screen, the player's own screenshot
    from Recent — and only a game that has none of them falls to the generic
    cover.
    **No artwork is redistributed.** Nothing from libretro-thumbnails is
    committed to this repository or bundled in a release; the images exist only
    in the player's own cache.

11. **The folder browser survives, unchanged, as *Browse a file…*.** For the
    one ROM outside the library the sheet keeps a second entry point that opens
    exactly the surface ADR-0256 Decision 9 built — its roots, its folder walk,
    its action row, its Back. The library is the default and the point of the
    sheet; the browser is the escape hatch, and it is not deleted.

12. **The target picture is rendered, not described.** Wireframes **W-P19**
    (the grid, with the header, the search field, the console segmented
    control, the pad hints, *Library folders…*, *Browse a file…* and Back) and
    **W-P19b** (the same surface with search active and the query `zel`) are
    produced by `scripts/render_gui_wireframes.py` into
    `docs/media/gui-redesign/`. Both are held in the render gate's
    **`WIREFRAMES_AWAITING_RENDER_CASE`** set, not in
    `EXPECTED_WIREFRAME_RENDERS`: no `RenderTests` case writes them yet, so
    the gate pins them as *drawn* without demanding a render that does not
    exist. They move into `EXPECTED_WIREFRAME_RENDERS` with the tickets that
    build the surface they picture — **L.1 (#1032)** for W-P19 and **L.2
    (#1033)** for W-P19b — which is when a visual regression in either starts
    turning CI red. Until then a regression in these two is caught by review
    of the regenerated PNG, not by the gate.

## Consequences

**What ADR-0256 Decision 9 loses, precisely.** Decision 9's sheet and its
reason for existing stand — a pad-reachable Play open path, no native dialog.
Its **folder navigation as the sheet's opening shape** is superseded: the
sheet no longer opens as a list of folders to descend. Nothing else of
Decision 9's is. Its first-row focus guard **lives on** inside *Browse a
file…*, where it still protects the *Make this my games folder* action row,
which also stays where it is; the guard is kept so that an empty folder
cannot silently repoint *Your games*. On the library side, the empty state is
a named state with a next step, and the action row is reachable one press
further, inside *Browse a file…*. Everything else Decision 9 decided is
untouched, and this ADR re-opens none of its refusals.

**A second list of the same kind.** The library grid and
`PlaySelectRomSheetView` are two lists over ROM files and stay deliberately
unmerged: one asks which game an archive holds (a list the loader owns), the
other shows what the player owns (a list the library owns).

**The outbound request is now a decision, not an accident.** Fetching box art
tells the host which games the player has. That is why the switch exists, why
it defaults to a value the player can find, and why the ADR names the host and
the purpose rather than leaving it to the network layer.

**Scanning is a new way to make the app slow.** The depth and count caps are
the whole protection; a library folder pointed at `/` lands on
`LibraryScan.MaxEntries` and the header says so. Removing a library folder
never deletes anything, which is also why nothing here prunes the box-art
cache when a folder leaves the list.

**A generic cover is a real outcome, not a failure.** A hack, a translation
and a homebrew ROM will keep Decision 6's console-colored cover forever; that
is the intended result, and it is why case 4 exists at all.

**The window into this work is the wireframe.** W-P19 and W-P19b are the
target picture the sibling tickets are built against; a change to the grid, the
tile proportion or the sheet's control set is an amendment to this ADR, not a
silent edit in the view.

**Not decided here.** A player-chosen custom cover, scraping services that need
an account or an API key, favorites, collections, play-time statistics and
metadata beyond title and console are all out of scope — each is its own
decision when someone asks for it.

## Amendment (2026-10-09, #1103): X is assigned to Favorite

Accepted 2026-10-09 together with ADR-0268, by the autonomy panel (Opus 5.5 as
the human proxy, issue #1103 comment) with edits, pick quoted verbatim: **"Accept ADR-0268, ADR-0269 and the ADR-0254 amendment with the listed edits; PR #1119 waits until ADR-0269 is accepted with Decisions 3 and 6 matching its code."**
Nothing is implemented by this amendment.

- **Decision 3** left X unassigned; ADR-0268 Decision 1 assigns X to Favorite on
  a focused cover. Y keeps meaning search.
- **Not decided here** no longer lists favorites as out of scope: ADR-0268 decides
  them (a newest-first list of library paths). Collections, play-time statistics,
  custom covers and scraping stay out of scope.

## Amendment (2026-10-09, #1108): the shoulder press lands the ring on the chip row

Accepted 2026-10-09 by the owner through the autonomy panel's pick, decision
quoted verbatim: **"OWNER DECISION via panel pick 2026-10-09: option A. #1108 AC3 now reads "Sheets keep their current pad behavior, except the library sheet, where the ring now reaches the console-filter chip (AC2).""**
Implemented by #1108 (PR #1168), with the cases named below.

**The rule.** On the library sheet, **LB / RB cycle the console filter AND land
the ring on the chip row** — on the segment the press selected. Decision 3's
"LB / RB cycle the console filter" is what the press *changes*; this is where
the press *puts the ring*, and it is Decision 3's own "focus is drawn on the
control the pad is acting on" (ADR-0256 Decision 3) read for the row. The row is
**one element** (PRD Part B §13.3 rule 2 counts a segmented row as one), so with
the ring on it:

- its **Left / Right** are the filter's own two directions — the same cycle the
  shoulders make, one step at a time — so the row and the grid can never
  disagree about which console is up (Decision 5);
- its **Up** leaves it for the sheet's header, and its **Down** returns to the
  grid it is filtering — the one-step-out answer every other control on the
  sheet has (RomPickerHeaderStep), which is what ADR-0256's stop rule requires:
  a place the pad can enter and not leave is not reversible;
- the footer names the shoulders and no Play (ADR-0256 Decision 6): A changes
  nothing on the row, so no Play is promised.

**What outranks the row.** The landing is skipped while the sheet's own claim
over the ring is standing: a restore in flight (`IsRestorePending` /
`IsRestoreLanding`) parks or lands the ring on the remembered game, so a player
who presses RB while a scan still owes them their last game gets the cycle, and
the game — not the segment — is where the ring lands. The **search box** keeps
the ring as it always did (#1034 review finding 2), and the landing does not
depend on a control being focused when the press arrives (a rebuild can take the
focused container out from under the ring).

**Supersedes, explicitly.** The **#1034 review finding 1** rule *"the ring lands
on a tile of the narrowed grid"* is **superseded**: after the cycle the ring
lands on the segment the press selected, which is the control the press was
about. The case that carried the old rule
(`PlayerLibraryConsoleFilterTests.Cycling_the_filter_from_the_grid_lands_the_ring_on_the_segment_it_selected`)
is its successor, and it still asserts the half of the old rule that survives —
the grid under the row is the narrowed one (Decision 5). Decision 5 itself is
untouched: this amendment says nothing about *which* consoles the filter lists.

**Decision 3's "focus is drawn on exactly one tile at a time"** is read with this
amendment: while the ring is on the filter row, the drawn focus is the row's
selected segment, and the row is one control — never a tile and a segment at
once.

**Evidence.** `UI.HeadlessTests/PlayerLibraryConsoleFilterTests` —
`Cycling_the_filter_from_the_grid_lands_the_ring_on_the_segment_it_selected`,
`The_ring_on_the_filter_row_cycles_it_and_steps_out_of_it`,
`A_shoulder_press_while_a_restore_waits_leaves_the_landing_to_the_restore`,
`A_shoulder_press_with_no_control_focused_still_lands_on_the_row`,
`The_footer_names_the_shoulders_while_the_ring_is_on_the_filter_row` — plus the
host-free declarations in `UI.Tests/Play/PlayBarSheetDeclarationsTests` and the
walk in `UI.HeadlessTests/PlayPadWalkTests`, whose `KnownChipGaps` ledger is
empty because the Library surface's chips are reached now.
