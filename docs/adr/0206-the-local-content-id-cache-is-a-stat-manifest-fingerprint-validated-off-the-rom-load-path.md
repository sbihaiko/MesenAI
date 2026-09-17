# ADR-0206: The local-container `content_id` cache is a stat-manifest fingerprint, read unvalidated at ROM load and revalidated on a background refresh

- Status: accepted (2026-09-17, at the user's direction: "Aceitar e
  implementar"), and implemented the same day as the P.1-local slice:
  `MepLocalIdentityCache` (fingerprint, cache file, refresh),
  `MepPackManager::ReadInstallIdentity`/`AdoptEqualContentIds`, the
  `RefreshMepLocalIdentities` export, the post-`GameLoaded` background call and
  the `MepContentId::IsHostControlFile`/`IsExcludedPath` split. Covered by the
  Bloco G cases in `scripts/core_unit_tests.cpp` and end-to-end by
  `scripts/p1_local_identity_check.py`.
- Date: 2026-09-17
- Related: ADR-0139 (`content_id` algorithm — unchanged by this ADR), ADR-0140 §rule 4 and ADR-0141 (`local:<container>` fallback, one-slot, preference key), ADR-0147 (`ComputeFolder` as an edit baseline), ADR-0138 §4/§46 (`.cache/` layout), ADR-0120 §4 (`MepZipExtract`'s own `.mep-source` fingerprint — the precedent this follows), ADR-0146 (the client work the `GameLoaded` hook already carries), ADR-0123 (host-free `UI/Logic` firewall), ADR-0137 (per-file line ceilings); PRD Part B §3.3 rule 4, §5, §8 (the slice), §10 (the stall risk), and Part A §3's P.1-local record

## Context

P.1-local is the last open slice of Part B. Today `MepPackManager::ReadInstallIdentity`
only reads `.mep-install.json`; a container the user dropped by hand has no
stamp, so it carries an empty identity, `EffectivePackId` returns the
ADR-0140 rule-4 fallback `local:<container>`, and the §5 content merge in
`PackPreferenceResolver` — which keys off `content_id` — can never fire for it.
A byte-identical copy of a catalog pack therefore stays a second, competing
choice in the picker, and a stored per-ROM preference cannot match it.

PRD Part B §3.3 rule 4 fixes the shape of the fix: compute the ADR-0139 tree
`content_id` **once**, cache it under `EnhancementPacks/.cache/`, keyed by the
container, and **never on the synchronous ROM-load path** — HD trees run to
hundreds of MB and hashing them at every boot is not acceptable (§10 records
the stall as the slice's risk). §8 adds the constraint this ADR exists to
settle: the key must detect a change to a **nested** file, so the container's
own `mtime` is not sufficient, and "settle any new cache trade-off in an ADR".

What already exists, and is reused rather than rebuilt:

- `MepContentId::ComputeFolder` (ADR-0139/ADR-0147) — the tree hash, with the
  host's own `.mep-install.json`/`.bootstrap` excluded so a reinstall is not an
  edit. It reads every byte, so it is the expensive half.
- `MepZipExtract`'s `.mep-source` stamp — a two-line `mtime+size` fingerprint
  written next to an extracted zip so the extraction is skipped when the zip
  did not change (`Core/Shared/EnhancementPacks/MepZipExtract.h`).
- `EnhancementPacks/.cache/`, already created and already skipped by the pack
  scan (`MepPackManager::CacheFolderName`, `ScanAndMatch`'s `.cache` continue).
- The install registry under `.cache/installs/<romsha1>.json`, which stores a
  `BaselineContentId` per install (`CommunityPackInstallRegistry`).

Non-goals, kept out of this decision: changing the ADR-0139 algorithm or its
Python/C++ parity fixture (this cache is a client-side accelerator, not part
of the normative id); product-level deduplication for packs without an `id`
hosted outside GitHub (PRD §7); re-associating a recipe *output* folder copied
without its stamp (PRD §7).

## Decision

**1. One cache file, one read.** The cache is a single JSON document at
`EnhancementPacks/.cache/content-ids.json` — not one file per container, so a
ROM load costs one read and one parse. It is written atomically (temp file +
rename) and holds one entry per container:

```json
{"version": 1, "containers": [
  {"path": "<absolute container root>", "container_stamp": "<hex>",
   "tree_fingerprint": "<hex>", "content_id": "<hex>"}
]}
```

`path` is matched case-insensitively on Windows and case-sensitively
elsewhere, mirroring the container-name comparison the identity map already
uses.

**2. Two keys, two jobs.**
- `container_stamp` = SHA-256 of the container root's own `mtime` and its
  entry count — one `stat` plus a shallow listing, the shape `MepZipExtract`'s
  zip stamp already uses (a directory has no meaningful `size`, so the count
  stands in for it). It answers "is this still the same container?" (folder
  replaced, deleted, zip re-extracted).
- `tree_fingerprint` = SHA-256 over a canonical **stat manifest** of the
  tree: one line per file, `relpath \t size \t mtime`, paths relative to the
  container root with `/` separators, sorted byte-wise, excluding exactly
  what `ComputeFolder` excludes (`__MACOSX`/`screenshots` segments,
  `.DS_Store`, `README*`, `.mep-install.json`, `.bootstrap`). It walks and
  stats, and never reads a file's bytes.

The `tree_fingerprint` is what makes a nested change visible: editing
`textures/foo.png` changes that file's `size`/`mtime` line even though the
container directory's own `mtime` is untouched.

**Accepted limit, stated rather than hidden:** this is a fingerprint, not a
content hash, so it sees what `size` and `mtime` can see and nothing more. A
rewrite that keeps the byte count **and** leaves the recorded time unchanged is
invisible to it. Two ways that happens:

- the `mtime` is deliberately restored (`touch -r`) after an edit;
- the write lands inside the same `mtime` tick as the one the cache recorded.
  That tick is filesystem *and* stdlib dependent — nanosecond on APFS, coarser
  on some libstdc++ builds, which is how this shipped test caught it: two
  writes in the same test failed to move `last_write_time` on the CI runner
  while passing on macOS.

This is git's own rule (`size` + `mtime`, with `git status` occasionally
needing a `touch` for the same reason), and the escapes are the ones that rule
has always had: delete `content-ids.json`, touch the tree, or run the
`Restore`/reinstall path, and the identity is recomputed from scratch. Reading
every byte at refresh time instead would remove the limit and also the point of
the cache — hundreds of MB per pack per session.

The consequences of a miss are bounded rather than silent-corrupting: the
container keeps its previous identity, which at worst means a drop stays merged
with a catalog twin it no longer byte-matches (or stays its own `local:` entry)
until one of those escapes runs. Nothing is applied that the pack did not ask
for; the ADR-0139 hash is still what the identity *means*.

**3. Load reads the cache without revalidating it. Refresh revalidates it.**

- **Synchronous ROM load** (`MepPackManager::LoadForRom` →
  `ReadInstallIdentity`): when a container has no `.mep-install.json` stamp,
  the entry is looked up by `path` and accepted when its `container_stamp`
  equals the current one — a single `stat`, no walk, no bytes. Hit → the
  entry's `content_id` (and the `pack_id` adopted in §4) is what the pack
  reports. Miss, stale stamp, or unreadable cache → today's behavior,
  `local:<container>`.
- **Background refresh** (off the load path): for each local container, compute
  the `tree_fingerprint` and compare it with the entry. Equal → nothing.
  Different or absent → run `MepContentId::ComputeFolder`, rewrite the entry,
  and record `updated_at`. Entries whose container no longer exists are pruned.
  The refresh is what pays the byte-reading cost, and it runs in the client, on
  a background thread, triggered after `GameLoaded` next to the existing
  `CommunityPackInstallService.OnGameLoaded` — never inside `LoadForRom`.

The refresh walks the packs folder itself and reads no manager state, so it
shares nothing with the emulation thread and needs no lock; it writes the file
and nothing else, and the next load picks the result up. That is deliberate —
the emulation thread's structures stay untouched while a game is running, and
a container that never matched a ROM still gets its identity the first time the
refresh sees it.

**4. A stamp-less container adopts the catalog `pack_id` when the trees are
equal.** At the end of `LoadForRom`, over the discovered packs only (a string
comparison, no I/O): entries sharing a non-empty `content_id` share the
`pack_id` — the one a stamped sibling in that group carries, else the ADR-0140
`local:<container>` fallback for the group's first container. This is what
makes PRD §5's merge real: a hand-dropped copy of a catalog pack reports the
catalog `pack_id`, so the stored per-ROM preference matches it and the picker
collapses the pair into one choice. `PackPreferenceResolver` needs no change —
it already merges on `content_id` and already mirrors the `local:` fallback.

**5. The window is one load cycle, and that is the contract.** A pack edited
between two loads is still identified by the previous value during the load
that precedes its refresh; the refresh makes it distinct from then on. This
is the same "the catalog merge happens on the next load" wording PRD §3.3
rule 4 already uses for a cold cache, and it is why the refresh writes to the
file instead of forcing a re-evaluation mid-session.

**6. Scope and interop.** The walk is `MepLocalIdentityCache::RefreshFolder`,
which takes the packs folder as a parameter — the production caller passes
`MepPackManager::GetPacksFolder()` (`RefreshMepLocalIdentities` in
`InteropDLL/EmuApiWrapperMep.cpp`, whose sibling `EmuApiWrapper.cpp` is already
at its 200-line guardrail, exposed as `EmuApi.RefreshMepLocalIdentities()`) and
the unit tests pass a temporary tree, so no test ever writes into the user's own
`EnhancementPacks/`. The C# side calls it in a background task after
`GameLoaded` and re-reads `GetMepPackList` when it reports that something was
recomputed; `scripts/p1_local_identity_check.py` drives the same export against
the built library for end-to-end evidence.

## Consequences

- P.1-local's acceptance rows become testable: identical stamp-less folders
  and zips collapse; a matching catalog tree adopts its `pack_id`; an edited
  nested payload invalidates the cache and stays distinct; the first load
  stays responsive with a cold cache (it does no hashing at all).
- `scripts/core_unit_tests.cpp` is the natural home for the fingerprint /
  cache / adoption tests, and it sits ~216 lines under its ADR-0137 ceiling:
  the new block must stay small, or the ceiling gets an explicit amendment
  rather than a silent bump.
- The normative hasher does not move: `scripts/mep_content_id.py`,
  `MepContentId.cpp`'s `ComputeTree` and `docs/specs/golden/mep-content-id.json`
  are untouched, because nothing here changes what a `content_id` *is*.
- The cache is derived data. It must be safe to delete at any time — deleting
  it degrades to `local:<container>` until the next refresh, never to a wrong
  merge (the entry is only trusted when the container stamp matches).
- Two containers in different homes with equal trees still merge, because the
  key is the tree, not the path — the path is only where the value is stored.
- The refresh adds one stat-walk per local container per session (background),
  and one `stat` per local container per load (synchronous). Neither reads the
  packs' bytes; `ComputeFolder` runs only where the fingerprint moved.
- A zip container is fingerprinted on its **extracted** tree under
  `EnhancementPacks/.cache/<container>/`, not on the zip bytes — so a
  re-download of the same tree keeps the identity, matching ADR-0139's
  wrapper-independence rule.
