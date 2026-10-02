using System;
using System.Collections.Generic;
using System.Linq;
using Mesen.Interop;

namespace Mesen.Logic
{
	//One game of the bundled cheat database (CheatDb.<Console>.json), as plain data.
	public sealed record CheatDbGame(string Name, string Sha1, IReadOnlyList<CheatDbCode> Cheats);

	//One database entry: its description and the raw code (`;`-joined parts).
	public sealed record CheatDbCode(string Description, string Code);

	//One entry of the per-game CheatCodes list (UI/Config/CheatCodes.cs), the
	//store the classic cheat window edits. Same four fields as CheatCode.
	public sealed record StoredCheat(string Description, CheatType Type, string Codes, bool Enabled);

	public enum CheatRowSource
	{
		//From the database entry of the loaded copy (matched by its cheat hash).
		ThisCopy,
		//From a database entry picked by game name: "made for another copy".
		AnotherCopy,
		//A stored code that matches no listed entry (typed by hand, or added
		//in the classic window).
		Yours
	}

	//One W-P11 row. IsOn mirrors the stored list; CanToggle is false only for
	//a code the recording rule refuses that is not on yet (one already on can
	//always be turned off). Note is the row's one-line mark, "" when none.
	public sealed record CheatSheetRow(string Description, CheatType Type, string Codes, bool IsOn, bool CanToggle, string Note, CheatRowSource Source);

	//P.10 (ADR-0245 §1-§3, PRD Part B §13 W-P11): the Play Cheats sheet's rules.
	//Every toggle is written to the same CheatCodes list the classic cheat
	//window uses, matched the way that window's database import dedupes
	//(description + codes + type), so the two never disagree (rule 12).
	//Host-free (UI/Logic firewall, ADR-0123): the ViewModel maps CheatCode to
	//StoredCheat and back, and owns the file I/O and EmuApi calls.
	public static class CheatSheet
	{
		public const string NotInListLine = "This copy of the game isn't in the cheat list";
		public const string AnotherCopyMark = "made for another copy — may not work";
		public const string ReplayNote = "Cheats you have on are recorded in a shared replay";
		public const string AllOffNote = "All cheats are switched off in Tools ⋯ › Cheats";
		public const int MaxGameResults = 20;

		public static CheatDbGame? FindGameForCopy(IEnumerable<CheatDbGame> db, string cheatSha1)
		{
			if(string.IsNullOrWhiteSpace(cheatSha1)) {
				return null;
			}
			return db.FirstOrDefault(g => string.Equals(g.Sha1, cheatSha1.Trim(), StringComparison.OrdinalIgnoreCase));
		}

		//The not-in-list fallback: games whose name contains every word of the
		//query. An empty query lists nothing (not the whole database).
		public static IReadOnlyList<CheatDbGame> SearchGamesByName(IEnumerable<CheatDbGame> db, string query)
		{
			string[] words = Words(query);
			if(words.Length == 0) {
				return Array.Empty<CheatDbGame>();
			}
			return db.Where(g => ContainsAll(g.Name, words)).Take(MaxGameResults).ToList();
		}

		public static IReadOnlyList<CheatSheetRow> BuildRows(ConsoleType console, CheatDbGame? game, bool gameIsAnotherCopy, IReadOnlyList<StoredCheat> stored, bool recordingArt, string search)
		{
			List<CheatSheetRow> rows = new();
			HashSet<int> matchedStored = new();

			if(game != null && CheatConsoleScope.HasCheatList(console)) {
				CheatRowSource source = gameIsAnotherCopy ? CheatRowSource.AnotherCopy : CheatRowSource.ThisCopy;
				foreach(CheatDbCode entry in game.Cheats) {
					CheatType type = CheatConsoleScope.DatabaseCodeType(console, entry.Code);
					string codes = ToStoredCodes(entry.Code);
					int index = IndexOf(stored, entry.Description, type, codes);
					if(index >= 0) {
						matchedStored.Add(index);
					}
					rows.Add(MakeRow(entry.Description, type, codes, index >= 0 && stored[index].Enabled, recordingArt, source));
				}
			}

			for(int i = 0; i < stored.Count; i++) {
				if(!matchedStored.Contains(i)) {
					StoredCheat cheat = stored[i];
					rows.Add(MakeRow(cheat.Description, cheat.Type, cheat.Codes, cheat.Enabled, recordingArt, CheatRowSource.Yours));
				}
			}

			string[] words = Words(search);
			return words.Length == 0 ? rows : rows.Where(r => ContainsAll(r.Description, words)).ToList();
		}

		//Flips one row in the stored list and returns the new list. Turning a
		//row off keeps it, disabled, the same as an unchecked row in the
		//classic window. A refused code (recording art, not a RAM code) never
		//turns on: the list comes back unchanged.
		public static IReadOnlyList<StoredCheat> Toggle(IReadOnlyList<StoredCheat> stored, CheatSheetRow row, bool recordingArt)
		{
			bool turnOn = !row.IsOn;
			if(turnOn && recordingArt && !CheatRecordingRule.IsRamCode(row.Type, row.Codes)) {
				return stored;
			}

			List<StoredCheat> next = stored.ToList();
			int index = IndexOf(stored, row.Description, row.Type, row.Codes);
			if(index >= 0) {
				next[index] = next[index] with { Enabled = turnOn };
			} else if(turnOn) {
				next.Add(new StoredCheat(row.Description, row.Type, row.Codes, true));
			}
			return next;
		}

		//*Add a Code…*: a code typed by hand, added on. Returns the new list and
		//"" or the reason it was refused (list unchanged).
		public static (IReadOnlyList<StoredCheat> Stored, string Error) AddCode(IReadOnlyList<StoredCheat> stored, ConsoleType console, string description, string text, bool recordingArt)
		{
			if(!CheatConsoleScope.TryParseCodes(console, text, out CheatType type, out string codes)) {
				return (stored, CheatConsoleScope.InvalidCodeReason);
			}
			if(recordingArt && !CheatRecordingRule.IsRamCode(type, codes)) {
				return (stored, CheatRecordingRule.RefusedReason);
			}

			string name = string.IsNullOrWhiteSpace(description) ? string.Join(", ", CheatConsoleScope.SplitCodes(codes)) : description.Trim();
			List<StoredCheat> next = stored.ToList();
			int index = IndexOf(stored, name, type, codes);
			if(index >= 0) {
				next[index] = next[index] with { Enabled = true };
			} else {
				next.Add(new StoredCheat(name, type, codes, true));
			}
			return (next, "");
		}

		public static int CountOn(IEnumerable<StoredCheat> stored) => stored.Count(c => c.Enabled);

		//The W-P4 row's value: "none", "N on", or "off" when every cheat is
		//switched off in the classic window (CheatWindowConfig.DisableAllCheats).
		public static string OverlaySummary(int countOn, bool disableAll)
		{
			if(disableAll) {
				return "off";
			}
			return countOn == 0 ? "none" : countOn + " on";
		}

		//The line under the list: which copy the codes are for, or why there is no list.
		public static string StatusLine(ConsoleType console, CheatDbGame? game, bool gameIsAnotherCopy, int countOn)
		{
			if(!CheatConsoleScope.HasCheatList(console)) {
				return CheatConsoleScope.NoListReason;
			}
			if(game == null) {
				return NotInListLine;
			}
			if(gameIsAnotherCopy) {
				return countOn + " on · codes from " + game.Name + " — made for another copy, may not work";
			}
			return countOn + " on · matched to your copy of " + game.Name;
		}

		//A database code as CheatListWindowViewModel stores it: parts joined by NewLine.
		public static string ToStoredCodes(string databaseCode)
		{
			return string.Join(Environment.NewLine, databaseCode.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
		}

		private static CheatSheetRow MakeRow(string description, CheatType type, string codes, bool isOn, bool recordingArt, CheatRowSource source)
		{
			string note = "";
			bool canToggle = true;
			if(recordingArt) {
				if(CheatRecordingRule.IsRamCode(type, codes)) {
					note = CheatRecordingRule.AllowedNote;
				} else {
					note = CheatRecordingRule.RefusedReason;
					canToggle = isOn;
				}
			}
			if(source == CheatRowSource.AnotherCopy && note != CheatRecordingRule.RefusedReason) {
				note = note.Length == 0 ? AnotherCopyMark : AnotherCopyMark + " · " + note;
			}
			return new CheatSheetRow(description, type, codes, isOn, canToggle, note, source);
		}

		//Same identity as the classic import's dedupe key (description + codes +
		//type); codes compare part by part, ignoring case and line endings.
		private static int IndexOf(IReadOnlyList<StoredCheat> stored, string description, CheatType type, string codes)
		{
			string[] parts = CheatConsoleScope.SplitCodes(codes);
			for(int i = 0; i < stored.Count; i++) {
				StoredCheat c = stored[i];
				if(c.Type == type && c.Description == description && CheatConsoleScope.SplitCodes(c.Codes).SequenceEqual(parts)) {
					return i;
				}
			}
			return -1;
		}

		private static string[] Words(string query)
		{
			return (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
		}

		private static bool ContainsAll(string text, string[] words)
		{
			return words.All(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
		}
	}
}
