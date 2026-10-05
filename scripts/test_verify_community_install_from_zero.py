#!/usr/bin/env python3
"""Tests for scripts/verify_community_install_from_zero.py's mirror of the
legacy HD pack extraction ceiling (#871).

Scope: only the nested-archive branch. The script is the "from-zero" harness
that claims to mirror the client install path (UI/Logic/LegacyHdPackInstall.cs,
unit-tested in UI.Tests); if its nested read is more permissive than the
client, it reports success for a wrapper the client refuses - which defeats the
point of a mirror. The client rule (#860) is two halves:

  * the nested entry's DECLARED size is gated first (cheap refusal for a
    declared bomb), and
  * the copy then runs through a byte-counting reader that refuses the moment
    more than the cap is offered, so a lying local header is caught too.

The cap is injected, not hard-coded, so the test drives it with a small
fixture instead of a real 2 GiB payload (UI/Logic/SizeCappedStream.cs +
the FourArg ExtractToFolder overload are the C# shape this follows).

Usage: python3 scripts/test_verify_community_install_from_zero.py
"""
from __future__ import annotations

import io
import os
import sys
import tempfile
import zipfile
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPTS))
import verify_community_install_from_zero as verify  # noqa: E402

FAILURES: list[str] = []
ONE_MIB = 1024 * 1024


def fail(msg: str) -> None:
    FAILURES.append(msg)
    print(f"FAIL: {msg}")


def ok(msg: str) -> None:
    print(f"PASS: {msg}")


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------

def _inner_hd_pack(filler_bytes: int) -> bytes:
    """A minimal legacy HD pack zip (root hires.txt). filler_bytes of
    incompressible data makes the inner zip's own size really exceed a small
    cap - the nested entry's declared size is the length of these bytes."""
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as inner:
        inner.writestr("hires.txt", "<scale>4</scale>")
        if filler_bytes:
            inner.writestr("big.png", os.urandom(filler_bytes))
    return buf.getvalue()


def _wrapper(inner_bytes: bytes) -> bytes:
    """The "UnZipMeFirst" shape: one root-level nested zip, no root hires.txt,
    so extract_legacy_pack must unwrap it."""
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as outer:
        outer.writestr("Pack.zip", inner_bytes)
        outer.writestr("readme.txt", "wrapper")
    return buf.getvalue()


def _run(wrapper_bytes: bytes, rom_name: str, max_bytes: int, td: str):
    """Drive extract_legacy_pack against a temp zip and return the written
    relative paths; a refusal propagates as ValueError."""
    zip_path = Path(td) / "pack.zip"
    zip_path.write_bytes(wrapper_bytes)
    dest = Path(td) / "HdPacks" / rom_name
    verify.extract_legacy_pack(zip_path, dest, rom_name, max_bytes=max_bytes)
    return sorted(str(p.relative_to(dest)) for p in dest.rglob("*") if p.is_file())


# ---------------------------------------------------------------------------
# Checks
# ---------------------------------------------------------------------------

def check_nested_entry_over_the_cap_is_refused():
    #The nested entry declares ~3 MiB, over the injected 1 MiB cap. Reading the
    #whole entry (the pre-#860 behavior) then extracting must NOT be the outcome.
    wrapper = _wrapper(_inner_hd_pack(3 * ONE_MIB))
    with tempfile.TemporaryDirectory() as td:
        dest = Path(td) / "HdPacks" / "Some Rom"
        try:
            _run(wrapper, "Some Rom", ONE_MIB, td)
        except ValueError as exc:
            msg = str(exc)
            if "inflates past" not in msg or "1 MiB" not in msg:
                fail(f"cap refusal does not name the cap: {msg!r}")
                return
            if "corrupt" in msg:
                fail(f"a size refusal was reported as corruption: {msg!r}")
                return
        except Exception as exc:  # noqa: BLE001 - a wrong exception type is the failure
            fail(f"expected a ValueError naming the cap, got {type(exc).__name__}: {exc}")
            return
        else:
            fail("nested entry over the injected cap was extracted instead of refused")
            return
        leftovers = list(dest.rglob("*")) if dest.exists() else []
        if leftovers:
            fail(f"refused install left files behind: {leftovers}")
            return
    ok("nested entry over the cap: refused with a ValueError naming the cap, nothing written")


def check_nested_entry_under_the_cap_still_extracts():
    #The gate must not be over-eager: a small inner pack under a 4 MiB cap
    #still unwraps and writes the pack root.
    wrapper = _wrapper(_inner_hd_pack(0))
    with tempfile.TemporaryDirectory() as td:
        written = _run(wrapper, "Some Rom", 4 * ONE_MIB, td)
    if "hires.txt" not in written:
        fail(f"under-cap nested pack did not extract its pack root: {written}")
        return
    ok("nested entry under the cap still unwraps and writes the pack root")


def check_corrupt_nested_archive_is_not_a_size_bomb():
    #A truncated nested zip is corrupt, not an oversized bomb; the two must not
    #be reported as the same thing (a reader sent hunting an attacker who is
    #not there). Its declared size is under the cap, so the read runs and the
    #inner archive fails to parse.
    corrupt = _inner_hd_pack(0)[:-20]
    wrapper = _wrapper(corrupt)
    with tempfile.TemporaryDirectory() as td:
        try:
            _run(wrapper, "Some Rom", ONE_MIB, td)
        except ValueError as exc:
            msg = str(exc)
            if "inflates past" in msg:
                fail(f"a corrupt nested archive was reported as a size bomb: {msg!r}")
                return
            if "corrupt" not in msg:
                fail(f"corrupt nested archive gave an unexpected message: {msg!r}")
                return
        except Exception as exc:  # noqa: BLE001
            fail(f"expected a ValueError about corruption, got {type(exc).__name__}: {exc}")
            return
        else:
            fail("a corrupt nested archive was extracted instead of refused")
            return
    ok("corrupt nested archive: refused, and not reported as a size bomb")


def main() -> int:
    check_nested_entry_over_the_cap_is_refused()
    check_nested_entry_under_the_cap_still_extracts()
    check_corrupt_nested_archive_is_not_a_size_bomb()
    if FAILURES:
        print(f"\n{len(FAILURES)} failure(s)")
        return 1
    print("\nall verify_community_install_from_zero checks passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
