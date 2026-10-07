using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.BoxArt
{
	//#1039 (ADR-0265 section 3): the seam between the shipped chain and the art
	//collection is the SHA-1 -> No-Intro record the app's own table answers with,
	//and this file is the answer to the review that found that seam returning a
	//stub null. With a stub, `NoIntroName` answered nothing for every dump, so no
	//tile ever sent a request, no cover could ever appear, and the library was
	//art-less in silence while every other test in this folder stayed green - all
	//of them proved the rules against a table the test itself built.
	//
	//`NoIntroNameTable.ForSha1` is that seam, and it is the exact call
	//MainWindowViewModel's `NoIntroName` makes, so these cases fail if it ever goes
	//back to answering nothing. The expected values are the same observed generator
	//output `NoIntroNameTableTests` pins (ADR-0003's rule for a hash golden, one per
	//console): an independent source of truth rather than a recomputation of the
	//lookup being tested.
	public class BoxArtNameSeamTests
	{
		[Theory]
		[InlineData("facee9c577a5262dbe33ac4930bb0b58c8c037f7", RomConsole.Nes, "Super Mario Bros. (World)")]
		[InlineData("74591cc9501af93873f9a5d3eb12da12c0723bbc", RomConsole.GameBoy, "Tetris (World) (Rev 1)")]
		[InlineData("f2f52230b536214ef7c9924f483392993e226cfb", RomConsole.GameBoyColor, "Pokemon - Crystal Version (USA, Europe) (Rev 1)")]
		[InlineData("41cb23d8dccc8ebd7c649cd8fbb58eeace6e2fdc", RomConsole.GameBoyAdvance, "Pokemon - FireRed Version (USA, Europe)")]
		[InlineData("8cecf8ed0f765163b2657be1b0a3ce2a9cb767f4", RomConsole.MasterSystem, "Alex Kidd in Miracle World (USA, Europe)")]
		[InlineData("39a91b5ad6b139ca5d8ac60b78520210e84944bd", RomConsole.Sg1000, "Flicky (Japan, New Zealand) (Ja)")]
		[InlineData("11241be4082f6f9d057488ae75ccdd482f623f8c", RomConsole.GameGear, "Sonic Blast (World)")]
		public void The_seam_answers_the_name_and_the_console_the_shipped_table_files_a_dump_under(string sha1, RomConsole console, string name)
		{
			NoIntroRomName? rom = NoIntroNameTable.ForSha1(sha1);

			//ADR-0265 section 3: this is the only thing a tile needs before a request
			//can be made at all - "with no name there is nothing to ask for".
			Assert.NotNull(rom);
			Assert.Equal(console, rom!.Value.Console);
			Assert.Equal(name, rom.Value.Name);
		}

		[Fact]
		public void The_seam_answers_nothing_for_a_dump_the_table_does_not_know()
		{
			//A miss is a real answer, and it stays a miss: an unknown hash and a value
			//that is not a hash at all both leave the tile on its generic cover rather
			//than sending a request nobody can answer.
			Assert.Null(NoIntroNameTable.ForSha1("0000000000000000000000000000000000000000"));
			Assert.Null(NoIntroNameTable.ForSha1("not-a-sha1"));
		}

		[Fact]
		public void The_seam_reads_the_table_once_and_keeps_it()
		{
			//Lazy and single: the artifact holds 17867 rows, and a session that never
			//opens the library must not pay for reading it. The reference identity is
			//the "once" - a seam that reloaded on every tile would parse that artifact
			//once per tile on the sheet.
			Assert.NotNull(NoIntroNameTable.Embedded);
			Assert.True(NoIntroNameTable.Embedded!.Count > 17000,
				"the shipped table looks truncated: " + NoIntroNameTable.Embedded.Count);
			Assert.Same(NoIntroNameTable.Embedded, NoIntroNameTable.Embedded);
		}
	}
}
