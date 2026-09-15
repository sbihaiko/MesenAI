#!/usr/bin/env python3
"""Acceptance for scripts/checks/verify_pack_host_allowlist_drift.py (Phase 11 C.8).

Drives the shipped check_texts() / check_repo() — not a re-implementation.
"""
from __future__ import annotations

import json
import sys
import tempfile
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO / "scripts" / "checks"))
import verify_pack_host_allowlist_drift as drift  # noqa: E402

FAILED = 0


def ok(msg: str) -> None:
    print(f"PASS: {msg}")


def fail(msg: str) -> None:
    global FAILED
    FAILED = 1
    print(f"FAIL: {msg}")


def main() -> int:
    # Live repo must pass.
    live = drift.check_repo(REPO)
    if not live:
        ok("live repo: fetch_pack, CommunityPackDownloader, validate.yml agree")
    else:
        fail(f"live repo drifted: {live}")

    # Deliberate kind mismatch must fail.
    errs = drift.check_texts(
        allowlist_kinds={"direct", "dropbox", "mega"},
        py_kinds={"dropbox", "mega", "mediafire"},
        cs_kinds={"dropbox", "mega"},
        yml_text="pack_host_allowlist.json\nload_allowlist\nmatch_host\n",
    )
    if any("disagree on special kinds" in e for e in errs) and any(
        "mediafire" in e or "does not dispatch" in e for e in errs
    ):
        ok("deliberate python/csharp kind mismatch fails the check")
    else:
        fail(f"mismatch was not reported: {errs}")

    # JSON kind without a handler fails.
    errs = drift.check_texts(
        allowlist_kinds={"direct", "newhost"},
        py_kinds={"dropbox"},
        cs_kinds={"dropbox"},
        yml_text="pack_host_allowlist.json\nload_allowlist\nmatch_host\n",
    )
    if any("newhost" in e for e in errs):
        ok("allow-list kind with no handler fails the check")
    else:
        fail(f"unhandled JSON kind was not reported: {errs}")

    # validate.yml without the allow-list markers fails.
    errs = drift.check_texts(
        allowlist_kinds={"direct", "dropbox"},
        py_kinds={"dropbox"},
        cs_kinds={"dropbox"},
        yml_text="# no allow-list markers here\n",
    )
    if len(errs) >= 3 and all(
        any(m in e for e in errs) for m in drift.YML_ALLOWLIST_MARKERS
    ):
        ok("validate.yml missing allow-list markers fails the check")
    else:
        fail(f"yml marker gaps were not reported: {errs}")

    # Agreement passes.
    errs = drift.check_texts(
        allowlist_kinds={"direct", "dropbox", "mega"},
        py_kinds={"dropbox", "mega"},
        cs_kinds={"dropbox", "mega"},
        yml_text="pack_host_allowlist.json\nload_allowlist\nmatch_host\n",
    )
    if not errs:
        ok("matching kinds and yml markers pass")
    else:
        fail(f"clean fixture failed: {errs}")

    # End-to-end through main() on a temp repo that mirrors the live shape.
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        (root / "scripts").mkdir()
        (root / "UI" / "Services").mkdir(parents=True)
        (root / ".github" / "workflows").mkdir(parents=True)
        (root / "scripts" / "pack_host_allowlist.json").write_text(
            json.dumps(
                {
                    "hosts": [
                        {"host": "example.com", "kind": "direct"},
                        {"host": "www.dropbox.com", "kind": "dropbox"},
                    ]
                }
            ),
            encoding="utf-8",
        )
        (root / "scripts" / "fetch_pack.py").write_text(
            'if entry["kind"] == "dropbox":\n    pass\n', encoding="utf-8"
        )
        (root / "UI" / "Services" / "CommunityPackDownloader.cs").write_text(
            'if(string.Equals(matched.Kind, "dropbox", StringComparison.OrdinalIgnoreCase)) {\n}\n',
            encoding="utf-8",
        )
        (root / ".github" / "workflows" / "community-pack-validate.yml").write_text(
            "pack_host_allowlist.json\nload_allowlist\nmatch_host\n",
            encoding="utf-8",
        )
        rc = drift.main(["--repo", str(root)])
        if rc == 0:
            ok("main() exits 0 on an agreeing temp repo")
        else:
            fail(f"main() exited {rc} on an agreeing temp repo")

        # Break C#: remove dropbox handler.
        (root / "UI" / "Services" / "CommunityPackDownloader.cs").write_text(
            "// no handlers\n", encoding="utf-8"
        )
        rc = drift.main(["--repo", str(root)])
        if rc == 1:
            ok("main() exits 1 when CommunityPackDownloader drifts")
        else:
            fail(f"main() exited {rc} on a drifted temp repo (want 1)")

    return 1 if FAILED else 0


if __name__ == "__main__":
    sys.exit(main())
