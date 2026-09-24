# ADR-0215: A copied tile key is resolved through the CHR mapping that drew the frame, never the one the paused emulator happens to hold

- Status: accepted (2026-09-19). The three **OPEN** either/ors below were
  decided **by the user on 2026-09-19**, one option each: OPEN 1 → option (a);
  OPEN 2 → "keep the image as it is"; OPEN 3 → "the same rule in all three
  viewers". The user's own wording is quoted verbatim under each. This change
  implements all three, together with issue #342 (the live palette) and issue
  #340 (the receipt), because the three share one code path.
  **The same-turn rule is NOT satisfied yet.** This project allows
  implementing in the turn an ADR is accepted only when the change ships with
  unit tests covering the decision **and** the go-ahead is quoted verbatim in
  this Status line **and** in the PR body (user's decision, 2026-09-14). The
  tests exist (`UI.Tests/Mep/NesDrawnTileResolverTests.cs`,
  `UI.Tests/Mep/NesPackTilePaletteTests.cs`,
  `UI.HeadlessTests/HdPackCopyReceiptTests.cs`) and the quotes are here.
  *Updated 2026-09-23:* the change landed in PR #348 (merged 2026-09-20).
  Its body names this ADR as accepted and implemented and states the three
  picks, but paraphrased rather than in the wording quoted under each OPEN
  below, so the PR-body half of the rule was met in substance, not
  verbatim.
- Date: 2026-09-19
- Related: issue #341, issue #342 (the live palette, decided with this ADR),
  issue #340 (no receipt, decided with this ADR), ADR-0172 (the sidecar records
  the CHR index — the field this key feeds), ADR-0043 (a CHR ROM key is an
  index), ADR-0169 (`_scanlineChrBankOffsets` / `_scanlineVideoRamAddr`, the
  per-scanline traces this reads), ADR-0167 (the OSD toast the receipt goes
  through), ADR-0214 (the F12.2 cold read that found it), ADR-0210 (where sheet
  coverage comes from)
- Supersedes / amends: nothing

## Context

`Copy as MEP sheet cell` and `Copy tile (HD pack format)` (both in
`HdPackCopyHelper`, reached from the Tilemap, Tile and Sprite viewers) name a
CHR ROM tile by its absolute CHR index. They get it by handing the tile's
**PPU-space** address to `DebugApi.GetAbsoluteAddress`, which lands in
`BaseMapper::GetPpuAbsoluteAddress` and reads `_chrPages` — the CHR mapping in
effect **at the instant the debugger asks**.

The runtime names the same tile differently. `HdBuilderPpu` resolves it inside
`StoreTileInformation`, at the scanline that drew it, and stores
`AbsoluteTileAddr / 16`. The per-scanline record of that mapping already exists
and is already published: `BaseNesPpu::_scanlineChrBankOffsets`, sampled at
cycle 257 of each visible scanline, reachable through
`GetScanlineChrBankTrace` (ADR-0169's "mid-frame CHR bank splits" update).
Nothing in the debugger path consults it.

The two disagree in two distinct ways, both measured in-process on 2026-09-19
against the F12.2 sweep's own save states, with the shipped copy action driven
through the real Tilemap Viewer ViewModel:

| Game | mapper / CHR | paused `_chrPages` for the bg pattern table | what the visible scanlines drew under |
|---|---|---|---|
| Dr. Mario (1990) | MMC1, 32 KB | `$1000` → CHR `$01000` | all 240 scanlines: CHR `$00000` |
| Gauntlet (1988) | 206, 64 KB | pages `$1800`–`$1BFF` off by `$400` | uniform, one mapping |
| Lemmings (1993) | MMC1, 128 KB | `$1000` → CHR `$00000` | scanlines 0–206: CHR `$1A000`; 207–239: CHR `$00000` |
| Ninja Gaiden (1989) | MMC3, 128 KB | `$1000` → CHR `$10000` | scanlines 0–146: CHR `$1E000`; 147–239: CHR `$10000` |

- **A transient mapping seen through the pause.** Dr. Mario never splits
  mid-frame; every one of its 240 visible scanlines drew under CHR `$00000`.
  The frame is paused in vblank (scanline 240), where the game has already
  written a different MMC1 CHR bank, so the debugger sees CHR `$01000` — one
  4 KB bank, exactly 256 tiles, late. Gauntlet is the same shape on a
  1 KB-banked mapper: 36 of its 960 on-screen cells are off by `$400` bytes.
- **A real mid-frame split.** Lemmings and Ninja Gaiden do switch banks
  mid-frame, and the paused mapping happens to be the *lower* band's, so every
  cell above the split is named with the wrong band's bank.

The copy path itself is faithful: over all 960 cells of Dr. Mario's frame, the
text the shipped action puts on the clipboard carries exactly
`GetAbsoluteAddress(tileAddr).Address / 16`, with zero mismatches against an
independent read of the same API — 149 distinct indices, every one in
`256..511`, none below 256. Substituting the scanline trace's mapping turns
each of them into its `-256` twin: cell `(0,0)` goes `508 → 252`, which is the
number the F12.2 evaluator's control experiment already proved correct (the
same paste with `252` rendered 30 720 magenta pixels, exactly the 30 cells the
copy table lists). Lemmings' `1 → 0x1A01` and Ninja Gaiden's `4346 → 0x1EFA`
reproduce the issue's two other measurements to the digit.

The failure is silent by construction. A wrong-bank index is a well-formed
key: `mep_build.py build` accepts it as a new conflict-free rule and prints a
precedence line, `mep_lint.py` has nothing to object to, and the renderer
simply never matches it. Every signal the artist has is green; the only
evidence is a frame that comes back pixel-identical.

The viewer's own picture is drawn under the same paused mapping, so on a
mid-frame-split game the image and the key agree with each other and both
disagree with the frame the artist is looking at. That is why Ninja Gaiden's
run saw glyphs drawn over the boulder it was trying to select.

Non-goals: changing `hires.txt` semantics (ADR-0005), changing what
`HdBuilderPpu` stores (it is the reference), or touching the CHR RAM key,
which is the tile's 16 bytes and has no bank in it.

## Decision

**A copy action names a tile with the mapping that drew it.** The absolute CHR
address is resolved through `_scanlineChrBankOffsets` for the scanline that
drew the tile, not through `_chrPages`. Where the drawing scanline is not
knowable, the action says so rather than emitting a plausible wrong key.

Those three questions are now decided. The user picked, on 2026-09-19, one
option each.

**OPEN 1 — which scanline names a tilemap cell: option (a).** The user's
words: *"invert `_scanlineVideoRamAddr`, use the first scanline that drew the
cell, and refuse when the set of drawing scanlines is empty or its members
disagree."*

`_scanlineVideoRamAddr[s]` is the loopy `v` that governed scanline `s`, taken
at cycle 257, so its coarse Y / nametable Y are the row that scanline drew and
its coarse X / nametable X are where that row's fetches started. A cell is
drawn by a scanline when the rows match and the cell falls inside the 33 tile
columns the scanline fetched, wrapping across the two horizontally adjacent
nametables. Of that set, the first member names the tile; an empty set is
`NotDrawnThisFrame` and a set whose members resolve the tile's PPU page to
different CHR offsets is `BanksDisagree`. Both are refusals with a reason.

Option (b) (`row * 8`) was not picked because it is wrong the moment the frame
scrolls; (c) (every visible scanline must agree) was not picked because it
refuses outright on Lemmings and Ninja Gaiden, the two games the artist most
wants; (d) (emit every band's key) was not picked because a copy action that
hands out several keys is no longer a paste.

**OPEN 2 — what the viewer's image does: keep the image as it is.** The user's
words: *"The Tilemap Viewer stays a view of PPU memory. Do not make the picture
follow the drawing mapping."*

So only the key follows the drawing mapping. On a mid-frame-split game the
picture and the key can therefore disagree, and the artist is asked to trust
the number over their eyes — which is exactly why the copy now speaks: the
receipt names the index and the scanline it came from, so the disagreement is
stated rather than silent. Making the picture a reconstruction of the last
frame would change every other entry in that viewer, and is not this decision.

**OPEN 3 — the other two viewers: the same rule in all three.** The user's
words: *"The Sprite Viewer has a Y and can name a scanline; the Tile Viewer has
neither a row nor a frame context, so it refuses. The three actions must mean
the same thing under one name."*

  - **Tilemap Viewer** — the cell's nametable address is inverted against the
    scroll trace, per OPEN 1 (a).
  - **Sprite Viewer** — a sprite is fetched during cycles 257-320 of the
    scanline before the one it appears on, which is the same cycle-257 point
    the traces are sampled at, so scanlines `Y+1 .. Y+height` are the ones that
    fetched it. They must agree, or it refuses; a sprite parked off the visible
    screen refuses too.
  - **Tile Viewer** — reading PPU memory, it has neither a row nor a frame, so
    it refuses and says where the tile can be picked instead. Reading CHR ROM
    or CHR RAM **directly** (its other sources) it is unchanged: that address
    never went through `_chrPages`, so there is nothing to resolve.

### The palette (issue #342)

The same copy path reads the tile's palette out of live PPU palette RAM, while
a pack's rules are keyed on the palette that was live when each tile was
*recorded*. On Metroid the two never intersect: the copy hands out `0F361506`,
every rule the pack holds for those bitmaps is keyed `0F0F0F0F`, and six
verbatim pastes built clean, linted clean and changed no pixel.

So the copy asks the loaded pack which palettes it keys the tile under, and
answers one of three ways — never a plausible key it knows cannot match:

  - the live palette is one of them, or the pack keys the tile with a
    `defaultTile` wildcard (`HdTileKey::GetKey(true)`, which matches any
    palette): keep the live palette;
  - the pack keys it under exactly one other palette: emit that one, and say so
    in the receipt, because that is the key a paste has to carry to match;
  - the pack holds no rule for the tile, or holds several and the live palette
    is none of them: refuse, naming which. "The pack does not hold this tile"
    is a better answer than a key that cannot match.

With no pack loaded there is nothing to check against, and the live palette is
emitted unchanged.

### The receipt (issue #340)

Every one of the 28 F12.2 cold-read sessions said the same thing: nothing after
the copy said it had worked, or which tile it took; the first confirmation was
a `build` line two steps later, by which point a wrong tile, a wrong slot and a
key lost to precedence all present as one symptom, an unchanged frame.

Both copy actions in all three viewers now return a result carrying a one-line
receipt, and every call site puts it on ADR-0167's OSD toast — the same seam
the other debugger actions use (`ShortcutHandler`'s layer toggles,
`LiveRecordingSession`'s failures), which also means the headless HUD capture
can see it. A success names what was copied, the index or the CHR RAM shape,
the palette, and the scanline the mapping came from. A refusal names the
reason. This is what keeps a refusal from trading a silent wrong key for a
silent nothing.

### What this adds

`DebugApi` could reach neither trace: `GetScanlineChrBankTrace` was exported
only through `HeadlessCaptureNesSpriteLayer`. Two new exports in
`InteropDLL/DebugApiWrapper.cpp` with their mirrors in `UI/Interop/DebugApi.cs`:

  - `GetNesScanlineTrace(uint32_t* outScroll, uint32_t* outChrBank)` — the raw
    240 and 240×32 traces. Raw rather than a scanline-aware
    `GetAbsoluteAddress`, because the decision they feed is a UI decision with
    its own host-free unit tests (`UI/Logic/NesDrawnTileResolver.cs`).
  - `GetNesHdPackTilePalettes(...)` — the palettes the loaded pack keys a tile
    under, which only the core holds (`NesConsole::GetHdData`, new).

## Correction, 2026-09-19 (measured while implementing)

The Context table above was taken by restoring each sweep save state and then
resuming for ~300 ms of wall clock before reading the trace. **The per-scanline
trace is not part of a Mesen save state.** After a `LoadStateFile` it still
holds whatever the run before the restore left, so at least one frame must be
rendered from the restored state before it describes anything — and that frame
is the one *after* the state's. Re-measured live against
`/Users/bihaiko/f12.2-opus-sandbox/frames/*.mss` with a deterministic
`Step(n, PpuFrame)` instead of a sleep:

| Game | ADR's row | live, from the sandbox state | verdict |
|---|---|---|---|
| Ninja Gaiden | `4346 → 0x1EFA` | `4346 → 0x1EFA` on 352 of 960 cells, `4344 → 0x1EF8` on 138 more | **reproduced exactly**, and phase-independent |
| Lemmings | `1 → 0x1A01` | `1 → 0x1A01` on the 7 cells the frame drew; 776 cells refuse as `NotDrawnThisFrame` | **reproduced exactly** |
| Dr. Mario | `508 → 252` | the state's own pause does read **508** (`page $1000 → CHR $01000`), but every frame reachable from it reads **252** on *both* sides — swept `MESEN_DIAG_STEP_FRAMES` 1–5, identical | **not reproducible from a save state** |

Dr. Mario's `508` is a mapping seen through a pause parked in vblank at the
instant the game had already written the next MMC1 bank — which is exactly the
first failure mode this ADR names, and exactly where a GUI pause lands. What
cannot be recovered is the *other* half of that pair: the trace of the frame
the artist was looking at, which the state does not carry. The `252` stands on
the F12.2 control experiment (that paste rendered 30 720 magenta pixels,
exactly the 30 cells the copy table lists), not on the harness.

None of this changes the decision or the fix. It changes what the harness can
be asked to prove: a save state can show the paused mapping, and a *running*
emulator paused by the user — the real flow — is the only place where the trace
genuinely describes the frame on screen. The contract on the rule is therefore
the host-free unit tests, not the harness.

## Consequences

- `Copy as MEP sheet cell` and `Copy tile (HD pack format)` now emit the index
  the runtime drew on a CHR-banked game, and the F12.2 evaluators' workaround
  (paint, rebuild, look for magenta, try the index one bank away) is retired.
- The decision is covered by host-free unit tests that fail on the old
  behaviour, because each one asserts against the number the old path produced
  (and, per the correction above, they are the contract — the harness is not):
  `UI.Tests/Mep/NesDrawnTileResolverTests.cs` (Dr. Mario `508 → 252`, Lemmings
  `1 → 0x1A01`, Ninja Gaiden `4346 → 0x1EFA`, the empty set, the disagreeing
  set, scroll, the sprite rule, the Tile Viewer refusal),
  `UI.Tests/Mep/NesPackTilePaletteTests.cs` (Metroid `0F361506 → 0F0F0F0F`,
  and the two refusals), `UI.HeadlessTests/HdPackCopyReceiptTests.cs` (the
  receipt, which did not exist to assert on before).
  `UI.HeadlessTests/ChrBankDiagnosticTests.cs` remains what it was — evidence,
  not a regression test — and now dumps a `drawn` line per cell carrying the
  paused index and the resolved one side by side.
- A refusal path is a new behaviour for these menu entries, which before either
  copied or silently no-opped. It is why the receipt is part of the same
  change: a refusal that says nothing trades a silent wrong key for a silent
  nothing.
- A tab showing a **mirrored** nametable now refuses, because `v` only ever
  names the logical nametable the game wrote to. The artist picks the tile on
  the tab the frame actually drew. This is the honest cost of OPEN 1 (a).
- Packs already recorded are unaffected: `HdBuilderPpu` was always right. Only
  hand-copied keys — the F12.2 paint loop's whole premise — carried the defect.
- Refusing when the loaded pack holds no rule for a tile means the copy is now
  narrower than "any tile you can see". That is deliberate: on the F12.2 paint
  loop a key the pack does not hold can never match at run time, and
  `mep_build` already says so for the repaint path (#253). This says it two
  steps earlier, where the artist can still act on it.
