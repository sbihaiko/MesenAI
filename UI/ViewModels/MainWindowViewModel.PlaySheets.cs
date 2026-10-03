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

		//W-P6: the current pack's detail.
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

		//W-P7: the draft the five switches edit (the Is*Enabled properties stay
		//what is applied, and feed W-P4's "N on").
		[ObservableProperty] public partial bool EnhTextures { get; set; }
		[ObservableProperty] public partial bool EnhAudio { get; set; }
		[ObservableProperty] public partial bool EnhBorder { get; set; }
		[ObservableProperty] public partial bool EnhWidescreen { get; set; }
		[ObservableProperty] public partial bool EnhOverclock { get; set; }
		[ObservableProperty] public partial string EnhOverclockReason { get; private set; } = "";
		[ObservableProperty] public partial string EnhancementsApplyText { get; private set; } = "";

		//W-P9: the HUD pill's sentence; empty when the pill is hidden.
		[ObservableProperty] public partial string PackInstallPillText { get; private set; } = "";
		[ObservableProperty] public partial bool IsPackInstallPillInstalling { get; private set; }

		//ADR-0244 / P.9: whether a pack layer change keeps the player's place
		//(the in-place swap of LoadRomHelper.ApplyPackChange), so W-P7 reads
		//Apply rather than Apply & Reload.
		public bool LayerChangeKeepsPlace => RomInfo.Format != RomFormat.Unknown
			&& LoadRomHelper.PlanPackChange(RomInfo.ConsoleType).Route == PackChangeRoute.InPlace;

		private RestoreStep _restoreStep = RestoreStep.Idle;
		private EnhancementsState _enhancementsApplied = new(false, false, false, false, false);
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
				PlayerPackPicker.DistinctPackIdCount(resolution.Candidates), hasSibling, !string.IsNullOrWhiteSpace(installedSourceSha256), folder, resolution.PrefersNoPack);

			PackDetailHasPack = model.HasPack;
			PackDetailTitle = current?.Name ?? ResourceHelper.GetMessage("PackDetailNoPackTitle");
			//W-P5's "No pack" is a choice, not a missing pack: say so.
			PackDetailByline = current != null ? BuildPackByline(current)
				: ResourceHelper.GetMessage(resolution.PrefersNoPack ? "PackDetailNoPackChosenBody" : "PackDetailNoPackBody");
			PackDetailTextures = model.Chips.Textures;
			PackDetailAudio = model.Chips.Audio;
			PackDetailPatch = model.Chips.Patch;
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

		//"by Tastic · version 1.2 · CC BY-NC 4.0"
		private static string BuildPackByline(PlayerPackChoice pack)
		{
			List<string> parts = new() {
				string.IsNullOrWhiteSpace(pack.Author) ? ResourceHelper.GetMessage("PackAuthorUnknown") : ResourceHelper.GetMessage("PackByAuthor", pack.Author)
			};
			if(!string.IsNullOrWhiteSpace(pack.Version)) {
				parts.Add(ResourceHelper.GetMessage("PackDetailVersion", pack.Version));
			}
			if(!string.IsNullOrWhiteSpace(pack.License)) {
				parts.Add(pack.License);
			}
			return string.Join(" · ", parts);
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

		//W-P7: the switches start from what is applied.
		private void LoadEnhancementsDraft()
		{
			_enhancementsApplied = new EnhancementsState(IsTexturesEnabled, IsAudioEnabled, IsBorderEnabled, IsWideScrnEnabled, IsOverclockEnabled);
			EnhTextures = IsTexturesEnabled;
			EnhAudio = IsAudioEnabled;
			EnhBorder = IsBorderEnabled;
			EnhWidescreen = IsWideScrnEnabled;
			EnhOverclock = IsOverclockEnabled;
			EnhOverclockReason = IsOverclockSupported ? "" : ResourceHelper.GetMessage("EnhancementsOverclockUnavailable", ResourceHelper.GetEnumText(RomInfo.ConsoleType));
			UpdateEnhancementsApplyText();
		}

		private EnhancementsState EnhancementsDraft => new(EnhTextures, EnhAudio, EnhBorder, EnhWidescreen, EnhOverclock && IsOverclockSupported);

		partial void OnEnhTexturesChanged(bool value) => UpdateEnhancementsApplyText();
		partial void OnEnhAudioChanged(bool value) => UpdateEnhancementsApplyText();
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
		//it: ToggleWideScrn (renderer only), ToggleLayer (the pack reload, which
		//P.9 turns into LoadRomHelper.ApplyPackChange) and ToggleOverclock (the
		//power cycle). Several layer changes cost one reload, not one each: the
		//others are written first and the last goes through ToggleLayer. With
		//Overclock pending, its power cycle reloads the pack too.
		public void ApplyEnhancements()
		{
			EnhancementsState applied = _enhancementsApplied;
			EnhancementsState draft = EnhancementsDraft;
			EnhancementsApplyKind kind = EnhancementsSheet.Pending(applied, draft, LayerChangeKeepsPlace);

			if(applied.Widescreen != draft.Widescreen) {
				ToggleWideScrn();
			}

			List<(Action<bool> Set, bool Value, Action Toggle)> layers = new();
			if(applied.Textures != draft.Textures) {
				layers.Add((v => Config.EnhancementPacks.EnableTextures = v, draft.Textures, ToggleTextures));
			}
			if(applied.Audio != draft.Audio) {
				layers.Add((v => Config.EnhancementPacks.EnableAudio = v, draft.Audio, ToggleAudio));
			}
			if(applied.Border != draft.Border) {
				layers.Add((v => Config.EnhancementPacks.EnableBorder = v, draft.Border, ToggleBorder));
			}

			if(applied.Overclock != draft.Overclock) {
				foreach((Action<bool> set, bool value, _) in layers) {
					set(value);
				}
				if(layers.Count > 0) {
					Config.EnhancementPacks.ApplyConfig();
				}
				ToggleOverclock();
			} else if(layers.Count > 0) {
				for(int i = 0; i < layers.Count - 1; i++) {
					layers[i].Set(layers[i].Value);
				}
				layers[^1].Toggle();
			}

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

		//The native renderer draws over Avalonia content, so over a running game
		//the pill is the core's HUD message (the same channel as the W-P3 toast).
		//A HUD message lasts PackInstallPillHud.RepostMilliseconds; it is posted
		//again while the install runs.
		private void PostPillToHud()
		{
			if(EmuApi.IsRunning()) {
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
