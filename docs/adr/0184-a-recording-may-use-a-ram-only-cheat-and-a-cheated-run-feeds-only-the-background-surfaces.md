# ADR-0184: A recording may use a RAM-only cheat, never a PRG patch, and a cheated run feeds only the background surfaces

- Status: accepted (2026-09-13, at the user's direction: "só cheat de RAM,
  nunca patch de PRG"; §1 is implemented as `scripts/headless_record`'s
  `cheat=` flag and §2 is measured, see "Measured 2026-09-13" below)
- Date: 2026-09-13
- Related: ADR-0183 (the artist kit — §3 "evidence and inference are never
  confused" is what this protects), ADR-0182 (recording coverage), ADR-0178
  (sheet sidecar / tile keys), ADR-0173 (`screenFixed` HUD classification),
  ADR-0159 (the grid dump the panorama is stitched from), F9.22 (the gameplay
  search that crossed Contra's stages)

## Context

Recording coverage is capped by how far a scripted run gets before it dies.
Measured on Contra stage 1: `scene/screen001.png` of the `stage1-run` pack is
the game's GAME OVER screen, and doubling the traverse from 150 s to 300 s
produced a panorama of the *identical* 2304 px width against the reference
pack's 3348 px — the run dies and respawns in the same stretch. The same wall
caps two other surfaces: 157 of Contra's 512 CHR RAM cells per stage bank are
never recorded because the game only unpacks them further in, and an enemy that
first appears past the death point has no pose at all.

Cheat codes are the obvious lever, and they are also the obvious way to poison
the archive. Two distinct hazards, and they are not the same hazard:

**Hazard 1 — the cheat changes the bytes we record as art.** Every NES Game
Genie code is a PRG-space code by construction: the device builds a 15-bit
address with bit 15 forced to 1, so it addresses `$8000–$FFFF` and cannot reach
RAM at all. On a CHR RAM game the pattern data is unpacked out of PRG at run
time — Contra (UNROM) and Zelda 1 (mapper 1, 0 KB CHR) are both CHR RAM — so a
substituted PRG byte can travel through the game's own loader into CHR RAM and
be hashed into `TileData` by the HD pack builder as if it were the game's real
art. On a CHR ROM game such as Mega Man 3 a PRG patch cannot alter tile bytes,
but it can alter *which bank is selected*, which corrupts the stitch instead.

**Hazard 2 — the cheat changes what the game legitimately draws.** This one is
not about ROM bytes at all, and a RAM-only rule does not fix it. Contra's
`$00B0`, shipped in the vendored cheat database as "Invincibility (star
effect)", is the **Barrier** power-up timer: the game draws the barrier's own
sprites and cycles the player's palette for as long as it runs. Held on for a
whole recording, the figure grids would archive the player wearing a shield in
every frame — real art, honestly recorded, of a state the game almost never
shows. Zelda's `$04F0` is documented as "Link Invulnerable Timeout (palette
dependent on value)": the same failure, in a source rather than a hypothesis.
Blink-style invulnerability (Castlevania `$005B`, Contra `$00AE`) hides the
sprite on alternating frames.

So "use invincibility" is not a decision that can be taken once for all
surfaces. It is safe for some and ruinous for others, and which is which
follows from where each surface's evidence comes from.

Non-goals: this ADR does not add a cheat UI, does not change the emulator's
`CheatManager`, and does not decide which code each game uses — that is data,
and it belongs beside the stage scripts.

## Decision

### 1. A recording may carry a cheat only as a RAM-address code

The only accepted cheat type for a recording is `NesCustom` — the
`AAAA:VV[:CC]` form that `CheatManager::ConvertFromNesCustomCode` stores with
the address taken verbatim — **and only with `AAAA < 0x0800`**, the NES's
internal RAM.

`NesGameGenie` and `NesProActionRocky` are rejected outright. Both force the
decoded address into PRG space (`+ 0x8000`), so no code of either type can ever
satisfy the rule; rejecting the *type* and rejecting every address above
`0x07FF` are two checks that must both be present, because a `NesCustom` code
may also carry a PRG address.

The rule is enforced by the parser, not by discipline. A recording harness that
accepts a cheat MUST refuse the run — not warn — when a code fails either
check, and MUST name the offending code.

This is not a claim about how the emulator applies cheats. Mesen substitutes
the byte in flight on the CPU read bus (`CheatManager::ApplyCheat`, called from
`NesMemoryManager::Read`) and never writes the PRG buffer. The rule is about
which *address space* the game is reading a different value from, because that
is what decides whether altered bytes can reach the tile data.

### 2. A cheated run is a second pass, and only the background surfaces read it

Each stage is recorded twice, and the two recordings are not interchangeable:

| pass | cheat | which of ADR-0183's four surfaces may be built from it |
|---|---|---|
| **clean** | none | figures (`sheets/usr*.png`), scenery (`sheets/`, `scene/`) |
| **coverage** | RAM-only, per §1 | stage maps (`map/`), pattern pages (`chr/`) |

The split follows the evidence, not taste. A panorama is stitched from the
background grid stream (ADR-0159, `MESEN_SHEET_GRID_DUMP`), which carries
nametable content; the player's sprite is not in it, so a cheat that recolours
the player or draws a shield around it cannot reach a panorama cell. A pattern
page is CHR bank content, equally out of reach. Figures and scenery come from
the OAM and background vocabularies the recorder builds per frame, and those
*do* see the barrier sprite and the swapped palette.

A kit fragment built from a coverage pass MUST record the cheat in its
`notes[]`, verbatim and with its address, so the provenance travels with the
art. A kit MUST NOT mix a clean and a coverage pass inside one surface.

### 3. Prefer the cheat that changes least

Ranked, and this order is the decision, not advice:

1. **Game code the developers shipped.** Contra's Konami Code (30 lives) is
   input, not a cheat: it writes the game's own variable through the game's own
   routine. `scripts/stages/contra/mint-stage1-30lives.txt` already does this.
   Nothing beats it on fidelity and it needs no code change.
2. **A counter the HUD renders.** Lives, continues, hit points — Contra
   `0032:99`, Castlevania `002A`, Zelda `0670:FF`, Mega Man 3 `00A2`/`00AE`.
   These change nothing drawn except HUD digits, which are real tiles the game
   draws anyway and which ADR-0173 already classifies as `screenFixed`.
3. **An invulnerability timer** — only in a coverage pass, never in a clean
   one, and only after checking what the game draws while it runs.
4. **Nothing else.** A cheat whose value the game itself never produces is
   refused however admissible its address: the vendored database's Contra
   `00AA:18` "Clone Attack (glitch weapon)" is a RAM write and is garbage.

### 4. At least one clean run must die

An immortal run never records the death animation, the respawn, or the GAME
OVER screen, and never loads a CHR bank that only a death brings in. Those are
real surfaces — the Contra stage 1 kit holds the GAME OVER screen today
precisely because the run dies. A stage's clean pass is therefore allowed to
end in death and MUST NOT be "fixed" by carrying the coverage pass's cheat.

### 5. No code is invented

A cheat used by a recording comes from a source that names it: the database
vendored at `UI/Dependencies/Internal/CheatDb.Nes.json` (keyed by ROM SHA-1),
or a published RAM map cited beside the stage script. A game for which no
RAM-only code can be verified simply does not get one — Excitebike is the
measured case, and it needs none: it has no lives and no death, so its coverage
is gated on which tracks and modes are driven, not on survival.

## Consequences

- **Recording cost roughly doubles for a stage that needs a coverage pass.**
  A stage whose clean run already reaches the end needs no second pass; the
  split is per stage, not global.
- **A harness change is required before any of this runs.** The capture tool
  does not accept a cheat today: the Core never reads a cheat file (the
  per-game `Cheats/<rom>.json` is read only by the C# GUI), no config field or
  env var enables one, and the headless harness does not declare `SetCheats` in
  its `extern "C"` block. The smallest honest change is a `cheat=` flag on the
  harness that declares `SetCheats`, validates per §1, and calls it **after**
  `LoadRom` and after any state load — `Emulator::LoadRom` clears the cheat list,
  so a call placed before it silently does nothing.
- **Provenance becomes part of the kit contract.** `kit-part-<part>.json` gains
  nothing structurally — the cheat goes in `notes[]` — but a reviewer can no
  longer tell a clean fragment from a coverage fragment by its shape alone. The
  note is the only record, so omitting it is a defect, not an oversight.
- **Invulnerability is a coverage trade, not a free win.** It buys panorama
  length and CHR cells and it costs the death-and-respawn material. §4 exists
  so that cost is paid deliberately in one pass rather than silently in both.
- **The rule is checkable and should be checked.** `AAAA < 0x0800` plus a type
  allow-list is two comparisons; a test that feeds a Game Genie code and a
  `NesCustom` code at `$8000` and asserts both are refused is cheap, and it is
  what keeps the rule from decaying into a comment.

## Measured 2026-09-13

The flag landed as `cheat=AAAA:VV[:CC]` on `scripts/headless_record`, validated
by `parseRamCheat()` before the emulator is touched. `SXKVPZAX` is refused as a
Game Genie letter code; `8000:99` is refused by address; `0032:99` and
`0032:99:FF` are accepted. A refusal ends the run — §1 says refuse, not warn,
and a run recorded under a PRG patch is evidence nobody can tell apart from the
real thing afterwards.

**A silent-failure trap that this ADR's own rule would not have caught.** The
first three cheated runs produced grid dumps byte-identical to an uncheated
one. The harness's ABI mirror declared `uint32_t Type` while `CheatType` is
`enum class CheatType : uint8_t` — 20 bytes against the Core's 17, so both the
array stride and the offset of `Code[]` were wrong and `CheatManager::AddCheat`
refused every code without a sound a headless run can hear. A
`static_assert(sizeof(CheatCodeAbi) == 17)` now stands where the assumption
was. A cheat that changes nothing is indistinguishable from a cheat that does
not help, so a cheated run must be diffed against its uncheated twin before its
numbers are believed.

**§2 and §3 measured on Contra stage 1**, same state, same input script, 300 s:

| run | frames stitched | panorama |
|---|---|---|
| clean | 3197 | 2304x240 |
| `0032:99` (99 lives) | 4074 | 2304x240 |
| `0032:99` + `00B0:FE` (barrier) | 4096 | **2512x240** |

Lives alone buy survival and no extra ground: a blind "hold right" script dies
against the same obstacle, and more lives only grant more attempts at it.

**And the barrier's 208 px were not the barrier's.** Contra's `stage1-run.txt`
is 3300 frames — 55 s of input inside a 300 s run, so for 82% of the recording
nothing was pressed and the player simply stood. Repeating the script's body to
cover the whole run reaches **the same 2512x240, with no cheat at all**:

| run | input | cheat | panorama |
|---|---|---|---|
| clean | 55 s | none | 2304x240 |
| coverage | 55 s | lives | 2304x240 |
| coverage | 55 s | lives + barrier | 2512x240 |
| **clean** | **659 s** | **none** | **2512x240** |
| coverage | 659 s | lives | 2512x240 |

Two unrelated configurations landing on exactly 2512x240 / 9420 cells says 2512
is a wall the blind script cannot pass, whatever keeps it alive. Reaching it is
a matter of *effective input time* — either press buttons for the whole run, or
survive long enough for the buttons you do press to count. The first costs
nothing and changes nothing the game draws, so §3's ranking holds harder than
it was written: the cheapest lever is not the cheapest **cheat**, it is no
cheat.

What the barrier does buy, measured separately, is **variety**: 2349
`(tileData, palette)` keys against the clean run's 2299, because a run that
survives sees more of what it walks past. It does not buy CHR completeness —
93% clean against 92% with the barrier, the clean run slightly ahead because
dying loads banks an immortal run never sees (§4, now a number).

**§2 confirmed by eye.** Figure grids generated from the barrier run show a
running cycle whose every phase carries a large pale ellipse fused into the
silhouette — the Barrier's own sprites — and the fused-pose count rises from 14
to 115, because the shield touches everything and the recorder reads it as two
figures that met. The panorama from the same run is clean. The split is not a
precaution; it is the difference between a usable figure sheet and an unusable
one.
