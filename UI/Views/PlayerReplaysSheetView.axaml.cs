using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.ViewModels;

namespace Mesen.Views
{
	//R.2 (ADR-0205 §7): thin code-behind - every click is one call into the
	//sheet's ViewModel, which asks the host-free UI/Logic/ReplayWatch.
	public class PlayerReplaysSheetView : UserControl
	{
		public PlayerReplaysSheetView()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private PlayerReplaysSheetViewModel? Model => DataContext as PlayerReplaysSheetViewModel;

		private void OnWatch(object? sender, RoutedEventArgs e)
		{
			if(sender is Control { DataContext: PlayerReplayRow row } && Model != null) {
				_ = Model.Watch(row);
			}
		}

		private void OnOpenIssue(object? sender, RoutedEventArgs e)
		{
			if(sender is Control { DataContext: PlayerReplayRow row }) {
				Model?.OpenIssue(row);
			}
		}

		private void OnDone(object? sender, RoutedEventArgs e) => Model?.Close();
	}
}
