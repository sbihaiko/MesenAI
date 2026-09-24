"""Headless suite for the unattended recording job's resolver and report
(`library_job.py`). F12.10.

What is worth testing here is everything that decides *what the job does* —
which driver a ROM gets, which state a stage starts from, and whether the
report tells the truth about a ROM that failed. The recording itself is the
emulator's and is not simulated: every fixture is a synthetic iNES file, a
folder of text scripts and hand-written JSON in a temp dir. No emulator, no
ROM, no network.

Two cases exist because the first real run got them wrong, and they are the
reason this file is not ceremony:

* `test_a_mint_serves_the_stages_that_start_from_it` — without minting, the
  first F12.10 run kept **1 retained frame per stage** on Mega Man 3, because
  `.mss` states are never versioned and every stage ran from the title screen.
* `test_a_movie_is_matched_on_the_whole_file_hash` — a bk2 header names the
  file, not the No-Intro payload. Comparing the wrong one never raises; it just
  silently never matches.

Run:  python3 scripts/test_library_job.py
"""

import json
import sys
import tempfile
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import library_job as L  # noqa: E402

_FAILURES = []


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


def ines(path, prg=1, chr_banks=1, body=b"\x00"):
    """A minimal iNES file: a 16-byte header plus the PRG/CHR it declares."""
    header = bytearray(16)
    header[:4] = b"NES\x1a"
    header[4] = prg
    header[5] = chr_banks
    data = bytes(header) + (body * (prg * 0x4000 + chr_banks * 0x2000))[
        : prg * 0x4000 + chr_banks * 0x2000]
    Path(path).write_bytes(data)
    return Path(path)


def stage_set(root, name, sha1s, scripts, mechanisms=None, game=None):
    d = Path(root) / name
    d.mkdir(parents=True, exist_ok=True)
    for s in scripts:
        (d / s).write_text("60f -\n", encoding="utf-8")
    doc = {"game": game or name, "rom": {"noIntroSha1": list(sha1s)}}
    if mechanisms:
        doc["mechanisms"] = mechanisms
    (d / L.SET_MANIFEST).write_text(json.dumps(doc), encoding="utf-8")
    return d


def bk2(path, sha1):
    with zipfile.ZipFile(path, "w") as z:
        z.writestr("Header.txt", f"MovieVersion 2\nPlatform NES\nSHA1 {sha1}\n")
    return Path(path)


# --- identity ---------------------------------------------------------------


def test_the_two_hashes_are_different_and_both_are_computed():
    with tempfile.TemporaryDirectory() as td:
        rom = ines(Path(td) / "g.nes")
        no_intro = L.no_intro_sha1(rom)
        whole = L.whole_file_sha1(rom)
        check(no_intro != whole,
              "the No-Intro hash and the whole-file hash differ",
              "a fixture where they collide would prove nothing")
        check(len(no_intro) == 40 and no_intro == no_intro.upper(),
              "hashes are upper-case hex, as the manifests are written")


def test_the_no_intro_hash_ignores_trailing_junk_but_the_file_hash_does_not():
    # The whole point of ADR-0003's range: a dump with a trailing tag still
    # matches its clean entry. A route set must match both.
    with tempfile.TemporaryDirectory() as td:
        clean = ines(Path(td) / "clean.nes")
        junked = Path(td) / "junked.nes"
        junked.write_bytes(clean.read_bytes() + b"TRAILING TAG")
        check(L.no_intro_sha1(clean) == L.no_intro_sha1(junked),
              "trailing junk does not change the No-Intro hash")
        check(L.whole_file_sha1(clean) != L.whole_file_sha1(junked),
              "it does change the whole-file hash, which is why the two are "
              "never used interchangeably")


def test_chr_ram_and_chr_rom_are_told_apart():
    with tempfile.TemporaryDirectory() as td:
        check(L.chr_kind(ines(Path(td) / "rom.nes", chr_banks=2)) == "CHR ROM",
              "a game with CHR banks is CHR ROM")
        check(L.chr_kind(ines(Path(td) / "ram.nes", chr_banks=0)) == "CHR RAM",
              "zero CHR banks is CHR RAM — F12.9 could never help it")
        other = Path(td) / "x.gb"
        other.write_bytes(b"\x00" * 64)
        check(L.chr_kind(other) == "",
              "a non-iNES file reports no CHR kind rather than guessing")


def test_only_roms_directly_in_the_folder_are_planned():
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        ines(root / "b.nes")
        ines(root / "a.nes")
        (root / "notes.txt").write_text("x", encoding="utf-8")
        nested = root / "A Game (1988)"
        nested.mkdir()
        ines(nested / "inner.nes")
        names = [p.name for p in L.find_roms(root)]
        check(names == ["a.nes", "b.nes"],
              "ROMs are sorted and non-ROM files ignored", str(names))
        check("inner.nes" not in names,
              "a sibling game folder is not descended into: a library holds one "
              "per game and a three-ROM job must stay a three-ROM job")


# --- driver resolution ------------------------------------------------------


def test_a_declared_route_set_wins_and_says_why():
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        (root / "roms").mkdir()
        rom = ines(root / "roms" / "g.nes")
        stages = root / "stages"
        stage_set(stages, "game", [L.no_intro_sha1(rom)],
                  ["mint-stage1.txt", "stage1-run.txt"], game="A Game")
        plan = L.build_plan(root / "roms", stages, 60)
        row = plan["roms"][0]
        check(row["driver"] == L.ROUTES, "driver is routes", row["driver"])
        check(row["stageCount"] == 1,
              "the mint script is not counted as a stage", str(row["stageCount"]))
        check(row["game"] == "A Game", "the set's game name is carried")
        check("declares this No-Intro SHA1" in row["reason"],
              "the reason names the evidence", row["reason"])


def test_a_set_that_declares_nothing_never_matches_and_is_listed():
    # Guessing that a folder called `zelda/` belongs to the Zelda in hand is how
    # a pack gets recorded against the wrong ROM (issue #314).
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        (root / "roms").mkdir()
        ines(root / "roms" / "g.nes")
        stages = root / "stages"
        (stages / "game").mkdir(parents=True)
        (stages / "game" / "stage1-run.txt").write_text("60f -\n", encoding="utf-8")
        plan = L.build_plan(root / "roms", stages, 60)
        check(plan["undeclaredStageSets"] == ["game"],
              "the undeclared set is reported", str(plan["undeclaredStageSets"]))
        check(plan["roms"][0]["driver"] == L.STATIC,
              "and it does not match by folder name")


def test_a_manifest_with_no_hash_is_refused_rather_than_ignored():
    with tempfile.TemporaryDirectory() as td:
        stages = Path(td) / "stages"
        d = stages / "game"
        d.mkdir(parents=True)
        (d / L.SET_MANIFEST).write_text('{"game": "x"}', encoding="utf-8")
        try:
            L.load_stage_sets(stages)
            check(False, "an empty manifest is refused", "it loaded")
        except L.LibraryError as exc:
            check("can never match a ROM" in str(exc),
                  "an empty manifest is refused, saying why", str(exc))


def test_a_movie_is_matched_on_the_whole_file_hash():
    # A bk2 Header names the file (scripts/fm2_to_bk2.py, rom_hashes). Matching
    # it against the No-Intro hash never raises — it just never fires, and every
    # movie-driven ROM quietly drops to a weaker driver.
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        (root / "roms").mkdir()
        rom = ines(root / "roms" / "g.nes")
        stages = root / "stages"
        stages.mkdir()
        bk2(root / "roms" / "run.bk2", L.whole_file_sha1(rom))
        row = L.build_plan(root / "roms", stages, 60)["roms"][0]
        check(row["driver"] == L.MOVIE, "the movie drives the run", row["driver"])
        check(row["movie"].endswith("run.bk2"), "the matching movie is named")

        bk2(root / "roms" / "run.bk2", L.no_intro_sha1(rom))
        row = L.build_plan(root / "roms", stages, 60)["roms"][0]
        check(row["driver"] == L.STATIC,
              "a bk2 carrying the No-Intro hash is NOT a match: that is the "
              "wrong hash for a bk2, and accepting it would run a movie against "
              "a dump it was not recorded on")


def test_a_movie_for_another_dump_is_not_a_match():
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        (root / "roms").mkdir()
        ines(root / "roms" / "g.nes")
        (root / "stages").mkdir()
        bk2(root / "roms" / "other.bk2", "A" * 40)
        row = L.build_plan(root / "roms", (root / "stages"), 60)["roms"][0]
        check(row["driver"] == L.STATIC, "a movie for another dump is ignored")


def test_routes_beat_a_movie_that_also_matches():
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        (root / "roms").mkdir()
        rom = ines(root / "roms" / "g.nes")
        stages = root / "stages"
        stage_set(stages, "game", [L.no_intro_sha1(rom)], ["stage1-run.txt"])
        bk2(root / "roms" / "run.bk2", L.whole_file_sha1(rom))
        row = L.build_plan(root / "roms", stages, 60)["roms"][0]
        check(row["driver"] == L.ROUTES, "(a) wins over (b)", row["driver"])
        check("and (a) wins" in row["reason"],
              "and the report says the movie also matched, so the precedence is "
              "observable rather than assumed", row["reason"])


def test_a_set_with_only_an_entry_script_is_one_power_on_run():
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        (root / "roms").mkdir()
        rom = ines(root / "roms" / "g.nes")
        stages = root / "stages"
        stage_set(stages, "game", [L.no_intro_sha1(rom)], ["mint-stage1.txt"])
        row = L.build_plan(root / "roms", stages, 60)["roms"][0]
        check(row["driver"] == L.ENTRY, "driver is entry", row["driver"])
        check(row["entry"].endswith("mint-stage1.txt"), "the entry script is named")


def test_nothing_matching_is_static_with_its_chr_kind():
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        (root / "roms").mkdir()
        ines(root / "roms" / "g.nes", chr_banks=1)
        (root / "stages").mkdir()
        row = L.build_plan(root / "roms", root / "stages", 60)["roms"][0]
        check(row["driver"] == L.STATIC, "driver is static")
        check(row["chr"] == "CHR ROM",
              "and the CHR kind is recorded, because that is what decides "
              "whether F12.9 could ever serve this ROM")


# --- the states a route set needs -------------------------------------------


def _starts(d):
    return {r: v["start"] for r, v in L.start_plan(d)["routes"].items()}


def test_a_mint_serves_the_stages_that_start_from_it():
    # The bug this exists for: `.mss` is never versioned, so a checkout has the
    # scripts and no states. The first F12.10 run recorded every Mega Man 3
    # stage from power-on and kept 1 retained frame each.
    with tempfile.TemporaryDirectory() as td:
        d = stage_set(Path(td), "game", ["A" * 40],
                      ["mint-stage1.txt", "stage1-run.txt", "stage1-probe.txt"])
        plan = L.start_plan(d)
        check(_starts(d) == {"stage1-probe": "mint mint-stage1.txt",
                             "stage1-run": "mint mint-stage1.txt"},
              "the mint serves both the stage and its probe — the README asks "
              "for the state copied beside the probe as <stage>-probe.mss",
              str(_starts(d)))
        ops = [s["op"] for s in plan["steps"]]
        check(ops == ["mint", "copy"],
              "one mint run, then a copy for the second route it serves", str(ops))


def test_the_most_specific_mint_wins():
    with tempfile.TemporaryDirectory() as td:
        d = stage_set(Path(td), "game", ["A" * 40], [
            "mint-stage1.txt", "mint-stage1-water.txt",
            "stage1-run.txt", "stage1-water.txt"])
        starts = _starts(d)
        check(starts["stage1-water"] == "mint mint-stage1-water.txt",
              "the water state comes from the water mint, not from stage1 — "
              "which is the whole reason both files exist", str(starts))
        check(starts["stage1-run"] == "mint mint-stage1.txt",
              "and stage1-run still comes from the general mint", str(starts))


def test_a_route_nothing_starts_is_skipped_not_recorded_from_power_on():
    # #407: Contra's stage2-base has no mint and no chain. Run from power-on it
    # recorded the attract demo, identical to eight other routes.
    with tempfile.TemporaryDirectory() as td:
        d = stage_set(Path(td), "game", ["A" * 40],
                      ["mint-stage1.txt", "stage1-run.txt", "stage2-base.txt"])
        info = L.start_plan(d)["routes"]["stage2-base"]
        check(info["start"] is None,
              "a route no mint or chain produces is skipped", str(info))
        check("stage2-base.mss" in info["reason"],
              "and the reason names the state it lacks", info["reason"])
        check(_starts(d)["stage1-run"] == "mint mint-stage1.txt",
              "the other routes are unaffected")


def test_a_chain_is_replayed_from_a_state_the_job_produced():
    with tempfile.TemporaryDirectory() as td:
        d = stage_set(Path(td), "game", ["A" * 40], [
            "mint-stage1.txt", "stage1-run.txt", "stage1-run-to-stage2-base.chain.txt",
            "stage2-base.txt", "stage2-base-probe.txt"])
        plan = L.start_plan(d)
        starts = _starts(d)
        check(starts["stage2-base"] == "chain stage1-run-to-stage2-base.chain.txt",
              "the chain's target starts from the chain", str(starts))
        check(starts["stage2-base-probe"] == "copy of stage2-base.mss",
              "and its probe from a copy of that state", str(starts))
        order = [(s["op"], s["state"]) for s in plan["steps"]]
        check(order.index(("mint", "stage1-run")) < order.index(("chain", "stage2-base"))
              < order.index(("copy", "stage2-base-probe")),
              "every step runs after the state it starts from exists", str(order))
        chain = next(s for s in plan["steps"] if s["op"] == "chain")
        check(chain["from"] == "stage1-run", "the chain starts from its <a>.mss", str(chain))


def test_a_chain_from_a_state_nobody_can_mint_skips_everything_after_it():
    # #407 as it is on disk: stage3-waterfall.mss cannot be re-minted from a
    # checkout (README), so every chain below it is unreachable too.
    with tempfile.TemporaryDirectory() as td:
        d = stage_set(Path(td), "game", ["A" * 40], [
            "mint-stage1.txt", "stage1-run.txt", "stage3-waterfall.txt",
            "stage3-waterfall-to-stage3-boss.chain.txt", "stage3-boss.txt",
            "stage3-boss-probe.txt"])
        routes = L.start_plan(d)["routes"]
        check(sorted(r for r, v in routes.items() if v["start"] is None)
              == ["stage3-boss", "stage3-boss-probe", "stage3-waterfall"],
              "the chain source, its target and the target's probe are skipped",
              str(routes))
        check("stage3-waterfall.mss" in routes["stage3-boss"]["reason"],
              "the reason names the state the chain is missing",
              routes["stage3-boss"]["reason"])
        check(not any(s["op"] == "chain" for s in L.start_plan(d)["steps"]),
              "and no chain is replayed from a state that does not exist")


def test_a_room_with_its_own_state_is_not_served_by_a_prefix_mint():
    # #408: `stage1-boss` shares mint-stage1's prefix, but navigation.json
    # enters that room from its own state. Paired with the mint it recorded the
    # stage-1 entry: 18 silhouettes against 155 from the wall state.
    with tempfile.TemporaryDirectory() as td:
        d = stage_set(Path(td), "game", ["A" * 40], [
            "mint-stage1.txt", "stage1-run.txt", "stage1-long.txt", "stage1-boss.txt"])
        (d / "navigation.json").write_text(json.dumps({"rooms": [
            {"name": "stage1-boss", "state": "stage1-boss.mss", "input": "stage1-boss.txt"}]}),
            encoding="utf-8")
        starts = _starts(d)
        check(starts["stage1-boss"] is None,
              "the room is skipped: no exact mint and no chain yields its state",
              str(starts))
        check(starts["stage1-long"] == "mint mint-stage1.txt",
              "stage1-long still starts at stage1-run's state, as every "
              "measurement of it did (F12.6b, F12.14)", str(starts))
        (d / "mint-stage1-boss.txt").write_text("60f -\n", encoding="utf-8")
        check(_starts(d)["stage1-boss"] == "mint mint-stage1-boss.txt",
              "an exact mint for the room does serve it")


def test_a_set_with_no_way_to_make_a_state_boots_from_power_on():
    # Metroid's stage1-run boots the game itself; it has no mint on purpose.
    with tempfile.TemporaryDirectory() as td:
        d = stage_set(Path(td), "game", ["A" * 40], ["stage1-run.txt"])
        check(_starts(d) == {"stage1-run": L.POWER_ON},
              "a set with no mint and no chain records its routes from power-on",
              str(_starts(d)))


def test_prune_drops_every_route_without_its_state_and_keeps_power_on():
    with tempfile.TemporaryDirectory() as td:
        d = Path(td)
        for n in ("a", "b", "c", "d"):
            (d / f"{n}.txt").write_text("60f -\n", encoding="utf-8")
        (d / "a.mss").write_bytes(b"MSS")
        starts = d / L.STARTS_FILE
        starts.write_text(json.dumps({"routes": {
            "a": {"start": "mint mint-a.txt", "reason": ""},
            "b": {"start": "mint mint-b.txt", "reason": ""},   # its mint failed
            "c": {"start": None, "reason": "nothing makes c.mss"},
            "d": {"start": L.POWER_ON, "reason": "boots itself"}}}), encoding="utf-8")
        n = L.prune_unstarted(d, starts)
        left = sorted(p.stem for p in d.glob("*.txt"))
        check(left == ["a", "d"] and n == 2,
              "a route whose state is missing is not left for record_stages.sh "
              "to run from power-on; a power-on route is kept", f"{left} {n}")
        routes = json.loads(starts.read_text(encoding="utf-8"))["routes"]
        check(routes["b"]["start"] is None and "b.mss" in routes["b"]["reason"],
              "a mint that failed at run time is reported as a skip", str(routes["b"]))


def test_the_real_contra_set_starts_only_what_a_checkout_can_produce():
    root = Path(__file__).resolve().parent / "stages" / "contra"
    starts = _starts(root)
    skipped = sorted(r for r, v in starts.items() if v is None)
    check(skipped == ["stage1-boss", "stage2-base", "stage3-boss", "stage3-boss-probe",
                      "stage3-waterfall", "stage3-waterfall-probe", "stage4-base",
                      "stage4-base-probe", "stage4-boss", "stage4-boss-probe"],
          "the ten routes whose states live only under runs/ are skipped", str(skipped))
    check(starts.get("stage1-long") == "mint mint-stage1.txt"
          and starts.get("stage1-2p-probe") == "copy of stage1-2p.mss",
          "and the six stage-1 routes start from their mints", str(starts))


def _mss(path, ram):
    """A save state `mss_ram.ram` can read, holding `ram` as internal RAM."""
    import struct
    import zlib
    key = b"memoryManager.internalRam\0"
    blob = key + struct.pack("<I", len(ram)) + bytes(ram)
    name = b"g.nes"
    data = (b"MSS" + struct.pack("<I", 1) + struct.pack("<I", 4) + struct.pack("<I", 0)
            + bytes(20) + zlib.compress(b"\0" * 64)
            + struct.pack("<I", len(name)) + name
            + b"\0" + struct.pack("<II", len(blob), len(blob)) + blob)
    Path(path).write_bytes(data)


def test_per_route_evidence_shows_a_mis_started_route():
    # #409: nine attract-demo routes left `seen %` at 87.6. The per-route rows
    # show what it hides: identical recordings and the stage each start is in.
    with tempfile.TemporaryDirectory() as td:
        out = Path(td) / "rom"
        work = out / "stages-src"
        work.mkdir(parents=True)
        (work / "navigation.json").write_text(json.dumps({"navigation": {
            "address": "0030", "values": [{"value": "00", "name": "stage1"},
                                          {"value": "01", "name": "stage2"}]}}),
            encoding="utf-8")
        ram = bytearray(2048)
        ram[0x30] = 1
        _mss(work / "stage2-base.mss", ram)
        routes = {"stage2-base": ("chain x.chain.txt", "T1", 3548, 358),
                  "stage3-boss": (L.POWER_ON, "DEMO", 2973, 1058),
                  "stage4-boss": (L.POWER_ON, "DEMO", 2973, 1058)}
        for name, (start, tiles, ret, sil) in routes.items():
            home = out / "stages" / name / "mesen-home"
            home.mkdir(parents=True)
            (home / "mesen.log").write_text(
                f"[HD Pack Builder] poses: {sil} silhouettes from {ret} retained "
                "OAM frames, 1 over the threshold\n", encoding="utf-8")
            tex = out / "stages" / name / "g" / "auto" / "textures"
            tex.mkdir(parents=True)
            (tex / "hires.txt").write_text(tiles, encoding="utf-8")
        (out / L.STARTS_FILE).write_text(json.dumps({"routes": dict(
            {n: {"start": v[0], "reason": ""} for n, v in routes.items()},
            **{"stage1-boss": {"start": None, "reason": "no state"}})}), encoding="utf-8")
        ev = {r["route"]: r for r in L.route_evidence(out)}
        check(ev["stage3-boss"]["sameAs"] == ["stage4-boss"],
              "two routes with byte-identical recordings name each other",
              str(ev["stage3-boss"]))
        check(ev["stage2-base"]["sameAs"] == [], "a distinct recording names nobody")
        check(ev["stage2-base"]["stageAtStart"] == "stage2 ($0030=01)",
              "the start state's stage is read off the set's RAM probe",
              str(ev["stage2-base"]["stageAtStart"]))
        check(ev["stage2-base"]["retained"] == 3548 and ev["stage2-base"]["silhouettes"] == 358,
              "retained frames and silhouettes are per route", str(ev["stage2-base"]))
        row = {"name": "Contra", "driver": L.ROUTES, "status": "recorded",
               "reason": "r", "noIntroSha1": "A" * 40, "chr": "CHR RAM",
               "stageCount": 3, "routesSkipped": ["stage1-boss"],
               "routes": list(ev.values())}
        text = L.render_report([row])
        check("## Routes: Contra" in text and "| stage3-boss | power-on |" in text
              and "stage4-boss |" in text, "the report has a per-route table", text)
        check("**stage1-boss** — no state" in text and "3 (+1 skipped)" in text,
              "and lists every route it did not record, with the reason", text)


def test_chain_and_mint_files_are_never_recorded_as_stages():
    with tempfile.TemporaryDirectory() as td:
        d = stage_set(Path(td), "game", ["A" * 40], [
            "mint-stage1.txt", "stage1-run.txt", "a-to-b.chain.txt"])
        names = [p.stem for p in L.recordable_stages(d)]
        check(names == ["stage1-run"],
              "only real stages are recordable — the same filter "
              "record_stages.sh applies, or the plan promises a run the job "
              "never makes", str(names))


# --- the report -------------------------------------------------------------


def test_a_kit_that_was_never_produced_reads_as_absent_not_as_zero():
    with tempfile.TemporaryDirectory() as td:
        got = L.kit_counts(Path(td) / "nope")
        check(all(v is None for v in got.values()),
              "every field is None for a missing kit", str(got))
        report = L.render_report([{
            "name": "G", "driver": L.STATIC, "status": "no kit", **got}])
        check("| - | - |" in report,
              "and the row prints dashes, not zeroes: a kit with zero figures "
              "and no kit at all are different facts")


def test_seen_percent_is_weighted_by_cells_not_by_surfaces():
    with tempfile.TemporaryDirectory() as td:
        kit = Path(td) / "kit"
        kit.mkdir()
        (kit / "kit-part-chr.json").write_text(json.dumps({
            "part": "chr",
            "files": [{"cells": 256, "seen": False}, {"cells": 256, "seen": True}],
            "verify": {"ran": True, "errors": 0},
        }), encoding="utf-8")
        (kit / "kit-part-sprites.json").write_text(json.dumps({
            "part": "sprites",
            "files": [{"cells": 2, "seen": True}],
            "verify": {"ran": True, "errors": 0},
        }), encoding="utf-8")
        got = L.kit_counts(kit)
        check(got["chrPages"] == 2 and got["figures"] == 1,
              "surfaces are counted per part", str(got))
        check(got["seen"] == 50.2,
              "seen % is cells on seen surfaces / all cells (258/514), not 2 of "
              "3 surfaces — one unseen CHR page hides 256 cells", str(got["seen"]))
        check(got["verify"] == 0, "verify sums the parts that ran")


def test_verify_errors_are_summed_and_a_part_that_did_not_run_is_not_a_pass():
    with tempfile.TemporaryDirectory() as td:
        kit = Path(td) / "kit"
        kit.mkdir()
        (kit / "kit-part-chr.json").write_text(json.dumps({
            "part": "chr", "files": [], "verify": {"ran": True, "errors": 3}}),
            encoding="utf-8")
        (kit / "kit-part-sprites.json").write_text(json.dumps({
            "part": "sprites", "files": [], "verify": {"ran": False}}),
            encoding="utf-8")
        got = L.kit_counts(kit)
        check(got["verify"] == 3, "errors are summed across the parts that ran",
              str(got["verify"]))
        kit2 = Path(td) / "kit2"
        kit2.mkdir()
        (kit2 / "kit-part-chr.json").write_text(json.dumps({
            "part": "chr", "files": [], "verify": {"ran": False}}),
            encoding="utf-8")
        check(L.kit_counts(kit2)["verify"] is None,
              "a kit where nothing verified reports no result, never 0 errors")


def test_retained_frames_come_from_the_recorders_own_line():
    with tempfile.TemporaryDirectory() as td:
        out = Path(td) / "rom"
        for stage, n in (("a", 1200), ("b", 300)):
            home = out / "stages" / stage / "mesen-home"
            home.mkdir(parents=True)
            (home / "mesen.log").write_text(
                f"[HD Pack Builder] poses: 5 silhouettes from {n} retained OAM "
                "frames, 5 over the threshold\n", encoding="utf-8")
        check(L.retained_frames(out) == 1500,
              "frames are summed over the run folders", str(L.retained_frames(out)))
        check(L.retained_frames(Path(td) / "none") == 0,
              "a ROM with no run reports zero rather than raising")


def test_the_report_names_the_driver_and_the_reason_for_every_rom():
    rows = [
        {"name": "A", "driver": L.ROUTES, "status": "recorded", "stageCount": 2,
         "retained": 6891, "seen": 23.5, "figures": 12, "scenery": 5,
         "maps": None, "verify": 0, "reason": "route set mm3/ declares it",
         "noIntroSha1": "A" * 40, "chr": "CHR ROM", "mechanisms": []},
        {"name": "B", "driver": L.STATIC, "status": "no kit - needs F12.9",
         "reason": "nothing matched", "noIntroSha1": "B" * 40, "chr": "CHR ROM"},
    ]
    text = L.render_report(rows)
    check("| A | `routes` |" in text and "| B | `static` |" in text,
          "each ROM has a row naming its driver")
    check("route set mm3/ declares it" in text and "nothing matched" in text,
          "and a reason a reader can act on")
    check("needs F12.9" in text,
          "a ROM the job could not record is a row, not a missing row")
    check("declaring no mechanism list" in text,
          "a route set with no ADR-0182 mechanism list is called out rather "
          "than read as covered")


def test_a_declared_mechanism_list_is_printed():
    rows = [{"name": "A", "driver": L.ROUTES, "status": "recorded",
             "reason": "r", "noIntroSha1": "A" * 40, "chr": "CHR ROM",
             "mechanisms": ["player cycles", "a boss", "the second player"]}]
    text = L.render_report(rows)
    check("the second player" in text, "the declared mechanisms are printed")
    check("declaring no mechanism list" not in text,
          "and the undeclared warning is not shown for a set that declared one")


def test_every_versioned_stage_set_names_a_dump_and_routes_that_exist():
    """The real `scripts/stages/`, not a fixture (F14.3).

    A set is bound to the exact dump its routes were authored on (#314), so
    each manifest must carry a well-formed No-Intro SHA1, no two sets may claim
    one dump, and the set must hold at least one recordable route on disk.
    Every folder must declare: an undeclared set is silently `static`, which is
    what four of the six were until F14.3.
    """
    root = Path(__file__).resolve().parent / "stages"
    sets, undeclared = L.load_stage_sets(root)
    check(not undeclared, "every versioned stage folder has a stage-set.json",
          f"undeclared: {undeclared}")
    claimed = {}
    for d in sorted(p for p in root.iterdir() if p.is_dir()):
        manifest = d / L.SET_MANIFEST
        if not manifest.is_file():
            continue
        doc = json.loads(manifest.read_text(encoding="utf-8"))
        hashes = (doc.get("rom") or {}).get("noIntroSha1")
        ok_list = isinstance(hashes, list) and bool(hashes)
        well_formed = ok_list and all(
            isinstance(h, str) and len(h) == 40 and h == h.upper()
            and all(c in "0123456789ABCDEF" for c in h) for h in hashes)
        check(well_formed, f"{d.name}: rom.noIntroSha1 is a list of 40-hex upper-case SHA1s",
              repr(hashes))
        check(isinstance(doc.get("game"), str) and doc["game"].strip(),
              f"{d.name}: names its game")
        check(isinstance(doc.get("mechanisms", []), list),
              f"{d.name}: mechanisms, when present, is a list")
        for h in hashes if ok_list else []:
            other = claimed.setdefault(str(h).upper(), d.name)
            check(other == d.name, f"{d.name}: {h} is claimed by no other set",
                  f"also claimed by {other}/")
        routes = L.recordable_stages(d)
        check(bool(routes) and all(p.is_file() and p.stat().st_size for p in routes),
              f"{d.name}: holds at least one non-empty recordable route",
              f"{[p.name for p in routes]}")
    check(len(sets) >= 6, "all six golden sets resolve through load_stage_sets",
          f"{sorted({s['name'] for s in sets.values()})}")


def main():
    tests = [
        test_the_two_hashes_are_different_and_both_are_computed,
        test_the_no_intro_hash_ignores_trailing_junk_but_the_file_hash_does_not,
        test_chr_ram_and_chr_rom_are_told_apart,
        test_only_roms_directly_in_the_folder_are_planned,
        test_a_declared_route_set_wins_and_says_why,
        test_a_set_that_declares_nothing_never_matches_and_is_listed,
        test_a_manifest_with_no_hash_is_refused_rather_than_ignored,
        test_a_movie_is_matched_on_the_whole_file_hash,
        test_a_movie_for_another_dump_is_not_a_match,
        test_routes_beat_a_movie_that_also_matches,
        test_a_set_with_only_an_entry_script_is_one_power_on_run,
        test_nothing_matching_is_static_with_its_chr_kind,
        test_a_mint_serves_the_stages_that_start_from_it,
        test_the_most_specific_mint_wins,
        test_a_route_nothing_starts_is_skipped_not_recorded_from_power_on,
        test_a_chain_is_replayed_from_a_state_the_job_produced,
        test_a_chain_from_a_state_nobody_can_mint_skips_everything_after_it,
        test_a_room_with_its_own_state_is_not_served_by_a_prefix_mint,
        test_a_set_with_no_way_to_make_a_state_boots_from_power_on,
        test_prune_drops_every_route_without_its_state_and_keeps_power_on,
        test_the_real_contra_set_starts_only_what_a_checkout_can_produce,
        test_per_route_evidence_shows_a_mis_started_route,
        test_chain_and_mint_files_are_never_recorded_as_stages,
        test_a_kit_that_was_never_produced_reads_as_absent_not_as_zero,
        test_seen_percent_is_weighted_by_cells_not_by_surfaces,
        test_verify_errors_are_summed_and_a_part_that_did_not_run_is_not_a_pass,
        test_retained_frames_come_from_the_recorders_own_line,
        test_the_report_names_the_driver_and_the_reason_for_every_rom,
        test_a_declared_mechanism_list_is_printed,
        test_every_versioned_stage_set_names_a_dump_and_routes_that_exist,
    ]
    for t in tests:
        t()
    print(f"\n{len(tests) - len(_FAILURES)}/{len(tests)} cases passed")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
