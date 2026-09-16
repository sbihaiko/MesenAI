# ADR-0202: The release artifacts are named after the product, not after the tag

- Status: accepted (2026-09-16, at the user's direction: "quero, pode fazer", given after this session reported that the release title reads `MesenAI v0.1.0` while its attached files read `MesenCE-v0.1.0-macos-arm64.zip` and `mesence-tools-v0.1.0.zip`, and named the cost of changing them; implemented in the same change, whose test is `scripts/checks/verify_release_asset_names.sh`)
- Date: 2026-09-16
- Related: ADR-0201 §4 (the list of things deliberately left saying MesenCE), `docs/releases/mesence-v0.1.0.md` (the release body), `scripts/release_macos.sh`
- Supersedes / amends: amends ADR-0201 §4 — it removes the **published assets** from that ADR's "deliberately not renamed" list, and leaves the rest of the list standing.

## Context

ADR-0201 renamed the runtime surface and stopped short of the published
release, listing the assets among the things that keep the old name. The
reason given was real: renaming an asset changes its URL and invalidates the
`SHA256SUMS`, which carries the file names.

What that left is visible on one page. The release title is `MesenAI v0.1.0`
and the table of contents immediately under it offers
`MesenCE-v0.1.0-macos-arm64.zip` and `mesence-tools-v0.1.0.zip`. A reader who
has just been told the project is called MesenAI is asked to download a file
that says otherwise. That is the same contradiction ADR-0201 set out to end,
one layer further out.

Two facts make the correction cheap, and both were checked rather than
assumed:

- **Nothing versioned downloads these files by URL.** A search for
  `releases/download` across the tree returns no project file — the only hits
  are unrelated (test fixtures, a vendored virtualenv). `README.md` points at
  the Releases page, not at an asset URL. No workflow, script or document in
  the repository fetches an asset by its path.
- **The bytes do not change.** A rename is a rename: the zips are re-uploaded
  with new names and the same SHA-256. The only file whose *content* changes
  is `SHA256SUMS`, which lists names beside hashes.

Non-goals. This does not rename the tag (`mesence-v0.1.0`), the release URL,
or the repository. It does not change what is inside either zip.

## Decision

1. **The artifact names are built from the product name, not from the tag
   prefix.** `scripts/release_macos.sh` produces:

   ```
   out/release/MesenAI-<version>-macos-arm64.zip
   out/release/mesenai-tools-<version>.zip
   out/release/SHA256SUMS
   ```

   The tag scheme stays `mesence-vMAJOR.MINOR.PATCH`, so the prefix of an
   artifact and the tag of the release that carries it no longer match. That
   is intended: the tag is a URL that was already published and linked, and
   the file name is what a person reads on the download page.

2. **The staging directories follow**, because `ditto --keepParent` writes the
   staged folder's name into the zip — the folder is the first thing a user
   sees after unpacking:

   ```
   out/stage/MesenAI-<version>-macos-arm64/
   out/stage/mesenai-tools-<version>/
   ```

3. **The published `mesence-v0.1.0` release is corrected in place.** The two
   zips are re-uploaded under the new names, the old ones are removed, and
   `SHA256SUMS` is regenerated from the new names. The tag and the release URL
   are untouched, so every link to the release page keeps working.

4. **The SHA-256 values are asserted unchanged.** The rename is a rename only
   if the hashes are identical before and after; `SHA256SUMS` is the record of
   that. A hash that moves means the artifact was rebuilt, not renamed, and
   the release body's reproducibility claim would need revisiting.

   Measured on the published release: the two zips hash `76172fa0…` and
   `ec1edace…` under both the old and the new names, and both new URLs answer
   `200`.

5. **The docs that name the artifacts say the new names**: `README.md`,
   `docs/releases/macos-zip-README.md`, `docs/releases/tools-zip-README.md`
   and the release body `docs/releases/mesence-v0.1.0.md` (whose *file name*
   follows the tag and therefore stays).

## Consequences

- **The download page stops contradicting itself**: title, table and file
  names all say MesenAI.
- **The direct asset URLs of the old names stop resolving.** Nothing in the
  repository used them, and the release page is what `README.md` links to, so
  the breakage is limited to a URL someone may have bookmarked in the hours
  the release has existed.
- **`SHA256SUMS` must be regenerated in the same operation**, not after it: it
  is generated from the zip basenames, and a stale copy would name two files
  that no longer exist while omitting the two that do.
- **The tag and the artifact prefix now differ on purpose**, and this is the
  kind of thing a later reader "fixes". `scripts/checks/verify_release_asset_names.sh`
  is the answer: it fails if a `MesenCE-`/`mesence-tools-` name returns to the
  four definitions in `release_macos.sh` or to the four documents above, and
  it asserts that `SHA256SUMS` is still derived from the zip basenames.
- **The next release is renamed by construction**, since the script is the
  only thing that names these files; the correction to the existing release
  was a one-off, and is recorded here rather than in a script.
