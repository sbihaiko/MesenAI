using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mesen.Interop;

namespace Mesen.Logic
{
	public enum PackLinkCheck
	{
		Empty,
		//Not https, or a host scripts/pack_host_allowlist.json does not list:
		//CI would refuse to download it, so W-H2 says so before the user leaves.
		NotAccepted,
		Accepted
	}

	//Why Continue on GitHub is disabled (rule 4: a disabled control shows its
	//reason). The order is the order a user fills the screen in.
	public enum PackShareReason
	{
		None,
		NeedsLink,
		HostNotAccepted,
		NeedsGame,
		NeedsConsole
	}

	//G.8 (PRD Part B §13.5.4 W-H2/W-H3, ADR-0241, §13.4): *Continue on GitHub ↗*
	//for a pack. It opens the community-pack Issue Form
	//(.github/ISSUE_TEMPLATE/community-pack.yml) in the user's own browser with
	//its three fields filled - the ReplayShare.BuildIssueUrl pattern. Share holds
	//no credential, uploads nothing and guesses no verdict: the bot
	//(community-pack-validate.yml) downloads, lints and classifies, and reads
	//authorship off the pack, so nothing here asks who made it ("I found this
	//pack" is the default framing). The parameter names are the form's field
	//ids and the console is one of its dropdown options, spelled exactly
	//(UI.Tests/Share/PackShareTests.cs reads the real form). Host-free.
	public static class PackShare
	{
		public const string FormTemplate = "community-pack.yml";
		public const string FormLabel = "community-pack";
		public const string TitlePrefix = "[Pack] ";
		public const string LinkField = "pack_link";
		public const string GameField = "rom_target";
		public const string ConsoleField = "console";
		public const string OtherConsole = "Other";
		public const int MaxUrlLength = 4000;

		//The form's dropdown, in its order.
		private static readonly string[] Options = { "NES", "GB", "GBC", "SMS", OtherConsole };
		public static IReadOnlyList<string> ConsoleOptions => Options;

		//The option for the running game. The Game Boy core runs GBC games too;
		//the file extension tells them apart - the game's own file, an archive's
		//inner one (#666). Consoles the form does not name (GBA, and cores that
		//are not product consoles) are "Other".
		public static string ConsoleOption(ConsoleType console, string romFile)
		{
			return console switch {
				ConsoleType.Nes => "NES",
				ConsoleType.Gameboy => string.Equals(Path.GetExtension(romFile ?? ""), ".gbc", StringComparison.OrdinalIgnoreCase) ? "GBC" : "GB",
				ConsoleType.Sms => "SMS",
				_ => OtherConsole
			};
		}

		//The option for a MEP-v1 pack.json target `system` (mep_build.py's
		//_system_for): gg and sg1000 run on the SMS core and share its label.
		public static string ConsoleOptionForSystem(string system)
		{
			return (system ?? "").Trim().ToLowerInvariant() switch {
				"nes" => "NES",
				"gb" => "GB",
				"gbc" => "GBC",
				"sms" or "gg" or "sg1000" => "SMS",
				"" => "",
				_ => OtherConsole
			};
		}

		public static PackLinkCheck CheckLink(string link, IReadOnlyList<CommunityPackHostEntry> hosts)
		{
			string trimmed = (link ?? "").Trim();
			if(trimmed.Length == 0) {
				return PackLinkCheck.Empty;
			}
			return CommunityPackHostAllowlist.MatchHost(trimmed, hosts) != null ? PackLinkCheck.Accepted : PackLinkCheck.NotAccepted;
		}

		//W-H3 takes Game/Console from the project; a project whose console is not
		//known yet still continues (the form asks for it in the browser).
		public static PackShareReason Evaluate(string link, string game, string console, IReadOnlyList<CommunityPackHostEntry> hosts, bool requireConsole = true)
		{
			switch(CheckLink(link, hosts)) {
				case PackLinkCheck.Empty: return PackShareReason.NeedsLink;
				case PackLinkCheck.NotAccepted: return PackShareReason.HostNotAccepted;
			}
			if(string.IsNullOrWhiteSpace(game)) {
				return PackShareReason.NeedsGame;
			}
			if(requireConsole && Array.IndexOf(Options, console ?? "") < 0) {
				return PackShareReason.NeedsConsole;
			}
			return PackShareReason.None;
		}

		//The title carries only the prefix, as the form's own default. The game
		//is the part cut when the URL would grow past MaxUrlLength; a console
		//that is not one of the options is left for the form.
		public static string BuildIssueUrl(string link, string game, string console)
		{
			string option = Array.IndexOf(Options, console ?? "") >= 0 ? console! : "";
			string head = "https://github.com/" + ReplayShare.Repository + "/issues/new"
				+ "?template=" + Uri.EscapeDataString(FormTemplate)
				+ "&title=" + Uri.EscapeDataString(TitlePrefix)
				+ "&labels=" + Uri.EscapeDataString(FormLabel)
				+ "&" + LinkField + "=" + Uri.EscapeDataString((link ?? "").Trim())
				+ (option.Length > 0 ? "&" + ConsoleField + "=" + Uri.EscapeDataString(option) : "")
				+ "&" + GameField + "=";

			StringBuilder escaped = new StringBuilder();
			//By rune: escaping a surrogate half alone would percent-encode U+FFFD.
			foreach(Rune c in (game ?? "").Trim().EnumerateRunes()) {
				string piece = Uri.EscapeDataString(c.ToString());
				if(head.Length + escaped.Length + piece.Length > MaxUrlLength) {
					break;
				}
				escaped.Append(piece);
			}
			return head + escaped;
		}
	}
}
