using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests
{
	//#1038 (ADR-0266, spec #1030): the title a library tile carries once the
	//ROM's own hash is known. The two halves are each tested where they live -
	//the reader and its spelling rule in NoIntroNameTableTests, the hash in
	//RomHashCacheTests - and what is pinned HERE is the composition this slice
	//adds: a hash the table knows becomes the database's own title, and every
	//way of not knowing keeps the cleaned file name. That fallback is the whole
	//point: a library whose second step failed must still read as a library.
	//
	//The good answers come from the table committed in scripts/ (embedded by
	//UI.Tests.csproj under Mesen.Logic's own LogicalName), read through the
	//app's loader. The hashes below are run-time facts about real dumps, quoted
	//as the literals ADR-0003 asks a hash golden to be, not recomputed here.
	public class PlayerLibraryCanonicalTitleTests
	{
		//RomHashCache emits 40 uppercase hex digits (ADR-0039, matching
		//Core's `SHA1::GetHash`), and the table stores lowercase: the boundary
		//between the two is what the first two cases walk over.
		private const string CastlevaniaSha1 = "EE09B857C90916EDD92A20C463485A610B0A76FD";
		private const string TetrisSha1 = "74591CC9501AF93873F9A5D3EB12DA12C0723BBC";
		private const string UnknownSha1 = "0000000000000000000000000000000000000000";

		private static NoIntroNameTable RealTable()
		{
			//A null table here would make every case below pass vacuously, so the
			//artifact is asserted present before it is trusted.
			return Assert.IsType<NoIntroNameTable>(NoIntroNameTable.LoadEmbedded());
		}

		//The header NoIntroNameTable.Load promises to accept, built here rather
		//than copied from the committed file: a table this file owns is what puts
		//a name of its choosing in front of the rule.
		private static NoIntroNameTable TableOf(params string[] rows)
		{
			const string header =
				"#mesen-no-intro-sha1-table\t1\n" +
				"#source\tfixture\n" +
				"#licence\tfixture\n" +
				"#hash\tfixture\n" +
				"#console\tnes\tFixture - NES\t2020.01.02\t" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n";
			using MemoryStream output = new MemoryStream();
			using(GZipStream gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true)) {
				byte[] bytes = Encoding.UTF8.GetBytes(header + string.Join("\n", rows) + "\n");
				gzip.Write(bytes, 0, bytes.Length);
			}
			return NoIntroNameTable.Load(new MemoryStream(output.ToArray()));
		}

		//The match: the database's spelling of the game, tags and all stripped by
		//CanonicalTitle - never the file name the player happened to download,
		//and never the raw No-Intro name with its region tags still on it.
		[Fact]
		public void A_rom_the_table_knows_takes_the_databases_own_title()
		{
			string title = CanonicalTitles.Resolve("Castlevania (U) [!]", CastlevaniaSha1, RealTable());

			Assert.Equal("Castlevania", title);
		}

		//A Game Boy dump, so the rule is not one console's: the same call answers
		//for a `.gb` file as for a `.nes` one, because the table's key is the
		//payload hash of either.
		[Fact]
		public void The_rule_is_the_same_for_another_console()
		{
			string title = CanonicalTitles.Resolve("tetris", TetrisSha1, RealTable());

			Assert.Equal("Tetris", title);
		}

		//The hash arrives uppercase from RomHashCache and the table is stored
		//lowercase. A case-sensitive comparison would answer nothing for every
		//ROM in every library while every other test here stayed green, so the
		//two spellings are asserted to be one answer.
		[Fact]
		public void The_case_of_the_hash_does_not_change_the_answer()
		{
			NoIntroNameTable table = RealTable();

			Assert.Equal(
				CanonicalTitles.Resolve("castlevania (u) [!]", CastlevaniaSha1, table),
				CanonicalTitles.Resolve("castlevania (u) [!]", CastlevaniaSha1.ToLowerInvariant(), table));
			Assert.Equal("Castlevania", CanonicalTitles.Resolve("castlevania (u) [!]", CastlevaniaSha1, table));
		}

		//The miss: a ROM the database does not list - a hack, a translation, a
		//homebrew dump - keeps the title the scan already gave it. Handing back a
		//placeholder instead would hide which file the tile is.
		[Fact]
		public void A_rom_the_table_does_not_know_keeps_its_cleaned_file_name()
		{
			string title = CanonicalTitles.Resolve("Mega Man 2", UnknownSha1, RealTable());

			Assert.Equal("Mega Man 2", title);
		}

		//The fallback is verbatim: whatever the scan's cleaner decided, including
		//its own punctuation and case, is what the tile reads.
		[Fact]
		public void The_fallback_is_the_cleaned_title_untouched()
		{
			const string cleaned = "Zelda II - The Adventure of Link (Hack)";

			string title = CanonicalTitles.Resolve(cleaned, UnknownSha1, RealTable());

			Assert.Equal(cleaned, title);
		}

		//The hash could not be computed - the file went away, the disk refused to
		//answer. A missing hash is a miss, never an exception: the pass that reads
		//a library has no business failing over one ROM.
		[Fact]
		public void A_hash_that_could_not_be_computed_keeps_the_cleaned_file_name()
		{
			string title = CanonicalTitles.Resolve("Metroid", null, RealTable());

			Assert.Equal("Metroid", title);
		}

		//An assembly carrying no table at all (NoIntroNameTable.LoadEmbedded
		//answers null): the library is a library of file names rather than a
		//crash, which is the contract the loader states.
		[Fact]
		public void An_app_with_no_embedded_table_shows_file_names()
		{
			string title = CanonicalTitles.Resolve("Castlevania (U) [!]", CastlevaniaSha1, null);

			Assert.Equal("Castlevania (U) [!]", title);
		}

		//ADR-0264 Decision 7: there are two title sources and **one** cleaner over
		//both. A No-Intro name carries tags in brackets as well as in parentheses -
		//the scene's dump names do - and the file name's own cleaner (GameLibrary,
		//whose CleanTitle strips `(...)` and `[...]` alike) drops them all. A
		//canonical name that kept its brackets would read one way while the same
		//game named by its file read another, and Decision 4's search would match a
		//tag word on the unmatched tile but not on the matched one.
		[Fact]
		public void A_bracket_tag_on_a_no_intro_name_is_stripped_like_one_on_a_file_name()
		{
			const string sha1 = "2222222222222222222222222222222222222222";
			NoIntroNameTable table = TableOf(sha1 + "\tnes\tCastlevania (USA) [b]");

			string title = CanonicalTitles.Resolve("castlevania", sha1, table);

			Assert.Equal("Castlevania", title);
			//The two sources are one cleaner, so they answer the same title for the
			//same game - which is the whole of Decision 7's "one cleaner over both".
			Assert.Equal(GameLibrary.CleanTitle("Castlevania (USA) [b].nes"), title);
		}

		//#1038 review finding 4, ADR-0264 Decision 9: an archive is one entry of the
		//library, and its path is what the grid holds - but the table's keys are
		//No-Intro PAYLOAD hashes (ADR-0003) and RomHashCache hashes the bytes of the
		//file it is handed, so a `.zip` can never answer a lookup. The rule that
		//says so is host-free and lives here; the pass that obeys it is the
		//view-model's.
		[Fact]
		public void An_archive_can_never_be_named_by_this_rule()
		{
			Assert.False(CanonicalTitles.IsTitleablePath("/library/Contra (U) [!].nes.zip"));
			Assert.False(CanonicalTitles.IsTitleablePath("/library/collection.7z"));

			Assert.True(CanonicalTitles.IsTitleablePath("/library/Contra (U) [!].nes"));
			Assert.True(CanonicalTitles.IsTitleablePath("/library/Tetris.gb"));
		}

		//A name that is nothing but tags has no title in it. NoIntroNameTable's
		//own rule says such a name keeps its own spelling, and that answer has to
		//survive the composition: the tile must not read as an empty string.
		[Fact]
		public void A_name_of_nothing_but_tags_is_still_a_title()
		{
			NoIntroNameTable table = TableOf(
				"1111111111111111111111111111111111111111\tnes\t(USA)");

			string title = CanonicalTitles.Resolve("weird", "1111111111111111111111111111111111111111", table);

			Assert.Equal("(USA)", title);
		}
	}
}
