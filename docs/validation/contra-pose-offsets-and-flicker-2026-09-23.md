# Contra pose offsets and sprite flicker — measurements (2026-09-23)

Evidence behind ADR-0225 (per-tile pixel offsets in `poses.json`) and
ADR-0226 (the pose-track linker tolerates one missing retained frame). The
raw material (OAM dump, `tracks.txt`, sidecars, comparison image) lived in a
session scratchpad that is not versioned; this log keeps the numbers the two
ADRs cite. Nothing here changes a pack — it is a reading of what the recorder
already writes.

## Inputs

- ROM: `Contra.nes` (the No-Intro dump `scripts/stages/contra/navigation.json`
  pins; see `docs/validation/f925-contra-matrix-2026-09-15.md` for the hash).
- Driver: `scripts/stages/contra/stage1-probe.txt` — `104f R`, `30f -`,
  `104f L`, `30f -`, repeated. One port; A, B, Up, Down, Select and Start
  never pressed (`poses.json` `input.never`).
- Recorder: `scripts/headless_record` linked against
  `InteropDLL/obj.osx-arm64/MesenCore.dylib`, sha256
  `17ecf8264e2b275e090e2cc3e48c587afa02641f39658f6a537ae07bf4b38435`, built
  2026-09-21 13:37 -03 — so **not** `main` at measurement time, as an earlier
  draft of this log said. The build tree was `63562a9e` plus the then
  uncommitted ADR-0221 option-B experiment, committed two minutes later as
  `502b4f44` (`HdPackBuilder.cpp`, `ScreenStitcher.*`, `TileSheetTypes.h`;
  never merged). Neither that commit nor anything merged between `63562a9e`
  and `5a5bc84f` touches `SpriteGrouping.cpp` or `SpriteGrouping.h`, the
  linker and pose code this log measures. The F12.19 implementation later
  rebuilt the recorder from its branch and re-recorded the same driver: the
  same retained stream (3 667 frames, 27 poses with identical tiles), which
  corroborates the numbers below. The OAM stream is dumped per retained frame
  (`K <shape> <hex>` vocabulary lines, then one line per retained frame with
  `(shape, x, y, palette)` entries), and the linker's tracks are written as
  `frame:pose:held` runs, one track per line.
- Two sidecars from the same driver: `rec` (963 retained frames, a short
  clean stretch) and `full` (3 667 retained frames, including the player's
  death and respawn).

## 1. Sub-tile offsets are lost by `ToCells`

`SpriteGrouping::ToCells` (`Core/NES/HdPacks/SpriteGrouping.cpp`) rounds a
pixel offset to the nearest 8 px cell — `(px + 4) / 8` for `px >= 0` — and
`BuildPoses` stores only that cell (`dx`, `dy`, ADR-0170 §1). Reading the
player's OAM entries of one frame per pose against the sidecar (`rec`,
offsets relative to the cluster's top-left, sorted):

| Pose (frame) | Real OAM offsets | `dx*8`, `dy*8` | Largest error |
|---|---|---|---|
| pose000 (f9) | legs (0,14) (0,22) (8,14) (8,22); torso (4,0) (4,8) (12,0) (12,8); foot (12,30) (12,38) | legs y 16/24; torso x 8/16; foot (16,32) (16,40) | 4 px (torso x), 2 px (legs y) |
| pose005 (f17) | torso (3,0) (3,8) (11,0) (11,8); foot (7,32) (7,40) | torso x 0/8; foot x 8 | 3 px |
| pose008 (f41) | torso (4,0) (4,8) (12,0) (12,8); foot (7,32) (7,40) | torso x 8/16; foot x 8 | 4 px |
| pose001 (f135) | legs (4,14) (4,22) (12,14) (12,22); foot (0,30) (0,38) | legs (8,16) (8,24) (16,16) (16,24); foot y 32/40 | 4 px, 2 px |
| pose011 (f143) | legs (3,16) (3,24) (11,16) (11,24); foot (4,32) (4,40) | legs x 0/8; foot x 8 | 3 px, 4 px |
| pose003 (f239) | legs (10,16) (10,24) (18,16) (18,24); foot (11,32) (11,40) | legs x 8/16; foot x 8 | 3 px |

Every pose of the run cycle carries a torso-to-legs offset of 2–4 px in x
and a 14 px (not 16) legs row. The torso at +4 rounds *up* to a full cell
(8 px) while +3 rounds *down* to 0, so two phases of one run land with the
torso displaced in opposite directions. The legs at y 14 and 22 also
overlap the torso's bottom row by 2 px — a real overlap the composed view
must be able to represent.

The comparison image (three panels per pose: real OAM, `dx*8` grid, kit
grid with ADR-0153's 1-px gutter at pitch 9) showed the effect the artist
sees: on the 8 px grid the torso is shifted a half tile against the legs;
with the gutter added the limbs detach and the figure reads as fragments.
Removing the gutter alone leaves the 4 px error; only the pixel offset
restores the silhouette.

`poses.json` in both sidecars: `"unit": 8`, tile keys `dx`, `dy`, `node`
only. No ADR states what happens to sub-tile offsets — ADR-0170 §1 names
`ToCells` and stops there.

## 2. Flicker shatters tracks

`full` `tracks.txt`: 69 tracks. **61 of them are single-frame tracks**, all
between retained frames **383 and 507**, on every other retained frame
(383, 385, 387, …, 507). The poses on them are the run phases in order
(`23 24 24 19 19 19 … 9 9 9 9 2 2 2 2 8 8 8 8 10 10 10 10 …`): the player is
running, drawn every other frame — the respawn invincibility flicker. Since
`LinkPoseTracks` (ADR-0179 §1) links only consecutive retained frames, each
drawn frame opens a track of its own that nothing continues.

Around the stretch, the stream reads as: the player's long track ends at
retained frame 282 after 4 frames of an 18-tile fusion (`pose021`, the
player touching an enemy); short tracks 284–324; a 58-frame hold of one pose
(`324:13:58`, the death); the flicker 383–507; and from 509 one long track
carries the run to the end of the stream (frame 3 588), with the driver's
cadence visible in its holds: run phases held 8, cut to 3 at the release,
the standing pose held 30 (`30f -`), 5 frames on the first phase after the
press, then 8s again.

## 3. What the sequence fallback does when cycles are missing

ADR-0179 §3 finds cycles by period repetition on one track and then hands
the *uncovered* stretches to the sequence search, which promotes every
all-distinct window of 3–32 poses that recurs identically (≥ 2 times). A
track that never holds two full turns of the run — because it was shattered,
or because it is cut every 104 frames — has no cycle, and the window that
recurs identically is the **driver's own period**.

The previous Contra kit (12 kept poses, 0 cycles, 12 sequences) shows it
exactly: `seq000` is 10 poses with holds `[8, 8, 8, 1, 30, 7, 8, 8, 8, 8]` —
the right-facing run, a 1-frame transition, the 30-frame standing hold, and
the left-facing run — i.e. `104f R / 30f - / 104f L` read back as an
animation. ADR-0179's own Consequences recorded the same shape on 2026-09-12
("the player's run is *not* a cycle … it shows up as four 3–4-pose
sequences instead").

The current build on a clean stretch finds the real animation:

| Sidecar | Retained frames | Poses | Cycles (period, repeats, driver) | Sequences |
|---|---|---|---|---|
| `rec` | 963 | 27 | 3 × period 6 — repeats 6, 4, 3; no driver (too few windows) | 0 |
| `full` | 3 667 | 27 | 3 × period 6 — repeats 26 (`port1`), 24 (`port1`), 3 | 0 |

In `full` the flicker stretch contributed nothing to `repeats` and 61 junk
tracks to the file; the cycle is found on the stretches before the death and
after the respawn. A shorter recording that dies early, or a game whose
figure flickers for longer, has no such stretch and falls back to §3's
sequences.

## Reproduction

`scripts/headless_record` is the native executable `make capture-tool`
builds (not a Python script). The `full` run was, from a stage-1 save state
minted per `docs/validation/f925-contra-matrix-2026-09-15.md` (`.mss` files
are never versioned):

```sh
MESEN_OAM_STREAM_DUMP=run/full/oam.txt \
MESEN_POSE_TRACK_DUMP=run/full/tracks.txt \
  scripts/headless_record run/full/Contra.nes 61 run/full/rec \
    bootstrap hdpack-off \
    input=scripts/stages/contra/stage1-probe.txt state=run/stage1.mss
```

Without the two variables neither `oam.txt` nor `tracks.txt` is written.
The `rec` run is the same command with `16` seconds and its own `run/rec/`
prefix.
Then read `textures/sheets/poses.json` and `adjacency.json` from the recorded
pack. The measurement is a ~60-line stdlib script: for each pose
in `cycles[0]`, take the first frame the linker put it on, keep the OAM
entries whose shape is one of the pose's nodes, subtract the cluster's
min x / min y, and compare with `dx*8`, `dy*8`. Single-frame tracks are the
lines of `tracks.txt` with a single `frame:pose:1` run.
