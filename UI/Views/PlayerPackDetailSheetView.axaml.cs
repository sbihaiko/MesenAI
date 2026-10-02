using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.ViewModels;

namespace Mesen.Views
{
	//G.4 (W-P6): thin code-behind. Change pack… and Restore need the core's
	//pack list / the install service, so they are raised to the window, which
	//owns the EmuApi calls (ChangePackRequested, RestoreRequested).
	public class PlayerPackDetailSheetView : UserControl
	{
		public event EventHandler? ChangePackRequested;
		public event EventHandler? RestoreRequested;

		public PlayerPackDetailSheetView()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private MainWindowViewModel? Model => DataContext as MainWindowViewModel;

		private void OnChange(object? sender, RoutedEventArgs e) => ChangePackRequested?.Invoke(this, EventArgs.Empty);

		private void OnShowFolder(object? sender, RoutedEventArgs e)
		{
			string folder = Model?.PackDetailFolder ?? "";
			if(folder.Length == 0 || !Directory.Exists(folder)) {
				return;
			}
			Process.Start(new ProcessStartInfo() {
				FileName = folder + Path.DirectorySeparatorChar,
				UseShellExecute = true,
				Verb = "open"
			});
		}

		//First press asks in place; the confirm button's press runs it.
		private void OnRestore(object? sender, RoutedEventArgs e)
		{
			if(Model?.PressRestore() == true) {
				RestoreRequested?.Invoke(this, EventArgs.Empty);
			}
		}

		private void OnKeep(object? sender, RoutedEventArgs e) => Model?.CancelRestore();
		private void OnDone(object? sender, RoutedEventArgs e) => Model?.ClosePackDetail();
	}
}
