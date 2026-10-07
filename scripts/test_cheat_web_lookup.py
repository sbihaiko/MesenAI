#!/usr/bin/env python3
"""Tests for scripts/cheat_web_lookup.py (ADR-0245 section 4, P.12, #923, #934).

Every case runs over the real libretro-database pages committed under
tests/fixtures/cheat-web-lookup/ (castlevania-usa.cht, contra-usa.cht) with a fake fetcher and a fake
headless session, so no network and no emulator are needed. The fake session
models a game: its whole internal RAM and its frame checksum are a function of
the frame, and a code listed in `effects` changes that game the way the entry
says - from which frame the RAM diverges, how many emulated CPU reads hit the
target, whether the run crashes or freezes. What the script offers is then
decided by the check alone, which is what these cases assert.

One test class per condition of ADR-0245 Decision 4 as amended by #934's
ratification: (1) identical off/off traces, (2) on/off divergence within the
window, (3) CPU-read hits on the target, (4) crash/freeze rejection, (5) an
inconclusive check fails closed, (6) RAM-only scope, (7) the label is "checked".

Run: python3 scripts/test_cheat_web_lookup.py (also run by `make python-tests`)
"""
from __future__ import annotations

import io
import json
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
HERE = ROOT / "tests" / "fixtures" / "cheat-web-lookup"
sys.path.insert(0, str(ROOT / "scripts"))
import cheat_web_lookup as lookup  # noqa: E402
import step_emu  # noqa: E402

CASTLEVANIA = HERE / "castlevania-usa.cht"
CONTRA = HERE / "contra-usa.cht"
ROM_BYTES = b"NES\x1a" + bytes(range(256)) * 64

#A code that changes the game from frame 3, and whose target the CPU reads.
WORKING = {"diverge_at": 3, "reads": 40, "hits": 40}


class FakeSession:
    """Stands in for step_emu.StepEmu. With no cheat, RAM and the frame are a
    function of the frame alone; `effects[code]` describes what a code does
    when it is on (see WORKING). `noisy_off` makes two "off" runs disagree, and
    `no_hits` makes the read counter unavailable."""

    def __init__(self, world, cheats):
        self.world = world
        self.cheats = cheats
        self.frame = 0
        self.watched = None
        world["sessions"].append(list(cheats))
        self.serial = len(world["sessions"])

    def effect(self):
        for code in self.cheats:
            if code in self.world["effects"]:
                return self.world["effects"][code]
        return {}

    def play(self, frames, buttons):
        self.frame += frames

    def save(self):
        return 1

    def save_file(self, handle, path):
        Path(path).write_bytes(b"MSS")

    def load_file(self, path):
        self.frame = 0
        return 1

    def run(self, frames):
        crash_at = self.effect().get("crash_at")
        if crash_at is not None and self.frame + frames >= crash_at:
            raise step_emu.StepEmuError(f"run: STALLED at frame {self.frame}")
        self.frame += frames
        return self.frame

    def row(self):
        effect = self.effect()
        frame = self.frame
        if effect.get("freeze_from") is not None:
            frame = min(frame, effect["freeze_from"])
        ram = bytearray(0x800)
        ram[0] = frame & 0xFF
        ram[1] = (frame * 7) & 0xFF
        if effect.get("diverge_at") is not None and self.frame >= effect["diverge_at"]:
            ram[0x40] = 0xEE
        if self.world.get("noisy_off") and not self.cheats:
            ram[0x41] = self.serial & 0xFF
        return bytes(ram)

    def read_ram(self, *items):
        assert list(items) == [(0, 0x7FF)], items
        return [self.row()]

    def capture(self):
        return self.frame, 256, 240, f"{sum(self.row()) & 0xFFFFFFFF:08x}"

    def watch_reads(self, address, compare=-1):
        if self.world.get("no_hits"):
            raise step_emu.StepEmuError("watch: unknown verb")
        self.watched = (address, compare)

    def read_hits(self):
        effect = self.effect()
        return effect.get("reads", 0), effect.get("hits", 0)

    def close(self):
        pass


def make_world(effects=None, **flags):
    world = {"effects": effects or {}, "sessions": [], **flags}
    return world, (lambda rom, cheats, work: FakeSession(world, cheats))


class LookupCase(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.rom = Path(self.tmp.name) / "Castlevania (U) (PRG0) [!].nes"
        self.rom.write_bytes(ROM_BYTES)
        self.state = Path(self.tmp.name) / "state.mss"

    def tearDown(self):
        self.tmp.cleanup()

    def run_lookup(self, page_file, world_factory, **kwargs):
        return lookup.lookup(rom=self.rom, page_file=page_file, state=self.state,
                             session_factory=world_factory, frames=10, **kwargs)

    def offered(self, result):
        return {code["code"] for code in result["codes"]}


class Condition1OffOffTracesAreIdentical(LookupCase):
    def test_two_off_runs_that_disagree_offer_nothing(self):
        world, factory = make_world({"0071:63": WORKING}, noisy_off=True)
        result = self.run_lookup(CASTLEVANIA, factory)
        self.assertEqual(result["determinism"], "differs")
        self.assertEqual(result["codes"], [])
        self.assertEqual(result["counts"]["reasons"].get("inconclusive"),
                         result["counts"]["checked"])
        #Two "off" runs, and no candidate is put to a check it cannot win.
        self.assertEqual(world["sessions"][1:3], [[], []])
        self.assertEqual(len(world["sessions"]), 3)


class Condition2OnOffDivergenceWithinTheWindow(LookupCase):
    def test_a_code_that_changes_the_game_inside_the_window_passes(self):
        world, factory = make_world({"0071:63": WORKING,
                                     "002A:05": {**WORKING, "diverge_at": 50}})
        result = self.run_lookup(CASTLEVANIA, factory)
        self.assertEqual(result["determinism"], "identical")
        self.assertEqual(self.offered(result), {"0071:63"})
        first = next(c for c in result["codes"] if c["code"] == "0071:63")
        self.assertLessEqual(first["diverged_at"], 10)
        self.assertGreater(result["counts"]["reasons"]["no-effect"], 0)

    def test_the_fixed_window_is_120_frames(self):
        self.assertEqual(lookup.CHECK_FRAMES, 120)


class Condition3CpuReadHitsOnTheTarget(LookupCase):
    def test_divergence_without_a_cpu_read_hit_is_not_offered(self):
        world, factory = make_world({
            "0071:63": {**WORKING, "reads": 0, "hits": 0},
            "002A:05": {**WORKING, "reads": 12, "hits": 0},
            "0042:32": WORKING})
        result = self.run_lookup(CASTLEVANIA, factory)
        self.assertEqual(self.offered(result), {"0042:32"})
        #Every candidate but 0042:32's entries had no hit: the two above, and
        #the codes this world gives no effect at all.
        working = sum(1 for c in lookup.collect(lookup.parse_cht(CASTLEVANIA.read_text()))[0]
                      if c.code == "0042:32")
        self.assertEqual(result["counts"]["reasons"]["not-read"],
                         result["counts"]["checked"] - working)
        self.assertNotIn("no-effect", result["counts"]["reasons"])

    def test_the_counter_is_armed_on_the_target_with_the_codes_compare(self):
        armed = []

        class Recording(FakeSession):
            def watch_reads(self, address, compare=-1):
                armed.append((tuple(self.cheats), address, compare))

        world = {"effects": {"0071:63": WORKING}, "sessions": []}
        self.run_lookup(CASTLEVANIA, lambda r, c, w: Recording(world, c))
        self.assertIn((("0071:63",), 0x71, -1), armed)
        #Only "on" sessions arm it: an "off" run has no cheat to count.
        self.assertTrue(all(cheats for cheats, _, _ in armed))


class Condition4CrashAndFreezeAreRejected(LookupCase):
    def test_a_code_that_crashes_or_freezes_the_game_is_not_offered(self):
        world, factory = make_world({
            "0071:63": {**WORKING, "crash_at": 4},
            "002A:05": {**WORKING, "freeze_from": 2},
            "0042:32": WORKING})
        result = self.run_lookup(CASTLEVANIA, factory)
        self.assertEqual(self.offered(result), {"0042:32"})
        reasons = result["counts"]["reasons"]
        self.assertEqual(reasons["crashed"], 2)
        self.assertEqual(reasons["frozen"], 2)


class Condition5InconclusiveFailsClosed(LookupCase):
    def test_no_hit_evidence_means_nothing_is_offered(self):
        world, factory = make_world({"0071:63": WORKING}, no_hits=True)
        result = self.run_lookup(CASTLEVANIA, factory)
        self.assertEqual(result["codes"], [])
        self.assertEqual(result["counts"]["passed"], 0)
        self.assertEqual(result["counts"]["reasons"]["inconclusive"],
                         result["counts"]["checked"])

    def test_counts_add_up_over_the_real_pages(self):
        for page in (CASTLEVANIA, CONTRA):
            world, factory = make_world({})
            result = self.run_lookup(page, factory)
            counts = result["counts"]
            rejected = counts["undecodable"] + counts["multi_part"] + counts["no_ram_target"]
            self.assertEqual(counts["entries"], rejected + counts["checked"], page.name)
            self.assertEqual(sum(counts["reasons"].values()), counts["checked"])
            self.assertEqual(counts["passed"], 0)


class Condition6RamOnlyScope(LookupCase):
    def test_codes_with_no_ram_target_are_never_checked_nor_offered(self):
        #Contra's page mixes RAM writes with Game Genie codes (ZEIIXZ...).
        everything = {entry.code: WORKING for entry in lookup.parse_cht(CONTRA.read_text())}
        world, factory = make_world(everything)
        result = self.run_lookup(CONTRA, factory)
        launched = {code for cheats in world["sessions"] for code in cheats}
        decoded_ram = {c.code for c in lookup.collect(
            lookup.parse_cht(CONTRA.read_text()))[0]}
        self.assertNotIn("ZEIIXZ", launched)
        self.assertTrue(launched <= decoded_ram)
        self.assertGreater(result["counts"]["no_ram_target"], 0)
        for code in result["codes"]:
            self.assertLess(int(code["address"], 16), 0x0800)


class Condition7TheLabelIsChecked(LookupCase):
    def test_every_offered_code_is_labelled_checked_never_works_or_safe(self):
        world, factory = make_world({"0071:63": WORKING})
        result = self.run_lookup(CASTLEVANIA, factory)
        self.assertTrue(result["codes"])
        for code in result["codes"]:
            self.assertEqual(code["label"], "found online, checked on your copy")
            self.assertEqual(code["verdict"], "checked")
            words = code["label"].lower()
            self.assertNotIn("works", words)
            self.assertNotIn("safe", words)


class TheRomNeverLeaves(LookupCase):
    def test_the_fetcher_only_sees_page_urls_on_the_allowed_host(self):
        seen = []

        def transport(url, timeout):
            seen.append(url)
            if "%28USA%29" not in url:
                raise lookup.FetchError(f"{url} answered HTTP 404")
            return CASTLEVANIA.read_text(), url

        world, factory = make_world({"0071:63": WORKING})
        result = lookup.lookup(rom=self.rom, state=self.state, frames=10,
                               session_factory=factory, transport=transport)
        self.assertEqual([c["code"] for c in result["codes"]][:1], ["0071:63"])
        rom_sha = lookup.file_sha256(self.rom)
        for url in seen:
            self.assertTrue(url.startswith("https://raw.githubusercontent.com/"), url)
            self.assertNotIn(rom_sha, url)
            self.assertNotIn(self.rom.name, url)
        self.assertIn("Castlevania%20%28USA%29.cht", seen[-1])

    def test_a_redirect_off_the_host_list_is_refused(self):
        def transport(url, timeout):
            return CASTLEVANIA.read_text(), "https://evil.example/c.cht"

        world, factory = make_world({})
        with self.assertRaises(lookup.LookupError):
            lookup.lookup(rom=self.rom, state=self.state, session_factory=factory,
                          transport=transport, page="https://raw.githubusercontent.com/x.cht")
        self.assertEqual(world["sessions"], [])


class TheCommandLine(LookupCase):
    def test_main_prints_only_passing_codes_and_records_n(self):
        world, factory = make_world({"0071:63": WORKING})
        out = io.StringIO()
        old = sys.stdout
        sys.stdout = out
        try:
            code = lookup.main(["--rom", str(self.rom), "--page-file", str(CASTLEVANIA),
                                "--state", str(self.state)], session_factory=factory)
        finally:
            sys.stdout = old
        self.assertEqual(code, 0)
        result = json.loads(out.getvalue())
        self.assertEqual(result["frames"], lookup.CHECK_FRAMES)
        self.assertEqual({c["code"] for c in result["codes"]}, {"0071:63"})

    def test_gb_is_refused(self):
        self.assertEqual(lookup.main(["--rom", str(self.rom), "--console", "gb",
                                      "--page-file", str(CASTLEVANIA)]), 2)


if __name__ == "__main__":
    unittest.main(verbosity=1)
