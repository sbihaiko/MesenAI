"""Headless suite for the Ninja Gaiden search's candidate set and beam rule
(`route_search.py`, F14.12/F14.13).

No ROM, no emulator, no session: the two things F14.13 added are decided
before a frame is played. The first is what a candidate *is* - a run of
(buttons, frames) parts that has to fill its window exactly, because the flat
script's frame accounting is what a replay depends on. The second is which
windows a hop keeps, and the case that matters is the one Act 1-1's x 988 pin
is: every candidate that moves Ryu forward ends on the same pixel, so the only
window that leaves is one that moved him *left* first, and a beam that kept
only the furthest windows would never hold it.

Run:  python3 scripts/test_route_search.py
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import route_search as R  # noqa: E402

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


def action_of(label):
    for name, action in R.CANDIDATES:
        if name == label:
            return action
    raise AssertionError(f"no candidate labelled {label}")


def frames_of(lines):
    return sum(int(line.split("f")[0]) for line in lines)


def main():
    # ---- what a candidate is ----
    for label, action in R.CANDIDATES:
        lines = R.window_lines(action, 29)
        check(frames_of(lines) == 29,
              f"{label}: the window is exactly 29 frames", lines)
        buttons = [line.split("f ")[1] for line in lines]
        check(buttons == [b for b, _c in action] or
              buttons == [b for b, _c in action] + ["-"],
              f"{label}: the parts play in order", lines)

    #The button the x 988 pin needs, and the wall techniques around it, with
    #the durations the search measures. F14.13's whole point is that these are
    #fixed in code and never chosen by a model (ADR-0238 section 3).
    check(action_of("LJ") == (("LA", None),),
          "JUMP_LEFT is a held Left+A", action_of("LJ"))
    check(action_of("WH") == (("LA", 4), ("RA", None)),
          "WALL_HOP jumps off the wall for 4 frames, then back at it",
          action_of("WH"))
    check(action_of("LJA") == (("LA", 4), ("A", None)),
          "LEDGE_JUMP is the same jump with A held through the window",
          action_of("LJA"))
    check(R.window_lines(action_of("WH"), 29) == ["4f LA", "25f RA"],
          "WALL_HOP's second part takes the rest of the window")
    check(R.flat_window(action_of("WH"), 29) == ["4f LA", "25f RA", "1f -"],
          "a wall hop in the flat script keeps the idle frame a boundary needs")

    #Up and Down are the crouch and the ladder, and Act 1-1 has both. No
    #candidate may hold either: a window that crouches is not a move forward.
    held = {button for _label, action in R.CANDIDATES
            for buttons, _count in action for button in buttons}
    check(not held & set("UD"),
          "no candidate holds Up or Down", sorted(held))

    #A chain of windows is the flat script's length, so the frame accounting
    #the docstring promises (120 hops x 30 = 3600 frames) has to hold for a
    #candidate set that is no longer one hold per window.
    chain = [line for _label, action in R.CANDIDATES[:3]
             for line in R.flat_window(action, 29)]
    check(frames_of(chain) == 3 * 30, "three windows are 90 frames", chain)

    #A window shorter than a wall hop's fixed frames drops that candidate
    #instead of crashing the hop that reaches it. (A hop's second part takes
    #whatever is left, so only the fixed first part can fail to fit.)
    playable, skipped = R.playable_candidates(R.CANDIDATES, 29)
    check(len(playable) == len(R.CANDIDATES) and not skipped,
          "at the search's own window every candidate is playable",
          (len(playable), skipped))
    playable, skipped = R.playable_candidates(R.CANDIDATES, 3)
    check([label for label, _a in playable] == list(R.SCRATCH_MACROS) + ["LJ"],
          "a 3-frame window drops the wall techniques, not the search",
          [label for label, _a in playable])
    check(skipped == ["WH", "WH5", "LJA"],
          "and it names which candidates it dropped", skipped)

    # ---- how a candidate window is written ----
    raises(lambda: R.window_lines((), 29), "an empty candidate is refused",
           "at least one part")
    raises(lambda: R.window_lines((("LA", None), ("RA", 4)), 29),
           "only the last part may hold to the end", "last part")
    raises(lambda: R.window_lines((("LA", 20), ("RA", 20)), 29),
           "parts that run past the window are refused", "past the")
    raises(lambda: R.window_lines((("LA", 0),), 29),
           "a part needs a frame count", "positive")
    raises(lambda: R.window_lines((("LX", 4),), 29),
           "a button the script does not know is refused", "not a button")
    check(R.window_lines((("LA", 4),), 29) == ["4f LA", "25f -"],
          "parts that do not fill the window are padded with the idle frame")

    # ---- the beam rule ----
    #`ranked` is [(slot, fingerprint, in_line)] best first; the slots stand in
    #for the parent a window was played from, and `in_line` says the window
    #continues a line an earlier diversity pick opened. The picks come back as
    #(index, opened), where `opened` is the pick that took the reserved slot.
    def picks(ranked, beam, occupied=()):
        return R.choose_beam(ranked, beam, occupied)

    same = "pin"
    forward = [(0, same, False), (0, same, False), (1, same, False),
               (1, same, False)]
    check(picks(forward, 3) == [(0, False), (1, False), (2, False)],
          "with nothing to diversify, the beam is the best by rank",
          picks(forward, 3))

    #The pin: the windows that moved Ryu forward all end on one fingerprint -
    #a dead end - and the window that jumped left ends on a new one, behind
    #them on rank. It has to take the reserved slot.
    dead_end = [(0, "pin", False), (0, "pin", False), (1, "pin", False),
                (1, "left", False)]
    check(picks(dead_end, 3) == [(0, False), (1, False), (3, True)],
          "a window that reached a new state takes the beam's last slot "
          "however low it ranks", picks(dead_end, 3))
    check(picks(dead_end, 2) == [(0, False), (3, True)],
          "the reserved slot is the beam's last one", picks(dead_end, 2))
    check(picks(dead_end, 1) == [(0, False)],
          "a one-wide beam has no slot to reserve", picks(dead_end, 1))

    #The wall climb: four windows that move Ryu only in y, so every one of
    #them ties on rank with the window that stands still, and the deepest of
    #them ranks last. The line the third window opened is what carries them.
    climb = [(0, "pin", False), (0, "pin", False), (0, "climb1", False),
             (2, "climb2", True)]
    check(picks(climb, 3) == [(0, False), (1, False), (3, True)],
          "a window continuing the opened line beats an earlier one on rank",
          picks(climb, 3))
    check(picks(climb, 2) == [(0, False), (3, True)],
          "a narrower beam keeps the line in its one reserved slot",
          picks(climb, 2))

    #The climb's own state is occupied: from it, `R` ends on that same state,
    #ranks ahead of the window that climbs, and would take the slot to go
    #nowhere. The window that moved is the one that continues the line.
    from_climb = [(0, "ground", False), (0, "ground", False),
                  (2, "climb", True), (2, "higher", True)]
    check(picks(from_climb, 3, occupied=["pin", "climb"])
          == [(0, False), (1, False), (3, True)],
          "a window that ends where the beam already stands takes no slot",
          picks(from_climb, 3, occupied=["pin", "climb"]))
    check(picks(from_climb, 3) == [(0, False), (1, False), (2, True)],
          "without that state occupied, the first window reaching a new "
          "fingerprint takes it", picks(from_climb, 3))

    #A fingerprint the beam already has does not take the slot; the newcomer
    #does.
    stale = [(0, "here", False), (0, "here", False), (0, "new", False),
             (1, "here", False)]
    check(picks(stale, 2) == [(0, False), (2, True)],
          "a fingerprint the beam already has does not take the slot",
          picks(stale, 2))

    #The beam never shrinks for diversity's sake: no new fingerprint, and the
    #slot falls back to the next window by rank.
    check(picks([(0, "a", False), (1, "a", False), (0, "a", False)], 2)
          == [(0, False), (1, False)],
          "with no new fingerprint the slot is the next by rank",
          picks([(0, "a", False), (1, "a", False), (0, "a", False)], 2))
    check(picks(forward, 9) == [(0, False), (1, False), (2, False), (3, False)],
          "a beam wider than the candidates keeps all of them",
          picks(forward, 9))
    check(picks([], 3) == [], "an empty hop keeps nothing")
    check(picks(forward, 0) == [], "a beam of zero keeps nothing")

    #Parents still matter: with more distinct parents than the beam is wide, a
    #second window from one parent does not take a slot from another.
    parents = [(0, "a", False), (0, "b", False), (1, "c", False),
               (2, "d", False), (3, "e", False)]
    check(picks(parents, 3) == [(0, False), (2, False), (1, True)],
          "different parents go through before a repeat, and the window that "
          "reached a further state takes the reserved slot",
          picks(parents, 3))

    # ---- the fingerprint ----
    here = dict(abs_x=992.0, cam=860.0, y=205, room=0, hp=0x0F)
    check(R.fingerprint(here) == R.fingerprint(dict(here, abs_x=999.0)),
          "positions within 8 px are one fingerprint")
    check(R.fingerprint(here) != R.fingerprint(dict(here, abs_x=1000.0)),
          "positions 8 px apart are two")
    check(R.fingerprint(here) != R.fingerprint(dict(here, hp=0x0E)),
          "a hit point is part of the identity")
    check(R.fingerprint(here) != R.fingerprint(dict(here, room=1)),
          "so is the room")

    print(f"\n{len(_FAILURES)} failure(s)")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
