using Mesen.Config;
using Mesen.Logic;
using Mesen.Services;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Mesen.ViewModels
{
	//#1039 (ADR-0265): the app's own end of the box-art chain - the cache, the hash
	//cache and the table, assembled once and handed to the library sheet as its
	//`BoxArtCoverSource`. It lives beside MainWindowViewModel.RomPicker because that
	//is where the picker is built, and it is a separate file so that the slice that
	//adds the covers does not restructure the one that added the sheet.
	public partial class MainWindowViewModel
	{
		//Where a downloaded cover lives: `<home>/BoxArt`, the layout the folder
		//convention next to Cheats and Screenshots already uses. The cache lays the
		//per-console folders and the file names out inside it (BoxArtCacheStore), so
		//nothing here spells those.
		public static string BoxArtFolder => Path.Combine(ConfigManager.HomeFolder, "BoxArt");

		private static BoxArtCache? _boxArtCache;
		private static bool _boxArtCacheOn;
		private static RomHashCache? _romHashes;

		//One tile's cover, or null when there is none to draw. Called by the sheet
		//off the UI thread, once per tile it is showing (ADR-0265 section 4).
		public async Task<BoxArtCover?> BoxArtCoverFor(LibraryEntry entry, CancellationToken cancellationToken)
		{
			return await new BoxArtLibrary(Cache(), Hashes(), NoIntroName)
				.GetCover(entry, cancellationToken)
				.ConfigureAwait(false);
		}

		//The cache reads the switch when it is built, and it is rebuilt when the
		//switch moves - so the Settings › System row takes effect on the next tile
		//rather than on the next launch. The switch's own meaning is the cache's
		//(`DownloadEnabled`): off is no request at all, not a quieter one, and a cover
		//already on the player's disk is still served, because reading their disk is
		//not a request (ADR-0265 section 8).
		private static BoxArtCache Cache()
		{
			bool enabled = ConfigManager.Config.Preferences.DownloadBoxArt;
			if(_boxArtCache is null || _boxArtCacheOn != enabled) {
				_boxArtCache = new BoxArtCache(BoxArtFetcher.Send, BoxArtFolder, new BoxArtCacheOptions {
					DownloadEnabled = enabled
				});
				_boxArtCacheOn = enabled;
			}
			return _boxArtCache;
		}

		private static RomHashCache Hashes() => _romHashes ??= new RomHashCache(Path.Combine(BoxArtFolder, "hashes"));

		//The SHA1 -> No-Intro name table (#1038). **It is not on this branch**, and
		//this is the one seam between the shipped chain and the art collection: with
		//no name there is nothing to ask for, so a tile whose dump the table does not
		//know falls straight to its generic cover rather than having a name guessed
		//from its file (ADR-0265 section 3, ADR-0003). Until the table lands, no
		//request is made at all - which is that rule taken literally rather than a
		//shortcut: this is where it plugs in, and nothing else has to move.
		private static string? NoIntroName(string sha1) => null;
	}
}
