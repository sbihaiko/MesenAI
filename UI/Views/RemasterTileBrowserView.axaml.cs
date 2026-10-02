using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.ViewModels;

namespace Mesen.Views
{
	//G.7: thin code-behind for zone ② - the rules are in
	//RemasterWorkspaceViewModel.Tiles.cs and UI/Logic/RemasterTileFacts.cs.
	public class RemasterTileBrowserView : UserControl
	{
		public RemasterTileBrowserView()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private RemasterWorkspaceViewModel? Model => DataContext as RemasterWorkspaceViewModel;

		private void OnCategory(object? sender, RoutedEventArgs e)
		{
			if(sender is Control { DataContext: RemasterCategoryChip chip }) {
				Model?.SelectCategory(chip.Category);
			}
		}

		private void OnOpenTile(object? sender, RoutedEventArgs e)
		{
			if(sender is Control { DataContext: RemasterTileRow row }) {
				Model?.OpenTile(row);
			}
		}
	}
}
