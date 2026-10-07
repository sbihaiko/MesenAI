namespace Mesen.Logic
{
	//#1038 (ADR-0266, spec #1030): the title a library tile carries once the
	//ROM's own hash is known. This is the second half of the flat library's
	//title rule - GameLibrary.CleanTitle reads the file name before any hash
	//exists, and this reads the database once one does:
	//
	//  - a hash the No-Intro table holds gives the database's own spelling of
	//    the game, its tags stripped by GameLibrary's own cleaner - the ONE
	//    cleaner ADR-0264 Decision 7 asks for over both title sources. A
	//    player sees "Castlevania" instead of "Castlevania (U) [!]" or
	//    "castlevania-final-fixed.nes";
	//  - EVERY other answer - a ROM the database does not list, a hash that
	//    could not be computed, an assembly carrying no table - keeps the
	//    cleaned file name the scan already decided on.
	//
	//The fallback is the rule, not an error path: a hack, a translation and a
	//homebrew dump are exactly the ROMs the database has never heard of, and a
	//library is not allowed to read worse for them than it did before this
	//slice. Nothing here throws and nothing here invents a title - a miss
	//answers what the caller passed in, and an empty cleaned title stays empty
	//rather than becoming a placeholder that hides which file the tile is.
	//
	//Host-free by construction (ADR-0123): the caller supplies the hash (this
	//file hashes nothing) and the table, so the composition is unit-tested in
	//UI.Tests against the committed artifact and against tables a test builds.
	public static class CanonicalTitles
	{
		//The title to show for a ROM whose payload hash is `sha1`, falling back
		//to `cleanedTitle` (GameLibrary.CleanTitle's answer) whenever the table
		//cannot answer for it. `sha1` is the 40-hex value RomHashCache produced -
		//uppercase on the way out of the cache, matched against the table's own
		//case-insensitive keys - and `table` is null in a build that embedded no
		//table at all.
		//Whether a library entry is one this rule can ever name. ADR-0264 Decision 9
		//lists an archive (`.zip`, `.7z`) as one game of the library, and
		//RomFileKinds is the classifier that decides what one is - but an archive's
		//hash can never answer a table lookup: RomHashCache hashes the bytes of the
		//file it is handed, and the table's keys are No-Intro PAYLOAD hashes
		//(ADR-0003), which for an archive are the bytes of the ROM inside it. The
		//caller skips what this refuses, so the walk stops reading whole archives it
		//could never title (review finding 4 on #1038) and the tile keeps the
		//cleaned file name the scan gave it - Decision 7's own fallback.
		//
		//A named gap, not a solution: naming the ROM inside an archive means opening
		//it and hashing the entry, which nothing here does.
		public static bool IsTitleablePath(string path)
		{
			return !RomFileKinds.IsArchiveFile(path);
		}

		public static string Resolve(string cleanedTitle, string? sha1, NoIntroNameTable? table)
		{
			if(table != null && table.TryLookup(sha1, out NoIntroRomName rom)) {
				//The scan's own cleaner, over this second source (ADR-0264
				//Decision 7): parentheses AND brackets go, so a No-Intro name
				//carrying a bracketed dump tag reads exactly as the same game
				//named by its file. A name that is nothing but tags keeps its
				//own spelling there, so this is never an empty string unless the
				//cleaned title was one too.
				string canonical = GameLibrary.CleanCanonicalTitle(rom.Name);
				if(canonical.Length > 0) {
					return canonical;
				}
			}
			return cleanedTitle;
		}
	}
}
