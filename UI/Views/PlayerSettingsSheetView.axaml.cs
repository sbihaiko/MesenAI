using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.Logic;

namespace Mesen.Views
{
	//ADR-0249 (W-P8, W-P10): thin code-behind. Done is raised to the window,
	//which owns the sheet's lifetime (MainWindowViewModel.PlayerSettings).
	public class PlayerSettingsSheetView : UserControl
	{
		public event EventHandler? DoneRequested;

		//ADR-0255 slice 1: Controls' "More in Options…" lands on the Play
		//Controller sheet now, which the window opens (it owns the game, the
		//paused state and the way back to W-P4). A view nobody wired - the
		//designer, a headless test of the sheet alone - keeps the classic page.
		public event EventHandler? ControllerSheetRequested;

		public PlayerSettingsSheetView()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		//Audio's link: the classic Audio page, in the Options window. Controls'
		//is the Controller sheet when the window is listening; Look has no link.
		private void OnMoreInOptions(object? sender, RoutedEventArgs e)
		{
			if(DataContext is ViewModels.ConfigViewModel { PlayerMode: true } model && PlayerSettingsEssentials.TabAt(model.PlayerTabIndex) is ConfigWindowTab tab) {
				if(tab == ConfigWindowTab.Input && ControllerSheetRequested != null) {
					ControllerSheetRequested.Invoke(this, EventArgs.Empty);
					return;
				}
				model.OpenInOptions(tab);
			}
		}

		private void OnDone(object? sender, RoutedEventArgs e) => DoneRequested?.Invoke(this, EventArgs.Empty);
	}
}
