using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.ViewModels;

namespace Mesen.Views
{
	//G.4 (W-P7): the switches bind the draft TwoWay; the one button applies it.
	public class PlayerEnhancementsSheetView : UserControl
	{
		public PlayerEnhancementsSheetView()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private void OnApply(object? sender, RoutedEventArgs e) => (DataContext as MainWindowViewModel)?.ApplyEnhancements();
	}
}
