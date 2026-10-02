using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.ViewModels;

namespace Mesen.Views
{
	//G.5 (W-P15): thin code-behind over PlayControllerSetupViewModel.
	public class PlayControllerSetupView : UserControl
	{
		public PlayControllerSetupView()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private PlayControllerSetupViewModel? Model => DataContext as PlayControllerSetupViewModel;

		private void OnSkip(object? sender, RoutedEventArgs e) => Model?.Skip();
		private void OnCancel(object? sender, RoutedEventArgs e) => Model?.Cancel();
	}
}
