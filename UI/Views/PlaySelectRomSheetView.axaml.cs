using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Mesen.ViewModels;
using System.Linq;

namespace Mesen.Views
{
	//ADR-0249 (W-P5 sheet shape): thin code-behind over
	//PlaySelectRomSheetViewModel. Enter in the search field opens the first
	//game it keeps; Down moves from it to the list (as SelectRomWindow does).
	public class PlaySelectRomSheetView : UserControl
	{
		public PlaySelectRomSheetView()
		{
			InitializeComponent();
			this.GetControl<TextBox>("SelectRomSheetSearch").AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private PlaySelectRomSheetViewModel? Model => DataContext as PlaySelectRomSheetViewModel;

		private void OnSearchKeyDown(object? sender, KeyEventArgs e)
		{
			if(e.Key == Key.Enter) {
				Model?.ChooseFirst();
				e.Handled = true;
			} else if(e.Key == Key.Down) {
				this.GetControl<ItemsControl>("SelectRomSheetList").GetVisualDescendants().OfType<Button>().FirstOrDefault()?.Focus();
				e.Handled = true;
			}
		}

		private void OnChoose(object? sender, RoutedEventArgs e)
		{
			if(sender is Control { DataContext: PlaySelectRomRow row }) {
				Model?.Choose(row);
			}
		}

		private void OnCancel(object? sender, RoutedEventArgs e) => Model?.Cancel();
	}
}
