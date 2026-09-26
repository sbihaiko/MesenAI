#!/usr/bin/env python3
"""A committed route is one play, whether it is replayed flat or window by window.

A route under `scripts/stages/<game>/*.txt` is a flat script, but it is *found*
as a chain: `route_search.py` plays one window per candidate and `jev_harness.py`
plays one window per macro, each window being its own `input` request to a
step-mode session. The artifact and the search are only the same play if a
pause at a window boundary costs nothing - and that was not true until issue
#543 was fixed, because the session's `ram` read advanced the emulated frame by
one, so a windowed replay ran a frame further per window than the flat script.

This checks the relation directly, on a real route and a real ROM, at three
points of the script (a third, two thirds and the end), comparing all 2 048
bytes of NES internal RAM:

  * **flat** - one `input=<script>`, one run to the checkpoint, through a
    one-shot `headless_record` (the reference a reader of the script would get);
  * **session** - one session, the state loaded once, one `run` to the
    checkpoint;
  * **chain** - the same session, but the script cut at every boundary frame
    (`1f -`) and each piece loaded and run on its own, which is what the search
    that wrote the script did.

Every arm runs twice and must reproduce itself, so a difference is a
difference and not a cold start.

    python3 scripts/verify_chain.py <rom> <state.mss> <script.txt> <work-dir>

Needs the ROM (a library file, never copied into this repo) and the built tool;
neither, and it exits 2 with a line saying which.
"""
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import step_emu  # noqa: E402
import mss_ram  # noqa: E402

BINARY = HERE / "headless_record"
#The line a one-shot run and a session both end on an idle frame with: the
#boundary `route_search.flat_window` writes and `step_emu.play_window` plays.
BOUNDARY = step_emu.IDLE_LINE


def checkpoints(frames: int, kind: int = 3) -> list:
    """`kind` frames spread over the script, the last of them its own end."""
    return sorted({max(1, round(frames * index / kind)) for index in range(1, kind + 1)})


def ends_of(pieces: list) -> list:
    """The frame each window ends on - where a chain can be stopped, because a
    chain is a sequence of `run`s and a `run` cannot stop between two frames of
    one window. A checkpoint is snapped to the nearest of these so all three
    arms are asked about the same instant."""
    out, total = [], 0
    for piece in pieces:
        total += sum(int(line.split("f", 1)[0]) for line in piece)
        out.append(total)
    return out


def windows(lines: list) -> list:
    """A flat script cut at its boundaries: each window is the lines up to and
    including a boundary frame, which is exactly what the writers loaded per
    candidate and per macro."""
    out, current = [], []
    for line in lines:
        current.append(line)
        if line.strip() == BOUNDARY:
            out.append(current)
            current = []
    if current:
        out.append(current)
    return out


def one_shot(rom, prefix, frames, state, script) -> bytes:
    """The reference: a fresh process, one input file, the run's frame budget."""
    args = [str(BINARY), str(rom), f"{frames / step_emu.FPS_NTSC:.6f}", str(prefix),
            "mep-off", "hdpack-off", f"state={state}", f"input={script}",
            f"save-state={prefix}.mss"]
    done = subprocess.run(args, capture_output=True, text=True)
    if done.returncode != 0:
        raise RuntimeError(f"headless_record failed: {done.stdout}{done.stderr}")
    return mss_ram.ram(f"{prefix}.mss")


def session_arm(emu, handle, text, frames) -> bytes:
    """One session arm: back to the state, load the script, run to the frame."""
    emu.restore(handle)
    emu.load_script(text)
    #run_exact, not run: the start state is a loaded one, and a `run` from it
    #covers a frame more than it asks for.
    emu.run_exact(frames)
    return emu.read_ram((0x0000, 0x07FF))[0]


def chain_arm(emu, handle, pieces, frames) -> bytes:
    """The chain: every window played on its own, as the search plays them.

    `play_window` takes the window's input lines and the boundary frame comes
    with them, so this is the code `route_search.py` and `jev_harness.py` run -
    not a re-implementation of it, which would prove nothing about them.
    """
    emu.restore(handle)
    walked = 0
    for piece in pieces:
        #The window's own span is what advances the script, boundary frame and
        #all; `play_window` takes the input frames, which is that span less the
        #boundary frame it adds itself.
        lines = [line for line in piece if line.strip() != BOUNDARY]
        span = sum(int(line.split("f", 1)[0]) for line in piece)
        if walked + span > frames:
            break
        emu.play_window(lines, span - step_emu.IDLE_FRAMES)
        walked += span
        if walked == frames:
            break
    return emu.read_ram((0x0000, 0x07FF))[0]


def verify(rom, state, script, work) -> int:
    text = Path(script).read_text(encoding="utf-8")
    #Blank lines and `#` comments are the script format's own
    #(Core/Shared/HeadlessInputScript.h), and a committed route carries a header
    #of them; they are not frames and the tool does not play them.
    lines = [line for line in text.splitlines()
             if line.strip() and not line.lstrip().startswith("#")]
    frames = sum(int(line.split("f", 1)[0]) for line in lines)
    pieces = windows(lines)
    #A chain stops on a window boundary, so the checkpoints are the window ends
    #nearest the frames asked for - the same instants for all three arms.
    ends = ends_of(pieces)
    points = [min(ends, key=lambda end: abs(end - frame))
              for frame in checkpoints(frames)]
    points = sorted(set(points))
    print(f"{script}: {len(lines)} lines, {frames} frames, "
          f"{len(pieces)} windows; checkpoints {points}")
    failures = 0
    with tempfile.TemporaryDirectory(prefix="verify-chain-") as tmp:
        tmp = Path(tmp)
        with step_emu.StepEmu(rom, work=Path(work) / "session") as emu:
            handle = emu.load_file(state)
            for frame in points:
                arms = {}
                for label, run in (
                        #The one-shot's budget names the frame the run ends on,
                        #so a budget of f-1 frames is the state after the
                        #script's first f frames - the instant the other two
                        #arms are read at.
                        ("flat", lambda f=frame: one_shot(
                            rom, tmp / f"flat{frame}" / "run", f - 1, state, script)),
                        ("session", lambda f=frame: session_arm(emu, handle, text, f)),
                        ("chain", lambda f=frame: chain_arm(emu, handle, pieces, f))):
                    first = run()
                    second = run()
                    arms[label] = first
                    if first != second:
                        print(f"FAIL frame {frame}: the {label} arm is not "
                              f"deterministic")
                        failures += 1
                reference = arms["flat"]
                for label in ("session", "chain"):
                    other = arms[label]
                    differs = [i for i in range(min(len(reference), len(other)))
                               if reference[i] != other[i]]
                    ok = not differs and len(reference) == len(other)
                    print(f"{'ok  ' if ok else 'FAIL'} frame {frame:5d}: "
                          f"{label:<7} is the flat play"
                          + ("" if ok else f" ({len(differs)} byte(s) differ, "
                                           f"first at ${differs[0]:04X})"))
                    failures += 0 if ok else 1
    return failures


def main(argv):
    if len(argv) != 5:
        print(__doc__.strip().splitlines()[-1], file=sys.stderr)
        return 2
    rom, state, script, work = argv[1:]
    if not Path(rom).exists():
        print(f"skip: no ROM at {rom}", file=sys.stderr)
        return 2
    if not BINARY.exists():
        print(f"skip: {BINARY} is not built (make capture-tool)", file=sys.stderr)
        return 2
    Path(work).mkdir(parents=True, exist_ok=True)
    failures = verify(rom, state, script, work)
    print(f"{failures} failure(s)")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
