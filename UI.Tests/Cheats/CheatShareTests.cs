using System;
using System.Collections.Generic;
using System.Linq;
using Mesen.Interop;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Cheats
{
	//R.4 (ADR-0248 §2): *Share This Cheat ↗* on a code the user added opens the
	//`[Cheat]` Issue Form (.github/ISSUE_TEMPLATE/cheat-code.yml) pre-filled,
	//the same way ReplayShare.BuildIssueUrl does. No credential, no upload: the
	//click and the ↗ glyph are the confirmation.
	public class CheatShareTests
	{
		private const string Sha1 = "D608F769333B13DA9C67F07599E405944893A950";

		private static Dictionary<string, string> Query(string url)
		{
			Uri uri = new(url);
			Assert.Equal("https", uri.Scheme);
			Assert.Equal("github.com", uri.Host);
			Assert.Equal("/sbihaiko/MesenAI/issues/new", uri.AbsolutePath);
			return uri.Query.TrimStart('?').Split('&')
				.Select(p => p.Split('=', 2))
				.ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
		}

		[Fact]
		public void The_url_opens_the_cheat_form_with_its_label_and_title_prefix()
		{
			Dictionary<string, string> q = Query(CheatShare.BuildIssueUrl(Sha1, "1942", ConsoleType.Nes, "0436:09", "Start with 9 rolls"));
			Assert.Equal("cheat-code.yml", q["template"]);
			Assert.Equal("cheat", q["labels"]);
			Assert.Equal("[Cheat] ", q["title"]);
		}

		//The parameter names are the form's field ids; the console is one of the
		//dropdown's options, spelled exactly.
		[Fact]
		public void Every_form_field_is_prefilled_by_its_id()
		{
			Dictionary<string, string> q = Query(CheatShare.BuildIssueUrl(Sha1.ToLowerInvariant(), "1942", ConsoleType.Nes, "0437:05" + Environment.NewLine + "0438:01", "Start on stage 5"));
			Assert.Equal(Sha1, q["game_sha1"]);
			Assert.Equal("1942", q["game_name"]);
			Assert.Equal("NES", q["console"]);
			//Several parts are one effect joined with `+`, as the form asks.
			Assert.Equal("0437:05+0438:01", q["code"]);
			Assert.Equal("Start on stage 5", q["description"]);
		}

		[Theory]
		[InlineData(ConsoleType.Nes, "NES")]
		[InlineData(ConsoleType.Gameboy, "Game Boy")]
		[InlineData(ConsoleType.Sms, "Master System / Game Gear")]
		public void The_console_is_the_forms_option(ConsoleType console, string option)
		{
			Assert.Equal(option, CheatShare.ConsoleOption(console));
		}

		[Fact]
		public void Text_is_escaped_so_it_cannot_add_a_parameter()
		{
			Dictionary<string, string> q = Query(CheatShare.BuildIssueUrl(Sha1, "Tom & Jerry", ConsoleType.Nes, "SXIOPO", "a&b=c #1"));
			Assert.Equal("Tom & Jerry", q["game_name"]);
			Assert.Equal("a&b=c #1", q["description"]);
			Assert.False(q.ContainsKey("b"));
		}

		[Fact]
		public void Only_the_users_own_codes_on_a_known_copy_and_console_can_be_shared()
		{
			CheatSheetRow yours = new("mine", CheatType.NesCustom, "0436:09", true, true, "", CheatRowSource.Yours);
			Assert.True(CheatShare.CanShare(yours, ConsoleType.Nes, Sha1));
			//Bundled and community codes are already shared.
			Assert.False(CheatShare.CanShare(yours with { Source = CheatRowSource.ThisCopy }, ConsoleType.Nes, Sha1));
			Assert.False(CheatShare.CanShare(yours with { Source = CheatRowSource.AnotherCopy }, ConsoleType.Nes, Sha1));
			Assert.False(CheatShare.CanShare(yours with { Source = CheatRowSource.Community }, ConsoleType.Nes, Sha1));
			//No game running (no hash), or a console the form does not offer.
			Assert.False(CheatShare.CanShare(yours, ConsoleType.Nes, ""));
			Assert.False(CheatShare.CanShare(yours, ConsoleType.Snes, Sha1));
		}

		[Fact]
		public void A_very_long_description_is_cut_so_the_url_stays_bounded()
		{
			string url = CheatShare.BuildIssueUrl(Sha1, "1942", ConsoleType.Nes, "SXIOPO", new string('é', 5000));
			Assert.True(url.Length <= CheatShare.MaxUrlLength);
			Assert.StartsWith(new string('é', 10), Query(url)["description"]);
		}
	}
}
