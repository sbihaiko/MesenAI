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
	//  - **The grid's ORDER follows the titles it shows.** Decision 1 orders the
	//    grid by title and Decision 7 makes the canonical title the displayed
	//    one, so when a canonical title arrives the order it belongs in is not
	//    the one the scan left: the walk re-sorts once, when it ends, and the
	//    tiles move under the player rather than being rebuilt. That moves a tile
	//    under a finger, and it ships because the alternative is worse: the grid
	//    would sit in an order the titles it shows contradict - a library whose
	//    canonical names put *Castlevania* before *Contra* would keep the file
	//    names' order until the next scan, which is the scan's order and not
	//    Decision 1's. What makes it safe to ship is that the ring is given back
	//    to the GAME the player had selected rather than to the place it held
	//    (review finding 5 on #1038), and that the re-sort is ONE change to the
	//    grid, computed off the UI thread (review finding 2 on #1038) - the tiles
	//    are the same objects throughout, so a tile is never dropped and rebuilt.
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

		//Lazy and thread-safe: the source runs on thread-pool threads, and a
		//cancelled pass whose read is still in flight can overlap the next one, so
		//a plain `??=` could create two caches over the same RomHashes folder
		//(review finding 4 on #1055).
		private readonly Lazy<RomHashCache> _romHashes =
			new(() => new RomHashCache(Path.Combine(ConfigManager.HomeFolder, "RomHashes")));

		private Task<string> DefaultRomHashSource(string path, RomConsole console, CancellationToken cancellationToken)
		{
			return _romHashes.Value.GetSha1Async(path, console, cancellationToken);
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

		//The sheet closing is one of the three ways the pass ends: a player who
		//leaves the library leaves no hash walk behind them, whatever the scan's own
		//generation says. Hooked to the property rather than written into every
		//caller - Play, Back, Hide, and whatever closes the sheet next - because
		//the state is what the rule is about.
		partial void OnIsVisibleChanged(bool value)
		{
			if(!value) {
				StopCanonicalTitles();
			}
		}

		//The second way: the sheet's OTHER surface (#1038 review finding 4). *Browse
		//a file…* leaves the library without closing the sheet, so IsVisible stays
		//true and no new scan has started - and the walk would go on hashing the
		//library behind a player who is now in the browser, every batch of it thrown
		//away by the `Mode != Library` guard in PostTitles, while the browser's own
		//scan wants the same disk. Hooked to Mode for the same reason as IsVisible:
		//the state is the rule, so every way of leaving the library is covered by the
		//one hook rather than by whoever remembers to call it.
		partial void OnModeChanged(RomPickerMode value)
		{
			if(value != RomPickerMode.Library) {
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
			if(NoIntroTable is null || _libraryGames.Count == 0) {
				return;
			}

			//The games this pass owns, captured once: a later scan replaces the
			//list, and a pass that walked it live would be reading a library
			//somebody else had already replaced. The pass holds GAMES and never
			//tiles - a query or a clear rebuilds the tiles under it, so a tile
			//list captured here would be stale by the first keystroke (review
			//finding 1 on #1055). What it resolves goes into the view-model's own
			//titles, and the grid is filled from those.
			IReadOnlyList<LibraryGame> games = _libraryGames;
			CancellationTokenSource pass = new();
			_titlePass = pass;
			CancellationToken token = pass.Token;
			_ = Task.Run(async () => {
				List<(string Path, string Title)> batch = new(TitleBatch);
				//Every game with the title it will read once this walk is over, not
				//only the ones that changed: the order Decision 1 asks for is over
				//the WHOLE grid, and a game whose title the table already agreed
				//with still has its place in it.
				List<(LibraryGame Game, string Title)> resolved = new(games.Count);
				foreach(LibraryGame game in games) {
					//Before every read, never after: a stopped pass must not so
					//much as ask the next file for its hash.
					if(token.IsCancellationRequested || !_scanGeneration.IsCurrent(generation)) {
						return;
					}
					string title = await ResolveTitle(game.Entry, token).ConfigureAwait(false);
					resolved.Add((game, title));
					if(string.Equals(title, game.Entry.Title, StringComparison.Ordinal)) {
						continue;
					}
					batch.Add((game.Entry.Path, title));
					if(batch.Count >= TitleBatch) {
						PostTitles(batch, token, generation);
						batch = new List<(string, string)>(TitleBatch);
					}
				}
				if(batch.Count > 0) {
					PostTitles(batch, token, generation);
				}
				//Once, when the walk is over - never per batch. The order is
				//Decision 1's rule over the titles the games now read, and the
				//titles are not final until the walk is: a re-sort per batch would
				//be up to MaxEntries / TitleBatch sorts of the whole library.
				//
				//Sorted HERE, on this thread and not on the grid's: the ordering is
				//O(n log n) over up to MaxEntries = 20000 games (review finding 2
				//on #1038). What reaches the UI thread is a list already in order.
				PostOrder(games, Ordered(resolved), token, generation);
			});
		}

		//One ROM's title once its hash is known, and the cleaned file name
		//whenever it is not. Nothing below this line may throw: a library read
		//that fails on one file - a volume unplugged mid-scan, a permission the
		//player's account does not have - leaves that tile exactly as the scan
		//named it and carries on with the rest.
		private async Task<string> ResolveTitle(LibraryEntry entry, CancellationToken cancellationToken)
		{
			//An entry this rule can never name is not read at all (review finding 4
			//on #1038): a zipped game is one tile on the grid (ADR-0264 Decision 9),
			//and hashing the archive would read it to the end for a lookup that
			//cannot match - the tile keeps the scan's own title, which is the same
			//title the loose ROM beside it gets.
			if(!CanonicalTitles.IsTitleablePath(entry.Path)) {
				return entry.Title;
			}
			try {
				string sha1 = await RomHashSource(entry.Path, entry.Console, cancellationToken).ConfigureAwait(false);
				return CanonicalTitles.Resolve(entry.Title, sha1, NoIntroTable);
			} catch {
				return entry.Title;
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
		private void PostTitles(List<(string Path, string Title)> batch, CancellationToken token, int generation)
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
				foreach((string path, string title) in batch) {
					_titles.Resolve(path, title);
					//A tile on screen is renamed in place, so its container and the
					//ring on it survive; a game the query has filtered out has no
					//tile and simply reads the new title when it comes back.
					if(_tileByPath.TryGetValue(path, out PlayerLibraryTile? tile)) {
						tile.Title = title;
					}
				}
				//A query that is on is NOT re-asked per batch (review finding 1 on
				//#1055): the grid is re-derived once, when the walk is over (PostOrder).
			});
		}

		//The grid into Decision 1's order, over the titles the tiles now read.
		//Guarded exactly as a batch is - the token and the generation say whether
		//this pass is still the one the sheet is showing.
		//
		//The reorder is ONE change to the collection and never a Move per tile
		//(review finding 2 on #1038). The panel under this list is a non-virtualized
		//WrapPanel: every notification it receives rebuilds a container and
		//invalidates layout, so a `Move` for each tile that changed place is up to
		//MaxEntries = 20000 notifications in a single turn - a frozen grid, exactly
		//where the issue promises hashing never blocks it. `ReplaceAll` says the same
		//thing once, and the list it is handed was already ordered on the thread
		//pool, so nothing here sorts a library.
		private void PostOrder(IReadOnlyList<LibraryGame> captured, IReadOnlyList<LibraryGame> ordered, CancellationToken token, int generation)
		{
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
				//The games this pass walked must still be the library on the sheet.
				if(!ReferenceEquals(_libraryGames, captured)) {
					return;
				}
				//A library already in this order has nothing to be told - unless a
				//query is on, which is asked of titles that may have changed under it.
				//This is the common case - a library of file names the table does not
				//know keeps the order the scan gave it - and it has to stay free: a
				//rebuild drops the ring off the game the player is on for no reason.
				if(!HasQuery && SameOrder(ordered)) {
					return;
				}
				//The order is changed in the view-model's games, and the grid is put
				//in that order from the tiles ON SCREEN NOW (_tileByPath), never from
				//a tile list the pass captured earlier: a list captured at the start
				//would put back tiles the query had dropped and covers the rebuild
				//had released (review finding 1 on #1055). The tiles are the same
				//objects, so a container and the ring on it are reordered and not
				//rebuilt. With a query on, the games it keeps are asked ONCE here, of
				//the titles the walk resolved; a game that starts to match gets a
				//tile and one that stops matching loses it.
				_libraryGames = ordered;
				IEnumerable<LibraryGame> kept = HasQuery
					? _titles.Search(ordered, g => g.Entry, SearchQuery)
					: ordered;
				List<PlayerLibraryTile> shown = new(Tiles.Count);
				foreach(LibraryGame game in kept) {
					if(_tileByPath.TryGetValue(game.Entry.Path, out PlayerLibraryTile? tile)) {
						shown.Add(tile);
					} else if(HasQuery) {
						shown.Add(TileFor(game.Entry, game.Cover));
					}
				}
				if(shown.SequenceEqual(Tiles)) {
					return;
				}
				//Whether the ring was inside the grid, read before the rebuild.
				bool ringInGrid = FocusTile != null && Tiles.Contains(FocusTile);
				Tiles.ReplaceAll(shown);
				if(HasQuery) {
					HashSet<string> paths = new(shown.Select(t => t.Path), StringComparer.Ordinal);
					foreach(string gone in _tileByPath.Keys.Where(path => !paths.Contains(path)).ToList()) {
						_tileByPath.Remove(gone);
					}
					UpdateEmptyResult();
				}
				//The rebuilt containers took the ring with them; this is the same
				//bump ApplyLibraryScan makes, for the same reason - except when the
				//ring is on the search box (a query on and no tile focused), where
				//re-claiming would take the focus off the field the player is typing
				//in (see FillTiles' closing comment). The ring then goes back to the
				//GAME the player selected: see PlayPadNavigationWiring.RomPickerFocusTarget.
				if(!HasQuery || ringInGrid) {
					TilesRevision++;
				}
			});
		}

		//Whether the library already reads in this order, by the games themselves.
		private bool SameOrder(IReadOnlyList<LibraryGame> ordered)
		{
			if(ordered.Count != _libraryGames.Count) {
				return false;
			}
			for(int place = 0; place < ordered.Count; place++) {
				if(!ReferenceEquals(ordered[place], _libraryGames[place])) {
					return false;
				}
			}
			return true;
		}

		//The tiles in Decision 1's order, by the titles this walk resolved for them.
		//The rule is GameLibrary's own - the one its scan already sorted the entries
		//with - and it is called, never copied: a second copy could drift from the
		//order on screen with every test still green (#1038 review finding 3).
		//
		//Ordered by the title each tile WILL read rather than by the one it reads
		//now, because the batches that apply those titles are posted turns and this
		//list is built before they land. The two agree once they do - the batches
		//were posted before this order was, so the dispatcher applies them first -
		//and ordering by what the walk resolved is what keeps this off the UI
		//thread's critical path.
		private static List<LibraryGame> Ordered(List<(LibraryGame Game, string Title)> resolved)
		{
			return resolved
				.OrderBy(entry => entry, TitleOrder)
				.Select(entry => entry.Game)
				.ToList();
		}

		//The comparer the ordering above is: the displayed title of a tile against
		//the path it holds, through GameLibrary's rule.
		private static readonly IComparer<(LibraryGame Game, string Title)> TitleOrder =
			Comparer<(LibraryGame Game, string Title)>.Create((left, right) =>
				GameLibrary.Compare(left.Title, left.Game.Entry.Path, right.Title, right.Game.Entry.Path));

		//The game the ring is on, as the tile itself and never as a place in the
		//grid. The view-model cannot see the ring - Avalonia's focus lives in the
		//visual tree - so the tile's own GotFocus reports it, and the focus arbiter
		//reads it back whenever the grid is rebuilt or re-sorted (see PostOrder).
		public PlayerLibraryTile? FocusTile { get; private set; }

		//The tile that took the ring, reported by the view. Nothing else may call
		//this: it is the ring's own state and not a selection the sheet keeps.
		public void NoteFocusedTile(PlayerLibraryTile tile) => FocusTile = tile;

		//The tiles a scan made are the only ones the ring can legitimately be on, so
		//a scan that replaces them drops the claim with them - the same reason the
		//scan's apply turn rebuilds the grid at all.
		private void ForgetFocusedTile() => FocusTile = null;
	}
}
