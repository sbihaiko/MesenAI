# ADR-0213: A painting surface's file name is a contract with the artist's paint program, checked where it is written

- Status: **accepted 2026-09-19 and implemented the same turn as F12.4** — the PRD's F12.4 row is an accepted slice and this ADR records the contract it ships. User go-ahead, verbatim: *"/goal F12,4"*. CLAUDE.md allows same-turn implementation when the change ships with unit tests covering the decision and the go-ahead is quoted here and in the PR body; both hold.
- Date: 2026-09-19
- Related: PRD Part A F12.4 (asset-name template for the paint program), ADR-0209 (the four-step artist loop; Q3 "how does the file come back"), ADR-0212 (F12.3's in-place reload), ADR-0183 (the artist kit and its four surface kinds), ADR-0165 (stdlib-only toolchain), ADR-0153 (artist-legible sheets)

## Context

ADR-0209 splits the work: MesenAI owns **selection** (which art, named) and
**return** (the edited file coming back and rendering), and delegates
**painting** to whatever program the artist already uses. F12.3 shipped the
return — a PNG overwritten on disk is re-decoded in place and on screen without
reopening the ROM (ADR-0212). What is left between the two is the file name,
and a name is not cosmetic here: **the paint program reads it as a directive.**

Three readers have to accept every name the kit writes, and they disagree.

1. **Photoshop's *Generate Image Assets*.** A layer named `usr000.png`
   generates a file of that name on every save of the document. The layer-name
   grammar is `[scale] name.extension[quality]`, comma-separated for several
   assets on one layer. So `run,walk.png` generates two files and neither is
   the surface; `2x usr000.png` generates a resized `usr000.png`; `usr000.png24`
   generates a 24-bit `usr000.png` — **without the alpha channel** every
   surface depends on. None of these fail loudly. They produce a plausible file
   that is the wrong one.
2. **Aseprite, Krita and GIMP.** Each exports to a path the artist picks once
   and repeats with a single command (*Repeat last export*, *File > Overwrite
   `<name>.png`*). For these the name only has to be a name the file system
   keeps.
3. **The file system, which may be Windows.** A kit travels. `aux.png` cannot
   exist there whatever the extension; `<>:"|?*` are refused; a stem ending in
   a space or a dot is silently truncated to a different file; and `Chr_0.png`
   and `chr_0.png` are one file on macOS and Windows and two here.

Today's names — `usr000.png`, `Chr_0.png`, `screen001.png`, `stage1-000.png` —
happen to satisfy all three. That is a coincidence of convention, not a
property: two of them are already derived from outside input (`artist_map.py`
builds `<stage>-NNN.png` from a command-line argument and
`pano-<map stem>.png` from a file name), and F12.9 and F12.11 will add surface
kinds this phase has not written yet.

**What this deliberately does not do.** It does not write `.psd`, `.aseprite`
or `.kra`, and it does not read them (ADR-0165, and the F12.4 row says so). It
does not watch the file system. It does not decide *where* the artist's program
writes — see §4, which is the honest half of this decision.

## Decision

### 1. One module owns the rule, and every generator calls it

`scripts/asset_names.py` (stdlib, no dependency on the kit) holds the whole
contract: `check_asset_name` returns the reasons a name is unusable,
`check_asset_set` the reasons a *folder* of names is, `sanitize_asset_stem`
turns outside input into something that passes, and `asset_name_for` gives the
string an artist pastes.

The rules, each with the reader it protects and a test:

| refused | because |
|---|---|
| `,` | Photoshop splits one layer into several assets on it |
| `/`, `\` | in a layer name `/` means a subfolder, so the asset lands somewhere the artist did not look |
| leading `2x `, `200% `, `100x50 ` | Photoshop reads it as a resize directive and writes a shorter name |
| not ending in exactly `.png` | Photoshop generates nothing for an unknown extension, and `.png8`/`.png24` drop the alpha |
| `<>:"|?*`, control characters | Windows refuses them in a file name |
| non-ASCII | a kit travels through zips and file systems that disagree about encoding |
| leading/trailing whitespace, a stem ending in a space or a dot | Photoshop trims it, Windows strips it — either way the file is not the one the manifest names |
| `CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9`, `LPT1`–`LPT9` as the stem | reserved on Windows whatever the extension |
| over 100 characters | a kit plus a Photoshop `-assets` sibling has to fit inside Windows' 260-character path limit |
| two names in one folder differing only in case | one file on the artist's machine, two on ours |

The last one is the only rule that does not exist per name, which is why
`check_asset_set` is separate.

### 2. A name we compose raises; a name derived from input is sanitized

Two classes of caller, and they are treated differently on purpose.

- `usr000.png`, `Chr_0.png`, `screen001.png` are **composed by us**.
  `require_asset_name` raises `AssetNameError`. A violation there is our bug,
  and quietly renaming the file would leave the manifest pointing at something
  that is not on disk — worse than stopping.
- `<stage>-NNN.png` and `pano-<map stem>.png` come from **outside**.
  `sanitize_asset_stem` rewrites them, and the caller records the original when
  the two differ (`artist_map.py` writes `renamedStage` into its fragment, and
  only when it applies). Nothing is lost silently.

### 3. The kit states the name, and the assembler is the last gate

Every entry of a `kit-part-*.json` fragment gains `assetName` — the base name,
which is exactly the string to paste as a Photoshop layer name. The assembler
computes it, and in doing so re-checks every surface and every folder, so a
generator added later cannot reach `kit.json` with a name the artist's program
would mangle. A violation is a `KitError` naming the file and the reader.

`ARTIST.md` gains an **"Open, paint, save"** section: the one step per program,
and the sentence that says the save is followed by *HD Packs > Reload Repainted
Images* rather than by reopening the ROM.

### 4. Photoshop's output folder is not ours to fix, and the docs say so

This is the part it would be easy to overstate. Photoshop's generator always
writes into `<document>-assets/` beside the `.psd`, and **that location is not
configurable** — no layer name reaches out of it, because `/` only descends.
So on Photoshop the exported file carries the right *name* and sits one copy
away from the kit. GIMP, Aseprite and Krita overwrite the kit file in place.

Three responses were considered and two rejected:

- **Rename the kit's folders to `<part>-assets`** so Photoshop's output lands
  on them. Rejected: `sheets/` and `chr/` are drop-in for a pack's own layout
  (the kit contract, ADR-0183), and bending the pack to one program's
  convention is the wrong way round.
- **Symlink `<part>-assets` at the kit's `<part>`.** Rejected: it needs
  Developer Mode or an elevated prompt on Windows, so the portable kit stops
  being portable exactly where the problem was.
- **Say it, in one line, where the artist reads it.** Chosen. It is one copy,
  it is honest, and it costs nothing to a Photoshop user who was going to copy
  anyway. `docs/remastering-a-game.md` and `ARTIST.md` both carry it.

ADR-0209's **Q3** ("how does the file come back") stays formally open. What
F12.4 settles is only that its option (i) — *"whatever F12.4's template already
decides"* — resolves to **(h), same path, explicit re-import**, because that is
the mechanism F12.3 shipped and the stop rule is met on it. Option (g), a
watcher that fires the reload without being asked, is neither adopted nor
refused here; it is a follow-up slice with its own race to answer (a
half-written PNG), and picking it is a human's call.

### 5. Verification

- `scripts/test_asset_names.py`, wired into `make doc-checks`: every refusal
  above, and — as important — the live Contra kit's real names asserted
  **valid**, so the contract stays a guard against something rather than
  against everything.
- `scripts/test_artist_kit_assemble.py`: `assetName` is stamped, a comma stops
  the kit, a case-only clash inside one folder stops it and the same name in
  two folders does not.
- The slice's stop rule, from the PRD: saving in the paint program overwrites
  the kit PNG and F12.3 renders it — measured on the Contra and Zelda kits and
  logged in `docs/validation/`, as valid names, reload fired and a pixel-exact
  frame.

## Consequences

- **A generator can now fail where it used to write.** Any surface name that
  breaks the contract raises instead of landing on disk. That is the point, and
  it will be felt first by whoever adds the next surface kind (F12.9's static
  pages, F12.11's `.ora`) — the contract is the thing they have to satisfy, and
  `require_asset_name` is one line.
- **`sanitize_asset_stem` can collapse two different inputs onto one name.**
  Two stages called `stage 1` and `stage-1` both become `stage-1`. The folder
  rule of §1 does not catch it, because the second write simply overwrites the
  first. Nothing in the bounded library produces such a pair, and the honest
  record is that this is a known hole, not a handled case; a stage id is
  operator-supplied and the fix, if it is ever needed, is a collision check at
  the call site rather than more cleverness in the rewrite.
- **`assetName` is redundant with `path`** and could be derived by any reader.
  It is written anyway because the artist copies it by hand, and a page that
  makes them strip a folder prefix is a page that gets it wrong once.
- **Photoshop users copy one folder.** Stated in two places rather than solved.
  If the F12.11 `.ora` path or the artist evidence later shows a Photoshop
  population large enough to justify it, a `kit-sync` helper is a small slice —
  but inventing it before anyone has asked would be building for an imagined
  user, which ADR-0183 §3's discipline exists to refuse.
