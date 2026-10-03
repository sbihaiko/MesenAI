"""Headless suite for Remaster's *Build & show* (`mep_project.py build`, slice G.6).

The build rebuilds the project's `mep/` from the newest recording and its kit,
through a staging copy, and only a clean build reaches `mep/`. A `mep/` the
build did not write (an installed pack, ADR-0147) is refused. The last line
tells the GUI whether the loaded pack can re-decode its images in place
(`show: images`, ADR-0212) or the ROM must be reopened (`show: reload`).

Run:  python3 scripts/test_mep_project_build.py
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import mep_project_build as PB  # noqa: E402
import test_mep_build as B  # noqa: E402 — the ADR-0153 author-folder fixture, one definition

_FAILURES = []


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


def make_project(root: Path) -> Path:
    """A project with one recording (`auto/rec-001/`, the fixture's sheets and
    manifest) and a kit holding a copy of its metatile sheet."""
    folder, _v, _c = B.make_sheet_folder(root, "fixture")
    project = root / "Game"
    rec = project / "auto" / "rec-001"
    rec.mkdir(parents=True)
    shutil.copytree(folder / "textures", rec / "textures")
    (rec / "textures" / "old.png").write_bytes(B.png(8, 8))  # the fixture's legacy <img>
    kit = project / "kit" / "rec-001" / "sheets"
    kit.mkdir(parents=True)
    for name in ("metatiles.png", "metatiles.orig.png", "metatiles.json"):
        shutil.copy2(rec / "textures" / "sheets" / name, kit / name)
    return project


def paint(png: Path, rgba) -> None:
    """Repaint the first opaque pixel of `png` (B.png_read/png_rgba round trip)."""
    rows = B.png_read(png)
    argb = (rgba[3] << 24) | (rgba[0] << 16) | (rgba[1] << 8) | rgba[2]
    for y, row in enumerate(rows):
        for x, px in enumerate(row):
            if px >> 24:
                rows[y][x] = argb
                png.write_bytes(B.png_rgba(rows))
                return
    raise AssertionError(f"{png} has no opaque pixel")


def build(project: Path):
    out = subprocess.run([sys.executable, str(HERE / "mep_project.py"), "build", str(project)],
                         capture_output=True, text=True)
    return out.returncode, out.stdout + out.stderr


def show_line(out: str) -> str:
    lines = [ln for ln in out.splitlines() if ln.startswith("show: ")]
    return lines[-1] if lines else ""


def test_a_clean_build_writes_mep_and_says_the_rom_must_be_reopened():
    with tempfile.TemporaryDirectory() as td:
        project = make_project(Path(td))
        rc, out = build(project)
        check(rc == 0, "an unpainted project builds", out[-600:])
        check((project / "mep" / "textures" / "hires.txt").is_file(), "mep/ holds the built manifest")
        check(PB.stamp_path(project).is_file(), "mep/ carries the build stamp")
        check(json.loads(PB.stamp_path(project).read_text())["recording"] == "rec-001",
              "the stamp names the recording it was built from")
        check(show_line(out) == "show: reload", "a new mep/ needs the ROM reopened", show_line(out))
        check(not (project / PB.STAGE).exists(), "the staging copy is gone after a clean build")
        steps = [ln for ln in out.splitlines() if ln.startswith(("ok   ", "FAIL "))]
        check(steps == ["ok   copy", "ok   build", "ok   figures", "ok   check"],
              "the four steps the job card counts, in order", str(steps))
        check("steps: 4" in out.splitlines() and "recording: rec-001" in out.splitlines(),
              "the step total and the recording come first")


def test_a_repaint_that_keeps_the_manifest_reloads_images_only():
    with tempfile.TemporaryDirectory() as td:
        project = make_project(Path(td))
        sheet = project / "kit" / "rec-001" / "sheets" / "metatiles.png"
        paint(sheet, (0x10, 0x20, 0x30, 0xFF))
        rc, out = build(project)
        check(rc == 0, "a painted project builds", out[-600:])
        before = {p: p.stat().st_mtime_ns for p in (project / "mep").rglob("*") if p.is_file()}
        manifest = (project / "mep" / "textures" / "hires.txt").read_bytes()
        rc, out = build(project)
        after = {p: p.stat().st_mtime_ns for p in (project / "mep").rglob("*") if p.is_file() and p.name != PB.STAMP}
        check(rc == 0 and show_line(out) == "show: images", "an unchanged rebuild only reloads images", show_line(out))
        check(all(before[p] == m for p, m in after.items()), "an unchanged rebuild rewrites no file of mep/")
        paint(sheet, (0x40, 0x50, 0x60, 0xFF))
        rc, out = build(project)
        check(rc == 0 and show_line(out) == "show: images", "repainting the same cell reloads images only", out[-600:])
        check((project / "mep" / "textures" / "hires.txt").read_bytes() == manifest, "the manifest did not move")
        check(B.png_read(project / "mep" / "textures" / "sheets" / "metatiles.png")
              == B.png_read(sheet), "the repainted sheet reached mep/")


def test_a_failed_build_keeps_the_last_good_mep():
    with tempfile.TemporaryDirectory() as td:
        project = make_project(Path(td))
        rc, _ = build(project)
        good = (project / "mep" / "textures" / "hires.txt").read_bytes()
        sheet = project / "kit" / "rec-001" / "sheets" / "metatiles.png"
        sheet.write_bytes(B.png(5, 3))  # resized: no longer a whole multiple of the sheet
        rc, out = build(project)
        check(rc == 1, "a resized sheet fails the build", out[-600:])
        check("FAIL build" in out.splitlines(), "the failing step is named")
        check("error" in out and "metatiles.png" in out, "the error names the sheet", out[-600:])
        check((project / "mep" / "textures" / "hires.txt").read_bytes() == good, "mep/ is still the last good build")
        check(not (project / PB.STAGE).exists(), "a failed build leaves no staging copy")
        check("show: " not in out, "a failed build shows nothing in game")


def test_a_mep_this_build_did_not_write_is_refused_and_untouched():
    with tempfile.TemporaryDirectory() as td:
        project = make_project(Path(td))
        mep = project / "mep"
        (mep / "textures").mkdir(parents=True)
        (mep / ".mep-install.json").write_text("{}")
        (mep / "textures" / "hires.txt").write_text("<ver>106\n")
        rc, out = build(project)
        check(rc == 2, "a foreign mep/ is refused", out[-400:])
        check("installed pack" in out, "the refusal says it is an installed pack", out[-400:])
        check((mep / "textures" / "hires.txt").read_text() == "<ver>106\n" and not PB.stamp_path(project).exists(),
              "the installed pack is untouched")
        check("steps: " not in out, "a refused build starts no step")


def test_nothing_recorded_is_refused():
    with tempfile.TemporaryDirectory() as td:
        project = Path(td) / "Empty"
        (project / "auto").mkdir(parents=True)
        rc, out = build(project)
        check(rc == 2 and "no recording with textures" in out, "a project without a textured recording is refused", out)


def test_pages_join_only_the_recording_they_were_made_for():
    with tempfile.TemporaryDirectory() as td:
        project = make_project(Path(td))
        rec = PB.pick_recording(project)
        pages = project / "kit" / "pages"
        (pages / "chr").mkdir(parents=True)
        (pages / "chr" / "Chr_0.png").write_bytes(B.png(8, 8))
        (pages / "kit-part-chr.json").write_text(json.dumps({"pack": str(project / "auto" / "rec-002")}))
        check(not PB.pages_are_for(pages, rec), "pages made for another recording are not this build's")
        stage = Path(td) / "stage"
        counts = PB.stage_copy(project, rec, stage)
        check(counts["pages"] == 0, "so they are not copied", str(counts))
        (pages / "kit-part-chr.json").write_text(json.dumps({"pack": str(rec.path)}))
        check(PB.pages_are_for(pages, rec), "pages made for this recording are")
        counts = PB.stage_copy(project, rec, stage)
        check(counts["pages"] == 1 and (stage / "textures" / "chr" / "Chr_0.png").is_file(),
              "and land in textures/chr/", str(counts))
        check(counts["sheets"] == 3 and (stage / "textures" / "sheets" / "metatiles.json").is_file(),
              "the kit's sheets land in textures/sheets/ with their sidecars", str(counts))


def test_the_newest_textured_recording_is_built():
    with tempfile.TemporaryDirectory() as td:
        project = make_project(Path(td))
        shutil.copytree(project / "auto" / "rec-001", project / "auto" / "rec-002")
        (project / "auto" / "rec-003" / "audio").mkdir(parents=True)
        (project / "auto" / "rec-003" / "audio" / "fingerprints.json").write_text("{}")
        check(PB.pick_recording(project).id == "rec-002", "an audio-only recording is not a build base")


def test_sync_removes_what_the_build_no_longer_has():
    with tempfile.TemporaryDirectory() as td:
        stage, mep = Path(td) / "stage", Path(td) / "mep"
        (stage / "textures").mkdir(parents=True)
        (stage / "textures" / "a.png").write_bytes(b"a")
        (mep / "textures" / "old").mkdir(parents=True)
        (mep / "textures" / "old" / "b.png").write_bytes(b"b")
        (mep / PB.STAMP).write_text("{}")
        written = PB.sync_into(stage, mep)
        check(written == 1 and (mep / "textures" / "a.png").read_bytes() == b"a", "a new file is written")
        check(not (mep / "textures" / "old").exists(), "a file and folder gone from the build are removed")
        check((mep / PB.STAMP).is_file(), "the stamp is kept")
        os.utime(mep / "textures" / "a.png", ns=(1, 1))
        check(PB.sync_into(stage, mep) == 0 and (mep / "textures" / "a.png").stat().st_mtime_ns == 1,
              "an identical file is not rewritten, so its mtime stays (ADR-0212)")



def test_the_pack_json_share_wrote_survives_a_rebuild():
    """#645: Share's `mep_build.py pack` writes mep/pack.json (targets, the
    ADR-0140 id, author, license) and a rebuild must not delete it; neither
    the zip beside mep/ nor a patch the pack.json declares may go either."""
    with tempfile.TemporaryDirectory() as td:
        project = make_project(Path(td))
        rc, _ = build(project)
        mep = project / "mep"
        (mep / "patches").mkdir()
        (mep / "patches" / "fix.ips").write_bytes(b"PATCHEOF")
        pack = {"mep": "1.1.0", "name": "Game", "version": "1.0.0", "id": "game-remaster",
                "license": "CC-BY-4.0", "author": "someone", "targets": [{"system": "nes", "sha1": "A" * 40}],
                "patches": [{"sha1": "B" * 40, "file": "patches/fix.ips"}],
                "sections": {"textures": {"path": "textures"}}}
        (mep / "pack.json").write_text(json.dumps(pack, indent=2) + "\n")
        written = (mep / "pack.json").read_bytes()
        zip_path = project / "game-mep.zip"
        zip_path.write_bytes(b"PK")
        rc, out = build(project)
        check(rc == 0, "a project Share packed rebuilds", out[-600:])
        check((mep / "pack.json").is_file() and (mep / "pack.json").read_bytes() == written,
              "mep/pack.json keeps its targets and id", out[-400:])
        check((mep / "patches" / "fix.ips").is_file(), "a patch the pack.json declares is kept")
        check(zip_path.read_bytes() == b"PK", "the zip beside mep/ is not touched")
        check(show_line(out) == "show: images", "a kept pack.json is not a manifest change", show_line(out))


def test_an_interrupted_first_build_does_not_lock_the_project_out():
    """#646: a first build stopped halfway through the sync left a stampless
    mep/ that every later build refused as foreign."""
    with tempfile.TemporaryDirectory() as td:
        project = make_project(Path(td))
        real = PB.sync_into

        def interrupted(stage, mep):
            mep.mkdir(parents=True, exist_ok=True)
            first = next(p for p in sorted(stage.rglob("*")) if p.is_file())
            (mep / first.relative_to(stage)).parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(first, mep / first.relative_to(stage))
            raise OSError("disk went away")

        PB.sync_into = interrupted
        try:
            rc = PB.run(project)
        finally:
            PB.sync_into = real
        check(rc == 1 and (project / "mep").is_dir(), "the interrupted build failed with mep/ half written", str(rc))
        rc, out = build(project)
        check(rc == 0, "the next build proceeds instead of refusing a foreign pack", out[-400:])
        check((project / "mep" / "textures" / "hires.txt").is_file(), "and finishes mep/")


def test_an_interrupted_rebuild_drops_the_complete_claim():
    """#665: the `complete: false` claim was written only when no stamp existed,
    so a rebuild stopped mid-sync kept the last build's `complete: true` and
    Remaster read the half-synced mep/ as up to date."""
    with tempfile.TemporaryDirectory() as td:
        project = make_project(Path(td))
        rc, out = build(project)
        stamp = PB.stamp_path(project)
        first = json.loads(stamp.read_text())
        check(rc == 0 and first.get("complete") is True, "the first build stamps complete", out[-400:])
        paint(project / "kit" / "rec-001" / "sheets" / "metatiles.png", (1, 2, 3, 255))
        real = PB.sync_into

        def interrupted(stage, mep):
            raise OSError("disk went away")

        PB.sync_into = interrupted
        try:
            rc = PB.run(project)
        finally:
            PB.sync_into = real
        doc = json.loads(stamp.read_text())
        check(rc == 1 and doc.get("complete") is False,
              "the interrupted rebuild leaves the stamp a claim, not complete", json.dumps(doc))
        rc, out = build(project)
        doc = json.loads(stamp.read_text())
        check(rc == 0 and doc.get("complete") is True, "the next build proceeds and stamps complete", out[-400:])
        kit_mtime = max(f.stat().st_mtime for f in (project / "kit").rglob("*") if f.is_file())
        check(stamp.stat().st_mtime >= kit_mtime, "and the stamp is newer than every kit file (up to date)")


def test_a_hand_made_mep_is_still_refused():
    """The #646 fix must not weaken the guard: a mep/ with no stamp that this
    build never started is someone else's pack."""
    with tempfile.TemporaryDirectory() as td:
        project = make_project(Path(td))
        (project / "mep" / "textures").mkdir(parents=True)
        (project / "mep" / "textures" / "hires.txt").write_text("<ver>106\n")
        rc, out = build(project)
        check(rc == 2 and "did not write" in out, "a hand-made mep/ is refused", out[-400:])
        check(not PB.stamp_path(project).exists(), "and is not stamped")


def main():
    tests = [
        test_a_clean_build_writes_mep_and_says_the_rom_must_be_reopened,
        test_a_repaint_that_keeps_the_manifest_reloads_images_only,
        test_a_failed_build_keeps_the_last_good_mep,
        test_a_mep_this_build_did_not_write_is_refused_and_untouched,
        test_nothing_recorded_is_refused,
        test_pages_join_only_the_recording_they_were_made_for,
        test_the_newest_textured_recording_is_built,
        test_sync_removes_what_the_build_no_longer_has,
        test_the_pack_json_share_wrote_survives_a_rebuild,
        test_an_interrupted_first_build_does_not_lock_the_project_out,
        test_an_interrupted_rebuild_drops_the_complete_claim,
        test_a_hand_made_mep_is_still_refused,
    ]
    for t in tests:
        t()
    if _FAILURES:
        print(f"\n{len(_FAILURES)} failure(s)")
        return 1
    print(f"\nall {len(tests)} test(s) passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
