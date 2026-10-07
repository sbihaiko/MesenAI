# ADR-0266: The embedded SHA1 -> No-Intro name table is CC BY-SA material and ships its own attribution

- Status: accepted 2026-10-07 — the owner asked for the table and for its
  publication, verbatim: **"sim, inclui e publica"** (2026-10-07). It is
  reflected in the code and in the artifact this ADR describes:
  `scripts/generate_no_intro_sha1_table.py` builds
  `scripts/no_intro_sha1.tsv.gz`, `UI/UI.csproj` embeds it as
  `Mesen.no_intro_sha1.tsv.gz`, `UI/Logic/NoIntroNameTable.cs` reads it, and
  `scripts/no_intro_sha1.NOTICE.md` is the notice that travels with it. Pinned
  by `scripts/test_generate_no_intro_sha1_table.py` (+ `make doc-checks`) and
  `UI.Tests/Play/NoIntroNameTableTests.cs`.
- Date: 2026-10-07
- Related: ADR-0003 and ADR-0039 (the payload byte-range contract every key in
  the table follows), ADR-0138 §41 (the host allow-list `raw.githubusercontent.com`
  is already on, which is how the DATs are fetched), issue #1038 (this slice)
  and #1030 (the parent spec). **Named by issue, not by id:** the library ADR's
  own record of this table lives on the `#1031` branch, which is not in this
  tree yet, and `scripts/checks/verify_adr_refs.py` fails on a cited id that has
  no file — so this ADR stands on its own rather than pointing at it.
- Supersedes / amends: nothing. This is the first record of this artifact's
  licensing.

## Context

`scripts/no_intro_sha1.tsv.gz` is a gzipped TSV of about 17 900 rows across 7
consoles, keyed by the SHA-1 of a ROM payload, and `UI/UI.csproj` embeds it in
the binary so the flat game library can show a player the database's own name
for a ROM instead of a file name. It is not written by hand and it is not our
data: it is extracted
from the No-Intro DATs that the libretro-database repository mirrors. That makes
it **third-party material redistributed inside our app**, and the question the
code alone cannot answer is what licence it carries and what that licence asks
of us.

Four things had to be established from the sources, not assumed:

1. **Where the bytes come from.** `libretro-database` at `metadat/no-intro/`,
   one DAT per console, fetched over HTTPS from `raw.githubusercontent.com`.
2. **What licence they carry upstream.** The script header previously claimed
   No-Intro "publishes for free redistribution" with no source. That claim is
   withdrawn here; the licence below was read, not guessed.
3. **What the table is, in licence terms.** It is not a copy: seven of the
   repository's systems were selected, each row was reduced to
   `(payload sha1, console code, game name)`, the NES DAT's headered `.nes` rows
   were dropped, and the result was re-serialized and gzipped. That is
   **Adapted Material** in the licence's own words (see below), which switches
   on conditions a verbatim copy would not trigger.
4. **Where the attribution lands.** The table is embedded in the binary, so
   attribution that exists only in a script docstring does not travel with a
   released copy.

### What was read, and what it says

- **`LICENSE`** — `https://raw.githubusercontent.com/libretro/libretro-database/blob/master/LICENSE`
  (file read on 2026-10-07). Its first line is `Attribution-ShareAlike 4.0
  International` and the body is the full legal text of **CC BY-SA 4.0**. The
  repository has exactly one licence file, at its root, with no per-folder
  carve-out.
- **`README.md`** — `https://raw.githubusercontent.com/libretro/libretro-database/blob/master/README.md`
  (read on 2026-10-07). It documents `metadat` as holding "Several principal
  third-party DATs (e.g. No-Intro, Redump, MAME, TOSEC)", states that
  `metadat/no-intro` is a "Bulk import from upstream No-Intro databases", and
  its Sources table attributes that folder to No-Intro
  (`http://datomatic.no-intro.org`). The README has **no licence section** and
  names no different licence for `metadat/` than the root `LICENSE`.
- **The DAT itself** — the head of
  `https://raw.githubusercontent.com/libretro/libretro-database/blob/master/metadat/no-intro/Nintendo%20-%20Nintendo%20Entertainment%20System.dat`
  (read on 2026-10-07) is
  `clrmamepro ( name "Nintendo - Nintendo Entertainment System" description "…" version "2026.08.01" homepage "http://github.com/robloach/libretro-dats" )`.
  There is **no `license` field**, and the header fields the README documents
  (`name`, `description`, `comment`) carry metadata, not licensing. So the DAT
  files themselves carry no per-file licence notice — the repository's own
  `LICENSE` is what governs the copies we read.
- **No-Intro's own sites** — `https://no-intro.org/` and
  `https://datomatic.no-intro.org/index.php?page=download` (read on 2026-10-07).
  Neither states licence terms for the DATs; `no-intro.org` says only that it
  catalogs dumps and does not help anyone obtain them. **No-Intro publishes no
  licence text to cite**, which is the honest reason the previous
  "free redistribution" claim is gone rather than reworded.

### What CC BY-SA 4.0 asks of a derived table

Both conditions are switched on the moment the material is shared in adapted
form. Quoting the `LICENSE` read above:

- **Attribution (§3(a)(1)).** You must retain, when supplied, the creator's
  identification, a copyright notice, a notice referring to this Public
  License, a notice referring to the disclaimer of warranties, and a URI or
  hyperlink to the material; **indicate if You modified the material** and
  retain any indication of previous modifications; and indicate that the
  material is licensed under this Public License, including the text of, or a
  URI or hyperlink to, it. §3(a)(2) allows this to be satisfied "in any
  reasonable manner based on the medium, means, and context" — for a data file
  embedded in a binary, that means the notices belong **in the file**.
- **ShareAlike (§3(b)).** The licence You apply to the Adapted Material You
  produce must be "a Creative Commons license with the same License Elements,
  this version or later, or a BY-SA Compatible License", and must include the
  text of, or the URI or hyperlink to, that licence.
- **What the table is, in those terms (§1(a)).** "Adapted Material means
  material subject to Copyright and Similar Rights that is derived from or
  based upon the Licensed Material and in which the Licensed Material is
  translated, altered, arranged, transformed, or otherwise modified". Selecting
  seven systems, stripping each row to three fields and re-serializing is
  exactly that.

## Decision

**The table is distributed under CC BY-SA 4.0, carries its own attribution in
its embedded header, and ships a notice beside it; it is never presented as
first-party data or relicensed.**

1. **Source, stated as a URL a reader can follow.** The DATs are the No-Intro
   files mirrored by `libretro-database` under `metadat/no-intro/`, fetched from
   `https://raw.githubusercontent.com/libretro/libretro-database/master/metadat/no-intro/<DAT name>.dat`
   over HTTPS (`DAT_BASE_URL` in
   `scripts/generate_no_intro_sha1_table.py`). The host is already on
   `scripts/pack_host_allowlist.json` (ADR-0138 §41), so this adds no host.

2. **The licence recorded is the one that was read.** CC BY-SA 4.0, from
   `libretro-database`'s root `LICENSE`
   (`https://github.com/libretro/libretro-database/blob/master/LICENSE`), which
   is the only licence file the repository carries. The DATs declare no licence
   of their own, and No-Intro publishes none to cite. The repository's licence
   is therefore the operative one for the bytes we mirror, and the previous
   unsourced "No-Intro publishes for free redistribution" sentence is removed
   from both the script header and the table.

3. **The table is shared under the same licence, not relicensed.** It is
   Adapted Material, so §3(b)(1) is met by offering the adapted table under
   **CC BY-SA 4.0** — the same licence, its own version — rather than under this
   project's GPL-3.0. The app's licence does not reach the table's contents;
   the table's header says which licence does.

4. **Attribution travels inside the payload, not only in the repository.**
   §3(a)(2) is what makes this the right medium: the `#source` and `#licence`
   lines are part of the gzipped file, and the gzipped file is the embedded
   resource, so **every copy of the table in a released binary already carries**
   the identification of the source, the licence name, the URI of the licence
   and the URI of the material. A reader with only the binary has everything
   §3(a)(1) requires. The `#licence` line also names this ADR and the notice
   file below, so the human-readable record is one step away.

5. **Modification is indicated, and the notice says how (§3(a)(1)(B)).** The
   `#licence` and `#hash` lines state what the table is and what was done to the
   data — seven consoles selected, rows reduced to payload SHA-1 + console code +
   game name, the NES DAT's headered `.nes` rows dropped in favour of their
   headerless `.unh` twins (ADR-0003, ADR-0039). Nothing is silent: the reader
   of the derived file can tell it is derived.

6. **A notice file ships next to the table.**
   `scripts/no_intro_sha1.NOTICE.md` is committed beside
   `scripts/no_intro_sha1.tsv.gz` and is named by the table's `#licence` line.
   It was added rather than extended because **the repository had no
   third-party notice file to extend** (searched 2026-10-07: the only
   `ThirdParty` path is `UI/ThirdParty/`, which is vendored DataBox *source*,
   not a notice, and the librashader MPL-2.0/GPL-3.0 texts live on the release
   mirror, not in the tree). It records the source, the licence, the licence
   URI, what was changed, the disclaimer and the fact that no ROM bytes and no
   artwork are redistributed.

7. **The shipped app's credits list names the source too.**
   `AboutInfo.Libraries()` (`UI/Windows/AboutWindow.axaml.cs`) is the app's
   existing third-party attribution surface — an in-app list of name, author,
   licence and URL, rendered by both the classic About window and the Play GUI's
   About sheet, and already carrying a comparable non-code entry (`LED Icons`,
   CC BY 4.0). It gains one row: `No-Intro DATs via libretro-database`,
   `No-Intro`, `CC BY-SA 4.0`, pointing at the repository. A player who never
   opens the repository still sees where the names come from.

8. **What is *not* redistributed, stated so nobody has to infer it.** The table
   holds 40-hex keys, seven console codes and game names. It holds **no ROM
   bytes and no artwork** — nothing a rights holder other than the database
   authors has a claim on. That is why this ADR is about database attribution
   and not about ROM distribution, and it is the boundary the generator must
   never cross.

9. **The license question is answered by the artifact, not by a reviewer.**
   Anyone auditing a release can read the `#licence` line out of the embedded
   resource, follow its two URIs, and land on the same `LICENSE` this ADR was
   written from. Regenerating the table preserves all of it: the header lines
   are built by `build_table`, so they cannot drift from the artifact the way a
   hand-maintained README could.

## Consequences

- The repository now carries a second licence for one file. Anyone reading
  `LICENSE` (GPL-3.0) alone would be wrong about `scripts/no_intro_sha1.tsv.gz`;
  the table's own header and this ADR are what correct that, which is precisely
  why the header carries the licence rather than pointing at a document.
- The table inherits CC BY-SA 4.0's property that **derivatives stay open**: a
  fork that regenerates the table from the same DATs and ships it must keep the
  attribution and the same licence. That is a constraint on the artifact only —
  the generator script is ours and is unaffected.
- The `#source` and `#licence` lines are now part of a **byte-pinned artifact**:
  `test_build_table_output_is_pinned` fails if they change without the pin being
  updated, and `--check` fails if the committed file is stale. Editing a licence
  string is therefore a deliberate, visible change rather than a silent one.
- The licence text is not shipped as a file in the app bundle. §3(a)(2) is
  satisfied by the URI in the embedded header and by the in-app credits row, not
  by bundling a copy of CC BY-SA 4.0's legal text. If a future release bundles
  licence texts for its other dependencies, this table's should be added there
  and this ADR amended.
- No-Intro's own terms remain **unstated upstream**. Should No-Intro publish a
  licence that differs from the repository's, or should `libretro-database`
  change its root `LICENSE`, both are live external contracts: the table's
  source URL and its licence line are the two places that would have to change
  together, and `--check` is what makes the change deliberate.
