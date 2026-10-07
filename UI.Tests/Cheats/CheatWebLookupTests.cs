using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mesen.Interop;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Cheats
{
	//P.12 (ADR-0245 §4, second bullet; #924): a game not in the bundled list is
	//offered the codes scripts/cheat_web_lookup.py found online for the user's
	//copy - only those whose check passed, each labelled "found online, checked
	//on your copy" and toggled like a database row. The check rule itself is the
	//script's (pending #934, it fails closed today); these tests stand a fake
	//checker in for it, so a passing code can be shown at all.
	public class CheatWebLookupTests
	{
		private static readonly IReadOnlyList<StoredCheat> NoneStored = Array.Empty<StoredCheat>();

		private static readonly WebFoundCode Passed = new("Infinite lives", "0032:09", WebCheckState.Passed);
		private static readonly WebFoundCode Failed = new("Max power", "0040:FF", WebCheckState.Failed);
		private static readonly WebFoundCode Unchecked = new("Moon jump", "0050:01", WebCheckState.Unchecked);
		private static readonly WebFoundCode Unknown = new("Stop timer", "0060:01", WebCheckState.Unknown);

		private static IReadOnlyList<CheatSheetRow> Rows(IReadOnlyList<WebFoundCode> web, IReadOnlyList<StoredCheat>? stored = null, bool recordingArt = false)
		{
			return CheatSheet.BuildRows(ConsoleType.Nes, null, false, stored ?? NoneStored, recordingArt, "", Array.Empty<CommunityCheat>(), web);
		}

		[Fact]
		public void Only_a_passed_code_is_listed_with_the_found_online_label()
		{
			IReadOnlyList<CheatSheetRow> rows = Rows(new[] { Failed, Passed, Unchecked, Unknown });

			CheatSheetRow row = Assert.Single(rows);
			Assert.Equal("Infinite lives", row.Description);
			Assert.Equal("found online, checked on your copy", row.Note);
			Assert.Equal(CheatRowSource.WebFound, row.Source);
			Assert.True(row.CanToggle);
			Assert.False(row.IsOn);
		}
		[Fact]
		public void A_web_row_toggles_into_the_same_stored_list_as_a_database_row()
		{
			CheatSheetRow row = Assert.Single(Rows(new[] { Passed }));

			IReadOnlyList<StoredCheat> stored = CheatSheet.Toggle(NoneStored, row, false);

			Assert.Equal(new StoredCheat("Infinite lives", CheatType.NesCustom, "0032:09", true), Assert.Single(stored));
			CheatSheetRow again = Assert.Single(Rows(new[] { Passed }, stored));
			Assert.True(again.IsOn);
			Assert.Equal(CheatRowSource.WebFound, again.Source);
			//Off keeps the entry, disabled - the same as a database row.
			Assert.False(Assert.Single(CheatSheet.Toggle(stored, again, false)).Enabled);
		}

		[Fact]
		public void The_status_line_counts_what_is_on_and_says_where_web_codes_come_from()
		{
			Assert.Equal("1 on · codes found online, checked on your copy",
				CheatSheet.StatusLine(ConsoleType.Nes, null, false, 1, 0, false, 1));
			//No passed code: still "not in the list".
			Assert.Equal(CheatSheet.NotInListLine, CheatSheet.StatusLine(ConsoleType.Nes, null, false, 0, 0, false, 0));
		}

		[Fact]
		public void A_failed_or_unchecked_code_is_never_a_row_whatever_the_search()
		{
			WebFoundCode[] notPassed = { Failed, Unchecked, Unknown };

			Assert.Empty(CheatSheet.BuildRows(ConsoleType.Nes, null, false, NoneStored, false, "max", Array.Empty<CommunityCheat>(), notPassed));
			Assert.Empty(Rows(notPassed));
		}

		//The script's stdout, shaped as scripts/cheat_web_lookup.py writes it
		//(result_json): only passing codes are in "codes", each with LABEL.
		private static string Output(params string[] codes)
		{
			return "{\"schema\": \"mesence.cheat-web-lookup/1\", \"game\": {\"name\": \"Castlevania\", \"rom\": \"c.nes\", \"rom_sha256\": \"ab\"}, "
				+ "\"frames\": 120, \"counts\": {\"checked\": 14, \"passed\": " + codes.Length + "}, \"codes\": [" + string.Join(", ", codes) + "]}";
		}

		private static string Code(string code, string desc, string? label)
		{
			string labelPart = label == null ? "" : ", \"label\": \"" + label + "\"";
			return "{\"code\": \"" + code + "\", \"desc\": \"" + desc + "\", \"address\": \"0x0032\", \"value\": 9, \"on\": \"120/120\", \"off\": \"0/120\"" + labelPart + "}";
		}

		[Fact]
		public void Only_a_code_the_script_labelled_as_checked_reads_as_passed()
		{
			IReadOnlyList<WebFoundCode> codes = CheatWebLookup.ParseOutput(Output(
				Code("0032:09", "Infinite lives", "found online, checked on your copy"),
				Code("0040:FF", "Max power", null),
				Code("0050:01", "Moon jump", "found online")));

			Assert.Equal(new[] {
				new WebFoundCode("Infinite lives", "0032:09", WebCheckState.Passed),
				new WebFoundCode("Max power", "0040:FF", WebCheckState.Unknown),
				new WebFoundCode("Moon jump", "0050:01", WebCheckState.Unknown),
			}, codes);
		}

		[Theory]
		[InlineData("")]
		[InlineData("not json")]
		[InlineData("{\"schema\": \"mesence.cheat-intent/1\", \"codes\": []}")]
		[InlineData("{\"schema\": \"mesence.cheat-web-lookup/1\", \"codes\": 3}")]
		public void An_answer_the_client_cannot_read_offers_nothing(string stdout)
		{
			Assert.Empty(CheatWebLookup.ParseOutput(stdout));
		}

		private sealed class FakeScript
		{
			public IReadOnlyList<string>? Arguments { get; private set; }
			public int ExitCode { get; init; }
			public string Stdout { get; init; } = "";

			public Task<CheatWebRun> RunAsync(IReadOnlyList<string> arguments)
			{
				Arguments = arguments;
				return Task.FromResult(new CheatWebRun(ExitCode, Stdout));
			}
		}

		[Fact]
		public async Task The_script_checker_runs_the_lookup_on_the_rom_path_and_reads_its_answer()
		{
			FakeScript script = new() { Stdout = Output(Code("0032:09", "Infinite lives", CheatWebLookup.Label)) };
			ICheatWebChecker checker = new CheatWebLookupScriptChecker(script.RunAsync, "/tools");

			IReadOnlyList<WebFoundCode> codes = await checker.LookUpAsync("/roms/Castlevania.nes", "Castlevania");

			Assert.Equal(new[] { "/tools/cheat_web_lookup.py", "--rom", "/roms/Castlevania.nes", "--game", "Castlevania" },
				script.Arguments!.Select(a => a.Replace('\\', '/')));
			Assert.Equal(WebCheckState.Passed, Assert.Single(codes).Check);
		}

		[Fact]
		public async Task A_lookup_that_exits_with_an_error_offers_nothing()
		{
			FakeScript script = new() { ExitCode = 1, Stdout = Output(Code("0032:09", "Infinite lives", CheatWebLookup.Label)) };

			Assert.Empty(await new CheatWebLookupScriptChecker(script.RunAsync, "/tools").LookUpAsync("/roms/c.nes", "Castlevania"));
		}
	}
}
