namespace Mesen.Logic
{
	//#1038 (ADR-0266, spec #1030): the title a library tile carries once the
	//ROM's own hash is known. This is the second half of the flat library's
	//title rule - GameLibrary.CleanTitle reads the file name before any hash
	//exists, and this reads the database once one does:
	//
	//  - a hash the No-Intro table holds gives the database's own spelling of
	//    the game, its tags stripped by NoIntroNameTable.CanonicalTitle. A
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
		public static string Resolve(string cleanedTitle, string? sha1, NoIntroNameTable? table)
		{
			if(table != null && table.TryLookup(sha1, out NoIntroRomName rom)) {
				//CanonicalTitle already keeps a name that is nothing but tags in
				//its own spelling, so this is never an empty string unless the
				//cleaned title was one too.
				string canonical = NoIntroNameTable.CanonicalTitle(rom.Name);
				if(canonical.Length > 0) {
					return canonical;
				}
			}
			return cleanedTitle;
		}
	}
}
