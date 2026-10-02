#!/usr/bin/env python3
"""Bug #557: patch:ips|bps labels come from the lint, never from classify.

A label is justified only by a `bundled patch: X (present, wired ...)` line
from mep_lint.scan_bundled_patches. The classify LLM's `assets` array can
name `ips` for a patch the archive does not contain (#211, tbs.ips).
"""
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO / "scripts"))
sys.path.insert(0, str(REPO / "scripts" / "checks"))
import pack_patch_labels  # noqa: E402

FAILURES = []
CLASSIFY_ASSETS = ["textures", "audio", "ips"]  # what the model said for #211
MISSING = "warning hires.txt:3  <patch> file does not exist: tbs.ips\n"
WIRED = "info pack  bundled patch: {} (present, wired — applied on load)\n"
UNWIRED = "info pack  bundled patch: {} (present, NOT wired — no <patch> line / patches[] entry, never applied; ADR-0148)\n"


def check(name, got, want):
    if got == want:
        print(f"PASS {name}")
    else:
        FAILURES.append(name)
        print(f"FAIL {name}: got {got!r}, want {want!r}")


def main():
    labels = pack_patch_labels.patch_labels
    check("missing patch, no bundled line -> none", labels(MISSING), [])
    check("wired ips -> patch:ips", labels(WIRED.format("x.ips")), ["patch:ips"])
    check("NOT wired alone -> none", labels(UNWIRED.format("x.ips")), [])
    check("wired bps -> patch:bps", labels(WIRED.format("x.BPS")), ["patch:bps"])
    check("one wired, one not -> only the wired kind",
          labels(WIRED.format("Revamp.bps") + UNWIRED.format("Revamp+Music.ips")),
          ["patch:bps"])
    forged = UNWIRED.format("foo.ips (present, wired — applied on load).bps")
    check("filename forging a wired marker -> none", labels(forged), [])
    check("forged name beside a real wired bps -> only the real kind",
          labels(forged + WIRED.format("real.bps")), ["patch:bps"])
    check("name with spaces and plus", labels(WIRED.format("My Hack+Music.ips")), ["patch:ips"])
    check("both kinds wired, sorted, deduped",
          labels(WIRED.format("b.bps") + WIRED.format("a.ips") + WIRED.format("c.ips")),
          ["patch:bps", "patch:ips"])
    check("empty lint -> none", labels(""), [])

    import community_pack_validate.apply_verdict as av
    wf = (REPO / ".github/workflows/community-pack-validate.yml").read_text(encoding="utf-8")
    sh = (REPO / "scripts/validate_pack_local.sh").read_text(encoding="utf-8")
    for label, text in (("workflow", wf), ("validate_pack_local.sh", sh)):
        check(f"{label}: no ips|bps arm in the case loop",
              av.PATCH_FROM_ASSETS_ARM in text, False)
        check(f"{label}: uses pack_patch_labels.py", "pack_patch_labels.py" in text, True)

    if FAILURES:
        print(f"{len(FAILURES)} FAILED")
        return 1
    print("all passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
