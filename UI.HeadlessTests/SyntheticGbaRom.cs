using System;

namespace Mesen.HeadlessTests;

//What the synthetic GBA cartridge leaves on screen. Every scene shares the same
//VRAM, palette and BG0 setup and differs only in the DISPCNT value written
//last, so a scene's side columns can only differ because of that register.
internal enum GbaScene
{
	//BG mode 0, BG0 on: a text BG with a 256x256 map
	TextBg,
	//BG mode 1, BG0 and BG2 on: the same text BG with an affine BG on the row
	AffineBg,
	//BG mode 3, BG2 on: a bitmap mode, no map at all
	Bitmap,
	//BG mode 0, BG0 on, DISPCNT bit 7 (forced blank)
	ForcedBlank,
}

//A 16 KB Game Boy Advance ROM whose whole program is a ten-instruction ARM
//copy loop, written here by hand - the GBA sibling of SyntheticGbRom. It is
//authored in this repository, so it carries no third-party code or data: no
//commercial ROM, no BIOS, and no Nintendo logo (the header's logo area is left
//zero, which the core does not check). See Fixtures/gba/README.md for the
//licence.
//
//The program walks a table of (destination, source, halfword count) triples in
//ROM, copies each block with LDRH/STRH, and spins once it reads a zero count.
//The table loads the palette, three 4bpp tiles, BG0's map, BG0CNT, the BG0
//scroll and finally DISPCNT. It never enables an interrupt, so it never
//vectors into the BIOS: the core zero-fills the BIOS when it has none, and
//GbaConfig.SkipBootScreen starts the CPU at 0x08000000.
//
//The picture it draws (ADR-0253 W.7): BG0 uses a 256x256 map whose columns
//0-29 hold tile 1 (palette entry 1, red) and whose columns 30-31 - the 16 px the
//240-px window never shows at scroll 0 - hold tile 2 (palette entry 2, green).
//The backdrop is blue. So the Reveal's side columns are green only if they
//really sample the map's hidden columns, and red would mean a copy of the edge.
internal static class SyntheticGbaRom
{
	private const int RomSize = 16 * 1024;
	private const uint RomBase = 0x08000000;

	private const int CodeOffset = 0xC0;
	private const int TableOffset = 0x100;
	private const int PaletteOffset = 0x200;
	private const int TilesOffset = 0x220;
	private const int MapOffset = 0x400;
	private const int RegsOffset = 0xC00;

	//BGR555
	public const ushort Backdrop = 0x7C00; //blue
	public const ushort ShownColumnColor = 0x001F; //red
	public const ushort HiddenColumnColor = 0x03E0; //green

	private static readonly uint[] Program = {
		0xE59F0020, //0xC0        ldr   r0, =table      (literal at 0xE8)
		0xE8B0000E, //0xC4 next:  ldmia r0!, {r1-r3}  (dest, src, halfwords)
		0xE3530000, //0xC8        cmp   r3, #0
		0x0A000004, //0xCC        beq   done
		0xE0D240B2, //0xD0 copy:  ldrh  r4, [r2], #2
		0xE0C140B2, //0xD4        strh  r4, [r1], #2
		0xE2533001, //0xD8        subs  r3, r3, #1
		0x1AFFFFFB, //0xDC        bne   copy
		0xEAFFFFF7, //0xE0        b     next
		0xEAFFFFFE, //0xE4 done:  b     done
		RomBase + TableOffset, //0xE8 literal: the table
	};

	public static byte[] Build(GbaScene scene)
	{
		byte[] rom = new byte[RomSize];

		//Entry point at 0x08000000: B 0xC0, over the header.
		WriteWord(rom, 0x00, 0xEA00002E);

		//Header: title, game code, maker, the fixed 0x96 byte, then the
		//complement check over 0xA0-0xBC the way a real cartridge carries it.
		WriteAscii(rom, 0xA0, "MESENCE-GBA");
		WriteAscii(rom, 0xAC, "ZMCE");
		WriteAscii(rom, 0xB0, "00");
		rom[0xB2] = 0x96;
		byte check = 0;
		for(int i = 0xA0; i <= 0xBC; i++) {
			check = (byte)(check - rom[i]);
		}
		rom[0xBD] = (byte)(check - 0x19);

		for(int i = 0; i < Program.Length; i++) {
			WriteWord(rom, CodeOffset + i * 4, Program[i]);
		}

		//Palette: backdrop, then the two colors the tiles use.
		WriteHalf(rom, PaletteOffset, Backdrop);
		WriteHalf(rom, PaletteOffset + 2, ShownColumnColor);
		WriteHalf(rom, PaletteOffset + 4, HiddenColumnColor);

		//Tiles 0-2 (4bpp, 32 bytes each): transparent, all index 1, all index 2.
		for(int i = 0; i < 16; i++) {
			WriteHalf(rom, TilesOffset + 32 + i * 2, 0x1111);
			WriteHalf(rom, TilesOffset + 64 + i * 2, 0x2222);
		}

		//BG0's 32x32 map: the 30 columns the window shows hold tile 1, the two
		//it does not hold tile 2.
		for(int row = 0; row < 32; row++) {
			for(int col = 0; col < 32; col++) {
				WriteHalf(rom, MapOffset + (row * 32 + col) * 2, (ushort)(col < 30 ? 1 : 2));
			}
		}

		//BG0CNT: priority 0, char base 0, 4bpp, screen base 8 (0x06004000), 256x256.
		WriteHalf(rom, RegsOffset, 0x0800);
		//BG0HOFS, BG0VOFS
		WriteHalf(rom, RegsOffset + 4, 0);
		WriteHalf(rom, RegsOffset + 6, 0);
		WriteHalf(rom, RegsOffset + 8, DisplayControl(scene));

		int entry = TableOffset;
		entry = WriteEntry(rom, entry, 0x05000000, PaletteOffset, 3);
		entry = WriteEntry(rom, entry, 0x06000000, TilesOffset, 48);
		entry = WriteEntry(rom, entry, 0x06004000, MapOffset, 1024);
		entry = WriteEntry(rom, entry, 0x04000008, RegsOffset, 1);
		entry = WriteEntry(rom, entry, 0x04000010, RegsOffset + 4, 2);
		entry = WriteEntry(rom, entry, 0x04000000, RegsOffset + 8, 1);
		WriteEntry(rom, entry, 0, 0, 0);

		return rom;
	}

	private static ushort DisplayControl(GbaScene scene)
	{
		const ushort Bg0 = 0x0100;
		const ushort Bg2 = 0x0400;
		const ushort ForcedBlank = 0x0080;
		return scene switch {
			GbaScene.TextBg => Bg0,
			GbaScene.AffineBg => 1 | Bg0 | Bg2,
			GbaScene.Bitmap => 3 | Bg2,
			GbaScene.ForcedBlank => Bg0 | ForcedBlank,
			_ => throw new ArgumentOutOfRangeException(nameof(scene)),
		};
	}

	private static int WriteEntry(byte[] rom, int offset, uint destination, int sourceOffset, uint halfwords)
	{
		WriteWord(rom, offset, destination);
		WriteWord(rom, offset + 4, halfwords == 0 ? 0 : RomBase + (uint)sourceOffset);
		WriteWord(rom, offset + 8, halfwords);
		return offset + 12;
	}

	private static void WriteWord(byte[] rom, int offset, uint value)
	{
		WriteHalf(rom, offset, (ushort)(value & 0xFFFF));
		WriteHalf(rom, offset + 2, (ushort)(value >> 16));
	}

	private static void WriteHalf(byte[] rom, int offset, ushort value)
	{
		rom[offset] = (byte)(value & 0xFF);
		rom[offset + 1] = (byte)(value >> 8);
	}

	private static void WriteAscii(byte[] rom, int offset, string text)
	{
		for(int i = 0; i < text.Length; i++) {
			rom[offset + i] = (byte)text[i];
		}
	}
}
