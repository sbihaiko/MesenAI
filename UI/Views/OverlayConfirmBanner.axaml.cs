using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.ViewModels;

namespace Mesen.Views
{
	//ADR-0249 (W-X1): thin code-behind over the overlay's InterruptionViewModel
	//(MainWindowViewModel.QuitGameConfirm).
	public class OverlayConfirmBanner : UserControl
	{
		public OverlayConfirmBanner()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private void OnKeep(object? sender, RoutedEventArgs e) => (DataContext as InterruptionViewModel)?.Keep();

		private void OnGo(object? sender, RoutedEventArgs e) => (DataContext as InterruptionViewModel)?.Go();
	}
}
