# ADR-0240: F6.9 is re-scoped: install reports a patch-redeemed pack's audio as "not generated", and generation is a separate opt-in job driven by a per-pack name map

- Status: accepted (2026-10-01), decided by the user's pick "Opção 1 + spike da A4" (verbatim, asked after this ADR was drafted as `proposed`): **Option 1** (report, do not generate) is the decision; Option 2 is **not** accepted and stays a future idea with no go-ahead; the **A4** two-pass correlation becomes its own spike slice, F6.10. Both are listed as slices in `docs/roadmap/PRD-mesence-enhancement-ecosystem.md` (F6.9, F6.10); F6.9 (Option 1) was then implemented under the user's separate go-ahead, quoted verbatim: "sim, implemente o F6.9" (2026-10-02; unit tests ship with it, see `UI.Tests/CommunityPacks/PackAudioNoticeTests.cs`); F6.10 then ran as a measurement-only spike under the user's separate go-ahead, quoted verbatim: "go-ahead com 3" (2026-10-02); its report, `docs/validation/f6.10-trigger-id-alignment-2026-10-02.md`, finds that the ids align on Mega Man (17 of 17 `<bgm>` lines) but that the fingerprint side carries no trigger id, so the full A4 join is not derivable yet and nothing beyond the report was merged (CLAUDE.md: accepting an ADR is a request for work, not a note).
- Date: 2026-10-01
- Related: ADR-0144 (the audio exception; "Not implemented" paragraph), ADR-0148 (wired-patch tightening; catalog self-containment), ADR-0135 (extract-audio runtime contract), ADR-0047 (APU fingerprint trigger), ADR-0049 (sibling `auto/` layer), ADR-0041 (NES OGG scope), ADR-0138 (split distribution, recipe deps), ADR-0203 (Windows + macOS arm64 binary policy), PRD Part A row F6.9
- Supersedes / amends: none yet. Options A3 and the "tracks" wording would amend ADR-0144 Decision ("extraction must produce exactly the referenced paths"); that amendment is the user's call and is not made here.

## Context

PRD row F6.9 asks that installing a pack whose audio a wired bundled patch redeems (ADR-0144, ADR-0148) "registers its `<bgm>`/`<sfx>` tracks", proven by a unit test and a headless run on Mega Man (#138) or Zelda II (#141). Reading the code at `649a3e3aa`, the row names a pipeline that does not exist end to end. Five gaps:

1. **The tool writes no `.ogg`.** `scripts/spike_sound_driver.cpp` (ADR-0135) writes `auto/audio/fingerprints.json`, one MIDI per track and `enumeration.log` (:18-21; `grep -c '\.ogg'` = 0). `.ogg` come from `scripts/mep_render_audio.py`, which writes `auto/audio/bgm/<fingerprint id>.ogg` (:6, :220) via fluidsynth + a SoundFont or a placeholder numpy synth, then `ffmpeg` + libvorbis (:170-179); without ffmpeg it leaves a WAV the host cannot play (:13). Core decodes (stb_vorbis in `OggReader`) but has no encoder.
2. **Names cannot be derived.** The packs reference authored names. Mega Man's lint lists 17 `<bgm>` lines such as `BGM/MUS_RM_Fireman.ogg` (one file twice); Zelda II lists 14 lines over 11 files such as `Music/town.ogg`, `Music/overworld.ogg`, `Music/greatpalace.ogg`, three referenced by two album/track ids each. Fingerprint ids are tool-assigned. ADR-0144 resolves each ref "as written" (`HdPackLoader::ProcessBgmTag`, `HdPackLoader.cpp:1027`; `CheckFile`, :1019) and requires extraction to produce exactly those paths, but nothing connects an id to a name.
3. **The tool takes no patch.** `main` accepts `<rom> <workdir> <output-folder>` and tuning numbers (:342-366), copies the ROM (:380) and loads it with an empty patch argument (:402), though the exported C function already has a `patchFile` parameter (:74). The host applies the patch in memory at ROM load (`NesConsole.cpp:567`, keyed by whole-file or No-Intro SHA1, :561-566); the installer never does (`CommunityPackInstallCoordinator.cs`, `LegacyHdPackInstall.cs` read `<patch>` targets only for the ADR-0198 supportedRom check, :146, :331).
4. **The toolchain is not shipped.** The tool is a Makefile target (`spike-sound-driver`, makefile:673) found via `MESEN_EXTRACT_AUDIO_TOOL`, next to the app, or `<home>/Tools` (`NesConsole.cpp:1143-1165`); no workflow references it. python3, ffmpeg and fluidsynth/SoundFont are not bundled, so a Windows or macOS arm64 user (ADR-0203) has none. ADR-0135 records no trigger on roughly half of ROMs (5-6 of 12) and a ~5 min budget (`g_wallBudgetSecs = 300`, :148).
5. **The bounded input is thinner than the row says.** #138's issue comments say it stays de-listed: the link was a branch archive and its `sha256` moved (`666bdeae...` to `20c890e6...`), yet `docs/community-packs.json` at this commit still carries a #138 row pinned to a commit archive (`.../archive/be6f9c23....zip`, `sha256` `21dd51ab...`); I did not resolve which is authoritative. #141 is listed with `rom: {}`. No local ROM matches either.

Two findings change what "generate" can honestly mean:

- **A patch routing music to the pack usually removes it from the APU.** `NesConsole.cpp:543-547`: a `<bgm>` pack's patch "strips the music out of the PRG" so the game asks the pack for an OGG, and warns when the audio layer is off (`WarnAboutSilentPatchedMusic`). Extracting from the *patched* ROM may enumerate nothing; the *unpatched* ROM (today's behavior) is the one that can find music. I ran neither.
- **The artifact differs from what the pack names.** ADR-0148 records the NEA `.ogg` as "distributed separately by the author" (ADR-0148 :37) — authored recordings. The render path produces a General-MIDI (or placeholder synth) rendering of the ROM's own APU music; saving it as `town.ogg` is a stand-in carrying the author's filename, not the author's track. F6.9 via fingerprints also duplicates the F5 bootstrap `auto/` audio (ADR-0049) except for the names.

Non-goals: hosting or committing ROM-derived audio; changing the ADR-0144/0148 verdicts; GB/SMS audio (ADR-0041, NES only).

## Decision

**Decided (2026-10-01): Option 1 now; Option 2 only if the user later wants stand-in tracks; A4 spiked first (F6.10).** The options are kept as weighed.

### Option 1 (recommended): report, do not generate

Install of a pack whose `<bgm>`/`<sfx>` refs do not resolve and whose audio a wired patch redeems finishes as `Installed` (texture install never fails for audio) and adds one non-fatal notice to the outcome and log: "audio not generated: N of M tracks unresolved; supply the `.ogg` files". No process, no patch, no toolchain, identical on Windows and macOS arm64. The ADR-0144 exception stays a classification rule. Cost: the user hears silence for those tracks until they act.

### Option 2 (opt-in follow-up): a separate "Generate pack audio" job

Only when the user asks for stand-in audio. Not a step of `Install()`.

**(a) Mapping.**
- A1, ordered/heuristic (enumeration order to `<bgm>` order): rejected — the ROM's id order is not the author's; duplicates (Zelda II) and ~50 % partial results make a wrong song under a right name likely and undetectable.
- A2, **per-pack name map (recommended for Option 2)**: a catalog table `audio_map: [{ "ref": "Music/town.ogg", "track": "<fingerprint id>" }]`. Names only (no audio), so committable (e); the maintainer authors it once per pack by listening to the enumerated MIDI, since no one else knows the ids. Fingerprint ids are per ROM revision (ADR-0047), so the map is keyed by the ROM SHA1 it was authored against and is inert on any other revision.
- A3, register by fingerprint (ADR-0047/0049): skip `<bgm>` refs and let the `auto/` layer serve `bgm/<id>.ogg` by convention. No mapping, no patched ROM, works on the unpatched game. Cost: amends ADR-0144's "exactly the referenced paths", leaves the pack's `<bgm>` lines unresolved, and makes the patch and fingerprint routes mutually exclusive at play time. It is what the F5 bootstrap already does for any ROM. Viable if F6.9's value is "some music", not "the pack's names".
- A4, two-pass correlation (unverified): run the patched ROM, record which `$41xx` album/track each trigger id writes (`HdAudioDevice` keys tracks as album*256+track, `HdPackLoader.cpp:1026`), run the unpatched ROM for the fingerprints, join on trigger id, map album/track to the `<bgm>` line — deriving A2 automatically. Nothing proves the ids align; needs a spike, not a decision.

**(b) Where rendering runs.** Shell out to the headless tool, then `mep_render_audio.py`; detect `python3`, `ffmpeg` and a SoundFont first and name what is missing ("ffmpeg not found") instead of leaving WAVs. Nothing is bundled: Windows has no python and macOS arm64 no ffmpeg, so a stock machine reports "prerequisites missing" and stops. An in-process path needs a vendored Vorbis encoder (none in the tree) and a renderer — a separate ADR with a size and license review, and the only route to a zero-prerequisite install.

**(c) Patched-ROM input.** Do not patch before extraction; extract from the user's unpatched ROM (today's `LoadRom(.., "")`), because the patch may remove the music from the APU. If A4 is pursued, the tool gains an optional patch argument wired to the existing `patchFile` parameter, chosen by the same whole-file / No-Intro SHA1 rule the host uses (`NesConsole.cpp:561-566`); the installer would hand it the wired patch path, which no installer code does today.

**(d) Failure and budget.** The job is detached and cancellable (ADR-0135 points 2-3: 300 s wall-clock default, per-id frame budget, SIGINT at a frame boundary, partial output kept). A ROM with no validated trigger yields `enumeration.log` only and the notice "audio not generated: no validated trigger for this ROM" (ADR-0135 point 4). A missing toolchain, a cancelled run or a budget stop end as a notice, never an install failure. Refs with no map entry are listed as unresolved, never filled.

**(e) Copyright.** Generated `.ogg` exist only in the user's pack folder. No ROM-derived audio enters git or the catalog (ADR-0144 Alternatives); the catalog holds only the name map (A2). Output written under a pack's referenced path (not `auto/`) is the user's artifact; the job never overwrites a file the user supplied (cf. `mep_render_audio.py` human-layer rule, :17).

**(f) Stop condition replacing the PRD's** ("registers `<bgm>`/`<sfx>`"):

- Option 1: a unit test that installing a fixture pack with wired patch and unresolved `<bgm>` refs returns `Installed` with the audio notice and an intact texture install, and one with no patch returns no notice; a headless run logs the notice.
- Option 2 adds, run by a person with a matching ROM (none local): after the job, every mapped ref exists, decodes through `OggReader`, and the load log has no `OGG file not found` line for it; unmapped refs are listed; a no-trigger ROM and a no-ffmpeg machine each end in a notice with the texture install untouched. "The tracks sound like the pack's" is not claimed: they are stand-ins.

### PRD row F6.9 would need amending (listed, not edited)

1. Deliverable: "generate" becomes "report" (Option 1) or "opt-in job" (Option 2); it must stop saying installing "registers" tracks.
2. The sentence that the builder shortcut "does not write `.ogg`": the tool never writes `.ogg`; `.ogg` come from `mep_render_audio.py`.
3. Bounded input: #138's listing is contradictory (Context 5); #141 has no declared ROM hash and no matching local ROM.
4. Stop condition, as in (f).
5. Decision column: points here instead of "ADR-0144 decided".
6. Header lines of Part A that call F6.9 "install-time audio generation under ADR-0144".
7. ADR-0144 "Not implemented" paragraph and, if A3 is chosen, its "exactly the referenced paths" sentence.

## Consequences

- Option 1 is small, testable, platform-neutral and honest about silence; it delivers no sound.
- Option 2 depends on a hand-authored map and a toolchain the product does not ship; the realistic first user is a maintainer. It cannot be an automatic install step without a bundled encoder.
- ADR-0144's premise that the tracks are "generated from the patched ROM" is weakened: the author's tracks are authored recordings (ADR-0148), the generated ones renderings. If that is accepted, ADR-0144's wording should be amended later.
- Traps: fingerprint ids are per ROM revision, so any map is revision-keyed; patched and fingerprint audio cannot both drive playback; the tool resolves from three locations and ships nowhere, so "tool not found" is the default state of a user install.

## Alternatives

- Implement F6.9 as the row is written (tool plus install hook): rejected, it cannot produce named `.ogg` (Context 1-2).
- Ordered/heuristic mapping: rejected (A1).
- Bundle python, ffmpeg and a SoundFont with the app: rejected for size, license and platform cost; revisit only with the in-process encoder ADR.
- Ship generated `.ogg` in the catalog: rejected, ROM-derived (ADR-0144).
- Fail the install when audio cannot be generated: rejected, textures are the pack's primary content.

## Measured / unverified

Verified by reading at `649a3e3aa` (file:line above): the tool's outputs and arguments, the absence of `.ogg` in `spike_sound_driver.cpp`, the render pipeline, the host-side patch application, the empty patch argument, the loader's as-written resolution, the #138/#141 lint lists, the catalog rows.

Not run: the tool on any ROM; whether it validates a trigger on Zelda II or Mega Man; whether the patched ROM is silent on the APU for these patches (`Revamp+Music.ips`, `Revamp.ips`, `Megaman - Super.ips`; the `NesConsole.cpp` comment says "typically"); whether #141's patches are *wired* (the issue says "present", ADR-0148 requires wired); the Zelda II `<bgm>` album/track numbers (the lint shows paths only); A4's trigger-id alignment; whether #138 is on the live board; the render toolchain on Windows. No pack was downloaded.
