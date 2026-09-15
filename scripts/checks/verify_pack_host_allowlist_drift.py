#!/usr/bin/env python3
"""Fail when the community-pack host allow-list drifts across its three readers.

Phase 11 C.8 / ADR-0187 Consequences: `scripts/pack_host_allowlist.json` is
the host list, but each `kind` needs a handler in both `scripts/fetch_pack.py`
and `UI/Services/CommunityPackDownloader.cs`, and
`.github/workflows/community-pack-validate.yml` must gate downloads through
that JSON (not an inline host table). Drift shows up as CI accepting a pack
the client cannot fetch, or the reverse.

Usage:
  python3 scripts/checks/verify_pack_host_allowlist_drift.py
  python3 scripts/checks/verify_pack_host_allowlist_drift.py --repo <root>

Exit 0 when the three sides agree; exit 1 with one error line per drift.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

PY_KIND_RE = re.compile(r'entry\["kind"\]\s*==\s*"([^"]+)"')
CS_KIND_RE = re.compile(
    r'string\.Equals\(\s*matched\.Kind\s*,\s*"([^"]+)"',
    re.IGNORECASE,
)
# validate.yml must load the JSON for the allow-list gate (not a hand list).
YML_ALLOWLIST_MARKERS = (
    "pack_host_allowlist.json",
    "load_allowlist",
    "match_host",
)


def json_kinds(allowlist_path: Path) -> set[str]:
    data = json.loads(allowlist_path.read_text(encoding="utf-8"))
    hosts = data.get("hosts") or []
    kinds = set()
    for entry in hosts:
        if isinstance(entry, dict) and entry.get("kind"):
            kinds.add(str(entry["kind"]))
    return kinds


def py_special_kinds(fetch_pack_text: str) -> set[str]:
    return set(PY_KIND_RE.findall(fetch_pack_text))


def cs_special_kinds(downloader_text: str) -> set[str]:
    return set(CS_KIND_RE.findall(downloader_text))


def yml_uses_allowlist(validate_yml_text: str) -> list[str]:
    missing = [m for m in YML_ALLOWLIST_MARKERS if m not in validate_yml_text]
    return missing


def check_texts(
    allowlist_kinds: set[str],
    py_kinds: set[str],
    cs_kinds: set[str],
    yml_text: str,
) -> list[str]:
    """Pure drift rules — used by the live check and by fixture tests."""
    errors = []
    if py_kinds != cs_kinds:
        only_py = sorted(py_kinds - cs_kinds)
        only_cs = sorted(cs_kinds - py_kinds)
        errors.append(
            "fetch_pack.py and CommunityPackDownloader.cs disagree on special "
            f"kinds: only-python={only_py} only-csharp={only_cs}"
        )
    special = allowlist_kinds - {"direct"}
    missing_py = sorted(special - py_kinds)
    missing_cs = sorted(special - cs_kinds)
    if missing_py:
        errors.append(
            f"pack_host_allowlist.json has kind(s) fetch_pack.py does not "
            f"dispatch: {missing_py}"
        )
    if missing_cs:
        errors.append(
            f"pack_host_allowlist.json has kind(s) CommunityPackDownloader.cs "
            f"does not dispatch: {missing_cs}"
        )
    for marker in yml_uses_allowlist(yml_text):
        errors.append(
            f"community-pack-validate.yml is missing allow-list marker {marker!r} "
            f"— the gate must load scripts/pack_host_allowlist.json via fetch_pack"
        )
    return errors


def check_repo(repo: Path) -> list[str]:
    allowlist = repo / "scripts" / "pack_host_allowlist.json"
    fetch_pack = repo / "scripts" / "fetch_pack.py"
    downloader = repo / "UI" / "Services" / "CommunityPackDownloader.cs"
    validate = repo / ".github" / "workflows" / "community-pack-validate.yml"
    for path in (allowlist, fetch_pack, downloader, validate):
        if not path.is_file():
            return [f"missing required file: {path.relative_to(repo)}"]
    return check_texts(
        json_kinds(allowlist),
        py_special_kinds(fetch_pack.read_text(encoding="utf-8")),
        cs_special_kinds(downloader.read_text(encoding="utf-8")),
        validate.read_text(encoding="utf-8"),
    )


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument(
        "--repo",
        type=Path,
        default=Path(__file__).resolve().parents[2],
        help="repository root (default: two levels above this script)",
    )
    args = p.parse_args(argv)
    errors = check_repo(args.repo.resolve())
    if errors:
        for err in errors:
            print(f"FAIL: {err}", file=sys.stderr)
        return 1
    print(
        "PASS: pack host allow-list kinds agree across fetch_pack.py, "
        "CommunityPackDownloader.cs, and community-pack-validate.yml"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
