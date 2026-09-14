# MesenCE native tools — `headless_record`

Stage 1 of [`docs/remastering-a-game.md`](docs/remastering-a-game.md) is
`scripts/headless_record`: a compiled tool that drives the emulator core and
writes out every tile the game drew. This zip is that tool, built for one
operating system and one architecture, with the core library it needs beside
it. The version and platform are in the zip's name and in `VERSION`.

*(This file is `docs/releases/native-tools-README.md` in the repository;
`build.yml` copies it in as the zip's `README.md`.)*

## Install

Unpack this zip **into the same folder as `mesence-tools-<version>.zip`**, so
that one `scripts/` folder ends up holding both `headless_record` and the
Python tools. The guide's commands are written for that layout:

```
mesence-tools-<version>/
  docs/
  requirements.txt
  scripts/
    headless_record        <- from this zip
    MesenCore.so|.dylib    <- from this zip, must stay beside it
    artist_kit.py
    mep_build.py
    ...
```

`MesenCore.so` / `MesenCore.dylib` is found through a relative runtime path,
so it has to stay in the same folder as the binary. Moving `headless_record`
on its own breaks it.

## Requirements

**SDL2**, the same dependency the emulator itself has:

```sh
brew install sdl2                  # macOS
sudo apt install libsdl2-2.0-0     # Debian/Ubuntu
```

On macOS the binary is ad-hoc signed, not notarised. If Gatekeeper blocks the
first run, clear the quarantine attribute:

```sh
xattr -dr com.apple.quarantine scripts/headless_record scripts/MesenCore.dylib
```

## Check it runs

```sh
scripts/headless_record
```

With no arguments it prints its usage — the three positional arguments and the
flags the guide uses. If instead you get a dynamic-linker error, the library is
not beside the binary.

## Windows

There is no Windows build: `headless_record` is a `make` target, and the
Windows release is built with MSBuild, which has no equivalent. Build it from a
repository checkout with `make core && make capture-tool`, or run the recording
stage under WSL. Every other stage of the guide is Python and runs on Windows
unchanged.
