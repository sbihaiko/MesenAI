using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Mesen.Logic;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace Mesen.ViewModels
{
	//#1039 (ADR-0265 section 4): the downloaded covers the library grid draws.
	//
	//Lazy, and bounded to what the player can see. The grid is filled from the
	//scan and every tile draws its generic console-coloured cover straight away -
	//the sheet never waits on a network (ADR-0265 section 9) - and the covers are
	//asked for separately, one call per tile, for the tiles that are actually on
	//screen: the window that fits the sheet when the grid lands, plus every tile the
	//ring reaches afterwards. Nothing else in the library is ever asked about, so a
	//twenty-thousand-ROM scan costs its first screenful and stops there.
	//
	//The work is the cache's (BoxArtCache: the switch, the ceiling, the timeout, the
	//remembered miss); this half decides only WHO is asked about and WHEN, and it
	//hands each answer to the tile it belongs to. A tile whose art is unavailable -
	//offline, a 404, a ROM the table does not know - simply keeps its generic cover.
	public partial class PlayerRomPickerViewModel
	{
		//How many tiles the sheet shows at once, and therefore how many covers are
		//asked for when the grid lands before the player has touched anything.
		//W-P19's sheet is 1000 px wide and its tile is 112 px with a 12 px gap, so
		//eight fit across, and three rows of 153 px fit in the 620 px sheet: this is
		//that block. It is a number of REQUESTS, not a rule about art - a tile
		//outside the window is asked for the moment the ring reaches it.
		public const int CoverWindow = 24;

		//One tile's downloaded cover, or null when there is none to show. The app
		//wires this to the box-art cache (see MainWindowViewModel); a test injects a
		//fake and drives the sheet without a network, and a null source - the
		//default - means the grid stays generic, which is also what every case that
		//is not about box art wants.
		public Func<LibraryEntry, CancellationToken, Task<BoxArtCover?>>? BoxArtCoverSource { get; set; }

		//One open, one lifetime: a tile that walks away - the sheet closed, a second
		//scan replacing the grid - has nothing left to draw into, and the download it
		//started is not what the player is waiting for (ADR-0265 section 7: a call
		//the caller cancelled records nothing).
		private CancellationTokenSource? _coverLifetime;

		//What has already been asked for, by the ROM's path. One tile, one call: a
		//ring that moves back and forth over the same tile does not reach the network
		//twice, and the cache's own answer (a cover or a remembered miss) is not paid
		//for again either.
		private readonly HashSet<string> _coversAsked = new(StringComparer.OrdinalIgnoreCase);

		//The tiles holding a decoded image, so the bitmaps go when the grid they
		//belong to does. A tile's cover is a file-backed Bitmap, and dropping the
		//tile without disposing it would leak the decoded pixels for the life of the
		//process - the grid is rebuilt on every scan, so this is the one place the
		//old ones are known.
		private readonly List<PlayerLibraryTile> _coversDrawn = new();

		public PlayerRomPickerViewModel()
		{
			Tiles.CollectionChanged += TilesReplaced;
		}

		//The grid was rebuilt: the tiles of the previous scan are gone from the tree
		//with their containers, so their images can go too - and the new tiles are
		//the ones to ask about, as far as the window reaches.
		private void TilesReplaced(object? sender, NotifyCollectionChangedEventArgs e)
		{
			if(e.Action == NotifyCollectionChangedAction.Reset) {
				ReleaseCovers();
				_coversAsked.Clear();
				return;
			}
			if(e.NewItems is null) {
				return;
			}
			for(int i = 0; i < e.NewItems.Count; i++) {
				int index = e.NewStartingIndex < 0 ? i : e.NewStartingIndex + i;
				if(index < CoverWindow && e.NewItems[i] is PlayerLibraryTile tile) {
					Ask(tile);
				}
			}
		}

		//The ring reached a tile: whatever the sheet cannot show without scrolling
		//is still seen the moment it is looked at, so it is asked about here. The
		//view calls this on the focus change (PlayerRomPickerView), which is what
		//keeps the whole thing pad-driven with no scrolling code of its own.
		public void TileReached(PlayerLibraryTile tile)
		{
			if(IsVisible && Mode == RomPickerMode.Library) {
				Ask(tile);
			}
		}

		//One tile's cover, off the UI thread and out of the player's way. Nothing is
		//awaited by the caller: the answer lands on the tile when it lands, and the
		//tile draws its generic cover until then.
		private void Ask(PlayerLibraryTile tile)
		{
			if(BoxArtCoverSource is not { } source || !_coversAsked.Add(tile.Path)) {
				return;
			}

			//The lifetime belongs to the open, not to this tile: created on the first
			//ask of an open and cancelled when the sheet goes or the grid is rebuilt.
			CancellationTokenSource lifetime = _coverLifetime ??= new CancellationTokenSource();
			CancellationToken token = lifetime.Token;
			LibraryEntry entry = new(tile.Path, tile.Console, tile.Title);

			_ = Task.Run(async () => {
				BoxArtCover? cover;
				try {
					cover = await source(entry, token).ConfigureAwait(false);
				} catch(Exception) {
					//The chain answers for everything a network and a disk can do
					//(ADR-0265 section 9); this is the belt to that braces, because a
					//throw here would be an unobserved task exception rather than a
					//tile without art.
					cover = null;
				}
				if(cover is null || token.IsCancellationRequested) {
					return;
				}
				Dispatcher.UIThread.Post(() => {
					if(!token.IsCancellationRequested) {
						tile.ShowArt(cover);
						if(!_coversDrawn.Contains(tile)) {
							_coversDrawn.Add(tile);
						}
					}
				});
			});
		}

		//The sheet closed. Nothing of this open is worth finishing: the tiles are
		//about to be out of sight, and a request that is no longer drawn is exactly
		//the kind ADR-0265 section 7 says must not be remembered as a miss.
		partial void OnIsVisibleChanged(bool value)
		{
			if(!value) {
				CancelCovers();
			}
		}

		//A second grid replaces the first (another scan, another open): the covers
		//of the first belong to tiles nobody will see again.
		partial void OnModeChanged(RomPickerMode value)
		{
			if(value != RomPickerMode.Library) {
				CancelCovers();
			}
		}

		private void CancelCovers()
		{
			_coverLifetime?.Cancel();
			_coverLifetime?.Dispose();
			_coverLifetime = null;
			_coversAsked.Clear();
		}

		private void ReleaseCovers()
		{
			foreach(PlayerLibraryTile tile in _coversDrawn) {
				tile.ReleaseArt();
			}
			_coversDrawn.Clear();
		}
	}

	//The tile's own half of #1039 (ADR-0265 section 6): the cover the download
	//produced, and the priority it was chosen by. The tile has one picture and two
	//sources for it - the downloaded art, and the generic console-coloured cover it
	//is built with - and the art wins wherever there is any, which is what makes
	//"box art, then title screen, then the generic cover" the order the player sees.
	//
	//(`LibraryCover.RecentScreenshot`, the third case of ADR-0264 Decision 6, is not
	//this slice's: it arrives as the entry's own `Cover` and needs no change here -
	//the downloaded art simply outranks it, and a tile with no art keeps whatever
	//the scan gave it.)
	public partial class PlayerLibraryTile : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler? PropertyChanged;

		private IImage? _art;
		private BoxArtCoverKind? _downloaded;

		//The downloaded cover, once it is there. Null while the tile draws its
		//generic cover, which is the state a tile is in from the scan until (and
		//unless) its art arrives.
		public IImage? Art => _art;

		//The template's two halves: with art the picture is drawn and the title
		//comes off the cover (the tile carries it under the cover either way),
		//without it the generic cover keeps its title.
		public bool HasArt => _art is not null;

		//The title the template writes ON the cover, and the one rule both cover
		//sources share: it is written on the console-coloured generic cover because
		//a colour says nothing about the game, and it comes off the moment a
		//picture covers the tile - the player's own screenshot (#1035,
		//ShowsTitleOnCover) or the downloaded box art (#1039). Stamping it across a
		//cover is the one thing a cover must not do, so the two conditions are
		//weighed together here rather than at the call site, where a binding could
		//only express one of them.
		public bool ShowsTitle => ShowsTitleOnCover && _art is null;

		//Which collection the cover came from. Box art above title screen is
		//ADR-0265 section 1's own order, and it is the cache that decided it - the
		//tile only says which one answered.
		public BoxArtCoverKind? DownloadedCover => _downloaded;

		//The image's opacity, bound so that the picture FADES in: the change from 0
		//to 1 when the art lands is what the sheet's own transition animates, so a
		//cover never appears out of nowhere under the player's ring.
		public double ArtOpacity => _art is null ? 0 : 1;

		//Hand the tile its downloaded cover. A file the decoder refuses leaves the
		//tile generic rather than blank: a cover that cannot be read is the same
		//thing to the player as one that was never downloaded.
		internal void ShowArt(BoxArtCover cover)
		{
			IImage image;
			try {
				image = new Bitmap(cover.FilePath);
			} catch(Exception) {
				return;
			}
			ReleaseArt();
			_art = image;
			_downloaded = cover.Kind;
			Raise(nameof(Art), nameof(HasArt), nameof(ShowsTitle), nameof(DownloadedCover), nameof(ArtOpacity));
		}

		//Let the decoded image go. Called when the grid a tile belongs to is
		//replaced, so a re-scan does not hold every previous cover's pixels.
		internal void ReleaseArt()
		{
			if(_art is null) {
				return;
			}
			(_art as IDisposable)?.Dispose();
			_art = null;
			_downloaded = null;
			Raise(nameof(Art), nameof(HasArt), nameof(ShowsTitle), nameof(DownloadedCover), nameof(ArtOpacity));
		}

		private void Raise(params string[] names)
		{
			foreach(string name in names) {
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
			}
		}
	}
}
