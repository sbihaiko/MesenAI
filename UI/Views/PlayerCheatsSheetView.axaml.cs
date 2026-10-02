using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.Logic;
using Mesen.ViewModels;

namespace Mesen.Views
{
	//P.10 (W-P11): thin code-behind - every click is one call into the sheet's
	//ViewModel, which asks the host-free UI/Logic/CheatSheet.
	public class PlayerCheatsSheetView : UserControl
	{
		public PlayerCheatsSheetView()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private PlayerCheatsSheetViewModel? Model => DataContext as PlayerCheatsSheetViewModel;

		private void OnToggleCheat(object? sender, RoutedEventArgs e)
		{
			if(sender is Control { DataContext: PlayerCheatRow row }) {
				Model?.Toggle(row);
			}
		}

		private void OnPickGame(object? sender, RoutedEventArgs e)
		{
			if(sender is Control { DataContext: CheatDbGame game }) {
				Model?.PickGame(game);
			}
		}

		private void OnChangeGame(object? sender, RoutedEventArgs e) => Model?.ChangeGame();
		private void OnAddCode(object? sender, RoutedEventArgs e) => Model?.ToggleAddCode();
		private void OnConfirmAddCode(object? sender, RoutedEventArgs e) => Model?.AddCode();
		private void OnDone(object? sender, RoutedEventArgs e) => Model?.Close();
	}
}
