using System;
using System.Text;
using Mesen.Interop;

namespace Mesen.Logic
{
	//R.4 (ADR-0248 §2): *Share This Cheat ↗*. A code the user added opens the
	//`[Cheat]` Issue Form (.github/ISSUE_TEMPLATE/cheat-code.yml) in the user's
	//own browser with its fields filled, the ReplayShare.BuildIssueUrl pattern:
	//no upload, no credential (PRD Part B §13.4). The parameter names are the
	//form's field ids and the console is one of its dropdown options, spelled
	//exactly (scripts/checks/verify_cheat_form_and_workflow.py holds the form).
	//Host-free (UI/Logic firewall, ADR-0123).
	public static class CheatShare
	{
		public const string FormTemplate = "cheat-code.yml";
		public const string FormLabel = "cheat";
		public const string TitlePrefix = "[Cheat] ";
		public const int MaxUrlLength = 4000;

		//The form's dropdown option for a core, or null when the form has none.
		//The Game Boy core runs GBC games too; the user picks GBC in the form.
		public static string? ConsoleOption(ConsoleType console)
		{
			return console switch {
				ConsoleType.Nes => "NES",
				ConsoleType.Gameboy => "Game Boy",
				ConsoleType.Sms => "Master System / Game Gear",
				_ => null
			};
		}

		//Bundled and community codes are already shared; a code needs the copy's
		//hash (a game running) and a console the form offers.
		public static bool CanShare(CheatSheetRow row, ConsoleType console, string cheatSha1)
		{
			return row.Source == CheatRowSource.Yours
				&& ConsoleOption(console) != null
				&& CommunityCheatCatalog.IsSha1((cheatSha1 ?? "").Trim().ToUpperInvariant());
		}

		//The title carries only the prefix: the workflow rewrites it whole
		//(ADR-0248 §1). The description goes last and is the part cut when the
		//URL would grow past MaxUrlLength; the gate bounds it at 80 characters.
		public static string BuildIssueUrl(string cheatSha1, string gameName, ConsoleType console, string codes, string description)
		{
			string head = "https://github.com/" + ReplayShare.Repository + "/issues/new"
				+ "?template=" + Uri.EscapeDataString(FormTemplate)
				+ "&title=" + Uri.EscapeDataString(TitlePrefix)
				+ "&labels=" + Uri.EscapeDataString(FormLabel)
				+ "&game_sha1=" + Uri.EscapeDataString((cheatSha1 ?? "").Trim().ToUpperInvariant())
				+ "&game_name=" + Uri.EscapeDataString(gameName ?? "")
				+ "&console=" + Uri.EscapeDataString(ConsoleOption(console) ?? "")
				+ "&code=" + Uri.EscapeDataString(string.Join("+", CheatConsoleScope.SplitCodes(codes ?? "")))
				+ "&description=";

			StringBuilder escaped = new StringBuilder();
			//By rune: escaping a surrogate half alone would percent-encode U+FFFD.
			foreach(Rune c in (description ?? "").Trim().EnumerateRunes()) {
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
