"""A "capture" run must end on the frame a plain run ends on (issue #506).

ADR-0157's guarantee is that the run parks *from inside* the frame it was told
to stop on, so the frame the run reached is a property of the script and not of
the host. `headless_record`'s HUD capture (ADR-0167) is the one step that has to
run with the pause flag cleared - `SystemHud::Draw` paints the pause icon on
every paused frame, so a paused HUD capture could never read `blank=1` - and
until #506 the stop was only re-armed *after* the HUD read, from the live frame
counter. Every frame the emulation thread covered during that window was then
counted: the run's closing "capture finished" line, the final sync-trace sample
and the `save-state=` written afterwards all landed 1-6 frames late under load
(arm D of ~/deep-new/runs/jitter: 5 of 10 runs off, while the `capture:` frame
and its checksum stayed fixed).

This case pins the property that makes the guarantee hold: a `capture` run
reports the same frame count as the same run without `capture`, and the state
it saves is on that frame. The load is not simulated by spawning busy loops -
that would make the case flaky by construction - but by the harness's own
env-gated delay seam, `HEADLESS_HUD_CAPTURE_DELAY_MS`, which sleeps inside the
HUD-capture window. Off by default: nothing but a test sets it.

The ROM is a synthetic NROM image (an infinite loop), so no ROM file is needed.
The binary is a build product (`make capture-tool`); when it is absent the case
says so and exits 0, the way the suite's other environment-bound cases do.
"""

import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
BINARY = ROOT / "scripts" / "headless_record"

FINISHED = re.compile(r"capture finished: (\d+) frames \(target (\d+)\)")
STATE_LOADED = re.compile(r"state loaded: \S+ \(at frame (\d+)")
HUD_BLANK = re.compile(r"capture hud: \d+x\d+ checksum=0x[0-9A-F]+ blank=(\d)")

#Long enough that the emulation thread covers many frames at the harness's
#default (unlimited) emulation speed if it is free to run at all, short enough
#to keep the case quick.
HUD_DELAY_MS = "250"

_FAILURES = []
_CHECKS = []


def check(cond, name, detail=""):
    _CHECKS.append(name)
    print(("ok   " if cond else "FAIL ") + name + ("" if cond else f"\n     {detail}"))
    if not cond:
        _FAILURES.append(name)


def nrom(path):
    """16 KB PRG whose reset vector points at a JMP to itself, 8 KB CHR."""
    prg = bytearray(16384)
    prg[0:3] = b"\x4c\x00\xc0"  # $C000: JMP $C000
    prg[0x3FFA:0x4000] = b"\x00\xc0\x00\xc0\x00\xc0"  # NMI, RESET, IRQ -> $C000
    path.write_bytes(b"NES\x1a\x01\x01" + bytes(10) + bytes(prg) + bytes(8192))


def run(work, args, delay_hud_read=False):
    """Runs the harness on a fresh synthetic ROM and returns its output."""
    rom = work / "loop.nes"
    nrom(rom)
    prefix = work / "out" / "run"
    prefix.parent.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ)
    if delay_hud_read:
        env["HEADLESS_HUD_CAPTURE_DELAY_MS"] = HUD_DELAY_MS
    else:
        env.pop("HEADLESS_HUD_CAPTURE_DELAY_MS", None)
    proc = subprocess.run(
        [str(BINARY), str(rom), "0.5", str(prefix)] + args,
        capture_output=True, text=True, timeout=300, env=env,
    )
    return proc.returncode, proc.stdout + proc.stderr


def finished_frames(output):
    match = FINISHED.search(output)
    if not match:
        return None
    return int(match.group(1)), int(match.group(2))


def test_a_capture_run_ends_on_the_frame_a_plain_run_ends_on():
    with tempfile.TemporaryDirectory() as plain_w, tempfile.TemporaryDirectory() as cap_w:
        plain_code, plain_out = run(Path(plain_w), ["log"])
        cap_code, cap_out = run(Path(cap_w), ["capture", "log"], delay_hud_read=True)
        state_path = Path(cap_w) / "out" / "end.mss"
        save_code, save_out = run(
            Path(cap_w), ["capture", f"save-state={state_path}", "log"], delay_hud_read=True)
        #A second, separate run loads that state; its "state loaded" line names
        #the frame counter the state restored, which is the frame it was saved
        #on.
        load_code, load_out = run(Path(cap_w), [f"state={state_path}", "log"])
        toast_code, toast_out = run(
            Path(cap_w), ["capture", "hud-message=Headless|toast", "log"], delay_hud_read=True)

    check(plain_code == 0 and cap_code == 0,
          "both the plain and the capture run exit 0",
          f"plain={plain_code} capture={cap_code}\n{cap_out[-2000:]}")
    plain = finished_frames(plain_out)
    capture = finished_frames(cap_out)
    check(plain is not None, "the plain run prints its frame count",
          f"output tail:\n{plain_out[-2000:]}")
    check(capture is not None, "the capture run prints its frame count",
          f"output tail:\n{cap_out[-2000:]}")

    if plain and capture:
        #ADR-0157: the run parks from inside the frame it stopped on, so a run
        #of N frames parks on N+1 whatever the host is doing.
        check(plain[0] == plain[1] + 1,
              "the plain run parks on target+1",
              f"reached={plain[0]} target={plain[1]}")
        check(capture[0] == plain[0],
              "a capture run ends on the same frame as a plain run",
              f"capture={capture[0]} plain={plain[0]} (target {capture[1]})")
        check(capture[0] == capture[1] + 1,
              "the capture run parks on target+1",
              f"reached={capture[0]} target={capture[1]}")

    hud = HUD_BLANK.search(cap_out)
    check(hud is not None and hud.group(1) == "1",
          "the HUD capture of a run with no toast reads blank=1 (no pause icon)",
          f"capture hud line: {hud.group(0) if hud else 'missing'}")
    toast = HUD_BLANK.search(toast_out)
    check(toast_code == 0 and toast is not None and toast.group(1) == "0",
          "a hud-message= run still reads blank=0 under the same delay",
          f"exit={toast_code} capture hud line: {toast.group(0) if toast else 'missing'}")

    check(save_code == 0 and load_code == 0,
          "the save-state run and the run that loads it exit 0",
          f"save={save_code} load={load_code}\n{save_out[-2000:]}")
    loaded = STATE_LOADED.search(load_out)
    check(loaded is not None, "loading the saved state names its frame",
          f"output tail:\n{load_out[-2000:]}")
    if loaded and capture:
        check(int(loaded.group(1)) == capture[0],
              "the state written after a capture is on the frame the run ended on",
              f"state frame={loaded.group(1)} run frame={capture[0]}")


def main():
    if not BINARY.exists():
        print(f"SKIP: {BINARY} not built (make capture-tool)")
        return 0
    test_a_capture_run_ends_on_the_frame_a_plain_run_ends_on()
    print(f"\n{len(_CHECKS) - len(_FAILURES)}/{len(_CHECKS)} checks passed")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
