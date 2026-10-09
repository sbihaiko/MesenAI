using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Utilities;

namespace Mesen.ViewModels
{
	//G.4 (PRD Part B §8, ADR-0241, §13.5.2 W-P5–W-P9): the Play sheets opened
	//from W-P4's Pack and Enhancements rows, and the HUD pill of a pack that
	//installs while the game plays. Every decision is host-free in UI/Logic
	//(PlayPackSheets, EnhancementsSheet, PackInstallPill); this partial maps it
	//onto bound properties. Data is injected by the code-behind (pack list, ROM
	//sha1, folders, install record), never read here from EmuApi.
	public partial class MainWindowViewModel
	{
		//W-P5: the picker's title and its primary button.
		[ObservableProperty] public partial string PackPickerTitle { get; private set; } = "";
		[ObservableProperty] public partial bool CanUseSelectedPack { get; private set; }

		//W-P6: the current pack's detail. PackDetailTextures/Audio/Patch: the
		//pack has the layer (its switch lives in MainWindowViewModel.PackLayers).
		[ObservableProperty] public partial bool IsPackDetailVisible { get; set; }
		[ObservableProperty] public partial bool PackDetailHasPack { get; private set; }
		[ObservableProperty] public partial string PackDetailTitle { get; private set; } = "";
		[ObservableProperty] public partial string PackDetailByline { get; private set; } = "";
		[ObservableProperty] public partial bool PackDetailTextures { get; private set; }
		[ObservableProperty] public partial bool PackDetailAudio { get; private set; }
		[ObservableProperty] public partial bool PackDetailPatch { get; private set; }
		[ObservableProperty] public partial bool PackDetailHasNotice { get; private set; }
		[ObservableProperty] public partial string PackDetailNoticeBody { get; private set; } = "";
		[ObservableProperty] public partial bool PackDetailCanChange { get; private set; }
		[ObservableProperty] public partial string PackDetailChangeReason { get; private set; } = "";
		[ObservableProperty] public partial bool PackDetailShowsRestore { get; private set; }
		[ObservableProperty] public partial bool PackDetailConfirmingRestore { get; private set; }
		[ObservableProperty] public partial bool PackDetailRestoreRunning { get; private set; }
		[ObservableProperty] public partial string PackDetailFolder { get; private set; } = "";
		[ObservableProperty] public partial bool PackDetailHasFolder { get; private set; }
		[ObservableProperty] public partial bool PackDetailShowsIds { get; set; }
		[ObservableProperty] public partial string PackDetailIds { get; private set; } = "";

		//W-P7: the draft the four switches edit (the Is*Enabled properties stay
		//what is applied, and feed W-P4's "N on").
		[ObservableProperty] public partial bool EnhModernInstruments { get; set; }
		[ObservableProperty] public partial bool EnhBorder { get; set; }
		[ObservableProperty] public partial bool EnhWidescreen { get; set; }
		[ObservableProperty] public partial bool EnhOverclock { get; set; }
		[ObservableProperty] public partial string EnhOverclockReason { get; private set; } = "";
		//ADR-0253 §4 (W.5): the one-line reason under a disabled Widescreen
		//switch; empty when the switch is enabled.
		[ObservableProperty] public partial string EnhWidescreenReason { get; private set; } = "";
		[ObservableProperty] public partial string EnhancementsApplyText { get; private set; } = "";
		//W-P7's last row: "Pack: Contra (USA)  ›", the way into W-P6 (or W-P5).
		[ObservableProperty] public partial string EnhPackRowText { get; private set; } = "";

		//W-P9: the HUD pill's sentence; empty when the pill is hidden.
		[ObservableProperty] public partial string PackInstallPillText { get; private set; } = "";
		[ObservableProperty] public partial bool IsPackInstallPillInstalling { get; private set; }
		//#734: the pill on screen (a popup over the game picture), its bar's
		//fill in percent and whether it is indeterminate.
		[ObservableProperty] public partial bool IsPackInstallPillShown { get; private set; }
		[ObservableProperty] public partial bool IsPackInstallPillFailed { get; private set; }
		[ObservableProperty] public partial double PackInstallPillPercent { get; private set; }
		[ObservableProperty] public partial bool IsPackInstallPillIndeterminate { get; private set; } = true;

		//ADR-0244 / P.9: whether a pack layer change keeps the player's place
		//(the in-place swap of LoadRomHelper.ApplyPackChange), so W-P7 reads
		//Apply rather than Apply & Reload.
		public bool LayerChangeKeepsPlace => RomInfo.Format != RomFormat.Unknown
			&& LoadRomHelper.PlanPackChange(RomInfo.ConsoleType).Route == PackChangeRoute.InPlace;

		private RestoreStep _restoreStep = RestoreStep.Idle;
		private EnhancementsState _enhancementsApplied = new(false, false, false, false);
		//W-P7: how the last visit of the panel ended; decides whether the next
		//open keeps the draft (only the Pack row's detour does).
		private EnhancementsDraftExit _enhancementsDraftExit = EnhancementsDraftExit.Closed;
		private readonly PackInstallPill _pill = new();
		private DispatcherTimer? _pillTimer;

		partial void OnPlayerPackChoicesChanged(List<PlayerPackChoice> value)
		{
			UpdateCanUseSelectedPack();
		}

		//W-P5: the stored choice starts selected, else the first (👍-first) row.
		private void SelectInitialPackChoice(string? preferredContainer)
		{
			int index = PackPickerRow.InitialSelection(PlayerPackChoices.Select(c => c.Container).ToList(), preferredContainer);
			for(int i = 0; i < PlayerPackChoices.Count; i++) {
				PlayerPackChoices[i].IsSelected = i == index;
			}
			PackPickerTitle = ResourceHelper.GetMessage("PackPickerTitle", RomInfo.GetRomName());
			UpdateCanUseSelectedPack();
		}

		public void SelectPackChoice(string container)
		{
			foreach(PlayerPackChoice choice in PlayerPackChoices) {
				choice.IsSelected = choice.Container.Equals(container, StringComparison.OrdinalIgnoreCase);
			}
			UpdateCanUseSelectedPack();
		}

		private void UpdateCanUseSelectedPack()
		{
			CanUseSelectedPack = PlayerPackChoices.Any(c => c.IsSelected);
		}

		//W-P5's Use This Pack: the P.5 pick (stored preference, then the reload
		//that applies it in place through LoadRomHelper.ApplyPackChange, P.9).
		public void UseSelectedPack()
		{
			PlayerPackChoice? selected = PlayerPackChoices.FirstOrDefault(c => c.IsSelected);
			if(selected != null) {
				PickPlayerPack(selected.Container);
			}
		}

		//W-P4's Pack row: W-P5 for 2+ packs, else W-P6 (PackRowRoute). Returns
		//true when the picker opened. packsFolder/siblingFolder locate the pack
		//on disk; installedSourceSha256 is the install registry's (ADR-0147).
		//community: the #736 offer context (null = none to offer); with an
		//offer the row opens W-P6, which holds it.
		public bool OpenPackFromOverlay(string packListText, string romSha1, string packsFolder, string siblingFolder, string? installedSourceSha256, CommunityPackOfferContext? community = null)
		{
			IsPlayerOverlayVisible = false;
			IsEnhancementsPanelVisible = false;
			//PackRowRoute: W-P5 for 2+ packs, else W-P6 - also under "No pack"
			//with one pack, where W-P6 shows the choice and Change Pack… leads back.
			//#736: with a community-pack offer the row opens W-P6, which holds it.
			bool offer = DecideCommunityOffer(packListText, romSha1, community).IsShown;
			BuildPackPickerData(packListText, romSha1, out PackPreferenceResolver.Resolution resolution, out bool hasSibling);
			bool picker = PackRowRoute.For(PlayerPackPicker.DistinctPackIdCount(resolution.Candidates), hasSibling, offer) == PackRowTarget.Picker;
			_packPickerFromOverlay = picker && OpenPlayerPackPickerForChange(packListText, romSha1, offer);
			if(!_packPickerFromOverlay) {
				OpenPackDetail(packListText, romSha1, packsFolder, siblingFolder, installedSourceSha256, community);
			}
			return _packPickerFromOverlay;
		}

		public void OpenPackDetail(string packListText, string romSha1, string packsFolder, string siblingFolder, string? installedSourceSha256, CommunityPackOfferContext? community = null)
		{
			_pickerRomSha1 = romSha1;
			ShowCommunityOffer(DecideCommunityOffer(packListText, romSha1, community), packListText);
			BuildPackPickerData(packListText, romSha1, out PackPreferenceResolver.Resolution resolution, out bool hasSibling);
			UpdateCurrentPack(resolution);

			PlayerPackChoice? current = RenderedPackChoice(resolution);
			MepPackListEntry? entry = current == null ? null : MepPackListParser.Parse(packListText).Packs
				.FirstOrDefault(e => e.Container.Equals(current.Container, StringComparison.OrdinalIgnoreCase));

			string folder = current == null ? "" : PackDetail.FolderFor(current.Origin, current.Container, packsFolder, siblingFolder);
			PackAudioScan? scan = current != null && folder.Length > 0 && PackDetail.CanScan(current.Origin) ? PackAudioNotice.Scan(folder) : null;
			PackDetailModel model = PackDetail.Build(current != null, entry?.Sections ?? "", scan,
				PlayerPackPicker.DistinctPackIdCount(resolution.Candidates), hasSibling, !string.IsNullOrWhiteSpace(installedSourceSha256), folder, resolution.PrefersNoPack, current?.IsAutoOnly == true);

			PackDetailHasPack = model.HasPack;
			//The automatic upscale is made here, not by an author: say that, with
			//the scaler it was made with (the .bootstrap stamp beside the ROM,
			//when known) and no author/version/license (PackDetail.AutoByline).
			PackDetailTitle = model.IsAutomatic ? ResourceHelper.GetMessage("PackDetailAutoTitle") : current?.Name ?? ResourceHelper.GetMessage("PackDetailNoPackTitle");
			//W-P5's "No pack" is a choice, not a missing pack: say so.
			PackDetailByline = model.IsAutomatic ? PackDetail.AutoByline(current!.Name, RemasterProjectReader.BootstrapScaler(model.Folder), sc => ResourceHelper.GetMessage("PackDetailAutoMadeWith", sc), ResourceHelper.GetMessage("PackDetailAutoMade"))
				: current != null ? BuildPackByline(current)
				: ResourceHelper.GetMessage(resolution.PrefersNoPack ? "PackDetailNoPackChosenBody" : "PackDetailNoPackBody");
			PackDetailTextures = model.Chips.Textures;
			PackDetailAudio = model.Chips.Audio;
			PackDetailPatch = model.Chips.Patch;
			LoadPackLayerSwitches(model.Chips);
			PackDetailHasNotice = model.Notice == PackDetailNotice.MissingMusic;
			PackDetailNoticeBody = PackDetailHasNotice ? ResourceHelper.GetMessage("PackDetailMissingMusicBody", model.MissingTracks, model.TotalTracks) : "";
			PackDetailCanChange = model.CanChangePack;
			PackDetailChangeReason = model.CanChangePack ? "" : ResourceHelper.GetMessage(hasSibling ? "PackDetailChangeSibling" : "PackDetailChangeOnlyOne");
			PackDetailShowsRestore = model.ShowsRestore;
			PackDetailFolder = model.Folder;
			PackDetailHasFolder = model.Folder.Length > 0;
			PackDetailShowsIds = false;
			PackDetailIds = current == null ? "" : ResourceHelper.GetMessage("PackDetailIds", current.PackId, current.ContentId.Length > 0 ? current.ContentId : "—", current.Container);
			SetRestoreStep(RestoreStep.Idle);

			IsPlayerOverlayVisible = false;
			IsPackDetailVisible = true;
		}

		//W-P7's Pack row names what renders: the automatic upscale as such, a
		//community offer, the pack's name, or "No pack".
		private string PackRowName()
		{
			bool none = string.IsNullOrWhiteSpace(PackSummary) || PackSummary == ResourceHelper.GetMessage("OverlayRowNone");
			return none ? ResourceHelper.GetMessage("PackDetailNoPackTitle") : PackSummary;
		}

		//"by Tastic · version 1.2 · CC BY-NC 4.0"
		private static string BuildPackByline(PlayerPackChoice pack)
		{
			return PackDetail.Byline(pack.Author, pack.Version, pack.License, a => ResourceHelper.GetMessage("PackByAuthor", a),
				ResourceHelper.GetMessage("PackAuthorUnknown"), v => ResourceHelper.GetMessage("PackDetailVersion", v));
		}

		public void ClosePackDetail()
		{
			IsPackDetailVisible = false;
			SetRestoreStep(RestoreFlow.Cancel(_restoreStep));
			OpenPauseOverlay();
		}

		//W-P6's Change pack…: W-P5, which closes back to W-P4 on Esc/Cancel.
		public void ChangePackFromDetail(string packListText)
		{
			if(!PackDetailCanChange) {
				return;
			}
			IsPackDetailVisible = false;
			_packPickerFromOverlay = OpenPlayerPackPickerForChange(packListText, _pickerRomSha1);
			if(!_packPickerFromOverlay) {
				OpenPauseOverlay();
			}
		}

		//Rule 7: the first press asks in place; returns true when the second
		//press should run the restore.
		public bool PressRestore()
		{
			SetRestoreStep(RestoreFlow.Press(_restoreStep));
			return _restoreStep == RestoreStep.Running;
		}

		public void CancelRestore() => SetRestoreStep(RestoreFlow.Cancel(_restoreStep));

		public void RestoreFinished() => SetRestoreStep(RestoreStep.Idle);

		private void SetRestoreStep(RestoreStep step)
		{
			_restoreStep = step;
			PackDetailConfirmingRestore = step == RestoreStep.Confirming;
			PackDetailRestoreRunning = step == RestoreStep.Running;
		}

		//W-P7: the switches start from what is applied, or - on the way back from
		//the Pack row - from the flips made before the detour (see
		//EnhancementsDraftVisit / EnhancementsSheet.Resume).
		private void LoadEnhancementsDraft(EnhancementsState? heldDraft = null)
		{
			//ADR-0253 §1/§4 (W.5): the applied baseline is this game's effective
			//widescreen, not the saved preference - so a game that cannot use it
			//shows the switch off (and disabled), and Apply has nothing to write.
			//The saved preference stays, and the next game that can use it gets
			//it back (WidescreenSupportRule.EffectiveWidescreen).
			EnhancementsState appliedNow = new(IsModernInstrumentsEnabled, IsBorderEnabled, EffectiveWidescreen, IsOverclockEnabled);
			EnhancementsState draft = heldDraft == null ? appliedNow : EnhancementsSheet.Resume(_enhancementsApplied, heldDraft, appliedNow);
			_enhancementsApplied = appliedNow;
			EnhModernInstruments = draft.ModernInstruments;
			EnhBorder = draft.Border;
			EnhWidescreen = draft.Widescreen;
			EnhOverclock = draft.Overclock;
			EnhPackRowText = ResourceHelper.GetMessage("EnhancementsPackRow", PackRowName());
			//#1081: the row names the console, and the console's name is a string
			//the player reads - so it is the locale file's own word for it
			//(ConsoleTypeNames names the id), not the core enum, whose missing
			//label `GetEnumText` answered with the raw `[[Sms]]` placeholder.
			EnhOverclockReason = IsOverclockSupported ? "" : ResourceHelper.GetMessage("EnhancementsOverclockUnavailable", ResourceHelper.GetMessage(ConsoleTypeNames.MessageId(RomInfo.ConsoleType)));
			UpdateEnhancementsApplyText();
		}

		//ADR-0253 §1/§4 (W.5): what widescreen means for the loaded game - the
		//saved preference only where the game can use it (the switch state
		//RefreshEnhancementsState computed from the core's verdict).
		private bool EffectiveWidescreen => WidescreenSupportRule.EffectiveWidescreen(IsWideScrnEnabled, _widescreenSwitch);

		//W-P7's Pack row: the row opens W-P5/W-P6 over the panel, which is a look
		//at the pack, not a decision about the switches - so the draft waits for
		//the way back (the window calls this before it routes the row).
		public void HoldEnhancementsDraftForPackRow()
		{
			_enhancementsDraftExit = EnhancementsDraftExit.PackRow;
		}

		//The visit ends (Esc, the button, the game changing): the next open of
		//the panel reads the switches from what is applied again.
		private void EndEnhancementsDraftVisit()
		{
			_enhancementsDraftExit = EnhancementsDraftExit.Closed;
		}

		private EnhancementsState EnhancementsDraft => new(EnhModernInstruments, EnhBorder, EnhWidescreen && IsWidescreenSupported, EnhOverclock && IsOverclockSupported);

		partial void OnEnhModernInstrumentsChanged(bool value) => UpdateEnhancementsApplyText();
		partial void OnEnhBorderChanged(bool value) => UpdateEnhancementsApplyText();
		partial void OnEnhWidescreenChanged(bool value) => UpdateEnhancementsApplyText();
		partial void OnEnhOverclockChanged(bool value) => UpdateEnhancementsApplyText();

		private void UpdateEnhancementsApplyText()
		{
			EnhancementsApplyText = EnhancementsSheet.Pending(_enhancementsApplied, EnhancementsDraft, LayerChangeKeepsPlace) switch {
				EnhancementsApplyKind.Apply => ResourceHelper.GetMessage("EnhancementsApply"),
				EnhancementsApplyKind.Reload => ResourceHelper.GetMessage("EnhancementsApplyReload"),
				EnhancementsApplyKind.Restart => ResourceHelper.GetMessage("EnhancementsApplyRestart"),
				_ => ResourceHelper.GetMessage("EnhancementsDone")
			};
		}

		//W-P7's one button. Each change goes through the path that already owns
		//it: SetModernInstruments (live), ToggleWideScrn (renderer only),
		//ToggleLayer (the Border pack reload, which P.9 turns into
		//LoadRomHelper.ApplyPackChange) and ToggleOverclock (the power cycle).
		public void ApplyEnhancements()
		{
			EnhancementsState applied = _enhancementsApplied;
			EnhancementsState draft = EnhancementsDraft;
			EnhancementsApplyKind kind = EnhancementsSheet.Pending(applied, draft, LayerChangeKeepsPlace);

			if(applied.Widescreen != draft.Widescreen) {
				ToggleWideScrn();
			}

			if(applied.ModernInstruments != draft.ModernInstruments) {
				SetModernInstruments(draft.ModernInstruments);
			}

			//Border is the one pack layer left here; with Overclock pending, its
			//power cycle reloads the pack too, so it is written first.
			if(applied.Overclock != draft.Overclock) {
				if(applied.Border != draft.Border) {
					Config.EnhancementPacks.EnableBorder = draft.Border;
					Config.EnhancementPacks.ApplyConfig();
				}
				ToggleOverclock();
			} else if(applied.Border != draft.Border) {
				ToggleBorder();
			}

			//The button is what applies the draft: the visit is over either way.
			EndEnhancementsDraftVisit();
			RefreshEnhancementsState();
			IsEnhancementsPanelVisible = false;
			if(kind == EnhancementsApplyKind.Reload || kind == EnhancementsApplyKind.Restart) {
				//The game restarted: back to it, not to a pause menu over a new start.
				IsPlayerOverlayVisible = false;
				EmuApi.Resume();
			} else {
				OpenPauseOverlay();
			}
		}

		//W-P9, fed by CommunityPackInstallService.InstallProgress on the UI thread.
		public void OnPackInstallStarted(string packName)
		{
			_pill.Begin(packName);
			UpdatePill();
		}

		//CommunityPackInstallService.InstallProgress: the artifact's bytes so far.
		public void OnPackInstallProgress(long received, long? total)
		{
			if(_pill.Report(received, total)) {
				UpdatePillBar();
			}
		}

		partial void OnIsPlayWorkspaceChanged(bool value)
		{
			UpdatePillBar();
		}

		private void UpdatePillBar()
		{
			IsPackInstallPillShown = PackInstallPill.ShowsOnScreen(_pill.State, Config.Preferences.UiMode == UiMode.Player, IsPlayWorkspace);
			IsPackInstallPillFailed = _pill.State == PackInstallPillState.Failed;
			IsPackInstallPillIndeterminate = _pill.Fraction == null;
			PackInstallPillPercent = (_pill.Fraction ?? 0) * 100;
		}

		public void OnPackInstallFinished(bool installed, bool silent)
		{
			if(silent) {
				_pill.Cancel();
			} else {
				_pill.Finish(installed, DateTime.Now);
			}
			UpdatePill();
		}

		private void UpdatePill()
		{
			string previous = PackInstallPillText;
			PackInstallPillText = _pill.State switch {
				PackInstallPillState.Installing => ResourceHelper.GetMessage("PackInstallPillInstalling", _pill.PackName),
				PackInstallPillState.Failed => ResourceHelper.GetMessage("PackInstallPillFailed"),
				_ => ""
			};
			IsPackInstallPillInstalling = _pill.State == PackInstallPillState.Installing;
			UpdatePillBar();
			Shell.UpdatePackInstall(PackInstallPillText);

			if(PackInstallPillText.Length > 0 && PackInstallPillText != previous) {
				PostPillToHud();
			}
			if(_pill.State == PackInstallPillState.Hidden) {
				_pillTimer?.Stop();
				return;
			}
			if(_pillTimer == null) {
				_pillTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PackInstallPillHud.RepostMilliseconds) };
				_pillTimer.Tick += (_, _) => {
					_pill.Tick(DateTime.Now);
					if(_pill.State == PackInstallPillState.Installing) {
						PostPillToHud();
					}
					if(_pill.State == PackInstallPillState.Hidden) {
						UpdatePill();
					}
				};
			}
			_pillTimer.Start();
		}

		//Where the pill is not on screen (Advanced mode) the sentence is the
		//core's HUD message (the same channel as the W-P3 toast). A HUD message
		//lasts PackInstallPillHud.RepostMilliseconds; it is posted again while
		//the install runs. In Player mode's Play the pill is a popup - its own
		//window, above the native game picture - with a moving bar (#734).
		private void PostPillToHud()
		{
			if(!IsPackInstallPillShown && EmuApi.IsRunning()) {
				EmuApi.DisplayMessage("MEP", PackInstallPillText);
			}
		}
	}

	//The core's HUD message duration (SystemHud::DisplayMessage, 3000 ms).
	public static class PackInstallPillHud
	{
		public const int RepostMilliseconds = 3000;
	}
}
