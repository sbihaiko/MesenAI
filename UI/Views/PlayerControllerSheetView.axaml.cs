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

		//ADR-0255 slice 2: a PLAYERS row assigns the pad the picker has selected
		//to that row's port. The row carries its port index in its DataContext.
		private void OnAssignPlayer(object? sender, RoutedEventArgs e)
		{
			if(sender is Control { DataContext: ViewModels.ControllerSheetPlayerRow row } && DataContext is ViewModels.ControllerSheetViewModel sheet) {
				sheet.AssignTo(row.PortIndex);
			}
		}

		//ADR-0255 slice 3: a REMAP row is picked. The sheet then waits for the pad
		//control the player wants on it; the row carries the console control in its
		//DataContext, as the PLAYERS rows carry their port.
		private void OnRemapRow(object? sender, RoutedEventArgs e)
		{
			if(sender is Control { DataContext: ViewModels.ControllerSheetRemapRow row } && DataContext is ViewModels.ControllerSheetViewModel sheet) {
				sheet.ArmRemap(row.Button);
			}
		}

		//The keyboard case's action: write the preset back (the ViewModel keeps
		//the guard - it only shows this button when nothing is bound).
		private void OnRestoreKeyboard(object? sender, RoutedEventArgs e)
		{
			if(DataContext is ViewModels.ControllerSheetViewModel sheet) {
				sheet.RestoreKeyboardPreset();
			}
		}
	}
}
