using Avalonia.Media;
using Avalonia.Media.Imaging;
using Mesen.Config;
using Mesen.Logic;
using System;
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
		private RecentCoverIndex? _recentCovers;

		//The Recent folder the tiles read their covers from - the Core's own, the
		//one the Play home's Continue card already reads, so a game the player has
		//run is the same list on both surfaces.
		//
		//The index is built at every scan rather than held for the session: it is a
		//snapshot of the folder (RecentCoverIndex says so), so a game played while
		//the app is open gets its screenshot the next time the library is opened
		//instead of never.
		private void RefreshRecentCovers()
		{
			_recentCovers = RecentCoverIndex.Open(ConfigManager.RecentGamesFolder);
		}

		//The screenshot of the game at this entry's path, or null - the tile keeps
		//the cover it already had - when there is none.
		//
		//The guard is Decision 6's order, as far as this slice reaches it: art the
		//scan already resolved for the entry (downloaded box art, a title screen)
		//is cases 1 and 2 and outranks the player's own screenshot, which fills the
		//gap the generic cover would otherwise take. The box-art slice lands there
		//without touching this file.
		private byte[]? RecentCoverOf(LibraryEntry entry)
		{
			if(_recentCovers is null || entry.Cover != LibraryCover.Generic) {
				return null;
			}
			return _recentCovers.FindCover(entry.Path);
		}
	}

	//The library tile, from this slice's side: what its cover draws when the
	//player has a screenshot of the game, and whether the title still belongs on
	//top of it.
	public partial class PlayerLibraryTile
	{
		//Decision 6's sources in the ADR's order, for the one case this slice adds.
		//Cases 1 and 2 - downloaded art - never reach here: the scan resolves them
		//onto the entry before a tile exists, and the view-model asks for a Recent
		//cover only when the entry carries none.
		//
		//The on-cover title goes with the generic cover. It is written ON the colour
		//because a colour says nothing about the game; a screenshot says everything,
		//and stamping the title across the picture would be the one thing a cover
		//must not do. The title still reads under the tile, where it always did.
		private static (IBrush Cover, bool ShowsTitleOnCover) TileCover(LibraryEntry entry, byte[]? recentCover)
		{
			if(recentCover is not null && RecentCoverBrush.TryImage(recentCover) is IBrush image) {
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
