#!/usr/bin/env python3
"""Tests for scripts/verify_community_install_from_zero.py's mirror of the
legacy HD pack extraction ceiling (#871).

The script is the "from-zero" harness that claims to mirror the client install
path (UI/Logic/LegacyHdPackInstall.cs, unit-tested in UI.Tests). If any stage of
its extraction is more permissive than the client, it reports success for a
pack the client refuses - which defeats the point of a mirror. The client
machine has three caps, and these tests exercise each one:

  * StageNestedArchive's DECLARED-size check on the wrapper's nested entry;
  * StageNestedArchive's read cap on the bytes actually delivered
    (_read_nested_capped / SizeCappedStream);
  * WriteUnderRoot's inflated-bytes ceiling on what is written under the pack
    root - the inner stage of a wrapper AND a plain non-nested pack.

Which halves these tests distinguish, honestly:

  * The two WriteUnderRoot tests (non-nested entries over the cap, and a nested
    wrapper whose INNER zip inflates over the cap) distinguish the
    inflated-bytes ceiling: the over-cap archive is refused with nothing
    written, and removing that ceiling makes both accept it (RED).
  * check_read_cap_refuses_when_driven_directly distinguishes the read cap, but
    only by calling _read_nested_capped directly. Through extract_legacy_pack it
    is UNREACHABLE: today's runtimes (the .NET ZipArchiveEntry stream and
    Python's zipfile alike) deliver at most the entry's declared Length, and the
    declared gate refuses any Length past the cap first - so no input reaches
    the cap by out-delivering the declared size. The function is kept for parity
    with the client's #860 / SizeCappedStream shape (a mirror must not look more
    permissive than the client), not because a fuzzable input can reach it.
  * The DECLARED-size gate is NOT distinguished by these tests: the
    over-the-cap nested case passes whether the refusal comes from the declared
    check or from the read cap, because for an honest header the two agree.
    Distinguishing it takes a crafted header whose declared Length exceeds the
    real payload (hand-patching the central-directory size field, since
    zipfile's writer only emits honest sizes) - deliberately not faked here.

The cap is injected, not hard-coded, so the tests drive it with a small fixture
instead of a real 2 GiB payload (UI/Logic/SizeCappedStream.cs + the FourArg
ExtractToFolder overload are the C# shape this follows).

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

def _inner_hd_pack(filler_bytes: int, compressible: bool = False) -> bytes:
    """A minimal legacy HD pack zip (root hires.txt). filler_bytes of
    incompressible data makes the inner zip's own size really exceed a small
    cap - the nested entry's declared size is the length of these bytes.
    compressible=True puts the filler in a highly compressible form instead, so
    the zip stays tiny (both nested gates pass) while big.png still inflates
    past a small cap - the reviewer's #871 repro."""
    payload = b"\x00" * filler_bytes if compressible else os.urandom(filler_bytes)
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as inner:
        inner.writestr("hires.txt", "<scale>4</scale>")
        if filler_bytes:
            inner.writestr("big.png", payload)
    return buf.getvalue()


def _wrapper(inner_bytes: bytes) -> bytes:
    """The "UnZipMeFirst" shape: one root-level nested zip, no root hires.txt,
    so extract_legacy_pack must unwrap it."""
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as outer:
        outer.writestr("Pack.zip", inner_bytes)
        outer.writestr("readme.txt", "wrapper")
    return buf.getvalue()


def _run(zip_bytes: bytes, rom_name: str, max_bytes: int, td: str):
    """Drive extract_legacy_pack against a temp zip and return the written
    relative paths; a refusal propagates as ValueError."""
    zip_path = Path(td) / "pack.zip"
    zip_path.write_bytes(zip_bytes)
    dest = Path(td) / "HdPacks" / rom_name
    verify.extract_legacy_pack(zip_path, dest, rom_name, max_bytes=max_bytes)
    return sorted(str(p.relative_to(dest)) for p in dest.rglob("*") if p.is_file())


def _assert_refused_naming_cap(run, label: str) -> bool:
    """Run `run()` expecting a ValueError that names the cap and is not the
    corruption message. Returns True on that refusal; records a failure and
    returns False for anything else (accept, wrong message, wrong type)."""
    try:
        run()
    except ValueError as exc:
        msg = str(exc)
        if "inflates past" not in msg or "1 MiB" not in msg:
            fail(f"{label}: cap refusal does not name the cap: {msg!r}")
            return False
        if "corrupt" in msg:
            fail(f"{label}: a size refusal was reported as corruption: {msg!r}")
            return False
        return True
    except Exception as exc:  # noqa: BLE001 - a wrong exception type is the failure
        fail(f"{label}: expected a ValueError naming the cap, got {type(exc).__name__}: {exc}")
        return False
    fail(f"{label}: over the injected cap was extracted instead of refused")
    return False


# ---------------------------------------------------------------------------
# Checks
# ---------------------------------------------------------------------------

def check_nested_entry_over_the_cap_is_refused():
    #The wrapper's nested entry declares ~3 MiB, over the injected 1 MiB cap.
    #Reading the whole entry (the pre-#860 behavior) then extracting must NOT be
    #the outcome. The declared gate refuses this before any byte is read.
    wrapper = _wrapper(_inner_hd_pack(3 * ONE_MIB))
    label = "nested entry over the cap"
    with tempfile.TemporaryDirectory() as td:
        dest = Path(td) / "HdPacks" / "Some Rom"
        if not _assert_refused_naming_cap(lambda: _run(wrapper, "Some Rom", ONE_MIB, td), label):
            return
        leftovers = list(dest.rglob("*")) if dest.exists() else []
        if leftovers:
            fail(f"{label}: refused install left files behind: {leftovers}")
            return
    ok(f"{label}: refused with a ValueError naming the cap, nothing written")


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


def check_non_nested_entries_over_the_cap_are_refused():
    #WriteUnderRoot's ceiling is NOT nested-only (LegacyHdPackInstall.cs:324):
    #a plain pack (root hires.txt, no wrapper) whose entries inflate past the
    #cap is refused by the client, so the mirror must refuse it too. Before
    #this fix _extract_under read every entry whole and wrote it - the same
    #gap the PR admitted for the non-nested path.
    pack = _inner_hd_pack(3 * ONE_MIB)
    label = "non-nested entries over the cap"
    with tempfile.TemporaryDirectory() as td:
        dest = Path(td) / "HdPacks" / "Some Rom"
        if not _assert_refused_naming_cap(lambda: _run(pack, "Some Rom", ONE_MIB, td), label):
            return
        leftovers = list(dest.rglob("*")) if dest.exists() else []
        if leftovers:
            fail(f"{label}: refused install left files behind: {leftovers}")
            return
    ok(f"{label}: refused with a ValueError naming the cap, nothing written")


def check_nested_inner_over_the_cap_is_refused():
    #The reviewer's #871 repro: the wrapper's nested entry is tiny (~3 KB, so
    #BOTH nested gates pass - declared size and capped read), but the inner
    #big.png inflates to 3 MiB. The client refuses this in WriteUnderRoot
    #(LegacyHdPackInstall.cs:213 -> ~324); before the fix the mirror wrote
    #3,145,744 bytes instead. It must refuse with nothing written.
    inner = _inner_hd_pack(3 * ONE_MIB, compressible=True)
    if len(inner) >= ONE_MIB:
        fail(f"fixture inner zip is not under the cap: {len(inner)}")
        return
    wrapper = _wrapper(inner)
    label = "nested inner over the cap"
    with tempfile.TemporaryDirectory() as td:
        dest = Path(td) / "HdPacks" / "Some Rom"
        if not _assert_refused_naming_cap(lambda: _run(wrapper, "Some Rom", ONE_MIB, td), label):
            return
        leftovers = list(dest.rglob("*")) if dest.exists() else []
        if leftovers:
            fail(f"{label}: refused install left files behind: {leftovers}")
            return
    ok(f"{label}: inner (not the wrapper) over the cap refused, nothing written")


def check_read_cap_refuses_when_driven_directly():
    #_read_nested_capped is unreachable through extract_legacy_pack's declared
    #gate under today's runtimes (see the module docstring). Driven directly
    #with a cap below the entry's true size it must still refuse, naming the
    #cap - the only way to distinguish this half. Remove the ceiling and it
    #returns the bytes, so this check goes RED.
    wrapper = _wrapper(_inner_hd_pack(3 * ONE_MIB))
    with tempfile.TemporaryDirectory() as td:
        zip_path = Path(td) / "pack.zip"
        zip_path.write_bytes(wrapper)
        with zipfile.ZipFile(zip_path) as outer:
            if outer.getinfo("Pack.zip").file_size <= ONE_MIB:
                fail("fixture nested entry is not over the cap")
                return
            try:
                verify._read_nested_capped(outer, "Pack.zip", ONE_MIB)
            except ValueError as exc:
                msg = str(exc)
                if "inflates past" not in msg or "1 MiB" not in msg:
                    fail(f"direct read cap did not name the cap: {msg!r}")
                    return
            except Exception as exc:  # noqa: BLE001
                fail(f"direct read cap raised {type(exc).__name__}, not a ValueError: {exc}")
                return
            else:
                fail("direct read cap returned the entry instead of refusing it")
                return
    ok("read cap driven directly: refuses a cap below the delivered size, naming the cap")


def main() -> int:
    check_nested_entry_over_the_cap_is_refused()
    check_nested_entry_under_the_cap_still_extracts()
    check_corrupt_nested_archive_is_not_a_size_bomb()
    check_non_nested_entries_over_the_cap_are_refused()
    check_nested_inner_over_the_cap_is_refused()
    check_read_cap_refuses_when_driven_directly()
    if FAILURES:
        print(f"\n{len(FAILURES)} failure(s)")
        return 1
    print("\nall verify_community_install_from_zero checks passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
