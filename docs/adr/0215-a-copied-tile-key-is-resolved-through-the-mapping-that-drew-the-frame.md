# ADR-0215: A copied tile key is resolved through the CHR mapping that drew the frame, never the one the paused emulator happens to hold

- Status: accepted (2026-09-19). Implemented in PR #348 (merged 2026-09-20). The NoRule reversal (2026-09-24) carries the user's go-ahead, verbatim: *"aceito sua sugestao. pode aplicar e rodar em paralelo"*. Unit tests: `UI.Tests/Mep/NesDrawnTileResolverTests.cs`, `UI.Tests/Mep/NesPackTilePaletteTests.cs`, `UI.HeadlessTests/HdPackCopyReceiptTests.cs`. Host-free code: `UI/Logic/NesDrawnTileResolver.cs`.
- Date: 2026-09-19
- Related: issue #341, issue #342, issue #340; ADR-0172, ADR-0043, ADR-0169, ADR-0167, ADR-0214, ADR-0210
- Amends / supersedes: nothing

## Decision

**A copy action names a tile with the mapping that drew it.** The absolute CHR address is resolved through `_scanlineChrBankOffsets` for the scanline that drew the tile, not through `_chrPages`; where the drawing scanline is not knowable, the action says so rather than emitting a plausible wrong key. It is one rule, under one name, in all three viewers.

### OPEN 1 — which scanline names a tilemap cell: option (a)

The user's words: *"invert `_scanlineVideoRamAddr`, use the first scanline that drew the cell, and refuse when the set of drawing scanlines is empty or its members disagree."*

`_scanlineVideoRamAddr[s]` is the loopy `v` that governed scanline `s`, taken at cycle 257, so its coarse Y / nametable Y are the row that scanline drew and its coarse X / nametable X are where that row's fetches started. A cell is drawn by a scanline when the rows match and the cell falls inside the 33 tile columns the scanline fetched, wrapping across the two horizontally adjacent nametables. Of that set the first member names the tile; an empty set is `NotDrawnThisFrame` and a set whose members resolve the tile's PPU page to different CHR offsets is `BanksDisagree` — both refusals with a reason. Option (b) (`row * 8`) was not picked because it is wrong the moment the frame scrolls; (c) (every visible scanline must agree) because it refuses outright on Lemmings and Ninja Gaiden; (d) (emit every band's key) because a copy action that hands out several keys is no longer a paste.

### OPEN 2 — what the viewer's image does: keep the image as it is

The user's words: *"The Tilemap Viewer stays a view of PPU memory. Do not make the picture follow the drawing mapping."* Only the key follows the drawing mapping, so on a mid-frame-split game the picture and the key can disagree, and the artist is asked to trust the number over their eyes — which is why the receipt names the index and the scanline it came from. Making the picture a reconstruction of the last frame would change every other entry in that viewer.

### OPEN 3 — the other two viewers: the same rule in all three

The user's words: *"The Sprite Viewer has a Y and can name a scanline; the Tile Viewer has neither a row nor a frame context, so it refuses. The three actions must mean the same thing under one name."*

- **Tilemap Viewer** — the cell's nametable address is inverted against the scroll trace, per OPEN 1 (a).
- **Sprite Viewer** — a sprite is fetched during cycles 257-320 of the scanline before the one it appears on, the same cycle-257 point the traces are sampled at, so scanlines `Y+1 .. Y+height` are the ones that fetched it; they must agree or it refuses, and a sprite parked off the visible screen refuses too.
- **Tile Viewer** — reading PPU memory it has neither a row nor a frame, so it refuses and says where the tile can be picked instead. Reading CHR ROM or CHR RAM **directly** it is unchanged: that address never went through `_chrPages`, so there is nothing to resolve.

### The palette (issue #342), as amended

The same copy path reads the tile's palette out of live PPU palette RAM, while a pack's rules are keyed on the palette live when each tile was *recorded*. On Metroid the two never intersect: the copy hands out `0F361506`, every rule the pack holds for those bitmaps is keyed `0F0F0F0F`, and six verbatim pastes built clean, linted clean and changed no pixel.

So the copy asks the loaded pack which palettes it keys the tile under, and answers — never a plausible key it knows cannot match:

- the live palette is one of them, or the pack keys the tile with a `defaultTile` wildcard (`HdTileKey::GetKey(true)`, which matches any palette): keep the live palette (`LiveMatches`);
- exactly one candidate (`Substituted`): emit it and say so in the receipt, because that is the key a paste must carry to match. A palette the pack keys the tile under is a **candidate** only when palette RAM holds it now for the same layer: one of the four background palettes for a background tile, one of the four sprite palettes (color 0 packed as `FF`) for a sprite, the words packed the way `HdTileKey` packs them;
- **no candidate** (`RecordedNotDrawn`): every palette the pack keys the tile under is one the frame cannot draw. The copy keeps the live palette and the receipt names the recorded palettes and says the paste adds a new key. This is not a refusal;
- `NoRule` (the loaded pack holds no rule for the tile): the copy keeps the live palette, packed as for any other copy (`FF` leads a sprite's word), and its receipt says the pack holds no rule, names the live palette and says the paste adds a new key. `IsRefusal` is false, and `NoRule` stays a status of its own, apart from `Unchecked` (no pack is loaded); both emit the live palette;
- several candidates and none is live: refuse as `Ambiguous`, listing only the candidates — the pack is evidence *against* the live palette here, and with no per-scanline palette trace neither the pack nor the frame picks one;
- with no pack loaded there is nothing to check against, and the live palette is emitted unchanged.

Why "palette RAM holds it" and not something stricter: `_scanlineVideoRamAddr` and `_scanlineChrBankOffsets` are the only traces, so palette RAM at pause is the only evidence the copy can check. Two residual cases are stated, not hidden: a candidate can sit on another slot (the receipt names both palettes), and a substitution is right for the clicked cell only when RAM changed after the cell was drawn — a mid-frame palette swap, or a fade step written in vblank. "The pack does not hold this tile" is a better answer than a key that cannot match; "can never match at run time" was the wrong premise, because the runtime looks the tile up under the live palette and a paste that adds a rule for that key gives the lookup something to find. `Ambiguous` stays a refusal; the reversal covered `NoRule` only, and did not make "any tile you can see" narrower for no gain. Changing `Ambiguous` would be a separate decision, and this amendment does not make it.

### The receipt (issue #340)

Every one of the 28 F12.2 cold-read sessions said the same thing: nothing after the copy said it had worked, or which tile it took. Both copy actions in all three viewers now return a result carrying a one-line receipt, and every call site puts it on ADR-0167's OSD toast — the same seam the other debugger actions use (`ShortcutHandler`'s layer toggles, `LiveRecordingSession`'s failures), which also means the headless HUD capture can see it. A success names what was copied, the index or the CHR RAM shape, the palette, and the scanline the mapping came from; a refusal names the reason.

### A trace older than the last state load is refused (issue #419)

The per-scanline trace is not part of a Mesen save state: after a `LoadStateFile` it holds whatever the run before the restore left, so at least one frame must be rendered before it describes anything. The core records whether the traces describe a whole frame drawn since the last state load or reset (`Core/NES/NesScanlineTraceValidity.h`, fed by `NesPpu`): a restore through `NesPpu::Serialize` or a `Reset` marks them stale, and a frame traced from row 0 (the pre-render line's cycle 257) through scanline 240 marks them current. `GetNesScanlineTrace` returns that status — 0 unavailable, 1 current, 2 not drawn since the load — and still writes both buffers when it answers 2. `NesDrawnTileResolver.Resolve` is the one entry the copy actions use; it refuses a stale trace with `NotDrawnSinceLoad` in all three viewers, before anything resolves, and the receipt says how to get out: run one frame, or unpause. The receipt text is inline en-US (`resources.en.xml` is the only shipped UI resource; no receipt in this code path uses it). Serializing the ~31 KB trace into every state and rewind snapshot was rejected.

### What this adds

`DebugApi` could reach neither trace: `GetScanlineChrBankTrace` was exported only through `HeadlessCaptureNesSpriteLayer`. Two new exports in `InteropDLL/DebugApiWrapper.cpp` with their mirrors in `UI/Interop/DebugApi.cs`:

- `GetNesScanlineTrace(uint32_t* outScroll, uint32_t* outChrBank)` — the raw 240 and 240×32 traces. Raw rather than a scanline-aware `GetAbsoluteAddress`, because the decision they feed is a UI decision with its own host-free unit tests.
- `GetNesHdPackTilePalettes(...)` — the palettes the loaded pack keys a tile under, which only the core holds (`NesConsole::GetHdData`, new).

## Context

`Copy as MEP sheet cell` and `Copy tile (HD pack format)` (both in `HdPackCopyHelper`, reached from the Tilemap, Tile and Sprite viewers) name a CHR ROM tile by its absolute CHR index: they hand the tile's **PPU-space** address to `DebugApi.GetAbsoluteAddress`, which lands in `BaseMapper::GetPpuAbsoluteAddress` and reads `_chrPages` — the CHR mapping in effect **at the instant the debugger asks**.

The runtime names the same tile differently: `HdBuilderPpu` resolves it inside `StoreTileInformation`, at the scanline that drew it, and stores `AbsoluteTileAddr / 16`. The per-scanline record of that mapping already exists and is published: `BaseNesPpu::_scanlineChrBankOffsets`, sampled at cycle 257 of each visible scanline, reachable through `GetScanlineChrBankTrace` (ADR-0169's "mid-frame CHR bank splits"). Nothing in the debugger path consults it.

The two disagree in two ways, measured in-process on 2026-09-19 against the F12.2 sweep's own save states, with the shipped copy action driven through the real Tilemap Viewer ViewModel:

| Game | mapper / CHR | paused `_chrPages` for the bg pattern table | what the visible scanlines drew under |
|---|---|---|---|
| Dr. Mario (1990) | MMC1, 32 KB | `$1000` → CHR `$01000` | all 240 scanlines: CHR `$00000` |
| Gauntlet (1988) | 206, 64 KB | pages `$1800`–`$1BFF` off by `$400` | uniform, one mapping |
| Lemmings (1993) | MMC1, 128 KB | `$1000` → CHR `$00000` | scanlines 0–206: CHR `$1A000`; 207–239: CHR `$00000` |
| Ninja Gaiden (1989) | MMC3, 128 KB | `$1000` → CHR `$10000` | scanlines 0–146: CHR `$1E000`; 147–239: CHR `$10000` |

- **A transient mapping seen through the pause.** Dr. Mario never splits mid-frame; every one of its 240 visible scanlines drew under CHR `$00000`. The frame is paused in vblank (scanline 240), where the game already wrote a different MMC1 CHR bank, so the debugger sees CHR `$01000` — one 4 KB bank, exactly 256 tiles, late. Gauntlet is the same shape on a 1 KB-banked mapper: 36 of its 960 on-screen cells are off by `$400` bytes.
- **A real mid-frame split.** Lemmings and Ninja Gaiden do switch banks mid-frame, and the paused mapping happens to be the *lower* band's, so every cell above the split is named with the wrong band's bank.

The copy path is faithful: over all 960 cells of Dr. Mario's frame the clipboard carries exactly `GetAbsoluteAddress(tileAddr).Address / 16`, zero mismatches — 149 distinct indices, every one in `256..511`, none below 256. Substituting the scanline trace turns each into its `-256` twin: cell `(0,0)` goes `508 → 252`, the number the F12.2 control experiment proved correct (the paste with `252` rendered 30 720 magenta pixels, exactly the 30 cells the copy table lists). Lemmings' `1 → 0x1A01` and Ninja Gaiden's `4346 → 0x1EFA` reproduce the issue's two other measurements to the digit.

The failure is silent by construction: a wrong-bank index is a well-formed key — `mep_build.py build` accepts it, `mep_lint.py` has nothing to object to, the renderer never matches it, so the only evidence is a frame that comes back pixel-identical. The viewer's own picture is drawn under the same paused mapping, so on a mid-frame-split game the image and the key agree with each other and both disagree with the frame the artist is looking at. Non-goals: changing `hires.txt` semantics (ADR-0005), changing what `HdBuilderPpu` stores (it is the reference), or touching the CHR RAM key, which is the tile's 16 bytes and has no bank in it.

The Context table above was taken by restoring each sweep save state and then resuming for ~300 ms of wall clock before reading the trace. The trace is **not** part of a save state. Re-measured live against `/Users/bihaiko/f12.2-opus-sandbox/frames/*.mss` with a deterministic `Step(n, PpuFrame)` instead of a sleep: Ninja Gaiden's `4346 → 0x1EFA` reproduced exactly on 352 cells (`4344 → 0x1EF8` on 138 more, phase-independent), Lemmings' `1 → 0x1A01` on the 7 cells the frame drew (776 cells refuse as `NotDrawnThisFrame`), and Dr. Mario's `508` reads `page $1000 → CHR $01000` on the paused state but every frame reachable from it reads `252` — swept `MESEN_DIAG_STEP_FRAMES` 1–5, identical, so it is not reproducible from a save state. The `252` stands on the F12.2 control experiment, not the harness; the contract on the rule is therefore the host-free unit tests.

## Consequences

- Both copy actions now emit the index the runtime drew on a CHR-banked game, and the F12.2 evaluators' workaround (paint, rebuild, look for magenta, try the index one bank away) is retired.
- The decision is covered by host-free unit tests that fail on the old behaviour (the contract, not the harness): `UI.Tests/Mep/NesDrawnTileResolverTests.cs` (Dr. Mario `508 → 252`, Lemmings `1 → 0x1A01`, Ninja Gaiden `4346 → 0x1EFA`, the empty set, the disagreeing set, scroll, the sprite rule, the Tile Viewer refusal), `UI.Tests/Mep/NesPackTilePaletteTests.cs` (Metroid `0F361506 → 0F0F0F0F`, the refusals, the Tetris 2 and NoRule cases), `UI.HeadlessTests/HdPackCopyReceiptTests.cs` (the receipt). `UI.HeadlessTests/ChrBankDiagnosticTests.cs` remains evidence, not a regression test, and now dumps a `drawn` line per cell with the paused and resolved index side by side.
- A refusal path is a new behaviour for these menu entries, which before either copied or silently no-opped; the receipt is part of the same change, so a refusal that says nothing does not trade a silent wrong key for a silent nothing.
- A tab showing a **mirrored** nametable now refuses, because `v` only ever names the logical nametable the game wrote to. This is the honest cost of OPEN 1 (a).
- Packs already recorded are unaffected: `HdBuilderPpu` was always right; only hand-copied keys — the F12.2 paint loop's whole premise — carried the defect.
- `NoRule` no longer refuses: on Tetris 2, tile `0x1170`, the pack held no rule under the live `0F281807` and a new cell with that key rendered **274 432** magenta pixels (268 cells of 32×32), while the held `0F0F0F0F` rendered 0 — the key the pack did not hold was the only one that matched. `Ambiguous` stays a refusal.

## Record

- 2026-09-19 — decided; the three **OPEN** either/ors picked by the user, one option each (OPEN 1 → (a); OPEN 2 → "keep the image as it is"; OPEN 3 → "the same rule in all three viewers"); implemented together with issue #342 (the live palette) and issue #340 (the receipt), which share one code path.
- 2026-09-20 — landed in PR #348; its body named this ADR accepted and implemented and stated the three picks, but paraphrased them, so the PR-body half of the same-turn rule was met in substance, not verbatim.
- 2026-09-24 — issue #419: a trace older than the last state load is refused (`NotDrawnSinceLoad`); F14.2's scan (`docs/validation/f14.2-cold-read-rescore-2026-09-24.md`, cause A) paid for the correction. Tests: `scripts/core_unit_tests.cpp`, `UI.HeadlessTests/CopyAfterStateLoadTests.cs`; the F12.2/F14.2 scan (`CopyAsMepSheetCellTests.cs`) now draws one frame after the load. Measurements: `docs/validation/issue-419-copy-after-state-load-2026-09-24.md`.
- 2026-09-24 — issue #431: a recorded palette is substituted only when palette RAM holds it (candidates). Tetris 2 keys 474 of its 518 `<tile>` rules under the all-black `0F0F0F0F`; Gauntlet behaved the same way (1 023 of 1 023 lines). Measurements: `docs/validation/issue-431-fade-palette-copy-2026-09-24.md`, `docs/validation/f14.2-rescore-after-419-421-2026-09-24.md`.
- 2026-09-24 — NoRule reversal, user's go-ahead *"aceito sua sugestao. pode aplicar e rodar em paralelo"*. `A_tile_the_pack_does_not_hold_is_refused` is replaced by `A_tile_the_pack_does_not_hold_copies_with_the_live_palette` and `A_sprite_the_pack_does_not_hold_copies_with_its_live_sprite_palette`; `Several_recorded_palettes_and_no_match_is_refused_with_the_list` still pins `Ambiguous` as a refusal. Tests: `UI.Tests/Mep/NesPackTilePaletteTests.cs`; measurements: `docs/validation/adr0215-norule-copy-2026-09-24.md`.
