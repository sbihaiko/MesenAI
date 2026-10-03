namespace Mesen.HeadlessTests;

//scripts/gen_synthetic_nrom.py, byte for byte: NROM-256, a JMP-to-self at the
//reset vector, an RTI for NMI/IRQ, and 8 KB of blank CHR ROM. Copyright-free,
//so a test can load a real game without the ROM library.
internal static class SyntheticNrom
{
	public static byte[] Build()
	{
		const int prgSize = 32 * 1024;
		const int chrSize = 8 * 1024;
		byte[] rom = new byte[16 + prgSize + chrSize];
		rom[0] = (byte)'N';
		rom[1] = (byte)'E';
		rom[2] = (byte)'S';
		rom[3] = 0x1A;
		rom[4] = prgSize / 16384;
		rom[5] = chrSize / 8192;

		int prg = 16;
		for(int i = 0; i < prgSize; i++) {
			rom[prg + i] = 0xEA;
		}
		rom[prg + 0x0000] = 0x4C;
		rom[prg + 0x0001] = 0x00;
		rom[prg + 0x0002] = 0x80;
		rom[prg + 0x0003] = 0x40;
		rom[prg + 0x7FFA] = 0x03;
		rom[prg + 0x7FFB] = 0x80;
		rom[prg + 0x7FFC] = 0x00;
		rom[prg + 0x7FFD] = 0x80;
		rom[prg + 0x7FFE] = 0x03;
		rom[prg + 0x7FFF] = 0x80;
		return rom;
	}
}
