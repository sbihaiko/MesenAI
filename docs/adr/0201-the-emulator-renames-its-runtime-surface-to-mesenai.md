# ADR-0201: The emulator renames its runtime surface to MesenAI, without moving the user data folder

- Status: accepted (2026-09-16, at the user's direction: "pode fazer", given after this session reported that the rename was a runtime job, that the data folder needs a migration, and that both want an ADR first; implemented in the same change, whose test is `UI.Tests/Config/HomeFolderChoiceTests.cs`)
- Date: 2026-09-16
- Related: ADR-0163 (fork/upstream coexistence), ADR-0137 (the guarded-file list this does not touch), the repository rename of 2026-09-16 recorded in `docs/hd-pack-toolchain-comparison.md`
- Supersedes / amends: nothing. No accepted ADR decided the product's display name or its data folder.

## Context

The repository became `sbihaiko/MesenAI` on 2026-09-16. PR #281 renamed the
release and the project prose, and deliberately stopped at the runtime
boundary, because "MesenCE" there is not prose:

- `UI/ViewModels/MainWindowViewModel.cs` — the **window title**;
- `UI/Config/ConfigManager.cs` — the **user data folder**,
  `…/Application Support/MesenCE`, holding settings, HD packs, save data and
  live recordings;
- `UI/Utilities/MesenMsgBox.cs` and `UI/App.axaml.cs` — the **message-box
  captions**.

A rename that stops at the docs leaves the project in the state where its
README says MesenAI and its own window says MesenCE. That is the state this
ADR exists to end.

The data folder is the only part with real risk, and the codebase already
answers it. `ConfigManager.HomeFolder` has resolved a legacy folder since
before this fork existed:

```csharp
string documentsFolder = DefaultDocumentsFolder;
if(MesenLegacyDocumentsFolder == null || File.Exists(Path.Combine(documentsFolder, "settings.json"))) {
    _homeFolder = documentsFolder;
} else {
    _homeFolder = MesenLegacyDocumentsFolder;
}
```

with `MesenLegacyDocumentsFolder` returning `…/Mesen2` when it holds a
`settings.json`. The precedent is therefore **adopt, never move**: an install
that already has a folder keeps using it, and only a fresh install gets the
new name. Nothing is copied, nothing is deleted, and no user's packs or saves
change location. This ADR extends that existing chain rather than inventing a
migration.

Non-goals. This does not rename the git tag, the published release assets, the
pack format identifiers, or anything that names a different project. It does
not move data.

## Decision

1. **The runtime display name is `MesenAI`.** The window title, the
   message-box captions, and the titles of the Python tools that present
   themselves to a user (`record_viewer`, `record_viewer_layout`,
   `compose_editor`) all say `MesenAI`.

2. **The data folder default becomes `…/MesenAI`, and the existing folder is
   added to the legacy chain — nothing is moved.** The resolution order
   becomes: the new folder if it holds a `settings.json`; else `MesenCE` if it
   holds one; else `Mesen2` if it holds one; else the new folder. An existing
   install therefore keeps its folder, which is the point: a rename must not
   relocate a user's packs, saves or recordings.

   The choice is a **pure function** over the candidates and a
   `hasSettings` predicate, so it is covered by unit tests without a
   filesystem (`UI.Tests/Config/HomeFolderChoiceTests.cs`).

3. **Tools that read the data folder resolve the same chain.** A script that
   looked only at `…/MesenCE` would stop finding an install that has since
   been recreated under `…/MesenAI`, and vice versa. `scripts/record_viewer.py`
   and `scripts/catalog_update_live_check.sh` therefore look for the new
   folder and fall back to the old one.

4. **Deliberately NOT renamed**, each for a stated reason:

   - **Environment variables** `MESENCE_RECORD_VIEWER` and
     `MESENCE_ACCURACY_ROM`. They are external contracts — a developer's shell
     profile, a CI job — and renaming one silently disables configuration
     somebody already set. Adding the new name as an alias is a separate,
     additive change. (2026-09-23: `MESENCE_RECORD_VIEWER` no longer exists —
     the emulator stopped launching the live viewer, ADR-0169 §4 as amended
     that day, so the override had nothing left to configure.)
   - **Identifiers written into produced files**: the VGM creator string
     (`VgmExporter.cpp`), the `emuVersion` field of a generated `.bk2`
     (`fm2_to_bk2.py`), and the `generator=mesence-bootstrap/1` stamp a pack
     carries (`MepPackManager.cpp`). These are version stamps that already
     exist inside files on disk; changing them changes the bytes of output and
     invalidates comparison against anything already produced.
   - **The pack format names** `MEP` and `MEI`, which expand from MesenCE while
     the on-disk format ids stay `mep`/`mei`.
   - **Upstream and third-party references** (`nesdev-org/MesenCE` and the
     fork network), the **published assets** (`MesenCE-v0.1.0-macos-arm64.zip`,
     `mesence-tools-<version>.zip`), the **tag** `mesence-v0.1.0`, the
     `"MesenCE validation"` **data value**, the two **GitHub Project board
     titles**, the `PRD-mesence-enhancement-ecosystem.md` **path**, and the
     `name` of the published MEI index (`docs/community-packs.json`, written
     by `mei_catalog_entry.py`) — all of these are things other systems and
     documents already refer to by that name, and none of them is the
     emulator introducing itself.

5. **Historical evidence is not rewritten.** `docs/validation/` records runs
   that happened when the folder was `MesenCE`, and the commands in
   `metroid-artist-workflow-evidence.md` really did write there. Those
   documents stay as they are: an audit trail that is edited to match the
   present is not an audit trail. Live documents (`docs/hd-pack-authoring.md`)
   are updated instead.

## Consequences

- **The project stops contradicting itself.** The window, the message boxes
  and the docs all say MesenAI.
- **Existing installs stay put, and that is deliberate.** Somebody who has
  been running the emulator keeps `…/MesenCE` forever; a fresh install gets
  `…/MesenAI`. Two users can therefore describe different paths for the same
  build, and support text has to allow for both. This is exactly the
  behaviour the fork already had for `Mesen2`, extended by one link.
- **Nothing migrates, so nothing can be lost.** The cost of this choice is
  that the rename is invisible to an existing user — it is a fresh-install
  change plus a display change, not a move.
- **The free function is the testable core.** `HomeFolderChoice` takes the
  candidates and a predicate, so the precedence is asserted directly rather
  than through the filesystem; if the chain is later reordered, the test on
  the wrong order fails first.
- **Four things still say MesenCE on purpose** (the env vars, the emitted-file
  stamps, the format names, the published assets). Each is listed above with
  its reason, so a future reader does not "finish the job" and break a
  contract.
