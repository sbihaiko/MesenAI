# F9.25 — bounded Contra second-pass matrix (2026-09-15)

Scope and stop rule live in the PRD (Part A §4, "F9.25 scope and stop rule"):
inventory the nine Contra states, log for each a clean control and either a
second pass or a justified not-applicable result, and stop when every row has
evidence or an explicit reason and the union rebuild passes structural
validation. ADR-0184 owns what a second pass may be (RAM-only cheat, per §1;
coverage feeds only maps and pattern pages; navigation feeds all four) and
ADR-0182 owns why the list stops at stage 4.

## Binary and inputs

- `scripts/headless_record` sha256 `511ba70f…7afa927`, the binary rebuilt at
  `main` HEAD `04d7fc63`; last `Core/`/`InteropDLL/` commit `f68093ec`. This
  is the same recorder the F9.18-V log measured — the two logs are
  commensurable.
- ROM `Contra (1988) (Konami).nes`, sha256 `26541a5550ee22de…fa5e5519`,
  sha1 `c9ea66bb7cb30ad5343f1721b1d4d3219859319b` — the hash
  `scripts/stages/contra/navigation.json` pins, and the ROM the nine states
  were minted against. (The same ROM is named `Contra (USA).nes` elsewhere,
  e.g. the F9.18-V log; `roms/spike-contra80s/live-validate-test/Contra
  (USA).nes` is a *different* dump, sha256 `929ea4fa…`, and is not used here.)
- Reference pack for every coverage number below: Contra80s 1.1,
  `roms/spike-contra80s/mesen-home/HdPacks/Contra (USA)/hires.txt`, sha256
  `ed68f6f9fd3e96f1…21d9996f8`, 3404 distinct `tileData` — the metric and the
  parser are `scripts/artist_cover.py`'s, imported by
  `record_navigation_sweep.py` so the acceptance number cannot drift from the
  one ADR-0184 quote.
- States: `runs/golden-20260913-f922/contra/stages-fresh/<state>.mss`
  (unversioned; a CHR RAM state carries the game's graphics). `stage3-waterfall`
  is the one state a fresh checkout cannot re-mint (README, "Reproducing the
  states") — it is present here, so no row is blocked for a missing state.
- Artifacts: `runs/f925-20260915/contra/{clean,sweep,coverage,map,kit-union}/`
  plus `sweep/sweep.json` (the sweep's own record), local and not versioned.

## 1. Clean control — nine states on one binary

`scripts/record_stages.sh` re-ran the nine state+script pairs, 60 s each, no
cheat (ADR-0184 §2 first row), one pack per state. Pose counts reproduce the
2026-09-13 measurements in `scripts/stages/README.md` exactly, which is the
check that these are the same recordings and not new behaviour.

| state | `.mss` sha256 | script sha256 | tileData | keys | frames | wall s | poses / cycles |
|---|---|---|---|---|---|---|---|
| stage1-run | `5f6ceccb0034268f` | `7ec47be41cd653c6` | 1812 | 1956 | 4509 | 13.1 | 51 / 2 |
| stage1-water | `6202fc32fe0cac0e` | `485c8b765f79d795` | 1676 | 1756 | 4810 | 13.8 | 13 / 0 |
| stage1-2p | `812e4d3995eadf2a` | `0f0ec38408823133` | 1923 | 2222 | 6012 | 14.7 | 311 / 11 |
| stage1-boss | `037ae636464bf943` | `b6f338c3a03cf67b` | 1839 | 2040 | 9447 | 14.5 | 69 / 3 |
| stage2-base | `ee32eff654827ac3` | `bba631150eaf1bb0` | 1820 | 2239 | 10607 | 14.8 | 163 / 10 |
| stage3-waterfall | `1e6ed6ac184cdbe4` | `b252c233c394a3bf` | 1802 | 2074 | 19473 | 15.1 | 252 / 12 |
| stage3-boss | `5bdb3af589e39bbc` | `da7413c986c4643e` | 1747 | 1910 | 30875 | 14.8 | 517 / 12 |
| stage4-base | `23784bdfe3f6a641` | `bba631150eaf1bb0` | 1861 | 1945 | 52493 | 15.1 | 291 / 17 |
| stage4-boss | `fedc96e5ebb9b84e` | `da7413c986c4643e` | 1811 | 1947 | 71450 | 15.7 | 255 / 12 |

Two script hashes repeat by design, not by accident: `stage2-base.txt` and
`stage4-base.txt` are the same script (`bba63115…`), as are `stage3-boss.txt`
and `stage4-boss.txt` (`da7413c9…`) — README, "The second base (stage 4)".
`stage4-boss` reads 255 poses here against the README's 181: that row was
measured over 40 s, these runs are 60 s.

Pack `textures/hires.txt` sha256, per state: `0751a38047c84197` (stage1-run),
`aa6a40128a65c986` (stage1-water), `9ce4e7dc9c0e8a23` (stage1-2p),
`b7d0c239870c4f12` (stage1-boss), `5de6fda5c7500406` (stage2-base),
`e93d1b530df1e282` (stage3-waterfall), `b5feb2b482317674` (stage3-boss),
`376da145696030c4` (stage4-base), `26370d86b93f27f2` (stage4-boss).

## 2. Second pass

### 2.1 `stage1-run` — a coverage pass (ADR-0184 §2 second row)

The stage-1 row is where ADR-0184's cheat ranking was measured, so it is
re-measured here on the current binary rather than cited. Conditions are
matched: the same 300 s script (entry `mint-stage1-30lives.txt`, body
`stage1-run.txt` repeated six times = 20 821 frames) with nothing but the
cheat list changed. The uncheated 300 s run is the sweep's `stage1` session
(`0030:00`, §2.2) — ADR-0184 §"Amended 2026-09-14" measured that identity warp
as byte-identical to its uncheated control.

| run | cheat | tileData | keys | ΔtileData | Δkeys | wall s | hires sha256 |
|---|---|---|---|---|---|---|---|
| clean, 300 s | `0030:00` | 2206 | 2580 | — | — | 85.0 | `41d4e665467b5dd2` |
| coverage | `0032:99` | 2197 | 2568 | +1 / −10 | +1 / −13 | 99.8 | `31b1ffd71d384c8a` |
| coverage | `0032:99` + `00B0:FE` | 2224 | 2697 | +32 / −14 | +154 / −37 | 75.2 | `759af3dee12c4671` |

The ADR's conclusion reproduces: **lives alone buy survival and no extra
ground** (+1 key against the clean run), the **barrier buys variety**
(+154 keys) and not completeness, and a cheated run is not a superset of its
clean twin — 14 `tileData` present in the clean run are absent from the
barrier run, the same direction as the ADR's CHR figure (93 % clean against
92 % with the barrier). The barrier run is admissible for maps and pattern
pages only (ADR-0184 §2), and the two coverage packs are excluded from figures
and scenery in §4 below.

### 2.2 Navigation passes (ADR-0184 §"Amended 2026-09-14")

`scripts/record_navigation_sweep.py` ran the whole profile as eleven sessions
of 300 s: eight level values of `$0030` and the three boss rooms. This is the
`0030:01` / `02` / `03` evidence the three remaining measurable rows need; the
other five values are measured by the same command and reported for a complete
matrix, not as new objectives (ADR-0182 stops at stage 4).

| session | cheat | wall s | tileData | of ref (3404) | % | hires sha256 |
|---|---|---|---|---|---|---|
| stage1 | `0030:00` | 85.0 | 2206 | 961 | 28.2 | `41d4e665467b5dd2` |
| stage2 | `0030:01` | 92.8 | 2094 | 849 | 24.9 | `d1a7cd7b415a3555` |
| stage3 | `0030:02` | 84.5 | 1948 | 703 | 20.7 | `5f990a9ddb86b422` |
| stage4 | `0030:03` | 87.5 | 2156 | 911 | 26.8 | `3897351fa3749ae3` |
| stage5 | `0030:04` | 98.1 | 2114 | 856 | 25.1 | `c498092f018bf51a` |
| stage6 | `0030:05` | 92.9 | 2094 | 849 | 24.9 | `438d11b7793852a2` |
| stage7 | `0030:06` | 93.6 | 2113 | 868 | 25.5 | `c4ed2a0343476c73` |
| stage8 | `0030:07` | 91.0 | 1966 | 721 | 21.2 | `ba8b21edafdf6328` |
| stage1-boss (room, no cheat) | — | 103.0 | 1839 | 594 | 17.5 | `906200978b2c557c` |
| stage3-boss (room, no cheat) | — | 96.0 | 1778 | 533 | 15.7 | `b18a17f79fef3e33` |
| stage4-boss (room, no cheat) | — | 104.3 | 1842 | 596 | 17.5 | `3e6323bef1a73176` |

Two cross-checks that this measurement is the same quantity the project has
measured before. The eleven-session union covers **1928 of the reference's
3404 tiles = 56.6 %** by the sweep's own number; recomputing it independently
from the eleven `hires.txt` files with `artist_cover.parse` gives the same
1928 and 56.6 %. And the `stage1-boss` room session's 594 reference tiles is
exactly the `stage1-boss | 594` row of the 2026-09-13
`runs/golden-20260913-f922/contra/artist-cover.md`, measured by a different
tool on a different day.

ADR-0184 reports **58.9 %** for its own eleven sessions (2026-09-14). Ours is
2.3 points lower — 77 tiles. The ADR's session artifacts do not survive on
disk, its reference revision and binary are not named, and no re-run of the
2026-09-14 command is possible, so the difference is recorded as unreconciled
rather than explained away. Both numbers are the same metric over the same
reference pack.

## 3. Not-applicable rows

Five rows have no second pass, each for a reason the PRD names — no admissible
cheat is known, or the clean pass already reaches its route boundary.

| state | why no second pass |
|---|---|
| `stage1-water` | `$0030` selects a *level*: the published map declares `0x00`–`0x07` and `0x09` only. The underwater stretch is a place inside stage 1 (Bill walks left into the water at x = 25), which no value of a level selector produces, and the vendored database names no RAM-only code for it. The clean pass reaches its route boundary (`stage1-water.txt`, 78 lines). |
| `stage1-2p` | Two-player mode is a title-screen Select press, not a RAM level pin; the profile's selector cannot enter it and no RAM-only code in the vendored database selects it. The clean pass reaches its route boundary (`stage1-2p.txt`, 52 lines). |
| `stage1-boss` | A room, not a level: `$0030` cannot address it (the profile's own `rooms[]` comment). Its session carries no cheat at all, so under ADR-0184 §2 first row it is a clean pass and every surface may read it — measured as a 300 s session in §2.2. |
| `stage3-boss` | Same room reason; measured as a 300 s session in §2.2. |
| `stage4-boss` | Same room reason; measured as a 300 s session in §2.2. |

The room rationale was previously only a comment inside
`scripts/stages/contra/navigation.json` (an unpublished data file); it is
restated here so the row's reason is part of the evidence rather than of the
tooling. The three rooms are also the rows where a navigation pass and a clean
pass coincide, which is why they carry two recordings each: the 60 s clean
control of §1 and the 300 s room session of §2.2.

## 4. Panorama extent — the map surface

`artist_map.py` needs a grid dump, and no 2026-09-13 recording produced one
(`MESEN_SHEET_GRID_DUMP` was not set), so the six sessions below were
re-recorded on the same binary with the same input script and the same cheats,
differing only in that variable. Each dump is 132–213 MB and none is
versioned.

| session | cheat | grid sha256 (16) | panorama | wall s |
|---|---|---|---|---|
| clean-300s | `0030:00` | `ce0bec44a8aff9d4` | 2512×240 + 464×936 | 79.0 |
| coverage-lives | `0032:99` | `380d5c218ecf899d` | 2512×240 + 464×936 | 82.4 |
| coverage-barrier | `0032:99` + `00B0:FE` | `3e9a0f224fdae2bf` | 2512×240 + 464×936 | 90.3 |
| nav-stage2 | `0030:01` | `7af425600753ceee` | 464×936 | 96.2 |
| nav-stage3 | `0030:02` | `1a5129fc665d66d0` | 464×936 | 84.9 |
| nav-stage4 | `0030:03` | `957c8c6c5c7fdb31` | 464×936 | 86.9 |

Two results, one of each kind.

**The cheat buys no map extent.** All three stage-1 variants stitch the
*identical* 2512×240 + 464×936, cheat or no cheat, at 300 s with the body
repeated. This reproduces ADR-0184's own correction of its first measurement
("repeating the script's body to cover the whole run reaches the same
2512×240, with no cheat at all") on a different binary: at 300 s of effective
input, 2512 is the wall the blind script cannot pass, whatever keeps the player
alive. The barrier's extra 208 px from the 2026-09-13 table was a 55 s-input
artefact, and the coverage pass's gain here is in `tileData` (§2.1), not in
ground.

**The navigation passes are one screen deep.** Stages 2, 3 and 4 each stitch
464×936 — a single screen column. The warp did load each stage (their tile
sets hold 202, 39 and 244 reference tiles the stage-1 session never exhibits),
but the sweep's body script is the profile's `defaults.body`,
`stage1-run.txt` — the right input for stage 1 and no input at all for a base
or a waterfall — so the recording stays on the warped stage's entry screen.
The three rows therefore report that screen's extent, not a traversal, and
reading 464×936 as these stages' panorama length would be a misreading. The
sweep buys *places*, not distance — which is what ADR-0184's "What a
navigation pass does not record" already says of a pinned selector.

`artist_map.py --verify` exits 0 on all six with 0 errors and 0 keys lost
(added: 48 / 46 / 45 for stage 1 and 3 for each navigation pass — the map
surface adds rules for cells the panorama addresses, which is what a painting
surface is for).

## 5. Union rebuild and structural validation

The union is a set union of the recordings, and the tooling defines that union
per surface rather than globally — an asymmetry this run measured rather than
assumed.

| step | command | result |
|---|---|---|
| pattern pages (union of 22 packs) | `artist_chr_kit.py <clean/stage1-run> --also <21 other packs> --rom … --out … --verify --fill-rules none` | exit 0; keys 1956 → 1956, **0 lost, 0 invented**, `rebuildIdentical: True`, 1956 cells checked and 0 differing; CHR completeness 94 % of the 512-tile bank — recorded 246, **donated 142 by the other 21 recordings**, ROM fill 91, unrecoverable 33 |
| figures (9 clean packs) | `artist_kit.py <pack> --verify` × 9 | all exit 0, 0 lost / 0 added each |
| scenery (9 clean packs) | `artist_bg_kit.py <pack> --verify` × 9 | all exit 0, 0 lost / 0 added each |
| flagship kit | the three generators into one kit dir, then `artist_kit_assemble.py` | 3 parts, 42 files, 4702 cells; `kit.json` + `ARTIST.md` written |
| painted pack | `mep_build.py build` on the recording with the kit's sheets and CHR pages dropped in, then `mep_lint.py` | **build exit 0, lint exit 0, 0 errors** (20 warnings, all the tool's own sheet-size note) |

Provenance held: the two coverage packs fed the pattern-page union only, never
figures or scenery (ADR-0184 §2). Two tooling facts fell out and are worth
fixing separately rather than working around: `artist_chr_kit.py --also`
refuses the pack that is also the positional argument (`error: --also …: that
is the pack itself, not a second recording of it`) instead of ignoring it, and
the union exists only where a flag defines it — figures and scenery are
per-recording surfaces with no `--also`, so their row above is nine
independent verifications, not one merged artifact. Both are recorded in
`runs/f925-20260915/contra/kit-union/RESULTS.md`.

## 6. Verdict

**F9.25's stop rule is met.** Nine rows, each with a hash-identified clean
control on one binary; four with a measured second pass (`stage1-run` by
coverage, `stage2-base` / `stage3-waterfall` / `stage4-base` by navigation);
five with a reason that names why no admissible pass exists (`stage1-water`,
`stage1-2p`, and the three boss rooms, whose sessions carry no cheat and so are
clean passes feeding all four surfaces). The union rebuild passes structural
validation: every generator's `--verify` is 0-lost/0-invented, the CHR union
takes 142 donated cells from the 21 other recordings, and the painted pack
builds and lints with 0 errors.

What this row does **not** claim:

- **No target of coverage.** The eleven-session union is 56.6 % of the
  reference pack's tiles and all 22 recordings together are 58.5 % (1993 of
  3404; the nine clean controls add 59 reference tiles the sweep lacks, the
  two coverage passes 6). ADR-0184 reports 58.9 % for its own eleven sessions.
  The 2.3-point gap is **unreconciled**: the 2026-09-14 artifacts are not on
  disk and its reference revision and binary are unnamed. Both figures are the
  same metric (`artist_cover.parse`, via `record_navigation_sweep.py`) over
  the same reference pack, whose sha256 is named above — a future run can
  settle it; this one cannot.
- **No panorama beyond the entry screen** for the three navigation rows (§4),
  and no stage beyond 4 (ADR-0182).
- **Nothing about the human panel.** F9.18 is untouched by this log.

The evidence is local and not versioned: `runs/f925-20260915/contra/{clean,
sweep,coverage,map,kit-union}/`, with `sweep/sweep.json` as the sweep's own
machine-readable record. The nine `.mss` states are the archived-only artifacts
of `runs/golden-20260913-f922/`; `stage3-waterfall.mss` still cannot be re-minted
from a fresh checkout, so this row's `stage3-waterfall` evidence is as durable
as that one file.
