# ADR-0244: A pack change applies in place through an in-memory save state and a reload, never a power cycle

- Status: accepted (2026-10-02). The user accepted it verbatim: *"aceito a 0244"*. Earlier, *"eu aceito, pode fazer"* had accepted the recommendation to write it. Listed as slice **P.9** in PRD Part A §4, Phase 7. The go-ahead to implement was given verbatim on 2026-10-02: *"sim, pode seguir. depois que tudo estiver no main, pode implementar usando paralelismo de tudo que puder"* and *"pode implementar em paralelo tudo que puder"*. **Measured and implemented 2026-10-02** (pending review): the exactness test of Decision 4 passes on every transition it runs, on NES, SMS, GB and GBC, so every one of them takes the in-place path; the restart stays for ROM-patch packs, movies/shared replays and netplay, and for the consoles not measured. See "Measurements" below.
- Date: 2026-10-02
- Related: ADR-0209 (save-state / reload / restore named as a third strategy; "pixel exactness after restore is the test"), ADR-0212 (in-place image reload, F12.3), ADR-0049 (pack discovery), ADR-0144/ADR-0148 (bundled ROM patches), ADR-0205 (shared replay recording), ADR-0241 / PRD Part B §13 (W-P5, W-P7)
- Supersedes / amends: none among ADRs. It changes the PRD's W-P7 *Apply & Reload* and W-P5's power cycle (Decision 3).

## Context

Every pack change today throws away the player's progress:

- *Textures*, *Audio* and *Border* go through
  `MainWindowViewModel.ToggleLayer`, then `LoadRomHelper.ReloadRom`.
- A pack picked in the picker, and an *Overclock* change, go through
  `LoadRomHelper.PowerCycle`.

Both reach `Emulator::LoadRom`, which stops the console and builds a new
one. The pack is read only there (`NesConsole::LoadRom` →
`LoadHdPack`), so the game restarts from power-on.

The one in-place path is ADR-0212's `RequestMepImageReload`. It re-decodes
the repainted images of the pack that is already loaded, on NES only, and
cannot add, remove or swap a pack or its manifest.

ADR-0209 names a third strategy without choosing it: save the state,
reload, restore. The code says it is plausible.

- **There is precedent.** `NesConsole::StartRecordingHdPack` already
  serializes the emulator, replaces the PPU with `HdBuilderPpu` and
  deserializes into it, in process. `HdBuilderPpu` and `HdNesPpu` both
  derive from `NesPpu<T>`, and the PPU state is `NesPpu::Serialize`.
- **The state is key-based.** `Utilities/Serializer.h` stores
  `unordered_map<string, …>` values. A state saved with an HD audio
  device (`SV(_hdAudioDevice)`, written only when a pack has audio) is
  therefore likely to load into a console without one, and the reverse —
  unverified, part of the test.
- **The save state names the ROM file, not the pack.** Same ROM, same
  file: the restore is allowed.

What it cannot cover:

- **A pack with a ROM patch** (IPS/BPS, ADR-0144/0148) changes PRG. A state
  of one PRG restored into another runs the wrong code.
- **A movie, a shared-replay recording (ADR-0205) or netplay.** The reload
  without a power cycle stops the movie manager (`_movieManager->Stop()`
  in `InternalLoadRom`).
- **Rewind history** is reset (`InitHistory`), and cheats are cleared on
  load (`ClearCheats`). Unverified: whether the UI re-applies the user's
  cheats afterward.

Non-goals:

- No seamless frame-perfect swap. A short pause while the pack loads is
  acceptable (ADR-0209: the decode is already detached through
  `HdPackData::LoadAsync`).
- No change to discovery or precedence (ADR-0049, ADR-0147).
- Overclock is not covered. It changes timing, and a power cycle stays
  honest for it.

## Decision

1. **The swap is: save state to memory, reload the ROM, load that state.**
   - It runs on the emulation thread, under the emulator lock, as one
     operation.
   - It uses the ordinary load path, so discovery, the PPU choice
     (`HdNesPpu` or the plain one), the audio device and the bootstrap all
     behave as on a fresh load.
   - On a failed restore (`DeserializeResult` not success), it falls back
     to the old behavior: the game is left freshly loaded, and the user is
     told "Couldn't keep your place — the game restarted".
   - The in-place PPU swap of `StartRecordingHdPack` is the faster
     alternative. The slice measures both and keeps the reload unless the
     reload's pause is noticeable.
2. **Refused, with the reason on the control (rule 4), and a restart
   offered instead:**
   - The old or new pack carries a ROM patch that applies to this ROM:
     "This pack changes the game itself — it restarts".
   - A movie is playing or recording, a shared-replay recording is on, or
     netplay is active: "Not while recording", or "Not during netplay".
3. **The GUI then says what really happens.**
   - W-P7's button becomes *Apply*. It reads *Apply & Restart* only when
     a refusal from 2 holds or Overclock changed.
   - W-P5's choice applies in place too.
   - Rewind history is lost on a swap, and the HUD toast after the swap
     says so once: "Pack changed · rewind history cleared".
4. **First slice: the exactness test, before any GUI work.**
   - A headless test on a committed ROM state:
     - play N frames;
     - save;
     - swap the pack (none → pack, pack → none, pack A → pack B);
     - restore and play M frames.
   - It compares against the same M frames played from a fresh load of the
     target pack with the same state loaded the ordinary way.
   - Pass means CPU, RAM and PPU registers are byte-identical, and the
     rendered frames are pixel-identical, on every console with HD art
     (NES first; GB/SMS through `HdTileVideoFilter`).
   - An audio-only pack covers the `_hdAudioDevice` present/absent case.
   - Any mismatch stops the GUI change. That outcome is recorded, not
     rounded.

## Consequences

- Turning a pack off to compare it, or choosing a different one, no longer
  costs the player their progress. It is the main reason W-P7 had to
  explain "applies on reload".
- One more path through `LoadRom`, which interacts with the bootstrap
  (ADR-0049). A swap to "no pack" on an undressed ROM may start a bootstrap
  recording. The test logs it, and the slice decides whether a swap
  suppresses the bootstrap.
- Users of patched packs keep the restart, now with its reason on screen.
- If the exactness test fails for one direction (for example pack → none),
  only that direction keeps the restart. The decision is per transition,
  not all-or-nothing.

## Measurements (2026-10-02)

Harness: `scripts/pack_swap_exactness.py` (three `headless_record session`
processes per transition: one writes the state S1 after N = 240 frames, a
reference launched with the *target* switches loads S1 the ordinary way and
plays M = 120 frames, and the swap path plays the same N frames — its state
equals S1 byte for byte — switches, swaps and plays on to the reference's last
frame). Pass = the swap answered `restored`, all M reference frames
pixel-identical on the swap path by frame number, the same last frame, the NES
2 KB RAM identical after each frame, and the final `cpu.*`, RAM and video
(`ppu.*`/`vdp.*`) state fields byte-identical; no other state field differed
in any passing run either. Fixtures are minted per run and never committed.
Reproduce with `python3 scripts/test_pack_swap_exactness.py` (asserts the
verdicts below) or `python3 scripts/pack_swap_exactness.py --console
nes|sms|gb|gbc`. Each console was run 3 times; every verdict held 3/3.

| Console (ROM) | Transition | Verdict | Swap outcome | Frames | Reload pause |
|---|---|---|---|---|---|
| NES (Ninja Gaiden, route Act 1-1) | control: pack A → pack A | PASS | restored | 120/120, RAM 120/120 | 60–86 ms |
| NES | none → pack (Textures on) | PASS | restored | 120/120, RAM 120/120 | 48–62 ms |
| NES | pack → none (Textures off) | PASS | restored | 120/120, RAM 120/120 | 36–61 ms |
| NES | pack A → pack B (picker) | PASS | restored | 120/120, RAM 120/120 | 51–61 ms |
| NES | audio-only pack on (Audio on; the HD audio device appears) | PASS | restored | 120/120, RAM 120/120 | 42–61 ms |
| NES | audio-only pack off (Audio off; the device goes away) | PASS | restored | 120/120, RAM 120/120 | 35–69 ms |
| NES | border-only pack on / off (Border) | PASS / PASS | restored | 120/120, RAM 120/120 | 40–69 ms |
| NES | pack A → ROM-patch pack, and back (picker) | PASS (restart kept) | patch-restarted | — | 37–65 ms |
| NES | negative control: swap to B, reference A | FAIL, as expected | restored | 0/120 | 52–56 ms |
| SMS (Sonic the Hedgehog, attract mode) | control; none → pack; pack → none; A → B | PASS ×4 | restored | 120/120 | 41–68 ms |
| SMS | negative control | FAIL, as expected | restored | 0/120 | 45–57 ms |
| GB (synthetic `test_dmg.gb`) | control; none → pack; pack → none; A → B | PASS ×4 | restored | 120/120 | 32–69 ms |
| GBC (synthetic `test_cgb.gbc`) | control; none → pack; pack → none; A → B | PASS ×4 | restored | 120/120 | 35–70 ms |
| GB / GBC | negative control | FAIL, as expected | restored | 0/120 | 41–65 ms |

What this does and does not cover:

- **The harness was checked against a broken swap.** With `LoadState`
  removed from `ReloadRomKeepingState` (a local mutation, reverted), every
  SMS transition FAILs on frames and on state. That run also showed that a
  game's attract mode replays the same frames from power-on, so the swap
  path is bounded to M + 2 frames after the swap.
- **GB/GBC were measured on synthetic ROMs** (there is no commercial GB ROM in
  the library); their screen is static, so the GB rows prove less than the
  NES and SMS ones. Audio and border transitions were measured on NES only:
  HD audio is NES-only, and the border is drawn by the renderer, outside the
  console state.
- **The ROM-patch guard** is Core's, not the UI's: only the core knows
  whether a pack's patch applied. `ReloadRomKeepingState` keeps no state when
  a patch applies before or after the reload and answers `PatchRestarted`.
- **The PPU-swap alternative of Decision 1 was not measured.** The reload's
  pause is 32–86 ms on this machine (2–5 frames, the game paused under the
  panel), which is not noticeable next to the panel itself, so the reload
  stays.
- **The bootstrap was not measured**: it writes beside the ROM, which is the
  user's library, so every run has it off. Whether a swap to "no pack" should
  suppress it (Consequences) is still open.
- **Cheats survive a swap.** `ClearCheats` runs on the reload, and the UI
  re-applies the user's cheats on `GameLoaded` (`MainWindow.axaml.cs`,
  `CheatCodes.ApplyCheats()`).
- **Decision 3's *Apply* button is not built**: W-P7 belongs to the GUI
  redesign (ADR-0241), which has no panel with an Apply button yet. Today's
  Textures/Audio/Border toggles and the picker's Apply take the in-place path
  through `LoadRomHelper.ApplyPackChange` and `UI/Logic/PackChangePolicy.cs`,
  and the HUD says what happened ("Pack changed · rewind history cleared",
  "Couldn't keep your place — the game restarted", "This pack changes the game
  itself — it restarts", "Not while recording — the game restarts", "Not
  during netplay — the game restarts"). No GUI run has been made.
