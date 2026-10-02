using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.ViewModels;

namespace Mesen.Views
{
	//G.4 (W-P5): thin code-behind - each click is one call into the main
	//ViewModel, which asks the host-free UI/Logic/PlayPackSheets.
	public class PlayerPackPickerSheetView : UserControl
	{
		public PlayerPackPickerSheetView()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private MainWindowViewModel? Model => DataContext as MainWindowViewModel;

		private void OnSelectChoice(object? sender, RoutedEventArgs e)
		{
			if(sender is Control { Tag: string container }) {
				Model?.SelectPackChoice(container);
			}
		}

		private void OnCancel(object? sender, RoutedEventArgs e) => Model?.DismissPlayerPackPicker();
		private void OnUse(object? sender, RoutedEventArgs e) => Model?.UseSelectedPack();
	}
}
