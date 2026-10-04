namespace Mesen.HeadlessTests;

//A 32 KB Game Boy ROM whose whole program is `JR -2` at 0x0150, jumped to from
//the entry point. Copyright-free, so a test can load a real Game Boy cartridge
//without the ROM library - the Game Boy sibling of SyntheticNrom.
//
//The header is the standard one, at the offset the core reads it from
//(Gameboy::HeaderOffset = 0x134), with the checksums computed the way a real
//cartridge carries them. Gameboy::GetHeader validates none of it: it only looks
//at the logo while deciding whether a ROM larger than 32 KB holds an MMM01
//header at the end of the file, which is why 32 KB of mostly zeros is enough.
internal static class SyntheticGbRom
{
	private const int HeaderOffset = 0x134;
	private const int RomSize = 32 * 1024;

	public static byte[] Build()
	{
		byte[] rom = new byte[RomSize];

		//Entry point: NOP, then JP 0150.
		rom[0x100] = 0x00;
		rom[0x101] = 0xC3;
		rom[0x102] = 0x50;
		rom[0x103] = 0x01;

		//The program: JR -2, a cartridge that spins on itself and never
		//writes anything. Enough for a save state to have a frame to hold.
		rom[0x150] = 0x18;
		rom[0x151] = 0xFE;

		//Title, and the fields the model is resolved from. CartType 0 is ROM
		//ONLY, 32 KB, no cartridge RAM; CgbFlag 0 makes this a DMG cartridge,
		//which is the model GetEffectiveModel picks from the default config.
		WriteAscii(rom, 0x134, "MESENCE-GBT");
		rom[0x143] = 0x00;
		rom[0x146] = 0x00;
		rom[0x147] = 0x00;
		rom[0x148] = 0x00;
		rom[0x149] = 0x00;
		rom[0x14A] = 0x01;
		rom[0x14B] = 0x33;
		rom[0x14C] = 0x00;

		//Header checksum: 0x134..0x14C, folded by `x = x - byte - 1`.
		byte headerChecksum = 0;
		for(int i = HeaderOffset; i <= 0x14C; i++) {
			headerChecksum = (byte)(headerChecksum - rom[i] - 1);
		}
		rom[0x14D] = headerChecksum;

		//Global checksum: the sum of every other byte, big-endian, the two
		//checksum bytes themselves counting as zero.
		int sum = 0;
		for(int i = 0; i < rom.Length; i++) {
			if(i != 0x14E && i != 0x14F) {
				sum += rom[i];
			}
		}
		rom[0x14E] = (byte)((sum >> 8) & 0xFF);
		rom[0x14F] = (byte)(sum & 0xFF);

		return rom;
	}

	private static void WriteAscii(byte[] rom, int offset, string text)
	{
		for(int i = 0; i < text.Length; i++) {
			rom[offset + i] = (byte)text[i];
		}
	}
}
