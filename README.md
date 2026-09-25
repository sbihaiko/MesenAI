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
your stored per-ROM choice follows it.

**→ [Download](#download)** · [Community packs](docs/community-packs.md)

</td>
<td width="33%" valign="top">

### 🎨 I want to remaster a game

Record the game once — scripted, from a save state, or driven by a published
TAS. Get back **sprite figures with their animation cycles, the stage stitched
into one panorama, and completed pattern pages**, each cell labeled. Paint the
PNGs. Build. See it in the game.

**→ [Remastering guide](docs/remastering-a-game.md)**

</td>
<td width="33%" valign="top">

### 📦 I made (or found) a pack

Open one pre-filled Issue with a link. A bot downloads it, lints it against an
open spec, labels it and lists it in the public catalog with a 👍 vote. Classic
`hires.txt` packs qualify as-is — years of community work, one ecosystem.

**→ [Submit a pack](https://github.com/sbihaiko/MesenAI/issues/new?template=community-pack.yml)**

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
| Play the whole game with the recorder on | **Write the route down.** Frame-counted input scripts, save states, published TAS movies and RAM-only cheats drive a headless recorder | ~3× real time, deterministic in emulated frames |
| Hope you saw everything | **Measure coverage, then steer.** Per recording, per image, which tiles only *that* state shows | Contra: 53.8 % → 58.9 % → 64.6 % across three recordings |
| Untangle thousands of 8×8 fragments | **A kit of four surfaces.** Figures with animation cycles, named scenery, stage panoramas, completed pattern pages — every cell labeled | Contra stage-3 boss: 517 poses over 195 distinct tiles, a 50× reuse the kit makes visible |
| Hand-write the rule file (or a 34-sheet spreadsheet) | **Build it from the sheets.** Ambiguous reused tiles get their conditions from observed neighbours, automatically | 0 tile keys lost, 0 invented, on every generator's round trip |
| No linter, no spec | **Lint against an open spec.** `mep_lint.py`, MEP v1, canonical content id, sha256 errata, pack CI | 15 community packs validated by the same script you run offline |

Everything a generator infers is marked as inference. Nothing that changes what
a rebuilt pack renders is emitted unless the recording actually observed it.
Names come from the data or from a human — never from a guess.

---

## Quick start

1. **[Download](#download)**, unzip, run `Mesen`.
2. **File → Open** a ROM. Enhanced Audio is already on (Style: *Studio*). With
   *Bootstrap* on, a starter enhancement pack is written beside the ROM while
   you play.
3. Different sound? **Settings → Audio → General → Enhanced audio** — pick
   Synthwave, Chip Deluxe, Orchestral Lite, Dry or Studio, or point it at your
   own `.sf2` SoundFont.
4. Got a pack? Drop the folder or `.zip` beside the ROM (or into
   `EnhancementPacks/`) and toggle textures / audio / synth per pack under
   **Tools → HD Packs → Enhancement Packs (MEP)…**.
5. Want the soundtrack as MIDI or VGM? **Tools → Record Music (MIDI/VGM)**.

Want to redraw a game? Start at **[docs/remastering-a-game.md](docs/remastering-a-game.md)** —
every command, in order, and the release ships every tool it uses.

## Download

**[Releases](https://github.com/sbihaiko/MesenAI/releases/latest)** carry the
emulator plus `mesenai-tools-<version>.zip`, the command-line tools the
remastering guide uses. **v0.1.0 is macOS Apple Silicon only**, cut locally
from a tagged commit. No installer: unzip and run. macOS needs SDL2
(`brew install sdl2`); the app is ad-hoc signed, so open it once, then
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
> Building from source: [COMPILING.md](COMPILING.md).

---

## How a remaster happens

```
  record ──▶ measure ──▶ unpack ──▶ paint ──▶ build ──▶ see it
  (route)   (coverage)    (kit)     (PNGs)   (hires.txt)  (in game)
```

1. **Record** the game doing everything it can do. Four drivers feed one
   builder: a frame-counted input script, a **save state** to start mid-level,
   a published **TAS movie** (`.bk2`), or a **RAM-only cheat** to reach a
   later stage. No window, no human at the pad, about 3× real time.
2. **Measure** what the recording put on screen — per image, per state — and
   write a better route if a figure is missing.
3. **Unpack** the recording into a **kit**: sprite figures and their cycles,
   named scenery, the stage as one scrolling panorama, and pattern pages
   completed from the cartridge — each with a sidecar saying what every cell
   is and whether it was *seen* or *inferred*.
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
| **HD textures** | NES only | **NES, Game Boy/GBC, SMS/Game Gear/SG-1000** |
| **Recording a game** | Press Start, play to the end, press Stop | Same window, **plus a headless recorder driven by scripts, states, TAS movies or RAM cheats** |
| **Knowing what you missed** | Play more and look | **Coverage per recording, per image, per state** |
| **Vocabulary** | Tiles in cartridge order | **Metatiles, sprite figures, poses, animation cycles**, inferred and marked |
| **The rule file** | By hand, or your own spreadsheet | **Generated from the sheets**; conditions for reused tiles attached from observed neighbours |
| **Validation** | None | **Linter, versioned spec, content id, sha256 errata, pack CI** |
| **Finding packs** | Forum threads | **Validated catalog**, hash-tracked, labeled by content, ranked by 👍 |
| **Pack format** | `hires.txt` per game | **MEP**: textures + audio + synth presets in one hash-keyed pack, folder or `.zip`, per-layer toggles |
| **Music export** | — | **MIDI / VGM** while you play |
| **Player mode** | — | Couch shell on a fresh install: overlay, recent games, pack picker |
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
- The headless recorder, all four drivers, coverage measurement, the four-surface
  kit, `mep_build`/`mep_lint`, the composition editor, auto-attached
  `spriteNearby`/`tileNearby` and hand-written conditions checked against
  recorded routes, the in-place reload of repainted images, importing a legacy
  `hires.txt` pack, 15 validated community packs auto-installing.
- A CI gate on every push: the structural suite, the Python tool suites and a
  headless boot of the real core. 1193 dependency-free C++ unit tests and a C#
  xUnit suite run locally.

**Not yet, and named as such**
- Two limits stand by design, not as gaps: a changed `hires.txt` still needs the
  ROM reopened — the in-place reload covers repainted images — and the layered
  `.ora` is write-only, so the flat PNG stays the return path. Otherwise
  **Phase 12** is delivered, with only human rows left; the live phase is
  **14**, proof at scale, of the
  [roadmap](docs/roadmap/PRD-mesence-enhancement-ecosystem.md) opened from a
  [side-by-side with upstream](docs/hd-pack-toolchain-comparison.md) that says
  where a hand author is still better served.
- A human artist who did not build the tools has not yet run the painting
  workflow end to end. Every acceptance so far is measured, but by proxy.
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
Record again where the number says so.

**Will you host packs?** No. Packs stay with their authors; MesenAI validates
and [catalogs](docs/community-packs.md) them, and the emulator can consume any
[MEI](docs/specs/MEI-v1.md) index.

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
