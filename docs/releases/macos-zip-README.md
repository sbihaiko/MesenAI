# MesenCE — macOS (Apple Silicon)

This zip holds the emulator and the compiled recorder the remastering guide
uses. `VERSION` and `COMMIT` name the release and the exact commit it was
built from.

*(This file is `docs/releases/macos-zip-README.md` in the repository;
`scripts/release_macos.sh` copies it in as the zip's `README.md`.)*

## Contents

```
MesenCE-<version>-macos-arm64/
  Mesen.app              the emulator
  headless_record        stage 1 of docs/remastering-a-game.md
  MesenCore.dylib        the core headless_record links against — keep it beside the binary
  VERSION, COMMIT
```

Apple Silicon only. There is no Intel, Linux or Windows build in this release;
see the release notes for why.

## Run the emulator

Drag `Mesen.app` anywhere and open it. It is **ad-hoc signed, not notarised**,
so Gatekeeper refuses the first launch. Either right-click → Open → Open, or:

```sh
xattr -dr com.apple.quarantine /path/to/Mesen.app
```

**SDL2** must be present, the same dependency the emulator has when built from
source: `brew install sdl2`.

## Run the recorder

```sh
./headless_record
```

With no arguments it prints its usage. `MesenCore.dylib` is found through
`@executable_path`, so it has to stay in the same folder as the binary —
moving `headless_record` on its own breaks it. If Gatekeeper blocks it:

```sh
xattr -dr com.apple.quarantine headless_record MesenCore.dylib
```

The guide's commands are written as `scripts/headless_record`, because they
address someone working from a repository checkout. From this download, the
path is wherever you unpacked it.

## Next

`mesence-tools-<version>.zip` carries the Python half of the pipeline and the
three guides. Unpack both and start at
[`docs/remastering-a-game.md`](https://github.com/sbihaiko/MesenCE/blob/main/docs/remastering-a-game.md).
