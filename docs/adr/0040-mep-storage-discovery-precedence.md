# ADR-0040: MEP storage folder, zip handling and deterministic precedence

- Status: accepted (reflected in the code; revised by ADR-0049, extended by ADR-0120/ADR-0121/ADR-0147 — see "Amended by")
- Date: 2026-08-24
- Phase: F3.0 (MEP v1 host)
- Refines ADR-0005 (loose HD pack wins) and the storage note in
  `docs/roadmap/PRD-mesence-enhancement-ecosystem.md` (memory: "central
  folder per ROM, accept zip and directory").
- Amended by: ADR-0049 (the sibling folder revises the storage root), ADR-0120 (zip subfolder fallback), ADR-0121 (legacy loose `hires.txt` as a fallback discovery signal), ADR-0147 (`auto/` and `mep/` sibling sub-folders)

## Context
MEP-v1 §2 requires hosts to accept a `.zip` and a directory with identical
semantics; §5.1 requires a deterministic order between multiple matching packs
and states that a loose `HdPacks/<rom>/` pack overrides any MEP `textures`
section. The loaders differ: NES `HdPackLoader::LoadHdNesPack(string)` and
`HdTilePack::LoadFromFolder` both take a *directory*; only the NES
`HdPacks/<rom>.zip` path reads zips directly.

## Decision
1. **Location.** `<home>/EnhancementPacks/`. Each pack is a sub-directory
   `<name>/pack.json` or a loose `<name>.zip`. Nothing else is scanned (no
   recursion, no per-ROM sub-folders — packs are located by hash, not ROM
   name). *Revised by ADR-0049 / MEP-v1 §2.1 rule 5:* named containers
   (`<Game>/` or `<Game>.zip`, matched by the ROM file name without extension)
   are also accepted alongside hash-matched `pack.json` containers; ADR-0120
   adds a last-resort subfolder fallback for wrapped zips.
2. **Zips are extracted, not streamed.** On scan, `<name>.zip` is extracted to
   `<home>/EnhancementPacks/.cache/<name>/`, re-extracted when the zip's size
   or mtime in `.cache/<name>/.mep-source` differs (ADR-0120 §2, `PrepareZip`).
   A zip pack and a directory pack are then the *same* thing for every
   consumer — one code path; the NES/GB/SMS texture loaders keep directory-only
   signatures. Entries are validated before extraction (spec §6): a normalised
   path containing `..`, starting with `/` or `\`, or carrying a drive prefix
   aborts the whole pack ("zip-slip"); directory entries are created,
   everything else written verbatim.
3. **Matching.** A container is a candidate when its `pack.json` parses and
   validates (MepPack) and any `targets[].sha1` equals the ROM's No-Intro
   SHA-1 (ADR-0039), case-insensitive.
4. **Precedence.** Candidates are ordered by container name (directory / zip
   base name), case-insensitive lexicographic, on the byte values of the
   lower-cased UTF-8 name. For each section the **first** pack in that order
   providing it wins; the others stay in the list (for the F3.3 UI) but are not
   applied. Users force priority with a name prefix (`00-…`). This differs from
   the spec's *reference* suggestion ("installation order, newest wins"): mtime
   is not reproducible across machines/backups and cannot be shown in the UI
   without confusing users. MEP-v1 §5.1's parenthetical is only the spec's
   example; the MesenCE order is the lexicographic one here, and this rule
   stands.
5. **Loose HD pack wins** (ADR-0005, MEP-v1 §5.1): when `HdPacks/<rom>/hires.txt`
   (or `HdPacks/<rom>.zip`) exists, the MEP `textures` section is skipped with a
   log line saying so. *Exception (ADR-0049, MEP-v1 §5.1):* a `textures` section
   from the ROM's sibling folder prevails over both the loose HD pack and
   central-storage containers.

## Consequences
- Extraction costs disk (a copy of every zip) and a one-off delay on first
  scan; no loader grows a second I/O backend and PNG/OGG files are served by
  the same `ifstream` paths already validated in F2.
- Deleting `EnhancementPacks/.cache/` is always safe (rebuilt on next scan).
- Precedence is stable and inspectable by looking at the folder.

## Alternatives
Rejected: streaming from zip via ZipReader in every section loader (three
loaders to teach, NES/HdTilePack/OggMixer, plus MSU-1 later); "Newest wins" by
mtime (non-deterministic across copies); per-ROM sub-folders like HdPacks/
(contradicts hash-based identity).

## Amendments (2026-09-06, code-review pass)
- `.mep-source` (§2) is now two lines: `<size>:<mtime>` followed by the root
  prefix resolved at extraction time (`""` for a pack whose `pack.json` sits at
  the archive root). A one-line stamp is still accepted as prefix `""`. This
  stops hires.txt-only zips (ADR-0049, ADR-0120) from being wiped and
  re-extracted on every ROM load, and makes a cache hit return the same folder
  the first extraction did.
- Decompression caps (1 GiB per entry, 2 GiB total) apply before any byte is
  written; see ADR-0006 amendments for the numbers and their location.
