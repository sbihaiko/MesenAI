using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.ViewModels;

namespace Mesen.Views
{
	//ADR-0256 Decision 8: thin code-behind. The choices are bound, so the only
	//action is the relaunch the storage row offers; the view model owns what it
	//does (ConfigManager.RestartMesen, then this window closes).
	public class PlayerSystemSettingsView : UserControl
	{
		public PlayerSystemSettingsView()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private void OnRestart(object? sender, RoutedEventArgs e)
		{
			if(DataContext is PlayerSystemSettingsViewModel model) {
				model.Restart();
			}
		}
	}
}
