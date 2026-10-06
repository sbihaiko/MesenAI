# ADR-0202: The release artifacts are named after the product, not after the tag

- Status: accepted (2026-09-16, at the user's direction: "quero, pode fazer", given after this session reported that the release title reads `MesenAI v0.1.0` while its attached files read `MesenCE-v0.1.0-macos-arm64.zip` and `mesence-tools-v0.1.0.zip`, and named the cost of changing them; implemented in the same change, whose test is `scripts/checks/verify_release_asset_names.sh`)
- Date: 2026-09-16
- Related: ADR-0201 §4 (the list of things deliberately left saying MesenCE), `docs/releases/mesence-v0.1.0.md` (the release body), `scripts/release_macos.sh`
- Supersedes / amends: amends ADR-0201 §4 — it removes the **published assets** from that ADR's "deliberately not renamed" list, and leaves the rest of the list standing.

## Context

ADR-0201 renamed the runtime surface but kept the assets' old names, for a real
reason: renaming an asset changes its URL and invalidates the `SHA256SUMS`,
which carries the file names. That leaves one visible contradiction: the release
title `MesenAI v0.1.0` sits above a table offering
`MesenCE-v0.1.0-macos-arm64.zip` and `mesence-tools-v0.1.0.zip` — the same
contradiction ADR-0201 set out to end, one layer out.

Two checked facts make the fix cheap:

- **Nothing versioned downloads these files by URL.** A search for
  `releases/download` across the tree returns no project file (only test
  fixtures and a vendored virtualenv); `README.md` points at the Releases page,
  not an asset URL.
- **The bytes do not change.** A rename re-uploads the zips with new names and
  the same SHA-256; only `SHA256SUMS` changes content, since it lists names
  beside hashes.

Non-goals: no rename of the tag (`mesence-v0.1.0`), the release URL, or the
repository; no change to either zip's contents.

## Decision

1. **Artifact names come from the product name, not the tag prefix.**
   `scripts/release_macos.sh` produces:

   ```
   out/release/MesenAI-<version>-macos-arm64.zip
   out/release/mesenai-tools-<version>.zip
   out/release/SHA256SUMS
   ```

   The tag scheme stays `mesence-vMAJOR.MINOR.PATCH`, so artifact prefix and tag
   no longer match — intended: the tag is an already-published, linked URL; the
   file name is what a reader sees on the download page.
2. **The staging directories follow**, because `ditto --keepParent` writes the
   staged folder's name into the zip (the first thing a user sees after
   unpacking):

   ```
   out/stage/MesenAI-<version>-macos-arm64/
   out/stage/mesenai-tools-<version>/
   ```

3. **The published `mesence-v0.1.0` release is corrected in place.** The two
   zips are re-uploaded under the new names, the old ones removed, `SHA256SUMS`
   regenerated from the new names; tag and release URL untouched, so every link
   to the release page keeps working.
4. **The SHA-256 values are asserted unchanged.** The rename is a rename only if
   the hashes are identical before and after (`SHA256SUMS` is the record); a
   hash that moves means a rebuild, not a rename, and the release body's
   reproducibility claim would need revisiting. Measured on the published
   release: the two zips hash `76172fa0…` and `ec1edace…` under both names, and
   both new URLs answer `200`.
5. **The docs that name the artifacts say the new names**: `README.md`,
   `docs/releases/macos-zip-README.md`, `docs/releases/tools-zip-README.md` and
   the release body `docs/releases/mesence-v0.1.0.md` (its *file name* follows
   the tag, so it stays).

## Consequences

- **The download page stops contradicting itself**: title, table and file names
  all say MesenAI.
- **The old names' direct asset URLs stop resolving.** Nothing in the repo used
  them and `README.md` links to the release page, so the breakage is limited to
  a URL someone may have bookmarked in the release's first hours.
- **`SHA256SUMS` must be regenerated in the same operation**, not after: it is
  generated from the zip basenames, so a stale copy would name two files that no
  longer exist while omitting the two that do.
- **The tag and the artifact prefix now differ on purpose** — the kind of thing
  a later reader "fixes". `scripts/checks/verify_release_asset_names.sh` is the
  answer: it fails if a `MesenCE-`/`mesence-tools-` name returns to the four
  definitions in `release_macos.sh` or the four documents above, and asserts
  `SHA256SUMS` is still derived from the zip basenames.
- **The next release is renamed by construction**, since the script is the only
  thing that names these files; the correction to the existing release was a
  one-off, recorded here rather than in a script.
