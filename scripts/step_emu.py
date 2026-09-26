#!/usr/bin/env python3
"""A long-lived headless emulator session: load a ROM (and a state) once, then
play frames, read RAM and keep states, without relaunching anything.

F14.12, ADR-0238 section 1. The searches this replaces (`runs/route-*/solve.py`)
launched one `scripts/headless_record` per candidate window, so every candidate
paid a process start, a ROM load and two state files - about a second, against
the tens of milliseconds the 29 frames it actually played cost
(`docs/validation/f1412-step-mode-emulator-2026-09-26.md`). This module drives
`headless_record ... session`, which is that tool's own init (scratch home,
MesenNesDB, controller type, AllZeros power-on RAM, EmulationSpeed 0, the pack
flags) followed by a request loop instead of a single run.

    from step_emu import StepEmu
    with StepEmu(rom, state="mint.mss", work="runs/ng") as emu:
        parent = emu.load_file("mint.mss")   # also starts from it
        for macro in ("R", "RB", "RA", "-"):
            emu.restore(parent)
            emu.play(29, macro)
            print(macro, emu.read_ram(0x86), emu.read_ram((0x51, 0x52)))

Determinism is the point and not a hope: the session pauses the emulator from
inside the frame whose number reaches its target, the same in-frame stop a
one-shot run has (ADR-0157 section 4). Same ROM, same start state, same script
and same frame count give the same RAM - `scripts/test_step_emu_rom.py` checks
that against `headless_record` running the same script from the same state.

Protocol: see RunStepSession in scripts/headless_record.cpp. One request line,
one reply line, "ok ..." or "err ...". The init chatter a one-shot run prints on
stdout is skipped here by waiting for the "ready" line the request loop prints
before it reads anything.
"""
import contextlib
import os
import select
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
DEFAULT_BINARY = HERE / "headless_record"
FPS_NTSC = 60.0988
FPS_PAL = 50.0070

#The buttons a NES pad's headless script understands (HeadlessInputScript):
#Up Down Left Right A B Select sTart, and "-" for none.
BUTTON_TOKENS = "UDLRABST"


class StepEmuError(RuntimeError):
    """The session refused a request, or stopped answering."""


def ram_item_spec(item):
    """One `read_ram` item as the protocol writes it: `0x0076`, or
    `0x0400-0x0497` for an inclusive range. Refuses anything above $07FF for
    the reason the core does (ADR-0184): above it is a mirror, a register or
    the cartridge, so a number read there is not a number the game keeps."""
    if isinstance(item, (tuple, list)):
        if len(item) != 2:
            raise ValueError(f"a range is (start, end) inclusive, got {item!r}")
        start, end = item
    else:
        start = end = item
    for address in (start, end):
        if not isinstance(address, int) or isinstance(address, bool):
            raise ValueError(f"an address must be an int, got {address!r}")
        if address < 0 or address > 0x7FF:
            raise ValueError(
                f"${address:04X} is outside the NES internal RAM ($0000-$07FF)")
    if end < start:
        raise ValueError(f"range end ${end:04X} is below its start ${start:04X}")
    if start == end:
        return f"0x{start:04x}"
    return f"0x{start:04x}-0x{end:04x}"


def ram_spec(items):
    """The comma-separated `<spec>` a `ram` request carries."""
    items = list(items)
    if not items:
        raise ValueError("no addresses asked for")
    return ",".join(ram_item_spec(item) for item in items)


def button_spec(buttons):
    """A macro as the input script writes it. "-" and "" both mean no button;
    anything else is a run of the tokens above, for example "RA"."""
    if buttons in ("", "-"):
        return "-"
    for token in buttons:
        if token not in BUTTON_TOKENS:
            raise ValueError(
                f"{token!r} is not a button; the script's tokens are "
                f"{BUTTON_TOKENS} or '-' for none")
    return buttons


def macro_line(frames, buttons):
    """One line of a headless input script: "<count>f <buttons>" (ADR-0157
    section 1 - the count carries its own unit and a bare count is a parse
    error)."""
    if not isinstance(frames, int) or isinstance(frames, bool) or frames <= 0:
        raise ValueError(f"a macro needs a positive frame count, got {frames!r}")
    return f"{frames}f {button_spec(buttons)}"


#The boundary frame a window ends on, and the frame count it costs. A one-shot
#run appends one idle frame past its script, and a save-state boundary is
#input-neutral only with that frame written in (scripts/stages/README.md), so
#every flat script a search or a harness writes carries it between windows -
#and now so does the chain that wrote it. It used to be played by accident:
#before issue #543 a session `ram` read advanced the emulated frame by one, so
#a driver that read RAM after every window played the boundary frame without
#asking for it. With the read inert the chain is one frame per window short of
#its own artifact unless it plays the frame - so it plays it, explicitly, here.
IDLE_FRAMES = 1
IDLE_LINE = macro_line(IDLE_FRAMES, "-")


def parse_ram_reply(reply, items):
    """Turns a `ok <hex> <hex> ...` reply into one entry per requested item: an
    int for a single address, a `bytes` for a range."""
    tokens = reply.split()
    if len(tokens) != len(items):
        raise StepEmuError(
            f"ram: asked for {len(items)} item(s), the session answered "
            f"{len(tokens)}")
    out = []
    for item, token in zip(items, tokens, strict=True):
        try:
            data = bytes.fromhex(token)
        except ValueError as error:
            raise StepEmuError(f"ram: {token!r} is not a hex byte string") from error
        out.append(data[0] if not isinstance(item, (tuple, list)) else data)
    return out


class StepEmu:
    """One `headless_record session` process, spoken to over its stdin/stdout.

    `work` only names a scratch folder: the session builds its emulator home at
    `<work>/../mesen-home` beside the given prefix, exactly as a one-shot run
    does, so a session never shares a home with a recording.
    """

    def __init__(self, rom, state=None, work=None, binary=None, pal=False,
                 timeout=600.0, cheats=None):
        self.rom = str(rom)
        self.timeout = timeout
        #Every line the session prints before "ready", kept rather than
        #discarded: it is where a launch reports what it accepted (F14.14 reads
        #the `cheat applied:` lines, or their absence, from here).
        self.init_output = []
        #`cheat=AAAA:VV[:CC]`, ADR-0184 - the tool parses these before it loads
        #the ROM and applies them after the load (F14.14 passes them through,
        #and checks that the session confirmed each one).
        self.cheats = list(cheats or [])
        self.binary = Path(binary or DEFAULT_BINARY)
        if not self.binary.exists():
            raise StepEmuError(
                f"{self.binary} is not built; run `make capture-tool`")
        prefix = Path(work or (HERE.parent / "runs" / "step-emu")) / "session"
        prefix.parent.mkdir(parents=True, exist_ok=True)
        self._error_log = prefix.with_suffix(".stderr.log")
        #Held open for the session's life, not for a block: it is what
        #_error_tail() reads when a request fails, and close() closes it.
        self._error_file = open(self._error_log, "wb")  # noqa: SIM115
        args = [str(self.binary), self.rom, "0", str(prefix), "session",
                "mep-off", "hdpack-off"]
        if pal:
            args.append("pal")
        if state:
            args.append(f"state={state}")
        args.extend(f"cheat={code}" for code in self.cheats)
        #No shell, and stderr to a file rather than a pipe: nothing the core
        #logs to stderr can then block on a full pipe nobody is draining.
        self._proc = subprocess.Popen(
            args, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=self._error_file, bufsize=0)
        self._buffer = b""
        #Whether a `run n` from here covers exactly n frames or n+1 of them.
        #
        #The session's `run` targets `runFrame + n`, and `runFrame` is the frame
        #whose input was last applied: after a run it is the frame the run ended
        #on, so the next run covers n. A state `save` took carries that same
        #pair, so a `restore` of it lands the session where a `run` left it.
        #`loadfile` is the odd one: a .mss carries the console's own counter,
        #which names the frame *about to* run, and the session takes it as
        #`runFrame` - so the first `run n` after a load covers n+1 frames, the
        #state's own frame among them. That is the same arithmetic a one-shot
        #run does with `state=` (`totalFrames += stateFrame`), and it is why a
        #flat replay of a script through a session matches a one-shot run of it.
        #
        #`play_window` is the one caller that has to know: it asks for one frame
        #less on a just-loaded state so that both cases cover the same window.
        self._standing_after_run = False
        self._load_like = set()
        try:
            self._await_ready()
        except Exception:
            self.close()
            raise

    # ---- process plumbing ------------------------------------------------

    def _readline(self, deadline):
        while b"\n" not in self._buffer:
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise StepEmuError(
                    f"the session did not answer within {self.timeout:.0f}s "
                    f"({self._error_tail()})")
            ready, _, _ = select.select([self._proc.stdout], [], [], remaining)
            if not ready:
                continue
            chunk = os.read(self._proc.stdout.fileno(), 65536)
            if not chunk:
                raise StepEmuError(
                    f"the session exited (code {self._proc.poll()}) while a "
                    f"reply was outstanding ({self._error_tail()})")
            self._buffer += chunk
        line, _, self._buffer = self._buffer.partition(b"\n")
        return line.decode("utf-8", "replace")

    def _error_tail(self, lines=6):
        try:
            text = self._error_log.read_text(errors="replace").strip()
        except OSError:
            return "no stderr log"
        return " | ".join(text.splitlines()[-lines:]) or "stderr empty"

    def _await_ready(self):
        """Skips the init chatter a one-shot run prints - the ROM load line,
        the emulated fps line - up to the "ready" the request loop prints
        before it reads its first request. The lines are kept in
        `init_output`, not dropped: a launch says there what it accepted."""
        deadline = time.monotonic() + self.timeout
        while True:
            line = self._readline(deadline)
            self.init_output.append(line)
            if line == "ready":
                return
            if line.startswith("err "):
                raise StepEmuError(f"the session refused to start: {line[4:]}")

    def _request(self, line, body=None):
        if self._proc.poll() is not None:
            raise StepEmuError(
                f"the session is gone (exit code {self._proc.returncode}; "
                f"{self._error_tail()})")
        payload = line.encode() + b"\n" + (body or b"")
        self._proc.stdin.write(payload)
        self._proc.stdin.flush()
        reply = self._readline(time.monotonic() + self.timeout)
        if reply.startswith("err "):
            raise StepEmuError(f"{line.split()[0]}: {reply[4:]}")
        if not reply.startswith("ok"):
            raise StepEmuError(f"unexpected reply to {line!r}: {reply!r}")
        return reply[2:].strip()

    # ---- the API ---------------------------------------------------------

    def load_script(self, text):
        """Loads a whole input script. Its frame 0 is the frame the emulator
        stands on now, so the same text means the same thing from any state.
        Returns the script's length in frames."""
        if not text.endswith("\n"):
            text += "\n"
        body = text.encode()
        reply = self._request(f"input {len(body)}", body)
        return int(reply)

    def load_script_file(self, path):
        return self.load_script(Path(path).read_text())

    def run(self, frames):
        """Runs `frames` emulated frames of the loaded script, and returns the
        frame the run ended on - the same number `frame()` reports, which the
        next run counts from. Nothing else in this module moves that frame: a
        `read_ram` or a `save` between two runs plays no frame, so a window
        replay that reads RAM after every window is the same play as one that
        does not (issue #543)."""
        if not isinstance(frames, int) or isinstance(frames, bool) or frames <= 0:
            raise ValueError(f"run needs a positive frame count, got {frames!r}")
        self._standing_after_run = True
        return int(self._request(f"run {frames}"))

    def play(self, frames, buttons):
        """A shortcut for one macro: loads "<frames>f <buttons>" and runs it.
        Returns the emulated frame the emulator parked on."""
        self.load_script(macro_line(frames, buttons) + "\n")
        return self.run(frames)

    def play_window(self, lines, frames):
        """Plays one *window* of a chain and returns the frame it parked on.

        A window is the input it carries (`lines`, one `macro_line` per part,
        `frames` frames of it) and the boundary frame that ends it, which is the
        frame a one-shot run appends past its script and a save-state boundary
        is input-neutral only with (`scripts/stages/README.md`). The boundary is
        written into the session's script, so a run of `k` windows is the same
        play as the flat script of those windows - `route_search.py` and
        `jev_harness.py` both play their candidates and their macros this way,
        and `scripts/verify_chain.py` measures the equality on a real route.

        Which frame count to ask for is the subtle part and the reason this is a
        method rather than two lines at each caller: from a state a run ended on
        the session covers exactly the frames asked for, and from a state it was
        just handed by `loadfile` it covers one more (that state's own frame),
        so the ask is one frame less there. Either way the window covers
        `frames + IDLE_FRAMES` frames with the input on the first `frames` of
        them. Before issue #543 this frame came for free: the `ram` read a
        driver made between windows advanced the emulated frame by one, so the
        chain played the boundary without asking and the flat script had to
        write it. The read is inert now, so the chain asks.
        """
        self.load_script("\n".join([*lines, IDLE_LINE]) + "\n")
        return self.run_exact(frames + IDLE_FRAMES)

    def run_exact(self, frames):
        """Runs exactly `frames` frames, whatever state the session stands on.

        `run` counts from `runFrame` - the frame whose input was last applied -
        which is the frame a run ended on but the frame a load is *about to*
        run, so the same `run n` covers n frames after a run and n+1 after a
        load. This is that count with the difference taken out: the ask is one
        frame less on a just-loaded state, and the two cases then cover the same
        span of emulated time. `play_window` is built on it, and so is a caller
        that has to stop mid-window (a checkpoint between two boundary frames).
        """
        if frames < 0:
            raise ValueError(f"run_exact needs a frame count, got {frames!r}")
        ask = frames if self._standing_after_run else frames - IDLE_FRAMES
        if frames and ask < 1:
            #One frame from a just-loaded state is the one span a single `run`
            #cannot name: run(1) covers two frames there. A caller that wants it
            #is asking for a boundary the session does not have, so say so
            #rather than play two frames and call it one.
            raise ValueError(
                "one frame from a just-loaded state is not a run this session "
                "can make; restore a state a run ended on first")
        return self.frame() if ask < 1 else self.run(ask)

    def read_ram(self, *items):
        """Reads NES internal RAM. Each item is an address (an int) or an
        (start, end) inclusive range of them; the reply carries one entry per
        item, an int or a `bytes`, in the order asked for - and it is one
        round trip however many items there are."""
        items = list(items)
        reply = self._request("ram " + ram_spec(items))
        return parse_ram_reply(reply, items)

    def frame(self):
        """The frame the session is on: the one the last `run` ended on, before
        any run the frame of the state the session started from. It does not
        move on its own, and no request but `run` moves it."""
        return int(self._request("frame"))

    def save(self):
        """Keeps the current state in the session. Returns its handle."""
        handle = int(self._request("save"))
        #A state taken before any run is a *load-like* one - the console's
        #counter names a frame the state already holds, exactly as a .mss read
        #by `load_file` does - so restoring it puts play_window back on the
        #one-frame-less ask. Taken after a run, it is an ordinary state.
        if not self._standing_after_run:
            self._load_like.add(handle)
        return handle

    def restore(self, handle):
        """Returns the emulator to a kept state."""
        self._request(f"restore {int(handle)}")
        #A state `save` took came from a run and lands the session after one; a
        #state `loadfile` read is the state's own frame. See play_window.
        self._standing_after_run = int(handle) not in self._load_like

    def drop(self, handle):
        """Forgets a kept state. Safe on an unknown handle."""
        self._request(f"drop {int(handle)}")
        self._load_like.discard(int(handle))

    def save_file(self, handle, path):
        """Writes a kept state out as a .mss - the same bytes
        `headless_record`'s `save-state=` writes, so `scripts/mss_ram.py` and a
        later one-shot run read it."""
        self._request(f"savefile {int(handle)} {Path(path)}")

    def load_file(self, path):
        """Reads a .mss, keeps it and starts from it. Returns its handle."""
        handle = int(self._request(f"loadfile {Path(path)}"))
        self._load_like.add(handle)
        self._standing_after_run = False
        return handle

    def close(self):
        """Asks the session to quit and reaps it. Idempotent."""
        proc = getattr(self, "_proc", None)
        if proc is None:
            return
        if proc.poll() is None:
            #A session that has already died is closed, not complained about.
            with contextlib.suppress(StepEmuError, BrokenPipeError, OSError, ValueError):
                self._request("quit")
            try:
                proc.wait(timeout=10)
            except subprocess.TimeoutExpired:
                proc.terminate()
                try:
                    proc.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    proc.kill()
                    proc.wait()
        for stream in (proc.stdin, proc.stdout):
            with contextlib.suppress(OSError):
                stream.close()
        error_file = getattr(self, "_error_file", None)
        if error_file is not None:
            error_file.close()

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.close()


def main(argv):
    import argparse
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("rom")
    ap.add_argument("--state")
    ap.add_argument("--work")
    ap.add_argument("--play", default=None,
                    help="frames:buttons, e.g. 120:R - runs once and prints RAM")
    ap.add_argument("--ram", default="0x76,0x65,0x86,0x51-0x52")
    a = ap.parse_args(argv[1:])

    with StepEmu(a.rom, state=a.state, work=a.work) as emu:
        items = [tuple(int(x, 0) for x in tok.split("-")) if "-" in tok
                 else int(tok, 0) for tok in a.ram.split(",")]
        if a.play:
            frames, buttons = a.play.split(":")
            print("parked on frame", emu.play(int(frames), buttons))
        print("frame", emu.frame(), "ram", emu.read_ram(*items))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
