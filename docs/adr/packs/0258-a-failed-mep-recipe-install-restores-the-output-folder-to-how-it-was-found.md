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
tests, and the tests did not say so. Those are two separate facts, and neither
causes the other: every *success* test runs on a directory `MakeTempPackDir` has
already created — exactly the shape that skips the old gate — so none of them
could observe a rollback; and the one *failure* test on this path (a primary
sha256 mismatch) removes its temporary directory and then fails in
`ParseAndVerify`, before `PrepareOutputFolder` is reached, so it could not
observe one either.

## Decision

**The core owns the output folder's contents from the moment
`PrepareOutputFolder` succeeds.** It can only succeed on a folder that was
absent or empty, so anything found in it afterwards was written by this install.
On failure the folder is restored to exactly how it was found:

- absent before → removed, so a retry creates it fresh;
- present and empty before → **emptied, not replaced**: the entry the caller
  handed over survives as the same entry, so a symlink stays a symlink and a
  real directory keeps its inode, mode and owner. It is left there and empty, so
  a retry is not refused as "output folder is not empty".

`Install` therefore rolls back on "we prepared it", not on "we created it". The
restoration is a `remove_all` of the folder when the core created it, and a
removal of its contents when the caller did; a removal that fails is logged
rather than swallowed, because the residue it leaves is the defect this ADR
exists to prevent.

**"Empty" means an empty directory, not an empty path.** `is_empty` is true for
a zero-byte regular file as well, so a version of this that accepted any empty
path handed a user's file to `remove_all` and replaced it with a directory. A
path that exists and is not a directory is refused, with the file left exactly
as it is.

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
- Restoring by emptying rather than replacing is what keeps the caller's entry
  intact, and it is also why this decision does not cost file metadata: a real
  directory the caller made keeps its inode, mode and owner, where a
  remove-and-recreate would have silently reset them.
- Not covered, and unchanged from before: when the folder is absent and
  `create_directories` fails partway, the leaf is absent — as it was — but any
  ancestor directories it already created are left behind. That is
  `create_directories`' own behaviour and this decision does not address it.
- Still open on the same path, and deliberately not decided here: the legacy HD
  install path (`InstallHdLegacy`) writes its `pack.json` and stamp after
  extraction without a guard of its own (ADR-0147's path, issue #881), and
  `TryExtractLegacyPack` catches a fixed list of exception types, so a throw
  outside that list skips the cleanup. Both are host-side and need their own
  decision.
