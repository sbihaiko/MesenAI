using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.Utilities;
using Mesen.ViewModels;
using System.Linq;

namespace Mesen.Views
{
	//G.5 (W-P13): thin code-behind over PlayBiosSheetViewModel.
	public class PlayBiosSheetView : UserControl
	{
		public PlayBiosSheetView()
		{
			InitializeComponent();
			AddHandler(DragDrop.DropEvent, OnDrop);
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private PlayBiosSheetViewModel? Model => DataContext as PlayBiosSheetViewModel;

		private void OnDrop(object? sender, DragEventArgs e)
		{
			e.Handled = true;
			string? path = e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.Path.LocalPath;
			if(path != null) {
				Model?.TryFile(path);
			}
		}

		private async void OnChooseFile(object? sender, RoutedEventArgs e)
		{
			string? path = await FileDialogHelper.OpenFile(null, TopLevel.GetTopLevel(this) as Window, FileDialogHelper.FirmwareExt);
			if(path != null) {
				Model?.TryFile(path);
			}
		}

		private void OnCancel(object? sender, RoutedEventArgs e) => Model?.Cancel();
		private void OnAcceptUnknown(object? sender, RoutedEventArgs e) => Model?.ConfirmUnknown(true);
		private void OnRejectUnknown(object? sender, RoutedEventArgs e) => Model?.ConfirmUnknown(false);
	}
}
