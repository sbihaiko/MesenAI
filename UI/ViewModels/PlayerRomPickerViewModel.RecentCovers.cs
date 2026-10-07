using Avalonia.Media;
using Avalonia.Media.Imaging;
using Mesen.Logic;
using System;
using System.Collections.Generic;
using System.IO;

namespace Mesen.ViewModels
{
	//#1035 (ADR-0264 Decision 6.3): the Recent list as the library's third cover
	//source. A game the player has already run shows its own screenshot where the
	//grid would otherwise draw the console-coloured generic cover - Decision 6's
	//case 4, the cover that says nothing about the game.
	//
	//The rule that finds the picture - the ROM's full path in the Recent record's
	//RomInfo.txt - is host-free and lives in Mesen.Logic.RecentCoverIndex, pinned
	//by UI.Tests/Play/RecentCoverIndexTests. What is here is the other half: which
	//tile asks, and what the tile draws when an answer comes back.
	//
	//This is a partial part of PlayerRomPickerViewModel, so the flat library
	//(#1032) keeps the shape it shipped with and this slice touches it only where
	//a tile is built.
	public partial class PlayerRomPickerViewModel
	{
		//One scan's two halves, together: the entries the walk found, and the cover
		//the library module chose for each of them, in the same order (#1035).
		//
		//They travel as one value because they are produced as one: the cover of a
		//played game comes out of the `.rgd` - a zip the sheet has to open and a
		//screenshot it has to decompress - and that is disk work, so it happens on
		//the thread that is already walking the library folders and never on the
		//thread that draws. What the UI thread still owns is the decode: turning
		//the bytes into a bitmap is an Avalonia object and nothing else.
		private sealed record LibraryScanPayload(LibraryScanResult Result, IReadOnlyList<LibraryCoverPick> Covers);

		//The library walk and the cover of every entry it found, off the UI thread.
		//
		//`recentGamesFolder` is passed in rather than read here: it comes off
		//ConfigManager, which is the UI thread's to read, and the scan should not
		//be a second reader of a setting the player can change mid-scan.
		//
		//The index is built at every scan rather than held for the session: it is a
		//snapshot of the folder (RecentCoverIndex says so), so a game played while
		//the app is open gets its screenshot the next time the library is opened
		//instead of never. It is built here, on this thread, because building it
		//reads and unzips every Recent record.
		private LibraryScanPayload ScanLibraryWithCovers(IReadOnlyList<string> folders, string? recentGamesFolder)
		{
			LibraryScanResult result = ScanLibrary(folders);
			RecentCoverIndex index = RecentCoverIndex.Open(recentGamesFolder);
			List<LibraryCoverPick> covers = new(result.Entries.Count);
			//The priority is the module's (ADR-0264 Decision 6); what this side
			//hands it is the one lookup it needs, and the entry whose cover the
			//lookup is NOT asked about costs no read at all.
			foreach(LibraryEntry entry in result.Entries) {
				covers.Add(GameLibraryCover.Resolve(entry, path => index.FindCover(path)));
			}
			return new LibraryScanPayload(result, covers);
		}
	}

	//The library tile, from this slice's side: what its cover draws when the
	//player has a screenshot of the game, and whether the title still belongs on
	//top of it.
	public partial class PlayerLibraryTile
	{
		//The module's answer as the brush the template binds to. Which cover the
		//entry gets is not decided here - the entry carries its own cover, and
		//GameLibraryCover.Resolve already weighed it against the player's Recent
		//list; what is left on this side is turning the answer into something
		//Avalonia can draw.
		//
		//The on-cover title goes with the generic cover. It is written ON the colour
		//because a colour says nothing about the game; a screenshot says everything,
		//and stamping the title across the picture would be the one thing a cover
		//must not do. The title still reads under the tile, where it always did.
		private static (IBrush Cover, bool ShowsTitleOnCover) TileCover(LibraryEntry entry, LibraryCoverPick cover)
		{
			if(cover.Cover == LibraryCover.RecentScreenshot && cover.Screenshot is byte[] bytes
				&& RecentCoverBrush.TryImage(bytes) is IBrush image) {
				return (image, false);
			}
			return (ConsoleCover(entry.Console), true);
		}

		//True while the tile draws the console-coloured cover, which is the only
		//one the template writes the title on.
		public bool ShowsTitleOnCover { get; }
	}

	//The bytes a Recent record held, as the brush a tile's cover Border draws.
	internal static class RecentCoverBrush
	{
		//UniformToFill crops rather than distorts: a NES screenshot is 4:3 and a
		//tile's cover is 104x139, and squashing a picture to fit is the one thing a
		//cover must not do.
		//
		//Null - the tile falls back to its generic cover - when the bytes are not a
		//picture. A `.rgd` is a file on the player's own disk: anything can sit in
		//its Screenshot.png, and a broken one is a tile without art, never a scan
		//that dies halfway and leaves the grid half built.
		internal static IBrush? TryImage(byte[] screenshot)
		{
			try {
				using MemoryStream stream = new(screenshot, writable: false);
				return new ImageBrush(new Bitmap(stream)) {
					Stretch = Stretch.UniformToFill,
					AlignmentX = AlignmentX.Center,
					AlignmentY = AlignmentY.Center
				};
			} catch(Exception) {
				return null;
			}
		}
	}
}
