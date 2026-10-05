# ADR-0258: A failed MEP recipe install restores the output folder to how it was found

- Status: accepted 2026-10-05 (go-ahead: “resolva os bugs que aparecerem”, the
  session's standing instruction)
- Date: 2026-10-05
- Related: ADR-0138 (MEP recipe external assets and client auto-install),
  ADR-0147 (sibling auto and MEP pack folders), ADR-0211 (a declared
  `supportedRom` mismatch refuses the install), MEP-recipe-v1 §8, issue #881

## Context

`MepRecipeInstaller::Install` runs the recipe's ops in order and aborts at the
first one that fails. The ops before it have already written their files, so a
failure partway leaves a half-written output folder. That residue is not
cosmetic: the folder carries no `.mep-install.json`, and the next install of the
same pack finds a non-empty unstamped `mep/`, which every gate on the path
reads as the *user's* own work — so it refuses, and the only way forward is to
delete the folder by hand.

The rollback that exists was gated on `CreatedOutFolder`, set by
`PrepareOutputFolder` **only when the folder did not exist**, with the comment
"a pre-existing empty folder is the caller's and is left in place". That rule is
wrong about production, not merely conservative: the C# side calls
`TryCreateOutFolder` before `EmuApi.InstallMepRecipe`
(`CommunityPackInstallCoordinator`), so the folder is *always* pre-existing and
empty when the core starts. The rollback therefore never ran outside the unit
tests — and the test harness hid it, because `MakeTempPackDir` pre-creates the
directory, so the only failure test on this path (a primary sha256 mismatch)
fails in `ParseAndVerify`, before `PrepareOutputFolder` is reached.

## Decision

**The core owns the output folder's contents from the moment
`PrepareOutputFolder` succeeds.** It can only succeed on a folder that was
absent or empty, so anything found in it afterwards was written by this install.
On failure the folder is restored to exactly how it was found:

- absent before → removed, so a retry creates it fresh;
- present and empty before → emptied and left in place, so the caller's folder
  still exists and a retry is not refused as "output folder is not empty".

`Install` therefore rolls back on "we prepared it", not on "we created it". The
rollback is a `remove_all` followed, when the folder pre-existed, by a
`create_directories`; a `remove_all` that fails is logged rather than swallowed,
because the residue it leaves is the defect this ADR exists to prevent.

This changes no success path and no refusal path: `PrepareOutputFolder` still
refuses a non-empty folder, and a failure before it (a recipe version, a hash
mismatch) still writes nothing and removes nothing.

## Consequences

- The two states a caller can hand the core now converge on one rule, so the
  correctness of the rollback no longer depends on the caller's ordering —
  which is what made it silently dead in production while passing in tests.
- Callers that pre-create the folder (the community-pack coordinator does, at
  two call sites) keep their folder, empty, on failure. A retry is a plain
  retry.
- The refusal to overwrite a non-empty unstamped folder is unchanged: a
  *failed* install no longer leaves something that looks like one.
- Test harnesses that pre-create the output folder are now exercising the
  production shape rather than hiding it, so the rollback needs its tests to
  assert on the folder's contents, not just on its existence.
- Still open on the same path, and deliberately not decided here: the legacy HD
  install path (`InstallHdLegacy`) writes its `pack.json` and stamp after
  extraction without a guard of its own (ADR-0147's path, issue #881), and
  `TryExtractLegacyPack` catches a fixed list of exception types, so a throw
  outside that list skips the cleanup. Both are host-side and need their own
  decision.
