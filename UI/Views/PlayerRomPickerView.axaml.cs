using Avalonia.Controls;
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

		//#1032 (ADR-0264 Decision 11): *Browse a file…* steps into the folder
		//browser ADR-0256 Decision 9 built, which is the same sheet's second
		//surface rather than a second sheet.
		private void OnBrowseFile(object? sender, RoutedEventArgs e) => Model?.BrowseFile();

		private void OnBack(object? sender, RoutedEventArgs e) => Model?.Back();
	}
}
