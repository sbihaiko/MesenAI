<div align="center">

# MesenAI

### Every 8-bit game you own is remaster material. This emulator proves it while you play.

[![Checks](https://github.com/sbihaiko/MesenAI/actions/workflows/checks.yml/badge.svg?branch=main)](https://github.com/sbihaiko/MesenAI/actions/workflows/checks.yml?query=branch%3Amain)
[![Release](https://img.shields.io/github/v/release/sbihaiko/MesenAI?label=release&color=2ea043)](https://github.com/sbihaiko/MesenAI/releases/latest)
[![License: GPL v3](https://img.shields.io/badge/license-GPLv3-blue.svg)](http://www.gnu.org/licenses/gpl-3.0.en.html)
[![Systems](https://img.shields.io/badge/systems-NES%20%7C%20GB%2FGBC%20%7C%20SMS%2FGG%2FSG--1000%20%7C%20GBA-8a2be2.svg)](#what-it-runs)
[![Open specs: CC0](https://img.shields.io/badge/open%20specs-CC0-lightgrey.svg)](docs/specs/)
[![Community packs](https://img.shields.io/badge/community%20packs-15%20validated-2ea043.svg)](docs/community-packs.md)

**[⬇ Download](https://github.com/sbihaiko/MesenAI/releases/latest)** · **[Remaster a game](docs/remastering-a-game.md)** · [Hear it](#hear-it) · [See it](#see-it) · [Widen it](#widen-it) · [Quick start](#quick-start) · [What's real today](#whats-real-today) · [FAQ](#faq)

</div>

<br/>

Emulators stopped at *faithful* twenty years ago. **MesenAI starts there** — it is
Mesen's accuracy-first core, unchanged — **and keeps going**: the first ROM you
open already sounds better, the picture reaches past the console's own window
instead of stretching to fill it, HD art works on three console families
instead of one, and the emulator quietly turns the game you are playing into a
folder an artist can paint.

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
catalog pack is recognized as that same pack — one entry in the picker, your
stored per-ROM choice following it, *No pack* included. Turn **Widescreen**
on and the picture stops stretching: the console draws the playfield it
already had beside the screen. Esc pauses into one overlay: save states, pack,
enhancements, cheats, settings.

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

## Widen it

Widescreen used to mean one thing: stretch the picture until the sides fill.
MesenAI **reveals** instead — the console already draws a background map wider
than the window it shows you, so the window widens (ADR-0253). One switch,
**Widescreen** under Enhancements, off until you turn it on; there is no mode
picker, the core decides per frame.

| Console | Picture | With the Reveal | What the sides show |
|---|---|---|---|
| **NES** | 256×240 | 384×240 — 64 px per side | the neighbouring nametable, through the mapper's mirroring |
| **GB / GBC** | 160×144 | 256×144 — 48 px per side | the wrapping 256×256 BG map and the window |
| **Game Gear** | 160×144 | 256×144 — 48 px per side | the 96 px its shipped preset crops, which the VDP drew all along |
| **GBA** | 240×160 | 284×160 — 22 px per side | text backgrounds only |
| **SMS / SG-1000** | 256×192 | no Reveal | the map is exactly as wide as the screen — nothing exists beside it |

A row the console cannot fill — a vertical scroller's sides, a single-screen
game, a bitmap-mode GBA frame — takes the loaded pack's own `widescreen` art
(MEP v1.8 §5.5), then the pack's **border** layer, then black. A fill-in never
counts as a mode on its own: a game with no Reveal and no pack art gets the
switch **disabled, with a one-line reason**, remembered per ROM, and a pack
that ships widescreen art turns it back on.

Three things to know before you report a bug:

- **Sprites still appear at the original edge.** The scenery widens; an enemy
  walks in from where it always did. The game's own logic is untouched —
  widescreen is presentation only, so save states, movies and netplay stay the
  4:3 game.
- **The extra columns are never recorded as tiles**, so a remaster is
  unaffected and pack art is still authored against the 256/160 px picture.
- **The GBA path is unit-tested and has not been seen on screen**: it was
  built with no GBA ROM in reach.

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
   **Player** mode on the **Play** workspace; **Remaster**, **Share** and
   **Classic** sit beside it in the workspace switcher (⌘1 / ⌘2 / ⌘3 / ⌘4,
   Ctrl elsewhere) — Classic being the original Mesen GUI.
2. **Open a ROM…** from the Play home, or drop one on the window. Games you
   played come back as *Continue playing* and a recent-games grid. Enhanced
   Audio is already on (Style: *Studio*).
3. In Player mode, **Esc** on the Play workspace pauses into the overlay: *Save states* (slot grids, plus *Shared
   replays…*), *Pack*, *Enhancements*, *Cheats*, *Settings*, *Quit game*.
   Different sound? *Enhancements* has **Modern instruments** on by default;
   the style — Synthwave, Chip Deluxe, Orchestral Lite, Dry or Studio, or your
   own `.sf2` SoundFont — is behind **Settings → Audio → More in Options…**.
   **Settings → Look** names the picture's three layers: pack art, pixel
   filter, screen.
4. Got a pack? Drop the folder or `.zip` beside the ROM (or into
   `EnhancementPacks/`). The overlay's *Pack* row picks among packs for the
   game and holds that game's Textures, Music and ROM Patch switches;
   *Enhancements* switches Modern instruments, Border, **Widescreen** and
   Overclock. The global per-layer defaults sit in another door —
   **Remaster ⋯ → Enhancement Packs** — and the HD Pack Builder under
   **Classic → Tools → HD Packs (NES)**.
5. Want the soundtrack as MIDI or VGM? **Remaster ⋯ → Record Music (MIDI/VGM)**.

Every classic Mesen menu — File, Game, Settings, Tools, Debug, Help — has a
door of its own: **Classic**, the original GUI, with the menu bar, the
debugger, Lua and every classic dialog. Play, Remaster and Share each carry a
short Tools ⋯ holding only what that task needs — Play's disk, coin, barcode
and tape items appear when the loaded game uses them — and no task-door entry
opens a classic window: it opens the Player look instead. An install that was
in Advanced opens straight in Classic, so the GUI you chose is the GUI you get.
A new install records nothing while you play:
recording starts from Remaster's **Record While I Play** (ADR-0243), and the
old *Record while I play* setting stays, off, in the Enhancement Packs window.
An upgraded install keeps its old value, and a settings file from before the
setting existed keeps recording on (with a one-time notice saying so).

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
`record_library.sh`, the `stage-set.json` route sets that now cover
twenty-one games, and the route search (`route_search.py`, `jev_harness.py`)
with the recorder's step-mode session it runs on. Those come from a checkout
instead. v0.1.0 also predates the workspaces described in
[Quick start](#quick-start): it opens in the earlier Player shell (overlay,
recent games, pack picker) with no Remaster, Share or Classic door, and
*Bootstrap* still records a starter pack beside each ROM you play. No
installer: unzip and run. macOS needs SDL2 (`brew install sdl2`); the app is
ad-hoc signed, so open it once, then **System Settings → Privacy & Security →
Open Anyway**.

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
> (ADR-0200, ADR-0203), never on a plain push. The assets refresh themselves
> when a promotion pull request merges into `prod`: its own build is
> republished when it compiled exactly the merged tree, and `build.yml` is
> dispatched on `prod` otherwise (ADR-0204 §6). A manual refresh still works:
> `gh workflow run build.yml --repo sbihaiko/MesenAI --ref prod`.
> A CI build carries what `prod` held when it ran. The current assets are the
> promotion in #791, so they include widescreen W.1–W.7, the Play / Remaster /
> Share GUI and the fixes through #790; anything merged into `main` since
> reaches the channel with the next promotion, or build from source:
> [COMPILING.md](COMPILING.md).
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
   for twenty-one games ship in `scripts/stages/`. No route yet? `scripts/route_search.py`
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
| **Widescreen** | Aspect-ratio stretch — the picture is never re-drawn | **Reveals the playfield the console already had**, per console; pack art, then the border, then black fill what it cannot |
| **Recording a game** | Press Start, play to the end, press Stop | Same window, **plus a headless recorder driven by scripts, states, TAS movies or RAM cheats** |
| **Knowing what you missed** | Play more and look | **Coverage per recording, per image, per state** |
| **Vocabulary** | Tiles in cartridge order | **Metatiles, sprite figures, poses, animation cycles**, inferred and marked |
| **The rule file** | By hand, or your own spreadsheet | **Generated from the sheets**; conditions for reused tiles attached from observed neighbours |
| **Validation** | None | **Linter, versioned spec, content id, sha256 errata, pack CI** |
| **Finding packs** | Forum threads | **Validated catalog**, hash-tracked, labeled by content, ranked by 👍 |
| **Pack format** | `hires.txt` per game | **MEP**: textures + audio + synth presets + a border frame + widescreen side art in one hash-keyed pack, folder or `.zip`, per-layer toggles |
| **Music export** | — | **MIDI / VGM** while you play |
| **Player GUI** | — | **Play / Remaster / Share workspaces** on a fresh install: recent games, a pause overlay with pack picker, enhancements, save-state slots, cheats and shared replays — plus a **Classic** door holding the original GUI unchanged |
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
  master switch, a per-pack disable, and ***No pack*** as a row of the picker
  when you want that game un-enhanced — stored per ROM, and the core then
  draws no pack and applies no pack's ROM patch for it (a pack in a sibling
  folder beside the ROM still wins, ADR-0049).
- **Update:** comment `/revalidate` on the Issue.
- **Cheats and replays, new and still empty:** the same loop now carries a
  [cheat code](https://github.com/sbihaiko/MesenAI/issues/new?template=cheat-code.yml)
  — one Issue per code, checked by structure only (known game, console, every
  part decodes, no duplicate; ADR-0248) — and a
  [recorded replay](https://github.com/sbihaiko/MesenAI/issues/new?template=replay.yml)
  (ADR-0205). Accepted ones land in
  [docs/community-cheats.json](docs/community-cheats.json) and
  [docs/community-replays.json](docs/community-replays.json), which Play reads:
  the Cheats sheet lists your copy's **own bundled codes first** (NES), the
  accepted community ones for its exact SHA-1 below them, with *Share This
  Cheat ↗* for a code you added yourself — on GB and SMS it is manual entry
  only, the sheet saying *no cheat list for this console yet* — and
  *Save states → Shared replays…* lists the replays. Both community catalogs
  are live and have no entries yet.

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
- **Widescreen that reveals instead of stretching** (ADR-0253, slices W.1–W.7,
  2026-10-03): the console draws the columns its own background map already has
  (NES 64 px per side, GB/GBC and Game Gear 48, GBA 22), what it cannot draw
  comes from the pack's `widescreen` art, then the border layer, then black,
  and a game with neither a Reveal nor pack art gets the switch disabled with
  its reason, remembered per ROM. Standard frames stay bit-identical
  (ADR-0162), the extra columns are never recorded as tiles, and a double-width
  frame still goes through both NTSC filters and the recorder whole.
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
- The Play / Remaster / Share / Classic GUI (ADR-0241, ADR-0250; slices
  G.1–G.9): the shell and its per-door Tools ⋯, the Play home, pause overlay
  and its sheets, the first-run sheet, the Look tab (ADR-0246), Remaster's
  recording, kit browser, import and Build & Show in Game (NES), Share's pack,
  project-package and replay flows, and the fourth door, Classic, which is the
  original GUI and the only one a classic window opens from — each surface's
  rules unit-tested and its wiring tested headless against the real core.
- Community cheats (ADR-0248) and shared replays (ADR-0205), submit and
  consume: Issue Forms, structural gates, generated catalogs, Play's sheets.
- A CI gate on every pull request to `main` and every push to `main`: the
  structural suite, the Python tool suites, a headless boot of the real core,
  about 2 080 dependency-free C++ unit tests, and the C# unit and headless-UI
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
  (shared replays and community cheats), the Play / Remaster / Share / Classic
  GUI (G.1–G.9) and widescreen (W.1–W.7) are delivered too; and the live work is
  **Phase 14** (proof at scale) plus Phase 7's open rows (shaders on macOS,
  in-place pack change, cheat search), of the
  [roadmap](docs/roadmap/PRD-mesence-enhancement-ecosystem.md) opened from a
  [side-by-side with upstream](docs/hd-pack-toolchain-comparison.md) that says
  where a hand author is still better served.
- A human artist who did not build the tools has not yet run the painting
  workflow end to end. Every acceptance so far is measured, but by proxy.
- Nobody has opened the new GUI on a real display yet: its look against the
  wireframes, a real gamepad, drag-and-drop, the browser and Finder hand-offs
  and a Remaster build shown on a running game are still human rows.
  Widescreen's GBA path was built with no GBA ROM in reach, so its 22 extra
  columns and its black fallback for an affine or bitmap-mode frame have never
  been seen on a display — everything else about it is unit-tested. Neither
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

**Can I turn it all off?** Yes — *Modern instruments* off on the Enhancements
sheet, or the Enhanced Audio checkbox in Options → Audio, and you have stock
Mesen accuracy.

**Does Widescreen stretch the picture?** Not any more. The console draws the
playfield it already had beside the screen, and only what it cannot draw comes
from the loaded pack's art, the border layer or black (ADR-0253). Sprites still
enter at the original edge: the game's own logic is untouched. If a game has
neither, the switch is disabled and says why.

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
