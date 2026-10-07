# P.12 checked web cheat lookup: re-measurement under the amended rule (2026-10-07)

Issue #934, ADR-0245 Decision 4 as amended 2026-10-07 (PICK b′, ratified by the
owner's proxy GPT Astra fast). It replaces nothing: the 2026-10-06 log
(`p12-cheat-web-lookup-2026-10-06.md`, 0/29 under the original rule) stays as the
record of why the rule changed. The tool is `scripts/cheat_web_lookup.py`, and its
tests are `scripts/test_cheat_web_lookup.py`, with one test class per condition.

## What changed in the instrument

- **Pass rule (amended).** A code is `checked` only when all of these hold:
  (1) two "off" runs from the minted state record byte-identical traces, where a
  trace is the whole internal RAM (`$0000-$07FF`, raw) plus the frame checksum
  after each of N frames; (2) the "on" trace diverges from "off" within N;
  (3) the emulated CPU read the target address while the code could apply, which
  is at least one *hit*; (4) the "on" run neither crashes nor freezes; (5) anything
  undecided is `inconclusive` and not offered; (6) the scope is RAM only (one part,
  target in `$0000-$07FF`). The label stays "found online, checked on your copy".
- **Hit counter.** The new session verbs are `watch <addr>[:<cmp>]` and `hits`
  (`scripts/headless_record.cpp`, `InteropDLL/EmuApiWrapperHeadless.cpp`). They
  register a read handler over the target address. `NesMemoryManager::Read`
  (the CPU) calls `ReadRam` on that handler, while `DebugRead` calls `PeekRam`
  and the `ram` verb copies the array, so debugger and probe reads cannot count.
  A hit is a read whose raw byte meets the code's compare, which is the condition
  `CheatManager::ApplyCheat` substitutes under. The debugger's
  `MemoryAccessCounter` was not used, because attaching a debugger stops a session
  run from parking on its frame.
- **Fixed parameters, unchanged from 2026-10-06:** N = 120 (`CHECK_FRAMES`). The
  mint is the same 601-frame `MINT_MACROS` from power-on with no cheat, and the
  corpus is the same two committed libretro-database pages. Freeze = the whole RAM
  unchanged over the last 60 frames while "off" changes (`FREEZE_FRAMES`).

## Corpus and environment

- Pages: `tests/fixtures/cheat-web-lookup/castlevania-usa.cht`,
  `tests/fixtures/cheat-web-lookup/contra-usa.cht` (the same files as 2026-10-06).
- ROMs (local copies, read by path, never uploaded): Castlevania sha256
  `c71be6ac16e8eea7f867cd5437afd1449bef7c4834ec4ca273cafe2882ebfc46`, Contra
  sha256 `26541a5550ee22deeb3d5484e4a96130219b58cff74d068fb1eb6567fa5e5519`. These
  are the hashes the 2026-10-06 table lists.
- Build: branch `feat/934-p12-checked` on `origin/main` `2453fb574`, `make core`
  and `make capture-tool` (macOS arm64).
- Command, one per game (exit 0 for both: Castlevania in 96 s, Contra in 103 s):

```
python3 scripts/cheat_web_lookup.py --rom "<roms>/Castlevania (1987) (Konami).nes" \
  --game Castlevania --page-file tests/fixtures/cheat-web-lookup/castlevania-usa.cht \
  --binary scripts/headless_record --work runs/p12b/castlevania-work \
  --state runs/p12b/castlevania.mss
```

## Result

| Game | entries | multi-part | no RAM target | checked (candidates) | off/off | passed (`checked`) | not-read | no-effect | crashed | frozen | inconclusive |
|---|---:|---:|---:|---:|---|---:|---:|---:|---:|---:|---:|
| Castlevania (USA) | 139 | 14 | 111 | 14 | identical | 13 | 1 | 0 | 0 | 0 | 0 |
| Contra (USA) | 59 | 13 | 31 | 15 | identical | 8 | 3 | 4 | 0 | 0 | 0 |
| **Total** | 198 | 27 | 142 | **29** | | **21** | 4 | 4 | 0 | 0 | 0 |

**Pass rate: 21/29 candidate codes (72 %), 21/198 entries.** Under the original
rule it was 0/29.

The numbers in the table come from the `counts` object of each run's JSON on stdout:

```
castlevania: determinism=identical {'entries': 139, 'undecodable': 0, 'multi_part': 14, 'no_ram_target': 111, 'checked': 14, 'passed': 13, 'reasons': {'checked': 13, 'not-read': 1}}
contra:      determinism=identical {'entries': 59, 'undecodable': 0, 'multi_part': 13, 'no_ram_target': 31, 'checked': 15, 'passed': 8, 'reasons': {'checked': 8, 'not-read': 3, 'no-effect': 4}}
```

The 4 Contra `no-effect` codes (`00AB:00`, `0090:01`, `00B2:00`, `0030:00`) are the
same 4 codes that were `off-unchanged` on 2026-10-06. The game reads them hundreds
of times, but the address already held the promised value, so nothing changed. The
amended rule fails them as well, for a better reason. The 4 `not-read` codes
(`0070:02`, `0033:09`, `0024:01`, `0032:09`) target bytes the game never read in
this window.

## Raw evidence: per-code lines (stderr of each run, verbatim)

```
minted runs/p12b/castlevania.mss (601 frames)
checking 14 code(s), 120 frames each side
[1/14] PASS 0071:63 -> $0071 = 63 (checked: diverged at 80, reads 1, hits 1)
[2/14] PASS 002A:05 -> $002A = 05 (checked: diverged at 98, reads 1, hits 1)
[3/14] PASS 0071:63 -> $0071 = 63 (checked: diverged at 80, reads 1, hits 1)
[4/14] PASS 0042:32 -> $0042 = 32 (checked: diverged at 80, reads 32, hits 32)
[5/14] PASS 0045:3D -> $0045 = 3D (checked: diverged at 80, reads 45, hits 45)
[6/14] PASS 002A:05 -> $002A = 05 (checked: diverged at 98, reads 1, hits 1)
[7/14] PASS 01A9:00 -> $01A9 = 00 (checked: diverged at 80, reads 21, hits 21)
[8/14] PASS 005B:02 -> $005B = 02 (checked: diverged at 100, reads 154, hits 154)
[9/14] PASS 001A:00 -> $001A = 00 (checked: diverged at 1, reads 72088, hits 72088)
[10/14] fail 0070:02 -> $0070 = 02 (not-read: diverged at None, reads 0, hits 0)
[11/14] PASS 015B:08 -> $015B = 08 (checked: diverged at 80, reads 20, hits 20)
[12/14] PASS 0560:FF -> $0560 = FF (checked: diverged at 101, reads 124, hits 124)
[13/14] PASS 0064:02 -> $0064 = 02 (checked: diverged at 98, reads 1, hits 1)
[14/14] PASS 043D:00 -> $043D = 00 (checked: diverged at 99, reads 24, hits 24)
minted runs/p12b/contra.mss (601 frames)
checking 15 code(s), 120 frames each side
[1/15] PASS 00AE:41 -> $00AE = 41 (checked: diverged at 1, reads 1057, hits 1057)
[2/15] fail 0033:09 -> $0033 = 09 (not-read: diverged at None, reads 0, hits 0)
[3/15] fail 0024:01 -> $0024 = 01 (not-read: diverged at None, reads 0, hits 0)
[4/15] fail 00AB:00 -> $00AB = 00 (no-effect: diverged at None, reads 333, hits 333)
[5/15] fail 0090:01 -> $0090 = 01 (no-effect: diverged at None, reads 712, hits 712)
[6/15] fail 00B2:00 -> $00B2 = 00 (no-effect: diverged at None, reads 2068, hits 2068)
[7/15] fail 0030:00 -> $0030 = 00 (no-effect: diverged at None, reads 408, hits 408)
[8/15] PASS 00AE:77 -> $00AE = 77 (checked: diverged at 1, reads 1057, hits 1057)
[9/15] PASS 00AA:16 -> $00AA = 16 (checked: diverged at 1, reads 999, hits 999)
[10/15] fail 0032:09 -> $0032 = 09 (not-read: diverged at None, reads 0, hits 0)
[11/15] PASS 00AE:4D -> $00AE = 4D (checked: diverged at 1, reads 1057, hits 1057)
[12/15] PASS 00B0:FE -> $00B0 = FE (checked: diverged at 1, reads 756, hits 756)
[13/15] PASS 00AF:4D -> $00AF = 4D (checked: diverged at 1, reads 363, hits 363)
[14/15] PASS 00B1:FE -> $00B1 = FE (checked: diverged at 1, reads 257, hits 257)
[15/15] PASS 0031:02 -> $0031 = 02 (checked: diverged at 34, reads 8, hits 8)
```

## Controls (the instrument, not the codes)

`runs/p12b/diag.py` played the same Castlevania state and compared each trace with
the "off" trace (output verbatim):

```
no cheat, watch $07FF: diverged_at=None counts=(1, 1)
no cheat, watch $0071: diverged_at=None counts=(1, 1)
inert cheat 0071:63:FE (compare never met), watch $0071: diverged_at=None counts=(1, 0)
0071:63, watch $0071: diverged_at=80 counts=(1, 1) ram-diff=['0x6f', '0x721', '0x722'] frame-diff=False
```

- An armed counter with no cheat leaves the trace identical, so the handler itself
  changes nothing.
- A cheat whose compare is never met (`0071:63:FE`) is read once, with 0 hits and no
  divergence. A loaded cheat that never applies does not count as an effect.
- `0071:63` ("Always have 99 hearts") diverges at frame 80, in RAM only
  (`$006F`, `$0721`, `$0722`), right after the single CPU read of `$0071`. On
  2026-10-06 it scored `on 0/120`.

`runs/p12b/probe.py` showed that probe reads never count (output verbatim):

```
cheats=[] after 500 probe reads, 0 frames: reads,hits=(0, 0)
cheats=[] after 60 frames: reads,hits=(0, 0) raw $0071=00
cheats=['0071:63'] after 500 probe reads, 0 frames: reads,hits=(0, 0)
cheats=['0071:63'] after 60 frames: reads,hits=(0, 0) raw $0071=00
```

500 `ram` reads with no frame played leave the counter at 0. The 60-frame window
from the state has no CPU read of `$0071`, which matches the lookup's single read
at frame 80 of 120.

## What "checked" does and does not show

Many Castlevania codes diverge at the same frame (80). That is the first point
after the state where the game reads these status bytes. The controls above rule
out the instrument as the cause. A `checked` code is one that the game read and
that made the game play differently on this copy, with no crash or freeze. It does
**not** show that the effect is the one the description promises. For example,
`001A:00` diverges at frame 1 with 72 088 hits, which is a byte the game reads
constantly, and the check cannot tell whether "the promised effect" is what changed.
The label says "checked", never "works" or "safe" (ADR-0245 Consequences, as
amended). Wiring the lookup into the cheats sheet is #924 and is not part of this
measurement.
