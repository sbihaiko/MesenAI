using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Mesen.Views
{
	//ADR-0255 slice 1 (PRD Part B §13.5.2 W-P17): thin code-behind. The sheet's
	//lifetime, its poll and the game it reads are the window's
	//(ControllerSheetViewModel); Done returns to W-P4, and More in Options…
	//leaves for the classic Input page, where remapping and the per-console
	//device rows still live.
	public class PlayerControllerSheetView : UserControl
	{
		public event EventHandler? DoneRequested;
		public event EventHandler? MoreInOptionsRequested;

		public PlayerControllerSheetView()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private void OnDone(object? sender, RoutedEventArgs e) => DoneRequested?.Invoke(this, EventArgs.Empty);

		private void OnMoreInOptions(object? sender, RoutedEventArgs e) => MoreInOptionsRequested?.Invoke(this, EventArgs.Empty);
	}
}
