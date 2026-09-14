# MesenCE v0.1.0

**The first binary release of this fork.** Until now, trying MesenCE meant
building it; this is the download.

MesenCE is a platform for **extracting, authoring and consuming community
enhancement packs** — textures, music and synth presets — while keeping the
emulator itself legally clean. The community produces the content; the project
ships the tools, the open specs and a validated catalog, and stays out of the
way.

## What is in this release

| File | What it is |
|---|---|
| `Mesen (Windows - net10.0 - AoT).zip` | Windows x64, ahead-of-time compiled, no runtime to install. Windows 10 (1607) or newer |
| `Mesen (Windows - net10.0).zip` | Windows x64, single-file, framework-dependent |
| `Mesen (Linux - ubuntu-22.04 - clang_aot).zip` | Linux x64 |
| `Mesen (Linux - ubuntu-22.04-arm - clang_aot).zip` | Linux ARM64 |
| `Mesen (Linux x64 - AppImage).zip` / `Mesen (Linux ARM64 - AppImage).zip` | the same builds as a single executable with a desktop entry |
| `Mesen (macOS - macos-15 - clang_aot).zip` | macOS Apple Silicon, `Mesen.app` |
| `Mesen (macOS - macos-15-intel - clang_aot).zip` | macOS Intel, `Mesen.app` |
| `mesence-tools-v0.1.0.zip` | the command-line tools the remastering guide uses, plus the three guides and a `requirements.txt` |
| `mesence-tools-native-v0.1.0-<os>-<arch>.zip` | `headless_record`, the compiled recorder the guide's stage 1 runs, with its core library — Linux and macOS only |

Linux and macOS need **SDL2** on the host (`sudo apt install libsdl2-2.0-0`,
`brew install sdl2`). On macOS the app is signed with the project's own
certificate rather than an Apple Developer ID, so on first launch Gatekeeper
refuses it: open it once, dismiss the warning, then **System Settings → Privacy
& Security → Open Anyway**.

## Remastering a game's art

The reason the tools ship beside the emulator: **[docs/remastering-a-game.md](https://github.com/sbihaiko/MesenCE/blob/main/docs/remastering-a-game.md)**
is the end-to-end guide from a recording of a game you own to a pack you can
paint and play. Record → measure coverage → unpack into a kit of PNGs → paint →
build and verify → ship. `mesence-tools-v0.1.0.zip` carries every tool that
guide names, and its own `README.md` says how to install them.

Orientation on the pack format:
[docs/enhancement-ecosystem.md](https://github.com/sbihaiko/MesenCE/blob/main/docs/enhancement-ecosystem.md).
Getting a finished pack listed:
[docs/hd-pack-authoring.md](https://github.com/sbihaiko/MesenCE/blob/main/docs/hd-pack-authoring.md).
The catalog of validated community packs:
[docs/community-packs.md](https://github.com/sbihaiko/MesenCE/blob/main/docs/community-packs.md).

## What it runs

**NES / Famicom** · **Game Boy / Game Boy Color / GBS** · **Master System /
Game Gear / SG-1000** (including YM2413 FM) · **Game Boy Advance**.

Not included, deliberately: SNES (including Super Game Boy), PC Engine,
WonderSwan, ColecoVision. Those cores were removed so the effort could go into
the enhancement layer; the four families that remain are the ones whose
enhancement communities already exist.

## Legal footing

**Ship the tool, never the files.** Extraction and authoring happen on your
machine, from ROMs you own, and the output stays there. This project
distributes the emulator, the tools, the open specs and a catalog of links —
it hosts no game assets, no extracted tiles, no transcribed music, and no pack
containing someone else's art. Packs stay with their authors.

## Version scheme

Semantic versioning, starting at `0.1.0` and staying on `0.x` until the guide
has been run end-to-end by someone who did not write it; tags are
`mesence-vMAJOR.MINOR.PATCH`.

## Known limitations

- **Nobody outside the project has used this yet.** Every issue, pull request
  and pack submission so far is the maintainer's. Treat `0.1.0` as what it
  says it is: the first build that leaves the machine it was written on.
- **No stage panorama for CHR ROM games.** `artist_map.py` stitches a scrolling
  stage into one image only for games that key tiles by pattern data (CHR RAM
  boards). On a CHR ROM game — Zelda II, Mega Man 3 — it refuses with "keys its
  tiles by CHR index" and you work from the sprite, scenery and pattern-page
  surfaces instead.
- **TAS recording is per-ROM-revision.** A movie only replays against the exact
  ROM it was recorded from; the published Contra runs are made against the
  Japanese ROM, so driver B works there and desyncs on the US one. The recorder
  refuses an unrecognised container rather than archiving a half-diverged
  playthrough, and `sync-watch=` lets a run fail itself.
- **`headless_record` is not built for Windows.** It is a `make` target and the
  Windows binary is built with MSBuild. Build it from a checkout, or run the
  recording stage under WSL; the rest of the guide is Python and works.
- **The `audio` pack layer applies on NES only.** GB and SMS wait for the
  `hires.txt` extension to freeze.
- **Binaries are built on demand.** The 14-job matrix in `build.yml` runs on
  `workflow_dispatch` only, so there is no per-commit build to download between
  releases.

## Relationship to upstream

MesenCE is a fork of [Mesen2](https://github.com/SourMesen/Mesen2) by Sour and
of [MesenCE](https://github.com/nesdev-org/MesenCE) by the nesdev.org
community, and keeps their accuracy, debugger, netplay, shaders, run-ahead and
rewind. Upstream fixes are ported in regularly. **Nothing goes the other way:**
this fork is AI-assisted by design and upstream's contribution policy does not
accept AI-assisted pull requests, so none of this is proposed upstream.

GPL v3. Copyright (C) 2014-2026 Sour, 2026 contributors. The specs in
`docs/specs/` are CC0.
