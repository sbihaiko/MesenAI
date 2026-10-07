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

		//Built on first use and thread-safe (BoxArtSession): the first screenful asks
		//from worker threads all at once.
		private static readonly BoxArtSession _session = new(
			() => new BoxArtCache(BoxArtFetcher.Send, BoxArtFolder, new BoxArtCacheOptions {
				DownloadEnabled = () => ConfigManager.Config.Preferences.DownloadBoxArt
			}),
			() => new RomHashCache(BoxArtHashFolder));

		//The same folder the picker's titles pass always used: one hash cache per
		//session, not a second one under BoxArt.
		public static string BoxArtHashFolder => Path.Combine(ConfigManager.HomeFolder, "RomHashes");

		public static RomHashCache BoxArtHashes => _session.Hashes;

		//One tile's cover, or null when there is none to draw. Called by the sheet
		//off the UI thread, once per tile it is showing (ADR-0265 section 4).
		public async Task<BoxArtCover?> BoxArtCoverFor(LibraryEntry entry, CancellationToken cancellationToken)
		{
			return await new BoxArtLibrary(Cache(), Hashes(), NoIntroName)
				.GetCover(entry, cancellationToken)
				.ConfigureAwait(false);
		}

		//ONE cache, for the whole session, and the switch is read through it rather
		//than into it: `BoxArtCacheOptions.DownloadEnabled` is the preference itself,
		//asked on every call, so the Settings › System row takes effect on the next
		//tile with no rebuild. Rebuilding is what this deliberately does not do - a
		//second instance carries its own semaphore and its own in-flight table, so
		//the four requests the first one still has running would be joined by four
		//more (ADR-0265 section 4's ceiling is the cache's, and the app has one), and
		//one game could be paid for twice.
		//
		//The switch's own meaning is the cache's: off is no request at all, not a
		//quieter one, and a cover already on the player's disk is still served,
		//because reading their disk is not a request (ADR-0265 section 8).
		private static BoxArtCache Cache() => _session.Cache;

		private static RomHashCache Hashes() => _session.Hashes;

		//The SHA1 -> No-Intro name table (#1038/#1041), read from the artifact the
		//app's own assembly ships and kept after the first use
		//(`NoIntroNameTable.Embedded`). This is the seam between the shipped chain
		//and the art collection, and it is live: a dump the table knows answers the
		//console and the database's own name for it, and a dump it does not know
		//falls straight to its generic cover rather than having a name guessed from
		//its file (ADR-0265 section 3, ADR-0003).
		//
		//The record travels whole rather than as a bare name because the console is
		//part of the key the collection is asked under (ADR-0265 section 6), and the
		//table is the only thing that knows it: BoxArtLibrary drops an answer filed
		//under another machine rather than asking the wrong repository for it.
		private static NoIntroRomName? NoIntroName(string sha1) => NoIntroNameTable.ForSha1(sha1);
	}
}
