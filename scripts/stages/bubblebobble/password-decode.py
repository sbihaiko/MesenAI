#!/usr/bin/env python3
"""The round a Bubble Bobble (NES) password arms, read off the ROM's own code.

    python3 scripts/stages/bubblebobble/password-decode.py BBAAB [MORE...]
    python3 scripts/stages/bubblebobble/password-decode.py --self-check

ADR-0239 section 4 asks a session to prove where it went, and this is the other
half of that proof: `password-entry.py` writes the keypresses that type a
password, and this reads back what the ROM's validator will make of them. The
two are checked against each other by `--self-check`, which is what keeps a
value's label from drifting away from the bytes a session actually plays.

THE ROUTINE. The password screen's submit runs the validator at $FE04, and the
accepted branch ($FEF3-$FF61) turns the five letters into a round in three
steps: each letter becomes an index through the ten-byte table at $FDA1
(`0B 0A 12 0F 13 0C 10 0E 0D 11`), and then

    round = (p1 << 4) | ((p1 ^ p2) << 1) | ((p2 ^ p3) & 1)

over the first three of those indices. `ORA`, not `+`: the three fields overlap
bit 4, so HBGBD is 146 and not 162. The same branch writes the round to both
$049C and $0401, stores $0505 & 3 and $0505 >> 2 as the round's two halves, and
rings sound $0D.

WHY THE THIRD INDEX IS NOT THE THIRD LETTER. p1, p2 and p3 are the indices of
the first three *letters*, and the fourth and fifth letters are not in the
arithmetic at all - they are what makes a password unique to a round, and the
table at $FDA1 is what spreads them across the four bits the three terms can
reach. So the alphabet the manual documents (A-J) is exactly the ten values
$FDA1 permutes.

THIS IS THE ROUND OF A PASSWORD THE VALIDATOR ACCEPTS, NOT AN ACCEPTANCE TEST.
The two are not the same, and the difference is measured: of ten probes of one
shape, `AAAA?` for ? = A..J (runs/f1418/bubblebobble/probe/accept5/), the ROM
accepts exactly one - AAAAB, which arms round 16 - while AAAAA computes the same
16 and is refused. So the last two letters carry a constraint this arithmetic
does not describe, and it is not "one string per round": round 1 accepts both
BBAAB and the Super BBAJI, and round 112 both EECJJ and EECFG. That is why the
set types published passwords and never a synthesised one.

Past the NES version's last round the round-to-level lookup then lands somewhere
else entirely, which is a property of the level table and not of this arithmetic
- see RESULT.md, "the five codes past the last round".
"""
import importlib.util
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent

# $FDA1, read as the index each letter becomes: A->1, B->0, I->2, F->3, J->4,
# C->5, G->6, E->7, D->8, H->9.
PERMUTATION = {"A": 1, "B": 0, "I": 2, "F": 3, "J": 4,
               "C": 5, "G": 6, "E": 7, "D": 8, "H": 9}


def decode(password: str) -> int:
    """The round $FEF3-$FF61 computes for `password`."""
    letters = password.strip().upper()
    if len(letters) != 5:
        raise ValueError(f"{password!r} is {len(letters)} letters, not 5")
    try:
        p = [PERMUTATION[c] for c in letters]
    except KeyError as exc:
        raise ValueError(
            f"{password!r}: {exc.args[0]!r} is outside the A-J the password "
            "screen offers ($FDA1 has ten entries)") from exc
    return (p[0] << 4) | ((p[0] ^ p[1]) << 1) | ((p[1] ^ p[2]) & 1)


def _generator():
    """`password-entry.py` itself, hyphen and all - one source for both halves."""
    spec = importlib.util.spec_from_file_location(
        "bubblebobble_password_entry", HERE / "password-entry.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def self_check() -> int:
    """Derive every round the set claims, from the passwords the set types.

    The claim being checked is the one that matters: for each value,
    `password-entry.py` types `password` for a script it labels `round`, and
    this module computes `round` from the password. They must agree, and a
    disagreement is a mislabelled value - an entry script that files a round's
    art under another round's name.
    """
    gen = _generator()
    bad = 0
    for name, password, round_no, label in gen.VALUES:
        got = decode(password)
        ok = got == round_no
        bad += 0 if ok else 1
        print(f"{name:12s} {password}  generator says round {round_no:3d} (level "
              f"{label}), $FEF3-$FF61 computes {got:3d}  "
              f"{'ok' if ok else 'MISMATCH'}")
    outside = [("GHCCB", 126), ("HBGBD", 146), ("HJFAB", 155), ("DDFFI", 129),
               ("EECJJ", 112), ("HCICD", 153)]
    print("\nCodes that are NOT in the set, and the round each one computes:")
    for password, want in outside:
        got = decode(password)
        bad += 0 if got == want else 1
        print(f"  {password} -> {got:3d}  {'ok' if got == want else 'MISMATCH'}")
    print(f"\n{len(gen.VALUES)} values + {len(outside)} outside codes, "
          f"{bad} mismatch(es)")
    return 1 if bad else 0


def main(argv: list) -> int:
    args = argv[1:]
    if not args:
        print(__doc__)
        return 1
    if args[0] == "--self-check":
        return self_check()
    bad = 0
    for password in args:
        try:
            print(f"{password.upper()} -> round {decode(password)}")
        except ValueError as exc:
            print(f"{password}: {exc}", file=sys.stderr)
            bad = 1
    return bad


if __name__ == "__main__":
    sys.exit(main(sys.argv))
