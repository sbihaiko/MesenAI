using Avalonia.Threading;
using Mesen.Config;
using Mesen.Logic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mesen.ViewModels
{
	//#1038 (ADR-0266, spec #1030): the canonical-title pass. The grid is up the
	//moment the scan lands, carrying the cleaned file name each tile already has
	//(GameLibrary.CleanTitle); this reads those ROMs through RomHashCache, looks
	//each hash up in the embedded No-Intro table, and a tile that matches is
	//renamed in place to the database's own title.
	//
	//Three rules shape it, and all three are why it is a pass of its own rather
	//than a step inside the scan:
	//
	//  - **Hashing never blocks the grid.** The pass starts only once the tiles
	//    are in the collection and in their containers, does every stat, read and
	//    hash on the thread pool, and touches a tile only from a posted turn. A
	//    library of a few hundred ROMs therefore opens instantly and fills its
	//    real titles in as they arrive (ADR-0264 Decisions 7 and 10).
	//
	//  - **A tile is renamed, never replaced.** The pass holds the tile objects
	//    the scan created and sets their Title, so the grid's containers - and
	//    the focus ring on one of them - survive the rename. Handing the grid a
	//    new list would re-claim the ring out from under a player already moving
	//    across it.
	//
	//  - **The grid's ORDER stays the scan's answer.** The tiles keep the places
	//    GameLibrary.Scan gave them (ordered by the title it knew, the cleaned
	//    file name); the rename never re-sorts. Live re-sorting would move a tile
	//    under the player's finger. An order by CANONICAL titles would mean the
	//    scan reading the cached hash itself, which is a change to the scan and
	//    not to this pass.
	public partial class PlayerRomPickerViewModel
	{
		//How many renames one posted turn carries. The pass resolves hundreds of
		//tiles, and one post per tile would be a queue of hundreds of tiny turns
		//for a change nobody sees individually; one post for the whole library
		//would hold every title back until the slowest ROM answered. A small
		//batch is both: titles fill in as they are known.
		private const int TitleBatch = 16;

		//The No-Intro table the pass reads hashes through: the app's own embedded
		//table (ADR-0266) by default, null in a build that carries none. A null
		//table means the pass does nothing at all - the same "a library of file
		//names" answer CanonicalTitles gives. A test replaces it with a table of
		//its own, so the wiring is proved without a real dump on the machine
		//running the suite.
		//
		//Read once per process and not once per sheet: it is the same immutable
		//artifact every time, and inflating and parsing 17 900 rows to show a
		//player their own library would be a cost the sheet pays for nothing.
		public NoIntroNameTable? NoIntroTable { get; set; } = EmbeddedNoIntroTable.Value;

		private static readonly Lazy<NoIntroNameTable?> EmbeddedNoIntroTable =
			new(() => NoIntroNameTable.LoadEmbedded());

		//How a tile's path becomes its payload SHA-1. The default is the real
		//RomHashCache over the app's own home folder - that class states the
		//caller resolves the directory, and this is the caller - created on first
		//use, so a sheet nobody opens never touches the disk. A test replaces it
		//with an answer of its own, which is also how a test holds a hash back to
		//prove the grid does not wait for it.
		//
		//The token is part of the seam because it is part of the answer: a pass
		//that is cancelled mid-read has to interrupt the READ, not only the loop
		//around it - a 200 MB archive must not be hashed to the end for a tile the
		//sheet has already dropped (review finding 2 on #1038).
		public Func<string, RomConsole, CancellationToken, Task<string>> RomHashSource
		{
			get => _romHashSource ??= DefaultRomHashSource;
			set => _romHashSource = value;
		}

		private Func<string, RomConsole, CancellationToken, Task<string>>? _romHashSource;
		private RomHashCache? _romHashes;

		private Task<string> DefaultRomHashSource(string path, RomConsole console, CancellationToken cancellationToken)
		{
			_romHashes ??= new RomHashCache(Path.Combine(ConfigManager.HomeFolder, "RomHashes"));
			return _romHashes.GetSha1Async(path, console, cancellationToken);
		}

		//The pass running NOW, and the one thing that can stop it. Kept beside the
		//generation rather than inside the pass because two different events stop
		//it - a new scan, and the sheet closing - and neither of them is the pass's
		//own business (review finding 2 on #1038).
		private CancellationTokenSource? _titlePass;

		//Stops the pass in flight, if there is one. Cancelled and NOT disposed: the
		//pass may be inside a read that the token is about to interrupt, and the
		//source has to outlive that answer - the registration a source hands out is
		//what a disposed one takes away.
		private void StopCanonicalTitles()
		{
			CancellationTokenSource? pass = _titlePass;
			_titlePass = null;
			pass?.Cancel();
		}

		//The sheet closing is half of "stop the pass": a player who leaves the
		//library leaves no hash walk behind them, whatever the scan's own
		//generation says. Hooked to the property rather than written into every
		//caller - Play, Back, Hide, and whatever closes the sheet next - because
		//the state is what the rule is about.
		partial void OnIsVisibleChanged(bool value)
		{
			if(!value) {
				StopCanonicalTitles();
			}
		}

		//The pass, started by the scan that filled the grid. Called once per scan
		//and only from that scan's own apply turn, so two passes never race for
		//the same tile: ApplyLibraryScan builds new tiles every time, so the
		//tiles a pass holds belong to the scan that made them and to no other.
		//
		//One pass at a time, and only one: the pass the previous scan started is
		//stopped here, before this one begins (review finding 2 on #1038). The
		//scan the sheet shows is the only scan whose hashes are worth reading, and
		//without this every reopen of the sheet over a large library added another
		//full walk - each one reading up to MaxEntries = 20000 files to the end.
		//The generation it carries is checked beside the token, because the two
		//answer different questions: the token says "this pass was stopped", the
		//generation says "the surface this pass read is not the one on the sheet".
		private void StartCanonicalTitles(int generation)
		{
			StopCanonicalTitles();
			if(NoIntroTable is null || Tiles.Count == 0) {
				return;
			}

			//The tiles this pass owns, captured once: a later scan clears the
			//collection, and a pass that walked it live would be reading a grid
			//somebody else had already replaced.
			List<PlayerLibraryTile> tiles = Tiles.ToList();
			CancellationTokenSource pass = new();
			_titlePass = pass;
			CancellationToken token = pass.Token;
			_ = Task.Run(async () => {
				List<(PlayerLibraryTile Tile, string Title)> batch = new(TitleBatch);
				foreach(PlayerLibraryTile tile in tiles) {
					//Before every read, never after: a stopped pass must not so
					//much as ask the next file for its hash.
					if(token.IsCancellationRequested || !_scanGeneration.IsCurrent(generation)) {
						return;
					}
					string title = await ResolveTitle(tile, token).ConfigureAwait(false);
					if(string.Equals(title, tile.Title, StringComparison.Ordinal)) {
						continue;
					}
					batch.Add((tile, title));
					if(batch.Count >= TitleBatch) {
						PostTitles(batch, token, generation);
						batch = new List<(PlayerLibraryTile, string)>(TitleBatch);
					}
				}
				if(batch.Count > 0) {
					PostTitles(batch, token, generation);
				}
			});
		}

		//One ROM's title once its hash is known, and the cleaned file name
		//whenever it is not. Nothing below this line may throw: a library read
		//that fails on one file - a volume unplugged mid-scan, a permission the
		//player's account does not have - leaves that tile exactly as the scan
		//named it and carries on with the rest.
		private async Task<string> ResolveTitle(PlayerLibraryTile tile, CancellationToken cancellationToken)
		{
			//An entry this rule can never name is not read at all (review finding 4
			//on #1038): a zipped game is one tile on the grid (ADR-0264 Decision 9),
			//and hashing the archive would read it to the end for a lookup that
			//cannot match - the tile keeps the scan's own title, which is the same
			//title the loose ROM beside it gets.
			if(!CanonicalTitles.IsTitleablePath(tile.Path)) {
				return tile.Title;
			}
			try {
				string sha1 = await RomHashSource(tile.Path, tile.Console, cancellationToken).ConfigureAwait(false);
				return CanonicalTitles.Resolve(tile.Title, sha1, NoIntroTable);
			} catch {
				return tile.Title;
			}
		}

		//One batch into the grid, on the UI thread and only while the library is
		//the surface that is up - the same guard the scan's own apply makes, for
		//the same reason: a pass that finishes after the player left the sheet
		//has nothing to say to it.
		//
		//The token and the generation are read HERE as well, on the far side of
		//the post (review finding 2 on #1038). A batch posted a moment before the
		//pass was stopped is still a batch about tiles the sheet has replaced:
		//IsVisible and Mode say which surface is up, and neither of them knows
		//that this batch belongs to an earlier scan of the same surface.
		private void PostTitles(List<(PlayerLibraryTile Tile, string Title)> batch, CancellationToken token, int generation)
		{
			//Cheapest answer first: a stopped pass does not even take a turn on
			//the UI thread.
			if(token.IsCancellationRequested) {
				return;
			}
			Dispatcher.UIThread.Post(() => {
				if(token.IsCancellationRequested || !_scanGeneration.IsCurrent(generation)) {
					return;
				}
				if(!IsVisible || Mode != RomPickerMode.Library) {
					return;
				}
				foreach((PlayerLibraryTile tile, string title) in batch) {
					tile.Title = title;
				}
			});
		}
	}
}
