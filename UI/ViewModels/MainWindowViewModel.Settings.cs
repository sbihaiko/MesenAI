using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Logic;

namespace Mesen.ViewModels
{
	//ADR-0249 (PRD Part B §13.5.2 W-P8, W-P10): W-P4's Settings row opens
	//Settings as a sheet in this window, like the other Play sheets, not as a
	//separate window. The sheet edits the live config, as the window did;
	//closing it - Done, Esc, or the game going - keeps what was changed (there
	//is no Cancel in Player mode).
	public partial class MainWindowViewModel
	{
		[ObservableProperty, NotifyPropertyChangedFor(nameof(IsPlayerSettingsVisible)), NotifyPropertyChangedFor(nameof(IsPlayerSystemTabVisible))]
		public partial ConfigViewModel? PlayerSettings { get; private set; }

		public bool IsPlayerSettingsVisible => PlayerSettings != null;

		//ADR-0256 Decision 8: the sheet's System tab is a surface of its own for
		//the pad - the storage choice is what it lands on when the sheet is
		//showing that tab, instead of the tab strip every other tab opens on
		//(PlayPadNavigationWiring's claim order). The tab's view model is built
		//per open (ConfigViewModel.System), so the answer is read off the sheet's
		//own tab index rather than registered on the view model itself.
		public bool IsPlayerSystemTabVisible => PlayerSettings is { PlayerMode: true } settings
			&& settings.PlayerTabIndex == PlayerSettingsEssentials.IndexOf(ConfigWindowTab.System);

		//The tab index is the sheet's own state and nothing on this view model
		//changes when the player picks another tab, so the claim above would
		//never be re-asked without this: the sheet tells the window, which
		//re-raises the one property the arbiter watches.
		//
		//Only when the answer changed (#1133): every tab change re-arbitrated the
		//focus, and the pad's Right onto the next tab selects it, so the arbiter
		//took the ring straight back to the strip's first tab - a move between
		//two tabs that both leave the System tab closed asked nothing new.
		private bool _playerSystemTabWasVisible;

		private void OnPlayerSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if(e.PropertyName == nameof(ConfigViewModel.PlayerTabIndex) && _playerSystemTabWasVisible != IsPlayerSystemTabVisible) {
				_playerSystemTabWasVisible = IsPlayerSystemTabVisible;
				OnPropertyChanged(nameof(IsPlayerSystemTabVisible));
			}
		}

		partial void OnPlayerSettingsChanging(ConfigViewModel? oldValue, ConfigViewModel? newValue)
		{
			if(oldValue != null) {
				oldValue.PropertyChanged -= OnPlayerSettingsPropertyChanged;
			}
			if(newValue != null) {
				newValue.PropertyChanged += OnPlayerSettingsPropertyChanged;
			}
			_playerSystemTabWasVisible = newValue is { PlayerMode: true } opened
				&& opened.PlayerTabIndex == PlayerSettingsEssentials.IndexOf(ConfigWindowTab.System);
		}

		public void OpenPlayerSettings(ConfigViewModel settings)
		{
			ClosePlayerSettings();
			IsPlayerOverlayVisible = false;
			PlayerSettings = settings;
		}

		//Done: back to W-P4 while a game is loaded in Play. Opened from another
		//door's Tools ⋯ or app menu (ADR-0250), Done just closes it.
		public void ClosePlayerSettingsToOverlay()
		{
			ClosePlayerSettings();
			if(IsGameLoaded && Shell.IsPlay) {
				OpenPauseOverlay();
			}
		}

		//Hides the sheet and saves, without re-showing anything (the Esc router
		//and "More in Options…" decide what comes next).
		public void ClosePlayerSettings()
		{
			//Look's Adjust… goes with the sheet it was opened from, unsaved.
			CloseShaderSheet(false);
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
