# Third-party notice — `scripts/no_intro_sha1.tsv.gz`

This notice travels with `scripts/no_intro_sha1.tsv.gz` (the SHA1 → console +
No-Intro name table), which is embedded into the app as
`Mesen.no_intro_sha1.tsv.gz` by `UI/UI.csproj` and read by
`UI/Logic/NoIntroNameTable.cs`. The decision it records is ADR-0266.

## The material

No-Intro DAT files, as mirrored by the **libretro-database** repository under
`metadat/no-intro/`.

- Repository: https://github.com/libretro/libretro-database
- Folder: `metadat/no-intro/`
- Fetched from:
  `https://raw.githubusercontent.com/libretro/libretro-database/master/metadat/no-intro/<DAT name>.dat`
- Upstream author of the data: No-Intro — https://no-intro.org/ (DAT-o-MATIC:
  https://datomatic.no-intro.org/)

## The licence

**Creative Commons Attribution-ShareAlike 4.0 International (CC BY-SA 4.0)** —
the licence the libretro-database repository carries in its root `LICENSE`:

- Licence text: https://github.com/libretro/libretro-database/blob/master/LICENSE
- Licence deed: https://creativecommons.org/licenses/by-sa/4.0/

The DAT files themselves declare no licence field in their clrmamepro headers,
and No-Intro publishes no licence text of its own, so the repository's licence
is the one recorded here. This is stated as read on 2026-10-07; see ADR-0266 for
the exact files and URLs it was verified from.

## What was changed

The table is **Adapted Material** under that licence. The changes are:

- Seven consoles were selected out of the repository's systems: `nes`, `gb`,
  `gbc`, `gba`, `sms`, `sg1000`, `gg`.
- Each row was reduced to three fields: the ROM payload SHA-1, the console code
  and the game name. Case and punctuation of the game names are untouched.
- The NES DAT's headered `.nes` rows were dropped in favour of their headerless
  `.unh` twins, because this app hashes the payload rather than the file
  (ADR-0003, ADR-0039). The remaining rows are emitted sorted by SHA-1 and
  serialized as a gzipped TSV.
- The result is offered under **CC BY-SA 4.0**, the same licence, not under this
  project's GPL-3.0.

## Disclaimer

The material is provided under CC BY-SA 4.0 **as-is and without warranties of
any kind**, as that licence's own disclaimer of warranties provides. Nothing in
this notice is a warranty about the accuracy of any game name or hash.

## What is not redistributed

The table holds only SHA-1 keys, console codes and game names. It contains **no
ROM data and no artwork**. No copyright in any game is exercised or implied by
this artifact.

## The table's own copy

The same information is carried inside the table itself, in its `#source` and
`#licence` header lines, so a copy that has travelled away from this repository
still carries its attribution. Regenerate the table with
`python3 scripts/generate_no_intro_sha1_table.py` rather than editing it by
hand.
