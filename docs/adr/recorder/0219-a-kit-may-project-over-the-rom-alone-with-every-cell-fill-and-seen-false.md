# ADR-0219: A kit may project over the ROM alone — every cell `fill`, `seen: false`, no play session — amending ADR-0183 §1 without making the ROM a second source of truth

- Status: accepted (2026-09-20; implemented in the same change as slice F12.9, tests `scripts/test_artist_chr_kit.py` and `scripts/test_mep_build.py`, measurement `docs/validation/slices/f12.9-static-kit-from-the-rom-2026-09-20.md`; go-ahead, verbatim: *"Aceitar e implementar agora (Recomendado)"*).
- Date: 2026-09-20
- Related: ADR-0183 §1/§2.4/§3/§4, ADR-0210 §2, ADR-0172 (the sheet sidecar's CHR index — the static page is index-keyed by construction), ADR-0209 Q4 (the editing-surface gap, and why "the shape exists" is not "the tile is reachable"), ADR-0165, ADR-0182, `docs/validation/measurements/metroid-artist-workflow-evidence.md`, PRD Part A F12.9, F12.10 path (d), F12.12
- Amends: ADR-0183 §1, by reference and in this file only; ADR-0183 stays `accepted`.

## Decision

### §1 as amended

The replacement for ADR-0183 §1's first sentence; the heading and the rest of the section stand as written.

> **§1 (amended).** A kit is a projection over a source of evidence. That source is a **recorded pack** whenever one exists — the normal case and unchanged: the artist kit is generated from an already-recorded pack, by scripts under `scripts/`, into a folder beside it (`kit/` by default) — never into the recording, and never by changing what the recorder captures at run time. When there is no recording, a kit **may instead project over the ROM's own CHR** (ADR-0210 §2), on a CHR ROM game and only there: every cell of every page is then `fill` and `seen: false`, the surfaces that need an observation (figures, scenery, stage maps — §2.1–§2.3) are not produced at all, and `ARTIST.md` says so in its first line. The recorded pack stays the evidence and the preferred source; a static kit never displaces, outranks or merges with a recorded one, and a cell a recording observed is never a `fill`. The ROM is not a second source of truth *about the game* — it is the only source of the 23 CHR ROM games' shapes, and ADR-0210 §2 already says so. A kit, recorded or static, can be regenerated, thrown away and regenerated differently without any recording being repeated.

Three load-bearing properties keep the amendment from being a hole in §1:

1. **Recording wins, always.** A static kit is the degenerate case of behaviour `artist_chr_kit.py` already has — it already fills unrecorded cells from the ROM under the name `fill` — not a parallel vocabulary. There is no code path where a static page outranks a recorded cell, because there is no cell to outrank.
2. **Nothing is claimed to be seen.** `seen: false` per cell, `fill` per cell, and `ARTIST.md`'s first line says no play happened; §3 is what makes the static kit admissible rather than what it bends.
3. **The recorder is untouched.** No new capture mode, no "dump CHR statically at save time" flag in `HdPackBuilder`, no change to what a run writes. The static path reads a `.nes` file; it does not make recordings cheaper, it makes them unnecessary *for this half*.

### The tool contract

- **Input.** `scripts/artist_chr_kit.py` gains `--static`, which accepts a missing or empty pack folder and derives every page from `--rom`. The ROM is the whole input; the pack folder is an output location only.
- **No play session, ever.** The tool never invokes `headless_record`, never reads a `.mss`, a route set, a `.bk2` or any replay, and never starts an emulator; the bounded input's test asserts this. This is a rule about the *tool*; F12.9's stop condition (3) uses a `headless_record` screenshot to check that a painted cell renders — an acceptance test drawing its own evidence, not the generator acquiring a play session.
- **No donor.** A static kit takes no `--also`. `--also` means "another recording of the same ROM as evidence for this pack's holes", and its precedence rule ("the pack named on the command line own cells always win") presupposes a primary with cells; with `--static` there is nothing to attach a donor to. Passing `--also` with `--static` is an error, not a silent no-op.
- **Refused: a CHR RAM ROM** (§2.4, ADR-0210 §2), with a pointer to F12.12. The refusal is *stronger* here: the existing CHR RAM recovery test — a contiguous PRG block pinned by tiles the bank *recorded* — has no recorded tiles to pin against, so the static path cannot recover a single CHR RAM tile by construction.
- **Refused: a third-party key index.** A static kit is the ROM alone; a community `hires.txt` is never read on this path (that is ADR-0210 §3 and F12.12, which needs the recording anyway). `<condition>` lines stay never imported.
- **Session-free by construction, not by convenience.** No `.mss` to mint, no route set to declare, no movie to match.

### What the amendment does not change

- §1's "never into the recording" and "never by changing what the recorder captures at run time".
- §3 (inference is marked, never confused with evidence) — the clause the static kit rests on.
- §5 (naming comes from the data, or from a human, never from a generator) — unchanged, and *thinner*: with no recording there are no ids or counts to build a caption from, so `names.json` is the only remaining source and a static kit invents no caption at all.
- §2's four surfaces, as a list. A static kit delivers §2.4 only, and the shortfall is stated rather than papered over.
- ADR-0183's status: it stays `accepted`; this file amends one sentence of it.

### Two readings fixed rather than edited

- **§2.4's "the existing `textures/chr/` pages"** means the CHR ROM's pages: with a recording they are the pages the recording already wrote, without one they are the pages the ROM's banks define. That is exactly what "page count equals CHR size / 4 KB" means.
- **§4's round-trip is not available verbatim** and must not be faked: §4 accepts a surface when a rebuilt pack loses and invents no `(tileData, palette)` key *against the recording it came from*, and a static kit has no recording to diff against, so "unchanged" is vacuous. The substitute is the half that survives: the rebuilt `hires.txt` carries exactly N = CHR tile count `<tile>` rules, all `Y` (nothing outside the ROM's own CHR is drawn; `Y` is the wildcard ADR-0210 relies on), `mep_build.py build` reports 0 errors, and `--verify` keeps printing counts.

### Acceptance (self-sufficient restatement)

Stdlib only, no `Core/` change. Bounded input: Super Mario Bros. (512 tiles, 2 banks) and Mega Man 3 (CHR ROM, the F12.5 game). Stop when:

1. page count equals CHR size / 4 KB and every cell is `fill`;
2. the pages-only folder passes `mep_build.py build` with 0 errors and the rebuilt `hires.txt` has exactly N = CHR tile count `<tile>` rules, all `Y`;
3. one cell painted on SMB's bank 0 renders pixel-exact in a `headless_record` screenshot of the title screen, by reopening the ROM (F12.3's reload is used when it has shipped, not a prerequisite);
4. wall time from ROM to `kit/` is under 10 s on the dev machine, recorded under `docs/validation/`.

## Context

The PRD's F12.9 row asks for a **static kit from the ROM alone (CHR ROM games)**: `artist_chr_kit.py` already builds rank-0 pages, `fill` cells, `seen: false`, the ADR-0172 sidecar and the CHR RAM refusal, and the recorder already emits every CHR ROM tile with `Y` (`HdPackBuilder::AddRomTiles`); what did not exist was running any of it **without a play session** — the positional argument is a recorded pack and `Pack.__init__` refuses a folder with no `textures/hires.txt`. The slice adds `--static`, has `mep_build.py build` accept a pack folder holding only `chr/` pages, their sidecars and `chr/fill-rules.hires.txt`, and writes an `ARTIST.md` whose first line says nothing on these pages was seen in play. The row offered two homes for the §1 amendment; ADR-0210 was accepted 2026-09-20 without it, so this is the successor.

The artist evidence puts the bottleneck on the recording, not the painting, and ADR-0210 splits the 30-ROM bounded library cleanly: 23 games are CHR ROM, where every shape is in the file and `HdPackBuilder::AddRomTiles` already emits each with `defaultTile = Y`, so a page that needs no play is available today and costs a flag, not a subsystem. What a static kit cannot give is organisation: figures, named scenery and stage maps come from observed OAM co-occurrence, adjacency and scroll (ADR-0164, ADR-0182). That is the whole difference, and why this amends one sentence of §1 rather than adding a new kind of artefact.

Non-goals: how a completed page reaches the artist's editor (ADR-0209 Q4), a `.ora` layer contract (F12.11's own ADR), a third-party key index (ADR-0210 §3, F12.12), and any `Core/` change — no recorder change, no format change, no new build step.

## Consequences

- **F12.9 unblocks, and F12.10's path (d) with it.** `record_library.sh`'s route resolution already falls through to `static` for a ROM matching no route set; today that path produces nothing, after this it produces the one kit a route set was never needed for.
- **The ROM-SHA-1 pin is disabled by construction, and that is the cost to name.** `artist_chr_kit.py` refuses when the pack's `<supportedRom>` SHA-1 differs from the ROM (ADR-0003/ADR-0039) because "the fill is the one place a wrong input produces confident, plausible, wrong art"; with no recording to pin against, the user's choice of `--rom` is the whole input. The substitute: the kit manifest records the ROM's SHA-1 and every cell says `seen: false` — the art is the named ROM's, never claimed to be the game's.
- **The palette side needs nothing.** `defaultTile = Y` is a per-rule palette wildcard, so a shape lifted out of CHR matches whatever colours the game puts it under. "all rules `Y`" is the mechanism, not a shortcut.
- **A static kit is a `kit/` folder and not a pack** — not written under `EnhancementPacks/`, not auto-installed, not a bootstrap pack; the pages-only folder `mep_build.py build` accepts is a build input. The "`--out must not be inside the recorded pack`" guard becomes a rule about the *artifact* class.
- **It is a floor, not a speedup.** "faster on day one" moves from "a play session" to "seconds", and says nothing about whether the page is useful: an artist still has no figure, no scenery and no stage map.
- **A static page and a later recording are not in competition:** the recording's keys carry the palettes ADR-0210 calls scarce, the static page's `Y` rules cover everything else.

## Alternatives

- **A separate ROM-only generator** (a fifth script, never constructing a `Pack`) — refused, not on size grounds: the conflict is about what a **kit** may project over, not which file projects, so the same §1 amendment would still be needed, or the artifact stops being a kit. Once it isn't a kit, every consumer pays for the second name (F12.11 takes "one SMB static page from F12.9" as input; F12.12's `index` sheet sits beside it), and the 1 800 lines of page/bank/fold/sidecar/`--verify` machinery would be duplicated and drift. Reading it as "ROM pages" or "not a kit" fails the same way — same folder, `ARTIST.md`, round-trip and consumers. §1's heading — "a kit is a projection, never a second source of truth" — is already the property the static case preserves.
- **Record once per ROM, automatically** (F12.10's shape) — refused as a substitute: it turns a seconds-long projection into an emulator run per game, exactly the bottleneck the artist evidence names. F12.10 is the complement, and its own path (d) now depends on this.
- **Extend to CHR RAM now** — refused, per §2.4 and ADR-0210 §2: with no recorded tiles there is no PRG block to pin, so a static CHR RAM page would be invented rather than read. ADR-0210 §3's third-party index is the only static source of shape for those 7 games, and it needs a recording.

## Contradiction (recorded, unresolved elsewhere)

ADR-0210 §2's last line — "The residue is the palette: a shape pulled straight from CHR has no colours attached. Source 3 supplies them." — would make source 2 *not* self-sufficient, implying a static kit needs a third-party index; ADR-0210's own later section retracts exactly that ("So source 2 is **already self-sufficient**"), as does its Date line ("the 'what stays open' claim is retracted"). This ADR reads §2 as the corrected section reads it and does not amend ADR-0210; worth folding into ADR-0210's §2 the next time that file is touched. No other contradiction found.
