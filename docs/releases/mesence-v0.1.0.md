# MesenAI v0.1.0

**The first binary release of this fork.** Until now, trying MesenAI meant
building it; this is the download.

MesenAI is a platform for **extracting, authoring and consuming community
enhancement packs** — textures, music and synth presets — while keeping the
emulator itself legally clean. The community produces the content; the project
ships the tools, the open specs and a validated catalog, and stays out of the
way.

## macOS Apple Silicon only, and why

This release is **macOS on Apple Silicon and nothing else**. That is a
deliberate scope decision, not an oversight: the project's CI compiles Linux
only, and no job of it produces a macOS binary, so a release has to be
cut on a maintainer's machine, and the maintainer's machine is an Apple Silicon
Mac. Shipping one build that has actually been run beats shipping seven that
have not.

Windows, Linux and Intel macOS follow once each has a build somebody has
launched. Until then, every other platform builds from source — `make core &&
make ui` — and every Python stage of the remastering pipeline already runs
anywhere Python does.

## What is in this release

| File | What it is |
|---|---|
| `MesenAI-v0.1.0-macos-arm64.zip` | `Mesen.app` (ad-hoc signed) + `headless_record` + `MesenCore.dylib` |
| `mesenai-tools-v0.1.0.zip` | the command-line tools the remastering guide uses, the shipped recording routes, the three guides and a `requirements.txt` |
| `SHA256SUMS` | hashes of both |

Both were built by `make release-macos VERSION=v0.1.0`
(`scripts/release_macos.sh`) from the commit recorded in each zip's `COMMIT`
file. The same command reproduces them: a release nobody can rebuild is a
release nobody can check.

The build does three things a plain `make ui` does not, and the ones that
matter are checked rather than assumed:

- the freshly built `MesenCore.dylib` is copied into `Mesen.app`, because
  `dotnet publish -t:BundleApp` does not refresh it when the C# build is
  already up to date — a bundle can otherwise carry a stale core with every
  timestamp looking right. The script verifies the copy landed byte for byte
  and, after signing, **compares the linker's `LC_UUID` inside the signed
  bundle with the one in the build tree and fails if they differ** — signing
  rewrites the file, but not that stamp;
- the bundle is ad-hoc codesigned and `codesign --verify --deep --strict` has
  to pass;
- `headless_record` is relinked to find its core through `@executable_path`,
  because `make capture-tool` links it for use from a checkout. The script
  fails if `otool -L` still mentions the build workspace.

**SDL2** must be present (`brew install sdl2`). The app is ad-hoc signed and
**not notarised**, so Gatekeeper refuses the first launch: right-click → Open →
Open, or `xattr -dr com.apple.quarantine Mesen.app`.

## Remastering a game's art

The reason the tools ship beside the emulator: **[docs/remastering-a-game.md](https://github.com/sbihaiko/MesenAI/blob/main/docs/remastering-a-game.md)**
is the end-to-end guide from a recording of a game you own to a pack you can
paint and play. Record → measure coverage → unpack into a kit of PNGs → paint →
build and verify → ship. `mesenai-tools-v0.1.0.zip` carries every Python tool
that guide names; `headless_record`, its stage 1, is in the macOS zip. Each zip
has its own `README.md` saying how to set it up.

Orientation on the pack format:
[docs/enhancement-ecosystem.md](https://github.com/sbihaiko/MesenAI/blob/main/docs/enhancement-ecosystem.md).
Getting a finished pack listed:
[docs/hd-pack-authoring.md](https://github.com/sbihaiko/MesenAI/blob/main/docs/hd-pack-authoring.md).
The catalog of validated community packs:
[docs/community-packs.md](https://github.com/sbihaiko/MesenAI/blob/main/docs/community-packs.md).

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
- **macOS Apple Silicon only**, for the reason at the top. No Intel Mac, no
  Windows, no Linux binary in this release.
- **The app is not notarised.** Ad-hoc signing means Gatekeeper's first-launch
  refusal is expected, not a sign of a bad download; compare against
  `SHA256SUMS` if in doubt.
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
- **The `audio` pack layer applies on NES only.** GB and SMS wait for the
  `hires.txt` extension to freeze.

## Relationship to upstream

MesenAI is a fork of [Mesen2](https://github.com/SourMesen/Mesen2) by Sour and
of [MesenCE](https://github.com/nesdev-org/MesenCE) by the nesdev.org
community, and keeps their accuracy, debugger, netplay, shaders, run-ahead and
rewind. Upstream fixes are ported in regularly. **Nothing goes the other way:**
this fork is AI-assisted by design and upstream's contribution policy does not
accept AI-assisted pull requests, so none of this is proposed upstream.

GPL v3. Copyright (C) 2014-2026 Sour, 2026 contributors. The specs in
`docs/specs/` are CC0.
