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
		//The decoded covers of the grid on screen, so the grid that replaces it
		//can hand them back (#1035). One per tile that drew the player's own
		//screenshot; the console-coloured covers are SolidColorBrushes and need
		//no owner.
		private readonly CoverArtLedger _coverArt = new();

		//One tile, with its picture registered before the grid ever draws it.
		//
		//Registration happens here rather than in the tile because the ledger is
		//the grid's, not the tile's: a tile does not know when it stops being
		//drawn, and the rebuild does. A tile with no picture of its own hands over
		//null, which the ledger ignores.
		//The tiles ON SCREEN by path, so a resolved title reaches the tile that shows
		//it without a walk of the grid. Rebuilt with the grid: it never holds a tile
		//ClearTiles has released.
		private readonly Dictionary<string, PlayerLibraryTile> _tileByPath = new(StringComparer.Ordinal);

		private PlayerLibraryTile TileFor(LibraryEntry entry, LibraryCoverPick cover)
		{
			PlayerLibraryTile tile = new(entry, ConsoleName(entry.Console), cover);
			tile.Title = _titles.TitleOf(entry);
			_coverArt.Track(tile.CoverArt);
			_tileByPath[tile.Path] = tile;
			return tile;
		}

		//Empties the grid and hands back the pictures it was drawing.
		//
		//The order is the point: a tile released while it is still bound to a
		//brush would be a container drawing a picture that is already gone, so the
		//panels go first and the allocations second. A rebuild calls this before it
		//tracks the new covers, which is what keeps them out of this Clear.
		private void ClearTiles()
		{
			Tiles.Clear();
			_tileByPath.Clear();
			_coverArt.Clear();
		}

		//The cover of every entry a batch brought back, in the same order (#1035).
		//
		//The entries and their covers are produced as one and travel as one: the
		//cover of a played game comes out of the `.rgd` - a zip the sheet has to
		//open and a screenshot it has to decompress - and that is disk work, so it
		//happens on the thread that is already walking the library folders and
		//never on the thread that draws. What the UI thread still owns is the
		//decode: turning the bytes into a bitmap is an Avalonia object and nothing
		//else.
		//
		//`index` is opened by the caller and held for the whole scan rather than
		//per batch: the index is a snapshot of the Recent folder (RecentCoverIndex
		//says so), so one per scan is both the correct lifetime and the only one
		//that does not unzip every record once per folder listed.
		private static IReadOnlyList<LibraryCoverPick> CoversFor(IReadOnlyList<LibraryEntry> entries, RecentCoverIndex index)
		{
			List<LibraryCoverPick> covers = new(entries.Count);
			//The priority is the module's (ADR-0264 Decision 6); what this side
			//hands it is the one lookup it needs, and the entry whose cover the
			//lookup is NOT asked about costs no read at all.
			foreach(LibraryEntry entry in entries) {
				covers.Add(GameLibraryCover.Resolve(entry, path => index.FindCover(path)));
			}
			return covers;
		}
	}

	//The library tile, from this slice's side: what its cover draws when the
	//player has a screenshot of the game, and whether the title still belongs on
	//top of it.
	public partial class PlayerLibraryTile
	{
		//The module's answer as the brush the template binds to, plus the
		//allocation behind it when the cover is a decoded picture. Which cover the
		//entry gets is not decided here - the entry carries its own cover, and
		//GameLibraryCover.Resolve already weighed it against the player's Recent
		//list; what is left on this side is turning the answer into something
		//Avalonia can draw.
		//
		//The on-cover title goes with the generic cover. It is written ON the colour
		//because a colour says nothing about the game; a screenshot says everything,
		//and stamping the title across the picture would be the one thing a cover
		//must not do. The title still reads under the tile, where it always did.
		private static (IBrush Cover, bool ShowsTitleOnCover, IDisposable? Art) TileCover(LibraryEntry entry, LibraryCoverPick cover)
		{
			if(cover.Cover == LibraryCover.RecentScreenshot && cover.Screenshot is byte[] bytes
				&& RecentCoverBrush.TryImage(bytes) is (IBrush image, IDisposable art)) {
				return (image, false, art);
			}
			//A generic cover is a SolidColorBrush over a colour from this file: no
			//picture, so nothing to hand back.
			return (ConsoleCover(entry.Console), true, null);
		}

		//True while the tile draws the console-coloured cover, which is the only
		//one the template writes the title on.
		public bool ShowsTitleOnCover { get; }

		//The decoded picture this tile draws, when it draws one - the Bitmap behind
		//the brush, which is an allocation the grid's rebuild has to hand back
		//(#1035). Null on the generic cover, and null again once the sheet has
		//disposed it.
		internal IDisposable? CoverArt { get; }
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
		//
		//The second member is the Bitmap itself, and the caller owns it: the brush
		//is what the template draws, and the picture behind it has to be handed back
		//when the grid is rebuilt (#1035). It is returned rather than wrapped so the
		//one place that knows the picture is a Bitmap is this one.
		internal static (IBrush Brush, IDisposable Art)? TryImage(byte[] screenshot)
		{
			Bitmap? bitmap = null;
			try {
				using MemoryStream stream = new(screenshot, writable: false);
				bitmap = new Bitmap(stream);
				ImageBrush brush = new(bitmap) {
					Stretch = Stretch.UniformToFill,
					AlignmentX = AlignmentX.Center,
					AlignmentY = AlignmentY.Center
				};
				return (brush, bitmap);
			} catch(Exception) {
				//The picture was decoded and the brush over it was not: the
				//allocation is this frame's to release, since nobody else has it.
				bitmap?.Dispose();
				return null;
			}
		}
	}
}
