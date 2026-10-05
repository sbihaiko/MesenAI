# ADR-0213: A painting surface's file name is a contract with the artist's paint program, checked where it is written

- Status: **accepted 2026-09-19 and implemented the same turn as F12.4** — the PRD's F12.4 row is an accepted slice and this ADR records the contract it ships. User go-ahead, verbatim: *"/goal F12,4"*. CLAUDE.md allows same-turn implementation when the change ships with unit tests covering the decision and the go-ahead is quoted here and in the PR body; both hold.
- Date: 2026-09-19
- Related: PRD Part A F12.4 (asset-name template for the paint program), ADR-0209 (the four-step artist loop; Q3 "how does the file come back"), ADR-0212 (F12.3's in-place reload), ADR-0183 (the artist kit and its four surface kinds), ADR-0165 (stdlib-only toolchain), ADR-0153 (artist-legible sheets)

## Context

ADR-0209 gives MesenAI **selection** (which art, named) and **return** (the edited file coming back and rendering) and delegates **painting** to the artist's program. F12.3 shipped the return (an overwritten PNG is re-decoded in place, ADR-0212); what is left is the file name, and it is not cosmetic — **the paint program reads it as a directive.** Three readers must accept every name the kit writes, and they disagree.

1. **Photoshop's *Generate Image Assets*.** A layer name is `[scale] name.extension[quality]`; `run,walk.png` splits into two files, `2x usr000.png` resizes, and `usr000.png24` drops the alpha every surface needs — none failing loudly.
2. **Aseprite, Krita and GIMP.** Each exports by *Repeat last export* or *File > Overwrite `<name>.png`*, needing only a name the file system keeps.
3. **The file system, which may be Windows.** `aux.png` cannot exist, `<>:"|?*` are refused, a trailing space/dot is truncated, and `Chr_0.png`/`chr_0.png` are one file on macOS/Windows and two here.

Today's names — `usr000.png`, `Chr_0.png`, `screen001.png`, `stage1-000.png` — satisfy all three by convention, not property: `artist_map.py` builds `<stage>-NNN.png` and `pano-<map stem>.png` from outside input, and F12.9/F12.11 add kinds this phase has not written.

**What this deliberately does not do.** It does not write `.psd`, `.aseprite` or `.kra`, and does not read them (ADR-0165). It does not watch the file system, and it does not decide *where* the program writes — see §4.

## Decision

### 1. One module owns the rule, and every generator calls it

`scripts/asset_names.py` (stdlib, no dependency on the kit) holds the whole contract: `check_asset_name` returns the reasons a name is unusable, `check_asset_set` the reasons a *folder* of names is, `sanitize_asset_stem` turns outside input into something that passes, and `asset_name_for` gives the string an artist pastes.

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

The last is the only rule that does not exist per name, which is why `check_asset_set` is separate.

### 2. A name we compose raises; a name derived from input is sanitized

Two classes of caller, treated differently on purpose.

- `usr000.png`, `Chr_0.png`, `screen001.png` are **composed by us**. `require_asset_name` raises `AssetNameError`. A violation there is our bug, and quietly renaming the file would leave the manifest pointing at something not on disk — worse than stopping.
- `<stage>-NNN.png` and `pano-<map stem>.png` come from **outside**. `sanitize_asset_stem` rewrites them, and the caller records the original when the two differ (`artist_map.py` writes `renamedStage` into its fragment, and only when it applies). Nothing is lost silently.

### 3. The kit states the name, and the assembler is the last gate

Every entry of a `kit-part-*.json` fragment gains `assetName` — the base name, exactly the string to paste as a Photoshop layer name. The assembler computes it and in doing so re-checks every surface and every folder, so a generator added later cannot reach `kit.json` with a name the artist's program would mangle. A violation is a `KitError` naming the file and the reader.

`ARTIST.md` gains an **"Open, paint, save"** section: the one step per program, and the sentence that says the save is followed by *HD Packs > Reload Repainted Images* rather than by reopening the ROM.

### 4. Photoshop's output folder is not ours to fix, and the docs say so

Photoshop always writes into `<document>-assets/` beside the `.psd`, and **that location is not configurable** — no layer name reaches out of it, because `/` only descends. So the exported file carries the right *name* and sits one copy away from the kit; GIMP, Aseprite and Krita overwrite the kit file in place. Three responses, two rejected:

- **Rename the kit's folders to `<part>-assets`** so Photoshop's output lands on them. Rejected: `sheets/` and `chr/` are drop-in for a pack's own layout (the kit contract, ADR-0183), and bending the pack to one program is the wrong way round.
- **Symlink `<part>-assets` at the kit's `<part>`.** Rejected: it needs Developer Mode or an elevated prompt on Windows, so the portable kit stops being portable exactly where the problem was.
- **Say it, in one line, where the artist reads it.** Chosen. `docs/remastering-a-game.md` and `ARTIST.md` both carry it.

ADR-0209's **Q3** ("how does the file come back") stays formally open; F12.4 settles only that its option (i) — *"whatever F12.4's template already decides"* — resolves to **(h), same path, explicit re-import**, because that is the mechanism F12.3 shipped and the stop rule is met on it. Option (g), a watcher that fires the reload without being asked, is neither adopted nor refused here; it is a follow-up slice with its own race (a half-written PNG), and picking it is a human's call.

### 5. Verification

- `scripts/test_asset_names.py`, wired into `make doc-checks`: every refusal above, and the live Contra kit's real names asserted **valid**, so the contract stays a guard against something rather than against everything.
- `scripts/test_artist_kit_assemble.py`: `assetName` is stamped, a comma stops the kit, a case-only clash inside one folder stops it and the same name in two folders does not.
- The slice's stop rule, from the PRD: saving in the paint program overwrites the kit PNG and F12.3 renders it — measured on the Contra and Zelda kits and logged in `docs/validation/`.

## Consequences

- **A generator can now fail where it used to write.** Any surface name that breaks the contract raises instead of landing on disk. Whoever adds the next surface kind (F12.9's static pages, F12.11's `.ora`) feels it first — the contract is what they must satisfy, and `require_asset_name` is one line.
- **`sanitize_asset_stem` can collapse two different inputs onto one name.** `stage 1` and `stage-1` both become `stage-1`; the folder rule of §1 does not catch it because the second write just overwrites the first. Nothing in the bounded library produces such a pair — a known hole, not a handled case — and the fix, if ever needed, is a collision check at the call site.
- **`assetName` is redundant with `path`** and could be derived by any reader; it is written anyway because the artist copies it by hand, and a page that makes them strip a folder prefix is a page that gets it wrong once.
- **Photoshop users copy one folder** — stated in two places rather than solved. If the F12.11 `.ora` path or the artist evidence later shows a Photoshop population large enough, a `kit-sync` helper is a small slice — but inventing it before anyone asked would be building for an imagined user, which ADR-0183 §3's discipline refuses.
