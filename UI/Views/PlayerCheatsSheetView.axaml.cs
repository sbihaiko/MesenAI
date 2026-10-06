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

		private async void OnFindIntent(object? sender, RoutedEventArgs e)
		{
			if(Model != null) {
				await Model.SearchByIntent();
			}
		}

		private async void OnIntentKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
		{
			if(e.Key == Avalonia.Input.Key.Enter && Model != null) {
				e.Handled = true;
				await Model.SearchByIntent();
			}
		}

		private void OnSaveKey(object? sender, RoutedEventArgs e) => Model?.SaveKey();

		private void OnRemoveKey(object? sender, RoutedEventArgs e) => Model?.RemoveKey();

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

		private void OnOpenIssue(object? sender, RoutedEventArgs e)
		{
			if(sender is Control { DataContext: PlayerCheatRow row }) {
				Model?.OpenIssue(row);
			}
		}

		private void OnShareCheat(object? sender, RoutedEventArgs e)
		{
			if(sender is Control { DataContext: PlayerCheatRow row }) {
				Model?.Share(row);
			}
		}

		private void OnChangeGame(object? sender, RoutedEventArgs e) => Model?.ChangeGame();
		private void OnAddCode(object? sender, RoutedEventArgs e) => Model?.ToggleAddCode();
		private void OnConfirmAddCode(object? sender, RoutedEventArgs e) => Model?.AddCode();
		private void OnDone(object? sender, RoutedEventArgs e) => Model?.Close();
	}
}
