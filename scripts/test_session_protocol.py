"""The step-mode session's request loop, against the real binary (F14.12/F14.14).

`scripts/test_step_emu.py` checks the client against a stand-in for the tool:
the framing, the error mapping, the ask `play_window` makes. This checks the
*tool's* side of the same contract, which the stand-in cannot: that the C++
loop refuses a request it cannot read instead of reading half of it, that it
keeps serving after a refusal, that a cheat reaches `SetCheats` and is reported
on stdout, and that it exits 0 whether it is told `quit` or just loses stdin.

The ROM is the synthetic NROM `scripts/gen_synthetic_nrom.py` writes - an
infinite loop, no copyrighted data - so this runs anywhere the tool is built.
The game logic is irrelevant here: every request below is answered before the
console's behaviour matters.

Run:  python3 scripts/test_session_protocol.py
"""
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(HERE))
import gen_synthetic_nrom  # noqa: E402

BINARY = HERE / "headless_record"

_FAILURES = []


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


def session(rom, work, requests, *extra):
    """One session, the given request lines, stdin closed after them."""
    args = [str(BINARY), str(rom), "0", str(Path(work) / "session"), "session",
            "mep-off", "hdpack-off", *extra]
    #A request that already ends in a newline (an `input` body does) is not
    #given a second one: the loop would read the empty line as a request and
    #answer "unknown request".
    done = subprocess.run(
        args, input="".join(line if line.endswith("\n") else line + "\n"
                            for line in requests),
        capture_output=True, text=True)
    return done.returncode, done.stdout.splitlines()


def after_ready(lines):
    """The replies, with the init chatter (up to and including "ready") gone."""
    try:
        return lines[lines.index("ready") + 1:]
    except ValueError:
        return []


def main():
    if not BINARY.exists():
        print(f"skip: {BINARY} is not built (run `make capture-tool`)")
        return 0

    with tempfile.TemporaryDirectory(prefix="session-proto-") as tmp:
        rom = Path(tmp) / "synthetic.nes"
        rom.write_bytes(gen_synthetic_nrom.build_rom())

        # ---- a well-formed exchange ----
        body = "4f R\n1f -\n"
        code, out = session(rom, tmp, [
            f"input {len(body)}", body, "run 4", "ram 0x0076", "frame",
            "save", "savefile 1 " + str(Path(tmp) / "kept.mss"), "drop 1",
            "quit"])
        replies = after_ready(out)
        check(code == 0, "a session that is told quit exits 0", f"exit {code}")
        check(replies[0] == "ok 5", "a loaded script answers its frame count",
              str(replies[:1]))
        #input, run, ram, frame, save, savefile, drop, quit
        run_frame = int(replies[1].split()[1])
        check(run_frame == int(replies[3].split()[1]),
              "the frame a run answers with is the frame the session reports",
              str([replies[1], replies[3]]))
        check(len(replies[2].split()) == 2 and replies[2].startswith("ok "),
              "a ram request answers one hex token per item", str(replies[2:3]))
        check(replies[4] == "ok 1" and replies[5] == "ok",
              "a saved state is kept and dropped by its handle",
              str(replies[4:6]))
        check((Path(tmp) / "kept.mss").exists(),
              "savefile writes the kept state out")

        # ---- a malformed request is refused, and the session keeps going ----
        #`strtoul` alone reads "1xyz" as 1: a typo would then play one frame and
        #answer ok, which is a shorter route nobody asked for.
        code, out = session(rom, tmp, [
            "run 1xyz", "ram 0xX76", "restore 1x", "drop 1x", "input 0",
            "savefile 1x /tmp/nope.mss", "frame", "quit"])
        replies = after_ready(out)
        #Six refusals, then the frame request and the quit that follow them.
        check(code == 0 and len(replies) == 8,
              "every malformed request is answered and the session continues",
              f"exit {code}, {replies}")
        check(all(line.startswith("err ") for line in replies[:6]),
              "each one is an err", str(replies[:6]))
        check(replies[6].startswith("ok ") and replies[7] == "ok",
              "and the next well-formed request still answers", str(replies[6:]))
        check(any("1xyz" in line for line in replies),
              "the refused text is named, not just refused", str(replies))

        # ---- cheats reach the console and are reported before "ready" ----
        #F14.14: the session returned to its request loop before the cheat
        #block, so `cheat=` was parsed, validated and dropped in silence.
        code, out = session(rom, tmp, ["quit"], "cheat=00A2:9C")
        applied = [index for index, line in enumerate(out)
                   if line.startswith("cheat applied: 00A2:9C")]
        check(code == 0 and applied,
              "a session reports the cheat it applied, before its ready",
              str(out[:3]))
        check(applied and applied[0] < out.index("ready"),
              "and reports it before the loop starts reading",
              str(out[:3]))

        code, out = session(rom, tmp, ["quit"], "cheat=8000:99")
        check(code != 0 and not after_ready(out),
              "a cheat above internal RAM is refused and no session starts",
              f"exit {code}")

        # ---- a session whose stdin just ends is a session that stops ----
        code, out = session(rom, tmp, ["ram 0x0076"])
        check(code == 0 and after_ready(out)[0].startswith("ok "),
              "EOF without a quit stops the session and exits 0", f"exit {code}")

    print(f"\n{len(_FAILURES)} failure(s)")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
