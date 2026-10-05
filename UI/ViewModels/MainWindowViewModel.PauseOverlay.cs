using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Utilities;

namespace Mesen.ViewModels
{
	//G.2 (PRD Part B §8, ADR-0241, §13.5.2 W-P4): the redesigned pause overlay -
	//its header, the values on its rows, the Save states sheet, and the Esc
	//router (rule 8). The controls, where each former overlay action went and
	//the Esc order are host-free in UI/Logic/PlayPauseOverlay; this file maps
	//them onto the existing surfaces. Kept in its own partial so the overlay
	//glue stays out of MainWindowViewModel.cs.
	public partial class MainWindowViewModel
	{
		//W-P4 header and row values, refreshed every time the overlay opens.
		[ObservableProperty] public partial string OverlayGameTitle { get; private set; } = "";
		[ObservableProperty] public partial string SaveStatesRowValue { get; private set; } = "";
		[ObservableProperty] public partial string PackSummary { get; private set; } = "";
		[ObservableProperty] public partial string EnhancementsSummary { get; private set; } = "";

		//ADR-0256 Decision 6 ("Segue o controle na mão"): W-P4's footer names the
		//control in the player's hand, not the keyboard's Esc.
		[ObservableProperty] public partial string OverlayResumeHint { get; private set; } = "";

		//Which device the player is holding: the pad's family, or null for the
		//keyboard and for a pad the app cannot tell apart (the two the footer must
		//not guess between - ADR-0256 Decision 4's reason). The pad navigation
		//bridge (ADR-0256 Decision 2, next to ShortcutHandler) owns the tracker
		//that answers this in the running app and assigns it there; until that
		//lands the answer is the keyboard, which is what the footer said before.
		//The headless tests replace it to drive every state.
		public Func<(PlayInputDevice Device, PadFamily? Family)> InHandDevice { get; set; }
			= () => (PlayInputDevice.Keyboard, null);

		//The Save states sheet (W-P4's merged Save/Load row). It offers today's
		//two slot grids (GameScreenMode.SaveState / LoadState); Esc closes it,
		//and either grid, back to the overlay.
		[ObservableProperty] public partial bool IsSaveStatesSheetVisible { get; set; }

		//The slot grid and the pack picker can also open without the overlay
		//(the quick save/load dialog shortcuts, the picker over an un-enhanced
		//first start), where Esc must not bring an overlay back.
		private bool _stateGridFromOverlay;
		private bool _packPickerFromOverlay;

		private bool IsGameLoaded => RomInfo.Format != RomFormat.Unknown;

		//PlayGameLayer: a Play surface is up over the game, so the native picture,
		//drawn above every Avalonia control, has to step aside for it.
		//
		//Public because it is also ADR-0256 Decision 2's authority input - "is a
		//Play surface up" is what hands the pad to the GUI - and that question is
		//already answered here, once, for the renderer. PlayPadNavigationWiring
		//reads this rather than re-deriving it from the same surfaces.
		public bool IsPlaySurfaceOverGame => PlayGameLayer.SurfaceOverGame(IsPlayerOverlayVisible, CurrentPlaySheet() != PlaySheet.None, BiosSheet.IsVisible, ControllerSetup.IsVisible, IsLoadWaitActive)
			|| SelectRomSheet.IsVisible || IsShaderSheetVisible || ToolSheet.IsVisible;

		//ADR-0256 (accepted 2026-10-04) Decisions 1 and 2: the W-P13 BIOS sheet and
		//the #734 load card share the same load - IsLoadWaitActive is true under
		//both - but they are not the same surface to the pad. The card is the one
		//Play surface with no focusable control of its own (the home stays under
		//it), so the pad has nothing to drive there and authority would only let a
		//Confirm reach the home and launch a game through the card; the BIOS sheet
		//does have a focusable control and is the pad's. Named apart so the
		//authority rule is handed the distinction instead of the coarse load flag.
		public bool IsLoadCardVisible => IsLoadWaitActive && !BiosSheet.IsVisible;

		//P.5 (W-P5): the picker opened by itself over an un-enhanced first start,
		//never from W-P4. It is up over a game that is NOT paused
		//(EvaluatePlayerPackPicker never pauses) and still has to be answered
		//before play, so it is the one unpaused surface the pad drives (ADR-0256
		//Decision 2). W-P4's own picker sits over the pause the overlay took, so
		//the ordinary pause pair already covers it.
		public bool IsOnLoadPackPickerVisible => IsPlayerPackPickerVisible && !_packPickerFromOverlay;

		private static readonly HashSet<string> PlaySurfaceProperties = new() {
			nameof(IsPlayerOverlayVisible), nameof(IsSaveStatesSheetVisible), nameof(IsEnhancementsPanelVisible),
			nameof(IsPlayerPackPickerVisible), nameof(IsPackDetailVisible), nameof(IsPlayerSettingsVisible),
			nameof(IsLoadWaitActive), nameof(IsShaderSheetVisible)
		};

		private void WatchPlaySurfaces()
		{
			PropertyChanged += (s, e) => {
				if(PlaySurfaceProperties.Contains(e.PropertyName ?? "")) {
					UpdateRendererVisibility();
				}
			};
			foreach(INotifyPropertyChanged sheet in new INotifyPropertyChanged[] { CheatsSheet, ReplaysSheet, PackDepSheet, BiosSheet, ControllerSetup, SelectRomSheet, ToolSheet, ControllerSheet, RomPicker }) {
				sheet.PropertyChanged += (s, e) => {
					if(e.PropertyName == "IsVisible") {
						UpdateRendererVisibility();
					}
				};
			}
		}

		private PlaySheet CurrentPlaySheet()
		{
			if(IsPlayerSettingsVisible) {
				return PlaySheet.Settings;
			}
			//ADR-0255 slice 1: it replaces the Settings sheet's Controls landing,
			//so the two are never up together.
			if(_controllerSheet?.IsVisible == true) {
				return PlaySheet.Controller;
			}
			if(PackDepSheet.IsVisible) {
				return PlaySheet.PackDep;
			}
			if(IsPlayerPackPickerVisible) {
				return _packPickerFromOverlay ? PlaySheet.PackPickerFromOverlay : PlaySheet.PackPickerOnLoad;
			}
			if(IsEnhancementsPanelVisible) {
				return PlaySheet.Enhancements;
			}
			if(IsPackDetailVisible) {
				return PlaySheet.PackDetail;
			}
			if(_cheatsSheet?.IsVisible == true) {
				return PlaySheet.Cheats;
			}
			if(_replaysSheet?.IsVisible == true) {
				return PlaySheet.Replays;
			}
			if(IsSaveStatesSheetVisible) {
				return PlaySheet.SaveStates;
			}
			if(_stateGridFromOverlay && RecentGames.Visible && RecentGames.Mode != GameScreenMode.RecentGames) {
				return PlaySheet.SaveStateGrid;
			}
			//#845: over the home rather than over the game, so it is read last -
			//it is the one Play sheet that is not opened from W-P4.
			if(_romPicker?.IsVisible == true) {
				return PlaySheet.RomPicker;
			}
			return PlaySheet.None;
		}

		//The overlay shortcut (Esc by default), Player mode in Play only
		//(ShortcutHandler checks both). Order: game → W-P4 → resume; a sheet
		//opened from W-P4 closes back to it.
		public void TogglePlayerOverlay()
		{
			//ADR-0249 (W-X1): Esc on Quit game's question answers it as Keep Playing.
			if(QuitGameConfirm.IsVisible) {
				QuitGameConfirm.Keep();
				return;
			}
			if(HandleEdgeFlowEsc()) {
				return;
			}
			PlaySheet sheet = CurrentPlaySheet();
			switch(PlayEsc.Next(IsGameLoaded, sheet, IsPlayerOverlayVisible, IsControllerCapturing)) {
				case PlayEscAction.CancelCapture:
					//ADR-0255 slice 3: Esc releases the capture and the sheet stays
					//up, ready for another row. The sheet's own tail decides what
					//"cancelled" means on screen.
					ControllerSheet.CancelCapture();
					break;

				case PlayEscAction.DismissPackPicker:
					//P.5: Esc on the first-start picker plays un-enhanced this session.
					DismissPlayerPackPicker();
					break;

				case PlayEscAction.RomPickerBack:
					//#845: one step up the tree, or - on the roots - the dismiss,
					//which the picker performs itself. Never the overlay: this
					//sheet is over the home, and there is no game to come back to.
					RomPicker.Back();
					break;

				case PlayEscAction.CloseSheetToOverlay:
					CloseSheet(sheet);
					OpenPauseOverlay();
					break;

				case PlayEscAction.CloseOverlayAndResume:
					//Back to the game: W-P7's detour (if one was open) ends here.
					EndEnhancementsDraftVisit();
					IsPlayerOverlayVisible = false;
					EmuApi.Resume();
					break;

				case PlayEscAction.OpenOverlayAndPause:
					OpenPauseOverlay();
					EmuApi.Pause();
					break;
			}
		}

		//Hides the sheet without re-showing anything: the router opens the
		//overlay itself, once (#641). A sheet's own Done (its Closed event, or
		//DismissPlayerPackPicker) would open it a second time, which stacked the
		//overlay with the pack-file sheet that the first open had shown.
		private void CloseSheet(PlaySheet sheet)
		{
			switch(sheet) {
				case PlaySheet.PackPickerFromOverlay:
					IsPlayerPackPickerVisible = false;
					_packPickerFromOverlay = false;
					break;
				case PlaySheet.Enhancements:
					//Esc ends the visit: the next open reads what is applied.
					EndEnhancementsDraftVisit();
					IsEnhancementsPanelVisible = false;
					break;
				case PlaySheet.PackDetail: IsPackDetailVisible = false; CancelRestore(); break;
				case PlaySheet.Cheats: HideCheatsSheet(); break;
				case PlaySheet.Replays: HideReplaysSheet(); break;
				case PlaySheet.SaveStates: IsSaveStatesSheetVisible = false; break;
				case PlaySheet.PackDep: PackDepSheet.CloseOnEsc(); break;
				case PlaySheet.Settings: ClosePlayerSettings(); break;
				case PlaySheet.Controller: CloseControllerSheet(); break;
				case PlaySheet.SaveStateGrid:
					//Init with the grid's own mode hides it (RecentGamesViewModel);
					//the overlay had already paused, so nothing resumes.
					RecentGames.Init(RecentGames.Mode);
					_stateGridFromOverlay = false;
					break;
			}
		}

		public void OpenPauseOverlay()
		{
			RefreshPauseOverlay();
			if(OpenPackDepSheetWithOverlay()) {
				return;
			}
			IsPlayerOverlayVisible = true;
		}

		private void RefreshPauseOverlay()
		{
			OverlayGameTitle = RomInfo.GetRomName();
			RefreshCheatsSummary();
			//#736: "Community pack available" when one is not the pack rendering.
			PackSummary = BuildPackSummary();

			RefreshEnhancementsState();
			int on = PauseOverlay.EnhancementsOn(IsModernInstrumentsEnabled, IsBorderEnabled, IsWideScrnEnabled, IsOverclockEnabled, IsOverclockSupported);
			EnhancementsSummary = on == 0 ? ResourceHelper.GetMessage("OverlayRowNone") : ResourceHelper.GetMessage("OverlayRowCountOn", on);

			SaveStatesRowValue = BuildSaveStatesSummary();

			(PlayInputDevice device, PadFamily? family) = InHandDevice();
			PlayResumeHint hint = PlayMenuHint.ResumeHint(device, family);
			OverlayResumeHint = ResourceHelper.GetMessage(hint.Message, hint.Param);
		}

		//"Slot 1 · 2 min ago": the newest of the ten manual slots (the auto-save
		//is not a slot the player chose). Same file names the slot grid uses.
		private string BuildSaveStatesSummary()
		{
			string romName = RomInfo.GetRomName();
			List<(int, DateTime?)> slots = new();
			if(!string.IsNullOrEmpty(romName)) {
				for(int i = 1; i <= 10; i++) {
					string file = Path.Combine(ConfigManager.SaveStateFolder, romName + "_" + i + "." + FileDialogHelper.MesenSaveStateExt);
					slots.Add((i, File.Exists(file) ? new FileInfo(file).LastWriteTime : null));
				}
			}

			SaveStateSlotSummary? newest = SaveStatesSummary.Newest(slots, DateTime.Now);
			if(newest == null) {
				return ResourceHelper.GetMessage("SaveStatesRowEmpty");
			}
			(SaveStateAgeKind kind, int count) = SaveStatesSummary.Age(newest.Age);
			string age = kind switch {
				SaveStateAgeKind.JustNow => ResourceHelper.GetMessage("AgeJustNow"),
				SaveStateAgeKind.Minutes => ResourceHelper.GetMessage("AgeMinutes", count),
				SaveStateAgeKind.Hours => ResourceHelper.GetMessage("AgeHours", count),
				_ => ResourceHelper.GetMessage("AgeDays", count)
			};
			return ResourceHelper.GetMessage("SaveStatesRowSlot", newest.Slot, age);
		}

		public void OpenSaveStatesSheet()
		{
			IsPlayerOverlayVisible = false;
			IsSaveStatesSheetVisible = true;
		}

		public void CloseSaveStatesSheet()
		{
			IsSaveStatesSheetVisible = false;
			OpenPauseOverlay();
		}

		//The sheet's two buttons: today's slot grid, in save or load mode.
		public void OpenSlotGrid(GameScreenMode mode)
		{
			IsSaveStatesSheetVisible = false;
			_stateGridFromOverlay = true;
			RecentGames.Init(mode);
		}

		//#692: the slot grid's own X. Opened from W-P4 it closes back to W-P4,
		//like Esc (the overlay paused the game, so the grid has nothing to
		//resume). Returns false for a grid opened any other way.
		public bool CloseSlotGridToOverlay()
		{
			if(CurrentPlaySheet() != PlaySheet.SaveStateGrid) {
				return false;
			}
			CloseSheet(PlaySheet.SaveStateGrid);
			OpenPauseOverlay();
			return true;
		}

		//W-P4's Pack row: OpenPackFromOverlay (MainWindowViewModel.PlaySheets.cs,
		//G.4) opens W-P5 for 2+ packs or W-P6; Esc returns to the overlay.

		//The game the Play surfaces belong to (PlaySurfaceGame): the last
		//RomInfo seen by OnRomInfoChanged.
		private bool _surfacesGameLoaded;
		private string _surfacesRomPath = "";

		//Any path that leaves the game (power off, a load failure) or replaces
		//it (another ROM opened directly, A → B with no EmulationStopped, #639)
		//takes the overlay and its sheets down: rule 5 is about screens the
		//user opened, and the game they belong to is gone. A reload of the same
		//file keeps them. Its pack's pending file goes with the game.
		private void ClosePauseSurfacesOnGameChange()
		{
			bool loaded = IsGameLoaded;
			string romPath = RomInfo.RomPath ?? "";
			bool close = PlaySurfaceGame.ClosesSurfaces(_surfacesGameLoaded, _surfacesRomPath, loaded, romPath);
			_surfacesGameLoaded = loaded;
			_surfacesRomPath = romPath;
			if(!close) {
				return;
			}
			ClosePlaySurfaces();
			ClearPackDepWithoutGame();
			WithdrawForcedPatchWithoutGame();
		}

		//Every Player surface over the game, hidden at once and silently: no
		//sheet's Closed brings the overlay back (#642 - in Advanced that
		//re-opened a Player overlay that Esc no longer routes). Used when the
		//game changes and when the UI mode leaves Player.
		private void ClosePlaySurfaces()
		{
			HideCheatsSheet();
			HideReplaysSheet();
			ClosePlayerSettings();
			CloseControllerSheet();
			RomPicker.Hide();
			IsSaveStatesSheetVisible = false;
			EndEnhancementsDraftVisit();
			IsEnhancementsPanelVisible = false;
			IsPackDetailVisible = false;
			IsPlayerPackPickerVisible = false;
			if(PackDepSheet.IsVisible) {
				PackDepSheet.CloseOnEsc();
			}
			if(SelectRomSheet.IsVisible) {
				SelectRomSheet.Cancel();
			}
			ToolSheet.Close();
			IsPlayerOverlayVisible = false;
			_stateGridFromOverlay = false;
			_packPickerFromOverlay = false;
		}
	}
}
