using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Mesen.Views
{
	//ADR-0249 (W-P8, W-P10): thin code-behind. Done is raised to the window,
	//which owns the sheet's lifetime (MainWindowViewModel.PlayerSettings).
	public class PlayerSettingsSheetView : UserControl
	{
		public event EventHandler? DoneRequested;

		public PlayerSettingsSheetView()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private void OnDone(object? sender, RoutedEventArgs e) => DoneRequested?.Invoke(this, EventArgs.Empty);
	}
}
