using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.ViewModels;

namespace Mesen.Views
{
	//G.6 (W-X3): thin code-behind over InterruptionViewModel.
	public class InterruptionBar : UserControl
	{
		public InterruptionBar()
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
