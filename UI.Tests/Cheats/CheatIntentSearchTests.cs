using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Cheats
{
	//P.11 (ADR-0245 §4, #922): W-P11's search by intent, host-free with a fake
	//runner in place of scripts/cheat_intent.py. The answer is a closed choice
	//over this game's listed entries: the client checks it again against the
	//list it shows, and anything outside that list is discarded, never shown.
	public class CheatIntentSearchTests
	{
		private const string ContraSha1 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

		private static readonly CheatDbGame Contra = new("Contra (USA)", ContraSha1, new[] {
			new CheatDbCode("Infinite lives - 1P game", "SZKGPAVG"),
			new CheatDbCode("Start with 30 lives", "AAUZGZAP;PEUZTZTP"),
			new CheatDbCode("Invincibility (star effect)", "00B0:FF"),
		});

		private sealed class FakeRunner : ICheatIntentRunner
		{
			private readonly CheatIntentRun _run;
			public IReadOnlyList<string>? Arguments { get; private set; }

			public FakeRunner(int exitCode, string stdout)
			{
				_run = new CheatIntentRun(exitCode, stdout);
			}

			public Task<CheatIntentRun> RunAsync(IReadOnlyList<string> arguments)
			{
				Arguments = arguments;
				return Task.FromResult(_run);
			}
		}

		private static string Answer(string status, int index = -1, string desc = "", string code = "")
		{
			string entry = index < 0 ? "null" : $"{{\"id\": \"E{index}\", \"index\": {index}, \"desc\": \"{desc}\", \"code\": \"{code}\"}}";
			return $"{{\"schema\": \"mesence.cheat-intent/1\", \"status\": \"{status}\", \"backend\": \"jev\", \"entry\": {entry}}}";
		}

		[Fact]
		public async Task A_listed_entry_is_the_match_and_the_line_names_it()
		{
			FakeRunner runner = new(0, Answer("match", 2, "Invincibility (star effect)", "00B0:FF"));

			CheatIntentOutcome outcome = await CheatIntentSearch.SearchAsync(runner, Contra, "don't die");

			Assert.Equal(CheatIntentStatus.Match, outcome.Status);
			Assert.Equal("Invincibility (star effect)", outcome.Entry!.Description);
			Assert.Equal("00B0:FF", outcome.Entry.Code);
			Assert.Contains("Invincibility (star effect)", outcome.Line);
		}

		[Fact]
		public async Task None_says_no_entry_matched()
		{
			FakeRunner runner = new(0, Answer("none"));

			CheatIntentOutcome outcome = await CheatIntentSearch.SearchAsync(runner, Contra, "fly");

			Assert.Equal(CheatIntentStatus.None, outcome.Status);
			Assert.Null(outcome.Entry);
			Assert.Equal(CheatIntentSearch.NoneMatchedLine, outcome.Line);
		}

		[Theory]
		[InlineData(7, "Infinite lives - 1P game", "SZKGPAVG")]   //no entry 7 in this list
		[InlineData(0, "Moon jump", "SZKGPAVG")]                  //entry 0 exists, but not with this text
		[InlineData(0, "Infinite lives - 1P game", "GZKGPAVG")]   //an invented code for a listed text
		public async Task An_answer_outside_the_listed_entries_is_discarded(int index, string desc, string code)
		{
			FakeRunner runner = new(0, Answer("match", index, desc, code));

			CheatIntentOutcome outcome = await CheatIntentSearch.SearchAsync(runner, Contra, "infinite lives");

			Assert.Equal(CheatIntentStatus.Discarded, outcome.Status);
			Assert.Null(outcome.Entry);
			Assert.Equal(CheatIntentSearch.NoneMatchedLine, outcome.Line);
		}

		[Theory]
		[InlineData(0, "not json")]
		[InlineData(0, "")]
		[InlineData(0, "{\"status\": \"discarded\", \"entry\": null}")]
		[InlineData(3, "")]
		public async Task A_failed_or_unreadable_run_shows_no_entry(int exitCode, string stdout)
		{
			FakeRunner runner = new(exitCode, stdout);

			CheatIntentOutcome outcome = await CheatIntentSearch.SearchAsync(runner, Contra, "infinite lives");

			Assert.NotEqual(CheatIntentStatus.Match, outcome.Status);
			Assert.Null(outcome.Entry);
			Assert.NotEqual("", outcome.Line);
		}

		[Fact]
		public async Task The_script_is_asked_with_jev_this_games_hash_and_the_intent_only()
		{
			FakeRunner runner = new(0, Answer("none"));

			await CheatIntentSearch.SearchAsync(runner, Contra, "  infinite lives  ");

			Assert.Equal(new[] { "--backend", "jev", "--sha1", ContraSha1, "--intent", "infinite lives" }, runner.Arguments);
		}

		[Fact]
		public async Task An_empty_intent_never_starts_the_script()
		{
			FakeRunner runner = new(0, Answer("none"));

			CheatIntentOutcome outcome = await CheatIntentSearch.SearchAsync(runner, Contra, "   ");

			Assert.Null(runner.Arguments);
			Assert.Equal(CheatIntentStatus.None, outcome.Status);
		}

		//The #915 ruling: a backend is offered only when it passes >= 90 %
		//correct and <= 5 % wrong entries on its own numbers - today Jev only.
		[Fact]
		public void Only_the_backend_that_passed_the_gate_is_offered()
		{
			Assert.Equal(new[] { "jev" }, CheatIntentSearch.OfferedBackends);
			Assert.Same(ByokVendor.OpenRouter, CheatIntentSearch.Vendor);
		}
			[Fact]
		public void The_matched_entry_highlights_its_own_row_only()
		{
			IReadOnlyList<CheatSheetRow> rows = CheatSheet.BuildRows(Interop.ConsoleType.Nes, Contra, false, Array.Empty<StoredCheat>(), false, "");
			CheatDbCode match = Contra.Cheats[1];

			Assert.Equal(new[] { "Start with 30 lives" }, rows.Where(r => CheatIntentSearch.IsMatch(r, match)).Select(r => r.Description));
			Assert.DoesNotContain(rows, r => CheatIntentSearch.IsMatch(r, null));
		}

		[Fact]
		public void A_typed_code_with_the_same_text_is_not_the_listed_entry()
		{
			CheatSheetRow yours = new("Start with 30 lives", Interop.CheatType.NesGameGenie, "AAUZGZAP", true, true, "", CheatRowSource.Yours);

			Assert.False(CheatIntentSearch.IsMatch(yours, Contra.Cheats[1]));
		}

		[Fact]
		public async Task The_script_runner_refuses_to_start_without_a_stored_key()
		{
			CheatIntentScriptRunner runner = new(new InMemoryByokKeyStore(), "python3", Array.Empty<string>(), "scripts");

			await Assert.ThrowsAsync<ByokKeyMissingException>(() => runner.RunAsync(new[] { "--intent", "x" }));
		}

		[Fact]
		public void The_script_runner_puts_the_script_first_and_never_a_key_on_the_command_line()
		{
			CheatIntentScriptRunner runner = new(new InMemoryByokKeyStore(), "py", new[] { "-3" }, "/tools/scripts");

			Assert.Equal(new[] { "-3", System.IO.Path.Combine("/tools/scripts", "cheat_intent.py"), "--intent", "x" }, runner.CommandLine(new[] { "--intent", "x" }));
		}
	}
}
