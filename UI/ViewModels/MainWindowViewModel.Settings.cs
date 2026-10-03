using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;

namespace Mesen.ViewModels
{
	//ADR-0249 (PRD Part B §13.5.2 W-P8, W-P10): W-P4's Settings row opens
	//Settings as a sheet in this window, like the other Play sheets, not as a
	//separate window. The sheet edits the live config, as the window did;
	//closing it - Done, Esc, or the game going - keeps what was changed (there
	//is no Cancel in Player mode).
	public partial class MainWindowViewModel
	{
		[ObservableProperty, NotifyPropertyChangedFor(nameof(IsPlayerSettingsVisible))]
		public partial ConfigViewModel? PlayerSettings { get; private set; }

		public bool IsPlayerSettingsVisible => PlayerSettings != null;

		public void OpenPlayerSettings(ConfigViewModel settings)
		{
			ClosePlayerSettings();
			IsPlayerOverlayVisible = false;
			PlayerSettings = settings;
		}

		//Done: back to W-P4 while a game is loaded.
		public void ClosePlayerSettingsToOverlay()
		{
			ClosePlayerSettings();
			if(IsGameLoaded) {
				OpenPauseOverlay();
			}
		}

		//Hides the sheet and saves, without re-showing anything (the Esc router
		//and "More in Options…" decide what comes next).
		public void ClosePlayerSettings()
		{
			ConfigViewModel? settings = PlayerSettings;
			if(settings == null) {
				return;
			}
			//The view lets go of the view-models first, so nothing writes to the
			//config while they are disposed (ConfigWindow.OnClosing's order).
			PlayerSettings = null;
			settings.SaveConfig();
			settings.Dispose();
			PreferencesConfig.UpdateTheme();
		}
	}
}
