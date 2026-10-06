# ADR-0184: A recording may carry a RAM-only cheat, never a PRG patch, and a cheated pass feeds only the surfaces its cheat can change

- Status: accepted (2026-09-13, at the user's direction: "só cheat de RAM, nunca patch de PRG"; §1 is implemented as `scripts/headless_record`'s `cheat=` flag, validated by `parseRamCheat()` before the emulator is touched, and §2 is measured)
- Date: 2026-09-13
- Related: ADR-0183 (the artist kit — §3 "evidence and inference are never confused" is what this protects), ADR-0182 (recording coverage), ADR-0178 (sheet sidecar / tile keys), ADR-0173 (`screenFixed` HUD classification), ADR-0159 (the grid dump the panorama is stitched from), ADR-0239 (§3 amended: a lives pin may ride with a navigation pin)

## Decision

### 1. A recording may carry a cheat only as a RAM-address code

The only accepted cheat type is `NesCustom` — the `AAAA:VV[:CC]` form that `CheatManager::ConvertFromNesCustomCode` stores with the address taken verbatim — and only with `AAAA < 0x0800`, the NES's internal RAM. `NesGameGenie` and `NesProActionRocky` are rejected outright: both force the decoded address into PRG space (`+ 0x8000`), so no code of either type can satisfy the rule. Rejecting the type and rejecting every address above `0x07FF` are two checks that must both be present, because a `NesCustom` code may also carry a PRG address.

The rule is enforced by the parser, not by discipline. A recording harness that accepts a cheat MUST refuse the run — not warn — when a code fails either check, and MUST name the offending code.

This is not a claim about how the emulator applies cheats. Mesen substitutes the byte in flight on the CPU read bus (`CheatManager::ApplyCheat`, called from `NesMemoryManager::Read`) and never writes the PRG buffer. The rule is about which address space the game is reading a different value from, because that is what decides whether altered bytes can reach the tile data.

### 2. A cheated run is a second pass, and only the surfaces its cheat cannot reach read it

Each stage is recorded twice, and the two recordings are not interchangeable:

| pass | cheat | which of ADR-0183's four surfaces may be built from it |
|---|---|---|
| **clean** | none | figures (`sheets/usr*.png`), scenery (`sheets/`, `scene/`) |
| **coverage** | RAM-only, per §1 | stage maps (`map/`), pattern pages (`chr/`) |
| **navigation** | a level/room selector read off a published RAM map, pinned for the run, per §1 | **all four** |

The split follows the evidence. A panorama is stitched from the background grid stream (ADR-0159, `MESEN_SHEET_GRID_DUMP`), which carries nametable content; the player's sprite is not in it, so a cheat that recolours the player or draws a shield around it cannot reach a panorama cell. A pattern page is CHR bank content, equally out of reach. Figures and scenery come from the OAM and background vocabularies the recorder builds per frame, and those *do* see the barrier sprite and the swapped palette.

A kit fragment built from a coverage pass MUST record the cheat in its `notes[]`, verbatim and with its address, so the provenance travels with the art. A kit MUST NOT mix a clean and a coverage pass inside one surface. A navigation pass is §3 rank 2 in spirit — it changes no drawn pixel — but is its own rank, because a lives counter buys attempts while a selector buys *places*; its `notes[]` obligation is the same, absolute. Because the selector is pinned for the whole run, the game never observes the stage advancing: the stage-clear transition, and any figure that exists only in it, is never recorded — an omission (cf. §4), not a contamination.

### 3. Prefer the cheat that changes least

Ranked, and this order is the decision, not advice:

1. **Game code the developers shipped.** Contra's Konami Code (30 lives) is input, not a cheat: it writes the game's own variable through the game's own routine. `scripts/stages/contra/mint-stage1-30lives.txt` already does this.
2. **A counter the HUD renders.** Lives, continues, hit points — Contra `0032:99`, Castlevania `002A`, Zelda `0670:FF`, Mega Man 3 `00A2`/`00AE`. These change nothing drawn except HUD digits, which are real tiles the game draws anyway and which ADR-0173 already classifies as `screenFixed`.
3. **An invulnerability timer** — only in a coverage pass, never in a clean one, and only after checking what the game draws while it runs.
4. **Nothing else.** A cheat whose value the game itself never produces is refused however admissible its address: the vendored database's Contra `00AA:18` "Clone Attack (glitch weapon)" is a RAM write and is garbage.

### 4. At least one clean run must die

An immortal run never records the death animation, the respawn, or the GAME OVER screen, and never loads a CHR bank that only a death brings in. Those are real surfaces — the Contra stage 1 kit holds the GAME OVER screen today precisely because the run dies. A stage's clean pass is therefore allowed to end in death and MUST NOT be "fixed" by carrying the coverage pass's cheat.

### 5. No code is invented

A cheat used by a recording comes from a source that names it: the database vendored at `UI/Dependencies/Internal/CheatDb.Nes.json` (keyed by ROM SHA-1), or a published RAM map cited beside the stage script. A game for which no RAM-only code can be verified simply does not get one — Excitebike is the measured case, and it needs none: it has no lives and no death, so its coverage is gated on which tracks and modes are driven, not on survival.

## Context

Recording coverage is capped by how far a scripted run gets before it dies. Measured on Contra stage 1: `scene/screen001.png` of the `stage1-run` pack is the game's GAME OVER screen, and doubling the traverse from 150 s to 300 s produced a panorama of the *identical* 2304 px width against the reference pack's 3348 px — the run dies and respawns in the same stretch. 157 of Contra's 512 CHR RAM cells per stage bank are never recorded because the game only unpacks them further in, and an enemy that first appears past the death point has no pose at all.

Cheat codes are the obvious lever, and the obvious way to poison the archive. Two distinct hazards:

**Hazard 1 — the cheat changes the bytes we record as art.** Every NES Game Genie code is a PRG-space code by construction: the device builds a 15-bit address with bit 15 forced to 1, so it addresses `$8000–$FFFF` and cannot reach RAM at all. On a CHR RAM game the pattern data is unpacked out of PRG at run time — Contra (UNROM) and Zelda 1 (mapper 1, 0 KB CHR) are both CHR RAM — so a substituted PRG byte can travel through the game's own loader into CHR RAM and be hashed into `TileData` by the HD pack builder as if it were the game's real art. On a CHR ROM game such as Mega Man 3 a PRG patch cannot alter tile bytes, but it can alter *which bank is selected*, which corrupts the stitch instead.

**Hazard 2 — the cheat changes what the game legitimately draws.** This one is not about ROM bytes at all, and a RAM-only rule does not fix it. Contra's `$00B0`, shipped in the vendored cheat database as "Invincibility (star effect)", is the **Barrier** power-up timer: the game draws the barrier's own sprites and cycles the player's palette for as long as it runs. Held on for a whole recording, the figure grids would archive the player wearing a shield in every frame. Zelda's `$04F0` is documented as "Link Invulnerable Timeout (palette dependent on value)": the same failure, in a source rather than a hypothesis. Blink-style invulnerability (Castlevania `$005B`, Contra `$00AE`) hides the sprite on alternating frames.

So "use invincibility" is not a decision that can be taken once for all surfaces. It is safe for some and ruinous for others, and which is which follows from where each surface's evidence comes from.

Non-goals: this ADR does not add a cheat UI, does not change the emulator's `CheatManager`, and does not decide which code each game uses — that is data, and it belongs beside the stage scripts.

## Record

- 2026-09-13 ("Measured 2026-09-13") — the flag landed as `cheat=AAAA:VV[:CC]` on `scripts/headless_record`, validated by `parseRamCheat()` before the emulator is touched. `SXKVPZAX` is refused as a Game Genie letter code; `8000:99` is refused by address; `0032:99` and `0032:99:FF` are accepted. A refusal ends the run — §1 says refuse, not warn.
- 2026-09-13 — a silent-failure trap this ADR's own rule would not have caught: the harness's ABI mirror declared `uint32_t Type` while `CheatType` is `enum class CheatType : uint8_t` — 20 bytes against the Core's 17, so both the array stride and the offset of `Code[]` were wrong and `CheatManager::AddCheat` refused every code silently. A `static_assert(sizeof(CheatCodeAbi) == 17)` now stands where the assumption was. A cheat that changes nothing is indistinguishable from a cheat that does not help, so a cheated run must be diffed against its uncheated twin before its numbers are believed.
- 2026-09-13 — §2/§3 measured on Contra stage 1, same state, same input script, 300 s: clean 3197 frames stitched → 2304x240; `0032:99` (99 lives) 4074 → 2304x240; `0032:99` + `00B0:FE` (barrier) 4096 → **2512x240**. Lives alone buy survival and no extra ground: a blind "hold right" script dies against the same obstacle. Contra's `stage1-run.txt` is 3300 frames — 55 s of input inside a 300 s run, so for 82% of the recording nothing was pressed; repeating the script's body to cover the whole run reaches **the same 2512x240, with no cheat at all** (clean 659 s, none, 2512x240). Two unrelated configurations landing on exactly 2512x240 / 9420 cells says 2512 is a wall the blind script cannot pass: the cheapest lever is not the cheapest **cheat**, it is no cheat. What the barrier buys is **variety**: 2349 `(tileData, palette)` keys against the clean run's 2299, while CHR completeness is 93% clean against 92% with the barrier. Confirmed by eye: figure grids from the barrier run show a pale ellipse fused into the running cycle, the panorama clean — and the barrier's 208 px were not the barrier's.
- Amended 2026-09-14 — §2 gains the navigation row; the title's "only the background surfaces" now describes the *coverage* pass alone. Contra's `$30` holds the current level (`0x00`–`0x07` = stages 1–8, `0x09` = game over), from a published RAM map; pinned for a run it warps the game to a stage. Eleven sessions, one per stage plus three bosses, about an hour of wall clock, cover **58.9%** of the reference pack's tiles against the 53.8% of 77 accumulated recordings, union **64.6%** — 367 tiles the archive never held. Such a run may feed the figure surfaces because the mechanism `$30` does not have is what §2's restriction derives from: `$30` changes *where the game is*, not what it draws. Measured on 300 s / 20821-frame runs: (1) `0030:00` (the warp value equal to the stage already loaded) is **byte-identical** to the uncheated control — `diff -rq` reports no difference across the whole `textures/` tree; (2) the pose grids are clean by eye across `w4` (`0030:03`) and `w7` (`0030:06`); (3) the positive control `0030:00` **plus** `00B0:FE` reproduces the palette-cycling defect in *every* phase. On the `w4` kit: build exit 0, **0 keys lost, 0 invented**, 216 files added, `verify: PASS`.
- 2026-09-14 — correction: the fused-pose count is a weak detector. The 14 → 115 signal does not reproduce in either direction at 300 s (`0030:00`: 83 clean vs 69 with the barrier; `0030:03`: 90 vs 80). §2's split and the barrier defect both still hold (visible in palette and silhouette), but the fused-pose count MUST NOT be used as an automatic gate for detecting a contaminating cheat.

## Consequences

- **Recording cost roughly doubles for a stage that needs a coverage pass.** A stage whose clean run already reaches the end needs no second pass; the split is per stage, not global.
- **A harness change is required before any of this runs.** The capture tool does not accept a cheat today: the Core never reads a cheat file (the per-game `Cheats/<rom>.json` is read only by the C# GUI), no config field or env var enables one, and the headless harness does not declare `SetCheats` in its `extern "C"` block. The smallest honest change is a `cheat=` flag that declares `SetCheats`, validates per §1, and calls it **after** `LoadRom` and after any state load — `Emulator::LoadRom` clears the cheat list, so a call placed before it silently does nothing.
- **Provenance becomes part of the kit contract.** `kit-part-<part>.json` gains nothing structurally — the cheat goes in `notes[]` — but a reviewer can no longer tell a clean fragment from a coverage fragment by its shape alone; omitting the note is a defect, not an oversight.
- **Invulnerability is a coverage trade, not a free win.** It buys panorama length and CHR cells and costs the death-and-respawn material. §4 exists so that cost is paid deliberately in one pass.
- **The rule is checkable and should be checked.** `AAAA < 0x0800` plus a type allow-list is two comparisons; a test that feeds a Game Genie code and a `NesCustom` code at `$8000` and asserts both are refused is what keeps the rule from decaying into a comment.
