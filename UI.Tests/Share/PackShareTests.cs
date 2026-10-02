using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mesen.Interop;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Share
{
	//G.8 (PRD Part B §13.5.4 W-H2, ADR-0241, §13.4): *Continue on GitHub ↗* opens
	//the community-pack Issue Form pre-filled with exactly its three fields. The
	//host check is the real allow-list (scripts/pack_host_allowlist.json, the
	//same file CI downloads through), and the field ids and console options are
	//read from the real form, so neither can drift from this screen.
	public class PackShareTests
	{
		internal static string RepoRoot()
		{
			DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
			while(dir != null && !File.Exists(Path.Combine(dir.FullName, "Mesen.sln"))) {
				dir = dir.Parent;
			}
			return dir?.FullName ?? throw new InvalidOperationException("Could not locate repo root (Mesen.sln) from " + AppContext.BaseDirectory);
		}

		internal static IReadOnlyList<CommunityPackHostEntry> Hosts { get; } =
			CommunityPackHostAllowlist.LoadFromFile(Path.Combine(RepoRoot(), CommunityPackHostAllowlist.RepoRelativePath));

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

		private const string Release = "https://github.com/someone/contra-80s/releases/download/v1.2/contra.zip";

		[Fact]
		public void The_url_opens_the_pack_form_with_its_label_and_title_prefix()
		{
			Dictionary<string, string> q = Query(PackShare.BuildIssueUrl(Release, "Contra (USA)", "NES"));
			Assert.Equal("community-pack.yml", q["template"]);
			Assert.Equal("community-pack", q["labels"]);
			Assert.Equal("[Pack] ", q["title"]);
		}

		[Fact]
		public void The_three_fields_are_prefilled_by_their_form_ids_and_nothing_else()
		{
			Dictionary<string, string> q = Query(PackShare.BuildIssueUrl("  " + Release + " ", " Contra (USA) ", "NES"));
			Assert.Equal(Release, q["pack_link"]);
			Assert.Equal("Contra (USA)", q["rom_target"]);
			Assert.Equal("NES", q["console"]);
			//No author, no description: authorship is read off the pack by CI.
			Assert.Equal(new[] { "console", "labels", "pack_link", "rom_target", "template", "title" }, q.Keys.OrderBy(k => k, StringComparer.Ordinal));
		}

		//The form is the contract: its input/dropdown ids are exactly the three
		//this screen sends, and its dropdown options are ConsoleOptions in order.
		[Fact]
		public void The_field_ids_and_console_options_match_the_real_issue_form()
		{
			string form = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "ISSUE_TEMPLATE", PackShare.FormTemplate));
			string[] ids = Regex.Matches(form, @"^\s+id:\s*(\S+)\s*$", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToArray();
			Assert.Equal(new[] { PackShare.LinkField, PackShare.GameField, PackShare.ConsoleField }, ids);
			Assert.Contains("labels:\n  - " + PackShare.FormLabel, form.Replace("\r\n", "\n"));
			Assert.Contains("title: \"" + PackShare.TitlePrefix + "\"", form);

			string dropdown = form.Substring(form.IndexOf("id: console", StringComparison.Ordinal));
			string optionsBlock = dropdown.Substring(dropdown.IndexOf("options:", StringComparison.Ordinal));
			optionsBlock = optionsBlock.Substring(0, optionsBlock.IndexOf("validations:", StringComparison.Ordinal));
			string[] options = Regex.Matches(optionsBlock, @"^\s+-\s+(.+?)\s*$", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToArray();
			Assert.Equal(options, PackShare.ConsoleOptions);
		}

		[Fact]
		public void Text_is_escaped_so_it_cannot_add_a_parameter()
		{
			Dictionary<string, string> q = Query(PackShare.BuildIssueUrl(Release + "?a=1&b=2", "Tom & Jerry #2", "NES"));
			Assert.Equal(Release + "?a=1&b=2", q["pack_link"]);
			Assert.Equal("Tom & Jerry #2", q["rom_target"]);
			Assert.False(q.ContainsKey("b"));
		}

		[Fact]
		public void A_console_that_is_not_an_option_is_left_for_the_form()
		{
			Dictionary<string, string> q = Query(PackShare.BuildIssueUrl(Release, "Contra (USA)", ""));
			Assert.False(q.ContainsKey("console"));
			Assert.False(Query(PackShare.BuildIssueUrl(Release, "Contra (USA)", "SNES")).ContainsKey("console"));
		}

		[Fact]
		public void A_long_game_name_is_cut_on_a_rune_boundary_to_keep_the_url_bounded()
		{
			string url = PackShare.BuildIssueUrl(Release, string.Concat(Enumerable.Repeat("🎮", 3000)), "NES");
			Assert.True(url.Length <= PackShare.MaxUrlLength);
			Assert.DoesNotContain("%EF%BF%BD", url);
			Assert.StartsWith("🎮", Query(url)["rom_target"]);
		}

		[Theory]
		[InlineData(ConsoleType.Nes, "Contra (USA).nes", "NES")]
		[InlineData(ConsoleType.Gameboy, "Tetris (World).gb", "GB")]
		[InlineData(ConsoleType.Gameboy, "Zelda DX (USA).GBC", "GBC")]
		[InlineData(ConsoleType.Sms, "Alex Kidd (USA).sms", "SMS")]
		[InlineData(ConsoleType.Gba, "Metroid Fusion (USA).gba", "Other")]
		public void The_running_game_fills_the_forms_console_option(ConsoleType console, string rom, string option)
		{
			Assert.Equal(option, PackShare.ConsoleOption(console, rom));
			Assert.Contains(option, PackShare.ConsoleOptions);
		}

		[Theory]
		[InlineData("nes", "NES")]
		[InlineData("gb", "GB")]
		[InlineData("GBC", "GBC")]
		[InlineData("sms", "SMS")]
		[InlineData("gg", "SMS")]
		[InlineData("sg1000", "SMS")]
		[InlineData("snes", "Other")]
		[InlineData("", "")]
		public void A_pack_json_target_system_maps_onto_the_forms_option(string system, string option)
		{
			Assert.Equal(option, PackShare.ConsoleOptionForSystem(system));
		}

		//W-H2: the host hint lists only allow-listed hosts, and any other host
		//is flagged before the user leaves the app (the CI would refuse it).
		[Theory]
		[InlineData(Release)]
		[InlineData("https://gist.githubusercontent.com/u/abc/raw/pack.zip")]
		[InlineData("https://raw.githubusercontent.com/u/r/main/pack.zip")]
		[InlineData("https://drive.google.com/file/d/abc/view?usp=sharing")]
		[InlineData("https://www.mediafire.com/file/abc/pack.zip/file")]
		[InlineData("https://www.dropbox.com/scl/fi/abc/pack.zip?dl=0")]
		[InlineData("https://mega.nz/file/abc#key")]
		public void A_link_on_an_accepted_host_is_accepted(string link)
		{
			Assert.Equal(PackLinkCheck.Accepted, PackShare.CheckLink(link, Hosts));
		}

		[Theory]
		[InlineData("https://example.com/pack.zip")]
		[InlineData("http://github.com/u/r/releases/download/v1/p.zip")]
		[InlineData("https://github.com/u/r/blob/main/p.zip")]
		[InlineData("https://www.dropbox.com/home/pack.zip")]
		[InlineData("not a link")]
		public void A_link_on_any_other_host_is_not_accepted(string link)
		{
			Assert.Equal(PackLinkCheck.NotAccepted, PackShare.CheckLink(link, Hosts));
		}

		[Fact]
		public void Continue_says_what_is_missing_in_the_order_the_screen_is_filled()
		{
			Assert.Equal(PackShareReason.NeedsLink, PackShare.Evaluate(" ", "Contra (USA)", "NES", Hosts));
			Assert.Equal(PackShareReason.HostNotAccepted, PackShare.Evaluate("https://example.com/p.zip", "Contra (USA)", "NES", Hosts));
			Assert.Equal(PackShareReason.NeedsGame, PackShare.Evaluate(Release, " ", "NES", Hosts));
			Assert.Equal(PackShareReason.NeedsConsole, PackShare.Evaluate(Release, "Contra (USA)", "", Hosts));
			Assert.Equal(PackShareReason.None, PackShare.Evaluate(Release, "Contra (USA)", "NES", Hosts));
			//W-H3: a project whose console nothing names still continues.
			Assert.Equal(PackShareReason.None, PackShare.Evaluate(Release, "Contra (USA)", "", Hosts, requireConsole: false));
		}
	}
}
