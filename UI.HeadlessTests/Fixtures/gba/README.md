# GBA test fixture (issue #954, ADR-0253 W.7)

The headless GBA coverage (`UI.HeadlessTests/GbaWidescreenRevealTests.cs`) runs a
**synthetic** Game Boy Advance ROM. No ROM file is committed: the cartridge is
built in memory, at test time, by `UI.HeadlessTests/SyntheticGbaRom.cs`, and
written to a per-test temp folder that the test deletes afterwards.

## Provenance

- Authored in this repository, by hand, for this test. It is not derived from
  any commercial game, homebrew release, SDK, or toolchain output.
- The program is ten hand-assembled ARM instructions (a table-driven
  halfword copy loop, then an endless branch); the data is three 4bpp tiles, a
  32x32 BG0 map, three palette entries and four I/O register values.
- The header's Nintendo logo area (0x04-0x9F) is left **zero**: the logo is not
  reproduced. The core does not check it.
- No BIOS is involved or committed. The test sets `GbaConfig.SkipBootScreen`, so
  the CPU starts at 0x08000000; the core zero-fills the BIOS when it has none,
  and the program never enables an interrupt, so it never vectors into it.
- No commercial ROM is committed anywhere for this test.

## Licence

The builder and every ROM it produces are part of this repository and are
distributed under the repository's licence, the **GNU General Public License
v3.0** (`LICENSE` at the repository root) - the same terms as
`SyntheticGbRom.cs` and `SyntheticNrom.cs`. Being authored here, the ROM has
no third-party rights attached.

## What it draws

Every scene shares the same VRAM, palette and BG0 setup; only the DISPCNT value
written last differs (`GbaScene`):

| Scene         | DISPCNT                  | Expected Reveal sides (284x160, N = 22)                  |
|---------------|--------------------------|----------------------------------------------------------|
| `TextBg`      | mode 0, BG0              | map columns 30-31 (green) + 6 px black where the map wraps |
| `AffineBg`    | mode 1, BG0 + BG2        | black                                                    |
| `Bitmap`      | mode 3, BG2              | black                                                    |
| `ForcedBlank` | mode 0, BG0, bit 7       | white, like the picture                                  |

BG0's columns 0-29 (the 240 px the window shows at scroll 0) are red and its
columns 30-31 are green, so green beside the picture can only come from the
map's hidden columns. The backdrop is blue.
