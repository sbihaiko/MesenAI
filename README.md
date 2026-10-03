<div align="center">

# MesenAI

### Every 8-bit game you own is remaster material. This emulator proves it while you play.

[![Checks](https://github.com/sbihaiko/MesenAI/actions/workflows/checks.yml/badge.svg?branch=main)](https://github.com/sbihaiko/MesenAI/actions/workflows/checks.yml?query=branch%3Amain)
[![Release](https://img.shields.io/github/v/release/sbihaiko/MesenAI?label=release&color=2ea043)](https://github.com/sbihaiko/MesenAI/releases/latest)
[![License: GPL v3](https://img.shields.io/badge/license-GPLv3-blue.svg)](http://www.gnu.org/licenses/gpl-3.0.en.html)
[![Systems](https://img.shields.io/badge/systems-NES%20%7C%20GB%2FGBC%20%7C%20SMS%2FGG%2FSG--1000%20%7C%20GBA-8a2be2.svg)](#what-it-runs)
[![Open specs: CC0](https://img.shields.io/badge/open%20specs-CC0-lightgrey.svg)](docs/specs/)
[![Community packs](https://img.shields.io/badge/community%20packs-15%20validated-2ea043.svg)](docs/community-packs.md)

**[⬇ Download](https://github.com/sbihaiko/MesenAI/releases/latest)** · **[Remaster a game](docs/remastering-a-game.md)** · [Hear it](#hear-it) · [See it](#see-it) · [Quick start](#quick-start) · [What's real today](#whats-real-today) · [FAQ](#faq)

</div>

<br/>

Emulators stopped at *faithful* twenty years ago. **MesenAI starts there** — it is
Mesen's accuracy-first core, unchanged — **and keeps going**: the first ROM you
open already sounds better, HD art works on three console families instead of
one, and the emulator quietly turns the game you are playing into a folder an
artist can paint.

That last part is the step forward. Redrawing a game used to mean playing it
end to end with a recorder running, then untangling thousands of 8×8 fragments
by hand — the most prolific HD-pack author on the NES keeps a 9.9 MB, 34-sheet
spreadsheet just to write the rule file. MesenAI replaces that with **a route
you can write down, a coverage number you can measure, and a kit laid out to
paint**. The play still happens. It just stops being the thing that decides
whether your remaster is complete.

---

## Pick your door

<table>
<tr>
<td width="33%" valign="top">

### 🎮 I want to play

Download, open a ROM, done. Enhanced Audio is **on by default** — same notes,
same timing, modern instruments. Open a ROM that has a validated community
HD pack and the pack downloads, installs and loads on its own — zero clicks,
no config. Fifteen packs ship that way today, and a hand-dropped copy of a
catalog pack is recognized as that same pack — one entry in the picker, and
your stored per-ROM choice follows it. Esc pauses into one overlay: save
states, pack, enhancements, cheats, settings.

**→ [Download](#download)** · [Community packs](docs/community-packs.md)

</td>
<td width="33%" valign="top">

### 🎨 I want to remaster a game

Record the game once — scripted, from a save state, or driven by a published
TAS. Get back **sprite figures with their animation cycles, the stage stitched
into one panorama, and completed pattern pages**, each cell labeled. Paint the
PNGs, or export a figure as one PNG, paint that whole, and import it back.
Build. See it in the game. The **Remaster** workspace runs the same tools
from buttons — *Record While I Play*, *Prepare Figures*, *Build & Show in
Game* — once Python 3.10+ and the MesenAI tools are found.

**→ [Remastering guide](docs/remastering-a-game.md)**

</td>
<td width="33%" valign="top">

### 📦 I made (or found) a pack

Open one pre-filled Issue with a link. A bot downloads it, lints it against an
open spec, labels it and lists it in the public catalog with a 👍 vote. Classic
`hires.txt` packs qualify as-is — years of community work, one ecosystem.
The **Share** workspace fills that Issue in for you, and packages a Remaster
project into a `.zip` first. A cheat code or a recorded replay travels the
same way, one Issue each.

**→ [Submit a pack](https://github.com/sbihaiko/MesenAI/issues/new?template=community-pack.yml)** · [a cheat](https://github.com/sbihaiko/MesenAI/issues/new?template=cheat-code.yml) · [a replay](https://github.com/sbihaiko/MesenAI/issues/new?template=replay.yml)

</td>
</tr>
</table>

---

## Hear it

*Mega Man 3, Shadow Man stage. Same game, same notes, same timing — only the instruments change.*

**Before** — original NES chip (2A03):

https://github.com/user-attachments/assets/985a03ae-d6ae-4ca2-8d86-f1ddb85b0a9d

**After** — Enhanced Audio, **Studio** style:

https://github.com/user-attachments/assets/20218b52-e79b-4bad-a99c-6cbd5ab6f6c1

![Spectrogram: original NES chip audio vs. Enhanced Audio remaster](docs/media/shadowman-spectrogram.png)

Enhanced Audio never re-composes — it re-**voices**. Melody and timing come
straight from the game's own sound-chip registers, frame by frame; the
square-wave lead becomes a detuned saw, the mix gains body, and every note stays
exactly where the game put it. Five styles, optional SoundFont, one checkbox
back to stock.

## See it

This is what the HD-pack community already achieves on the NES with the engine
Mesen ships:

<!-- Images hotlinked from the pack author's own repository, with credit — not redistributed here. -->
<p align="center">
  <a href="https://github.com/TasticHacks/Contra80s"><img src="https://raw.githubusercontent.com/TasticHacks/Contra80s/main/screenshots/Contra80s-Screenshot-Larger-1.png" width="49%" alt="Contra 80s — NES Contra rendered with full HD textures via a Mesen HD Pack"></a>
  <a href="https://github.com/TasticHacks/Contra80s"><img src="https://raw.githubusercontent.com/TasticHacks/Contra80s/main/screenshots/Contra80s-Screenshot-Larger-4.png" width="49%" alt="Contra 80s — HD pack gameplay, jungle stage reimagined"></a>
</p>

<p align="center"><sub><i>Contra</i> (NES, 1988) through <b><a href="https://github.com/TasticHacks/Contra80s">Contra 80s</a></b>, an HD pack by <b>Tastic</b> — 8-bit graphics replaced in real time with hand-made HD art (<a href="https://www.youtube.com/watch?v=Ho1-30w41RU">trailer</a>).</sub></p>

MesenAI takes that pipeline to **Game Boy and Master System**, ships the tools
that generate an artist's base material from a recording, and keeps a
[validated catalog](docs/community-packs.md) so nobody digs through forum
threads again.

---

## Why artists pick up MesenAI

The artist's real competitor was never another emulator. It was a spreadsheet,
a text editor, and a week of playing with the recorder running. Here is what
changes, with the numbers behind each claim taken from packs recorded on this
machine ([method](docs/hd-pack-toolchain-comparison.md)):

| The old way | With MesenAI | Measured |
|---|---|---|
| Play the whole game with the recorder on | **Write the route down.** Frame-counted input scripts, save states, published TAS movies and RAM-only cheats drive a headless recorder — and a route you do not have yet can be **searched**, with Jev asked only where the search cannot pass | ~3× real time, deterministic in emulated frames; a searched candidate 2.6× cheaper |
| Hope you saw everything | **Measure coverage, then steer.** Per recording, per image, which tiles only *that* state shows | Contra: 53.8 % → 58.9 % → 64.6 % across three recordings |
| Untangle thousands of 8×8 fragments | **A kit of four surfaces.** Figures with animation cycles, named scenery, stage panoramas, completed pattern pages — every cell labeled | Contra stage-3 boss: 517 poses over 195 distinct tiles, a 50× reuse the kit makes visible |
| Hand-write the rule file (or a 34-sheet spreadsheet) | **Build it from the sheets.** Ambiguous reused tiles get their conditions from observed neighbours, automatically | 0 tile keys lost, 0 invented, on every generator's round trip |
| No linter, no spec | **Lint against an open spec.** `mep_lint.py`, MEP v1, canonical content id, sha256 errata, pack CI | 15 community packs validated by the same script you run offline |

Everything a generator infers is marked as inference. Nothing that changes what
a rebuilt pack renders is emitted unless the recording actually observed it.
Names come from the data or from a human — never from a guess.

---

## Quick start

1. **[Download](#download)**, unzip, run `Mesen`. A fresh install opens in
   **Player** mode on the **Play** workspace; **Remaster** and **Share** sit
   beside it in the workspace switcher (⌘1 / ⌘2 / ⌘3, Ctrl elsewhere).
2. **Open a ROM…** from the Play home, or drop one on the window. Games you
   played come back as *Continue playing* and a recent-games grid. Enhanced
   Audio is already on (Style: *Studio*).
3. **Esc** pauses into the overlay: *Save states* (slot grids, plus *Shared
   replays…*), *Pack*, *Enhancements*, *Cheats*, *Settings*, *Quit game*.
   Different sound? **Settings → Audio → General → Enhanced audio** — pick
   Synthwave, Chip Deluxe, Orchestral Lite, Dry or Studio, or point it at your
   own `.sf2` SoundFont. **Settings → Look** names the picture's three layers:
   pack art, pixel filter, screen.
4. Got a pack? Drop the folder or `.zip` beside the ROM (or into
   `EnhancementPacks/`). The overlay's *Pack* row picks among packs for the
   game and *Enhancements* switches textures, audio, border, widescreen and
   overclock; per-pack layer toggles stay under **Tools ⋯ → Tools → HD Packs
   (NES) → Enhancement Packs (MEP)…**.
5. Want the soundtrack as MIDI or VGM? **Tools ⋯ → Tools → Record Music (MIDI/VGM)**.

Every classic Mesen menu — File, Game, Settings, Tools, Debug, Help — lives
under **Tools ⋯** in both modes; *Show classic menu bar* there brings the bar
back, and **Settings → Preferences → UI mode** switches to **Advanced**, which
keeps the classic dialogs. A new install records nothing while you play:
recording starts from Remaster's **Record While I Play** (ADR-0243), and the
old *Record while I play* setting stays, off, in the Enhancement Packs window.

Want to redraw a game? Start at **[docs/remastering-a-game.md](docs/remastering-a-game.md)** —
every command, in order. The tools are Python scripts under `scripts/` plus the
compiled `headless_record`; a release bundles the set its tag had, not the tools
added since (see [Download](#download)).

## Download

**[Releases](https://github.com/sbihaiko/MesenAI/releases/latest)** carry the
emulator plus `mesenai-tools-<version>.zip`, the command-line tools the
remastering guide uses as of that tag. **v0.1.0 (2026-09-15) is macOS Apple
Silicon only**, cut locally from a tagged commit, and its tools zip predates
what the guide has gained since — `mep_figure.py`, `mep_add_cell.py`,
`record_library.sh`, the `stage-set.json` route sets that now cover ten
games, and the route search (`route_search.py`, `jev_harness.py`) with the
recorder's step-mode session it runs on. Those come from a checkout instead.
v0.1.0 also predates the Play / Remaster / Share GUI described in
[Quick start](#quick-start): it opens in the earlier Player shell (overlay,
recent games, pack picker), and *Bootstrap* still records a starter pack
beside each ROM you play. No installer: unzip and run. macOS
needs SDL2 (`brew install sdl2`); the app is ad-hoc signed, so open it once, then
**System Settings → Privacy & Security → Open Anyway**.

Platforms without a tagged release use the on-demand CI channel — the newest
build of `prod` that passed, unzip and run:

| Platform | Build | Notes |
|---|---|---|
| **Linux x64** | [Download](https://github.com/sbihaiko/MesenAI/releases/download/ci-latest/MesenAI-ci-linux-x64.zip) · [AppImage](https://github.com/sbihaiko/MesenAI/releases/download/ci-latest/MesenAI-ci-linux-x64.AppImage) | `sudo apt install libsdl2-2.0-0` |
| **Linux ARM64** | [Download](https://github.com/sbihaiko/MesenAI/releases/download/ci-latest/MesenAI-ci-linux-arm64.zip) · [AppImage](https://github.com/sbihaiko/MesenAI/releases/download/ci-latest/MesenAI-ci-linux-arm64.AppImage) | `sudo apt install libsdl2-2.0-0` |
| **macOS Apple Silicon (CI)** | [Download](https://github.com/sbihaiko/MesenAI/releases/download/ci-latest/MesenAI-ci-macos-arm64.zip) | Not code-signed (ADR-0203); Gatekeeper needs `xattr -dr com.apple.quarantine Mesen.app`. The signed, released build is below. `brew install sdl2` |
| **macOS Apple Silicon (release)** | [Releases](https://github.com/sbihaiko/MesenAI/releases/latest) | arm64; ad-hoc signed, so the first-open step above applies. `brew install sdl2` |
| **Windows x64** | [Download](https://github.com/sbihaiko/MesenAI/releases/download/ci-latest/MesenAI-ci-windows-x64-aot.zip) | Windows 10 (1607) or newer |

> Those files are assets of the `ci-latest` **pre-release** (ADR-0204), so the
> URLs are fixed and the files never expire — and because it is a pre-release,
> the [Releases](#download) link above still resolves to the tagged release, not
> to a CI build. The channel is **built on demand, not on every push** —
> `build.yml` runs on a pull request opened against `prod` or a manual dispatch
> (ADR-0200, ADR-0203), never on a plain push. To refresh the assets after
> promoting `main` into `prod`:
> `gh workflow run build.yml --repo sbihaiko/MesenAI --ref prod`.
> A CI build carries what `prod` held when it ran. The current assets are the
> 2026-10-03 promotion (#714), so they include the Play / Remaster / Share GUI;
> anything merged into `main` since reaches the channel with the next
> promotion, or build from source: [COMPILING.md](COMPILING.md).
>
> **Help → Check for updates** never offers an upstream Mesen build: the fork
> publishes no update feed, so the startup check does nothing and the menu item
> offers to open [this repository's releases page](https://github.com/sbihaiko/MesenAI/releases).

---

## How a remaster happens

```
  record ──▶ measure ──▶ unpack ──▶ paint ──▶ build ──▶ see it
  (route)   (coverage)    (kit)     (PNGs)   (hires.txt)  (in game)
```

1. **Record** the game doing everything it can do. Four drivers feed one
   builder: a frame-counted input script, a **save state** to start mid-level,
   a published **TAS movie** (`.bk2`), or a **RAM-only cheat** to reach a
   later stage. No window, no human at the pad, about 3× real time. Route sets
   for ten games ship in `scripts/stages/`. No route yet? `scripts/route_search.py`
   searches one, locally and for free. Where the search stalls,
   `scripts/jev_harness.py` can optionally ask **Jev** (TypeSafe, through your own
   OpenRouter key) to pick one macro from a fixed set; it sees RAM-derived numbers
   only, never ROM bytes or pixels. Either way the output is a plain input script,
   and replaying it never calls a model ([finding a route](docs/remastering-a-game.md#finding-a-route--search-it-with-jev-at-the-stalls)).
2. **Measure** what the recording put on screen — per image, per state — and
   write a better route if a figure is missing.
3. **Unpack** the recording into a **kit**: sprite figures and their cycles,
   named scenery, the stage as one scrolling panorama (CHR RAM games), and
   pattern pages completed from the cartridge — each with a sidecar saying what
   every cell is and whether it was *seen* or *inferred*. The pattern pages need
   no recording at all: `artist_chr_kit.py --static` fills them from the ROM.
4. **Paint** the PNGs in your own editor. Cells are the deliverable.
5. **Build and lint.** `mep_build.py` regenerates `hires.txt` from the sheets;
   `mep_lint.py` checks the pack against MEP v1. Every generator round-trips
   with 0 keys lost or invented.
6. **See it.** Open the game. It is your art now.

Step by step, with every command: **[docs/remastering-a-game.md](docs/remastering-a-game.md)**.
Want a model to propose the tedious names? It can — as a proposal a human
promotes, never as evidence ([docs/ai-kit-review.md](docs/ai-kit-review.md)).

## What you get, side by side

| | Stock Mesen / MesenCE | **MesenAI** |
|---|---|---|
| **Audio** | Faithful chip emulation | Faithful **+ real-time modern re-voicing**, on by default, 5 styles, SoundFont, text-file presets |
| **HD textures** | NES only | **NES, Game Boy/GBC, SMS/Game Gear** |
| **Recording a game** | Press Start, play to the end, press Stop | Same window, **plus a headless recorder driven by scripts, states, TAS movies or RAM cheats** |
| **Knowing what you missed** | Play more and look | **Coverage per recording, per image, per state** |
| **Vocabulary** | Tiles in cartridge order | **Metatiles, sprite figures, poses, animation cycles**, inferred and marked |
| **The rule file** | By hand, or your own spreadsheet | **Generated from the sheets**; conditions for reused tiles attached from observed neighbours |
| **Validation** | None | **Linter, versioned spec, content id, sha256 errata, pack CI** |
| **Finding packs** | Forum threads | **Validated catalog**, hash-tracked, labeled by content, ranked by 👍 |
| **Pack format** | `hires.txt` per game | **MEP**: textures + audio + synth presets + a border frame in one hash-keyed pack, folder or `.zip`, per-layer toggles |
| **Music export** | — | **MIDI / VGM** while you play |
| **Player GUI** | — | **Play / Remaster / Share workspaces** on a fresh install: recent games, a pause overlay with pack picker, enhancements, save-state slots, cheats and shared replays; the classic menus under Tools ⋯ |
| **Consoles** | 10+ systems | **4 families**, chosen because their enhancement ecosystems already exist |

Everything in the left column is also in the right one. `Core/NES/HdPacks/`,
the format and the HD Pack Builder are upstream's work, credited as such, and a
MesenCE pack loads here unchanged.

### What it runs

**NES / Famicom** · **Game Boy / Game Boy Color / GBS** · **Master System / Game Gear / SG-1000** (incl. YM2413 FM) · **Game Boy Advance**

Not included: SNES (incl. Super Game Boy), PC Engine, WonderSwan, ColecoVision — see [FAQ](#faq).

---

## Community packs

Packs stay with their authors. MesenAI **validates and catalogs** them, so
players get one trustworthy list and pack makers get found.

- **Browse:** [docs/community-packs.md](docs/community-packs.md) — regenerated
  from the [Community Packs board](https://github.com/users/sbihaiko/projects/3),
  ranked by 👍. Click a row's 👍 to vote on its Issue.
- **Submit:** [one Issue](https://github.com/sbihaiko/MesenAI/issues/new?template=community-pack.yml)
  with pack link, game + region, console. A workflow downloads the pack (GitHub
  releases, gists, raw links, Drive, MediaFire, Dropbox, MEGA; 300 MB cap), runs
  the same `mep_lint.py` you can run offline, computes its hash, labels the
  Issue (`pack:valid` / `pack:invalid`, `assets:*`, `patch:*`, `console:*`) and
  comments with the spec section behind the verdict.
- **Play:** every accepted pack is auto-installed for the matching ROM. One
  master switch, per-pack disable.
- **Update:** comment `/revalidate` on the Issue.
- **Cheats and replays, new and still empty:** the same loop now carries a
  [cheat code](https://github.com/sbihaiko/MesenAI/issues/new?template=cheat-code.yml)
  — one Issue per code, checked by structure only (known game, console, every
  part decodes, no duplicate; ADR-0248) — and a
  [recorded replay](https://github.com/sbihaiko/MesenAI/issues/new?template=replay.yml)
  (ADR-0205). Accepted ones land in
  [docs/community-cheats.json](docs/community-cheats.json) and
  [docs/community-replays.json](docs/community-replays.json), which Play reads:
  the Cheats sheet lists the codes for your exact copy, with *Share This Cheat
  ↗* for your own, and *Save states → Shared replays…* lists the replays. Both
  catalogs are live and have no entries yet.

Both formats are welcome — a plain **Mesen `hires.txt` pack** or a **full MEP
`pack.json`**. Making one? [Remastering guide](docs/remastering-a-game.md), then
the [pack authoring guide](docs/hd-pack-authoring.md).

---

## What's real today

A project that measures its own claims should say what is and isn't shipped.

**Shipped and measured**
- Enhanced Audio on NES, GB/GBC and SMS/GG (PSG and YM2413 FM). On GBA the
  checkbox exists and does nothing yet.
- HD textures on NES, GB/GBC, SMS/GG. The `audio` layer of a MEP pack applies
  on NES only until the GB/SMS `hires.txt` extension freezes.
- The MEP `border` layer: a frame or bezel composited around the game viewport,
  toggled with the other enhancement layers (ADR-0149).
- The headless recorder, all four drivers, coverage measurement, the four-surface
  kit — every palette a shape was drawn in reaches its cell (ADR-0230) —
  `mep_build`/`mep_lint`, the composition editor, auto-attached
  `spriteNearby`/`tileNearby` and hand-written conditions checked against
  recorded routes, the in-place reload of repainted images, a recorded capture
  that draws only the cells its own record carries (ADR-0236), importing a
  legacy `hires.txt` pack, 15 validated community packs auto-installing.
- Route search on a persistent step-mode session, and the optional Jev stall
  helper (ADR-0238) — a **measured spike, not a shipped feature**. On two real
  stalls: Jev passed Mega Man 3's page-3 wall in 5 of 5 arms, tips on and off, at
  3.57–3.62× real time where the search alone stops, and the Ninja Gaiden control
  on its first rung in two decisions; every script it wrote replays without the
  model to the same positions. The adoption verdict is **do not adopt beyond the
  spike** ([the F14.15 log](docs/validation/f1415-jev-adoption-2026-09-26.md)).
- The Play / Remaster / Share GUI (ADR-0241): the shell and Tools ⋯, the Play
  home, pause overlay and its sheets, the first-run sheet, the Look tab
  (ADR-0246), Remaster's recording, kit browser, import and Build & Show in
  Game (NES), and Share's pack, project-package and replay flows — each
  surface's rules unit-tested and its wiring tested headless against the real
  core.
- Community cheats (ADR-0248) and shared replays (ADR-0205), submit and
  consume: Issue Forms, structural gates, generated catalogs, Play's sheets.
- A CI gate on every pull request to `main` and every push to `main`: the
  structural suite, the Python tool suites, a headless boot of the real core,
  about 1 460 dependency-free C++ unit tests, and the C# unit and headless-UI
  suites.

**Not yet, and named as such**
- Shaders on macOS are **built but not yet signed off by a person**: RetroArch
  `.slangp` presets run through librashader on a native Metal renderer
  ([ADR-0237](docs/adr/0237-macos-gets-shader-support-through-a-native-metal-renderer.md),
  roadmap slice P.8), with the old software renderer kept behind the
  `UseSoftwareRenderer` setting. Two named looks ship with the app, picked in
  **Settings → Look → Screen**: *CRT TV* (`crt-geom`) and *Handheld LCD*
  (`zfast-lcd`), each file listed with its license, source and sha256; for
  anything else, put presets in the `Shaders` folder of the data directory.
  The headless checks pass; the on-screen check (Retina, vsync, fullscreen)
  is still a human row.
  Take presets from [libretro/slang-shaders](https://github.com/libretro/slang-shaders)
  (copy the whole repository: most presets reach for its `include/` and
  `stock.slang`; it is not bundled because its shaders carry mixed licenses).
  These checked out on the Metal path, no GPU hang, and look right on a
  256x240 picture: `crt/zfast-crt` (the cheapest), `crt/crt-lottes-fast`,
  `crt/crt-easymode`, `crt/crt-geom` and `crt/crt-guest-advanced-fastest`.
  Some presets are known not to load (for example the `-wcg` and
  `steamdeck-oled-native` variants under `bezel/scanline-classic`, which use
  an outdated HDR name): a failed load keeps the unfiltered picture.
- Two limits stand by design, not as gaps: a changed `hires.txt` still needs the
  ROM reopened — the in-place reload covers a cell already painted, and a cell's
  first paint re-points its rule, so it wants one reopen of its own (ADR-0231) —
  and the layered `.ora` is write-only, so the flat PNG stays the return path.
  Otherwise **Phase 12** is delivered, with only human rows left; **Phase 13**
  (shared replays and community cheats) and the Play / Remaster / Share GUI
  (G.1–G.8) are delivered too; and the live work is **Phase 14** (proof at
  scale) plus Phase 7's open rows (shaders on macOS, in-place pack change,
  cheat search), of the
  [roadmap](docs/roadmap/PRD-mesence-enhancement-ecosystem.md) opened from a
  [side-by-side with upstream](docs/hd-pack-toolchain-comparison.md) that says
  where a hand author is still better served.
- A human artist who did not build the tools has not yet run the painting
  workflow end to end. Every acceptance so far is measured, but by proxy.
- Nobody has opened the new GUI on a real display yet: its look against the
  wireframes, a real gamepad, drag-and-drop, the browser and Finder hand-offs
  and a Remaster build shown on a running game are still human rows. Neither
  the cheat nor the replay workflow has run against a real Issue, and a Game
  Boy or Master System cheat is refused as an unknown game until the
  repository carries No-Intro data for those consoles.
- The Jev stall helper is proven and **not adopted beyond the spike**
  ([ADR-0238](docs/adr/0238-jev-via-openrouter-is-a-stuck-point-input-generator-behind-a-persistent-step-mode-emulator.md)
  §5, one clause short, [log](docs/validation/f1415-jev-adoption-2026-09-26.md)):
  the route that passes a stall buys the recorded kit **0 keys** the committed
  routes do not already have, and its Mega Man 3 route has no mint that rebuilds
  its start state, so `scripts/stages/` refuses it. Ninja Gaiden's section 1-2
  death window is still not passed — 0 of 4 arms in the second pass and 0 of 2 in
  the third, because that state has no legal candidate for the base search and its
  rewind ring holds one checkpoint — and a run that reaches the web-research step
  pays 30–60 s for the pass, which drops a long hybrid run to 2.02× real time
  against the ≥ 3× target.
- Only macOS Apple Silicon is a tagged release. The Windows and Linux binaries,
  and macOS's own CI build, come from the on-demand channel in
  [Download](#download) — never from a tag.

Decisions behind all of it are recorded as an [ADR trail](docs/adr/) — the *why*
stays reviewable.

---

## Why this fork exists

**Focused, not generalist.** Every extra console is another core to keep
accurate and another place "enhancement" has to be reinvented. Dropping SNES,
PCE, WonderSwan and ColecoVision paid for three things a generalist cannot
easily have: audio that upgrades every game automatically, one pack format
across consoles, and a real test layer.

**Built with AI, on purpose — and honest about it.** Implementation, review and
the test suite are AI-assisted; that is how a small project moves four cores, a
synth engine and a pack ecosystem at once, with tests as the safety net.
Upstream's contribution policy does not accept AI-assisted PRs (Enhanced Audio
was proposed as [nesdev-org/MesenCE#262](https://github.com/nesdev-org/MesenCE/pull/262)
and closed for exactly that reason), so MesenAI is an independent fork.
Upstream accuracy fixes are ported in regularly; enhancement never means
drifting from accuracy. Throughput since going AI-assisted, with the commit
video: [issue #166](https://github.com/sbihaiko/MesenAI/issues/166).

**Lineage.** Mesen 0.9.x → Mesen2 → [MesenCE](https://github.com/nesdev-org/MesenCE)
(upstream, nesdev.org) → **MesenAI** (this fork).

## FAQ

**Does Enhanced Audio change the music?** No. It changes the *instruments*.
Notes, timing and dynamics come from the game's own registers, frame by frame.

**Can I turn it all off?** Yes — one checkbox in Settings → Audio, and you have
stock Mesen accuracy.

**Do existing NES HD packs work?** Yes. The `hires.txt` format is unchanged;
drop them in `HdPacks/` as always, or wrap them in a MEP pack.

**Do I need to play the whole game to remaster it?** No. Write the route, or
start from a save state, or let a published TAS play. Measure what you covered.
Record again where the number says so. A route you cannot write can be searched
instead, and a spot the search cannot pass can be handed to a model as a choice
between fixed macros — the output is still a plain input script, and replaying it
never calls the model. The model step is optional and needs your own OpenRouter
key; the search alone needs nothing ([finding a route](docs/remastering-a-game.md#finding-a-route--search-it-with-jev-at-the-stalls)).

**Will you host packs?** No. Packs stay with their authors; MesenAI validates
and [catalogs](docs/community-packs.md) them, and the emulator reads that one
[MEI](docs/specs/MEI-v1.md) index — extra index URLs are a deferred non-goal.

**Where's SNES?** Not here, deliberately. [bsnes](https://github.com/bsnes-emu/bsnes),
[snes9x](https://github.com/snes9x/snes9x) and [ZSNES](https://www.zsnes.com/)
already do it better than a bolted-on core would.

**Is it a drop-in replacement for Mesen?** For NES, GB/GBC, SMS/GG/SG-1000 and
GBA — yes: same core, same debugger, same save/state formats, plus the
enhancement layer.

## Contributing

- **Something sounds wrong or a pack won't load?** [Open a bug](https://github.com/sbihaiko/MesenAI/issues/new)
  with the ROM's No-Intro name — never the ROM.
- **Made or found a pack?** [Submit it](https://github.com/sbihaiko/MesenAI/issues/new?template=community-pack.yml).
- **Tuned a style by ear?** Presets are `.cfg` files — PRs welcome. See
  [CONTRIBUTING.md](CONTRIBUTING.md).

## Credits & license

Built on [Mesen2](https://github.com/SourMesen/Mesen2) by Sour and
[MesenCE](https://github.com/nesdev-org/MesenCE) by the nesdev.org community.
GPL v3 — full text: <http://www.gnu.org/licenses/gpl-3.0.en.html>.
Copyright (C) 2014-2026 Sour, 2026 contributors. Open specs in `docs/specs/`
are CC0.

Thanks to the wider MesenCE fork network, all GPLv3 like this repo:
[zerkz/MesenCE](https://github.com/zerkz/MesenCE)'s `InputOverrideProvider` is
the prior art behind our frame-bounded headless input;
[lusid/MesenCE](https://github.com/lusid/MesenCE)'s in-memory frame capture is
behind the harness that reads a frame straight from the emulator;
[libretro/MesenCE](https://github.com/libretro/MesenCE) shaped
[ADR-0157](docs/adr/0157-headless-input-counted-in-frames.md) twice over; and
[ky12138/MesenCE](https://github.com/ky12138/MesenCE)'s `NES_ONLY`/`LessUI`
build modes we measured and declined
([ADR-0158](docs/adr/0158-no-nes-only-lessui-build-modes.md)) — a fork earns
credit for the question it made us answer, not only for the code we took.
