using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.ViewModels;

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

		//#1037 (ADR-0264 Decision 1): the game the player is on, so the sheet can
		//reopen on it. The event, not the click: the arbiter putting the ring on a
		//tile is the player being on it too, and the tile the ring left when the
		//sheet closed is exactly the one this has to remember.
		private void OnTileFocused(object? sender, FocusChangedEventArgs e)
		{
			if(sender is Control { DataContext: PlayerLibraryTile tile }) {
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
	}
}
