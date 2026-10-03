"""Headless suite for the Remaster project folder (`mep_project.py`, ADR-0243, F12.20).

A Remaster project is the ROM's enhancement folder: every recording the
bootstrap makes is `auto/rec-NNN/`, a bare `auto/textures/` recorded before the
ADR reads as `rec-001` without being moved, and `project.json` is the
machine-written list - absent, the list is derived from the folder names.
`mep_build.py`, the kit and the recording tools all find the recordings
through this one module.

Run:  python3 scripts/test_mep_project.py
"""

import json
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import mep_project as P  # noqa: E402

_FAILURES = []


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


def _recording(root: Path, rel: str, textures=True, audio=False, body="<ver>106\n"):
    base = root / "auto" / rel if rel else root / "auto"
    if textures:
        (base / "textures").mkdir(parents=True, exist_ok=True)
        (base / "textures" / "hires.txt").write_text(body)
    if audio:
        (base / "audio").mkdir(parents=True, exist_ok=True)
        (base / "audio" / "fingerprints.json").write_text("{}")
    return base


def test_recording_ids_follow_the_cores_rule():
    check(P.recording_number("rec-001") == 1, "rec-001 is recording 1")
    check(P.recording_number("rec-1234") == 1234, "an id past 999 keeps its digits")
    for name in ("rec-", "rec-01a", "rec-000", "textures", "repaint", "rec-0000001"):
        check(P.recording_number(name) == 0, f"{name!r} is not a recording folder")
    check(P.format_id(7) == "rec-007", "ids are zero-padded to three digits")


def test_a_bare_auto_textures_reads_as_rec_001_without_moving():
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        _recording(root, "")
        recs = P.recordings(root)
        check([r.id for r in recs] == ["rec-001"], "a bare auto/textures is one recording, rec-001",
              str([r.id for r in recs]))
        check(recs and recs[0].path == root / "auto" and recs[0].bare,
              "its pack dir is auto/ itself, the folder nobody moved", str(recs and recs[0].path))
        check((root / "auto" / "textures" / "hires.txt").is_file() and not (root / "auto" / "rec-001").exists(),
              "reading it moves nothing")


def test_recordings_are_listed_in_id_order_with_the_bare_one_first():
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        _recording(root, "")
        _recording(root, "rec-003", audio=True)
        _recording(root, "rec-002")
        (root / "auto" / "repaint").mkdir()
        ids = [r.id for r in P.recordings(root)]
        check(ids == ["rec-001", "rec-002", "rec-003"], "recordings come in id order; repaint/ is not one", str(ids))
        latest = P.latest(root)
        check(latest is not None and latest.id == "rec-003", "the newest recording with textures is the latest",
              str(latest and latest.id))
        check(P.next_id(root) == "rec-004", "the next id is one past the highest, as the core allocates")


def test_the_latest_section_skips_a_recording_without_it():
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        _recording(root, "rec-001")
        _recording(root, "rec-002", textures=False, audio=True)
        check(P.latest(root, "textures").id == "rec-001", "textures come from the newest recording that has them")
        check(P.latest(root, "audio").id == "rec-002", "audio from the newest that has audio")
        check(P.auto_textures(root) == root / "auto" / "rec-001" / "textures",
              "auto_textures() names that recording's textures/", str(P.auto_textures(root)))


def test_auto_textures_of_a_folder_with_no_recording_is_the_legacy_path():
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        check(P.auto_textures(root) == root / "auto" / "textures",
              "with no recording, the path messages cite is the pre-ADR one", str(P.auto_textures(root)))
        check(P.latest(root) is None, "and there is no latest recording")


def test_a_bare_layout_and_an_explicit_rec_001_is_refused():
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        _recording(root, "")
        _recording(root, "rec-001")
        try:
            P.recordings(root)
            check(False, "two folders claiming rec-001 are refused")
        except P.ProjectError as exc:
            check("rec-001" in str(exc), "two folders claiming rec-001 are refused", str(exc))


def test_project_json_absent_is_derived_from_folder_names():
    with tempfile.TemporaryDirectory() as td:
        root = Path(td) / "Castlevania (USA)"
        _recording(root, "rec-001")
        _recording(root, "rec-002")
        doc = P.manifest(root)
        check(doc["name"] == "Castlevania (USA)", "a derived manifest is named after the folder", doc["name"])
        check([r["id"] for r in doc["recordings"]] == ["rec-001", "rec-002"], "one entry per folder")
        check(all(r["source"] is None and r["recordedAt"] is None for r in doc["recordings"]),
              "fields nobody wrote stay unknown, never invented", json.dumps(doc["recordings"]))
        check(not (root / "project.json").exists(), "deriving writes nothing")


def test_project_json_metadata_is_joined_to_the_folders_on_disk():
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        _recording(root, "rec-001")
        _recording(root, "rec-003")
        (root / "project.json").write_text(json.dumps({
            "name": "My project",
            "recordings": [
                {"id": "rec-001", "recordedAt": "2026-10-02T11:00:00Z", "source": "tas",
                 "durationSeconds": 30.5, "note": "stage 1"},
                {"id": "rec-002", "recordedAt": "2026-10-02T11:30:00Z", "source": "play",
                 "durationSeconds": 10, "note": "deleted"},
            ]}))
        doc = P.manifest(root)
        by_id = {r["id"]: r for r in doc["recordings"]}
        check(doc["name"] == "My project", "the display name comes from project.json")
        check(by_id.get("rec-001", {}).get("source") == "tas" and by_id["rec-001"]["durationSeconds"] == 30.5,
              "a listed recording carries its source and duration", json.dumps(by_id.get("rec-001")))
        check("rec-002" not in by_id, "a recording deleted from disk is not listed (it can be deleted alone)")
        check(by_id.get("rec-003", {}).get("source") is None, "a folder project.json does not list is still a recording")


def test_an_unknown_source_is_not_passed_through():
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        _recording(root, "rec-001")
        (root / "project.json").write_text(json.dumps(
            {"recordings": [{"id": "rec-001", "source": "movie"}]}))
        check(P.manifest(root)["recordings"][0]["source"] is None,
              "only play/tas/ai/script are sources (ADR-0243 Q2)")


def test_a_malformed_project_json_is_an_error_not_an_empty_project():
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        _recording(root, "rec-001")
        (root / "project.json").write_text("{ not json")
        try:
            P.manifest(root)
            check(False, "a malformed project.json is refused")
        except P.ProjectError as exc:
            check("project.json" in str(exc), "a malformed project.json is refused", str(exc))


def test_recorded_packs_under_a_session_tree():
    with tempfile.TemporaryDirectory() as td:
        session = Path(td)
        _recording(session / "stage1" / "Contra", "rec-001")
        _recording(session / "stage2" / "Contra", "")
        _recording(session / "stage2" / "Contra", "rec-002", textures=False, audio=True)
        packs = P.recorded_packs(session)
        rel = [str(p.relative_to(session)) for p in packs]
        check(rel == ["stage1/Contra/auto/rec-001", "stage2/Contra/auto", "stage2/Contra/auto/rec-002"],
              "every recording of every project under a tree, in path order", str(rel))
        hires = [str(p.relative_to(session)) for p in P.recorded_hires(session)]
        check(hires == ["stage1/Contra/auto/rec-001/textures/hires.txt", "stage2/Contra/auto/textures/hires.txt"],
              "only recordings with textures have a hires.txt", str(hires))
        out = subprocess.run([sys.executable, str(HERE / "mep_project.py"), "packs", str(session)],
                             capture_output=True, text=True)
        check(out.returncode == 0 and out.stdout.splitlines() == [str(p) for p in packs],
              "`mep_project.py packs` prints the same list, one per line", out.stdout + out.stderr)


def test_list_cli_prints_the_manifest():
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        _recording(root, "")
        out = subprocess.run([sys.executable, str(HERE / "mep_project.py"), "list", str(root)],
                             capture_output=True, text=True)
        doc = json.loads(out.stdout) if out.returncode == 0 else {}
        check([r["id"] for r in doc.get("recordings", [])] == ["rec-001"], "`list` prints the derived manifest",
              out.stdout + out.stderr)
        empty = Path(td) / "nothing"
        empty.mkdir()
        out = subprocess.run([sys.executable, str(HERE / "mep_project.py"), "list", str(empty)],
                             capture_output=True, text=True)
        check(out.returncode == 1 and "no recording" in out.stderr, "`list` of a folder with no recording fails loudly",
              out.stderr)


def test_next_cli_numbers_one_past_the_highest_recording():
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        run = lambda: subprocess.run([sys.executable, str(HERE / "mep_project.py"), "next", str(root)],
                                     capture_output=True, text=True)
        first = run().stdout.strip()
        _recording(root, "")
        bare = run().stdout.strip()
        _recording(root, "rec-007")
        gap = run().stdout.strip()
        check((first, bare, gap) == ("rec-001", "rec-002", "rec-008"),
              "`next` is rec-001 on an empty project, counts a bare auto/ as rec-001, never refills a gap",
              str((first, bare, gap)))


def test_the_session_and_sheet_readers_find_the_newest_recording():
    import nav_sweep_metrics
    import sheet_repaint
    with tempfile.TemporaryDirectory() as td:
        session = Path(td)
        game = session / "Contra"
        _recording(game, "rec-001")
        _recording(game, "rec-002")
        (game / "auto" / "rec-002" / "textures" / "sheets").mkdir()
        (game / "auto" / "rec-002" / "textures" / "sheets" / "misc.json").write_text("{}")
        hit = nav_sweep_metrics.find_pack_hires(session)
        check(hit == game / "auto" / "rec-002" / "textures" / "hires.txt",
              "nav_sweep_metrics reads the session's newest auto/rec-NNN", str(hit))
        sheets = sheet_repaint.find_sheets_dir(game)
        check(sheets == game / "auto" / "rec-002" / "textures" / "sheets",
              "sheet_repaint finds a game folder's sheets in its newest recording", str(sheets))
        check(sheet_repaint.find_hires_txt(game) == game / "auto" / "rec-002" / "textures" / "hires.txt",
              "sheet_repaint's --target screens reads the newest recording's hires.txt")
        check(sheet_repaint.default_out_dir(sheets) == game / "auto" / "repaint",
              "the repaint output stays beside the recordings, never inside one")


def test_kit_plan_is_per_recording_surfaces_plus_union_pages():
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        _recording(root, "")
        _recording(root, "rec-002")
        _recording(root, "rec-003", textures=False, audio=True)
        plan = P.kit_plan(root, rom=Path("game.nes"), out=root / "kit")
        steps = [(s["kit"].name, Path(s["argv"][1]).name, s["argv"][2]) for s in plan]
        check(steps[:4] == [
            ("rec-001", "artist_kit.py", str(root / "auto")),
            ("rec-001", "artist_bg_kit.py", str(root / "auto")),
            ("rec-002", "artist_kit.py", str(root / "auto" / "rec-002")),
            ("rec-002", "artist_bg_kit.py", str(root / "auto" / "rec-002")),
        ], "figures and scenery stay per recording, one kit each (ADR-0194 §1)", str(steps))
        pages = [s for s in plan if Path(s["argv"][1]).name == "artist_chr_kit.py"]
        check(len(pages) == 1 and pages[0]["argv"][2] == str(root / "auto")
              and pages[0]["argv"].count("--also") == 1
              and pages[0]["argv"][pages[0]["argv"].index("--also") + 1] == str(root / "auto" / "rec-002"),
              "the pattern pages are the one union: first recording, --also every other (ADR-0194 §2)",
              str(pages and pages[0]["argv"]))
        check(all("rec-003" not in " ".join(s["argv"]) for s in plan),
              "an audio-only recording has no kit surface")
        assembled = [s["argv"][2] for s in plan if Path(s["argv"][1]).name == "artist_kit_assemble.py"]
        check(assembled == [str(root / "kit" / k) for k in ("rec-001", "rec-002", "pages")],
              "every kit folder is assembled after its generators ran", str(assembled))



def _kit_cli(project: Path, rom: Path):
    out = subprocess.run([sys.executable, str(HERE / "mep_project.py"), "kit", str(project), "--rom", str(rom),
                          "--no-verify"], capture_output=True, text=True)
    return out.returncode, out.stdout + out.stderr


def _chr_project(td: Path):
    """rec-001 is a CHR ROM recording; the second recording of the same ROM
    (with cells and a whole bank rec-001 never saw) is returned unplaced, so a
    test can add it after the first kit, the way Stop recording does."""
    import shutil
    import test_artist_chr_kit as CK
    primary, donor, rom = CK.chr_rom_pair_fixture(td)
    project = td / "Game"
    (project / "auto").mkdir(parents=True)
    shutil.move(str(primary), str(project / "auto" / "rec-001"))
    return project, donor, rom


def _repaint(png: Path) -> bytes:
    from sheet_repaint import read_png, write_png
    img = read_png(png)
    for x in range(4):
        img.set(x, 0, (0x12, 0x34, 0x56, 0xFF))
    write_png(png, img)
    return png.read_bytes()


def _donated(sidecar: Path) -> int:
    return sum(1 for c in json.loads(sidecar.read_text())["cells"] if c.get("state") == "donated")


def test_a_rerun_kit_never_overwrites_a_painted_pattern_page():
    """#644: Stop recording reruns the kit; a page the artist painted keeps its
    whole family (PNG, twin, sidecar, legend, .ora), and a page nobody painted
    still takes the new recording's cells."""
    import shutil
    with tempfile.TemporaryDirectory() as td:
        project, donor, rom = _chr_project(Path(td))
        _kit_cli(project, rom)
        chr_dir = project / "kit" / "pages" / "chr"
        painted, open_page = chr_dir / "Chr_00_1.png", chr_dir / "Chr_00_0.json"
        if not (painted.is_file() and open_page.is_file()):
            check(False, "the first kit writes the pattern pages",
                  str(sorted(p.name for p in chr_dir.glob("*"))) if chr_dir.is_dir() else "no kit/pages/chr")
            return
        painted_bytes = _repaint(painted)
        family = {n: (chr_dir / n).read_bytes() for n in ("Chr_00_1.json", "Chr_00_1.orig.png", "Chr_00_1.ora")}
        shutil.move(str(donor), str(project / "auto" / "rec-002"))
        rc, out = _kit_cli(project, rom)
        check(painted.read_bytes() == painted_bytes, "a painted page PNG survives a rerun kit", out[-600:])
        check(all((chr_dir / n).read_bytes() == b for n, b in family.items()),
              "its sidecar, twin and .ora stay the ones it was painted against")
        check("Chr_00_1.png" in out and "painted" in out, "the run says which painted page it kept", out[-600:])
        check(_donated(open_page) > 0, "an unpainted page still takes the new recording's cells",
              str(_donated(open_page)))
        frag = json.loads((project / "kit" / "pages" / "kit-part-chr.json").read_text())
        check(frag.get("pack") and Path(frag["pack"]).resolve() == (project / "auto" / "rec-001").resolve(),
              "the pages are still made for the first recording (ADR-0194 §2)", str(frag.get("pack")))
        _repaint(painted)
        rc, out = _kit_cli(project, rom)
        check(not list((project / "kit").glob(".pages*")), "no staging folder is left in kit/",
              str(sorted(p.name for p in (project / "kit").iterdir())))


def test_a_page_saved_through_its_ora_is_never_overwritten():
    """ADR-0220: the `.ora` is write-only for the tools, but an artist who
    saves it in Krita has painted it - the page keeps its family."""
    import shutil
    with tempfile.TemporaryDirectory() as td:
        project, donor, rom = _chr_project(Path(td))
        _kit_cli(project, rom)
        chr_dir = project / "kit" / "pages" / "chr"
        ora = chr_dir / "Chr_00_0.ora"
        if not ora.is_file():
            check(False, "the first kit writes the page's .ora")
            return
        ora.write_bytes(ora.read_bytes() + b"saved by a paint program")
        saved = ora.read_bytes()
        sidecar = (chr_dir / "Chr_00_0.json").read_bytes()
        shutil.move(str(donor), str(project / "auto" / "rec-002"))
        rc, out = _kit_cli(project, rom)
        check(ora.read_bytes() == saved, "a page saved through its .ora survives a rerun kit", out[-600:])
        check((chr_dir / "Chr_00_0.json").read_bytes() == sidecar,
              "and its sidecar is the one the .ora was painted against")


def test_a_legacy_kit_page_that_matches_the_generator_is_regenerated():
    """A kit written before the kit kept a record of its own bytes: a page
    equal to what the generator writes is the generator's, any other page is
    kept (the conservative reading - it may be painted)."""
    with tempfile.TemporaryDirectory() as td:
        project, _donor, rom = _chr_project(Path(td))
        _kit_cli(project, rom)
        pages = project / "kit" / "pages"
        for ledger in pages.glob(".kit-written*"):
            ledger.unlink()
        page = pages / "chr" / "Chr_00_0.png"
        if not page.is_file():
            check(False, "the first kit writes the pattern pages")
            return
        painted_bytes = _repaint(page)
        rc, out = _kit_cli(project, rom)
        check(page.read_bytes() == painted_bytes, "an unrecorded page that differs from the generator is kept",
              out[-600:])
        check((pages / "chr" / "Chr_00_1.png").is_file(), "an unrecorded page equal to the generator stays")


def test_merge_removes_only_what_the_kit_wrote_and_nobody_touched():
    import mep_project_kit as MK
    with tempfile.TemporaryDirectory() as td:
        fresh, kit = Path(td) / "fresh", Path(td) / "kit"
        (fresh / "chr").mkdir(parents=True)
        for name in ("Chr_0.png", "Chr_0.json", "Chr_1.png", "Chr_1.json"):
            (fresh / "chr" / name).write_bytes(name.encode())
        MK.merge_kit(fresh, kit)
        (kit / "chr" / "mine.png").write_bytes(b"the artist's own file")
        for name in ("Chr_1.png", "Chr_1.json"):
            (fresh / "chr" / name).unlink()
        (fresh / "chr" / "Chr_0.json").write_bytes(b"new sidecar")
        report = MK.merge_kit(fresh, kit)
        check(not (kit / "chr" / "Chr_1.png").exists() and not (kit / "chr" / "Chr_1.json").exists(),
              "an untouched page the generator no longer makes is removed", str(report))
        check((kit / "chr" / "mine.png").read_bytes() == b"the artist's own file",
              "a file the kit never wrote is never removed or replaced")
        check((kit / "chr" / "Chr_0.json").read_bytes() == b"new sidecar" and "chr/mine" in report["kept"],
              "an unpainted page takes the new sidecar", str(report))


def test_kit_plan_skips_the_generators_of_a_recording_already_kitted():
    """#644: rerunning a per-recording generator into its own kit mints a new
    `usrNNN` sheet beside the old one, so a recording whose kit part exists is
    not kitted again. The step count the job card shows does not move."""
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        _recording(root, "rec-001")
        _recording(root, "rec-002")
        kit = root / "kit" / "rec-001"
        kit.mkdir(parents=True)
        for name in ("kit-part-sprites.json", "kit-part-background.json", "kit.json"):
            (kit / name).write_text("{}")
        plan = P.kit_plan(root, rom=Path("game.nes"), out=root / "kit")
        check(len(plan) == 3 * 2 + 2, "the plan keeps one step per generator and kit", str(len(plan)))
        skipped = sorted((s["kit"].name, Path(s["argv"][1]).name) for s in plan if s.get("skip"))
        check(skipped == [("rec-001", "artist_bg_kit.py"), ("rec-001", "artist_kit.py"),
                          ("rec-001", "artist_kit_assemble.py")],
              "every step of the kitted recording is skipped, none of the new one", str(skipped))
        (kit / "kit-part-background.json").unlink()
        plan = P.kit_plan(root, rom=Path("game.nes"), out=root / "kit")
        skipped = sorted((s["kit"].name, Path(s["argv"][1]).name) for s in plan if s.get("skip"))
        check(skipped == [("rec-001", "artist_kit.py")],
              "a kit missing one part reruns only that generator and its assemble", str(skipped))


def _build(*argv):
    out = subprocess.run([sys.executable, str(HERE / "mep_build.py"), *argv], capture_output=True, text=True)
    return out.returncode, out.stdout + out.stderr


def test_mep_build_keys_from_the_newest_recording():
    import test_mep_build as B  # the ADR-0153 author-folder fixture, one definition
    with tempfile.TemporaryDirectory() as td:
        folder, _v, _c = B.make_sheet_folder(Path(td), "project-rec")
        key_source = folder / "textures" / "hires.txt"
        rec = folder / "auto" / "rec-002" / "textures"
        rec.mkdir(parents=True)
        key_source.rename(rec / "hires.txt")
        # rec-001, the bare layout, holds a manifest with no <tile>: keying from
        # it fails the build, so a passing build proves rec-002 was read.
        (folder / "auto" / "textures").mkdir(parents=True)
        (folder / "auto" / "textures" / "hires.txt").write_text("<ver>106\n<scale>1\n")
        rc, out = _build("build", str(folder))
        check(rc == 0 and key_source.is_file(), "mep_build keys from auto/rec-002/, the newest recording", out[-400:])


def test_mep_build_reads_a_bare_auto_textures_as_rec_001():
    import test_mep_build as B
    with tempfile.TemporaryDirectory() as td:
        folder, _v, _c = B.make_sheet_folder(Path(td), "project-bare")
        key_source = folder / "textures" / "hires.txt"
        # The whole layer, as the recorder leaves it: the linter checks every
        # <img> of auto/textures/hires.txt against auto/textures/.
        import shutil
        shutil.copytree(folder / "textures", folder / "auto" / "textures")
        (folder / "auto" / "textures" / "old.png").write_bytes(B.png(8, 8))  # the fixture's legacy <img>
        key_source.unlink()
        rc, out = _build("build", str(folder))
        check(rc == 0 and key_source.is_file() and (folder / "auto" / "textures" / "hires.txt").is_file()
              and not (folder / "auto" / "rec-001").exists(),
              "mep_build keys from a bare auto/textures (rec-001) and moves nothing", out[-400:])


def test_check_coverage_defaults_to_the_newest_recordings_baseline():
    import test_mep_build as B
    import shutil
    with tempfile.TemporaryDirectory() as td:
        folder, _v, _c = B.make_sheet_folder(Path(td), "project-cc")
        rc, out = _build("build", str(folder))
        shutil.copytree(folder / "textures", folder / "auto" / "rec-003" / "textures")
        rc2, out2 = _build("build", str(folder))
        rc3, out3 = _build("check-coverage", str(folder))
        check(rc == 0 and rc2 == 0 and rc3 == 0 and "every baseline tile key still resolves" in out3,
              "check-coverage's default baseline is auto/rec-NNN/textures/hires.txt", (out + out2 + out3)[-400:])


def main():
    for name, fn in sorted(globals().items()):
        if name.startswith("test_") and callable(fn):
            fn()
    if _FAILURES:
        print(f"\n{len(_FAILURES)} failure(s)")
        return 1
    print("\nall passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
