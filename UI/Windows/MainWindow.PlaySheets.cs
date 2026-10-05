using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Services;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Views;

namespace Mesen.Windows
{
	//G.4 (PRD Part B §8, ADR-0241, §13.5.2 W-P5–W-P9): the window side of the
	//Play sheets. It owns the EmuApi reads the ViewModel is handed (pack list,
	//ROM sha1, folders, install record) and the jobs a sheet starts (Restore).
	public partial class MainWindow
	{
		//W-P8's Scale row: the renderer's height over the console's, in physical
		//pixels (0 before a game has drawn).
		public double CurrentScale
		{
			get
			{
				FrameInfo baseSize = EmuApi.GetBaseScreenSize();
				return baseSize.Height == 0 ? 0 : _rendererPanel.Bounds.Height * LayoutHelper.GetLayoutScale(this) / baseSize.Height;
			}
		}

		private void InitPlaySheets()
		{
			//ADR-0255 slice 1 (W-P17): Settings › Controls' link is the Play
			//Controller sheet; the sheet's own link leaves for the classic Input
			//page.
			PlayerSettingsSheetView settingsSheet = this.GetControl<PlayerSettingsSheetView>("PlayerSettingsSheetHost");
			settingsSheet.DoneRequested += (_, _) => _model.ClosePlayerSettingsToOverlay();
			settingsSheet.ControllerSheetRequested += (_, _) => OpenControllerSheetFromSettings();
			PlayerControllerSheetView controllerSheet = this.GetControl<PlayerControllerSheetView>("PlayerControllerSheetHost");
			controllerSheet.DoneRequested += (_, _) => _model.CloseControllerSheetToOverlay();
			controllerSheet.MoreInOptionsRequested += (_, _) => OpenClassicControllerPage();
			PlayerPackDetailSheetView detail = this.GetControl<PlayerPackDetailSheetView>("PlayerPackDetailHost");
			detail.ChangePackRequested += (_, _) => _model.ChangePackFromDetail(EmuApi.GetMepPackList());
			//W-P7's Pack row routes like W-P4's: W-P5 for 2+ packs, else W-P6.
			//Leaving by it is a detour, not a decision: the unapplied draft waits
			//for the way back (ADR-0244 Decision 3, only the button applies it).
			this.GetControl<PlayerEnhancementsSheetView>("PlayerEnhancementsSheetHost").PackRequested += (_, _) => {
				_model.HoldEnhancementsDraftForPackRow();
				OnOverlayPack(null, new RoutedEventArgs());
			};
			detail.RestoreRequested += (_, _) => RestorePackFromDetail();
			detail.UseCommunityPackRequested += (_, _) => UseCommunityPackFromDetail();
			//#736: W-P4's Pack row reads the community-pack offer when it opens.
			_model.ReadPackRowState = ReadPackRowState;

			//W-P9: the auto-install's pill. Static events, so unsubscribe when
			//the window goes (headless tests open several MainWindows).
			Action<string> started = name => _model.OnPackInstallStarted(name);
			Action<bool, bool> finished = (installed, silent) => _model.OnPackInstallFinished(installed, silent);
			Action<long, long?> progress = (received, total) => _model.OnPackInstallProgress(received, total);
			CommunityPackInstallService.InstallStarted += started;
			CommunityPackInstallService.InstallFinished += finished;
			CommunityPackInstallService.InstallProgress += progress;
			Closed += (_, _) => {
				CommunityPackInstallService.InstallStarted -= started;
				CommunityPackInstallService.InstallFinished -= finished;
				CommunityPackInstallService.InstallProgress -= progress;
			};
		}

		//Window.GetControl only sees this window's own name scope; the sheets
		//are UserControls with their own, so their controls are found by walking
		//the visual tree.
		private Control? FindNamedDescendant(string name)
		{
			return this.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == name);
		}

		//W-P4's Pack row: W-P5 for 2+ packs (even with a stored choice -
		//"changing the choice later"), else W-P6 inspects the one pack (or none).
		private void OnOverlayPack(object? sender, RoutedEventArgs e)
		{
			string romSha1 = EmuApi.GetMepRomSha1();
			_model.OpenPackFromOverlay(EmuApi.GetMepPackList(), romSha1, ConfigManager.EnhancementPackFolder, EmuApi.GetMepSiblingFolder(), InstalledSourceSha256(romSha1), CommunityOfferContext(romSha1));
		}

		//ADR-0249 (W-P10 › W-P6): Settings › Look's Art row - the Settings sheet
		//closes, keeping what was changed (as Done does), and the current pack's
		//detail sheet opens, closing back to W-P4 like every sheet from it.
		//Posted: closing the sheet disposes Look's view-model, whose row's click
		//is still running.
		public void OpenPackDetailFromSettings()
		{
			Dispatcher.UIThread.Post(() => {
				_model.ClosePlayerSettings();
				string romSha1 = EmuApi.GetMepRomSha1();
				_model.OpenPackDetail(EmuApi.GetMepPackList(), romSha1, ConfigManager.EnhancementPackFolder, EmuApi.GetMepSiblingFolder(), InstalledSourceSha256(romSha1), CommunityOfferContext(romSha1));
			});
		}

		//#736: the catalog row for the loaded game (the catalog copy on disk,
		//no network) and the install registry's container for it.
		private CommunityPackOfferContext CommunityOfferContext(string romSha1)
		{
			return CommunityPackInstallService.ReadOfferContext(romSha1, _model.RomInfo.GetRomName());
		}

		private PackRowState? ReadPackRowState()
		{
			if(!EmuApi.IsRunning()) {
				return null;
			}
			string romSha1 = EmuApi.GetMepRomSha1();
			return new PackRowState(EmuApi.GetMepPackList(), romSha1, CommunityOfferContext(romSha1));
		}

		//#736: W-P6's Use Community Pack. The player asked for this pack for
		//this game, so it is turned back on and/or chosen (an installed pack),
		//or installed even with auto-install off - which stays off.
		private void UseCommunityPackFromDetail()
		{
			CommunityPackOffer offer = _model.CommunityOffer;
			switch(offer.Action) {
				case CommunityPackOfferAction.TurnOn:
					ConfigManager.Config.EnhancementPacks.SetPackEnabled(offer.Container, true);
					_model.UseOfferedPack(CurrentPackList(), offer.Container);
					break;
				case CommunityPackOfferAction.Choose:
					_model.UseOfferedPack(CurrentPackList(), offer.Container);
					break;
				case CommunityPackOfferAction.Install:
					if(CommunityPackInstallService.InstallOnRequest()) {
						_model.LeaveDetailForCommunityInstall();
					} else {
						EmuApi.WriteLogEntry("[CommunityPack] W-P6 Use Community Pack: an install or Restore is already running");
					}
					break;
			}
		}

		//Read after a turn-on, so the list carries the pack as enabled. Through
		//the model's seam, which headless tests replace.
		private string CurrentPackList()
		{
			return _model.ReadPackRowState?.Invoke()?.PackList ?? EmuApi.GetMepPackList();
		}

		private static string? InstalledSourceSha256(string romSha1)
		{
			return string.IsNullOrWhiteSpace(romSha1) ? null : CommunityPackInstallRegistry.Read(CommunityPackPaths.CacheRoot, romSha1)?.SourceSha256;
		}

		//W-P8 (ADR-0249): Player mode's Settings is a sheet in this window - the
		//essentials strip (Display, Look, Audio, Controls), opened on Display;
		//Done and Esc keep the changes and return to W-P4 while a game is loaded.
		private void OnOverlaySettings(object? sender, RoutedEventArgs e) => OpenPlayerSettingsSheet();

		//ADR-0250: also the shared tail's Settings… in every task door (Tools ⋯,
		//or the macOS app menu).
		public void OpenPlayerSettingsSheet()
		{
			ConfigViewModel settings = new(ConfigWindowTab.Display, playerMode: true, CreateDisplaySettings);
			settings.PropertyChanged += OnPlayerSettingsChanged;
			_model.OpenPlayerSettings(settings);
			//Keyboard and gamepad start on the strip (rule: everything reachable)
			//- the sheet's own claim in PlayPadNavigationWiring, through the one
			//path, so a sheet opened with something else already up cannot grab
			//the keyboard from it.
		}

		//G.4 (W-P8): Display edits this window - its full screen and scale.
		private PlayerWindowSettingsViewModel CreateDisplaySettings()
		{
			return new PlayerWindowSettingsViewModel(ConfigManager.Config.Video, WindowState == WindowState.FullScreen, CurrentScale, ToggleFullscreen, SetScale);
		}

		//Look's "More in Options…" leaves the essentials (ConfigViewModel turns
		//PlayerMode off): the sheet closes, keeping what was changed, and the
		//classic Options window opens on the tab it was asked for (Video from
		//Look, Audio or Input from their own tab) - Advanced territory. Closing it
		//returns to W-P4 while the game runs, as the sheet would.
		private void OnPlayerSettingsChanged(object? sender, PropertyChangedEventArgs e)
		{
			if(e.PropertyName != nameof(ConfigViewModel.PlayerMode) || sender is not ConfigViewModel { PlayerMode: false } settings) {
				return;
			}
			settings.PropertyChanged -= OnPlayerSettingsChanged;
			//After SelectTab finishes: it is still running on this view-model.
			Dispatcher.UIThread.Post(() => {
				if(_model.PlayerSettings != settings) {
					return;
				}
				_model.ClosePlayerSettings();
				ConfigWindow options = _model.MainMenu.OpenConfig(this, settings.SelectedIndex);
				options.Closed -= OnOptionsFromSettingsClosed;
				options.Closed += OnOptionsFromSettingsClosed;
			});
		}

		private void OnOptionsFromSettingsClosed(object? sender, EventArgs e)
		{
			if(sender is ConfigWindow wnd) {
				wnd.Closed -= OnOptionsFromSettingsClosed;
			}
			if(ConfigManager.Config.Preferences.UiMode == UiMode.Player && EmuApi.IsRunning()) {
				_model.OpenPauseOverlay();
			}
		}

		//ADR-0255 slice 1 (W-P17): Settings › Controls › More in Options… opens
		//the Play Controller sheet over the paused game, not the classic window.
		//Posted for the same reason OpenPackDetailFromSettings is: this closes the
		//Settings sheet from inside its own row's click, disposing the view-model
		//that is still running. Without a game there is no W-P4 to return to and
		//nothing for the sheet to read, so the classic Input page stays that
		//landing (a task door's Settings… can be open with no game at all). The
		//sheet pauses the game itself when it opens - its reads need that, and it
		//knows it before the window would.
		private void OpenControllerSheetFromSettings()
		{
			Dispatcher.UIThread.Post(() => {
				ConfigViewModel? settings = _model.PlayerSettings;
				if(settings == null) {
					return;
				}
				if(!_model.OpenControllerSheet()) {
					settings.OpenInOptions(ConfigWindowTab.Input);
				}
			});
		}

		//The Controller sheet's own "More in Options…": the classic Input page,
		//where remapping and the per-console device rows live. Same tail as the
		//Settings sheet's link - closing it returns to W-P4 while a game runs.
		private void OpenClassicControllerPage()
		{
			_model.CloseControllerSheet();
			ConfigWindow options = _model.MainMenu.OpenConfig(this, ConfigWindowTab.Input);
			options.Closed -= OnOptionsFromSettingsClosed;
			options.Closed += OnOptionsFromSettingsClosed;
		}

		//W-P6's Restore (ADR-0147), after its one in-place confirm (rule 7): a
		//job with the W-P9 pill, never a window (rule 6). On success the game
		//restarts to load the restored files - the confirm said so - unless
		//another game was opened (or this one quit) during the download (#643).
		private async void RestorePackFromDetail()
		{
			try {
				int openGeneration = _model.OpenGeneration;
				string romSha1 = EmuApi.GetMepRomSha1();
				(bool ok, string error) = await CommunityPackInstallService.RestoreInstalledPack();
				_model.RestoreFinished();
				switch(RestoreFlow.After(ok, openGeneration, _model.OpenGeneration, romSha1, EmuApi.GetMepRomSha1())) {
					case RestoreOutcome.Failed:
						EmuApi.WriteLogEntry("[CommunityPack] W-P6 Restore failed: " + error);
						return;
					case RestoreOutcome.Stale:
						EmuApi.WriteLogEntry("[CommunityPack] W-P6 Restore finished after the game changed; the loaded game is not restarted");
						return;
				}
				ConfigManager.Config.EnhancementPacks.ApplyConfig();
				_model.IsPackDetailVisible = false;
				_model.IsPlayerOverlayVisible = false;
				LoadRomHelper.PowerCycle();
				EmuApi.Resume();
			} catch(Exception ex) {
				_model.RestoreFinished();
				EmuApi.WriteLogEntry("[CommunityPack] W-P6 Restore failed: " + ex.Message);
			}
		}
	}
}
