#!/usr/bin/env python3
"""Tests for scripts/cheat_web_lookup.py (ADR-0245 section 4, P.12, #923).

Every case runs over the real libretro-database pages committed next to this
file (castlevania-usa.cht, contra-usa.cht) with a fake fetcher and a fake
headless session, so no network and no emulator are needed. The fake session
is a RAM model: each address has an "off" trace, and a code listed in
`pinning` holds its value on its address while it is on. What the script
offers is then decided by the check alone, which is what these cases assert.

Run: python3 tests/fixtures/cheat-web-lookup/test_cheat_web_lookup.py
"""
from __future__ import annotations

import io
import json
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
import cheat_web_lookup as lookup  # noqa: E402

CASTLEVANIA = HERE / "castlevania-usa.cht"
CONTRA = HERE / "contra-usa.cht"
ROM_BYTES = b"NES\x1a" + bytes(range(256)) * 64


class FakeSession:
    """Stands in for step_emu.StepEmu. `off` maps an address to the value it
    holds on each frame with no cheat; a cheat in `pinning` writes its value
    every frame, and a cheat not in it does nothing."""

    def __init__(self, world, cheats):
        self.world = world
        self.cheats = cheats
        self.frame = 0
        world["sessions"].append(list(cheats))

    def play(self, frames, buttons):
        self.frame += frames

    def save(self):
        return 1

    def save_file(self, handle, path):
        Path(path).write_bytes(b"MSS")

    def load_file(self, path):
        self.frame = 0

    def run(self, frames):
        self.frame += frames

    def read_ram(self, *addresses):
        values = []
        for address in addresses:
            value = self.world["off"].get(address, lambda f: f & 0xFF)(self.frame)
            for code in self.cheats:
                pinned = self.world["pinning"].get(code)
                if pinned is not None and pinned[0] == address:
                    value = pinned[1]
            values.append(value)
        return values

    def close(self):
        pass


def make_world(pinning, off=None):
    world = {"pinning": pinning, "off": off or {}, "sessions": []}
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


class TheCheckDecidesWhatIsOffered(LookupCase):
    def test_only_codes_that_hold_on_and_differ_off_are_offered(self):
        #0071:63 (99 hearts) really pins its address; 002A:05 (5 lives) is a
        #code that does nothing on this copy; 0045:3D targets an address whose
        #"off" trace already holds the promised value every frame.
        world, factory = make_world(
            {"0071:63": (0x71, 0x63), "0045:3D": (0x45, 0x3D)},
            off={0x45: lambda f: 0x3D})
        result = self.run_lookup(CASTLEVANIA, factory)
        offered = {code["code"] for code in result["codes"]}
        self.assertEqual(offered, {"0071:63"})
        self.assertNotIn("002A:05", offered)
        self.assertNotIn("0045:3D", offered)
        for code in result["codes"]:
            self.assertEqual(code["label"], "found online, checked on your copy")
            self.assertEqual(code["on"], "10/10")

    def test_a_code_that_slips_on_one_on_frame_fails(self):
        class Flaky(FakeSession):
            def read_ram(self, *addresses):
                values = super().read_ram(*addresses)
                return [0x00 if self.cheats and self.frame == 7 else v for v in values]

        world = {"pinning": {"0071:63": (0x71, 0x63)}, "off": {}, "sessions": []}
        result = self.run_lookup(CASTLEVANIA, lambda r, c, w: Flaky(world, c))
        self.assertEqual(result["codes"], [])
        self.assertGreater(result["counts"]["checked"], 0)
        self.assertEqual(result["counts"]["passed"], 0)

    def test_codes_with_no_ram_target_are_never_checked_nor_offered(self):
        #Contra's page mixes RAM writes with Game Genie codes (ZEIIXZ...).
        everything = {}
        for entry in lookup.parse_cht(CONTRA.read_text()):
            everything[entry.code] = (0x10, 0x01)  # would "pass" if ever run
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

    def test_counts_add_up_over_the_real_pages(self):
        for page in (CASTLEVANIA, CONTRA):
            world, factory = make_world({})
            result = self.run_lookup(page, factory)
            counts = result["counts"]
            rejected = counts["undecodable"] + counts["multi_part"] + counts["no_ram_target"]
            self.assertEqual(counts["entries"], rejected + counts["checked"], page.name)
            self.assertEqual(counts["passed"], 0)


class TheRomNeverLeaves(LookupCase):
    def test_the_fetcher_only_sees_page_urls_on_the_allowed_host(self):
        seen = []

        def transport(url, timeout):
            seen.append(url)
            if "%28USA%29" not in url:
                raise lookup.FetchError(f"{url} answered HTTP 404")
            return CASTLEVANIA.read_text(), url

        world, factory = make_world({"0071:63": (0x71, 0x63)})
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
        world, factory = make_world({"0071:63": (0x71, 0x63)})
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
        self.assertEqual(lookup.CHECK_FRAMES, 120)
        self.assertEqual({c["code"] for c in result["codes"]}, {"0071:63"})

    def test_gb_is_refused(self):
        self.assertEqual(lookup.main(["--rom", str(self.rom), "--console", "gb",
                                      "--page-file", str(CASTLEVANIA)]), 2)


if __name__ == "__main__":
    unittest.main(verbosity=1)
