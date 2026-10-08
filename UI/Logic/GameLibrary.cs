using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Mesen.Logic;

//Where a tile's picture comes from. ADR-0264 Decision 6 fixes the priority;
//this is the answer the view draws, never the picture itself.
//
//Only Generic is produced today: this slice (#1032, PRD L.1) is the tracer -
//the scan, the titles and the grid - and box art arrives with its own slice
//(L.7/#1038's SHA1 table and L.8's downloader). The other three members exist
//because the priority is the ADR's, and a caller that reads Cover switches on
//this set rather than on a bool it would have to widen later.
public enum LibraryCover
{
	//The console-coloured cover carrying the title: a hack, a translation, or
	//any ROM no database knows.
	Generic = 0,
	//Decision 6's cases 1 and 2, the downloaded art.
	BoxArt,
	TitleScreen,
	//Case 3: the player's own screenshot from the Recent list.
	RecentScreenshot
}

//One tile: the path that opens it, the console it is for, the title it carries
//and where its cover comes from (ADR-0264 Decision 7). A library entry is a
//value, so the view-model can hold a list of them and a test can write one out
//in full.
public sealed record LibraryEntry(string Path, RomConsole Console, string Title, LibraryCover Cover = LibraryCover.Generic);

//What one bounded scan answered: the entries in the order the grid shows them,
//how many library folders actually contributed one of them, and whether the
//count cap was reached. `Truncated` is the header's business - ADR-0264
//Decision 9 wants a capped scan to SAY so rather than silently drop games.
public sealed record LibraryScanResult(IReadOnlyList<LibraryEntry> Entries, int FolderCount, bool Truncated);

//#1032 (ADR-0264 Decisions 1, 7 and 9): the flat library's rules, host-free so
//UI.Tests can pin them against a fake tree (ADR-0123). The walk reads nothing
//itself - the listing comes in as the FolderLister seam RomFolderScan already
//uses - and "is this a game" is RomFileKinds' answer rather than a second list
//of extensions.
//
//The shape is one flat list. A folder is never a row: the folders shape the
//scan, and what comes out is every openable ROM under the library folders in
//one grid ordered by title (Decision 1). That is the whole difference from the
//folder browser PlayRomPicker still owns, and the reason this is a separate
//module rather than a mode of that one.
public static class GameLibrary
{
	//The two caps Decision 9 names, so they are the decision and not a detail
	//of one implementation. MaxDepth bounds how far below a library folder the
	//walk goes - a whole mounted disk is what it stops - and MaxEntries bounds
	//how much one scan may collect.
	public const int MaxDepth = 6;
	public const int MaxEntries = 20000;

	//The comparer the scan folds two spellings of a path with when its caller
	//names none. Folding is a FILE SYSTEM rule and not a string rule: Windows and
	//macOS spell a path case-insensitively - `roms/Contra.nes` and
	//`roms/contra.nes` are one file there, so one tile - while Linux does not, and
	//folding there would hide a game the player really has.
	public static StringComparer PathComparer { get; } = PathComparerFor(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());

	//The rule PathComparer follows, with the platform NAMED rather than
	//interrogated: the choice is the decision, so both sides of it are pinned by a
	//test on whatever OS the test happens to run on.
	public static StringComparer PathComparerFor(bool caseInsensitiveFileSystem)
	{
		return caseInsensitiveFileSystem ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
	}

	//Every openable ROM under `libraryFolders`, in the order the grid shows
	//them, folded the way the platform folds paths.
	public static LibraryScanResult Scan(IEnumerable<string> libraryFolders, FolderLister list)
	{
		return Scan(libraryFolders, list, PathComparer);
	}

	//The same scan with the fold named by the caller. `list` is the host's half:
	//one folder in, its subfolders and its files out, and an empty answer for a
	//folder it cannot read (a drive pulled out between listing and descending is
	//not a crash). `pathComparer` is the "same file?" question - see PathComparer
	//for what the default answers and why.
	public static LibraryScanResult Scan(IEnumerable<string> libraryFolders, FolderLister list, StringComparer pathComparer)
	{
		return Walk(libraryFolders, list, pathComparer, null);
	}

	//#1037 (ADR-0264 Decision 9): the same bounded walk, reporting the entries it
	//finds one listing at a time instead of only when the last folder answers -
	//which is what lets a sheet fill its grid while the scan runs rather than
	//showing a still picture until it ends.
	//
	//`onEntries` is called with the entries a single listing added, and never
	//with an empty batch: a folder that held no game is not a revision of the
	//grid. The call happens on the thread that called this, before the walk moves
	//on, so a caller who wants the work off the UI thread owns that thread
	//itself. The caps are the same two constants either way - streaming changes
	//when the entries are handed over, never how many or how deep.
	public static LibraryScanResult ScanStreaming(IEnumerable<string> libraryFolders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onEntries)
	{
		return Walk(libraryFolders, list, PathComparer, onEntries);
	}

	//Both scans walk here: `onEntries` is the only difference between a scan that
	//answers once and one that answers as it goes, and `pathComparer` is the fold
	//the walk asks its "same file?" questions with.
	private static LibraryScanResult Walk(IEnumerable<string> libraryFolders, FolderLister list, StringComparer pathComparer, Action<IReadOnlyList<LibraryEntry>>? onEntries)
	{
		List<LibraryEntry> entries = new();
		//One spelling per file and per folder, so the same ROM reachable through
		//two library folders - or a folder that lists itself, which is what a
		//symlinked home or a mount pointing back up looks like - is one tile and
		//one visit. "One spelling" is the platform's answer, not this file's.
		HashSet<string> seenFiles = new(pathComparer);
		HashSet<string> visited = new(pathComparer);
		int foldersAnswered = 0;
		bool truncated = false;

		foreach(string libraryFolder in libraryFolders) {
			string? root = Normalize(libraryFolder);
			if(root is null) {
				continue;
			}
			int before = entries.Count;

			//Breadth-first, so the cap is reached by the shallowest games first:
			//a truncated grid holds the library's near half rather than one deep
			//branch of it. A queue of (folder, depth), where depth 0 is the
			//library folder itself - MaxDepth is "at most six levels BELOW a
			//library folder", so a game directly in it is one of the six.
			Queue<(string Folder, int Depth)> pending = new();
			pending.Enqueue((root, 0));
			while(pending.Count > 0 && !truncated) {
				(string current, int depth) = pending.Dequeue();
				if(!visited.Add(current)) {
					continue;
				}
				(IReadOnlyList<string> folders, IReadOnlyList<string> files) = List(list, current);
				//What this one listing adds, so the stream reports a folder's
				//games as it finds them rather than at the end of the walk.
				int found = entries.Count;
				foreach(string file in files) {
					if(RomFileKinds.IsHiddenName(Path.GetFileName(file)) || !RomFileKinds.IsOpenable(file)) {
						continue;
					}
					string full = Normalize(file) ?? file;
					if(!seenFiles.Add(full)) {
						continue;
					}
					//Checked before the add, so a capped scan holds exactly the
					//cap and not one more.
					if(entries.Count >= MaxEntries) {
						truncated = true;
						break;
					}
					entries.Add(new LibraryEntry(full, ConsoleOf(full), CleanTitle(Path.GetFileName(file))));
				}
				//Reported whatever stopped the loop - a capped scan streams the
				//entries it did collect, so the player sees the games up to the
				//limit rather than nothing.
				if(onEntries is not null && entries.Count > found) {
					onEntries(entries.GetRange(found, entries.Count - found));
				}
				if(truncated || depth >= MaxDepth) {
					continue;
				}
				foreach(string folder in folders) {
					if(RomFileKinds.IsHiddenName(Path.GetFileName(folder))) {
						continue;
					}
					pending.Enqueue((Normalize(folder) ?? folder, depth + 1));
				}
			}

			//The header counts the folders that answered (Decision 8): a library
			//folder with nothing openable under it is not one of the player's
			//folders of games, and saying it is would make the count a lie.
			if(entries.Count > before) {
				foldersAnswered++;
			}
			if(truncated) {
				break;
			}
		}

		entries.Sort((left, right) => Compare(left, right, pathComparer));
		return new LibraryScanResult(entries, foldersAnswered, truncated);
	}

	//The title a tile carries. The extension goes - both of them for an archive
	//(`Contra (U) [!].nes.zip` is Contra) - and so does every region, revision
	//and dump tag the scene carries in parentheses and brackets, so
	//`Castlevania (U) [!].nes` reads as *Castlevania* (Decision 7).
	//
	//A name that is nothing but tags has no title left, so it keeps its own name
	//without the extensions: never an empty tile, and never a tile called
	//"Unknown" that hides which file it is.
	public static string CleanTitle(string fileName)
	{
		string name = Path.GetFileName(fileName);
		string bare = StripExtensions(name);
		string cleaned = Collapse(RemoveTags(bare));
		return cleaned.Length > 0 ? cleaned : bare.Trim();
	}

	//The same cleaner over the OTHER title source: the canonical No-Intro name.
	//ADR-0264 Decision 7 asks for two title sources and **one** cleaner over both,
	//so a matched game and an unmatched one are cleaned to the same shape - and
	//the tag rule above is the one that does it, brackets included, because a
	//No-Intro name carries bracketed dump tags of its own (`Castlevania (USA)
	//[b]`) exactly as a scene file name does.
	//
	//The ONE step CleanTitle takes that this does not is the extension one, and it
	//is left out on purpose: a No-Intro name is a game's name and not a file name,
	//and StripExtensions answers a name it does not recognise by splitting at the
	//last dot - "Dr. Mario" would read as "Dr". The tags and the spacing are the
	//whole of the cleaning a second source needs.
	//
	//A name that is nothing but tags keeps its own spelling, for CleanTitle's own
	//reason: an empty title is not a title.
	public static string CleanCanonicalTitle(string noIntroName)
	{
		string cleaned = Collapse(RemoveTags(noIntroName));
		return cleaned.Length > 0 ? cleaned : noIntroName.Trim();
	}

	//The title as the grid orders it. A leading article moves to the end - "The
	//Legend of Zelda" files under L, where a player looks for it - and it moves
	//for SORT ORDER only, never for display (Decision 7).
	//
	//The article has to be a word of its own: "Thexder" is not "The xder", and
	//the space is what tells them apart.
	public static string SortTitle(string title)
	{
		//Longest first, so "An" is tested before "A" - although the space makes
		//them mutually exclusive, the order is what a reader expects.
		foreach(string article in new[] { "The", "An", "A" }) {
			if(title.Length > article.Length + 1
				&& title.StartsWith(article + " ", StringComparison.OrdinalIgnoreCase)) {
				return title[(article.Length + 1)..] + ", " + article;
			}
		}
		return title;
	}

	//The console a file is for. A ROM names it by its extension; an archive
	//names it by the extension inside its own name, which is exactly what
	//Decision 9 means by "an archive whose name the console classifier
	//recognises". An archive that names none - `collection.7z` - is Unknown
	//rather than a guess: RomConsoleKinds' own rule, and the one that keeps a
	//tile from claiming a machine it cannot run.
	private static RomConsole ConsoleOf(string path)
	{
		RomConsole console = RomConsoleKinds.OfFile(path);
		if(console != RomConsole.Unknown) {
			return console;
		}
		//The ARCHIVE extension only: the ROM extension inside the name is the
		//thing being classified, so stripping it too would leave `Contra (U)`,
		//which names no console at all.
		return RomFileKinds.IsArchiveFile(path) ? RomConsoleKinds.OfFile(StripArchiveExtensions(path)) : RomConsole.Unknown;
	}

	//The archive extension(s) off the end, and nothing more.
	private static string StripArchiveExtensions(string name)
	{
		string current = name;
		for(int i = 0; i < 4 && RomFileKinds.IsArchiveFile(current); i++) {
			string stripped = Path.GetFileNameWithoutExtension(current);
			if(stripped.Length == 0) {
				break;
			}
			current = stripped;
		}
		return current;
	}

	//Every openable extension off the end: `.zip` first for an archive, then the
	//ROM extension it wraps, and a plain `.nes` in one step. A name with two
	//extensions is two steps rather than a special case.
	private static string StripExtensions(string name)
	{
		string current = name;
		for(int i = 0; i < 4; i++) {
			if(!RomFileKinds.IsOpenable(current)) {
				break;
			}
			string stripped = Path.GetFileNameWithoutExtension(current);
			if(stripped.Length == 0) {
				break;
			}
			current = stripped;
		}
		return current;
	}

	//`(U)`, `(Rev 1)` and `[!]` are tags the scene adds to a file name, not words
	//in a title, so they go wherever they sit. An unclosed group is left alone:
	//cutting from an opening bracket to the end would eat a title over one
	//mistyped character.
	private static string RemoveTags(string name)
	{
		char[] output = new char[name.Length];
		int length = 0;
		int depth = 0;
		foreach(char c in name) {
			if(c == '(' || c == '[') {
				depth++;
				continue;
			}
			if(c == ')' || c == ']') {
				if(depth > 0) {
					depth--;
				}
				continue;
			}
			if(depth == 0) {
				output[length++] = c;
			}
		}
		return new string(output, 0, length);
	}

	//One space where a tag sat, and nothing hanging off either end: `Mega Man 2
	//(U) (Rev 1)` is "Mega Man 2" and not "Mega Man 2  ".
	private static string Collapse(string text)
	{
		List<char> output = new(text.Length);
		bool space = false;
		foreach(char c in text) {
			if(char.IsWhiteSpace(c)) {
				space = output.Count > 0;
				continue;
			}
			if(space) {
				output.Add(' ');
				space = false;
			}
			output.Add(c);
		}
		return new string(output.ToArray());
	}

	//By title, then by path: two games of the same name (an NES and a Game Boy
	//one, or the same ROM under two folders) keep one stable order between runs
	//rather than whatever the disk answered first. The path tiebreak asks the
	//scan's own "is this the same path?" question, so two spellings that tie on
	//Windows and macOS are still ordered on Linux instead of left to the host.
	//
	//Public because it IS the grid's order (Decision 1) and not only the
	//collected list's: a caller that fills the grid as entries stream in merges
	//them with this comparator rather than restating the rule, so a streamed
	//grid and a whole-result one cannot disagree about where a game belongs.
	public static int Compare(LibraryEntry left, LibraryEntry right, StringComparer pathComparer)
	{
		int byTitle = string.Compare(SortTitle(left.Title), SortTitle(right.Title), StringComparison.OrdinalIgnoreCase);
		return byTitle != 0 ? byTitle : pathComparer.Compare(left.Path, right.Path);
	}

	//The same order with the platform's fold, which is the question a grid
	//merging streamed entries asks - it has no caller-named fold to pass on.
	public static int Compare(LibraryEntry left, LibraryEntry right)
	{
		return Compare(left, right, PathComparer);
	}

	//A folder listing that threw is an empty answer: the scan runs over disks
	//that are not ours, and a permission or an unplugged volume is not a reason
	//to lose the games already found.
	private static (IReadOnlyList<string> Folders, IReadOnlyList<string> Files) List(FolderLister list, string folder)
	{
		try {
			return list(folder);
		} catch {
			return (Array.Empty<string>(), Array.Empty<string>());
		}
	}

	//One spelling per folder, so two names for the same place are one visit. A
	//path the platform cannot spell is not a folder to walk - the library
	//folders come out of settings.json, which a person can edit.
	private static string? Normalize(string path)
	{
		try {
			string full = Path.GetFullPath(path);
			string trimmed = full.TrimEnd(Path.DirectorySeparatorChar);
			return trimmed.Length == 0 || trimmed.EndsWith(":", StringComparison.Ordinal) ? full : trimmed;
		} catch(Exception) {
			return null;
		}
	}
}
