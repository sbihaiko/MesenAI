"""Headless suite for the artist kit assembler (`artist_kit_assemble.py`).

The assembler is the one part of the kit an artist reads before anything else,
so what is tested here is exactly what they see: the parts come in reading
order, a surface carrying inferred art is marked as such, a dropped surface
still says why it was dropped, and a failed rebuild is never reported as
passed.

Run:  python3 scripts/test_artist_kit_assemble.py
"""

import json
import subprocess
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import artist_kit_assemble as A  # noqa: E402

_FAILURES = []


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


def _fragment(part, files=(), dropped=(), notes=(), verify=None, pack="packs/demo"):
    return {
        "part": part,
        "generator": f"scripts/artist_{part}.py",
        "pack": pack,
        "files": list(files),
        "dropped": list(dropped),
        "notes": list(notes),
        "verify": verify if verify is not None else {
            "ran": True, "errors": 0, "keys_before": 10, "keys_after": 10,
            "lost": 0, "added": 0},
    }


def _kit(td, *fragments):
    root = Path(td)
    for fragment in fragments:
        (root / f"kit-part-{fragment['part']}.json").write_text(json.dumps(fragment))
    return root


def test_parts_are_ordered_most_recognisable_first():
    with tempfile.TemporaryDirectory() as td:
        root = _kit(td, _fragment("chr"), _fragment("map"), _fragment("sprites"),
                    _fragment("background"))
        kit = A.build_kit(root)
        order = [p["part"] for p in kit["parts"]]
        check(order == ["sprites", "background", "map", "chr"],
              "the kit opens on the figures and ends on the pattern pages", str(order))


def test_an_unplanned_part_is_shown_rather_than_dropped():
    with tempfile.TemporaryDirectory() as td:
        root = _kit(td, _fragment("sprites"), _fragment("audio"))
        kit = A.build_kit(root)
        check([p["part"] for p in kit["parts"]] == ["sprites", "audio"],
              "a part the assembler does not know about still reaches the artist")


def test_totals_add_up_across_parts():
    with tempfile.TemporaryDirectory() as td:
        root = _kit(
            td,
            _fragment("sprites", files=[{"path": "sheets/usr000.png", "title": "a run",
                                         "rows": 1, "columns": 3, "cells": 3, "seen": True}]),
            _fragment("chr", files=[{"path": "chr/Chr_0.png", "title": "page 0",
                                     "cells": 256, "seen": False}],
                      dropped=[{"path": "chr/Chr_9.png", "why": "nothing recoverable"}]),
        )
        kit = A.build_kit(root)
        t = kit["totals"]
        check(t["files"] == 2 and t["cells"] == 259, "files and cells are summed",
              json.dumps(t))
        check(t["inferred_files"] == 1, "a surface carrying inferred art is counted", json.dumps(t))
        check(t["dropped"] == 1, "a dropped surface is counted", json.dumps(t))


def test_inferred_art_is_marked_in_the_page_an_artist_reads():
    with tempfile.TemporaryDirectory() as td:
        root = _kit(td, _fragment("chr", files=[
            {"path": "chr/Chr_0.png", "title": "page 0", "cells": 256, "seen": False}]))
        text = A.render_markdown(A.build_kit(root))
        check("inferred - check it" in text, "a ROM fill is labelled as inferred in ARTIST.md")
        check("never paint it" in text, "the page tells the artist the .orig.png is the reference")
        check("mep_build.py build" in text and "cp -R" in text,
              "the page says to build a copy of the recording, not the recording itself")
        check("ADR-0147" in text, "the page cites the layer rule it follows")
        check("<bgPreservesBehindBgSprites>" in text and "ADR-0224" in text,
              "the page tells the artist what the recorder's opt-in tag is, so it is not deleted as noise")
        check("Select `paint` in the Layers panel before your first stroke" in text
              and "`orig`, active" in text,
              "the page tells the artist GIMP and Krita open the .ora with `orig` active (ADR-0220 §3, 2026-09-23)")


def test_a_dropped_surface_keeps_its_reason():
    with tempfile.TemporaryDirectory() as td:
        root = _kit(td, _fragment("background", dropped=[
            {"path": "sheets/obj000.png", "why": "19 cells, all uniform black"}]))
        text = A.render_markdown(A.build_kit(root))
        check("19 cells, all uniform black" in text,
              "the artist is told what was left out and why")


def test_a_failed_or_missing_rebuild_is_never_reported_as_passed():
    with tempfile.TemporaryDirectory() as td:
        root = _kit(td,
                    _fragment("sprites", verify={"ran": True, "errors": 2, "keys_before": 10,
                                                 "keys_after": 9, "lost": 1, "added": 0}),
                    _fragment("map", verify={"ran": False}))
        text = A.render_markdown(A.build_kit(root))
        check("FAILED" in text, "a rebuild with errors reads as failed")
        check("not verified" in text, "a part that never ran the rebuild says so")
        check("passed" not in text.replace("not verified", ""),
              "nothing unverified is dressed up as passed")


def test_a_gained_key_is_only_passed_when_it_came_from_the_pack():
    # A surface that *invents* a key must never read as passed - the artist
    # would see a green line over art the pack cannot place. A stage panorama
    # legitimately gains keys the pack's own manifest already held and merely
    # routed to art for the first time; it says so with addedAreFromSource,
    # and only then is the gain reported rather than failed.
    with tempfile.TemporaryDirectory() as td:
        root = _kit(td, _fragment("map", verify={
            "ran": True, "errors": 0, "keys_before": 350, "keys_after": 396,
            "lost": 0, "added": 46, "addedAreFromSource": True}))
        text = A.render_markdown(A.build_kit(root))
        check("passed" in text, "a panorama's gained keys still pass")
        check("newly routed to art" in text,
              "and the line says where the gained keys came from")

    with tempfile.TemporaryDirectory() as td:
        root = _kit(td, _fragment("background", verify={
            "ran": True, "errors": 0, "keys_before": 350, "keys_after": 396,
            "lost": 0, "added": 46}))
        text = A.render_markdown(A.build_kit(root))
        check("FAILED" in text, "a gained key with no provenance reads as failed")
        check("passed" not in text.replace("not verified", ""),
              "and is not dressed up as passed")


def test_an_empty_or_broken_kit_fails_loudly():
    with tempfile.TemporaryDirectory() as td:
        try:
            A.build_kit(Path(td))
            check(False, "an empty kit folder raises")
        except A.KitError:
            check(True, "an empty kit folder raises")
        (Path(td) / "kit-part-sprites.json").write_text("{not json")
        try:
            A.build_kit(Path(td))
            check(False, "a malformed fragment raises")
        except A.KitError:
            check(True, "a malformed fragment raises")


def test_writing_the_kit_produces_both_files():
    with tempfile.TemporaryDirectory() as td:
        root = _kit(td, _fragment("sprites", files=[{"path": "sheets/usr000.png",
                                                     "title": "a run", "cells": 3}]))
        rc = A.main([str(root), "--title", "Demo"])
        check(rc == 0, "the tool exits 0 on a good kit")
        kit = json.loads((root / "kit.json").read_text())
        check(kit["title"] == "Demo", "the title reaches kit.json")
        check((root / "ARTIST.md").read_text().startswith("# Artist kit - Demo"),
              "ARTIST.md is written and titled")


def test_every_surface_carries_the_name_the_paint_program_exports_to():
    # F12.4 / ADR-0213. The manifest's `path` is kit-relative, but what an
    # artist pastes into Photoshop is the base name: a `/` in a layer name is a
    # subfolder under Photoshop's own -assets folder.
    with tempfile.TemporaryDirectory() as td:
        root = _kit(td, _fragment("sprites", files=[
            {"path": "sheets/usr000.png", "cells": 3},
            {"path": "chr/Chr_0.png", "cells": 256},
        ]))
        kit = A.build_kit(root)
        got = [f["assetName"] for f in kit["parts"][0]["files"]]
        check(got == ["usr000.png", "Chr_0.png"],
              "each surface carries the layer name to paste, base name only",
              str(got))
        page = A.render_markdown(kit)
        check("Reload Repainted Images" in page,
              "the page names the F12.3 action that puts the save on screen")
        check("-assets" in page and "cannot be changed" in page,
              "the page states Photoshop's -assets folder rather than implying "
              "an in-place overwrite it does not do")


def test_a_surface_a_paint_program_cannot_export_to_stops_the_kit():
    with tempfile.TemporaryDirectory() as td:
        root = _kit(td, _fragment("sprites", files=[
            {"path": "sheets/run,walk.png", "cells": 1}]))
        try:
            A.build_kit(root)
            check(False, "a comma in a surface name stops the kit", "it built")
        except A.KitError as exc:
            check("comma" in str(exc) and "run,walk.png" in str(exc),
                  "a comma in a surface name stops the kit, naming the file "
                  "and the reader it would break", str(exc))


def test_two_surfaces_that_differ_only_in_case_stop_the_kit():
    # The one rule that does not exist per name: on the artist's macOS or
    # Windows machine these are one file, so the kit would silently lose one.
    with tempfile.TemporaryDirectory() as td:
        root = _kit(td, _fragment("chr", files=[
            {"path": "chr/Chr_0.png", "cells": 1},
            {"path": "chr/chr_0.png", "cells": 1},
        ]))
        try:
            A.build_kit(root)
            check(False, "a case-only clash stops the kit", "it built")
        except A.KitError as exc:
            check("differ only in case" in str(exc),
                  "a case-only clash inside one folder stops the kit", str(exc))
    # The same two names in *different* folders are two files everywhere.
    with tempfile.TemporaryDirectory() as td:
        root = _kit(td, _fragment("chr", files=[
            {"path": "chr/Chr_0.png", "cells": 1},
            {"path": "sheets/chr_0.png", "cells": 1},
        ]))
        kit = A.build_kit(root)
        check(len(kit["parts"][0]["files"]) == 2,
              "the same name in two folders is not a clash")


def _done_block(page):
    """The command block of "When you are done", one command per line."""
    section = page.split("## When you are done", 1)[1].split("\n## ", 1)[0]
    block = section.split("```", 2)[1]
    return section, [line for line in block.splitlines() if line.strip()]


def test_the_done_steps_import_painted_figures_between_copy_and_build():
    # #399: ARTIST.md invited painting figures/usrNNN-figure.png, then the
    # done steps only copied sheets/ and chr/ - every painted figure was lost.
    with tempfile.TemporaryDirectory() as td:
        root = _kit(td, _fragment("sprites", files=[
            {"path": "sheets/usr000.png", "cells": 3, "figure": "figures/usr000-figure.png"}]))
        section, cmds = _done_block(A.render_markdown(A.build_kit(root)))
        imports = [i for i, c in enumerate(cmds)
                   if "scripts/mep_figure.py import <game>/painted" in c
                   and "<kit>/figures/usr*-figure.png" in c]
        copy = next((i for i, c in enumerate(cmds) if c.startswith("cp <kit>/sheets/")), None)
        build = next((i for i, c in enumerate(cmds) if "mep_build.py build <game>/painted" in c), None)
        check(len(imports) == 1, "a kit with figures names the figure import as a concrete "
              "command on the painted copy", str(cmds))
        check(imports and copy is not None and build is not None and copy < imports[0] < build,
              "the figure import runs after the sheet copy and before the build", str(cmds))
        # Codex on #402: a `for` loop's status is its last iteration's, so an
        # import that fails mid-loop was masked and the build still ran.
        imp = cmds[imports[0]] if imports else ""
        check("|| exit 1" in imp and imp.startswith("sh -c '") and imp.rstrip().endswith("&&"),
              "a failed figure import exits its own `sh -c` child (never the artist's "
              "terminal) and `&&` holds the build back", imp)
        check("not both" in section and "lost to" in section,
              "the done section says a figure and its sheet row are one surface, and what "
              "the build says when both are painted")


def test_the_done_steps_name_only_what_the_kit_has():
    # A literal `cp` of a glob that matches nothing fails, and no figure step
    # belongs in a kit that exported none.
    with tempfile.TemporaryDirectory() as td:
        root = _kit(td, _fragment("sprites", files=[{"path": "sheets/usr000.png", "cells": 3}]))
        section, cmds = _done_block(A.render_markdown(A.build_kit(root)))
        check("mep_figure.py" not in section, "a kit without figures has no figure import step",
              str(cmds))
        check(not any("<kit>/chr/" in c for c in cmds), "a kit without pattern pages copies no chr/",
              str(cmds))
    with tempfile.TemporaryDirectory() as td:
        root = _kit(td, _fragment("chr", files=[{"path": "chr/Chr_0.png", "cells": 256}]))
        _section, cmds = _done_block(A.render_markdown(A.build_kit(root)))
        check(any(c.startswith("cp <kit>/chr/") for c in cmds)
              and not any("<kit>/sheets/" in c for c in cmds),
              "a kit of pattern pages alone copies chr/ and no sheets/", str(cmds))


def test_the_done_steps_return_painted_screens_to_the_backgrounds():
    # #403: the background kit hands out scene/screenNNN.png and invites
    # painting it, but the done steps never copied scene/ anywhere - measured
    # on a Mega Man 3 recording: build exit 0, the painted pixel absent from the
    # built pack's textures/backgrounds/screen001.png.
    with tempfile.TemporaryDirectory() as td:
        root = _kit(td, _fragment("background", files=[
            {"path": "sheets/obj000.png", "cells": 2},
            {"path": "scene/screen001.png", "unit": "scene", "cells": 1}]))
        section, cmds = _done_block(A.render_markdown(A.build_kit(root)))
        scene = [i for i, c in enumerate(cmds)
                 if c.startswith("cp <kit>/scene/") and c.endswith("<game>/painted/textures/backgrounds/")]
        clone = next((i for i, c in enumerate(cmds) if c.startswith("cp -R <game>/auto")), None)
        build = next((i for i, c in enumerate(cmds) if "mep_build.py build <game>/painted" in c), None)
        check(len(scene) == 1, "a kit with scene/ screens copies them into the painted copy's "
              "textures/backgrounds/", str(cmds))
        check(scene and clone is not None and build is not None and clone < scene[0] < build,
              "the screen copy runs after the recording copy and before the build", str(cmds))
        check("textures/backgrounds/" in section and "<background>" in section,
              "the done section says where a painted screen goes and what draws it")
    with tempfile.TemporaryDirectory() as td:
        root = _kit(td, _fragment("background", files=[{"path": "sheets/obj000.png", "cells": 2}]))
        _section, cmds = _done_block(A.render_markdown(A.build_kit(root)))
        check(not any("<kit>/scene/" in c or "backgrounds" in c for c in cmds),
              "a kit without scene/ screens copies no scene/", str(cmds))


def test_the_figure_loop_stops_at_the_first_failed_import():
    # A `for` loop's status is its last command's: without a stop, a failed
    # import followed by a good one exits 0 and the build runs on a copy that
    # lost the first figure (Codex on #402).
    with tempfile.TemporaryDirectory() as td:
        root = _kit(td, _fragment("sprites", files=[
            {"path": "sheets/usr000.png", "cells": 3, "figure": "figures/usr000-figure.png"}]))
        _section, cmds = _done_block(A.render_markdown(A.build_kit(root)))
        loop = next((c for c in cmds if c.startswith("sh -c 'for f in <kit>/figures/")), None)
        check(loop is not None, "a kit with figures has a figure loop", str(cmds))
        if loop is None:
            return
        figures = Path(td) / "figures"
        figures.mkdir()
        for name in ("usr000-figure.png", "usr001-figure.png"):
            (figures / name).write_bytes(b"")
        # The first import fails, the second succeeds, and a sentinel after the
        # `&&` records whether the build would have run. The stand-in import
        # lives inline because `sh -c` sees no function from the outer shell.
        script = (loop.replace("<kit>", td)
                      .replace("python3 scripts/mep_figure.py import <game>/painted",
                               '[ -n "${f##*usr000*}" ] && :')
                  + f"\ntouch {td}/after")
        proc = subprocess.run(["bash", "-c", script], capture_output=True, text=True)
        check(proc.returncode != 0 and not (Path(td) / "after").exists(),
              "a failed figure import stops the done steps even when a later import succeeds",
              f"exit {proc.returncode}, after={(Path(td) / 'after').exists()}")


def main():
    tests = [
        test_parts_are_ordered_most_recognisable_first,
        test_an_unplanned_part_is_shown_rather_than_dropped,
        test_totals_add_up_across_parts,
        test_inferred_art_is_marked_in_the_page_an_artist_reads,
        test_a_dropped_surface_keeps_its_reason,
        test_a_failed_or_missing_rebuild_is_never_reported_as_passed,
        test_a_gained_key_is_only_passed_when_it_came_from_the_pack,
        test_an_empty_or_broken_kit_fails_loudly,
        test_writing_the_kit_produces_both_files,
        test_every_surface_carries_the_name_the_paint_program_exports_to,
        test_a_surface_a_paint_program_cannot_export_to_stops_the_kit,
        test_two_surfaces_that_differ_only_in_case_stop_the_kit,
        test_the_done_steps_import_painted_figures_between_copy_and_build,
        test_the_done_steps_name_only_what_the_kit_has,
        test_the_done_steps_return_painted_screens_to_the_backgrounds,
        test_the_figure_loop_stops_at_the_first_failed_import,
    ]
    for t in tests:
        t()
    print(f"\n{len(tests) - len(_FAILURES)}/{len(tests)} cases passed")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
