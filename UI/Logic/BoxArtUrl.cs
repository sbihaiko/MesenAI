using System;
using System.Text;

namespace Mesen.Logic
{
	//The URL one game's art lives at. Two decisions are in this file and nowhere
	//else, so both are testable without a window or a network:
	//
	//- **The file name is the No-Intro name, sanitised.** The collection names
	//  its files after the game the way the database spells it, with the eleven
	//  characters that cannot appear in a file name of its own convention
	//  replaced by `_`; every other character, spaces included, is kept.
	//- **A name that is not there produces no URL at all.** The key comes from
	//  the SHA1 -> No-Intro table (#1038); a ROM the table does not know has no
	//  name to ask for, and this returns null rather than guessing from the
	//  file name - the collection is keyed by dump, not by what the file is
	//  called (ADR-0003).
	public static class BoxArtUrl
	{
		//Already on the pack host allow-list (scripts/pack_host_allowlist.json,
		//ADR-0138 §41). Nothing here adds a host: this is the second, outbound
		//purpose that allow-list entry is used for.
		public const string Host = "raw.githubusercontent.com";

		//Box art first, title screen second - the order Decision 6's cover
		//priority reads them in.
		public const string BoxartsCollection = "Named_Boxarts";
		public const string TitlesCollection = "Named_Titles";

		//The characters the collection's own file names cannot carry. This is the
		//list written into the decision and it is one table, not one per caller.
		private static readonly char[] ReplacedChars = { '&', '*', '/', ':', '`', '<', '>', '?', '\\', '|', '"' };

		//`No-Intro name -> collection file name (without the extension)`.
		public static string Sanitize(string noIntroName)
		{
			StringBuilder builder = new(noIntroName.Length);
			foreach(char c in noIntroName) {
				builder.Append(Array.IndexOf(ReplacedChars, c) >= 0 ? '_' : c);
			}
			return builder.ToString();
		}

		//The box art URL for a game, or null when there is nothing to ask for.
		public static Uri? Boxarts(BoxArtConsole console, string noIntroName) => Build(console, BoxartsCollection, noIntroName);

		//The title-screen URL for a game, or null when there is nothing to ask for.
		public static Uri? Titles(BoxArtConsole console, string noIntroName) => Build(console, TitlesCollection, noIntroName);

		private static Uri? Build(BoxArtConsole console, string collection, string noIntroName)
		{
			string? repo = BoxArtSystems.RepoFolder(console);
			if(repo == null || string.IsNullOrEmpty(noIntroName)) {
				return null;
			}

			//The sanitised name still holds spaces and punctuation the URL cannot
			//carry raw, so the path segment is percent-encoded (a space becomes
			//%20). The eleven replaced characters are gone before this, so nothing
			//here can turn a game name into a path of its own.
			string file = Uri.EscapeDataString(Sanitize(noIntroName)) + ".png";
			return new Uri($"https://{Host}/{repo}/master/{collection}/{file}");
		}
	}
}
