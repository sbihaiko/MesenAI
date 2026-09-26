#!/usr/bin/env python3
"""ROM-backed determinism check for the step-mode session (F14.12).

The session is only worth having if a script played through it is the script a
one-shot `headless_record` plays. Same ROM, same start state, same script, same
frame count must give the same RAM - otherwise every route the ported search
writes would be a route nobody can replay.

What is compared, on Ninja Gaiden Act 1-1's committed route
(`scripts/stages/ninjagaiden/stage1-run.txt`) from the state its mint script
writes:

  * the whole 2 KB of NES internal RAM at frames 688, 1800 and 3600, byte for
    byte, session against one-shot;
  * the state bytes themselves: the session's in-memory save written out with
    `savefile` against the `.mss` a one-shot run writes at the same frame. If
    the in-memory export and the file export ever disagree, a route chain would
    silently start from a different moment than the search ended on;
  * the two facts the route is described by - absolute x 987 at frame 688, and
    `$0076` = 2 lives throughout.

Needs the ROM (a library file, never copied into this repo) and the built tool.
Either missing and the whole file exits 0 with a "skip" line, the way
`scripts/test_compose_editor_gui.py` self-skips without a display, so a CI
image with neither stays green without pretending it checked anything.

Run:  python3 scripts/test_step_emu_rom.py
"""
import os
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(HERE))
import step_emu  # noqa: E402
import route_search  # noqa: E402
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

_FAILURES = []


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
            print(f"session parks on frame {emu.frame()} after the state load")
            #Loaded once. Every `restore` maps script frame 0 onto the frame
            #the restored state is on, so the same text replays from any state.
            script_frames = emu.load_script_file(ROUTE_SCRIPT)
            print(f"route script: {script_frames} frames")

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
                repeats.append(route_search.read_probes(emu)["abs_x"])
            check(len(set(repeats)) == 1,
                  f"frame {CHECKPOINTS[0]} replays to the same position 3 times",
                  f"read {repeats}")

            for frame in CHECKPOINTS:
                #Session: from the start state, the committed script, N frames.
                #A checkpoint is one run, not two: the state and the RAM are
                #read at the same parked frame.
                emu.restore(start)
                emu.run(frame)
                session_frame = emu.frame()
                probes = route_search.read_probes(emu)
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

                #The route's own claims, on the session's own numbers.
                check(probes["lives"] == start_ram[0x76],
                      f"lives $0076 is {start_ram[0x76]} at frame {frame}",
                      f"read {probes['lives']}")
                if frame == 688:
                    check(int(probes["abs_x"]) == 987,
                          "absolute x is 987 at frame 688",
                          f"read {probes['abs_x']:.2f} "
                          f"(camera {probes['cam']:.2f} + x {probes['ryu_x']:.2f})")
                print(f"     frame {session_frame}: abs_x={probes['abs_x']:.2f} "
                      f"hp={probes['hp']:02X} lives={probes['lives']} "
                      f"room={probes['room']}")

    print(f"\n{len(_FAILURES)} failure(s)")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
