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

		public PlayerSettingsSheetView()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		//Audio's and Controls' link: the classic page of the tab, in the Options window.
		private void OnMoreInOptions(object? sender, RoutedEventArgs e)
		{
			if(DataContext is ViewModels.ConfigViewModel { PlayerMode: true } model && PlayerSettingsEssentials.TabAt(model.PlayerTabIndex) is ConfigWindowTab tab) {
				model.OpenInOptions(tab);
			}
		}

		private void OnDone(object? sender, RoutedEventArgs e) => DoneRequested?.Invoke(this, EventArgs.Empty);
	}
}
