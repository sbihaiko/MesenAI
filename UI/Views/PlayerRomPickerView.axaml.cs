using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace Mesen.Views
{
	//#845 (ADR-0256 Decision 9): thin code-behind over
	//PlayerRomPickerViewModel - a row descends or opens, and Back is the same
	//step Esc takes (the router's RomPickerBack reaches the same method).
	public class PlayerRomPickerView : UserControl
	{
		public PlayerRomPickerView()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private PlayerRomPickerViewModel? Model => DataContext as PlayerRomPickerViewModel;

		private void OnChoose(object? sender, RoutedEventArgs e)
		{
			if(sender is Control { DataContext: PlayerRomPickerRow row }) {
				Model?.Choose(row);
			}
		}

		//#1032 (ADR-0264 Decision 3): A plays the focused game. The tile IS the
		//choice, so this is the whole of the press.
		private void OnPlayTile(object? sender, RoutedEventArgs e)
		{
			if(sender is Control { DataContext: PlayerLibraryTile tile }) {
				Model?.Play(tile);
			}
		}

		//#1039 (ADR-0265 section 4): the covers are asked for lazily and only for the
		//tiles the sheet is showing. WHO those are is a question about layout, so it is
		//answered here rather than guessed at in the view-model: every container the
		//WrapPanel has realized is measured against the ScrollViewer's viewport, and
		//the tiles that intersect it are handed over. This fires on the layout that
		//fills the grid and again on every scroll, so a tile below the fold is asked
		//about the moment it comes into view.
		//
		//The ring is the second way in: the pad moves the real focus (ADR-0256 Decision
		//3), and OnTileFocus below asks about whatever it reaches, in case a tile is
		//reached before its layout has settled.
		private void OnGridShowing(object? sender, EffectiveViewportChangedEventArgs e) => AskShowing();

		//The viewport event stays silent when the grid itself changes under an
		//unchanged ScrollViewer (the scan landing after the sheet laid out empty, a
		//search rebuilding the tiles), so a change of the tiles asks again, posted
		//at Render priority so the containers exist and are measured by then.
		private PlayerRomPickerViewModel? _watched;
		private bool _askPosted;

		protected override void OnDataContextChanged(EventArgs e)
		{
			base.OnDataContextChanged(e);
			if(_watched != null) {
				_watched.Tiles.CollectionChanged -= OnTilesChanged;
			}
			_watched = Model;
			if(_watched != null) {
				_watched.Tiles.CollectionChanged += OnTilesChanged;
			}
		}

		private void OnTilesChanged(object? sender, NotifyCollectionChangedEventArgs e) => PostAskShowing();

		private void OnGridLayoutUpdated(object? sender, EventArgs e) => PostAskShowing();

		private void PostAskShowing()
		{
			if(_askPosted) {
				return;
			}
			_askPosted = true;
			Dispatcher.UIThread.Post(() => {
				_askPosted = false;
				AskShowing();
			}, DispatcherPriority.Render);
		}

		private void AskShowing()
		{
			if(this.FindControl<ItemsControl>("RomPickerGrid") is not { } grid || Model is not { } model) {
				return;
			}
			if(grid.FindAncestorOfType<ScrollViewer>() is not { } sheet) {
				return;
			}

			//The viewport is a rectangle in the content's own coordinates, which is
			//what a container's Bounds are measured in - the WrapPanel sits at the
			//content's origin and the containers sit in the panel.
			Rect viewport = new(sheet.Offset.X, sheet.Offset.Y, sheet.Viewport.Width, sheet.Viewport.Height);
			List<PlayerLibraryTile> showing = new();
			for(int i = 0; i < model.Tiles.Count; i++) {
				if(grid.ContainerFromIndex(i) is Control { } container && container.Bounds.Intersects(viewport)) {
					showing.Add(model.Tiles[i]);
				}
			}
			model.AskVisible(showing);
		}

		//#1039 (ADR-0265 section 4): the ring is the other half of "the tiles that are
		//actually visible" - a tile the pad has reached is being looked at whatever the
		//layout says, so it is asked about here too, and nothing else in the library
		//ever is.
		//
		//#1037 (ADR-0264 Decision 1): the same event is also the game the player is
		//on, so the sheet can reopen on it. The event, not the click: the arbiter
		//putting the ring on a tile is the player being on it too, and the tile the
		//ring left when the sheet closed is exactly the one this has to remember.
		//One GotFocus attribute binds one handler, so both reactions live here.
		private void OnTileFocused(object? sender, FocusChangedEventArgs e)
		{
			if(sender is Control { DataContext: PlayerLibraryTile tile }) {
				Model?.TileReached(tile);
				Model?.RememberFocus(tile);
			}
		}

		//#1032 (ADR-0264 Decision 11): *Browse a file…* steps into the folder
		//browser ADR-0256 Decision 9 built, which is the same sheet's second
		//surface rather than a second sheet.
		private void OnBrowseFile(object? sender, RoutedEventArgs e) => Model?.BrowseFile();

		//#1033 (ADR-0264 Decision 4): the empty result's way out - the box empties
		//and the whole library comes back. The press that clears it also parks the
		//ring (ADR-0256 Decision 3): Clear hides ITSELF the moment the query empties,
		//so a pad player who pressed A on it was left with no focus at all - the next
		//D-pad press had nowhere to move from and the ring was simply gone. The
		//search box is still on screen and is where the player who just undid a
		//search is, so the ring goes there through the one focus entry point.
		private void OnClearSearch(object? sender, RoutedEventArgs e)
		{
			Model?.ClearSearch();
			if(this.FindControl<TextBox>("RomPickerSearch") is TextBox field) {
				Utilities.PlayFocusOnOpen.Enter(field);
			}
		}

		private void OnBack(object? sender, RoutedEventArgs e) => Model?.Back();

		//#1036 (ADR-0264 Decision 8): *Library folders…*, and the two presses inside
		//the sheet it opens. Add is the MOUSE door - the native folder dialog, which
		//is what a player at a desk expects. The pad's Confirm never lands here: the
		//bridge answers it with the sheet's own folder browser instead, because a
		//native dialog owns the screen once it is up (PlayPadNavigationWiring).
		private void OnLibraryFolders(object? sender, RoutedEventArgs e) => Model?.OpenFoldersSheet();

		private async void OnAddFolder(object? sender, RoutedEventArgs e)
		{
			if(Model is PlayerRomPickerViewModel model) {
				await model.AddFolderFromMouse();
			}
		}

		//The row's own Remove. It edits the list and nothing else: no file call is
		//made on this path, on purpose.
		private void OnRemoveFolder(object? sender, RoutedEventArgs e)
		{
			if(sender is Control { DataContext: PlayerLibraryFolderRow row }) {
				Model?.RemoveLibraryFolder(row);
			}
		}
	}
}
