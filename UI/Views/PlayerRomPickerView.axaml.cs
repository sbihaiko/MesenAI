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

		private void OnBack(object? sender, RoutedEventArgs e) => Model?.Back();
	}
}
