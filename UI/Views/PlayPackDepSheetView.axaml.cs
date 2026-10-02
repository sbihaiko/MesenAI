using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.Utilities;
using Mesen.ViewModels;
using System.Linq;

namespace Mesen.Views
{
	//G.5 (W-P16): thin code-behind over PlayPackDepSheetViewModel.
	public class PlayPackDepSheetView : UserControl
	{
		public PlayPackDepSheetView()
		{
			InitializeComponent();
			AddHandler(DragDrop.DropEvent, OnDrop);
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private PlayPackDepSheetViewModel? Model => DataContext as PlayPackDepSheetViewModel;

		private async void OnDrop(object? sender, DragEventArgs e)
		{
			e.Handled = true;
			string? path = e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.Path.LocalPath;
			if(path != null && Model != null) {
				await Model.TryFile(path);
			}
		}

		private async void OnChooseFile(object? sender, RoutedEventArgs e)
		{
			//Any file: a dep is matched by its content hash, never its name or type.
			string? path = await FileDialogHelper.OpenFile(null, TopLevel.GetTopLevel(this) as Window);
			if(path != null && Model != null) {
				await Model.TryFile(path);
			}
		}

		private void OnShowFolder(object? sender, RoutedEventArgs e) => Model?.ShowFolder();
		private void OnPlayWithout(object? sender, RoutedEventArgs e) => Model?.PlayWithoutIt();
	}
}
