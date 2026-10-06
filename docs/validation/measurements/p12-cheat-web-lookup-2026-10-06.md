# P.12 checked web cheat lookup: measurement (2026-10-06)

Issue #923, ADR-0245 section 4 (P.12). The tool is `scripts/cheat_web_lookup.py`.
Its tests are `scripts/test_cheat_web_lookup.py`.

## Fixed parameters

- **N = 120 frames** per side (`CHECK_FRAMES`). This is about two seconds of NTSC
  play. One session reads the "off" window from the minted state, and one
  session per candidate reads the "on" window from that same state.
- **Mint**: 601 frames from power-on, with no cheat, using `MINT_MACROS`: idle
  180, Start 1, idle 60, Right 120, Right+A 120, idle 120. The ROM is read by
  path. Only its sha256 appears in the output.
- **Pass rule**: in the "on" run, the target address holds the promised value
  in all N frames. In the "off" run, it differs in at least one frame. A
  multi-part code or a code with no internal-RAM target (`$0000-$07FF`) is
  never checked.

## Fixed set

The two libretro-database pages committed under `tests/fixtures/cheat-web-lookup/`
were byte-identical (`diff -q`) to the live
`raw.githubusercontent.com/libretro/libretro-database/master/cht/Nintendo - Nintendo Entertainment System/<Game> (USA).cht`
on 2026-10-06.

| Game | ROM sha256 (local copy) | entries | multi-part | no RAM target | undecodable | checked | passed |
|---|---|---:|---:|---:|---:|---:|---:|
| Castlevania (USA) | `c71be6ac…2882ebfc46` | 139 | 14 | 111 | 0 | 14 | 0 |
| Contra (USA) | `26541a55…67fa5e5519` | 59 | 13 | 31 | 0 | 15 | 0 |
| **Total** | | 198 | 27 | 142 | 0 | **29** | **0** |

**Pass rate: 0/29 checked codes (0/198 entries).**

Command, one per game (exit 0 for both):

```
python3 scripts/cheat_web_lookup.py --rom "<roms>/Castlevania (1987) (Konami).nes" \
  --game Castlevania --page-file tests/fixtures/cheat-web-lookup/castlevania-usa.cht \
  --binary <main checkout>/scripts/headless_record --work runs/p12/castlevania-work \
  --state runs/p12/castlevania.mss
```

Failure reasons over the 29 checked codes:

- `value-missing-on`: 25 codes (Castlevania 14, Contra 11).
- `off-unchanged`: 4 codes (Contra `00AB:00`, `0090:01`, `00B2:00`, `0030:00`). The
  address already held the value with the code off.

Castlevania's own "Always have 99 hearts" code (`0071:63`) scored `on 0/120, off 120/120`.

## Finding: the "on" side reads the wrong layer

The pass rate of 0 comes from the instrument, so it says nothing about the
codes. The core applies a `NesCustom` (RAM) code as a **CPU read intercept**:
`NesMemoryManager::Read` and `DebugRead` call `CheatManager::ApplyCheat`, and
nothing writes the value into RAM. The session's `ram` verb reads through
`HeadlessReadNesRam` (`InteropDLL/EmuApiWrapperHeadless.cpp`), which does a
`memcpy` of raw internal RAM. A code that works looks the same to that read as
a code that does nothing. Two results confirm this:

- A direct probe applied `0071:63`, and the session's own init line
  `cheat applied: 0071:63` confirms it was set. The probe still read `$0071 = 00`,
  both on a fresh run and after `loadfile`.
- Some "on" values match the promised value: `01A9`/`043D` at 79/120, and the 4
  Contra `off-unchanged` codes at 120/120. In each case the game's own raw value
  happened to equal the promised one, and the cheat had no part in it.

Until the runner has a cheat-aware read, the lookup offers nothing. The
check fails closed, so a code it cannot confirm is not offered. Fixing this
needs a change outside this slice: a session read that goes through
`DebugRead`, or a `ram` mode with that behavior, in `InteropDLL` and
`scripts/headless_record.cpp`. With that read, a code's "on" value always
matches whenever its compare byte matches. A person needs to decide whether
the rule in ADR-0245 Decision 4 should then measure something else, for
example a game-visible effect.
