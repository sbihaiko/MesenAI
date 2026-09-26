#!/usr/bin/env python3
"""ROM-backed determinism check for the step-mode session (F14.12).

The session is only worth having if a script played through it is the script a
one-shot `headless_record` plays. Same ROM, same start state, same script, same
frame count must give the same RAM - otherwise every route the ported search
writes would be a route nobody can replay.

What is compared, on the committed route of Ninja Gaiden Act 1-1
(`scripts/stages/ninjagaiden/stage1-run.txt`) from the state its mint script
writes:

  * the whole 2 KB of NES internal RAM at three checkpoints, byte for byte,
    session against one-shot;
  * the state bytes themselves: the session's in-memory save written out with
    `savefile` against the `.mss` a one-shot run writes at the same frame. If
    the in-memory export and the file export ever disagree, a route chain would
    silently start from a different moment than the search ended on.

Those two are properties of the *transport*, and they hold whatever route is
committed - which is why they are the whole of what this file asserts. It used
to assert the route's own numbers too without saying which route they were the
numbers *of*, and that made it fail for a reason that had nothing to do with the
session: F14.13 rewrote `stage1-run.txt` (the route now passes the x 987 pin and
reaches a second section), and a rewritten route is not a broken transport.

So the route it replays is pinned by content, and the route-specific facts are
asserted only while that pin holds:

  * `PINNED_ROUTE_SHA256` is the route this file's facts were measured on - the
    one the slice's own commit carries (`5f6c7c13...`, F14.13's route: it passes
    the x 987 pin, so at window 30 it holds abs x 1170.50 with 2 lives, and at
    frame 688 abs x 988). While the file still hashes to it, those facts are
    checked as well; when a later slice rewrites the route, the file says `skip`
    for them and checks the transport only. Nothing is silently dropped: the
    skip line names both hashes, and moving the pin forward is one line.
  * The checkpoints are `min(CHECKPOINT, script frames)`, so a route of any
    length is replayed to its own end rather than past it.

And the two facts a driver that steers by RAM between windows depends on
(issue #543 - the session's `ram`/`save` path was not inert):

  * `ram`, `save` and `frame` leave the emulated frame where `run` parked it,
    and `restore` returns to the frame its state was saved on;
  * the same route replayed window by window, 30 frames a window with a RAM
    read after each, ends on the same play as a one-shot run of the same 900
    frames - the play the reads are watching, not a variant of it.

Needs the ROM (a library file, never copied into this repo) and the built tool.
Either missing and the whole file exits 0 with a "skip" line, the way
`scripts/test_compose_editor_gui.py` self-skips without a display, so a CI
image with neither stays green without pretending it checked anything.

Run:  python3 scripts/test_step_emu_rom.py
"""
import hashlib
import os
import re
import subprocess
import sys
import tempfile
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(HERE))
import step_emu  # noqa: E402
import mss_ram  # noqa: E402

BINARY = HERE / "headless_record"
ROM = Path(os.environ.get(
    "MESENCE_NG_ROM",
    "/Users/bihaiko/VSCodeProjects/EMULADORES/2. Switch/G3 - Nitendinho/roms/"
    "Ninja Gaiden (1989) (Tecmo).nes"))
MINT_SCRIPT = ROOT / "scripts" / "stages" / "ninjagaiden" / "mint-stage1.txt"
ROUTE_SCRIPT = ROOT / "scripts" / "stages" / "ninjagaiden" / "stage1-run.txt"
#The mint is 18 s - the smallest whole second covering the 1075-frame script -
#so the state is written at frame 1082 and the route's frame 688 is emulator
#frame 1083 + 688.
MINT_SECONDS = 18
CHECKPOINTS = (688, 1800, 3600)
#The route F14.12 measured, by content: the file at that slice's commit. A
#route-specific fact is only a fact about *this* file, so it is checked while
#the file is this one and skipped - loudly - when F14.13 or a later slice
#replaces it. The transport checks above need no pin.
PINNED_ROUTE_SHA256 = \
    "5f6c7c135d10b5a76163ec7fbc0b1903e2bc2ff4233d59a5c3ea5e28dcc4a76a"
#A window is 29 frames of input plus the idle frame every `headless_record` run
#appends past its script, so a window is 30 frames and a state boundary stays
#input-neutral (scripts/stages/README.md). 30 windows is the "window 30" the
#issue measured both of its arms on.
WINDOW_FRAMES = 30
WINDOW_COUNT = 30
WINDOW_END = WINDOW_FRAMES * WINDOW_COUNT

#The route's own RAM map (the header of stage1-run.txt). Spelled here rather
#than imported from the ported search's `route_search.py`: that module belongs
#to another slice's worktree (F14.13), and this check has to run wherever the
#ROM and the built tool are.
WATCHED_BYTES = (
    (0x50, "xpos_sub"), (0x51, "xpos"), (0x52, "xpos_hi"),
    (0x60, "score_lo"), (0x61, "score_hi"), (0x65, "hp"), (0x6E, "room"),
    (0x76, "lives"), (0x84, "state"), (0x85, "x_sub"), (0x86, "x"),
    (0x8A, "y"),
)

_FAILURES = []


def probes_from_ram(ram):
    """Ryu's absolute x and the bytes the route is described by, out of one 2
    KB RAM image - the same numbers whether the image came from a one-shot
    run's .mss or from a session."""
    p = {name: ram[address] for address, name in WATCHED_BYTES}
    #$0052 is the high byte of the level x counter and is signed: left of the
    #level start it reads $FF (stage1-run.txt).
    high = p["xpos_hi"] - 256 if p["xpos_hi"] >= 0x80 else p["xpos_hi"]
    p["cam"] = high * 256 + p["xpos"] + p["xpos_sub"] / 256.0
    p["ryu_x"] = p["x"] + p["x_sub"] / 256.0
    p["abs_x"] = p["cam"] + p["ryu_x"]
    return p


def read_ram_image(emu):
    """The whole 2 KB of NES internal RAM a session holds, in one request - the
    read a search judges a window with."""
    return emu.read_ram((0x0000, 0x07FF))[0]


def read_probes(emu):
    return probes_from_ram(read_ram_image(emu))


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


def one_shot(rom, prefix, seconds, state=None, script=None, save_state=None):
    """The archived searches' own driver: one `headless_record` per candidate,
    a state in and a state out."""
    args = [str(BINARY), str(rom), f"{seconds:.6f}", str(prefix),
            "mep-off", "hdpack-off"]
    if state:
        args.append(f"state={state}")
    if script:
        args.append(f"input={script}")
    if save_state:
        args.append(f"save-state={save_state}")
    done = subprocess.run(args, capture_output=True, text=True)
    if done.returncode != 0:
        raise RuntimeError(f"headless_record failed: {done.stdout}{done.stderr}")
    return done.stdout


def main():
    if not ROM.exists():
        print(f"skip: the ROM is not at {ROM} "
              f"(set MESENCE_NG_ROM to run this)")
        return 0
    if not BINARY.exists():
        print(f"skip: {BINARY} is not built (run `make capture-tool`)")
        return 0

    with tempfile.TemporaryDirectory(prefix="f1412-rom-") as tmp:
        tmp = Path(tmp)
        minted = tmp / "stage1-run.mss"
        one_shot(ROM, tmp / "mint" / "mint", MINT_SECONDS,
                 script=MINT_SCRIPT, save_state=minted)
        start_ram = mss_ram.ram(minted)
        print(f"minted state: {len(start_ram)} bytes of RAM, "
              f"lives ${0x76:02X}={start_ram[0x76]}, "
              f"ryu x ${0x86:02X}={start_ram[0x86]}")

        with step_emu.StepEmu(ROM, work=tmp / "session") as emu:
            start = emu.load_file(minted)
            start_frame = emu.frame()
            print(f"session parks on frame {start_frame} after the state load")
            #Loaded once. Every `restore` maps script frame 0 onto the frame
            #the restored state is on, so the same text replays from any state.
            script_frames = emu.load_script_file(ROUTE_SCRIPT)
            route_sha = hashlib.sha256(ROUTE_SCRIPT.read_bytes()).hexdigest()
            pinned = route_sha == PINNED_ROUTE_SHA256
            print(f"route script: {script_frames} frames, sha256 {route_sha[:16]}")
            if not pinned:
                #The route-specific facts below are this file's, and this is a
                #different file. Said once, with both hashes, so a reader knows
                #what was not checked and why - and a future slice that rewrites
                #the route has one line to update rather than a red suite.
                print(f"skip: the committed route is not the one F14.12 pinned "
                      f"(pinned {PINNED_ROUTE_SHA256[:16]}, found {route_sha[:16]}) - "
                      f"asserting session-versus-one-shot only, not the route's "
                      f"own position and lives")
            #A checkpoint is a frame of the script's own: a pinned checkpoint
            #past the route's end (`3600` against F14.13's 3 330) is clamped to
            #the last frame rather than replayed past the artifact.
            checkpoints = tuple(sorted({min(frame, script_frames)
                                        for frame in CHECKPOINTS}))
            print(f"checkpoints: {', '.join(str(frame) for frame in checkpoints)}")

            #A session is a live emulator driven from another thread, so the
            #first thing to rule out is that it answers the same question
            #differently twice. It did: before HeadlessSaveState/
            #HeadlessLoadState took the emulator lock, 3 of 9 identical runs of
            #the 688-frame window came back with different RAM
            #(docs/validation/f1412-step-mode-emulator-2026-09-26.md sec. 7).
            #One replay of a checkpoint would have caught that a third of the
            #time, which is not a check; three do.
            repeats = []
            for _ in range(3):
                emu.restore(start)
                emu.run(CHECKPOINTS[0])
                repeats.append(read_probes(emu)["abs_x"])
            check(len(set(repeats)) == 1,
                  f"frame {CHECKPOINTS[0]} replays to the same position 3 times",
                  f"read {repeats}")

            #Issue #543: a read has to be inert, and the frame a `run` reports
            #has to be the frame it parks on. Neither held. The core's counter
            #only becomes the frame the session is on once the frame it paused
            #in has finished, so `run` - which answers as soon as the pause is
            #asked for - reported the frame it aimed at, while a `ram` (which
            #takes the emulator lock and therefore waits for the park) reported
            #that plus one. A driver reading RAM after every window played one
            #extra frame per window, and F14.14's stall detector plans a rewind
            #ladder on these states.
            #
            #The 50 ms wait is what makes this a check rather than a coin flip:
            #a parked frame is one that stays put on the wall clock. Without it
            #a sampled counter can be read either side of the frame finishing.
            emu.restore(start)
            parked = emu.run(1)
            time.sleep(0.05)
            check(emu.frame() == parked,
                  "the frame a run replies with is the frame it parks on",
                  f"replied {parked}, parked on {emu.frame()}")
            emu.read_ram(0x86)
            check(emu.frame() == parked,
                  "a ram read leaves the frame where run parked it",
                  f"parked {parked}, the read left {emu.frame()}")
            read_ram_image(emu)
            check(emu.frame() == parked,
                  "a whole-RAM read leaves it there too",
                  f"parked {parked}, the read left {emu.frame()}")
            handle = emu.save()
            check(emu.frame() == parked, "a save leaves it there too",
                  f"parked {parked}, the save left {emu.frame()}")
            emu.run(5)
            emu.restore(handle)
            check(emu.frame() == parked,
                  "restore returns to the frame its state was saved on",
                  f"saved on {parked}, restored to {emu.frame()}")
            emu.drop(handle)

            #The same property where it costs a route. Three runs of the same
            #900 frames of the committed script: a one-shot run (the reference),
            #the session playing it in one go, and the session playing it the
            #way a search does - one 30-frame window at a time, judging each
            #window with a RAM read. Before the fix the third was a different
            #play from the other two: 232 bytes of RAM apart here, and on the
            #route F14.13's search wrote the issue measured it as abs x 1202.50
            #y 208 state 12 against the other two's 1170.50 y 180 state 47.
            window_out = tmp / "window-oneshot.mss"
            window_output = one_shot(ROM, tmp / "window-oneshot" / "run",
                                     WINDOW_END / step_emu.FPS_NTSC,
                                     state=minted, script=ROUTE_SCRIPT,
                                     save_state=window_out)
            #The arms are only "the same frames" if the one-shot's own budget
            #landed where the session counts to: 900 frames from the state is
            #stateFrame + 900 in the tool's arithmetic (`totalFrames +=
            #stateFrame`, headless_record's main), and a rounding drift here
            #would silently compare two different plays.
            ends_at = re.search(r"the run ends at frame (\d+)", window_output)
            check(ends_at and int(ends_at.group(1)) == start_frame + WINDOW_END,
                  f"the one-shot reference runs to frame {start_frame + WINDOW_END}",
                  f"the tool reports "
                  f"{ends_at.group(1) if ends_at else 'no frame'}")
            reference = mss_ram.ram(window_out)
            reference_probes = probes_from_ram(reference)
            print(f"     window {WINDOW_COUNT} one-shot: "
                  f"abs_x={reference_probes['abs_x']:.2f} "
                  f"y={reference_probes['y']} "
                  f"state={reference_probes['state']:02X}")
            #Anchored only while the route is the pinned one: the whole point of
            #this check is that the two session arms are the reference arm, and
            #that holds for any route. Issue #543's own divergence showed up as
            #232 bytes of RAM here (and, on the route F14.13's search wrote, as
            #abs x 1202.50 y 208 state 12 against the one-shot's 1170.50 y 180
            #state 47), which is why the whole 2 KB is compared and not the
            #position: F14.12's route pinned Ryu at x 987 and the route that
            #replaced it passes the pin, so a position assertion is a claim
            #about a route and not about the transport.
            if pinned:
                check(abs(reference_probes["abs_x"] - 1170.50) < 0.001
                      and reference_probes["lives"] == 2,
                      f"the one-shot reference still plays the pinned route at "
                      f"window {WINDOW_COUNT} (abs x 1170.50, 2 lives)",
                      f"read abs_x={reference_probes['abs_x']:.2f} "
                      f"lives={reference_probes['lives']}")

            for reading in (False, True):
                label = ("a RAM read after every window" if reading
                         else "no reads between windows")
                emu.restore(start)
                emu.load_script_file(ROUTE_SCRIPT)
                if reading:
                    for _ in range(WINDOW_COUNT):
                        emu.run(WINDOW_FRAMES)
                        read_ram_image(emu)
                else:
                    emu.run(WINDOW_END)
                arm = read_ram_image(emu)
                probes = probes_from_ram(arm)
                differs = [i for i in range(min(len(arm), len(reference)))
                           if arm[i] != reference[i]]
                print(f"     window {WINDOW_COUNT} session ({label}): "
                      f"abs_x={probes['abs_x']:.2f} y={probes['y']} "
                      f"state={probes['state']:02X}")
                check(not differs and len(arm) == len(reference),
                      f"session replay with {label} is the one-shot play",
                      f"{len(differs)} byte(s) of RAM differ, first at "
                      f"${differs[0]:04X}" if differs else "lengths differ")
                check(probes["abs_x"] == reference_probes["abs_x"],
                      f"absolute x at window {WINDOW_COUNT} matches with "
                      f"{label}",
                      f"{probes['abs_x']:.2f} against "
                      f"{reference_probes['abs_x']:.2f}")

            for frame in checkpoints:
                #Session: from the start state, the committed script, N frames.
                #A checkpoint is one run, not two: the state and the RAM are
                #read at the same parked frame.
                emu.restore(start)
                emu.run(frame)
                session_frame = emu.frame()
                probes = read_probes(emu)
                session_handle = emu.save()
                session_out = tmp / f"session-{frame}.mss"
                emu.save_file(session_handle, session_out)
                emu.drop(session_handle)
                session_ram = mss_ram.ram(session_out)

                #One-shot: the same script, the same start state, the same N
                #frames - <seconds> is the run's frame budget, so a script
                #longer than N is simply cut at N.
                alone_out = tmp / f"oneshot-{frame}.mss"
                one_shot(ROM, tmp / f"oneshot-{frame}" / "run",
                         frame / step_emu.FPS_NTSC, state=minted,
                         script=ROUTE_SCRIPT, save_state=alone_out)
                alone_ram = mss_ram.ram(alone_out)

                differs = [i for i in range(min(len(session_ram), len(alone_ram)))
                           if session_ram[i] != alone_ram[i]]
                check(not differs and len(session_ram) == len(alone_ram),
                      f"RAM at frame {frame} is byte-identical to a one-shot run",
                      f"{len(differs)} byte(s) differ, first at "
                      f"${differs[0]:04X}" if differs else "lengths differ")

                check(session_out.read_bytes() == alone_out.read_bytes(),
                      f"the state written at frame {frame} is byte-identical",
                      "the in-memory save and the file save disagree")

                #The pinned route's own claims, on the session's own numbers -
                #and only while the route is that one. A committed route may
                #lose a life or leave the pin; neither is the session's doing.
                if pinned:
                    check(probes["lives"] == start_ram[0x76],
                          f"lives $0076 is {start_ram[0x76]} at frame {frame}",
                          f"read {probes['lives']}")
                    if frame == 688:
                        check(int(probes["abs_x"]) == 988,
                              "absolute x is 988 at frame 688",
                              f"read {probes['abs_x']:.2f} "
                              f"(camera {probes['cam']:.2f} + x {probes['ryu_x']:.2f})")
                print(f"     frame {session_frame}: abs_x={probes['abs_x']:.2f} "
                      f"hp={probes['hp']:02X} lives={probes['lives']} "
                      f"room={probes['room']}")

    print(f"\n{len(_FAILURES)} failure(s)")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
