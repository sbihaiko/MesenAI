# MesenCE tools

The command-line tools [`docs/remastering-a-game.md`](docs/remastering-a-game.md)
uses, packaged for someone who downloaded a MesenCE binary instead of building
the repository. The release and the exact commit it was cut from are in
`VERSION` and `COMMIT`.

*(This file is `docs/releases/tools-zip-README.md` in the repository;
`scripts/release_macos.sh` copies it in as the zip's `README.md`.)*

## Install

```sh
python3 -m pip install -r requirements.txt
```

Python 3.10 or newer. `Pillow` and `numpy` are the only third-party packages
the tools import — everything else is the standard library.

Run every tool from the folder this file is in, so that `scripts/<tool>.py`
resolves: the tools import each other by module name and shell out to each
other by path.

## Start here

`docs/remastering-a-game.md` — record a game, measure what the recording
reached, unpack it into a kit of PNGs, paint, build and verify a pack.

`docs/hd-pack-authoring.md` — getting a finished pack validated and listed in
the community catalog.

`docs/enhancement-ecosystem.md` — the two-minute orientation on the pack
format and why the project ships tools and never game files.

## `headless_record` — stage 1 of the guide

Stage 1 is a compiled C++ tool, not a Python one, because it drives the
emulator core directly, so it does not travel in this zip.

- **macOS (Apple Silicon)**: it ships inside
  `MesenCE-<version>-macos-arm64.zip`, beside `Mesen.app`, with the core
  library it links against. Keep the two together and run it from there.
- **Every other platform**: there is no build in this release. Build it from a
  repository checkout with `make core && make capture-tool`. Every other stage
  of the pipeline is Python and runs anywhere Python does.

`headless_record` also needs **SDL2** on the host, the same requirement the
emulator itself has: `brew install sdl2` on macOS,
`sudo apt install libsdl2-2.0-0` on Debian/Ubuntu.

## What is deliberately not here

The repository's test suites, the community-pack CI helpers, and the
maintainer scripts that need the `gh` CLI. Clone the repository if you want
those.
