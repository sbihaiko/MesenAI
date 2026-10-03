#!/usr/bin/env python3
"""Bug #677: assets:textures|audio|external follow the latest pass both ways.

The apply-verdict loop only ever `--add-label`ed the assets:* labels, so a
revalidated pack that dropped its audio kept `assets:audio`. patch:* got the
add/remove loop in #557; assets:* now mirror it: scripts/pack_asset_labels.py
names the labels this pass justifies, and the workflow and the local harness
add those and remove the rest.

Usage: python3 scripts/test_pack_asset_labels.py
"""
import json
import re
import sys
import tempfile
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO / "scripts"))

FAILURES = []
ALL = ("assets:textures", "assets:audio", "assets:external")


def check(name, got, want):
    if got == want:
        print(f"PASS {name}")
    else:
        FAILURES.append(name)
        print(f"FAIL {name}: got {got!r}, want {want!r}")


def unit_checks():
    try:
        import pack_asset_labels as pal
    except ImportError as exc:
        check("scripts/pack_asset_labels.py exists", str(exc), "")
        return
    labels = pal.asset_labels
    check("textures+audio", labels({"assets": ["textures", "audio"]}, False), ["assets:audio", "assets:textures"])
    check("audio dropped on revalidation -> textures only", labels({"assets": ["textures"]}, False),
          ["assets:textures"])
    check("patch/synth kinds earn no assets:* label", labels({"assets": ["ips", "bps", "synth"]}, False), [])
    check("external comes from the recipe deps flag", labels({"assets": ["textures"]}, True),
          ["assets:external", "assets:textures"])
    check("no assets key -> none", labels({}, False), [])
    check("non-list assets -> none", labels({"assets": "audio"}, False), [])
    check("non-string members ignored", labels({"assets": [1, None, "audio"]}, False), ["assets:audio"])
    check("ALL_LABELS is the three-label universe", tuple(pal.ALL_LABELS), ALL)
    with tempfile.TemporaryDirectory() as tmp:
        path = Path(tmp) / "c.json"
        path.write_text(json.dumps({"assets": ["audio", "textures"]}), encoding="utf-8")
        import io
        import contextlib
        buf = io.StringIO()
        with contextlib.redirect_stdout(buf):
            rc = pal.main(["pack_asset_labels.py", str(path), "--external"])
        check("CLI prints the labels on one line", (rc, buf.getvalue()),
              (0, "assets:audio assets:external assets:textures\n"))


def loop_checks(label, text):
    check(f"{label}: uses pack_asset_labels.py", "pack_asset_labels.py" in text, True)
    check(f"{label}: loops over all three assets:* labels",
          "for L in assets:textures assets:audio assets:external; do" in text, True)
    loop = text.split("for L in assets:textures assets:audio assets:external; do", 1)[-1].split("done", 1)[0]
    check(f"{label}: the loop removes the labels this pass does not justify",
          re.search(r'--remove-label "\$L"', loop) is not None, True)
    check(f"{label}: no add-only assets:* arm left",
          re.search(r'(textures\|audio|external)\) L="assets:', text) is not None, False)


def main():
    unit_checks()
    loop_checks("workflow", (REPO / ".github/workflows/community-pack-validate.yml").read_text(encoding="utf-8"))
    loop_checks("validate_pack_local.sh", (REPO / "scripts/validate_pack_local.sh").read_text(encoding="utf-8"))
    if FAILURES:
        print(f"{len(FAILURES)} FAILED")
        return 1
    print("all passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
