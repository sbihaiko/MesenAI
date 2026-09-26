#!/usr/bin/env python3
"""Unit tests for scripts/jev_harness.py and scripts/jev_stall_research.py.

No ROM, no session, no network, no key: the emulator is a side scroller
(`FakeEmu`) that pins its runner at x 987 exactly the way Ninja Gaiden does -
nothing but Left+A leaves the spot - and the vendor is a scripted list of choices
(`FakeJev`). What is under test is the harness's own bookkeeping: the rewind
ladder and its floor, the withdrawal of macros that failed here, the RAM gating
of situation tips, the three loop detectors and their escalation, the caps, the
research pass, the cheat rule of ADR-0184 section 1, the script that comes out
and the replay that checks it.

The real artifacts are used where they are the point: the game config is
`scripts/stages/ninjagaiden/ram-map.json` and `jev-tips.json`, so a schema drift
in a game's own files fails here instead of in a 90-second emulator run.
"""
import contextlib
import io
import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import jev_client  # noqa: E402
import jev_harness  # noqa: E402
import jev_stall_research  # noqa: E402

NG = HERE / "stages" / "ninjagaiden"
FPS = jev_harness.FPS
PIN = 987

_FAILURES = []


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


# --------------------------------------------------------------------------
# the fakes
# --------------------------------------------------------------------------

class FakeEmu:
    """A side scroller with the pin: nothing but Left+A leaves x 987.

    It serves the real map's addresses ($0076 lives, $0065 hp, $0086 screen x,
    $0051/$0052 the signed camera, $006E room), so the real `ram-map.json` reads
    it the way it reads a console.
    """

    SPEED = 4           # px per frame held Right: a four-second approach

    def __init__(self, *, start=32, lives=2, hp=0x10, pin=PIN, step=SPEED):
        self.ram = bytearray(0x800)
        self.pin = pin
        self.step = step
        self.loaded = None
        self.state = {"abs_x": start, "hp": hp, "lives": lives,
                      "escaped": pin < 0, "frame": 0, "room": 0}
        self.handles = {}
        self.next_handle = 1
        self.start_handle = self.save()

    def _sync(self):
        state = self.state
        cam = max(0, int(state["abs_x"]) - 128)
        self.ram[0x76] = state["lives"]
        self.ram[0x65] = state["hp"]
        self.ram[0x86] = int(state["abs_x"]) - cam
        self.ram[0x51] = cam & 0xFF
        self.ram[0x52] = (cam >> 8) & 0xFF
        self.ram[0x6E] = state["room"]
        self.ram[0x6D] = 0
        self.ram[0x60] = self.ram[0x61] = 0
        self.ram[0x84] = self.ram[0x8A] = 0
        self.ram[0x62] = self.ram[0x63] = 0

    def _frame(self, buttons):
        state = self.state
        state["frame"] += 1
        move = 0
        if "R" in buttons:
            move = self.step + (4 if "B" in buttons else 0)
        if "L" in buttons:
            move = -self.step
        if not state["escaped"] and state["abs_x"] >= self.pin:
            if "L" in buttons and "A" in buttons:
                #The wall hop: the one press that leaves the pin.
                state["escaped"] = True
                state["abs_x"] += 1
                return
            move = 0
        if not state["escaped"] and state["abs_x"] + move > self.pin:
            #The pin is a wall, not a bucket: the walker lands on it exactly.
            move = self.pin - state["abs_x"]
        state["abs_x"] = max(0, state["abs_x"] + move)

    def play(self, frames, buttons):
        for _ in range(frames):
            self._frame(buttons)
        self._sync()

    def run_exact(self, frames):
        """The frames asked for, exactly - the fake plays frame by frame, so it
        has no load/run asymmetry to take out, and this is `run`."""
        self.run(frames)
        return self.state["frame"]

    def play_window(self, lines, frames):
        """The window a chain plays: the parts' own frames, then the boundary
        frame `step_emu.play_window` writes into the session's script."""
        used = 0
        for line in lines:
            count, _, buttons = line.partition("f")
            used += int(count)
            self.play(int(count), buttons.strip().replace("-", ""))
        self.play(frames + 1 - used, "")
        return self.state["frame"]

    def load_script(self, text):
        """The real session plays a loaded script frame by frame from wherever
        the emulator stands; the fake keeps the cursor so a flat replay of the
        emitted script lands where the chain of plays did."""
        self.script = [line.strip() for line in text.splitlines() if line.strip()]
        self.cursor = 0
        return sum(int(line.split("f")[0]) for line in self.script)

    def run(self, frames):
        left = frames
        while left > 0 and self.cursor < len(self.script):
            count, _, buttons = self.script[self.cursor].partition("f")
            count = int(count)
            take = min(left, count)
            for _ in range(take):
                self._frame(buttons.strip().replace("-", ""))
            left -= take
            self.cursor += 1
            if take < count:
                self.script[self.cursor:self.cursor] = [f"{count - take}f {buttons.strip()}"]
        self._sync()
        return self.state["frame"]

    def read_ram(self, *items):
        self._sync()
        return [bytes(self.ram[item[0]:item[1] + 1]) if isinstance(item, tuple)
                else self.ram[item] for item in items]

    def save(self):
        handle = self.next_handle
        self.next_handle += 1
        self.handles[handle] = dict(self.state)
        return handle

    def restore(self, handle):
        self.state = dict(self.handles[handle])

    def drop(self, handle):
        self.handles.pop(handle, None)

    def load_file(self, path):
        self.loaded = str(path)
        return self.save()

    def save_file(self, handle, path):
        Path(path).write_bytes(b"MSS" + bytes(64))

    def frame(self):
        return self.state["frame"]


class FakeJev:
    """A scripted vendor: one choice per call, the last one repeating.

    A scripted choice that the harness did not offer falls back to an offered
    one, the way the real client would refuse an unoffered answer outright.
    """

    COST = 2.2974e-05

    def __init__(self, choices, *, spent_usd=0.0):
        self.choices = list(choices) or ["WAIT_15"]
        self.calls = 0
        self.spent_usd = spent_usd
        self.seen = []
        self.forced = 0

    def ask(self, question, *, state, instructions, criteria):
        self.seen.append({"question": question, "state": state,
                          "instructions": instructions, "criteria": dict(criteria)})
        wanted = self.choices[min(self.calls, len(self.choices) - 1)]
        if wanted not in criteria:
            self.forced += 1
            wanted = next(iter(criteria))
        self.calls += 1
        self.spent_usd += self.COST
        return jev_client.Decision(
            choice=wanted, probabilities=dict.fromkeys(criteria, 1.0 / len(criteria)),
            confidence=0.8, model="typesafe/jev-1.13-test",
            request_id=f"req-{self.calls}", cost=self.COST)


def _no_research(report):
    """The default: a stall that ends without a web pass, silently."""
    return {}


def make_harness(emu, client, tmp, *, research=None, **kwargs):
    options = {"stall_seconds": 5.0, "settle_seconds": 1.0, "ring_seconds": 60.0,
               "max_questions_per_rung": 3, "max_decisions_per_stall": 12,
               "max_stalls": 1, "max_emulated_seconds": 600.0, "budget_usd": 1.0,
               "frames": 15}
    options.update(kwargs)
    log = Path(tmp) / "events.jsonl"
    return jev_harness.Harness(
        emu, jev_harness.RamMap.load(NG / "ram-map.json"),
        start_handle=emu.start_handle, client=client,
        tips=jev_harness.load_tips(NG / "jev-tips.json",
                                   fields=jev_harness.RamMap.load(NG / "ram-map.json").fields),
        macros=jev_harness.macro_table(options["frames"]), work=Path(tmp),
        log_path=log, dashboard=io.StringIO(),
        research=research or _no_research, **options)


def events(tmp):
    path = Path(tmp) / "events.jsonl"
    if not path.exists():
        return []
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines()]


# --------------------------------------------------------------------------
# the base search, the pin, and the script that comes out
# --------------------------------------------------------------------------

def test_base_search_and_script_format(tmp):
    emu = FakeEmu()
    run = make_harness(emu, FakeJev(["WAIT_15"]), tmp)
    summary = run.run()
    check(summary.frames > 0 and summary.script, "the base search commits a path",
          f"frames={summary.frames}")
    lines = summary.script
    check(all(len(line.split()) == 2 and line.split()[0].endswith("f") for line in lines),
          "every script line is `<n>f <buttons>`", str(lines[:3]))
    check(summary.played_frames > summary.frames,
          "the played clock counts the candidates the search tried, not just the path",
          f"played={summary.played_frames} path={summary.frames}")


def test_macro_library_covers_the_stage_sets(tmp):
    """The two stage sets' tips name macros by their *move*; the table is where
    the move's buttons and its duration live. A macro a tip asks for but the
    table does not carry is a tip that can never be offered."""
    table = jev_harness.macro_table(15)
    for move, buttons in (("SHOOT", "B"), ("SHOOT_BURST", "B"), ("SLIDE", "DA"),
                          ("SLIDE_RIGHT", "DRA"), ("SLIDE_LEFT", "DLA"),
                          ("JUMP_SHOOT", "AB")):
        macro = table.get(f"{move}_15")
        check(macro is not None and macro.buttons == buttons,
              f"{move} is a macro of the fixed table ({buttons})",
              str(macro and macro.buttons))
    base = sorted(name for name in table
                  if name.rsplit("_", 1)[0] in jev_harness.BASE_MACROS)
    check(len(base) == 7,
          "the base search still plays ADR-0238's seven, not the whole library",
          str(base))


def test_route_macros_offer_the_searches_own_windows(tmp):
    """F14.13's candidates are *runs* of parts, not one hold: the wall hop is
    off the wall (a few frames) and then back at it (the rest of the window).
    The harness reads them whole, with the search's own fixed durations."""
    windows = jev_harness.route_macros()
    check({"R", "WH", "WH5", "LJ", "LJA"} <= set(windows),
          "the search's own window labels come across", str(sorted(windows)))
    table = jev_harness.macro_table(15, windows)
    check("WH_15" in table and "LJA_15" in table,
          "a multi-part window becomes one macro of the fixed table",
          ", ".join(sorted(table)))
    check(table["WH_15"].lines() == ("4f LA", "11f RA"),
          "its parts keep the search's durations and fill the window",
          str(table["WH_15"].lines()))
    check(table["WH_15"].line() == "4f LA\n11f RA",
          "the macro is one entry whose text is both parts",
          repr(table["WH_15"].line()))
    check(table["R_15"].lines() == ("15f R",),
          "a one-hold window is still one line", str(table["R_15"].lines()))
    short = jev_harness.macro_table(3, windows)
    check("WH_3" not in short and "R_3" in short,
          "a window a fixed part does not fit is skipped, not a crash",
          ", ".join(sorted(short)))


def test_a_multi_part_macro_is_played_part_by_part(tmp):
    emu = FakeEmu()
    run = make_harness(emu, FakeJev(["WAIT_15"]), tmp)
    played = []
    emu.play_window = lambda lines, frames: played.append((list(lines), frames))
    run._play_macro(run.macros["JUMP_LEFT_15"])
    check(played == [(["15f LA"], 15)],
          "a one-hold macro plays as one window of its own frames", str(played))
    played.clear()
    run._play_macro(jev_harness.Macro("WH_15", (("LA", 4), ("RA", None)), 15, "wall hop"))
    check(played == [(["4f LA", "11f RA"], 15)],
          "a window plays its parts in order, the last holding to the end",
          str(played))
    #The boundary frame is what makes the chain the flat script's own play: a
    #window costs `macro.frames` of input plus that one frame, and every
    #frame-counting caller uses `window_frames` for it.
    hop = jev_harness.Macro("WH_15", (("LA", 4), ("RA", None)), 15, "wall hop")
    check(run._played_frames == jev_harness.window_frames(run.macros["JUMP_LEFT_15"])
          + jev_harness.window_frames(hop),
          "each macro costs one window of emulated frames plus the boundary",
          str(run._played_frames))


def test_tip_macro_escapes_the_pin(tmp):
    emu = FakeEmu()
    client = FakeJev(["WAIT_15", "JUMP_LEFT_15"])
    summary = make_harness(emu, client, tmp, goal=f"abs_x:{PIN + 5}").run()
    check(client.calls >= 1, "the stall asks Jev", f"calls={client.calls}")
    first = client.seen[0]
    check("JUMP_LEFT_15" in first["criteria"],
          "the tip's macro is offered at the pin", ", ".join(first["criteria"]))
    check(any(text.startswith("jump backward-left") and "Tip:" in text
              for text in first["criteria"].values()),
          "the tip whose trigger holds is folded into that option's description",
          str(first["criteria"].get("JUMP_LEFT_15")))
    check(first["state"]["tips"] == ["wall-pin"],
          "only the tips whose RAM trigger holds are carried",
          str(first["state"]["tips"]))
    check(summary.goal_reached, "the run gets past the pin", summary.reason)
    check(any("JUMP_LEFT_15" in entry["criteria"] for entry in client.seen),
          "the question offered the wall hop")


def test_a_tips_move_is_matched_by_the_questions_own_name(tmp):
    """A tip writes the *move* (`JUMP_LEFT`); a question offers the table's name
    for it (`JUMP_LEFT_15`). Comparing the two directly matches nothing, which
    folds every tip into every option - advice for a move the model may not
    even be choosing."""
    emu = FakeEmu()
    client = FakeJev(["WAIT_15"])
    make_harness(emu, client, tmp, max_decisions_per_stall=1).run()
    check(client.seen, "a question was asked at the pin")
    criteria = client.seen[0]["criteria"] if client.seen else {}
    check("Tip:" in criteria.get("JUMP_LEFT_15", ""),
          "the wall tip is folded into its own move's option",
          str(criteria.get("JUMP_LEFT_15"))[:120])
    others = {name: text for name, text in criteria.items() if name != "JUMP_LEFT_15"}
    check(others and not any("Tip:" in text for text in others.values()),
          "and into no other option, or every choice reads the same",
          str(sorted(others))[:120])


def test_tips_are_gated_by_their_trigger(tmp):
    emu = FakeEmu()
    client = FakeJev(["WAIT_15"])
    make_harness(emu, client, tmp, max_decisions_per_stall=1).run()
    check(client.seen, "a question was asked")
    ids = client.seen[0]["state"]["tips"] if client.seen else []
    check("wall-pin" in ids and "barbarian-boss" not in ids and "birds" not in ids,
          "a tip whose RAM trigger does not hold is never sent", str(ids))
    names = set(client.seen[0]["criteria"]) if client.seen else set()
    check("JUMP_LEFT_15" in names and "ATTACK_LEFT_15" not in names,
          "a tip's macro is offered only while that tip's trigger holds",
          str(sorted(names)))


def test_failed_macro_withdrawn_and_ladder_monotone(tmp):
    emu = FakeEmu()
    client = FakeJev(["WAIT_15"])
    make_harness(emu, client, tmp, max_decisions_per_stall=6,
                 max_questions_per_rung=3).run()
    check(len(client.seen) >= 2, "the stall asked more than once", str(len(client.seen)))
    if len(client.seen) >= 2:
        first, second = client.seen[0], client.seen[1]
        check("WAIT_15" in first["criteria"] and "WAIT_15" not in second["criteria"],
              "a macro that failed at this checkpoint is withdrawn from the next "
              "question there")
        check(second["state"]["tried_here"]
              and second["state"]["tried_here"][0]["macro"] == "WAIT_15"
              and second["state"]["tried_here"][0]["died"] is False,
              "the state carries what was tried and how it failed",
              json.dumps(second["state"]["tried_here"]))
    rewinds = [entry["state"]["rewound_seconds"] for entry in client.seen]
    check(all(later >= earlier for earlier, later in zip(rewinds, rewinds[1:])),
          "the ladder only ever rewinds further back", str(rewinds))
    check(all(0 <= value <= 16 for value in rewinds), "no rung is deeper than 16 s",
          str(rewinds))
    check(len(set(rewinds)) >= 2, "the ladder climbs past the first rung", str(rewinds))


def test_checkpoint_floor_is_screen_start_or_last_progress(tmp):
    emu = FakeEmu()
    run = make_harness(emu, FakeJev(["WAIT_15"]), tmp)
    ram = run.ram_map
    run.ring = [jev_harness.Checkpoint(t=float(t), frame=int(t * FPS), handle=t,
                                       state={"abs_x": 100 + t, "camera_x": 0},
                                       progress=100.0 + t, lines=1, screen=0)
                for t in range(1, 21)]
    check(run._checkpoint_at(13.0).t == 13.0,
          "with no marks the ladder reaches any checkpoint")
    run._last_progress_ckpt = run.ring[17]          # progress at t = 18
    check(run._checkpoint_at(13.0).t == 18.0,
          "the last real progress is a floor: a deeper rung clamps to it")
    check(run._checkpoint_at(19.5).t == 19.0,
          "a rung behind the head resolves to the newest checkpoint before it")
    check(run._checkpoint_at(0.0).t == 18.0,
          "no rung ever lands before the floor")
    run._screen_ckpt = run.ring[19]                 # the screen changed at t = 20
    check(run._checkpoint_at(19.5).t == 20.0 and run._checkpoint_at(24.0).t == 20.0,
          "the start of the current screen is the later floor of the two",
          str(run._checkpoint_at(19.5).t))
    check(ram.progress == "abs_x", "the floor test ran on the game's own progress field")


def test_question_excludes_banned_and_carries_tips(tmp):
    emu = FakeEmu()
    run = make_harness(emu, FakeJev(["WAIT_15"]), tmp)
    checkpoint = jev_harness.Checkpoint(
        t=10.0, frame=600, handle=1, lines=1, screen=3, progress=PIN,
        state={"abs_x": PIN, "hp": 16, "enemies_near": 0, "stage": 0})
    stall = jev_harness.StallState(start_t=10.0, watermark=PIN)
    stall.tried_at(10.0).append(("RIGHT_15", PIN, False))
    stall.banned_at(10.0)["LEFT_15"] = "loop guard"
    _names, criteria, matching = run._question(checkpoint, stall)
    check("RIGHT_15" not in criteria and "LEFT_15" not in criteria,
          "a macro tried here, or banned by the loop guard, is not offered",
          ", ".join(criteria))
    check("JUMP_LEFT_15" in criteria, "the pin tip's macro is offered",
          ", ".join(criteria))
    check([tip.id for tip in matching] == ["wall-pin"],
          "the question carries exactly the tips whose trigger holds")

    stall.tried_at(10.0).extend([("JUMP_15", PIN, False), ("ATTACK_15", PIN, False),
                                 ("WAIT_15", PIN, False), ("RIGHT_RUN_15", PIN, False),
                                 ("JUMP_RIGHT_15", PIN, False),
                                 ("JUMP_LEFT_15", PIN, True)])
    check(run._question(checkpoint, stall) is None,
          "a checkpoint with fewer than two macros left is spent")


# --------------------------------------------------------------------------
# the loop guard
# --------------------------------------------------------------------------

def test_loop_detectors(tmp):
    ram = jev_harness.RamMap.load(NG / "ram-map.json")
    check(jev_harness.cycle_in(["A", "B", "A", "B", "A", "B"]) == (2, ["A", "B"]),
          "a period-2 cycle repeated three times is detected")
    check(jev_harness.cycle_in(["A", "B", "C"] * 3) == (3, ["A", "B", "C"]),
          "a period-3 cycle repeated three times is detected")
    check(jev_harness.cycle_in(["A", "B", "A", "B"]) is None, "twice is not three times")
    check(jev_harness.cycle_in(["A", "B", "C", "D", "E"]) is None,
          "a run of distinct choices is not a cycle")
    check(jev_harness.cycle_in(["A", "B", "C", "D"] * 4) == (4, ["A", "B", "C", "D"]),
          "period 4 is covered")
    check(jev_harness.cycle_in(["A", "B"] * 6) == (2, ["A", "B"]),
          "a period-2 run is caught inside the last 12 choices")
    check(jev_harness.cycle_in(["A", "B", "C", "D", "E"] * 3) is None,
          "period 5 is outside the ADR's 2..4")
    check(jev_harness.cycle_in(["C"] * 12) is None,
          "a constant run is not a period-2 cycle: the watermark detector's job")
    check(jev_harness.cycle_in(["A", "B"] * 3, window=5) is None,
          "the window is the ADR's, and a shorter one cannot hold three repeats")

    state = {"abs_x": 987, "camera_x": 859, "room": 0, "hp": 16}
    near = {"abs_x": 991, "camera_x": 859, "room": 0, "hp": 16}
    tiny = {"abs_x": 988, "camera_x": 859, "room": 0, "hp": 16}
    far = {"abs_x": 1100, "camera_x": 972, "room": 0, "hp": 16}
    hurt = {"abs_x": 987, "camera_x": 859, "room": 0, "hp": 15}
    check(jev_harness.fingerprint(state, ram) == jev_harness.fingerprint(near, ram),
          "the fingerprint rounds position to 8 px")
    check(jev_harness.fingerprint(state, ram) != jev_harness.fingerprint(far, ram),
          "a different camera is a different fingerprint")
    check(jev_harness.fingerprint(state, ram) == jev_harness.fingerprint(tiny, ram),
          "one pixel inside the same 8-px cell is the same fingerprint")
    check(jev_harness.fingerprint(state, ram) != jev_harness.fingerprint(hurt, ram),
          "a different HP is a different fingerprint")


def test_loop_guard_escalation(tmp):
    emu = FakeEmu()
    client = FakeJev(["JUMP_RIGHT_15", "RIGHT_15"] * 30)
    researched = []

    def research(report):
        researched.append(report)
        return {"tips": [], "macros": []}

    summary = make_harness(emu, client, tmp, max_decisions_per_stall=60,
                           max_questions_per_rung=3, research=research).run()
    loops = [event for event in events(tmp) if event.get("event") == "loop"]
    check(loops, "the cycle detector fires", str(events(tmp))[:200])
    check(len(researched) == 1, "the ladder's end and the second loop share one "
                                "research pass", str(len(researched)))
    check(loops and all(event.get("flags") for event in loops),
          "every loop is logged, with the detector that fired",
          json.dumps(loops[:1])[:200])
    check(summary.reason == "loop" and summary.loops >= 3,
          "the third loop ends the stall as `loop`", f"{summary.reason} {summary.loops}")


def test_flat_watermark_detector(tmp):
    emu = FakeEmu()
    run = make_harness(emu, FakeJev(["WAIT_15"]), tmp, max_decisions_per_stall=1)
    run.run()
    stall = jev_harness.StallState(start_t=0.0, watermark=987.0)
    stall.frames = 60.0 * FPS + 1
    flags, _cycle = run._loop_flags(stall, {"abs_x": 900, "camera_x": 772, "hp": 16})
    check(any("watermark has not risen" in flag for flag in flags),
          "a watermark flat for 60 emulated seconds is a loop", str(flags))
    stall.frames = 59.0 * FPS
    flags, _cycle = run._loop_flags(stall, {"abs_x": 901, "camera_x": 773, "hp": 16})
    check(not any("watermark" in flag for flag in flags),
          "59 emulated seconds is not 60", str(flags))

    repeated = jev_harness.StallState(start_t=0.0, watermark=987.0)
    state = {"abs_x": 987, "camera_x": 859, "hp": 16}
    flags, _cycle = run._loop_flags(repeated, state)
    run._loop_flags(repeated, dict(state, abs_x=990))
    flags, _cycle = run._loop_flags(repeated, dict(state, abs_x=989))
    check(any("fingerprint" in flag for flag in flags),
          "the same fingerprint three times in one stall is a loop", str(flags))

    alternating = jev_harness.StallState(start_t=0.0, watermark=987.0)
    for name in ["JUMP_RIGHT_15", "RIGHT_15"] * 3:
        flags, cycle = run._loop_flags(alternating, state, choice=name)
    check(cycle == (2, ["JUMP_RIGHT_15", "RIGHT_15"]),
          "six alternating choices are a period-2 cycle", str(cycle))
    check(any("period-2 cycle" in flag for flag in flags),
          "and the detector that fired says so in the log line", str(flags))


# --------------------------------------------------------------------------
# caps
# --------------------------------------------------------------------------

def test_decisions_cap_stops_a_stall(tmp):
    emu = FakeEmu()
    client = FakeJev(["WAIT_15"])
    summary = make_harness(emu, client, tmp, max_decisions_per_stall=2,
                           max_questions_per_rung=1).run()
    check(client.calls == 2, "the per-stall decision cap holds", f"calls={client.calls}")
    check(summary.reason == "decisions-cap", "the stall ends on the cap", summary.reason)


def test_budget_cap_stops_the_run(tmp):
    emu = FakeEmu()
    client = FakeJev(["WAIT_15"], spent_usd=1.0)
    summary = make_harness(emu, client, tmp, budget_usd=1.0).run()
    check(client.calls == 0 and summary.reason == "budget",
          "a spent budget refuses the next call and ends the run", summary.reason)


def test_emulated_seconds_cap(tmp):
    emu = FakeEmu()
    summary = make_harness(emu, FakeJev(["WAIT_15"]), tmp,
                           max_emulated_seconds=1.0, max_stalls=0).run()
    check(summary.reason == "emulated-seconds-cap",
          "the emulated-seconds cap ends the run", summary.reason)
    check(summary.played_frames >= FPS,
          "the cap counts frames the emulator actually ran", str(summary.played_frames))


# --------------------------------------------------------------------------
# research
# --------------------------------------------------------------------------

def test_ladder_exhausted_runs_one_research_pass_then_retries(tmp):
    emu = FakeEmu()
    client = FakeJev(["WAIT_15"])
    calls = []

    def research(report):
        calls.append(report)
        return {"tips": [{"id": "researched", "tip": "research says wall-hop left",
                          "macro": "JUMP_LEFT",
                          "when": [{"field": "abs_x", "min": 950, "max": 1035}]}],
                "macros": [{"name": "HOP_LEFT", "buttons": "LA",
                            "description": "a hop to the left"}]}

    make_harness(emu, client, tmp, max_decisions_per_stall=60,
                 max_questions_per_rung=1, research=research).run()
    check(len(calls) == 1, "the ladder's end runs exactly one research pass",
          str(len(calls)))
    if calls:
        report = calls[0]
        check(report["game"] == "Ninja Gaiden" and report["spot"]["position"],
              "the report carries the game and the spot", json.dumps(report)[:120])
        check(report["tried"] and report["macros_available"],
              "the report carries what was tried and what is available")
    retries = [entry for entry in client.seen
               if "HOP_LEFT_15" in entry["criteria"]
               and any("research says" in text for text in entry["criteria"].values())]
    check(retries, "after research the ladder is walked again with the proposals",
          f"questions={len(client.seen)}")


def test_research_merge_keeps_the_harness_duration(tmp):
    emu = FakeEmu()
    run = make_harness(emu, FakeJev(["WAIT_15"]), tmp)
    added = run._merge_research({"macros": [{"name": "HOP", "buttons": "LA",
                                             "frames": 9999, "description": "a hop"}],
                                 "tips": [{"id": "t1", "tip": "advice",
                                           "when": [{"field": "abs_x", "min": 1}]}]})
    check(added == 2 and run.macros["HOP_15"].frames == 15,
          "a proposed macro keeps the harness's fixed duration, never the worker's",
          str(run.macros["HOP_15"].frames))
    check(run.macros["HOP_15"].buttons == "LA", "its buttons are taken as proposed")
    check([tip.id for tip in run.tips.extra] == ["t1"],
          "a proposed tip joins the runtime tips, not the versioned file")
    dropped = run._merge_research({"macros": [{"name": "BAD", "buttons": "QWERTY"}]})
    check(dropped == 0 and "BAD_15" not in run.macros,
          "a macro with buttons the pad does not have is dropped", str(dropped))


def test_promote_tips_is_a_separate_step(tmp):
    emu = FakeEmu()
    run = make_harness(emu, FakeJev(["WAIT_15"]), tmp)
    run._merge_research({"tips": [{"id": "from-research", "tip": "advice",
                                   "when": [{"field": "abs_x", "min": 1}],
                                   "sources": ["https://example.invalid"]}]})
    versioned = Path(tmp) / "jev-tips.json"
    versioned.write_text(json.dumps({"tips": []}), encoding="utf-8")
    check(run.tips.sha256 and not json.loads(versioned.read_text())["tips"],
          "a research tip does not touch the versioned file on its own")
    added = jev_harness.promote_tips(versioned, run.tips)
    written = json.loads(versioned.read_text(encoding="utf-8"))["tips"]
    check(added == 1 and written[0]["id"] == "from-research"
          and written[0]["when"] == [{"field": "abs_x", "min": 1, "max": None}],
          "`--promote-tips` writes it back with its trigger", json.dumps(written))
    check(jev_harness.promote_tips(versioned, run.tips) == 0,
          "promotion is idempotent")


# --------------------------------------------------------------------------
# cheats (ADR-0184 section 1)
# --------------------------------------------------------------------------

def test_cheat_validation(tmp):
    for code in ("0076:09", "0032:99:FF", "07FF:00", "0a2:ff"):
        try:
            jev_harness.parse_cheat(code)
        except jev_harness.CheatError as error:
            check(False, f"{code} is the accepted form", str(error))
    check(True, "the AAAA:VV[:CC] form under $0800 is accepted")
    try:
        check(jev_harness.parse_cheat("0a2:ff") == "00A2:FF",
              "a short address is normalised, not refused")
    except jev_harness.CheatError as error:
        check(False, "a short address is normalised", str(error))

    #`1000` and `2000` are the NES's mirrors of internal RAM and are still
    #refused: ADR-0184 section 1 draws the line at $07FF, not at what mirrors.
    for code in ("SXKVPZAX", "ZZZZZZ", "8000:99", "1000:99", "2000:99",
                 "07FF:99:00:11", "0032:9", "0032", "", "GGGG:99"):
        try:
            jev_harness.parse_cheat(code)
            check(False, f"{code!r} is refused", "it was accepted")
        except jev_harness.CheatError as error:
            check(not code or code in str(error),
                  f"{code!r} is refused by name", str(error))


def test_cheats_unconfirmed_reads_the_launch(tmp):
    check(jev_harness.cheats_unconfirmed(["cheat applied: 0076:09"], ["0076:09"]) == [],
          "a launch that reports the cheat confirms it")
    #The line the tool actually prints. Reading the whole tail after the colon
    #would carry the parenthetical into the code and make every real line look
    #like an unconfirmed one - which is what the session transport did before
    #F14.14 gave it a cheat block.
    check(jev_harness.cheats_unconfirmed(
        ["cheat applied: 00A2:9C (RAM address, ADR-0184)"], ["00A2:9C"]) == [],
        "the tool's own line, parenthetical and all, confirms the code")
    check(jev_harness.cheats_unconfirmed([], ["0076:09"]) == ["0076:09"],
          "a launch that says nothing has not applied it")
    check(jev_harness.cheats_unconfirmed(["cheat applied: 0076:09"],
                                         ["0076:09", "0065:10"]) == ["0065:10"],
          "an unconfirmed code is named")


def test_main_refuses_without_a_key_and_on_a_bad_cheat(tmp):
    game = Path(tmp) / "stages" / "ninjagaiden"
    game.mkdir(parents=True, exist_ok=True)
    for name in ("ram-map.json", "jev-tips.json"):
        (game / name).write_text((NG / name).read_text(encoding="utf-8"), encoding="utf-8")
    argv = ["--rom", str(Path(tmp) / "nope.nes"), "--game", "ninjagaiden",
            "--state", str(Path(tmp) / "nope.mss"), "--work", str(Path(tmp) / "work")]

    saved = (jev_harness.STAGES, jev_harness.jev_client.ROOT,
             os.environ.pop(jev_client.API_KEY_ENV, None))
    jev_harness.STAGES = Path(tmp) / "stages"
    jev_harness.jev_client.ROOT = Path(tmp) / "no-such-repo"
    try:
        for cheat, needle in (("8000:99", "8000:99"), ("SXKVPZAX", "SXKVPZAX"),
                              ("0032:9", "0032:9")):
            err = io.StringIO()
            with contextlib.redirect_stderr(err):
                code = jev_harness.main([*argv, "--cheat", cheat])
            check(code == 2 and needle in err.getvalue(),
                  f"the run is refused on {needle} before any emulator starts",
                  err.getvalue()[:140])
        err = io.StringIO()
        with contextlib.redirect_stderr(err):
            code = jev_harness.main([*argv, "--cheat", "0076:09"])
        check(code == 2 and jev_client.API_KEY_ENV in err.getvalue(),
              "with no key the harness refuses to run at all", err.getvalue()[:140])
    finally:
        jev_harness.STAGES, jev_harness.jev_client.ROOT, env = saved
        if env is not None:
            os.environ[jev_client.API_KEY_ENV] = env


# --------------------------------------------------------------------------
# the run-level stage (ADR-0238 section 3)
# --------------------------------------------------------------------------

def test_stage_comes_from_the_flag_or_the_stage_set(tmp):
    """A tip gated on `stage` needs a run that declares one, and the two
    sources are `--stage` and the set's own `stage`/`stages`."""
    single = Path(tmp) / "single.json"
    single.write_text('{"game": "x", "stage": "snake-man", "stages": ["snake-man"]}',
                      encoding="utf-8")
    many = Path(tmp) / "many.json"
    many.write_text('{"game": "x", "stages": ["snake-man", "gemini-man"]}',
                    encoding="utf-8")
    none = Path(tmp) / "none.json"
    none.write_text('{"game": "x"}', encoding="utf-8")

    check(jev_harness.resolve_stage(single, None) == ("snake-man", ["snake-man"]),
          "a set that names its one stage supplies it")
    check(jev_harness.resolve_stage(single, "gemini-man")
          == ("gemini-man", ["snake-man"]),
          "--stage wins over the set")
    #A set with several stages and no way to say which this run plays is not a
    #source: taking the first would gate on a stage the run may not be in.
    named, known = jev_harness.resolve_stage(many, None)
    check(named is None and known == ["snake-man", "gemini-man"],
          "a set of several stages names none, and says which it carries",
          f"{named!r} of {known}")
    check(jev_harness.resolve_stage(many, "gemini-man")[0] == "gemini-man",
          "and --stage still names one of them")
    check(jev_harness.resolve_stage(none, None) == (None, []),
          "a set that names nothing declares nothing")
    check(jev_harness.resolve_stage(Path(tmp) / "missing.json", None) == (None, []),
          "a set that is not there is not an error")

    #The refusal the flag needs: a stage the set does not carry is a typo, and
    #a typo would gate every tip off in silence.
    err = io.StringIO()
    argv = ["--rom", str(Path(tmp) / "nope.nes"), "--game", "nosuchgame",
            "--state", str(Path(tmp) / "nope.mss"), "--stage", "needle-man"]
    saved = jev_harness.STAGES
    jev_harness.STAGES = Path(tmp) / "stages"
    game = jev_harness.STAGES / "nosuchgame"
    game.mkdir(parents=True, exist_ok=True)
    (game / "stage-set.json").write_text(
        '{"game": "x", "stages": ["snake-man", "gemini-man"]}', encoding="utf-8")
    try:
        with contextlib.redirect_stderr(err):
            code = jev_harness.main(argv)
    finally:
        jev_harness.STAGES = saved
    check(code == 2 and "stage set" in err.getvalue() and "needle-man" in err.getvalue(),
          "a --stage the set does not carry is refused, not ignored",
          err.getvalue()[:140])


# --------------------------------------------------------------------------
# the output script and the replay that checks it
# --------------------------------------------------------------------------

def _ram_bytes(state) -> bytearray:
    """A 2 KB RAM image carrying one state, for the replay comparison."""
    ram = bytearray(0x800)
    cam = max(0, int(state["abs_x"]) - 128)
    ram[0x76] = state["lives"]
    ram[0x65] = state["hp"]
    ram[0x86] = int(state["abs_x"]) - cam
    ram[0x51] = cam & 0xFF
    ram[0x52] = (cam >> 8) & 0xFF
    return ram


def test_script_output_and_verify(tmp):
    emu = FakeEmu()
    client = FakeJev(["WAIT_15", "JUMP_LEFT_15"])
    summary = make_harness(emu, client, tmp, goal=f"abs_x:{PIN + 5}").run()
    check(len(summary.checkpoints) >= 3,
          "the run keeps at least three RAM checkpoints", str(len(summary.checkpoints)))
    out = jev_harness.write_script(Path(tmp) / "route.txt", summary.script)
    text = out.read_text(encoding="utf-8").replace(jev_harness.IDLE_LINE, "1f X")
    check(all(len(line.split()) == 2 for line in text.splitlines()),
          "the script is input only - no comments, no state", text[:60])

    wanted = {entry["frame"]: entry["state"] for entry in summary.checkpoints}
    played = []

    def fake_one_shot(*, rom, prefix, frame, state, script, save_state):
        played.append((frame, script, state))
        Path(save_state).write_bytes(b"MSS")
        return ""

    def exact(path):
        return _ram_bytes(wanted[exact.frame])

    def drifted(path):
        return _ram_bytes(dict(wanted[drifted.frame], abs_x=42))

    checkpoints = [(entry["frame"], entry["state"]) for entry in summary.checkpoints]
    exact.frame = checkpoints[-1][0]
    drifted.frame = checkpoints[-1][0]
    good = jev_harness.verify_script("rom.nes", "state.mss", str(out), checkpoints[-1:],
                                     jev_harness.RamMap.load(NG / "ram-map.json"),
                                     work=Path(tmp) / "verify", one_shot=fake_one_shot,
                                     ram_of=exact)
    check(good["ok"], "a replay that matches its checkpoint verifies",
          json.dumps(good)[:200])
    zero = jev_harness.verify_script("rom.nes", "state.mss", str(out), checkpoints,
                                    jev_harness.RamMap.load(NG / "ram-map.json"),
                                    work=Path(tmp) / "verify0", one_shot=fake_one_shot,
                                    ram_of=exact)
    check(zero["skipped_frames"] == [checkpoints[0][0]],
          "a frame-0 checkpoint is skipped, not faked",
          json.dumps(zero["skipped_frames"]))
    check(played and played[0][1] == str(out) and played[0][2] == "state.mss",
          "the replay is a one-shot run of the script from the minted state",
          str(played[:1]))
    bad = jev_harness.verify_script("rom.nes", "state.mss", str(out), checkpoints[-1:],
                                    jev_harness.RamMap.load(NG / "ram-map.json"),
                                    work=Path(tmp) / "verify", one_shot=fake_one_shot,
                                    ram_of=drifted)
    check(not bad["ok"], "a replay that drifts from the checkpoint is caught",
          json.dumps(bad)[:200])


def test_the_flat_replay_is_what_gets_measured(tmp):
    """The search plays a chain of `play` calls; the artifact is a flat script.

    A chain is not the same thing: the session's pause costs a frame per play,
    so the chain's frame numbers run ahead of the script's, and a chain may
    reach a state the flat script does not. The summary is taken from the flat
    replay, and a path only the chain reaches is reported, never shipped.
    """
    emu = FakeEmu()
    client = FakeJev(["WAIT_15", "JUMP_LEFT_15"])
    summary = make_harness(emu, client, tmp, goal=f"abs_x:{PIN + 5}").run()
    check(summary.reason == "goal" and summary.goal_reached,
          "a path the flat script reproduces is a goal", summary.reason)
    text = jev_harness.render_script(summary.script)
    check(summary.frames == sum(int(line.split("f")[0]) for line in text.split("\n") if line),
          "the summary's frames are the rendered script's own", str(summary.frames))
    check(text.count(jev_harness.IDLE_LINE) == len(summary.script),
          "every macro gets the input-neutral boundary frame the chain needs",
          text[:60])
    check(jev_harness.render_script([]) == "",
          "an empty path renders an empty script")
    check([entry["frame"] for entry in summary.checkpoints] ==
          sorted(entry["frame"] for entry in summary.checkpoints),
          "the checkpoints come off the flat replay, in order",
          str([entry["frame"] for entry in summary.checkpoints]))

    class LossyEmu(FakeEmu):
        """A fake whose *flat replay* loses the wall hop the chain found.

        The session plays a loaded script and a chain of play calls down two
        different paths; here they differ on purpose, so there is a chain that
        gets somewhere and a script that does not - the case the gate exists
        for.
        """

        flat = False

        def load_script(self, text):
            self.flat = True
            return super().load_script(text)

        def play(self, frames, buttons):
            self.flat = False
            return super().play(frames, buttons)

        def _frame(self, buttons):
            if self.flat and "L" in buttons and "A" in buttons:
                buttons = buttons.replace("L", "R")
            return super()._frame(buttons)

    lossy = make_harness(LossyEmu(), FakeJev(["WAIT_15", "JUMP_LEFT_15"]), tmp,
                         goal=f"abs_x:{PIN + 5}").run()
    check(not lossy.goal_reached and lossy.reason == "chain-not-reproduced",
          "a goal only the chain reaches is reported, not shipped",
          f"{lossy.reason} {lossy.goal_reached}")
    check(lossy.chain_goal_reached is True,
          "the summary says the chain believed it", str(lossy.chain_goal_reached))
    as_json = lossy.as_json()
    #The chain and the script are the same number of frames now that the chain
    #plays the boundary frame itself (`_play_macro`), which is what makes this
    #verdict a real disagreement between two plays rather than the one-frame
    #drift the chain used to leave behind: a script one frame per macro shorter
    #than the chain that wrote it is not a script of that chain.
    check(as_json["script_frames"] == as_json["chain_frames"],
          "the script and the chain are the same frames",
          json.dumps({k: as_json[k] for k in ("chain_frames", "script_frames")}))


# --------------------------------------------------------------------------
# the config readers
# --------------------------------------------------------------------------

def test_ram_map_shapes(tmp):
    ram = jev_harness.RamMap.load(NG / "ram-map.json")
    check(ram.progress == "abs_x" and ram.screen == "camera_x",
          "the progress and screen fields come from the game's own file")
    values = {"ryu_screen_x": 128, "x_sub": 0, "camera_x": 859}
    check(jev_harness.eval_field_expr("camera_x + ryu_screen_x", values) == 987,
          "an expr field is evaluated over the fields before it")
    check(jev_harness.eval_field_expr("ryu_screen_x + x_sub / 256", values) == 128,
          "a fractional byte divides as it should")
    for bad in ("__import__('os').system('true')", "nope + 1", "abs_x if x else 1"):
        try:
            jev_harness.eval_field_expr(bad, values)
            check(False, f"{bad!r} is refused", "it was evaluated")
        except jev_harness.RamMapError:
            check(True, f"an expression that is not arithmetic is refused ({bad[:16]})")

    notes = Path(tmp) / "notes.json"
    notes.write_text(json.dumps({"game": "Mega Man 3", "verified": {
        "camera_scroll_x": {"source": "ppu", "evidence": "x"}}}), encoding="utf-8")
    try:
        jev_harness.RamMap.load(notes)
        check(False, "a notes-shaped map with no fields is refused", "it loaded")
    except jev_harness.RamMapError as error:
        check("field map" in str(error),
              "a map with no field map is refused with a reason", str(error))
    notes.write_text(json.dumps({"game": "X", "verified": {
        "cam": {"address": "0x51"}, "lives": {"address": "0x76"}}}), encoding="utf-8")
    loaded = jev_harness.RamMap.load(notes)
    check(sorted(loaded.fields) == ["cam", "lives"],
          "field specs inside a `verified` section are found", str(sorted(loaded.fields)))
    check(loaded.progress == "cam", "a map without `progress` falls back to its first field")


def test_ram_map_reads_the_stage_sets_own_spelling(tmp):
    """Mega Man 3's map writes its addresses the way the stage set's notes do
    (`0027`), records a field that is not RAM at all (`ppu.frameCount` in the
    save state), and names no `progress` key. All three have to load."""
    ram = jev_harness.RamMap.load(HERE / "stages" / "mm3" / "ram-map.json")
    check(ram.progress == "abs_x",
          "the map's own progress field is found by name", str(ram.progress))
    check(ram.fields["abs_x"].address == 0x27 and ram.fields["screen_y"].address == 0x12,
          "a four-digit address with no `0x` is read as hex",
          str({name: ram.fields[name].address for name in ("abs_x", "screen_y")}))
    check(ram.fields["player_hp"].address == 0xA2,
          "and one with letters in it", str(ram.fields["player_hp"].address))
    check(ram.not_ram == ["state_frame"] and "state_frame" not in ram.fields,
          "a field the map records as not RAM is skipped and named, not refused",
          str(ram.not_ram))
    check(ram.screen is None and ram.screen_width == 256,
          "the map names no screen field yet, and that is not an error",
          str(ram.screen))

    spelled = Path(tmp) / "spelled.json"
    spelled.write_text(json.dumps({"fields": {
        "a": {"address": "0027"}, "b": {"address": "0x27"},
        "c": {"address": "0012"}, "d": {"address": 0x1F},
        "e": {"address": None, "note": "a save-state key, not RAM"}}}), encoding="utf-8")
    loaded = jev_harness.RamMap.load(spelled)
    check([loaded.fields[name].address for name in ("a", "b", "c", "d")] ==
          [0x27, 0x27, 0x12, 0x1F],
          "both spellings and a plain number land on the same address",
          str({name: loaded.fields[name].address for name in ("a", "b", "c", "d")}))
    check(loaded.not_ram == ["e"], "the address-less field is named", str(loaded.not_ram))
    spelled.write_text(json.dumps({"fields": {"a": {"address": "zz"}}}), encoding="utf-8")
    try:
        jev_harness.RamMap.load(spelled)
        check(False, "an address that is not hex is refused", "it loaded")
    except jev_harness.RamMapError as error:
        check("zz" in str(error), "an address that is not hex is refused", str(error))


def test_tips_pin_a_value_and_honour_declared_run_keys(tmp):
    """A `when` clause may pin a value (`{"field": "stage", "value":
    "snake-man"}`) and may name a key the file declares under `runKeys` - a
    field that is not a RAM address at all. `scripts/stages/README.md` says both,
    and Mega Man 3's tips do both."""
    path = Path(tmp) / "tips.json"
    path.write_text(json.dumps({
        "runKeys": {"_comment": "not a field", "stage": "a run-level key"},
        "tips": [{"id": "pinned", "tip": "do the thing", "macro": "SHOOT",
                  "when": [{"field": "stage", "value": "snake-man"}]},
                 {"id": "numbered", "tip": "the numbered form", "macro": "SHOOT",
                  "when": [{"field": "stage", "value": 2}]},
                 {"id": "commented", "tip": "gated on a comment key", "macro": "SHOOT",
                  "when": [{"field": "_comment", "value": "x"}]}]}),
        encoding="utf-8")
    try:
        jev_harness.load_tips(path, fields={"abs_x"})
        check(False, "a field the ram map does not define and runKeys does not "
                     "declare is still refused", "it loaded")
    except jev_harness.TipsError as error:
        check("_comment" in str(error),
              "`runKeys` declares the run-level names and nothing else",
              str(error))
    path.write_text(json.dumps({
        "runKeys": {"stage": "a run-level key"},
        "tips": [{"id": "pinned", "tip": "do the thing", "macro": "SHOOT",
                  "when": [{"field": "stage", "value": "snake-man"}]},
                 {"id": "numbered", "tip": "the numbered form", "macro": "SHOOT",
                  "when": [{"field": "stage", "value": 2}]}]}), encoding="utf-8")
    loaded = jev_harness.load_tips(path, fields={"abs_x"})
    pinned, numbered = loaded.tips
    check(pinned.conditions[0].equals == "snake-man",
          "`value` pins the field the way `equals` does", str(pinned.conditions[0]))
    check(pinned.holds({"stage": "snake-man"}) and not pinned.holds({"stage": "gemini-man"}),
          "a string value compares as a string")
    check(not pinned.holds({"abs_x": 200}),
          "a state that does not carry the field never holds - never always-on")
    check(numbered.conditions[0].equals == 2 and numbered.holds({"stage": 2})
          and not numbered.holds({"stage": 3}),
          "a numeric value pins a number", str(numbered.conditions[0]))


def test_the_real_ninjagaiden_data_gates_as_the_file_says(tmp):
    """The game's own two files through the readers that consume them, so a
    schema drift fails here and not in an emulator run."""
    ram = jev_harness.RamMap.load(NG / "ram-map.json")
    tips = jev_harness.load_tips(NG / "jev-tips.json", fields=ram.fields)
    state = {"abs_x": PIN, "camera_x": 859, "hp": 16, "enemies_near": 0, "stage": 0}
    check([tip.id for tip in tips.matching(state)] == ["wall-pin"],
          "at the pin exactly the wall tip holds",
          str([tip.id for tip in tips.matching(state)]))
    for tip in tips.tips:
        check(all(condition.field in ram.fields for condition in tip.conditions),
              f"tip {tip.id} gates on fields this map reads",
              str([c.field for c in tip.conditions]))
    pin = next(tip for tip in tips.tips if tip.id == "wall-pin")
    check(pin.holds(state) and not pin.holds({**state, "abs_x": 500}),
          "the wall tip is off the wall when Ryu is off the wall")
    hurt = {**state, "hp": 8, "enemies_near": 1}
    check([tip.id for tip in tips.matching(hurt)] == ["wall-pin", "enemy-behind"],
          "the tip that needs damage taken holds only once it is taken",
          str([tip.id for tip in tips.matching(hurt)]))


def test_tips_schema_and_refusals(tmp):
    path = Path(tmp) / "tips.json"
    path.write_text(json.dumps({"tips": [
        {"id": "a", "tip": "do a thing", "when": {"abs_x": {"min": 1, "max": 2}}},
        {"id": "b", "tip": "always", "macro": "JUMP_LEFT"},
    ]}), encoding="utf-8")
    fields = jev_harness.RamMap.load(NG / "ram-map.json").fields
    loaded = jev_harness.load_tips(path, fields=fields)
    check(len(loaded.tips) == 2 and len(loaded.sha256) == 64,
          "a tips file loads and carries the hash the decision log records")
    check([tip.id for tip in loaded.matching({"abs_x": 2, "hp": 16})] == ["a", "b"],
          "a tip with no trigger always holds")
    check([tip.id for tip in loaded.matching({"abs_x": 9, "hp": 16})] == ["b"],
          "a compact range trigger is gated by the RAM")

    path.write_text(json.dumps({"tips": [{"id": "c", "tip": "x",
                                          "when": {"nosuchfield": {"min": 1}}}]}),
                    encoding="utf-8")
    try:
        jev_harness.load_tips(path, fields=fields)
        check(False, "a trigger on an unknown field is refused", "it loaded")
    except jev_harness.TipsError as error:
        check("nosuchfield" in str(error), "a trigger on an unknown field is refused",
              str(error))
    path.write_text(json.dumps({"tips": [{"id": "d", "when": {"abs_x": {"min": 1}}}]}),
                    encoding="utf-8")
    try:
        jev_harness.load_tips(path, fields=fields)
        check(False, "a tip with no text is refused", "it loaded")
    except jev_harness.TipsError:
        check(True, "a tip with no text is refused")
    check(jev_harness.load_tips(Path(tmp) / "missing.json") is not None,
          "a game with no tips file is a game with no tips")

    versions = jev_harness.load_tips(NG / "jev-tips.json", fields=fields)
    check(all(tip.sources for tip in versions.tips),
          "every shipped Ninja Gaiden tip cites a source",
          str([tip.id for tip in versions.tips if not tip.sources]))


# --------------------------------------------------------------------------
# the research helper
# --------------------------------------------------------------------------

def test_research_query_and_parsing(tmp):
    report = {"game": "Ninja Gaiden", "progress_field": "abs_x",
              "spot": {"position": 987, "state": {"abs_x": 987, "hp": 16}},
              "macros_available": {"RIGHT_15": "walk", "JUMP_LEFT_15": "hop"},
              "tried": {"10.0": [{"macro": "RIGHT_15", "reached": 987, "died": False}]},
              "stall_seconds": 5}
    query = jev_stall_research.build_query(report)
    check("Ninja Gaiden" in query and "987" in query,
          "the query names the game and the spot")
    check("RIGHT_15" in query and "JUMP_LEFT_15" in query,
          "the query names the macros the harness can play")
    check(".nes" not in query and "/Users/" not in query,
          "the query carries no ROM path and no pixels")

    fenced = ('here you go:\n```json\n{"tips": [{"id": "t", "tip": "hop"}], '
              '"macros": [], "query": "q"}\n```\n')
    check(jev_stall_research.extract_json(fenced)["tips"][0]["id"] == "t",
          "a fenced JSON answer is parsed")
    check(jev_stall_research.extract_json("no json here") is None,
          "an answer with no JSON object is refused")
    check(jev_stall_research.extract_json('{"unrelated": 1}') is None,
          "an object with none of the proposal keys is not an answer")

    argv = jev_stall_research.claude_argv()
    check(argv[argv.index("--allowedTools") + 1] == "WebSearch,WebFetch",
          "the worker gets web search and no other tool", " ".join(argv))
    check("-p" in argv and "--output-format" in argv,
          "the worker runs headless and answers JSON", " ".join(argv))

    calls = []

    def runner(argv_, input=None, capture_output=True, text=True, timeout=None):
        calls.append((argv_, input))
        payload = {"result": '{"tips": [], "macros": [{"name": "HOP", "buttons": "LA"}],'
                             ' "query": "q"}'}
        return subprocess.CompletedProcess(argv_, 0, json.dumps(payload), "")

    out = Path(tmp) / "research"
    proposals = jev_stall_research.research(report, out_dir=out, runner=runner)
    check(proposals["macros"][0]["name"] == "HOP",
          "a worker's answer becomes proposals", json.dumps(proposals))
    check((out / "proposals.json").exists() and (out / "query.txt").exists(),
          "the proposals and the query are kept under runs/")
    check(calls and "Ninja Gaiden" in calls[0][1] and "987" in calls[0][1],
          "the report goes in on stdin as data")
    check(jev_stall_research.build_query(report) in calls[0][1],
          "the query is the question; the report is the data under it")

    def failing(argv_, input=None, capture_output=True, text=True, timeout=None):
        return subprocess.CompletedProcess(argv_, 1, "", "boom")

    check(jev_stall_research.research(report, runner=failing).get("error"),
          "a worker that fails is an error, not empty advice")
    dry = jev_stall_research.research(report, dry_run=True)
    check(dry["dry_run"] and dry["prompt"], "--dry-run calls nothing and shows the query")


def main():
    tests = [
        test_base_search_and_script_format,
        test_macro_library_covers_the_stage_sets,
        test_route_macros_offer_the_searches_own_windows,
        test_a_multi_part_macro_is_played_part_by_part,
        test_tip_macro_escapes_the_pin,
        test_a_tips_move_is_matched_by_the_questions_own_name,
        test_tips_are_gated_by_their_trigger,
        test_failed_macro_withdrawn_and_ladder_monotone,
        test_checkpoint_floor_is_screen_start_or_last_progress,
        test_question_excludes_banned_and_carries_tips,
        test_loop_detectors,
        test_loop_guard_escalation,
        test_flat_watermark_detector,
        test_decisions_cap_stops_a_stall,
        test_budget_cap_stops_the_run,
        test_emulated_seconds_cap,
        test_ladder_exhausted_runs_one_research_pass_then_retries,
        test_research_merge_keeps_the_harness_duration,
        test_promote_tips_is_a_separate_step,
        test_cheat_validation,
        test_cheats_unconfirmed_reads_the_launch,
    test_stage_comes_from_the_flag_or_the_stage_set,
        test_main_refuses_without_a_key_and_on_a_bad_cheat,
        test_script_output_and_verify,
        test_the_flat_replay_is_what_gets_measured,
        test_ram_map_shapes,
        test_ram_map_reads_the_stage_sets_own_spelling,
        test_tips_pin_a_value_and_honour_declared_run_keys,
        test_the_real_ninjagaiden_data_gates_as_the_file_says,
        test_tips_schema_and_refusals,
        test_research_query_and_parsing,
    ]
    for test in tests:
        print(f"--- {test.__name__}")
        with tempfile.TemporaryDirectory(prefix="jev-harness-") as tmp:
            try:
                test(tmp)
            except Exception as error:  # noqa: BLE001 - a raised test is a failure
                import traceback
                traceback.print_exc()
                check(False, test.__name__, f"{type(error).__name__}: {error}")
    print(f"\n{len(_FAILURES)} failure(s)")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
