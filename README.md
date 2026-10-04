<div align="center">

# MesenAI

[![Checks](https://github.com/sbihaiko/MesenAI/actions/workflows/checks.yml/badge.svg?branch=main)](https://github.com/sbihaiko/MesenAI/actions/workflows/checks.yml?query=branch%3Amain)
[![Release](https://img.shields.io/github/v/release/sbihaiko/MesenAI?label=release&color=2ea043)](https://github.com/sbihaiko/MesenAI/releases/latest)
[![License: GPL v3](https://img.shields.io/badge/license-GPLv3-blue.svg)](http://www.gnu.org/licenses/gpl-3.0.en.html)
[![Systems](https://img.shields.io/badge/systems-NES%20%7C%20GB%2FGBC%20%7C%20SMS%2FGG%2FSG--1000%20%7C%20GBA-8a2be2.svg)](#what-it-runs)
[![Community packs](https://img.shields.io/badge/community%20packs-15%20validated-2ea043.svg)](docs/community-packs.md)
[![Open specs: CC0](https://img.shields.io/badge/open%20specs-CC0-lightgrey.svg)](docs/specs/)

**[Download](https://github.com/sbihaiko/MesenAI/releases/latest)** · **[Remaster a game](docs/remastering-a-game.md)** · [Hear it](#hear-it) · [See it](#see-it) · [Widen it](#widen-it) · [Quick start](#quick-start) · [What's real today](#whats-real-today) · [FAQ](#faq)

</div>

## Pick your door

MesenCE is one window that does everything; this fork is the same emulator behind three task-shaped workspaces, plus a **Classic** door that is the original GUI, a keystroke away.

- **🎮 Play.** Download, open a ROM, done. Enhanced Audio is on by default (Style: *Studio*) — same notes, same timing, modern instruments — and an accepted catalog pack for the loaded ROM downloads, installs and loads on its own, no config. **Widescreen** stops the stretch: the console draws the playfield it already had beside the screen. **Esc** opens the pause overlay: save states, pack, enhancements, cheats, settings. → [Download](#download) · [Community packs](docs/community-packs.md)
- **🎨 Remaster.** Record the game once — scripted, from a save state, or driven by a published TAS — and get back sprite figures with their animation cycles, the stage stitched into one panorama, and completed pattern pages, every cell labeled. Paint the PNGs, build, see it in the game. → [Remastering guide](docs/remastering-a-game.md) · needs Python 3.10+ (the tools use `zip(strict=)`) and the tools for that release, or a checkout.
- **📦 Share.** One pre-filled Issue for a pack, a cheat or a replay. A bot downloads it, lints it against an open spec, labels it and lists it in the public catalog with a 👍 vote; classic `hires.txt` packs qualify as-is. → [pack](https://github.com/sbihaiko/MesenAI/issues/new?template=community-pack.yml) · [cheat](https://github.com/sbihaiko/MesenAI/issues/new?template=cheat-code.yml) · [replay](https://github.com/sbihaiko/MesenAI/issues/new?template=replay.yml)

## What it runs

**NES / Famicom** · **Game Boy / Game Boy Color / GBS** · **Master System / Game Gear / SG-1000** (incl. YM2413 FM) · **Game Boy Advance**

Not included: SNES (incl. Super Game Boy), PC Engine, WonderSwan, ColecoVision.

## Download

**[Releases](https://github.com/sbihaiko/MesenAI/releases/latest)** ship the emulator plus `mesenai-tools-<version>.zip` (the remastering CLI as of that tag). **v0.1.0 (2026-09-15) is macOS Apple Silicon only**, cut locally from a tagged commit. That build predates the Play / Remaster / Share / Classic workspaces (it opens in the earlier Player shell) and its tools zip predates scripts added since — those come from a checkout. No installer: unzip and run. macOS needs SDL2 (`brew install sdl2`); the app is ad-hoc signed, so open it once, then **System Settings → Privacy & Security → Open Anyway**.

Platforms without a tagged release use the on-demand CI channel (`ci-latest` pre-release, ADR-0204), unzip and run:

| Platform | Build | Notes |
|---|---|---|
| **Linux x64** | [Download](https://github.com/sbihaiko/MesenAI/releases/download/ci-latest/MesenAI-ci-linux-x64.zip) · [AppImage](https://github.com/sbihaiko/MesenAI/releases/download/ci-latest/MesenAI-ci-linux-x64.AppImage) | `sudo apt install libsdl2-2.0-0` |
| **Linux ARM64** | [Download](https://github.com/sbihaiko/MesenAI/releases/download/ci-latest/MesenAI-ci-linux-arm64.zip) · [AppImage](https://github.com/sbihaiko/MesenAI/releases/download/ci-latest/MesenAI-ci-linux-arm64.AppImage) | `sudo apt install libsdl2-2.0-0` |
| **macOS Apple Silicon (CI)** | [Download](https://github.com/sbihaiko/MesenAI/releases/download/ci-latest/MesenAI-ci-macos-arm64.zip) | Not code-signed (ADR-0203); Gatekeeper needs `xattr -dr com.apple.quarantine Mesen.app`. The signed tagged build is above. `brew install sdl2` |
| **macOS Apple Silicon (release)** | [Releases](https://github.com/sbihaiko/MesenAI/releases/latest) | arm64; ad-hoc signed, first-open step above. `brew install sdl2` |
| **Windows x64** | [Download](https://github.com/sbihaiko/MesenAI/releases/download/ci-latest/MesenAI-ci-windows-x64-aot.zip) | Windows 10 (1607) or newer |

`ci-latest` is a pre-release, so [Releases](https://github.com/sbihaiko/MesenAI/releases/latest) still resolves to the tagged build. The channel is **built on demand** — `build.yml` on a pull request against `prod` or a manual dispatch (ADR-0200, ADR-0203), not on every push. Assets are whatever `prod` held when that run compiled. Code on `main` that has not been promoted is [built from source](docs/COMPILING.md).

**Help → Check for updates** never offers an upstream Mesen build: the fork publishes no update feed, so the startup check does nothing and the menu item offers to open [this repository's releases page](https://github.com/sbihaiko/MesenAI/releases).

## Quick start

1. **[Download](#download)**, unzip, run `Mesen`. A fresh install opens in **Player** mode on **Play**. **Remaster**, **Share** and **Classic** sit beside it (⌘1 / ⌘2 / ⌘3 / ⌘4, Ctrl elsewhere). Classic is the original Mesen GUI. An upgraded install that was in Advanced opens in Classic.
2. **Open a ROM…** from the Play home, or drop one on the window. Enhanced Audio is already on (Style: *Studio*).
3. **Esc** on Play pauses into the overlay: *Save states* (including *Shared replays…*), *Pack*, *Enhancements*, *Cheats*, *Settings*, *Quit game*. *Enhancements* has **Modern instruments** on by default; the style (Synthwave, Chip Deluxe, Orchestral Lite, Dry, Studio, or a `.sf2` SoundFont) is under **Settings → Audio → More in Options…**. **Settings → Look** names the picture layers: pack art, pixel filter, screen.
4. Drop a pack folder or `.zip` beside the ROM, or into `EnhancementPacks/`. Overlay *Pack* picks among packs for that game (including *No pack*) and holds Textures, Music and ROM Patch; *Enhancements* switches Modern instruments, Border, **Widescreen** and Overclock. Global per-layer defaults: **Remaster ⋯ → Enhancement Packs**. HD Pack Builder: **Classic → Tools → HD Packs (NES)**.
5. Soundtrack as MIDI or VGM: **Remaster ⋯ → Record Music (MIDI/VGM)**.

A new install does not record while you play. Recording starts from Remaster's **Record While I Play** (ADR-0243). Redraw a game: **[docs/remastering-a-game.md](docs/remastering-a-game.md)**. A release bundles the tools its tag had, not scripts added since.

## Hear it

*Mega Man 3, Shadow Man stage. Same notes and timing; only the instruments change.*

**Before** — original NES chip (2A03):

https://github.com/user-attachments/assets/985a03ae-d6ae-4ca2-8d86-f1ddb85b0a9d

**After** — Enhanced Audio, **Studio** style:

https://github.com/user-attachments/assets/20218b52-e79b-4bad-a99c-6cbd5ab6f6c1

![Spectrogram: original NES chip audio vs. Enhanced Audio remaster](docs/media/shadowman-spectrogram.png)

Enhanced Audio re-voices chip registers frame by frame; it does not re-compose. Five styles (ESP v1), optional SoundFont, one checkbox back to stock. On NES, GB/GBC and SMS/GG (PSG and YM2413). On GBA the checkbox exists and does nothing yet.

## See it

HD packs on NES are the engine Mesen already ships. Example, hotlinked from the author's repository:

<!-- Images hotlinked from the pack author's own repository, with credit — not redistributed here. -->
<p align="center">
  <a href="https://github.com/TasticHacks/Contra80s"><img src="https://raw.githubusercontent.com/TasticHacks/Contra80s/main/screenshots/Contra80s-Screenshot-Larger-1.png" width="49%" alt="Contra 80s — NES Contra rendered with full HD textures via a Mesen HD Pack"></a>
  <a href="https://github.com/TasticHacks/Contra80s"><img src="https://raw.githubusercontent.com/TasticHacks/Contra80s/main/screenshots/Contra80s-Screenshot-Larger-4.png" width="49%" alt="Contra 80s — HD pack gameplay, jungle stage reimagined"></a>
</p>

<p align="center"><sub><i>Contra</i> (NES, 1988) through <b><a href="https://github.com/TasticHacks/Contra80s">Contra 80s</a></b>, an HD pack by <b>Tastic</b> — 8-bit graphics replaced in real time with hand-made HD art (<a href="https://www.youtube.com/watch?v=Ho1-30w41RU">trailer</a>).</sub></p>

This fork also loads HD textures on **Game Boy/GBC and SMS/Game Gear**, and keeps a [validated catalog](docs/community-packs.md). Existing NES `hires.txt` packs load unchanged.

## Widen it

**Widescreen** (Enhancements) is on/off only. When on, the core reveals extra columns from the console's own background map (ADR-0253). No stretch, no mode picker.

| Console | Picture | With the Reveal | What the sides show |
|---|---|---|---|
| **NES** | 256×240 | 384×240 — 64 px per side | the neighbouring nametable, through the mapper's mirroring |
| **GB / GBC** | 160×144 | 256×144 — 48 px per side | the wrapping 256×256 BG map and the window |
| **Game Gear** | 160×144 | 256×144 — 48 px per side | the 96 px its shipped preset crops, which the VDP drew all along |
| **GBA** | 240×160 | 284×160 — 22 px per side | text backgrounds only |
| **SMS / SG-1000** | 256×192 | no Reveal | the map is exactly as wide as the screen |

A row the console cannot fill takes the pack's `widescreen` art (MEP v1.8 §5.5), then the pack **border**, then black. Border or black alone does not enable the switch. A game with no Reveal and no pack art gets the switch **disabled, with a one-line reason**, remembered per ROM.

- Sprites still appear at the original edge. Widescreen is presentation only; save states, movies and netplay stay the 4:3 game.
- Extra columns are never recorded as tiles; pack art is still authored against the 256/160 px picture.
- The GBA path is unit-tested and has not been seen on a display (no GBA ROM in the environment that built it).

<a id="why-artists-pick-up-mesenai"></a>

## What you get, side by side

| | Stock Mesen / MesenCE | MesenAI |
|---|---|---|
| **Audio** | Faithful chip emulation | Faithful + real-time re-voicing, on by default, 5 styles, SoundFont, text-file presets |
| **HD textures** | NES only | NES, Game Boy/GBC, SMS/Game Gear |
| **Widescreen** | Aspect-ratio stretch | Reveals the playfield the console already had; pack art, then border, then black fill what it cannot |
| **Recording a game** | Start, play, Stop | Same window, plus a headless recorder driven by scripts, save states, TAS movies or RAM cheats |
| **Pack format** | `hires.txt` per game | **MEP**: textures + audio + synth presets + border + widescreen side art, folder or `.zip`, per-layer toggles |
| **Finding packs** | Forum threads | [Validated catalog](docs/community-packs.md), hash-tracked, auto-installed for the matching ROM |
| **Music export** | — | MIDI / VGM while you play |
| **Player GUI** | — | Play / Remaster / Share, plus a **Classic** door for the original GUI |
| **Consoles** | 10+ systems | 4 families on `main` |

`Core/NES/HdPacks/`, the `hires.txt` format and the HD Pack Builder are upstream's. A MesenCE pack loads here unchanged. Measured toolchain numbers: [docs/hd-pack-toolchain-comparison.md](docs/hd-pack-toolchain-comparison.md).

## How a remaster happens

```
  record ──▶ measure ──▶ unpack ──▶ paint ──▶ build ──▶ see it
  (route)   (coverage)    (kit)     (PNGs)   (hires.txt)  (in game)
```

Four drivers feed one builder: a frame-counted input script, a save state, a published TAS movie (`.bk2`), or a RAM-only cheat. Route sets for twenty-one games ship in `scripts/stages/`. Commands, in order: **[docs/remastering-a-game.md](docs/remastering-a-game.md)**. Specs: [docs/specs/](docs/specs/) (CC0).

## Community packs

Packs stay with their authors. This fork [validates and catalogs](docs/community-packs.md) them (regenerated from the [Community Packs board](https://github.com/users/sbihaiko/projects/3), ranked by 👍).

- **Submit:** [one Issue](https://github.com/sbihaiko/MesenAI/issues/new?template=community-pack.yml) with pack link, game + region, console. The workflow downloads from the [host allow-list](scripts/pack_host_allowlist.json) (GitHub releases/gists/raw, Drive, MediaFire, Dropbox, MEGA; 300 MB cap), runs `mep_lint.py`, hashes the pack, and labels the Issue.
- **Play:** every accepted pack auto-installs for the matching ROM (`AutoInstallCommunityPacks`, default on). Per-pack disable and *No pack* are stored per ROM. A sibling folder beside the ROM still wins (ADR-0049).
- **Update:** comment `/revalidate` on the Issue.
- **Cheats and replays:** [cheat](https://github.com/sbihaiko/MesenAI/issues/new?template=cheat-code.yml) (ADR-0248) and [replay](https://github.com/sbihaiko/MesenAI/issues/new?template=replay.yml) (ADR-0205). Catalogs: [docs/community-cheats.json](docs/community-cheats.json), [docs/community-replays.json](docs/community-replays.json) — both live, no entries yet.

Plain **Mesen `hires.txt`** packs and full **MEP `pack.json`** packs are both accepted. Authoring: [docs/hd-pack-authoring.md](docs/hd-pack-authoring.md).

## What's real today

- Enhanced Audio on NES, GB/GBC, SMS/GG. GBA: checkbox is a no-op.
- HD textures on NES, GB/GBC, SMS/GG. A MEP `audio` layer applies on NES only until the GB/SMS `hires.txt` extension freezes ([draft](docs/specs/hires-gbsms-v1-draft.md)).
- MEP `border` layer (ADR-0149) and widescreen Reveal (ADR-0253, W.1–W.7). Standard frames stay bit-identical (ADR-0162). GBA Reveal is unit-tested only.
- Play / Remaster / Share / Classic GUI (ADR-0241, ADR-0250).
- Community cheats and shared replays: submit/consume wired; catalogs empty.
- macOS shaders: RetroArch `.slangp` through librashader on Metal (ADR-0237). Two named looks ship (*CRT TV* / `crt-geom`, *Handheld LCD* / `zfast-lcd`), picked in **Settings → Look → Screen**. Headless checks pass; the on-screen Retina/vsync/fullscreen check is still a human row. Extra presets: copy [libretro/slang-shaders](https://github.com/libretro/slang-shaders) into the data directory `Shaders` folder (not bundled; mixed licenses). A failed load keeps the unfiltered picture. `UseSoftwareRenderer` keeps the old path.
- A changed `hires.txt` still needs the ROM reopened for a cell's first paint (ADR-0231). The layered `.ora` is write-only; the flat PNG is the return path.
- Route search (`scripts/route_search.py`) ships. The optional Jev stall helper is a **measured spike, not adopted** (ADR-0238).
- Only macOS Apple Silicon is a tagged release. Windows, Linux, and the macOS CI zip come from [Download](#download).

Decisions: [docs/adr/](docs/adr/). Roadmap: [docs/roadmap/PRD-mesence-enhancement-ecosystem.md](docs/roadmap/PRD-mesence-enhancement-ecosystem.md).

## Why this fork exists

Upstream's contribution policy does not accept AI-assisted PRs (Enhanced Audio was [nesdev-org/MesenCE#262](https://github.com/nesdev-org/MesenCE/pull/262), closed for that reason). This is an independent fork. Product consoles are the four families above; the dropped ones are named under [What it runs](#what-it-runs). Lineage: Mesen 0.9.x → Mesen2 → [MesenCE](https://github.com/nesdev-org/MesenCE) → **MesenAI**. How to contribute, including the AI-assisted rule: [CONTRIBUTING.md](CONTRIBUTING.md).

## FAQ

**Does Enhanced Audio change the music?** No. It changes the instruments. Notes, timing and dynamics come from the game's own registers, frame by frame.

**Can I turn it all off?** Yes — *Modern instruments* off on the Enhancements sheet, or the Enhanced Audio checkbox in Options → Audio.

**Does Widescreen stretch the picture?** No. The console draws the playfield it already had beside the screen; only what it cannot draw comes from pack art, the border layer, or black (ADR-0253). Sprites still enter at the original edge. If a game has neither Reveal nor pack art, the switch is disabled and says why.

**Do existing NES HD packs work?** Yes. The `hires.txt` format is unchanged; drop them in `HdPacks/` as always, or wrap them in a MEP pack.

**Do I need to play the whole game to remaster it?** No. Write a route, start from a save state, or play a published TAS. Coverage is measurable. Searching a route, and the optional model step at stalls, is in the [remastering guide](docs/remastering-a-game.md#finding-a-route--search-it-with-jev-at-the-stalls).

**Will you host packs?** No. Packs stay with their authors; this fork validates and [catalogs](docs/community-packs.md) them. The emulator reads that one [MEI](docs/specs/MEI-v1.md) index.

**Where's SNES?** Not here. [bsnes](https://github.com/bsnes-emu/bsnes), [snes9x](https://github.com/snes9x/snes9x) and [ZSNES](https://www.zsnes.com/) already cover it.

**Is it a drop-in replacement for Mesen?** For NES, GB/GBC, SMS/GG/SG-1000 and GBA: same core, the same save-state (`.mss`) format, plus a couple of debugger expression fixes and the enhancement layer.

## Contributing

Product branch is `main`. Style, licensing, and the AI-assisted rule: [CONTRIBUTING.md](CONTRIBUTING.md).

- **Bug:** [open an issue](https://github.com/sbihaiko/MesenAI/issues/new) with the ROM's No-Intro name — never the ROM.
- **Pack:** [submit it](https://github.com/sbihaiko/MesenAI/issues/new?template=community-pack.yml).
- **Preset:** `.cfg` files — PRs welcome.

## Credits & license

Built on [Mesen2](https://github.com/SourMesen/Mesen2) by Sour and [MesenCE](https://github.com/nesdev-org/MesenCE) by the nesdev.org community. GPL v3 — full text: <http://www.gnu.org/licenses/gpl-3.0.en.html>. Copyright (C) 2014-2026 Sour, 2026 contributors. Open specs in `docs/specs/` are CC0.

Thanks to the wider MesenCE fork network, all GPLv3: [zerkz/MesenCE](https://github.com/zerkz/MesenCE) (`InputOverrideProvider`, prior art for frame-bounded headless input); [lusid/MesenCE](https://github.com/lusid/MesenCE) (in-memory frame capture); [libretro/MesenCE](https://github.com/libretro/MesenCE) ([ADR-0157](docs/adr/0157-headless-input-counted-in-frames.md)); [ky12138/MesenCE](https://github.com/ky12138/MesenCE) `NES_ONLY`/`LessUI` build modes, measured and declined ([ADR-0158](docs/adr/0158-no-nes-only-lessui-build-modes.md)).
