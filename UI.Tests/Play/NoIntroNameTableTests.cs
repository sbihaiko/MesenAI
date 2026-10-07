using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#1038 (spec #1030, Seam 1): the No-Intro name table the flat library looks
	//a ROM up in. A match supplies the canonical title and the box-art key; a
	//miss leaves the cleaned file name in charge. Two seams here: the reader
	//against a table this file builds (the format is the contract), and the
	//reader against the table committed in scripts/ (the artifact the app
	//embeds), so a drift between the python generator and this reader fails
	//here instead of only in a player's library.
	public class NoIntroNameTableTests
	{
		private const string Sha1Nes = "1111111111111111111111111111111111111111";
		private const string Sha1Gg = "2222222222222222222222222222222222222222";

		//The pinned format's header, built here rather than copied from the
		//committed file: this is what the reader promises to accept.
		private static readonly string FixtureHeader =
			"#mesen-no-intro-sha1-table\t1\n" +
			"#source\tfixture\n" +
			"#licence\tfixture\n" +
			"#hash\tfixture\n" +
			"#console\tnes\tFixture - NES\t2020.01.02\t" + new string('a', 64) + "\n";

		private static NoIntroNameTable TableOf(params string[] rows)
		{
			return NoIntroNameTable.Load(new MemoryStream(Gzip(FixtureHeader + string.Join("\n", rows) + "\n")));
		}

		private static byte[] Gzip(string text)
		{
			using MemoryStream output = new MemoryStream();
			using(GZipStream gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true)) {
				byte[] bytes = Encoding.UTF8.GetBytes(text);
				gzip.Write(bytes, 0, bytes.Length);
			}
			return output.ToArray();
		}

		[Fact]
		public void A_hash_the_table_holds_answers_with_its_console_and_name()
		{
			NoIntroNameTable table = TableOf(
				Sha1Nes + "\tnes\tCastlevania (USA)",
				Sha1Gg + "\tgg\tSonic Blast (World)");

			Assert.Equal(2, table.Count);
			Assert.True(table.TryLookup(Sha1Nes, out NoIntroRomName nes));
			Assert.Equal(RomConsole.Nes, nes.Console);
			Assert.Equal("Castlevania (USA)", nes.Name);
			Assert.Equal("Castlevania", NoIntroNameTable.CanonicalTitle(nes.Name));

			//A caller holding a hash the emulator printed in another case still
			//matches: SHA-1 hex case is not identity (ADR-0039).
			Assert.True(table.TryLookup(Sha1Gg.ToLowerInvariant(), out NoIntroRomName gg));
			Assert.Equal(RomConsole.GameGear, gg.Console);
			Assert.Equal("Sonic Blast (World)", gg.Name);
		}

		[Fact]
		public void A_hash_the_table_does_not_hold_is_a_miss_not_a_guess()
		{
			NoIntroNameTable table = TableOf(Sha1Nes + "\tnes\tCastlevania (USA)");

			Assert.False(table.TryLookup("3333333333333333333333333333333333333333", out _));
			Assert.False(table.TryLookup(null, out _));
			Assert.False(table.TryLookup("", out _));
			Assert.False(table.TryLookup("   ", out _));
			//A truncated hash is a miss, never a prefix match.
			Assert.False(table.TryLookup("111111111111111111111111111111111111111", out _));
		}

		[Theory]
		[InlineData("Castlevania (USA)", "Castlevania")]
		[InlineData("Legend of Zelda, The (USA) (Rev 1)", "Legend of Zelda, The")]
		[InlineData("Tetris (World) (Rev 1)", "Tetris")]
		[InlineData("Pokemon - Crystal Version (USA, Europe) (Rev 1)", "Pokemon - Crystal Version")]
		[InlineData("Super Mario Bros. (World)", "Super Mario Bros.")]
		[InlineData("Flicky (Japan, New Zealand) (Ja)", "Flicky")]
		public void Canonical_title_drops_the_region_and_revision_tags(string noIntroName, string expected)
		{
			Assert.Equal(expected, NoIntroNameTable.CanonicalTitle(noIntroName));
		}

		[Fact]
		public void A_name_that_is_only_a_tag_keeps_its_own_spelling()
		{
			//Stripping every group would leave an empty title, which no tile can
			//draw, so the original stands.
			Assert.Equal("(Proto)", NoIntroNameTable.CanonicalTitle("(Proto)"));
			Assert.Equal("", NoIntroNameTable.CanonicalTitle(""));
		}

		[Fact]
		public void A_table_of_another_format_version_is_refused()
		{
			Assert.Throws<InvalidDataException>(() => NoIntroNameTable.Load(
				new MemoryStream(Gzip("#mesen-no-intro-sha1-table\t2\n" + Sha1Nes + "\tnes\tX\n"))));
			Assert.Throws<InvalidDataException>(() => NoIntroNameTable.Load(
				new MemoryStream(Gzip("sha1\tconsole\tname\n"))));
		}

		[Fact]
		public void A_row_naming_a_console_the_app_does_not_run_is_refused()
		{
			Assert.Throws<InvalidDataException>(() => TableOf(Sha1Nes + "\tsnes\tChrono Trigger (USA)"));
		}

		[Fact]
		public void A_row_whose_key_is_not_forty_hex_is_refused()
		{
			Assert.Throws<InvalidDataException>(() => TableOf("not-a-sha1\tnes\tCastlevania (USA)"));
		}

		[Fact]
		public void A_missing_embedded_table_is_null_not_an_exception()
		{
			//The dual-compiled Logic type has to survive an assembly that does
			//not carry the table: a library with no names is a library of file
			//names, never a crash (System.Private.CoreLib embeds no such file).
			Assert.Null(NoIntroNameTable.LoadEmbedded(typeof(string).Assembly));
		}

		//The artifact itself: the table committed to scripts/ and embedded by
		//both UI.csproj and UI.Tests.csproj, read through the app's own loader.
		//The goldens are observed output of the generator (ADR-0003's own rule
		//for a hash golden), one per console, so a regeneration that loses or
		//renames a console fails here.
		[Theory]
		[InlineData("33d23c2f2cfa4c9efec87f7bc1321ce3ce6c89bd", RomConsole.Nes, "Super Mario Bros. (World)", "Super Mario Bros.")]
		[InlineData("74591cc9501af93873f9a5d3eb12da12c0723bbc", RomConsole.GameBoy, "Tetris (World) (Rev 1)", "Tetris")]
		[InlineData("f2f52230b536214ef7c9924f483392993e226cfb", RomConsole.GameBoyColor, "Pokemon - Crystal Version (USA, Europe) (Rev 1)", "Pokemon - Crystal Version")]
		[InlineData("41cb23d8dccc8ebd7c649cd8fbb58eeace6e2fdc", RomConsole.GameBoyAdvance, "Pokemon - FireRed Version (USA, Europe)", "Pokemon - FireRed Version")]
		[InlineData("8cecf8ed0f765163b2657be1b0a3ce2a9cb767f4", RomConsole.MasterSystem, "Alex Kidd in Miracle World (USA, Europe)", "Alex Kidd in Miracle World")]
		[InlineData("39a91b5ad6b139ca5d8ac60b78520210e84944bd", RomConsole.Sg1000, "Flicky (Japan, New Zealand) (Ja)", "Flicky")]
		[InlineData("11241be4082f6f9d057488ae75ccdd482f623f8c", RomConsole.GameGear, "Sonic Blast (World)", "Sonic Blast")]
		public void The_committed_table_answers_for_every_supported_console(string sha1, RomConsole console, string name, string canonical)
		{
			NoIntroNameTable table = NoIntroNameTable.LoadEmbedded()!;

			Assert.True(table.Count > 20000, "the committed table looks truncated: " + table.Count);
			Assert.True(table.TryLookup(sha1, out NoIntroRomName rom), sha1 + " is not in the committed table");
			Assert.Equal(console, rom.Console);
			Assert.Equal(name, rom.Name);
			Assert.Equal(canonical, NoIntroNameTable.CanonicalTitle(rom.Name));
		}

		[Fact]
		public void The_committed_table_does_not_claim_a_hash_no_dump_has()
		{
			NoIntroNameTable table = NoIntroNameTable.LoadEmbedded()!;

			Assert.False(table.TryLookup("0000000000000000000000000000000000000000", out _));
		}
	}
}
