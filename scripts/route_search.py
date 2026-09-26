#!/usr/bin/env python3
"""Beam search over short input windows for Ninja Gaiden Act 1-1, on the
persistent step-mode session.

F14.12, ADR-0238 section 1. This is the search that produced
`scripts/stages/ninjagaiden/stage1-run.txt` (`runs/route-*/solve.py` in the
scratch tree), ported off the per-candidate process it used to pay for. The
port kept *what* it looks for: the scratch driver's eight macros, the same
29-frame window, the same ranking, the same flat script out - only the place
the frames are played changed, `step_emu.StepEmu` (one process for the whole
search, candidate states kept in it) instead of one `headless_record` launch
per candidate.

    python3 scripts/route_search.py --rom <rom> --state mint.mss \
        --work runs/ng-search --out runs/ng-search/route.txt \
        --hops 120 --chunk 29 --beam 3 --sessions 8

`--sessions N` runs the candidates in N emulator sessions at once, which is
what a search that ran the scratch driver at its default `--jobs 8` wants. One
session is cheaper per candidate than one launch (2.6x, measured) but it is one
process, so on its own it is slower than eight launches were. With the same
eight workers the session is ahead, and the route it finds is the same one:
`docs/validation/f1412-step-mode-emulator-2026-09-26.md` has both numbers and
the 15-hop comparison against the scratch search's own log.

Frame accounting, unchanged from the scratch driver: each hop is one window of
`--chunk` frames plus the idle frame a one-shot run adds past its script, so
`--hops` x (`--chunk` + 1) is the flat script's length - 120 x 30 = 3 600
frames, the batch's 60 s. The idle frame is what makes a save-state boundary
input-neutral (`scripts/stages/README.md`), and it is written into the flat
script and played in the chain (`StepEmu.play_window`) for the same reason it
was there before.

The chain plays it because a *flat* script is the artifact and the chain has to
be the same play as the script it writes. Before issue #543 the session's `ram`
read advanced the emulated frame by one, so the search played that boundary
frame without asking - which is why the committed routes carry `1f -` after
every window and why they were valid. With the read inert the chain is one
frame per window short of its own script unless it plays the frame itself, and
it does - `StepEmu.play_window` loads the window's lines plus that boundary
frame and asks for the window's own frames.

RAM map (public: TASVideos "Game Resources / NES / Ninja Gaiden" RAM Map, and
Data Crystal "Ninja Gaiden (NES)/RAM map"):

    $0076 Ryu's lives            $0065 Ryu's hit points (max $10)
    $0063 level timer seconds    $0062 level timer frames ($3C-0)
    $0084 Ryu's facing/state     $0085/$0086 Ryu x (fractions/pixels)
    $008A Ryu y                  $0050-$0052 level x position (the camera)
    $006D stage, $006E room      $0060/$0061 score
    $0400+i/$0460+i/$0490+i enemy id / x pixels / hp (i = 0..7)

Ryu's `$0086` is *screen*-relative - it stops at 128 once the camera follows
him - so the distance covered is `camera + $0086`. `$0052` is a *signed* high
byte: walking left of the level's start clamps the camera and the byte turns
$FF, which read unsigned would look like the far right of the level (TASVideos'
Lua script reads the word with `readwordsigned(0x51)` for the same reason).

F14.13 (ADR-0238 section 2) adds the macros the pin needs. Measured from the
state that ends on it (abs x 988, camera 860, y 205): `A` alone and `R+A` do
nothing at all - the state does not answer to a standing jump - `L` does
nothing, `L+A` jumps, and the way over the wall is to jump *off* it and come
back at it holding the jump: `L+A` for a few frames, then `R+A` to the end of
the window. That window, applied hop after hop, walks Ryu up the wall to
abs x 1068 and y 104 (`runs/f1413`, not versioned; the numbers are in
`docs/validation/f1413-ninjagaiden-search-2026-09-26.md`). A candidate is
therefore a *run* of parts, not one hold, and the durations are fixed here.
"""
import argparse
import contextlib
import sys
import threading
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import step_emu  # noqa: E402

FPS = 60.0988

#A candidate is a label and the window it plays: a run of (buttons, frames)
#parts, where a part with no frame count holds to the end of the window. Most
#candidates are one hold; the wall techniques cannot be, because a Ninja Gaiden
#jump is edge-triggered and a wall hop is two moves - off the wall, then back
#at it. Every candidate fills the same window, so a chain of them has a known
#frame count. No candidate holds Up or Down: Act 1-1 has a crouch and a ladder
#state and neither is a move this search wants to make.
CANDIDATES = [
    ("R", (("R", None),)),      # walk right
    ("RB", (("RB", None),)),    # walk right, sword out
    ("RJ", (("RA", None),)),    # running jump right
    ("RJB", (("RAB", None),)),  # running jump right, sword out
    ("B", (("B", None),)),      # sword, standing
    ("J", (("A", None),)),      # standing jump
    ("-", (("-", None),)),      # stand
    ("L", (("L", None),)),      # walk back left
    #F14.13, ADR-0238 section 2. The four the x 988 pin answers to, all with
    #fixed durations: 4 and 5 frames of jump-off are the two shapes measured to
    #walk Ryu up the wall, and the ledge jump is the same jump with the button
    #held through the window, which is what an edge-triggered jump needs to
    #fire again on landing.
    ("LJ", (("LA", None),)),             # JUMP_LEFT: the Left+A jump itself
    ("WH", (("LA", 4), ("RA", None))),   # WALL_HOP: off the wall, back at it
    ("WH5", (("LA", 5), ("RA", None))),  # the same hop, one frame later
    ("LJA", (("LA", 4), ("A", None))),   # LEDGE_JUMP: the jump, button held
]

#The eight the scratch driver searched with, in its own order. The port's
#fidelity check (`measure_step_emu.py --archive-log`) runs the search with
#exactly these: the archive's log is the eight-macro search, and a log compared
#against a different candidate set measures the difference between the two sets
#and not whether the port is faithful.
SCRATCH_MACROS = ("R", "RB", "RJ", "RJB", "B", "J", "-", "L")

#How long a line the diversity rule opened may be followed. The case it exists
#for is Act 1-1's wall climb: four windows that move Ryu only in y before the
#fifth clears the wall, and none of the four improves on abs x, so the line is
#the only thing that carries them. Eight leaves room for a longer climb while
#stopping a line that only ever hops in place from holding the slot for the
#rest of the search.
STICKY_HOPS = 8

#The named bytes the search judges a window on, and the two blocks they live in.
#One request covers both blocks whatever the window asked for, so a candidate
#costs one round trip for RAM and not one per address.
NAMED = [
    (0x3D, "forward"), (0x4E, "spawn_block"),
    (0x50, "xpos_sub"), (0x51, "xpos"), (0x52, "xpos_hi"),
    (0x60, "score_lo"), (0x61, "score_hi"), (0x62, "timer_f"), (0x63, "timer_s"),
    (0x65, "hp"), (0x6D, "stage"), (0x6E, "room"), (0x76, "lives"),
    (0x84, "state"), (0x85, "x_sub"), (0x86, "x"), (0x8A, "y"),
    (0xA2, "screen_sub"), (0xA3, "screen"), (0xAC, "xspeed_sub"), (0xAD, "xspeed"),
]
RAM_BLOCKS = ((0x3D, 0xAD), (0x400, 0x497))


def read_probes(emu):
    """The RAM facts a window is judged on, in one session round trip."""
    low, high = emu.read_ram(*RAM_BLOCKS)
    (lowStart, lowEnd), (highStart, _) = RAM_BLOCKS

    #The two blocks come back as two byte strings; one address lookup covers
    #both, so the named bytes and the enemy table read the same way.
    def byte(address):
        if address <= lowEnd:
            return low[address - lowStart]
        return high[address - highStart]

    p = {name: byte(address) for address, name in NAMED}
    p["enemies"] = [(i, byte(0x400 + i), byte(0x460 + i), byte(0x490 + i))
                    for i in range(8) if byte(0x400 + i)]
    #$0052 is signed (see the module docstring).
    hi = p["xpos_hi"] - 256 if p["xpos_hi"] >= 0x80 else p["xpos_hi"]
    p["cam"] = hi * 256 + p["xpos"] + p["xpos_sub"] / 256.0
    p["ryu_x"] = p["x"] + p["x_sub"] / 256.0
    p["abs_x"] = p["cam"] + p["ryu_x"]
    return p


def fmt(p):
    #`y` is in the line because a wall climb shows in nothing else: F14.13's
    #four climbed window ends read abs=987 with the same hit points, score and
    #state as the window that stands still, and differ only in how high Ryu is.
    return (f"abs={p['abs_x']:8.2f} y={p['y']:3d} hp={p['hp']:02X} "
            f"sc={p['score_hi'] * 256 + p['score_lo']:5d} "
            f"st={p['state']:02X} lives={p['lives']}")


def rank(p):
    """How far a window got, and how cleanly.

    Distance first (Ryu's own x resets to 128 every time the camera starts
    following, so the camera is what carries the progress); then hit points,
    then score, so that among windows that end within 16 px of each other the
    one that took no damage and connected wins."""
    score = p["score_hi"] * 256 + p["score_lo"]
    return (int(p["abs_x"]) // 16, p["hp"], score, p["lives"])


def window_lines(action, frames):
    """A candidate's window as the lines a script holds: one
    `<n>f <buttons>` per part, with the part that has no frame count taking
    whatever is left. Parts that do not fill the window are padded with the
    idle frame, so every candidate is exactly `frames` long and a chain of
    them has a known frame count."""
    parts = list(action)
    if not parts:
        raise ValueError("a candidate window needs at least one part")
    if any(count is None for _buttons, count in parts[:-1]):
        raise ValueError(
            f"only the last part of {action!r} may hold to the end of the window")
    lines, used = [], 0
    for buttons, count in parts:
        count = frames - used if count is None else count
        if not isinstance(count, int) or isinstance(count, bool) or count <= 0:
            raise ValueError(f"a part needs a positive frame count, got {count!r}")
        if used + count > frames:
            raise ValueError(
                f"the parts of {action!r} run past the {frames}-frame window")
        lines.append(step_emu.macro_line(count, buttons))
        used += count
    if used < frames:
        lines.append(step_emu.macro_line(frames - used, "-"))
    return lines


def flat_window(action, frames):
    """The window as it has to appear in the flat script: the action's own
    lines, then the boundary frame a one-shot run adds past every script. A
    save-state boundary is input-neutral only with that frame written in - and
    `StepEmu.play_window` plays the very same two pieces, so the chain and the
    script it writes are the same play."""
    return window_lines(action, frames) + [step_emu.IDLE_LINE]


def playable_candidates(candidates, chunk):
    """The candidates whose parts fit the window, and the labels of those that
    do not. A window shorter than a wall hop's fixed frames is a legitimate
    thing to try on another stage, and it should drop those candidates with a
    note rather than crash the first hop that reaches one."""
    playable, skipped = [], []
    for label, action in candidates:
        try:
            window_lines(action, chunk)
        except ValueError:
            skipped.append(label)
            continue
        playable.append((label, action))
    return playable, skipped


def fingerprint(p):
    """The coarse identity of a window's end state, which is what keeps a beam
    diverse: position rounded to 8 px, the camera's screen, Ryu's y, the room
    and his hit points. Two windows on one fingerprint are the same place in
    the same condition, however far apart their ranks."""
    return (int(p["abs_x"]) // 8, int(p["cam"]) // 8, p["y"] // 8, p["room"],
            p["hp"])


def choose_beam(ranked, beam, occupied=()):
    """Which windows a hop keeps, as (index into `ranked`, opened) pairs -
    best first, where `ranked` is [(slot, fingerprint, in_line)] already in
    rank order, `in_line` says the window continues a line the diversity rule
    opened on an earlier hop, and `occupied` is the fingerprints the beam is
    already standing on.

    Rank alone cannot get off a pin. Act 1-1's x 988 answers to a wall hop that
    moves Ryu only in *y* for four windows - abs x 987, 987, 987, 987, one
    16-px rank bucket, hit points and score untouched - and only the fifth
    window clears the wall at abs x 1000. Every one of those windows ties on
    rank with the window that stands still, and a tie goes to the window whose
    parent comes first, so nothing in the ranking can tell the climb from the
    pin: only the fingerprint (which reads y) and the lineage can.

    So: the best `beam - 1` windows go through on rank, preferring different
    parents so a dead end two hops out has a sibling to fall back on. The last
    slot is reserved, in three tries: a window that continues the line the beam
    already opened (its parent was that line's last pick, and the line is under
    `STICKY_HOPS` old), then any window that reached a fingerprint the beam does
    not have, then the next by rank, so the beam never shrinks. The pick that
    took the reserved slot comes back as `opened`, which is what keeps a
    non-improving line alive across the hops it needs.

    A window that ends on a fingerprint the beam already stands on is not a
    way out of anywhere, so it is not a candidate for the reserved slot: from
    the climb's own state, `R` ends on that same state, ranks ahead of the
    window that climbs, and would take the slot to go nowhere.
    """
    if beam <= 0 or not ranked:
        return []
    ranked = list(ranked)
    room = beam - 1 if beam > 1 else 1
    parents = {slot for slot, _fp, _line in ranked}
    keep, seen = [], set()
    for index, (slot, _fp, _line) in enumerate(ranked):
        if len(keep) >= room:
            break
        if slot in seen and len(parents) > beam:
            continue
        seen.add(slot)
        keep.append(index)

    fingerprints = {ranked[index][1] for index in keep}
    occupied = set(occupied)
    opened = None
    #A dead end is the case this exists for, and the rule is not narrowed to it:
    #every hop spends its last slot on a state the rest of the beam does not
    #have, so a climb that only changes y keeps one. Narrowing it to hops whose
    #rank picks are all one fingerprint keeps the port's fidelity check against
    #the scratch search's log at 14/15 hops instead of 12/15, and was measured
    #to climb the Act 1-1 wall two hops later and less reliably - the mechanism
    #is the point, and the log says what it costs
    #(docs/validation/f1413-ninjagaiden-search-2026-09-26.md).
    if len(keep) < beam:
        for in_line_only in (True, False):
            for index, (_slot, fp, in_line) in enumerate(ranked):
                if index in keep or fp in fingerprints or fp in occupied:
                    continue
                if in_line_only and not in_line:
                    continue
                keep.append(index)
                fingerprints.add(fp)
                opened = index
                break
            if opened is not None:
                break
    for index in range(len(ranked)):
        if len(keep) >= beam:
            break
        if index not in keep:
            keep.append(index)
    return [(index, index == opened) for index in keep]


def mirror(old_beam, keep, sessions, folder):
    """One beam for every session.

    A candidate is played in whichever session took its turn, so the handle a
    kept window ends on belongs to that session alone. The next hop wants to
    play its candidates anywhere, so each kept state is written out once and
    read into every session - a few kilobytes per hop, against the process a
    one-shot search paid per candidate. With one session there is nothing to
    copy and the beam stays entirely in memory.
    """
    if len(sessions) == 1:
        return [{"probes": probes, "lines": lines, "owner": 0, "sticky": sticky,
                 "handles": [handle]}
                for _, _, handle, probes, lines, sticky in keep]

    for entry in old_beam:
        for index, session in enumerate(sessions):
            session.drop(entry["handles"][index])

    folder.mkdir(parents=True, exist_ok=True)
    staged = []
    for index, (_slot, owner, handle, probes, lines, sticky) in enumerate(keep):
        path = folder / f"beam{index}.mss"
        sessions[owner].save_file(handle, path)
        sessions[owner].drop(handle)
        staged.append((probes, lines, path, sticky))

    beam = []
    try:
        for probes, lines, path, sticky in staged:
            beam.append({"probes": probes, "lines": lines, "owner": 0,
                         "sticky": sticky,
                         "handles": [session.load_file(path) for session in sessions]})
    finally:
        for __probes, __lines, path, __sticky in staged:
            path.unlink(missing_ok=True)
    return beam


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--rom", required=True)
    ap.add_argument("--state", required=True, help="the .mss the search starts from")
    ap.add_argument("--work", required=True, help="scratch folder for the session")
    ap.add_argument("--out", required=True, help="the flat script to write")
    ap.add_argument("--hops", type=int, default=120)
    ap.add_argument("--chunk", type=int, default=29)
    ap.add_argument("--beam", type=int, default=3)
    ap.add_argument("--sessions", type=int, default=1,
                    help="emulator sessions to play candidates in. 1 is the "
                         "archive's `--jobs 1`; a search that ran the archive "
                         "at its default 8 workers wants --sessions 8, which "
                         "is the same parallelism at roughly half the wall "
                         "clock per candidate, and never more than three "
                         "quarters - the log has the range, not a single "
                         "digit (docs/validation/"
                         "f1412-step-mode-emulator-2026-09-26.md)")
    ap.add_argument("--only", default=None, help="comma-separated labels")
    ap.add_argument("--log", default=None)
    ap.add_argument("--state-out", default=None,
                    help="write the best end state here, so a next run continues "
                         "the same chain")
    a = ap.parse_args(argv)

    cands = CANDIDATES
    if a.only:
        wanted = set(a.only.split(","))
        cands = [c for c in CANDIDATES if c[0] in wanted]
        if not cands:
            print(f"no candidate matches --only {a.only}", file=sys.stderr)
            return 1
    cands, too_long = playable_candidates(cands, a.chunk)
    if not cands:
        print(f"no candidate fits a {a.chunk}-frame window", file=sys.stderr)
        return 1

    #A search writes into a scratch tree it may be the first to use, so the
    #folders it was pointed at are made rather than assumed.
    for path in (a.out, a.log, a.state_out):
        if path:
            Path(path).parent.mkdir(parents=True, exist_ok=True)

    #Every session holds a copy of the current beam, so any candidate can be
    #played in any of them. `--sessions N` is how a search that used the
    #archive's `--jobs 8` gets the same parallelism back: see the note on
    #--sessions below.
    with contextlib.ExitStack() as stack:
        #The log lives as long as the run, so it is managed with the sessions
        #rather than closed by hand on the way out.
        log = stack.enter_context(open(a.log, "w")) if a.log else None

        def note(message):
            print(message, flush=True)
            if log:
                log.write(message + "\n")
                log.flush()

        if too_long:
            note(f"--chunk {a.chunk} is too short for: {', '.join(too_long)}")

        sessions = [stack.enter_context(
            step_emu.StepEmu(a.rom, work=Path(a.work) / f"session{i}"))
            for i in range(a.sessions)]

        #A beam entry is {probes, lines, owner, handles} - `handles[i]` is the
        #same state in sessions[i], kept in step by mirror() below. The start
        #state is loaded once per session; every candidate restores it, so no
        #hop reads or writes a state file. Its probes are read *after* the load
        #- read before, they describe the console's power-on frame and the
        #first hop would judge its candidates against a machine with no lives
        #and no position.
        start = Path(a.state)
        start_handles = [session.load_file(start) for session in sessions]
        first = read_probes(sessions[0])
        note(f"start {fmt(first)}")
        beam = [{"probes": first, "lines": [], "owner": 0, "sticky": 0,
                 "handles": start_handles}]
        taken = []

        for hop in range(a.hops):
            tasks = [(slot, label, action)
                     for slot in range(len(beam)) for label, action in cands]
            locks = [threading.Lock() for _ in sessions]

            #Bound as defaults, not closed over: `beam` and `locks` are rebound
            #every hop, and a closure reading them late is the classic way to
            #play one hop's candidates from another hop's states.
            def play(numbered, beam=beam, locks=locks):
                index, (slot, label, action) = numbered
                which = index % len(sessions)
                session = sessions[which]
                with locks[which]:
                    session.restore(beam[slot]["handles"][which])
                    #The window is the candidate's own parts, so a candidate
                    #can be more than one hold (see CANDIDATES). play_window
                    #plays the boundary frame too, which is what makes the hop
                    #the same play as the flat window it writes.
                    session.play_window(window_lines(action, a.chunk), a.chunk)
                    p = read_probes(session)
                    if p["lives"] < beam[slot]["probes"]["lives"] or p["lives"] == 0:
                        return None
                    return (rank(p), slot, label, action, session.save(), which, p)

            with ThreadPoolExecutor(max_workers=a.sessions) as pool:
                #map, not as_completed: the results come back in task order
                #whatever order they finished in, so the ranking below - and
                #with it a tie between two equally good windows - does not
                #depend on how many sessions were used.
                played = list(pool.map(play, enumerate(tasks)))
            ranked = sorted((r for r in played if r), key=lambda t: t[0],
                            reverse=True)

            note(f"hop {hop + 1:3d}: " + (" | ".join(
                f"{label} {fmt(p)}" for r, si, label, ac, h, oi, p in ranked[:4])
                or "STALL"))

            if not ranked:
                note("no window advanced - stopping")
                break

            #Which windows to keep is choose_beam's call: the best by rank,
            #plus one reserved for a state the rest of the beam does not have -
            #the rule that lets a window which moves *left* off a pin survive
            #the hop it was played in.
            #`in_line` is what carries a line the diversity rule opened into
            #the next hop, and it is why `STICKY_HOPS` is a limit: only a
            #window whose parent is still inside that budget may continue it.
            picked = choose_beam(
                [(slot, fingerprint(p),
                  beam[slot]["sticky"] > 0 and beam[slot]["sticky"] < STICKY_HOPS)
                 for _rank, slot, _l, _a, _h, _o, p in ranked], a.beam,
                occupied=[fingerprint(entry["probes"]) for entry in beam])
            keep = []
            for index, opened in picked:
                _rank, slot, _label, action, handle, owner, p = ranked[index]
                sticky = beam[slot]["sticky"] + 1 if opened else 0
                keep.append((slot, owner, handle, p,
                             beam[slot]["lines"] + flat_window(action, a.chunk),
                             sticky))
            keepers = {entry[2] for entry in keep}
            for _rank, _slot, _label, _action, handle, owner, _p in ranked:
                if handle not in keepers:
                    sessions[owner].drop(handle)

            beam = mirror(beam, keep, sessions, Path(a.work) / "beam")
            taken = beam[0]["lines"]

        Path(a.out).write_text("\n".join(taken) + "\n")
        frames = sum(int(line.split("f")[0]) for line in taken)
        note(f"flat script: {a.out} ({len(taken)} lines, {frames} frames, "
             f"{frames / FPS:.2f} s)")
        note(f"end {fmt(beam[0]['probes'])}")
        if a.state_out:
            sessions[0].save_file(beam[0]["handles"][0], a.state_out)
            note(f"end state: {a.state_out}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
