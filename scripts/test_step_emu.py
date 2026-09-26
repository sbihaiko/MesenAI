"""Headless suite for the step-mode emulator session (`step_emu.py`, F14.12).

Two layers, neither needing a ROM or the emulator.

The first is the request grammar: an address outside the NES internal RAM, a
reversed range, a button the script format does not know - all of it has to be
refused on this side, with a message that names the offending value, because
the alternative is a session that answers about the wrong address.

The second is the protocol framing itself, and it is not ceremony. The module
skips the init chatter a one-shot run prints before the request loop starts
speaking, and a mistake there is invisible until a search has been running for
minutes: the driver reads the "ROM loaded:" line as a reply and every number
after it is off by one. `StubSession` is a stand-in for `headless_record
session` - same argv, same line protocol - so the skip, the request/reply
pairing, the error mapping and the teardown are all exercised for real.

Run:  python3 scripts/test_step_emu.py
"""
import stat
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import step_emu as S  # noqa: E402

_FAILURES = []


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


def raises(call, name, fragment=None):
    try:
        call()
    except Exception as error:  # noqa: BLE001 - the type is what is under test
        if fragment and fragment not in str(error):
            check(False, name, f"message was {str(error)!r}, wanted {fragment!r}")
        else:
            check(True, name)
        return
    check(False, name, "nothing was raised")


#The stand-in. It answers the same protocol and keeps the frame counter the way
#the real session does: `input` counts the frames the script declares, `run`
#adds to the counter, and a state restore is a saved counter.
STUB = r'''#!/usr/bin/env python3
import sys

argv = sys.argv[1:]
rom, prefix = argv[0], argv[2]
out = sys.stdout.buffer
stdin = sys.stdin.buffer
frame = 100   # a state the caller started from is never frame 0
states = {}
nextId = 1


def send(line):
    out.write((line + "\n").encode())
    out.flush()


def err(text):
    send("err " + text)


#The chatter a one-shot run prints, which the module must skip. Written
#through the same stream and with the same flush the tool uses - mixing a
#buffered print() with a flushed os.write here would reorder them and test a
#hazard the C tool does not have (every line it prints goes through stdout
#together, and the flush that sends "ready" sends them first).
send("ROM loaded: %s" % rom)
send("emulated fps: 60.099 (master clock 1789772 Hz)")
send("ready")

while True:
    line = stdin.readline()
    if not line:
        break
    request = line.decode().rstrip("\r\n")
    verb, _, argument = request.partition(" ")

    if verb == "input":
        length = int(argument)
        body = stdin.read(length).decode()
        frames = sum(int(part.split("f")[0])
                     for part in body.splitlines() if part.strip())
        send("ok %d" % frames)
    elif verb == "run":
        frame += int(argument)
        send("ok %d" % frame)
    elif verb == "ram":
        tokens = []
        for item in argument.split(","):
            start, _, end = item.partition("-")
            start = int(start, 0)
            end = int(end, 0) if end else start
            tokens.append("".join("%02x" % ((a + 17) & 0xFF)
                                  for a in range(start, end + 1)))
        send("ok " + " ".join(tokens))
    elif verb == "save":
        states[nextId] = frame
        send("ok %d" % nextId)
        nextId += 1
    elif verb == "restore":
        handle = int(argument)
        if handle not in states:
            err("restore: no state %s" % argument)
        else:
            frame = states[handle]
            send("ok")
    elif verb == "drop":
        states.pop(int(argument), None)
        send("ok")
    elif verb == "frame":
        send("ok %d" % frame)
    elif verb == "savefile":
        handle, _, path = argument.partition(" ")
        if int(handle) not in states or not path:
            err("savefile: needs a known state id and a path")
        else:
            open(path, "w").write("stub %s" % handle)
            send("ok")
    elif verb == "loadfile":
        frame = 500
        states[nextId] = frame
        send("ok %d" % nextId)
        nextId += 1
    elif verb == "quit":
        send("ok")
        break
    elif verb == "explode":
        sys.exit(3)
    else:
        err("unknown request: " + verb)

sys.exit(0)
'''


class StubSession:
    """Writes the stand-in beside a scratch folder and hands back a StepEmu
    driving it. The argv the module builds is passed through unchanged, so a
    change to how the tool is invoked fails here rather than in a search."""

    def __init__(self, tmp):
        self.path = Path(tmp) / "fake_headless_record"
        self.path.write_text(STUB)
        self.path.chmod(self.path.stat().st_mode | stat.S_IXUSR)
        self.work = Path(tmp) / "work"


def buffer_of(*items):
    """A plausible main-RAM image: byte at address a is (a + 17) & 0xFF, the
    same arithmetic the stub answers with."""
    return bytes(((a + 17) & 0xFF) for a in items)


def main():
    # ---- the request grammar ----
    check(S.ram_item_spec(0x76) == "0x0076", "an address is written in hex")
    check(S.ram_item_spec((0x400, 0x497)) == "0x0400-0x0497",
          "a range is start-end inclusive")
    check(S.ram_item_spec(0x7FF) == "0x07ff", "$07FF is the last internal RAM byte")
    check(S.ram_spec([0x76, (0x400, 0x401)]) == "0x0076,0x0400-0x0401",
          "several items are comma-separated in the order asked for")

    raises(lambda: S.ram_item_spec(0x800), "$0800 is refused (ADR-0184)")
    raises(lambda: S.ram_item_spec((0x400, 0x399)), "a reversed range is refused",
           "below its start")
    raises(lambda: S.ram_item_spec((0x10, 0x20, 0x30)),
           "a three-item range is refused", "inclusive")
    raises(lambda: S.ram_item_spec("0x76"), "a string address is refused")
    raises(lambda: S.ram_spec([]), "an empty address list is refused")

    check(S.button_spec("RA") == "RA", "a macro is its button tokens")
    check(S.button_spec("-") == "-", "'-' is no button")
    check(S.button_spec("") == "-", "an empty macro is no button")
    raises(lambda: S.button_spec("X"), "an unknown button is refused", "'X'")
    raises(lambda: S.button_spec("R2"), "a digit is not a button")

    check(S.macro_line(29, "RB") == "29f RB", "a macro line is '<n>f <buttons>'")
    check(S.macro_line(1, "-") == "1f -", "the idle frame a boundary needs")
    raises(lambda: S.macro_line(0, "R"), "a zero-frame macro is refused")
    raises(lambda: S.macro_line(True, "R"), "a bool is not a frame count")

    check(S.parse_ram_reply("02 ffff", [0x76, (0x65, 0x66)])
          == [2, b"\xff\xff"], "a reply is one entry per item, ints and bytes")
    raises(lambda: S.parse_ram_reply("02", [0x76, 0x65]),
           "a short reply is refused", "asked for 2")
    raises(lambda: S.parse_ram_reply("zz", [0x76]),
           "a non-hex reply is refused", "hex byte")

    # ---- the protocol framing, against a stand-in for the tool ----
    with tempfile.TemporaryDirectory() as tmp:
        stub = StubSession(tmp)
        with S.StepEmu("FAKE.nes", work=stub.work, binary=stub.path) as emu:
            check(emu.frame() == 100, "the session starts on the state's frame")
            check(emu.load_script("29f RB\n1f -\n") == 30,
                  "a loaded script is measured in frames it declares")
            check(emu.run(30) == 130, "a run ends on the frame it asked for")
            check(emu.play(29, "RA") == 159, "play is a one-macro load and run")

            check(emu.read_ram(0x76) == [0x87], "a single address is one int")
            check(emu.read_ram((0x51, 0x52)) == [buffer_of(0x51, 0x52)],
                  "a range is bytes")
            check(emu.read_ram(0x86, (0x400, 0x402))
                  == [0x97, buffer_of(0x400, 0x401, 0x402)],
                  "a mixed request is answered per item, in order")

            parent = emu.save()
            emu.play(10, "R")
            child = emu.save()
            check(child != parent, "each save is its own handle")
            emu.restore(parent)
            check(emu.frame() == 159, "restore returns the emulator to the state")
            emu.drop(child)
            raises(lambda: emu.restore(child), "a dropped handle is refused",
                   "no state")

            raises(lambda: emu.read_ram(0x800), "the module refuses $0800 before "
                                                "the session sees it")

            out = Path(tmp) / "kept.mss"
            emu.save_file(parent, out)
            check(out.read_text() == f"stub {parent}",
                  "save_file writes the state the handle names")
            check(emu.load_file(out) > 0, "load_file starts from a .mss")

            emu.play(5, "R")
            raises(lambda: emu._request("explode"),
                   "a session that dies mid-request is reported", "exit")
        check(emu._proc.poll() is not None, "close reaps the session")
        check(emu.close() is None, "close is idempotent")

    #A failed start is reported, not swallowed as an empty session.
    with tempfile.TemporaryDirectory() as tmp:
        bad = Path(tmp) / "not_a_tool"
        bad.write_text("#!/bin/sh\nexit 7\n")
        bad.chmod(bad.stat().st_mode | stat.S_IXUSR)
        raises(lambda: S.StepEmu("FAKE.nes", work=tmp, binary=bad),
               "a tool that exits during init is reported")

    print(f"\n{len(_FAILURES)} failure(s)")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
